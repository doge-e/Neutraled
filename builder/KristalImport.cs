using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>Kristal（Lua mod 框架）→ Neutraled mod 转换。
///
/// Kristal mod 结构：mod.json（幂等元数据）+ mod.lua（入口）+ assets/{sprites,sounds} + scripts/{battle,world,data}
/// 关键映射：
///   id/name/version  → Neutraled mod 元数据
///   chapter          → 基底章节（Neutraled 的 mods/&lt;name&gt;/&lt;author&gt;/chapterN/）
///   整个 mod         → 默认注册为 ~Chapter:N:name（平行时间线，因为 Kristal mod 是独立内容）
///   assets/sprites   → Neutraled 精灵包（单帧 PNG → JSON）
///   assets/sounds    → Neutraled 声音包
///   scripts/*.lua    → 无法自动转换（语言不同），写入转换报告供开发者手工迁移
/// </summary>
public static class KristalImport
{
    public static int Import(string gameRoot, string kristalDir, string? overrideName = null,
        string? overrideAuthor = null, bool timeline = true)
    {
        if (!Directory.Exists(kristalDir)) { Paths.Log(L("  [错误] 目录不存在: {0}", kristalDir)); return 1; }

        // 1) 读取 mod.json（Kristal 使用带注释的 JSON）
        var modJsonPath = Path.Combine(kristalDir, "mod.json");
        if (!File.Exists(modJsonPath))
        {
            bool plugin = Directory.Exists(Path.Combine(kristalDir, "scripts")) || Directory.Exists(Path.Combine(kristalDir, "assets"));
            if (plugin)
            {
                Paths.Log(L("  [无法独立转换] 这是 Kristal **插件型 mod**（只有 scripts/ + assets/，没有 mod.json）"));
                Paths.Log(L("     它必须挂在某个 Kristal 项目下运行（项目的 mod.json 决定 chapter/map/party）。"));
                Paths.Log(L("     可行路径："));
                Paths.Log(L("       1) 放进某个 Kristal 项目的 mods/ 目录，然后对**那个项目**跑 --import-kristal"));
                Paths.Log(L("       2) 想做成 Neutraled 独立章节，需要项目级 data.win（Kristal 主引擎尚未移植）"));
            }
            else
            {
                Paths.Log(L("  [错误] 缺少 mod.json: {0}", modJsonPath));
            }
            return 1;
        }

        JsonNode? root;
        try
        {
            var raw = File.ReadAllText(modJsonPath);
            raw = SanitizeJson(raw);

            var opts = new JsonDocumentOptions { AllowTrailingCommas = true };
            using var doc = JsonDocument.Parse(raw, opts);
            root = JsonNode.Parse(doc.RootElement.GetRawText());
        }
        catch (Exception ex) { Paths.Log(L("  [错误] mod.json 解析失败: {0}", ex.Message)); return 1; }
        if (root == null) { Paths.Log(L("  [错误] mod.json 为空")); return 1; }

        string S(string key, string fallback = "")
        {
            var v = root[key];
            if (v == null) return fallback;
            var s = v.ToString().Trim().Trim('"');
            // Kristal 模板里的占位符 {id}/{name} 视为空
            if (s.StartsWith("{") && s.EndsWith("}")) return fallback;
            return string.IsNullOrWhiteSpace(s) ? fallback : s;
        }
        int I(string key, int fallback)
        {
            var v = root[key];
            if (v == null) return fallback;
            var s = v.ToString().Trim();
            if (s.StartsWith("{") && s.EndsWith("}")) return fallback;
            return int.TryParse(s, out var i) ? i : fallback;
        }

        var dirName = Path.GetFileName(kristalDir.TrimEnd('\\', '/'));
        var modName = overrideName ?? S("name", dirName);
        var author = overrideAuthor ?? S("authors", S("author", "unknown"));
        var version = S("version", "1.0.0").TrimStart('v', 'V');
        var chapterOrder = I("chapter", 1);
        if (chapterOrder < 1 || chapterOrder > 7) chapterOrder = 1;
        var startMap = S("map", "");

        Paths.Log(L("  Kristal mod: {0} v{1}  基底章节 {2}  起始地图 {3}", modName, version, chapterOrder, startMap));

        // 2) 输出目录（Neutraled 统一格式）
        var neutraled = Paths.NeutraledRoot(gameRoot);
        var modDir = Path.Combine(neutraled, "mods", Mods.Sanitize(modName), Mods.Sanitize(author),
                                  "chapter" + chapterOrder);
        Directory.CreateDirectory(modDir);

        // 3) 资源转换
        int spriteCount = ConvertSprites(kristalDir, modDir);
        int soundCount = ConvertSounds(kristalDir, modDir);
        int fileCount = CopyMiscFiles(kristalDir, modDir);

        // 3.5) 外部启动登记
        //   Kristal 项目是 **LÖVE 工程**（main.lua + conf.lua + data/），根本没有 GameMaker data.win，
        //   所以在 DELTARUNE 运行时里加载不了它。与其移植引擎，不如把它登记成「外部章节」：
        //   在章节选择里选中它 → Neutraled 退出游戏 → 拉起 Kristal 引擎跑这个项目。
        //   这样**插件加载 / 进度保存 / 通关判定全部是 Kristal 原生行为**，一点都不用仿。
        var engine = FindKristalEngine(gameRoot);
        var love = FindLove(gameRoot, engine);
        ModExternal? ext = null;
        if (timeline && engine != null && love != null)
        {
            CopyDir(kristalDir, Path.Combine(modDir, "kristal"));   // 保留原项目（自包含、可搬走）
            ext = new ModExternal
            {
                Exe = love,
                Args = engine,
                Cwd = engine,
                Project = "kristal",
                EngineMods = Path.Combine(engine, "mods"),
                ModName = Mods.Sanitize(modName)
            };
            Paths.Log(L("  [外部章节] 已登记：选中该章节 → 退出游戏并启动 {0}（引擎 {1}）", Path.GetFileName(love), Path.GetFileName(engine)));
        }
        else if (timeline)
        {
            Paths.Log(L("  [提示] 没找到 Kristal 引擎或 LÖVE 运行时，无法登记为外部章节"));
            Paths.Log(L("         → 放一份 Kristal 到 <游戏根>\\Kristal-main，把 love.exe 放进 <游戏根>\\Kristal-main\\ 或 Neutraled\\tools\\love\\"));
        }

        // 4) 生成 mod.json
        // 独立章节（~Chapter:N:name）**必须自带 data.win**，否则每次部署都报「平行时间线缺少自带 data」。
        // Kristal 主引擎尚未移植 → 转换产物通常没有 data；这种情况把 mod 标记为**未启用**，
        // 让它不再是个天天报错的雷，同时资源（精灵/声音）仍然可用、可叠加到别的基底上。
        var dataDir = Path.Combine(modDir, "data");
        bool hasOwnData = Directory.Exists(dataDir)
                          && Directory.GetFiles(dataDir, "*.win", SearchOption.AllDirectories).Length > 0;
        var chapterSpec = (timeline ? "~Chapter:" : "Chapter:") + chapterOrder + ":" + modName;
        var modEntry = new JsonObject
        {
            ["id"] = "kristal." + Mods.Sanitize(modName),
            ["name"] = modName,
            ["author"] = author,
            ["version"] = version,
            ["enabled"] = hasOwnData || ext != null,   // 外部章节不需要自带 data
            ["chapters"] = new JsonArray { chapterSpec },
            ["dependencies"] = new JsonArray(),
            ["api"] = new JsonObject
            {
                ["ns"] = Mods.Sanitize(modName),
                ["functions"] = new JsonArray(),
                ["constants"] = new JsonArray()
            }
        };
        if (ext != null)
        {
            modEntry["kristal_external"] = new JsonObject
            {
                ["exe"] = ext.Exe,
                ["args"] = ext.Args,
                ["cwd"] = ext.Cwd,
                ["project"] = ext.Project,
                ["engine_mods"] = ext.EngineMods,
                ["mod_name"] = ext.ModName
            };
        }
        File.WriteAllText(Path.Combine(modDir, "mod.json"),
            modEntry.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }));

        // 5) 转换报告
        var report = new StringBuilder();
        report.AppendLine("===== Kristal → Neutraled 转换报告 =====");
        report.AppendLine($"来源: {kristalDir}");
        report.AppendLine($"输出: {modDir}");
        report.AppendLine();
        report.AppendLine($"[元数据] 名称={modName}  作者={author}  版本={version}");
        report.AppendLine($"[章节]   声明为 {chapterSpec}");
        report.AppendLine($"         含义: {(timeline ? "第 " + chapterOrder + " 章的平行时间线（玩家可独立进入）" : "修改第 " + chapterOrder + " 章")}");
        if (!string.IsNullOrEmpty(startMap)) report.AppendLine($"[起始地图] {startMap}（Kristal 地图需手工迁移到 GML 房间）");
        report.AppendLine();
        report.AppendLine($"[资源] 精灵 {spriteCount} 个 / 声音 {soundCount} 个 / 其它文件 {fileCount} 个");
        if (!hasOwnData)
        {
            report.AppendLine();
            report.AppendLine("[未启用] 该转化产物**没有自带 data.win**（Kristal 主引擎未移植 → 无法作为独立章节运行）。");
            report.AppendLine("         已把 mod.json 的 enabled 设为 false，避免每次部署报「平行时间线缺少自带 data」。");
            report.AppendLine("         资源已经转换好了：可以把它当作资源包叠加到别的基底上，或补一份自带 data 后再启用。");
        }
        report.AppendLine();
        report.AppendLine("[需要手工迁移的部分]");
        foreach (var lua in Directory.Exists(Path.Combine(kristalDir, "scripts"))
                     ? Directory.GetFiles(Path.Combine(kristalDir, "scripts"), "*.lua", SearchOption.AllDirectories)
                     : Array.Empty<string>())
            report.AppendLine("  - " + Path.GetRelativePath(kristalDir, lua));
        if (File.Exists(Path.Combine(kristalDir, "mod.lua"))) report.AppendLine("  - mod.lua（入口）");
        report.AppendLine();
        report.AppendLine("Lua 脚本无法自动转换为 GML/NTL Script（语言不同）。建议：");
        report.AppendLine("  1. 阅读脚本逻辑，用 Neutraled 的 live 脚本（.ntl）重写等价行为");
        report.AppendLine("  2. 数据类内容（角色/物品/地图）可用 JSON 数据驱动");
        report.AppendLine("  3. 资源（精灵/声音）已自动转换，可直接在游戏里使用");
        File.WriteAllText(Path.Combine(modDir, "CONVERSION.txt"), report.ToString(), Encoding.UTF8);

        Paths.Log(L("  已生成 Neutraled mod: {0}", modDir));
        Paths.Log(L("    章节声明: {0}", chapterSpec));
        Paths.Log(L("    资源: 精灵 {0} / 声音 {1} / 其它 {2}", spriteCount, soundCount, fileCount));
        Paths.Log(L("    转换报告: {0}", Path.Combine(modDir, "CONVERSION.txt")));
        return 0;
    }

    /// <summary>把 Kristal 风格的 JSON（含 // 与 /* */ 注释、尾随逗号、未加引号占位符）整理成标准 JSON。</summary>
    private static string SanitizeJson(string text)
    {
        // 1) 逐字符扫描剥离注释（跳过字符串内部）
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
        var s = sb.ToString();

        // 2) 未加引号的占位符 {xxx} → 数字 0；引号内的 {xxx} → 清空
        s = System.Text.RegularExpressions.Regex.Replace(s, @":\s*\{(\w+)\}", ": 0");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\{(\w+)\}", "");

        // 3) 去掉对象/数组尾随逗号
        s = System.Text.RegularExpressions.Regex.Replace(s, @",\s*([}\]])", "$1");
        return s;
    }

    /// <summary>assets/sprites/**/*.png → Neutraled 精灵包（每张图为单帧精灵）。</summary>
    private static int ConvertSprites(string kristalDir, string modDir)
    {
        var srcDir = Path.Combine(kristalDir, "assets", "sprites");
        if (!Directory.Exists(srcDir)) return 0;
        var outDir = Path.Combine(modDir, "sprites");
        Directory.CreateDirectory(outDir);

        int count = 0;
        foreach (var png in Directory.GetFiles(srcDir, "*.png", SearchOption.AllDirectories))
        {
            var baseName = Path.GetFileNameWithoutExtension(png);
            var rel = Path.GetRelativePath(srcDir, png).Replace('\\', '/').Replace('/', '_');
            var frameFile = rel;   // 保持唯一文件名
            File.Copy(png, Path.Combine(outDir, frameFile), true);

            // 读取尺寸
            int w = 0, h = 0;
            try
            {
                using var img = new ImageMagick.MagickImage(png);
                w = (int)img.Width; h = (int)img.Height;
            }
            catch { }

            var def = new JsonObject
            {
                ["name"] = baseName,
                ["width"] = w,
                ["height"] = h,
                ["originX"] = w / 2,
                ["originY"] = h,
                ["marginLeft"] = 0, ["marginRight"] = 0, ["marginTop"] = 0, ["marginBottom"] = 0,
                ["bboxMode"] = 0,
                ["frames"] = new JsonArray
                {
                    new JsonObject { ["file"] = frameFile, ["targetX"] = 0, ["targetY"] = 0 }
                }
            };
            File.WriteAllText(Path.Combine(outDir, baseName + ".json"), def.ToJsonString(new JsonSerializerOptions(Paths.Json) { WriteIndented = true }));
            count++;
        }
        return count;
    }

    /// <summary>assets/sounds/**/* → Neutraled 声音包。</summary>
    private static int ConvertSounds(string kristalDir, string modDir)
    {
        var srcDir = Path.Combine(kristalDir, "assets", "sounds");
        if (!Directory.Exists(srcDir)) return 0;
        var outDir = Path.Combine(modDir, "sounds");
        Directory.CreateDirectory(outDir);

        int count = 0;
        foreach (var f in Directory.GetFiles(srcDir, "*.*", SearchOption.AllDirectories)
                     .Where(f => f.EndsWith(".ogg") || f.EndsWith(".wav") || f.EndsWith(".mp3")))
        {
            var baseName = Path.GetFileNameWithoutExtension(f);
            var ext = Path.GetExtension(f).ToLowerInvariant();
            var target = baseName + ext;
            File.Copy(f, Path.Combine(outDir, target), true);

            var def = new JsonObject
            {
                ["name"] = baseName,
                ["file"] = target,
                ["volume"] = 1.0,
                ["pitch"] = 1.0,
                ["type"] = ext.TrimStart('.'),
                ["preload"] = true
            };
            File.WriteAllText(Path.Combine(outDir, baseName + ".json"), def.ToJsonString(new JsonSerializerOptions(Paths.Json) { WriteIndented = true }));
            count++;
        }
        return count;
    }

    /// <summary>把其余文件（lang / data / 地图等）复制到 files/ 供手工使用。</summary>
    /// <summary>找 Kristal 引擎目录：&lt;游戏根&gt;\Kristal-main（也认 Kristal / kristal）。</summary>
    private static string? FindKristalEngine(string gameRoot)
    {
        foreach (var n in new[] { "Kristal-main", "Kristal", "kristal" })
        {
            var p = Path.Combine(gameRoot, n);
            if (File.Exists(Path.Combine(p, "main.lua")) && Directory.Exists(Path.Combine(p, "mods")))
                return p;
        }
        return null;
    }

    /// <summary>找 LÖVE 运行时：引擎目录 → Neutraled\tools\love → 游戏根 → PATH。</summary>
    private static string? FindLove(string gameRoot, string? engine)
    {
        var cands = new List<string>();
        if (engine != null)
        {
            cands.Add(Path.Combine(engine, "love.exe"));
            cands.Add(Path.Combine(engine, "lovec.exe"));
        }
        var ntl = Paths.NeutraledRoot(gameRoot);
        cands.Add(Path.Combine(ntl, "tools", "love", "love.exe"));
        cands.Add(Path.Combine(ntl, "tools", "love.exe"));
        cands.Add(Path.Combine(gameRoot, "love.exe"));
        foreach (var c in cands) if (File.Exists(c)) return c;

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            try
            {
                if (dir.Trim().Length == 0) continue;
                var p = Path.Combine(dir.Trim(), "love.exe");
                if (File.Exists(p)) return p;
            }
            catch { }
        }
        return null;
    }

    /// <summary>整目录复制（跳过 .git，避免把版本库塞进 mod）。</summary>
    private static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, f);
            if (rel.StartsWith(".git" + Path.DirectorySeparatorChar) || rel.StartsWith(".git/")) continue;
            var outPath = Path.Combine(dst, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            File.Copy(f, outPath, true);
        }
    }

    private static int CopyMiscFiles(string kristalDir, string modDir)
    {
        var outDir = Path.Combine(modDir, "files");
        int count = 0;
        foreach (var sub in new[] { "lang", "data", "fonts" })
        {
            var src = Path.Combine(kristalDir, sub);
            if (!Directory.Exists(src)) continue;
            foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(kristalDir, f);
                var dst = Path.Combine(outDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Copy(f, dst, true);
                count++;
            }
        }
        return count;
    }
}
