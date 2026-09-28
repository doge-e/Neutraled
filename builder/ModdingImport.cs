using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>
/// 传统 mod 包导入器（Modding.xml / Deltamod / DeltaPatcher 生态）
/// —— 把"必须改原版文件"的传统 mod 自动转换成 Neutraled 的 mod 格式，
/// 从而做到不碰原版、多 mod 平等共存。
///
/// 支持的包形态（自动识别）：
///   1) modding.xml 包（权威映射，含"多个根元素"的野格式）
///   2) Deltamod 包：meta.toml / meta.json + patches|files
///   3) mod_config.json 包（chapter_N/xxx.xdelta 映射）
///   4) 裸 xdelta：文件名或父目录带章节提示，或**自动逐章探测**
///   5) 裸 data.win：按文件大小与各官方基线比对猜章节
///   6) 原生 Neutraled / Kristal mod（含 mod.json）→ 转交 ModInstall
///
/// 产出统一格式（与 Mods.ScanMods / Injector / Program 的基底选择约定一致）：
///   mods/NAME/AUTHOR/chapterN|root/mod.json
///   mods/NAME/AUTHOR/chapterN|root/ref/data.win   ← references.source（整体替换 data.win）
///   mods/NAME/AUTHOR/chapterN|root/files/**       ← override 资源
///
/// 版本预检：meta.toml / meta.json 的 neededFiles 校验和会与游戏目录里的官方备份（backup/）比对，
/// 不一致时明确报"补丁基于其它游戏版本"，而不是让 xdelta 抛一句看不懂的错。
/// </summary>
public static class ModdingImport
{
    /// <summary>通用解压（zip 之外用 SharpCompress：7z / rar / tar / gz …）。返回写出的文件数。</summary>
    internal static int ExtractAny(string archivePath, string destDir)
    {
        int n = 0;
        using var archive = SharpCompress.Archives.ArchiveFactory.OpenArchive(archivePath);
        foreach (var entry in archive.Entries)
        {
            if (entry.IsDirectory) continue;
            var key = (entry.Key ?? "").Replace('\\', '/').TrimStart('/');
            if (key.Length == 0) continue;
            var dest = Path.Combine(destDir, key.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            using var es = entry.OpenEntryStream();
            using var fso = File.Create(dest);
            es.CopyTo(fso);
            n++;
        }
        return n;
    }

    private sealed class Meta
    {
        public string Name = "";
        public string Version = "1.0.0";
        public string Author = "unknown";
        public string Description = "";
        public string PackageId = "";
        public string TargetVersion = "";
        public readonly List<(string file, string sha)> Needed = new();
    }

    private sealed class ChapterPlan
    {
        public string Chapter = "";
        public bool Unsure;                 // 章节未知，需要探测
        public string? Xdelta;
        public string? CopyFrom;            // 裸 data.win（整包替换）
        public string? ExpectedSha;
        public string ExpectedFile = "";
        public string? Baseline;
        public bool DataOk;
        public string? Error;
        public readonly List<(string from, string rel)> Overrides = new();
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly string[] AllChapters = { "root", "chapter1", "chapter2", "chapter3", "chapter4", "chapter5" };
    private static readonly Dictionary<string, string> ShaCache = new(StringComparer.OrdinalIgnoreCase);

    // ------------------------------------------------------------------ 入口

    /// <summary>判断是不是"成品程序"包：有 .exe + love/SDL 运行库，但没有 mod.json / mod.lua / modding.xml / data.win。</summary>
    internal static bool LooksLikeStandaloneBuild(string dir, out string exePath)
    {
        exePath = "";
        try
        {
            if (Directory.GetFiles(dir, "mod.json", SearchOption.AllDirectories).Length > 0) return false;
            if (Directory.GetFiles(dir, "mod.lua", SearchOption.AllDirectories).Length > 0) return false;
            if (Directory.GetFiles(dir, "modding.xml", SearchOption.AllDirectories).Length > 0) return false;
            if (Directory.GetFiles(dir, "data.win", SearchOption.AllDirectories).Length > 0) return false;
            var exes = Directory.GetFiles(dir, "*.exe", SearchOption.AllDirectories);
            if (exes.Length == 0) return false;
            // 判定运行库：LÖVE 或 GameMaker / SDL 系的伴生 DLL
            bool hasRuntime = new[] { "love.dll", "SDL2.dll", "OpenAL32.dll", "lua51.dll", "msvcp120.dll" }
                .Any(d => Directory.GetFiles(dir, d, SearchOption.AllDirectories).Length > 0);
            if (!hasRuntime) return false;
            // 优先 *_final.exe，其次体积最大的
            exePath = exes.OrderByDescending(f => f.EndsWith("_final.exe", StringComparison.OrdinalIgnoreCase))
                          .ThenByDescending(f => new FileInfo(f).Length)
                          .First();
            return true;
        }
        catch { return false; }
    }

    /// <summary>把成品程序注册成外部引擎章节：在 mods/&lt;slug&gt;/external/&lt;名字&gt;/ 下写 mod.json。</summary>
    internal static int RegisterExternalMod(string gameRoot, string pkgDir, string exePath, string? modName, string? author)
    {
        var exe = Path.GetFullPath(exePath);
        var disp = string.IsNullOrWhiteSpace(modName) ? Path.GetFileNameWithoutExtension(exe) : modName!;
        // slug 必须是 ASCII（路径要跨工具/编码安全）；显示名仍然保留中文
        var slug = new string(disp.Where(ch => (ch < 128) && (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-')).ToArray()).ToLowerInvariant();
        if (slug.Length == 0) slug = "external" + Math.Abs(exe.GetHashCode()).ToString("x8");
        // ⚠ 目录名必须是 chapter* 形式：Mods.ScanMods 只把 IsChapterFolder(名字以 chapter 开头且长度>7) 的目录当 mod，
        //   否则整个 mod 会被静默跳过（踩过：目录叫 Frostveil → 章节选择器里怎么都不出现）。
        var chapName = "chapter1";
        var root = Path.Combine(Paths.NeutraledRoot(gameRoot), "mods", slug, "external", chapName);
        Directory.CreateDirectory(root);
        var mod = new Dictionary<string, object?>
        {
            ["id"] = "external." + slug,
            ["name"] = disp,
            ["author"] = string.IsNullOrWhiteSpace(author) ? "external" : author,
            ["version"] = "1.0.0",
            ["enabled"] = true,
            // 章节显示名用中文（不含空格，避免声明语法被空格拆开）；外部章节不依赖目录名匹配
            ["chapters"] = new[] { "~Chapter:1:" + disp },
            ["dependencies"] = Array.Empty<string>(),
            ["api"] = new Dictionary<string, object?> { ["ns"] = slug, ["functions"] = Array.Empty<string>(), ["constants"] = Array.Empty<string>() },
            ["kristal_external"] = new Dictionary<string, object?>
            {
                ["exe"] = exe,
                ["args"] = "",
                ["cwd"] = Path.GetDirectoryName(exe) ?? pkgDir,
                ["project"] = "",
                ["mod_name"] = slug
            }
        };
        File.WriteAllText(Path.Combine(root, "mod.json"),
            System.Text.Json.JsonSerializer.Serialize(mod, Paths.Json), new System.Text.UTF8Encoding(false));
        Console.WriteLine();
        Console.WriteLine(L("✓ 转换成功（外部引擎章节）"));
        Console.WriteLine(L("  mod 目录 : ") + root);
        Console.WriteLine(L("  可执行   : ") + exe);
        Console.WriteLine(L("  章节名   : ") + disp);
        Console.WriteLine(L("  下一步   : ntl-builder.exe --deploy --chapter root   （之后在章节选择器里就能看到它）"));
        Console.WriteLine(L("  注意     : 玩它需要守候进程（--watch-external）；已随安装器/开机自启一起提供"));
        return 0;
    }

    /// <summary>带插件钩子的导入入口：before_import 非 0 直接中止，结束后触发 after_import。</summary>
    public static int Import(string gameRoot, string input, string? modName, string? author,
        bool dryRun = false, bool force = false, string? forceChapter = null)
    {
        Program.PluginBoot(gameRoot);
        var veto = Program.PluginFire(PluginHooks.BeforeImport, new { input, modName, chapter = forceChapter });
        if (veto != 0) return veto;
        var rc = ImportCore(gameRoot, input, modName, author, dryRun, force, forceChapter);
        Program.PluginFire(PluginHooks.AfterImport, new { input, rc });
        return rc;
    }

    private static int ImportCore(string gameRoot, string input, string? modName, string? author,
        bool dryRun = false, bool force = false, string? forceChapter = null)
    {
        Console.WriteLine(L("===== 传统 mod 导入（转 ntl 格式） ====="));
        Console.WriteLine(L("  包: ") + input);
        if (dryRun) Console.WriteLine(L("  模式: dry-run（只分析，不写盘）"));

        string? temp = null;
        string pkgDir;

        if (Directory.Exists(input)) pkgDir = Path.GetFullPath(input);
        else if (File.Exists(input))
        {
            var ext = Path.GetExtension(input).ToLowerInvariant();
            if (ext == ".zip" || ext == ".ntlmod")
            {
                temp = Path.Combine(TempDir(gameRoot), "modimp-" + Guid.NewGuid().ToString("N")[..8]);
                Directory.CreateDirectory(temp);
                try { ZipFile.ExtractToDirectory(input, temp, true); }
                catch (Exception ex) { Console.WriteLine(L("[错误] 解压失败: ") + ex.Message); return 1; }
                pkgDir = FindPackageRoot(temp);
            }
            // 7z / rar 等：GameBanana 与国内补丁包很常用（实测两个包都是 .7z）
            else if (ext is ".7z" or ".rar" or ".tar" or ".gz" or ".tgz" or ".bz2")
            {
                temp = Path.Combine(TempDir(gameRoot), "modimp-" + Guid.NewGuid().ToString("N")[..8]);
                Directory.CreateDirectory(temp);
                try
                {
                    int n = ExtractAny(input, temp);
                    Console.WriteLine(L("  已解压 {0} 个文件（{1}）", n, ext));
                }
                catch (Exception ex) { Console.WriteLine(L("[错误] 解压失败: ") + ex.Message); return 1; }
                pkgDir = FindPackageRoot(temp);
            }
            else if (ext == ".xdelta" || ext == ".win")
            {
                Console.WriteLine(L("  → 单项补丁：") + (forceChapter ?? L("自动探测章节")));
                return ImportSingle(gameRoot, input, forceChapter, modName, author, dryRun);
            }
            else { Console.WriteLine(L("[错误] 不支持的包类型: ") + ext); return 1; }
        }
        else { Console.WriteLine(L("[错误] 找不到包: ") + input); return 1; }

        try
        {
            // ★ 成品程序（fused LÖVE / 打包好的游戏）：**不能**当 mod 转换，但用户想"转换"它 →
            //   照样在 mods/ 里建条目（外部引擎章节：选中后退出游戏并拉起它），并报告转换成功。
            if (LooksLikeStandaloneBuild(pkgDir, out var fusedExe))
            {
                Console.WriteLine(L("  → 检测到成品程序（自带 exe + love/SDL 运行库，没有工程源码）"));
                Console.WriteLine(L("     这类包无法作为普通 mod 转换，将注册为**外部引擎章节**（照样出现在 mods/ 与章节选择器里）"));
                if (dryRun) { Console.WriteLine(L("  [dry-run] 不写盘（去掉 --dry-run 即注册）")); return 0; }
                return RegisterExternalMod(gameRoot, pkgDir, fusedExe, modName, author);
            }

            // Kristal 生态（项目 / 插件型 mod）→ 交给 Kristal 转换器
            if (LooksLikeKristal(pkgDir))
            {
                Console.WriteLine(L("  → 检测到 Kristal 结构（mod.lua / engineVer / scripts+assets）：转交 --import-kristal"));
                if (dryRun) { Console.WriteLine(L("  [dry-run] 不写盘（去掉 --dry-run 即执行 Kristal 转换）")); return 0; }
                return KristalImport.Import(gameRoot, pkgDir, modName, author, true);
            }

            // 原生 Neutraled mod（含 mod.json）→ 交给安装器
            if (Directory.GetFiles(pkgDir, "mod.json", SearchOption.AllDirectories).Length > 0)
            {
                Console.WriteLine(L("  → 检测到 mod.json：原生 Neutraled mod，转交 --mod-install"));
                if (dryRun) { Console.WriteLine(L("  [dry-run] 不写盘（去掉 --dry-run 即执行安装）")); return 0; }
                return ModInstall.Install(gameRoot, pkgDir, force);
            }

            var meta = LoadMeta(pkgDir);

            var plans = new Dictionary<string, ChapterPlan>(StringComparer.OrdinalIgnoreCase);
            string format;
            var xmlPath = Path.Combine(pkgDir, "modding.xml");
            var cfgPath = Path.Combine(pkgDir, "mod_config.json");

            if (File.Exists(xmlPath)) { format = "modding.xml"; ReadModdingXml(pkgDir, xmlPath, plans); }
            else if (File.Exists(cfgPath)) { format = "mod_config.json"; ReadModConfigJson(pkgDir, cfgPath, plans); }
            else { format = L("扫描推断"); ReadHeuristic(gameRoot, pkgDir, plans); }

            // 纯声明（无内容）时再退回扫描
            if (plans.Count == 0 || plans.Values.All(p => p.Xdelta == null && p.CopyFrom == null && p.Overrides.Count == 0))
            {
                plans.Clear();
                format = L("扫描推断");
                ReadHeuristic(gameRoot, pkgDir, plans);
            }

            if (plans.Count == 0)
            {
                DiagnoseUnsupported(pkgDir);
                return 1;
            }

            MatchNeeded(plans, meta);

            // --chapter 只在"章节确实未知"时生效
            var unsure = plans.Values.Where(p => p.Unsure).ToList();
            if (forceChapter != null && unsure.Count == 1 && plans.Count == 1)
            {
                Console.WriteLine(L("  章节由 --chapter 指定: ") + forceChapter);
                unsure[0].Unsure = false;
                unsure[0].Chapter = forceChapter;
            }

            var name = FirstNonEmpty(modName, meta.Name, new DirectoryInfo(pkgDir).Name);
            var authorFinal = FirstNonEmpty(author, meta.Author, "unknown");
            var id = "converted." + DeltaImport.Sanitize(name) + "." + DeltaImport.Sanitize(authorFinal);

            Console.WriteLine(L("  格式: ") + format);
            Console.WriteLine(L("  名称: {0}  作者: {1}  版本: {2}  id: {3}", name, authorFinal, meta.Version, id));
            if (meta.TargetVersion.Length > 0) Console.WriteLine(L("  目标游戏版本: ") + meta.TargetVersion);
            if (meta.Description.Length > 0) Console.WriteLine(L("  描述: ") + Truncate(meta.Description, 100));
            PrintPlan(plans);

            var ntlRoot = Paths.NeutraledRoot(gameRoot);
            var modRoot = Path.Combine(ntlRoot, "mods", DeltaImport.Sanitize(name), DeltaImport.Sanitize(authorFinal));
            var xdelta = DeltaImport.FindXdelta(gameRoot);
            if (plans.Values.Any(p => p.Xdelta != null))
            {
                if (xdelta == null) Console.WriteLine(L("  [警告] 未找到 xdelta3.exe（应在 <游戏根>/xdelta/xdelta3.exe）"));
                else Console.WriteLine("  xdelta3: " + xdelta);
            }

            int ok = 0, fail = 0, overridesTotal = 0;

            foreach (var plan in Ordered(plans).ToList())
            {
                Console.WriteLine("--- " + (plan.Unsure ? L("自动探测章节") : plan.Chapter) + " ---");

                // 1) 章节未知 → 逐章探测
                if (plan.Unsure)
                {
                    if (plan.Xdelta == null)
                    {
                        Console.WriteLine(L("  [错误] 裸 data.win 无法自动判定章节 —— 请用 --chapter chapterN 指定"));
                        plan.Error = "chapter-unknown"; fail++; continue;
                    }
                    if (xdelta == null) { plan.Error = "no-xdelta3"; fail++; continue; }
                    var probeDir = Path.Combine(TempDir(gameRoot), "probe-" + Guid.NewGuid().ToString("N")[..8]);
                    Directory.CreateDirectory(probeDir);
                    var probeOut = Path.Combine(probeDir, "data.win");
                    string? hit = null;
                    foreach (var ch in AllChapters)
                    {
                        var b = Paths.BackupDataWin(gameRoot, ch);
                        if (!File.Exists(b)) continue;
                        if (ApplyXdelta(xdelta, b, plan.Xdelta, probeOut, quiet: true)) { hit = ch; plan.Baseline = b; break; }
                    }
                    try { Directory.Delete(probeDir, true); } catch { }
                    if (hit == null)
                    {
                        Console.WriteLine(L("  [错误] 补丁与本机所有官方基线都不匹配（可能基于其它游戏版本）"));
                        plan.Error = "no-baseline-match"; fail++; continue;
                    }
                    plan.Chapter = hit;
                    plan.Unsure = false;
                    Console.WriteLine(L("  自动识别章节: {0}（{1}）", hit, Path.GetFileName(plan.Xdelta)));
                }

                var modDir = Path.Combine(modRoot, plan.Chapter);

                // 2) data.win：xdelta 优先，其次裸文件复制
                if (plan.Xdelta != null)
                {
                    if (xdelta == null) plan.Error = "no-xdelta3";
                    else if (plan.Baseline == null && !Preflight(gameRoot, plan, force)) plan.Error = "baseline-mismatch";
                    else if (dryRun) { Console.WriteLine(L("  [dry-run] {0} → ref/data.win（基线 {1}）", Path.GetFileName(plan.Xdelta), Short(plan.Baseline!))); plan.DataOk = true; }
                    else
                    {
                        Directory.CreateDirectory(Path.Combine(modDir, "ref"));
                        var outWin = Path.Combine(modDir, "ref", "data.win");
                        if (ApplyXdelta(xdelta, plan.Baseline!, plan.Xdelta, outWin)) { plan.DataOk = true; ReportProduct(outWin); }
                        else plan.Error = "xdelta-failed";
                    }
                }
                else if (plan.CopyFrom != null)
                {
                    if (dryRun) { Console.WriteLine(L("  [dry-run] 整包 data.win 复制 → ref/data.win（{0} MB）", new FileInfo(plan.CopyFrom).Length / 1024 / 1024)); plan.DataOk = true; }
                    else
                    {
                        Directory.CreateDirectory(Path.Combine(modDir, "ref"));
                        File.Copy(plan.CopyFrom, Path.Combine(modDir, "ref", "data.win"), true);
                        plan.DataOk = true;
                        ReportProduct(Path.Combine(modDir, "ref", "data.win"));
                    }
                }

                // 3) override 资源
                foreach (var (from, rel) in plan.Overrides)
                {
                    if (dryRun) { overridesTotal++; continue; }
                    try
                    {
                        var dst = Path.Combine(modDir, "files", rel.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                        File.Copy(from, dst, true);
                        overridesTotal++;
                    }
                    catch (Exception ex) { Console.WriteLine(L("  [警告] 覆盖失败 {0}: {1}", rel, ex.Message)); }
                }
                if (!dryRun && plan.Overrides.Count > 0) Console.WriteLine(L("  覆盖文件: {0}", plan.Overrides.Count));

                if (plan.Error == null) ok++; else fail++;

                if (!dryRun && plan.Error == null && (plan.DataOk || plan.Overrides.Count > 0))
                    WriteModJson(modDir, id, name, authorFinal, meta, plan);
            }

            Console.WriteLine();
            Console.WriteLine(L("===== 导入结果 ====="));
            Console.WriteLine(L("  格式 {0} / 章节 {1} / 成功 {2} / 失败 {3} / 覆盖文件 {4}", format, plans.Count, ok, fail, overridesTotal));
            foreach (var p in Ordered(plans))
                Console.WriteLine($"    {p.Chapter,-10} data.win={(p.DataOk ? "✓" : "-")}  override={p.Overrides.Count,-4} {(p.Error != null ? "✗ " + p.Error : "")}");

            if (!dryRun)
            {
                Console.WriteLine(L("  mod 目录: ") + modRoot);
                WriteRecord(modRoot, input, format, meta, plans, id, name, authorFinal, overridesTotal);
                Console.WriteLine(L("  下一步:"));
                Console.WriteLine("    ntl-builder.exe --mod-list");
                Console.WriteLine("    ntl-builder.exe --deploy --chapter " + Ordered(plans).First().Chapter);
            }
            return (fail == 0 && ok > 0) ? 0 : 1;
        }
        finally
        {
            if (temp != null) { try { Directory.Delete(temp, true); } catch { } }
        }
    }

    /// <summary>单个 xdelta / data.win 文件（无包结构）。</summary>
    private static int ImportSingle(string gameRoot, string file, string? forceChapter, string? modName, string? author, bool dryRun)
    {
        // 复用包逻辑：造一个临时"包目录"，把文件放进去
        var tmp = Path.Combine(TempDir(gameRoot), "single-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmp);
        try
        {
            var dst = Path.Combine(tmp, Path.GetFileName(file));
            File.Copy(file, dst, true);
            return Import(gameRoot, tmp, modName ?? Path.GetFileNameWithoutExtension(file), author, dryRun, false, forceChapter);
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    // ------------------------------------------------------------ 包形态识别

    private static string FindPackageRoot(string dir)
    {
        if (HasSignature(dir)) return dir;
        foreach (var sub in Directory.GetDirectories(dir))
            if (HasSignature(sub)) return sub;
        var subs = Directory.GetDirectories(dir);
        if (subs.Length == 1 && Directory.GetFiles(dir).Length == 0) return FindPackageRoot(subs[0]);
        return dir;
    }

    /// <summary>是否 Kristal 生态（≠ Neutraled mod）：
    /// ① 有 mod.lua；② mod.json 里带 Kristal 项目字段 engineVer / chapter+map；③ 只有 scripts/+assets/ 的插件型 mod。
    /// 判错方向的代价不对称：把 Kristal 项目当 Neutraled mod 安装 = 装了个永远不会生效的壳，必须避免。</summary>
    private static bool LooksLikeKristal(string dir)
    {
        if (Directory.GetFiles(dir, "mod.lua", SearchOption.AllDirectories).Length > 0) return true;

        var mj = Path.Combine(dir, "mod.json");
        if (File.Exists(mj))
        {
            try
            {
                var t = File.ReadAllText(mj);
                if (t.Contains("\"engineVer\"")) return true;
                if (t.Contains("\"chapter\"") && t.Contains("\"map\"")) return true;
            }
            catch { }
        }
        return Directory.Exists(Path.Combine(dir, "scripts")) && Directory.Exists(Path.Combine(dir, "assets"));
    }

    private static bool HasSignature(string dir) =>
        File.Exists(Path.Combine(dir, "modding.xml")) ||
        File.Exists(Path.Combine(dir, "meta.toml")) ||
        File.Exists(Path.Combine(dir, "meta.json")) ||
        File.Exists(Path.Combine(dir, "mod_config.json")) ||
        File.Exists(Path.Combine(dir, "mod.json")) ||
        Directory.Exists(Path.Combine(dir, "patches")) ||
        Directory.Exists(Path.Combine(dir, "files")) ||
        Directory.GetFiles(dir, "*.xdelta", SearchOption.AllDirectories).Length > 0;

    private static (string chapter, string rel) SplitTarget(string to)
    {
        var t = NormalizeRel(to);
        if (t.Length == 0) return ("", "");
        if (t.Equals("data.win", StringComparison.OrdinalIgnoreCase)) return ("root", "data.win");
        var m = Regex.Match(t, @"^(?<ch>chapter\d+)_(windows|linux|unix|macos)/(?<rel>.+)$", RegexOptions.IgnoreCase);
        if (m.Success) return (m.Groups["ch"].Value.ToLowerInvariant(), m.Groups["rel"].Value);
        if (t.StartsWith("Neutraled/", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("backup/", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("UTMT/", StringComparison.OrdinalIgnoreCase)) return ("", "");
        return ("root", t);
    }

    private static string NormalizeRel(string p)
    {
        var t = p.Replace('\\', '/').Trim();
        while (t.StartsWith("./")) t = t[2..];
        return t.TrimStart('/');
    }

    // --------------------------------------------------------- modding.xml 读取

    /// <summary>真实世界的 modding.xml 常常**有多个根元素**（一堆 patch 平铺，无外层包裹），
    /// XDocument.Load 会直接报 "multiple root elements" → 先去掉 XML 声明，失败再套一层合成根。</summary>
    private static XDocument LoadLooseXml(string path)
    {
        var text = File.ReadAllText(path);
        text = Regex.Replace(text, @"^\s*<\?xml[^>]*\?>", "");
        try { return XDocument.Parse(text); }
        catch { return XDocument.Parse("<ntl-root>" + text + "</ntl-root>"); }
    }

    private static void ReadModdingXml(string pkgDir, string xmlPath, Dictionary<string, ChapterPlan> plans)
    {
        XDocument doc;
        try { doc = LoadLooseXml(xmlPath); }
        catch (Exception ex) { Console.WriteLine(L("[警告] modding.xml 解析失败: ") + ex.Message); return; }

        int skipped = 0;
        foreach (var el in doc.Descendants("patch"))
        {
            var type = ((string?)el.Attribute("type") ?? "").Trim().ToLowerInvariant();
            var from = NormalizeRel((string?)el.Attribute("patch") ?? "");
            var to = (string?)el.Attribute("to") ?? "";
            if (from.Length == 0 || to.Length == 0) continue;

            var (chapter, rel) = SplitTarget(to);
            if (chapter.Length == 0) { skipped++; continue; }

            var src = Path.GetFullPath(Path.Combine(pkgDir, from.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(src)) { Console.WriteLine(L("  [警告] 包内缺少文件: ") + from); skipped++; continue; }

            var plan = Plan(plans, chapter);
            if (type == "xdelta")
            {
                if (rel.Equals("data.win", StringComparison.OrdinalIgnoreCase)) plan.Xdelta ??= src;
                else { Console.WriteLine(L("  [警告] xdelta 目标不是 data.win: ") + to); skipped++; }
            }
            else if (type == "override" || type == "copy" || type == "add")
            {
                plan.Overrides.Add((src, rel));
            }
            else { Console.WriteLine(L("  [警告] 未知 patch 类型: {0} → {1}", type, to)); skipped++; }
        }
        if (skipped > 0) Console.WriteLine(L("  （{0} 条未处理）", skipped));
    }

    // ---------------------------------------------------- mod_config.json 读取

    private static void ReadModConfigJson(string pkgDir, string cfgPath, Dictionary<string, ChapterPlan> plans)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(cfgPath),
                new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (!doc.RootElement.TryGetProperty("files", out var files)) return;
            foreach (var prop in files.EnumerateObject())
            {
                string? rel = null;
                foreach (var k in new[] { "data_file_path", "path", "file" })
                    if (prop.Value.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String) { rel = v.GetString(); break; }
                if (string.IsNullOrEmpty(rel)) continue;

                var ch = InferChapter(prop.Name) ?? InferChapterFromDir(Path.GetDirectoryName(rel)!, pkgDir) ?? InferChapter(Path.GetFileNameWithoutExtension(rel));
                var src = Path.GetFullPath(Path.Combine(pkgDir, rel.Replace('/', Path.DirectorySeparatorChar)));
                if (!File.Exists(src)) { Console.WriteLine(L("  [警告] 包内缺少文件: ") + rel); continue; }
                if (ch == null) { PlanUnsure(plans, src); continue; }
                Plan(plans, ch).Xdelta ??= src;
            }
        }
        catch (Exception ex) { Console.WriteLine(L("[警告] mod_config.json 解析失败: ") + ex.Message); }
    }

    // ------------------------------------------------------------ 启发式（无清单）

    private static void ReadHeuristic(string gameRoot, string pkgDir, Dictionary<string, ChapterPlan> plans)
    {
        // 1) xdelta：文件名 → 父目录 → 未知（探测）
        foreach (var f in Directory.GetFiles(pkgDir, "*.xdelta", SearchOption.AllDirectories))
        {
            var ch = InferChapter(Path.GetFileNameWithoutExtension(f))
                     ?? InferChapterFromDir(Path.GetDirectoryName(f)!, pkgDir);
            if (ch == null) { PlanUnsure(plans, f); continue; }
            Plan(plans, ch).Xdelta ??= f;
        }

        // 2) 裸 data.win（整包替换）
        foreach (var f in Directory.GetFiles(pkgDir, "data.win", SearchOption.AllDirectories))
        {
            var ch = InferChapterFromDir(Path.GetDirectoryName(f)!, pkgDir);
            if (ch != null)
            {
                var p0 = Plan(plans, ch);
                if (p0.Xdelta == null) p0.CopyFrom ??= f;
                continue;
            }
            var guess = GuessChapterBySize(gameRoot, f);
            if (guess != null)
            {
                var p1 = Plan(plans, guess);
                if (p1.Xdelta == null) p1.CopyFrom ??= f;
            }
            else
            {
                var p2 = PlanUnsure(plans, null);
                p2.CopyFrom ??= f;
            }
        }

        // 3) files/** 覆盖（无 modding.xml 时，按包内 xdelta 覆盖的章节各放一份）
        var filesDir = Path.Combine(pkgDir, "files");
        if (Directory.Exists(filesDir))
        {
            var targets = plans.Values.Where(p => !p.Unsure).Select(p => p.Chapter).Distinct().ToList();
            if (targets.Count == 0) targets.Add("root");
            foreach (var ch in targets)
                foreach (var f in Directory.GetFiles(filesDir, "*", SearchOption.AllDirectories))
                    Plan(plans, ch).Overrides.Add((f, Path.GetRelativePath(filesDir, f).Replace('\\', '/')));
        }
    }

    /// <summary>裸 data.win 没有清单时，用"与官方各章基线的体积接近度"猜章节（并打印全部候选）。</summary>
    private static string? GuessChapterBySize(string gameRoot, string dataWin)
    {
        long size;
        try { size = new FileInfo(dataWin).Length; } catch { return null; }
        if (size < 1024 * 1024) return null;

        var scored = new List<(string ch, double diff, long len)>();
        foreach (var ch in AllChapters)
        {
            var b = Paths.BackupDataWin(gameRoot, ch);
            if (!File.Exists(b)) continue;
            var len = new FileInfo(b).Length;
            if (len == 0) continue;
            scored.Add((ch, Math.Abs((double)size / len - 1.0), len));
        }
        if (scored.Count == 0) return null;
        scored.Sort((a, b) => a.diff.CompareTo(b.diff));

        Console.WriteLine(L("  裸 data.win（{0} MB）章节推断:", size / 1024 / 1024));
        foreach (var (ch, diff, len) in scored)
            Console.WriteLine(L("    {0,-10} 基线 {1,4} MB  差异 {2,6:F2}%", ch, len / 1024 / 1024, diff * 100));
        var best = scored[0];
        if (best.diff > 0.05) { Console.WriteLine(L("    → 差异过大，无法确定章节（请用 --chapter 指定）")); return null; }
        Console.WriteLine(L("    → 判定: {0}", best.ch));
        return best.ch;
    }

    private static string? InferChapter(string baseName)
    {
        var n = baseName.Trim().ToLowerInvariant();
        if (n.Length == 0) return null;
        if (n == "root" || n == "main" || n == "launcher") return "root";
        var m = Regex.Match(n, @"^(?:chapter|ch)[_\-]?0?(\d+)$");
        if (m.Success) return "chapter" + m.Groups[1].Value;
        m = Regex.Match(n, @"(?:^|[_\-\s])(?:chapter|ch)[_\-]?0?(\d+)(?:[_\-\s.]|$)");
        if (m.Success) return "chapter" + m.Groups[1].Value;
        return null;
    }

    /// <summary>父目录提示：chapter_1 / chapter-1 / ch1 / chapter1_windows / deltarune_4。</summary>
    private static string? InferChapterFromDir(string dir, string pkgRoot)
    {
        var cur = new DirectoryInfo(Path.GetFullPath(dir));
        var root = new DirectoryInfo(Path.GetFullPath(pkgRoot));
        for (int i = 0; i < 6 && cur != null; i++)
        {
            var ch = InferChapter(cur.Name);
            if (ch != null) return ch;
            var m = Regex.Match(cur.Name.ToLowerInvariant(), @"^(?:deltarune|game|data)[_\-]?0?(\d+)$");
            if (m.Success) return "chapter" + m.Groups[1].Value;
            if (cur.FullName.Equals(root.FullName, StringComparison.OrdinalIgnoreCase)) break;
            cur = cur.Parent;
        }
        return null;
    }

    private static ChapterPlan PlanUnsure(Dictionary<string, ChapterPlan> plans, string? xdelta)
    {
        var key = "?" + plans.Count;
        var p = new ChapterPlan { Chapter = "?", Unsure = true, Xdelta = xdelta };
        plans[key] = p;
        return p;
    }

    // ---------------------------------------------------------------- 元数据

    private static Meta LoadMeta(string pkgDir)
    {
        var m = new Meta();
        var toml = Path.Combine(pkgDir, "meta.toml");
        if (File.Exists(toml)) return ParseToml(File.ReadAllText(toml));

        foreach (var jsonName in new[] { "meta.json", "mod_config.json", "modinfo.json" })
        {
            var jp = Path.Combine(pkgDir, jsonName);
            if (!File.Exists(jp)) continue;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(jp),
                    new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                var rootEl = doc.RootElement;
                if (rootEl.TryGetProperty("metadata", out var md)) rootEl = md;
                m.Name = Str(rootEl, "name");
                m.Version = Str(rootEl, "version");
                m.Author = Str(rootEl, "author");
                m.Description = Str(rootEl, "description");
                m.PackageId = Str(rootEl, "packageID") is { Length: > 0 } pid ? pid : Str(rootEl, "id");
                m.TargetVersion = Str(rootEl, "game_version") is { Length: > 0 } gv ? gv : Str(rootEl, "deltaruneTargetVersion");
                if (rootEl.TryGetProperty("neededFiles", out var need) && need.ValueKind == JsonValueKind.Array)
                    foreach (var e in need.EnumerateArray())
                        m.Needed.Add((Str(e, "file"), Str(e, "checksum")));
                break;
            }
            catch (Exception ex) { Console.WriteLine(L("  [警告] {0} 解析失败: {1}", jsonName, ex.Message)); }
        }
        if (string.IsNullOrWhiteSpace(m.Version)) m.Version = "1.0.0";
        return m;
    }

    private static string Str(JsonElement e, string prop) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(prop, out var v)
            ? (v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "")
               : v.ValueKind == JsonValueKind.Array && v.GetArrayLength() > 0 && v[0].ValueKind == JsonValueKind.String ? (v[0].GetString() ?? "")
               : "")
            : "";

    // ---------------------------------------------------------------- 校验和预检

    private static void MatchNeeded(Dictionary<string, ChapterPlan> plans, Meta meta)
    {
        foreach (var (file, sha) in meta.Needed)
        {
            if (file.Length == 0 || sha.Length == 0) continue;
            var (ch, rel) = SplitTarget(file);
            if (ch.Length == 0 || !plans.TryGetValue(ch, out var p)) continue;
            if (!rel.Equals("data.win", StringComparison.OrdinalIgnoreCase)) continue;
            p.ExpectedSha = sha.ToLowerInvariant();
            p.ExpectedFile = file;
        }
    }

    private static bool Preflight(string gameRoot, ChapterPlan plan, bool force)
    {
        var backup = Paths.BackupDataWin(gameRoot, plan.Chapter);
        var current = Paths.ChapterDataWin(gameRoot, plan.Chapter);
        var candidates = new List<string>();
        if (File.Exists(backup)) candidates.Add(backup);
        if (File.Exists(current)) candidates.Add(current);
        if (candidates.Count == 0) { Console.WriteLine(L("  [错误] 缺少基线: ") + backup); return false; }

        string? pick = null;
        if (!string.IsNullOrEmpty(plan.ExpectedSha))
        {
            foreach (var c in candidates)
                if (Sha256(c).Equals(plan.ExpectedSha, StringComparison.OrdinalIgnoreCase)) { pick = c; break; }

            if (pick == null)
            {
                Console.WriteLine(L("  [警告] {0}: 本机与期望的官方版本不一致", plan.Chapter));
                Console.WriteLine(L("         期望 {0} = {1}…", plan.ExpectedFile, plan.ExpectedSha![..16]));
                foreach (var c in candidates) Console.WriteLine(L("         本机 {0} = {1}…", Short(c), Sha256(c)[..16]));
                if (!force)
                {
                    Console.WriteLine(L("         → 跳过该章（补丁基于其它游戏版本）；确认无误可用 --mod-force 强制尝试"));
                    return false;
                }
                Console.WriteLine(L("         → --mod-force：仍按官方备份尝试"));
            }
            else Console.WriteLine(L("  校验和 ✓（{0} 与清单期望一致）", Short(pick)));
        }

        plan.Baseline = pick ?? (File.Exists(backup) ? backup : current);
        Console.WriteLine(L("  基线: ") + Short(plan.Baseline));
        return true;
    }

    private static string Short(string p)
    {
        var dir = Path.GetFileName(Path.GetDirectoryName(p)!) ?? "";
        return dir.Length > 0 ? dir + "/" + Path.GetFileName(p) : p;
    }

    // ------------------------------------------------------------------ 执行

    private static bool ApplyXdelta(string xdeltaExe, string baseline, string patch, string outWin, bool quiet = false)
    {
        if (!quiet) Console.WriteLine(L("  应用 xdelta: ") + Path.GetFileName(patch));
        try
        {
            if (File.Exists(outWin)) { try { File.Delete(outWin); } catch { } }
            var psi = new ProcessStartInfo(xdeltaExe)
            {
                Arguments = $"-d -f -s \"{baseline}\" \"{patch}\" \"{outWin}\"",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi)!;
            var err = p.StandardError.ReadToEnd();
            var outp = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0 || !File.Exists(outWin))
            {
                if (!quiet) Console.WriteLine(L("  [错误] xdelta 失败 (exit={0}): {1}", p.ExitCode, Truncate((err + outp).Trim(), 200)));
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            if (!quiet) Console.WriteLine(L("  [错误] xdelta 调用异常: ") + ex.Message);
            return false;
        }
    }

    private static void ReportProduct(string outWin)
    {
        try
        {
            var data = Injector.Load(outWin);
            var mb = new FileInfo(outWin).Length / 1024 / 1024;
            Console.WriteLine(L("  产物: {0} MB  对象={1} 代码={2} 精灵={3} 声音={4}", mb, data.GameObjects.Count, data.Code.Count, data.Sprites.Count, data.Sounds.Count));
        }
        catch (Exception ex) { Console.WriteLine(L("  [警告] 产物解析失败（不影响导入）: ") + Truncate(ex.Message, 120)); }
    }

    private static void DiagnoseUnsupported(string pkgDir)
    {
        var csx = Directory.GetFiles(pkgDir, "*.csx", SearchOption.AllDirectories);
        var scripts = Directory.GetFiles(pkgDir, "*.cs", SearchOption.AllDirectories)
                               .Concat(Directory.GetFiles(pkgDir, "*.py", SearchOption.AllDirectories)).ToArray();
        Console.WriteLine(L("[错误] 这个包不能自动转换 —— 包里没有可用的 data.win / xdelta 补丁。"));
        if (csx.Length > 0)
        {
            Console.WriteLine(L("  发现 UTMT C# 脚本（.csx）:"));
            foreach (var f in csx.Take(5)) Console.WriteLine("    " + Path.GetFileName(f));
            Console.WriteLine(L("  原因: .csx 必须由 UndertaleModTool 运行 C# 脚本才能产出修改后的 data.win；"));
            Console.WriteLine(L("        Neutraled 导入器只处理成品 data.win / xdelta 补丁（不内嵌 C# 运行时）。"));
            Console.WriteLine(L("  可行路径（2 步，手工一次即可）:"));
            Console.WriteLine(L("    1) 用 UTMT 打开 chapterN_windows/data.win，运行该 .csx，另存为 new.win"));
            Console.WriteLine("    2) ntl-builder.exe --import-mod <new.win> --chapter chapterN");
        }
        else if (scripts.Length > 0)
        {
            Console.WriteLine(L("  发现源码脚本（.cs/.py），但没有编译产物 data.win。"));
        }
        else
        {
            Console.WriteLine(L("  包内文件:"));
            foreach (var f in Directory.GetFiles(pkgDir, "*", SearchOption.TopDirectoryOnly).Take(10))
                Console.WriteLine("    " + Path.GetFileName(f));
        }
    }

    private static void WriteModJson(string modDir, string id, string name, string author, Meta meta, ChapterPlan plan)
    {
        Directory.CreateDirectory(modDir);
        var obj = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["name"] = name,
            ["author"] = author,
            ["version"] = meta.Version,
            ["enabled"] = true,
            ["description"] = meta.Description
        };
        if (plan.DataOk)
            obj["references"] = new Dictionary<string, object?> { ["source"] = "ref/data.win", ["assets"] = "inherit" };

        File.WriteAllText(Path.Combine(modDir, "mod.json"), JsonSerializer.Serialize(obj, JsonOpts));
    }

    private static void WriteRecord(string modRoot, string source, string format, Meta meta,
        Dictionary<string, ChapterPlan> plans, string id, string name, string author, int overrides)
    {
        var chapters = new Dictionary<string, object?>();
        foreach (var kv in plans.OrderBy(k => k.Key))
            chapters[kv.Key] = new Dictionary<string, object?>
            {
                ["dataWin"] = kv.Value.DataOk,
                ["overrides"] = kv.Value.Overrides.Count,
                ["error"] = kv.Value.Error
            };

        var rec = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["name"] = name,
            ["author"] = author,
            ["version"] = meta.Version,
            ["format"] = format,
            ["source"] = source,
            ["packageId"] = meta.PackageId,
            ["targetVersion"] = meta.TargetVersion,
            ["importedAt"] = DateTime.Now.ToString("s"),
            ["overrides"] = overrides,
            ["chapters"] = chapters
        };
        Directory.CreateDirectory(modRoot);
        File.WriteAllText(Path.Combine(modRoot, ".ntl-import.json"), JsonSerializer.Serialize(rec, JsonOpts));
    }

    // ------------------------------------------------------------------ TOML

    private static Meta ParseToml(string text)
    {
        var m = new Meta();
        string section = "";
        foreach (var raw in text.Split('\n'))
        {
            var line = StripComment(raw).Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith("[[") && line.EndsWith("]]"))
            {
                section = line[2..^2].Trim();
                if (section.Equals("neededFiles", StringComparison.OrdinalIgnoreCase)) m.Needed.Add(("", ""));
                continue;
            }
            if (line.StartsWith("[") && line.EndsWith("]")) { section = line[1..^1].Trim(); continue; }

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim().ToLowerInvariant();
            var val = line[(eq + 1)..].Trim();

            switch (section.ToLowerInvariant())
            {
                case "metadata":
                    if (key == "name") m.Name = Unquote(val);
                    else if (key == "version") m.Version = Unquote(val);
                    else if (key == "description") m.Description = Unquote(val);
                    else if (key == "author") m.Author = FirstArrayItem(val);
                    else if (key == "packageid") m.PackageId = Unquote(val);
                    break;
                case "neededfiles":
                    if (m.Needed.Count == 0) m.Needed.Add(("", ""));
                    var idx = m.Needed.Count - 1;
                    var cur = m.Needed[idx];
                    if (key == "file") m.Needed[idx] = (Unquote(val), cur.sha);
                    else if (key == "checksum") m.Needed[idx] = (cur.file, Unquote(val));
                    break;
                default:
                    if (key == "deltarunetargetversion") m.TargetVersion = Unquote(val);
                    break;
            }
        }
        if (string.IsNullOrWhiteSpace(m.Version)) m.Version = "1.0.0";
        return m;
    }

    private static string StripComment(string line)
    {
        bool q = false;
        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"') q = !q;
            else if (c == '#' && !q) return line[..i];
        }
        return line;
    }

    private static string Unquote(string v)
    {
        v = v.Trim();
        if (v.Length >= 2 && v[0] == '"' && v[^1] == '"') v = v[1..^1];
        return v.Replace("\\\"", "\"");
    }

    private static string FirstArrayItem(string v)
    {
        var m = Regex.Match(v, "\"([^\"]*)\"");
        return m.Success ? m.Groups[1].Value : Unquote(v);
    }

    // ------------------------------------------------------------------ 工具

    private static ChapterPlan Plan(Dictionary<string, ChapterPlan> plans, string chapter)
    {
        foreach (var kv in plans)
            if (!kv.Value.Unsure && kv.Value.Chapter.Equals(chapter, StringComparison.OrdinalIgnoreCase)) return kv.Value;
        var p = new ChapterPlan { Chapter = chapter };
        plans[chapter] = p;
        return p;
    }

    private static IEnumerable<ChapterPlan> Ordered(Dictionary<string, ChapterPlan> plans) =>
        plans.Values
             .OrderBy(p => p.Unsure ? 1 : (p.Chapter.Equals("root", StringComparison.OrdinalIgnoreCase) ? 0 : 2))
             .ThenBy(p => p.Chapter, StringComparer.OrdinalIgnoreCase);

    private static void PrintPlan(Dictionary<string, ChapterPlan> plans)
    {
        Console.WriteLine(L("  计划:"));
        foreach (var p in Ordered(plans))
        {
            var what = p.Xdelta != null ? Path.GetFileName(p.Xdelta) : (p.CopyFrom != null ? L("data.win 整包") : "-");
            Console.WriteLine($"    {(p.Unsure ? "?" : p.Chapter),-10} {what,-24} override={p.Overrides.Count}");
        }
    }

    /// <summary>工作区内的临时目录：不用 %TEMP%（本机子进程创建被拒，且不想污染 C 盘），
    /// 与目标同卷 → 探测产物可以秒级 rename。</summary>
    private static string TempDir(string gameRoot)
    {
        var d = Path.Combine(Paths.NeutraledRoot(gameRoot), ".tmp");
        Directory.CreateDirectory(d);
        return d;
    }

    private static string Sha256(string path)
    {
        if (ShaCache.TryGetValue(path, out var cached)) return cached;
        using var fs = File.OpenRead(path);
        using var sha = SHA256.Create();
        var v = Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
        ShaCache[path] = v;
        return v;
    }

    private static string FirstNonEmpty(params string?[] vals)
    {
        foreach (var v in vals) if (!string.IsNullOrWhiteSpace(v)) return v!.Trim();
        return "";
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
