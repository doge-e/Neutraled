using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>Kristal 宿主合并导入 —— 把"插件型 Kristal mod"（只有 scripts/ + assets/、没有 mod.json）
/// 合并进宿主项目后**整体**转换。
///
/// 为什么需要它：Kristal 的插件挂在 &lt;项目&gt;\mods\&lt;插件名&gt;\ 下由引擎加载，插件本身没有 mod.json
/// （章节/地图/队伍全由宿主项目决定），因此直接丢给 KristalImport 必然报「缺少 mod.json」。
/// 缺的不是转换能力，是宿主。
///
/// 流程：
///   1) 宿主项目整树复制到 Neutraled\.tmp\kristal-merge-&lt;随机&gt;\（不污染用户目录；
///      本机 %TEMP% 对子进程可能被拒，所以临时区必须落在 NTL 同卷的 .tmp 下）
///   2) 每个插件解析出真正的插件根（自动下钻），复制到 &lt;暂存&gt;\mods\&lt;插件名&gt;\
///      —— 插件名优先取插件 mod.json 的 id，其次 name，最后取目录名
///   3) 插件的 assets\{sprites,sounds} 额外"上提"到宿主 assets\ 下再合并
///      （KristalImport 只扫描项目根的 assets\，不上提的话插件资源根本进不了转换产物）
///   4) 调 KristalImport.Import(gameRoot, 暂存, overrideName, overrideAuthor, timeline: true)
///   5) 打印逐插件合并清单 + 冲突清单，并把清单写进产物的 KRISTAL-MERGE.txt
///
/// 同名文件冲突一律**响亮报告**（保留先到者、丢弃后到者并列出双方归属），绝不静默覆盖。
/// </summary>
public static class KristalMerge
{
    /// <summary>自动下钻的最大层数（插件常被包在"下载目录/版本号目录"里）。</summary>
    private const int MaxDescendDepth = 3;

    private static readonly string[] SkipDirs = { ".git", ".vs", ".idea", "__MACOSX" };
    private static readonly string[] SkipFiles = { ".DS_Store", "Thumbs.db", "desktop.ini" };
    private static readonly string[] LiftSubs = { "sprites", "sounds" };

    /// <summary>单个插件的合并计划与结果（用于报告）。</summary>
    private sealed class PluginPlan
    {
        public string Name = "";           // 显示名（mod.json id/name 或目录名）
        public bool Renamed;
        public bool HasModJson;
        public bool HasModLua;
        public int ScriptFiles;
        public int ScriptLua;
        public int AssetFiles;
        public int OtherFiles;
        public int LiftedSprites;
        public int LiftedSounds;
        public readonly List<string> LuaFiles = new();
    }

    /// <summary>把若干插件 mod 合并进宿主项目后整体转换。
    /// pluginDirs 里每一项可以是插件根目录，也可以是它的父目录（自动下钻找含 scripts/ 或 assets/ 的那层）。
    /// 返回 0 成功 / 非 0 失败。</summary>
    public static int Import(string gameRoot, string projectDir, IEnumerable<string> pluginDirs,
                             string? overrideName = null, string? overrideAuthor = null)
    {
        if (string.IsNullOrWhiteSpace(gameRoot)) { Paths.Log(L("  [错误] gameRoot 为空")); return 1; }
        if (string.IsNullOrWhiteSpace(projectDir)) { Paths.Log(L("  [错误] 未指定宿主项目目录")); return 1; }
        projectDir = projectDir.Trim().Trim('"');
        if (File.Exists(projectDir)) { Paths.Log(L("  [错误] 宿主项目应为目录，却收到文件: {0}", projectDir)); return 1; }
        if (!Directory.Exists(projectDir))
        {
            Paths.Log(L("  [错误] 宿主项目目录不存在: {0}", projectDir));
            Paths.Log(L("     请确认路径；Kristal 项目目录里应当能看到 mod.json / assets / scripts。"));
            return 1;
        }
        projectDir = Path.GetFullPath(projectDir);

        var rawPlugins = new List<string>();
        if (pluginDirs != null)
            foreach (var p in pluginDirs)
                if (!string.IsNullOrWhiteSpace(p)) rawPlugins.Add(p.Trim().Trim('"'));
        if (rawPlugins.Count == 0)
        {
            Paths.Log(L("  [错误] 未提供任何插件目录（至少要给一个插件型 Kristal mod）"));
            Paths.Log(L("     用法: --import-kristal-merge <宿主项目目录> <插件目录1> [插件目录2 ...]"));
            return 1;
        }

        // 宿主必须是"项目"：mod.json 是 Kristal 区分项目/插件的唯一权威标记
        var hostModJson = Path.Combine(projectDir, "mod.json");
        if (!File.Exists(hostModJson))
        {
            Paths.Log(L("  [错误] 宿主缺少 mod.json: {0}", hostModJson));
            if (HasKristalContent(projectDir))
                Paths.Log(L("     这个目录只有 scripts/ 或 assets/ —— 它本身就是插件型 mod，不能当宿主。请改传含 mod.json 的项目目录。"));
            else
                Paths.Log(L("     该目录既没有 mod.json，也没有 scripts/ 或 assets/，请确认路径。"));
            return 1;
        }

        var hostRoot = TryParseKristalJson(hostModJson, out var hostParseErr);
        if (hostRoot == null)
            Paths.Log(L("  [警告] 宿主 mod.json 解析失败（{0}），元数据按目录名推断", hostParseErr));

        var hostName = Field(hostRoot, "name");
        if (string.IsNullOrWhiteSpace(hostName)) hostName = new DirectoryInfo(projectDir).Name;
        var hostAuthor = Field(hostRoot, "authors");
        if (string.IsNullOrWhiteSpace(hostAuthor)) hostAuthor = Field(hostRoot, "author");
        if (string.IsNullOrWhiteSpace(hostAuthor)) hostAuthor = "unknown";
        var hostChapter = FieldInt(hostRoot, "chapter", 1);
        if (hostChapter < 1 || hostChapter > 7) hostChapter = 1;
        var hostMap = Field(hostRoot, "map");

        var stage = Path.Combine(Paths.NeutraledRoot(gameRoot), ".tmp",
                                 "kristal-merge-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" +
                                 Guid.NewGuid().ToString("N").Substring(0, 6));
        var report = new StringBuilder();
        var conflicts = new List<string>();
        var owned = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var plans = new List<PluginPlan>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mergedRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skippedPlugins = new List<string>();
        bool ok = false;
        bool keepStage = false;   // 只有"转换已开始但失败"才值得保留现场

        // 跳过时记录，结尾统一响亮汇总 —— 免得"少合并了一个插件"被埋在日志里
        void SkipPlugin(string requested, string reason)
        {
            Paths.Log("    " + reason);
            skippedPlugins.Add(requested);
        }

        try
        {
            // ── 1) 暂存：复制宿主项目（绝不直接改用户的目录） ──
            Paths.Log(L("  Kristal 宿主合并: {0}  v{1}  基底章节 {2}", hostName, Field(hostRoot, "version").TrimStart('v', 'V'), hostChapter));
            Paths.Log(L("    宿主: {0}", projectDir));
            Paths.Log(L("    暂存: {0}", stage));
            Directory.CreateDirectory(stage);
            int hostFiles = 0;
            CopyTree(projectDir, stage, L("宿主项目"), _ => hostFiles++, owned, conflicts);
            Paths.Log(L("    宿主文件: {0} 个", hostFiles));
            report.AppendLine($"宿主项目: {projectDir}");
            report.AppendLine($"  名称={hostName}  作者={hostAuthor}  章节={hostChapter}  起始地图={(string.IsNullOrEmpty(hostMap) ? "(未声明)" : hostMap)}");
            report.AppendLine($"  文件数={hostFiles}");
            report.AppendLine($"  暂存目录={stage}");

            // ── 2) 逐个插件：解析根 → 定名 → 复制 → 上提资源 ──
            report.AppendLine();
            report.AppendLine("===== 插件合并清单 =====");
            var stageMods = Path.Combine(stage, "mods");
            Directory.CreateDirectory(stageMods);

            for (int i = 0; i < rawPlugins.Count; i++)
            {
                var requested = rawPlugins[i];
                Paths.Log("");
                Paths.Log(L("  [插件 {0}/{1}] {2}", i + 1, rawPlugins.Count, requested));

                if (File.Exists(requested))
                {
                    SkipPlugin(requested, L("[跳过] 这是一个文件，不是目录；压缩包请先解压再传入。"));
                    continue;
                }
                if (!Directory.Exists(requested))
                {
                    SkipPlugin(requested, L("[跳过] 目录不存在，请确认路径。"));
                    continue;
                }

                var pluginRoot = Path.GetFullPath(requested);
                if (string.Equals(pluginRoot, projectDir, StringComparison.OrdinalIgnoreCase))
                {
                    SkipPlugin(requested, L("[跳过] 与宿主项目是同一个目录（它已经在被转换了）。"));
                    continue;
                }

                if (!TryResolvePluginRoot(pluginRoot, out var resolved, out var candidates))
                {
                    if (candidates.Count > 1)
                    {
                        Paths.Log(L("    [失败] 该目录下有 {0} 个候选插件，无法自动判断用哪个：", candidates.Count));
                        foreach (var c in candidates) Paths.Log($"        - {c}");
                        Paths.Log(L("      请把 --import-kristal-merge 的参数直接指向其中某一个目录。"));
                        skippedPlugins.Add(requested);
                    }
                    else
                    {
                        Paths.Log(L("    [失败] 该目录（含 {0} 层子目录）既没有 scripts/ 也没有 assets/，请确认路径。", MaxDescendDepth));
                        Paths.Log(L("      期望结构: {0}\\scripts\\*.lua  或  {1}\\assets\\sprites|sounds\\", pluginRoot, pluginRoot));
                        skippedPlugins.Add(requested);
                    }
                    continue;
                }

                if (!string.Equals(resolved, pluginRoot, StringComparison.OrdinalIgnoreCase))
                    Paths.Log(L("    [下钻] 真正的插件根: {0}", resolved));

                if (string.Equals(resolved, projectDir, StringComparison.OrdinalIgnoreCase))
                {
                    SkipPlugin(requested, L("[跳过] 下钻结果就是宿主项目本身。"));
                    continue;
                }
                if (!mergedRoots.Add(resolved))
                {
                    SkipPlugin(requested, L("[跳过] 与前面某个插件解析到同一个目录（重复传入）。"));
                    continue;
                }

                var plan = new PluginPlan();
                plan.HasModJson = File.Exists(Path.Combine(resolved, "mod.json"));
                plan.HasModLua = File.Exists(Path.Combine(resolved, "mod.lua"));

                // 插件名: mod.json id > name > 目录名
                string dispName = "";
                if (plan.HasModJson)
                {
                    var pj = TryParseKristalJson(Path.Combine(resolved, "mod.json"), out var perr);
                    if (pj == null) Paths.Log(L("    [警告] 插件 mod.json 解析失败（{0}），改用目录名", perr));
                    else
                    {
                        dispName = Field(pj, "id");
                        if (string.IsNullOrWhiteSpace(dispName)) dispName = Field(pj, "name");
                    }
                }
                if (string.IsNullOrWhiteSpace(dispName)) dispName = new DirectoryInfo(resolved).Name;
                plan.Name = dispName;

                var dirName = Mods.Sanitize(dispName);
                if (dirName.Length == 0) dirName = Mods.Sanitize(new DirectoryInfo(resolved).Name);
                if (dirName.Length == 0) dirName = "plugin" + (i + 1);
                var unique = dirName;
                int suffix = 2;
                while (usedNames.Contains(unique) || Directory.Exists(Path.Combine(stageMods, unique)))
                {
                    unique = dirName + "_" + suffix++;
                    plan.Renamed = true;
                }
                usedNames.Add(unique);

                var dst = Path.Combine(stageMods, unique);
                var owner = L("插件 {0}", dispName);
                CopyTree(resolved, dst, owner, rel => Classify(plan, rel), owned, conflicts);

                // 资源上提: 插件 assets{sprites,sounds} → 宿主 assets（否则 KristalImport 扫不到）
                foreach (var sub in LiftSubs)
                {
                    var srcSub = Path.Combine(resolved, "assets", sub);
                    if (!Directory.Exists(srcSub)) continue;
                    int n = 0;
                    CopyTree(srcSub, Path.Combine(stage, "assets", sub), $"{owner}/assets/{sub}", _ => n++, owned, conflicts);
                    if (sub == "sprites") plan.LiftedSprites = n; else plan.LiftedSounds = n;
                }

                plans.Add(plan);

                Paths.Log(L("    合并为 mods/{0}/{1}", unique, (plan.Renamed ? L("   [注意] 目录名已去重（原候选 {0}）", dirName) : "")));
                Paths.Log(L("    内容: scripts {0} 个（其中 .lua {1}）/ assets {2} 个 / 其它 {3} 个", plan.ScriptFiles, plan.ScriptLua, plan.AssetFiles, plan.OtherFiles));
                Paths.Log(L("    mod.json: {0}   mod.lua: {1}", (plan.HasModJson ? L("有（本可独立成项目）") : L("无（插件型，符合预期）")), (plan.HasModLua ? L("有") : L("无"))));
                Paths.Log(L("    资源上提: sprites {0} / sounds {1}", plan.LiftedSprites, plan.LiftedSounds));

                report.AppendLine();
                report.AppendLine($"[{i + 1}] {dispName}");
                report.AppendLine($"    来源: {resolved}{(string.Equals(resolved, pluginRoot, StringComparison.OrdinalIgnoreCase) ? "" : "   (由 " + pluginRoot + " 下钻)")}");
                report.AppendLine($"    合并到: mods/{unique}/{(plan.Renamed ? "   [目录名冲突，已去重]" : "")}");
                report.AppendLine($"    scripts {plan.ScriptFiles} 个（.lua {plan.ScriptLua}）/ assets {plan.AssetFiles} 个 / 其它 {plan.OtherFiles} 个");
                report.AppendLine($"    mod.json: {(plan.HasModJson ? "有" : "无（插件型）")}   mod.lua: {(plan.HasModLua ? "有" : "无")}");
                report.AppendLine($"    资源上提: sprites {plan.LiftedSprites} / sounds {plan.LiftedSounds}");
                if (plan.LuaFiles.Count > 0)
                {
                    plan.LuaFiles.Sort(StringComparer.OrdinalIgnoreCase);
                    report.AppendLine($"    [需手工迁移的插件 Lua 脚本 {plan.LuaFiles.Count} 个]（语言不同，无法自动转 GML）");
                    foreach (var l in plan.LuaFiles) report.AppendLine("      " + l);
                }
            }

            if (plans.Count == 0)
            {
                Paths.Log("");
                Paths.Log(L("  [错误] 没有任何插件被成功合并 —— 中止（不产出只有宿主的转换结果，避免误以为合并成功）"));
                Paths.Log(L("     请按上面的 [失败]/[跳过] 提示修正插件路径后重试。"));
                return 1;
            }

            if (skippedPlugins.Count > 0)
            {
                Paths.Log("");
                Paths.Log(L("  [注意] 有 {0} 个插件未被合并 —— 转换产物将缺少它们的内容:", skippedPlugins.Count));
                foreach (var s in skippedPlugins) Paths.Log("      " + s);
                Paths.Log(L("      若这是无意的，请修正路径后重跑；本次仍会继续转换已合并的插件。"));
                report.AppendLine();
                report.AppendLine($"===== 未合并的插件 {skippedPlugins.Count} 个（产物缺少其内容）=====");
                foreach (var s in skippedPlugins) report.AppendLine("  " + s);
            }

            // ── 3) 整体转换（timeline: true → ~Chapter:N:name） ──
            Paths.Log("");
            Paths.Log(L("  合并完成: {0} 个插件 → 交给 KristalImport 整体转换", plans.Count));
            keepStage = true;   // 从这里起失败要保留现场供排查
            int rc = KristalImport.Import(gameRoot, stage, overrideName, overrideAuthor, true);
            if (rc != 0)
            {
                Paths.Log(L("  [错误] KristalImport 返回 {0}，转换失败。现场保留: {1}", rc, stage));
                return rc;
            }
            ok = true;
            keepStage = false;

            // ── 4) 报告 ──
            var outName = overrideName ?? hostName;
            var outAuthor = overrideAuthor ?? hostAuthor;
            var outDir = Path.Combine(Paths.ModsRoot(gameRoot), Mods.Sanitize(outName),
                                      Mods.Sanitize(outAuthor), "chapter" + hostChapter);

            report.AppendLine();
            report.AppendLine("===== 合并结果 =====");
            report.AppendLine($"插件数={plans.Count}   转换产物={outDir}");
            report.AppendLine($"插件清单: {string.Join(", ", plans.Select(p => p.Name))}");

            if (conflicts.Count > 0)
            {
                conflicts.Sort(StringComparer.OrdinalIgnoreCase);
                Paths.Log("");
                Paths.Log(L("  [冲突] {0} 处同名文件 —— 保留先到者，丢弃后到者（未静默覆盖）:", conflicts.Count));
                report.AppendLine();
                report.AppendLine($"===== 同名文件冲突 {conflicts.Count} 处（保留先到者）=====");
                foreach (var c in conflicts)
                {
                    Paths.Log("      " + c);
                    report.AppendLine("  " + c);
                }
                Paths.Log(L("      多个插件提供同名资源时，后到者被丢弃；若顺序不对，请调整命令行里的插件先后顺序。"));
                report.AppendLine("  多个插件提供同名资源时，后到者被丢弃；调整插件先后顺序即可改变优先级。");
            }

            int totalScripts = plans.Sum(p => p.ScriptFiles);
            int totalAssets = plans.Sum(p => p.AssetFiles);
            Paths.Log("");
            Paths.Log(L("  合并统计: 插件 {0} 个 / scripts {1} 个 / assets {2} 个 / 上提资源 {3} 个 / 冲突 {4} 处", plans.Count, totalScripts, totalAssets, plans.Sum(p => p.LiftedSprites + p.LiftedSounds), conflicts.Count));

            if (Directory.Exists(outDir))
            {
                try
                {
                    var head = new StringBuilder();
                    head.AppendLine("===== Kristal 宿主合并清单 =====");
                    head.AppendLine($"宿主: {hostName}    插件: {plans.Count} 个");
                    head.AppendLine();
                    head.Append(report);
                    File.WriteAllText(Path.Combine(outDir, "KRISTAL-MERGE.txt"), head.ToString(), Encoding.UTF8);
                    Paths.Log(L("  合并清单: {0}", Path.Combine(outDir, "KRISTAL-MERGE.txt")));
                }
                catch (Exception ex) { Paths.Log(L("  [警告] 合并清单写入失败: {0}", ex.Message)); }
            }
            else
            {
                Paths.Log(L("  [警告] 未找到预期产物目录 {0}，跳过合并清单写入（转换本身已成功）", outDir));
            }
            return 0;
        }
        catch (Exception ex)
        {
            Paths.Log(L("  [错误] 合并导入异常: {0}: {1}", ex.GetType().Name, ex.Message));
            Paths.Log(keepStage ? L("     现场保留: {0}", stage) : L("     暂存目录已清理。"));
            return 1;
        }
        finally
        {
            // 只有"转换已开始但失败"才保留现场；输入校验类失败没有排查价值，直接清掉不留垃圾
            if (ok || !keepStage)
            {
                try { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
                catch (Exception ex) { Paths.Log(L("  [警告] 暂存目录清理失败（可手工删）: {0} — {1}", stage, ex.Message)); }
            }
        }
    }

    // ────────────────────────── 内部工具 ──────────────────────────

    /// <summary>插件根判定：含 scripts/ 或 assets/ 即认为是插件/项目根。</summary>
    private static bool HasKristalContent(string dir) =>
        Directory.Exists(Path.Combine(dir, "scripts")) || Directory.Exists(Path.Combine(dir, "assets"));

    /// <summary>把"下载目录 / 版本号目录"下钻到真正的插件根。
    /// 命中唯一候选即返回 true；多候选或找不到返回 false（candidates 用于给出可执行提示）。</summary>
    private static bool TryResolvePluginRoot(string dir, out string root, out List<string> candidates)
    {
        root = dir;
        candidates = new List<string>();
        if (HasKristalContent(dir)) return true;

        var frontier = new List<string> { dir };
        for (int depth = 1; depth <= MaxDescendDepth && frontier.Count > 0; depth++)
        {
            var next = new List<string>();
            var hits = new List<string>();
            foreach (var d in frontier)
                foreach (var sub in SafeDirs(d))
                    if (HasKristalContent(sub)) hits.Add(sub); else next.Add(sub);
            if (hits.Count == 1) { root = hits[0]; return true; }
            if (hits.Count > 1) { candidates = hits; return false; }
            frontier = next;
        }
        return false;
    }

    private static IEnumerable<string> SafeDirs(string dir)
    {
        try { return Directory.GetDirectories(dir); }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>递归复制；同名文件**不覆盖**，改为记录冲突（保留先到者）。</summary>
    private static void CopyTree(string srcRoot, string dstRoot, string owner, Action<string> onFile,
                                 Dictionary<string, string> owned, List<string> conflicts)
    {
        var queue = new Queue<(string src, string dst)>();
        queue.Enqueue((srcRoot, dstRoot));
        while (queue.Count > 0)
        {
            var (src, dst) = queue.Dequeue();
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src))
            {
                var name = Path.GetFileName(f);
                if (SkipFiles.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                var target = Path.Combine(dst, name);
                var rel = Path.GetRelativePath(dstRoot, target).Replace('\\', '/');
                if (File.Exists(target))
                {
                    var prev = owned.TryGetValue(target, out var o) ? o : L("宿主项目已有");
                    conflicts.Add(L("{0}  ←  保留 [{1}]，丢弃 [{2}]", rel, prev, owner));
                    continue;
                }
                File.Copy(f, target, true);
                owned[target] = owner;
                onFile(rel);
            }
            foreach (var d in SafeDirs(src))
            {
                var n = Path.GetFileName(d);
                if (SkipDirs.Contains(n, StringComparer.OrdinalIgnoreCase)) continue;
                queue.Enqueue((d, Path.Combine(dst, n)));
            }
        }
    }

    /// <summary>按插件树里的相对路径归类计数（scripts/ vs assets/ vs 其它）。</summary>
    private static void Classify(PluginPlan p, string rel)
    {
        var slash = rel.IndexOf('/');
        var top = slash < 0 ? "" : rel.Substring(0, slash);
        if (top == "scripts")
        {
            p.ScriptFiles++;
            if (rel.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)) { p.ScriptLua++; p.LuaFiles.Add(rel); }
        }
        else if (top == "assets") p.AssetFiles++;
        else p.OtherFiles++;
    }

    /// <summary>读 Kristal 风格 JSON（含 // 与 /* */ 注释、尾随逗号、未加引号的 {占位符}）。</summary>
    private static JsonNode? TryParseKristalJson(string path, out string error)
    {
        error = "";
        string text;
        try { text = File.ReadAllText(path); }
        catch (Exception ex) { error = ex.Message; return null; }

        try
        {
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions
            { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            return JsonNode.Parse(doc.RootElement.GetRawText());
        }
        catch { /* 落到下面的宽容解析 */ }

        try
        {
            using var doc = JsonDocument.Parse(SanitizeKristalJson(text), new JsonDocumentOptions { AllowTrailingCommas = true });
            return JsonNode.Parse(doc.RootElement.GetRawText());
        }
        catch (Exception ex) { error = ex.Message; return null; }
    }

    /// <summary>剥注释 → 占位符 {xxx} → 去尾随逗号。</summary>
    private static string SanitizeKristalJson(string text)
    {
        var sb = new StringBuilder(text.Length);
        bool inStr = false, esc = false, inLine = false, inBlock = false;
        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';
            if (inLine) { if (c == '\n') { inLine = false; sb.Append(c); } continue; }
            if (inBlock) { if (c == '*' && next == '/') { inBlock = false; i++; } continue; }
            if (inStr)
            {
                sb.Append(c);
                if (esc) esc = false;
                else if (c == '\\') esc = true;
                else if (c == '"') inStr = false;
                continue;
            }
            if (c == '"') { inStr = true; sb.Append(c); continue; }
            if (c == '/' && next == '/') { inLine = true; i++; continue; }
            if (c == '/' && next == '*') { inBlock = true; i++; continue; }
            sb.Append(c);
        }
        var s = Regex.Replace(sb.ToString(), @":\s*\{(\w+)\}", ": 0");
        s = Regex.Replace(s, @"\{(\w+)\}", "");
        return Regex.Replace(s, @",\s*([}\]])", "$1");
    }

    private static string Field(JsonNode? root, string key)
    {
        var v = root?[key];
        if (v == null) return "";
        var s = v.ToString().Trim().Trim('"');
        if (s.StartsWith("{") && s.EndsWith("}")) return "";   // Kristal 模板占位符
        return s.Trim();
    }

    private static int FieldInt(JsonNode? root, string key, int fallback)
    {
        var s = Field(root, key);
        return int.TryParse(s, out var i) ? i : fallback;
    }
}
