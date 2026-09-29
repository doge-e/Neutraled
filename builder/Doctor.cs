using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>Neutraled 自检工具（--doctor）
///
/// 存在的理由：Neutraled 的故障模式主要是「静默失效」—— 不报错、不崩溃，
/// 但功能就是不工作。2026-09-21 一天踩了 12 个 bug，其中 9 个是这类，
/// 平均每个要花 20+ 轮排查。这个工具把那些排查压缩到 10 秒。
///
/// 检查项：
///   1. 宿主函数完整性（注册了 __x 但 ntl_lua_host 里没有分支）
///   2. 脚本编译完整性（文件名 = 函数名；是否都进了 data.win）
///   3. 不可达代码（return 之后还有代码）
///   4. 未定义变量（老式脚本里用了没声明的 _x）
///   5. 部署目标一致性（root 与 chapterN 的时间戳差异）
///   6. mod 加载验证（脚本能否被发现）
///   7. 空 catch 块（异常被静默吞掉）
/// </summary>
public static class Doctor
{
    public sealed class Issue
    {
        public string Level = "warn";   // error / warn / info
        public string Check = "";
        public string Detail = "";
        public string Hint = "";
    }

    public sealed class Report
    {
        public List<Issue> Issues = new();
        public int ChecksRun = 0;
        public int Passed = 0;

        public void Add(string level, string check, string detail, string hint = "")
            => Issues.Add(new Issue { Level = level, Check = check, Detail = detail, Hint = hint });

        public int Count(string level) => Issues.Count(i => i.Level == level);
    }

    public static Report Run(string gameRoot)
    {
        var rep = new Report();
        var ntl = Paths.NeutraledRoot(gameRoot);
        var api = Path.Combine(ntl, "api");

        // 没有 api/ 时不要崩栈（--game 指到空目录/半装环境时医生该报错，不该抛异常）。
        // 2026-09-29 实测：api/ 不存在时 Doctor.CheckScripts 会在 Directory.GetFiles 抛
        // DirectoryNotFoundException 并打印调用栈、exit 1。脚本类检查各自跳过，配置/部署检查照跑。
        if (!Directory.Exists(api))
            rep.Add("error", L("安装"), L("找不到 api/ 目录: {0}", api),
                    L("--doctor 应在已安装 Neutraled 的游戏根里运行；缺少 api/ 时脚本类检查会跳过。"));

        Console.WriteLine(L("===== Neutraled 自检 (doctor) ====="));
        Console.WriteLine();

        // ---------- 1. 宿主函数完整性 ----------
        rep.ChecksRun++;
        Console.WriteLine(L("[1/8] 宿主函数完整性..."));
        CheckHostFunctions(api, rep);

        // ---------- 2. 脚本编译完整性 ----------
        rep.ChecksRun++;
        Console.WriteLine(L("[2/8] 脚本完整性..."));
        CheckScripts(api, rep);

        // ---------- 3. 不可达代码 ----------
        rep.ChecksRun++;
        Console.WriteLine(L("[3/8] 不可达代码..."));
        CheckUnreachable(api, rep);

        // ---------- 4. 未定义变量 ----------
        rep.ChecksRun++;
        Console.WriteLine(L("[4/8] 变量定义..."));
        CheckVariables(api, rep);

        // ---------- 5. 部署目标一致性 ----------
        rep.ChecksRun++;
        Console.WriteLine(L("[5/8] 部署目标一致性..."));
        CheckDeployTargets(gameRoot, rep);

        // ---------- 6. config.json 完整性 ----------
        rep.ChecksRun++;
        Console.WriteLine(L("[6/8] config.json 完整性..."));
        CheckConfigJson(gameRoot, rep);

        // ---------- 7. mod 加载验证 ----------
        rep.ChecksRun++;
        Console.WriteLine(L("[7/8] mod 加载..."));
        CheckMods(ntl, rep);

        // ---------- 7. 空 catch ----------
        rep.ChecksRun++;
        Console.WriteLine(L("[8/8] 异常处理..."));
        CheckEmptyCatch(api, rep);

        // ---------- 报告 ----------
        Console.WriteLine();
        Console.WriteLine(L("===== 结果 ====="));
        if (rep.Issues.Count == 0)
        {
            Console.WriteLine(L("  ✅ 全部检查通过，没有发现问题"));
            return rep;
        }

        foreach (var lvl in new[] { "error", "warn", "info" })
        {
            var list = rep.Issues.Where(i => i.Level == lvl).ToList();
            if (list.Count == 0) continue;
            var tag = lvl == "error" ? L("❌ 错误") : lvl == "warn" ? L("⚠️ 警告") : L("ℹ️ 提示");
            Console.WriteLine();
            Console.WriteLine(L("  {0}（{1}）:", tag, list.Count));
            foreach (var i in list)
            {
                Console.WriteLine($"    [{i.Check}] {i.Detail}");
                if (i.Hint.Length > 0) Console.WriteLine($"        → {i.Hint}");
            }
        }

        Console.WriteLine();
        Console.WriteLine(L("  统计: {0} 错误 / {1} 警告 / {2} 提示", rep.Count("error"), rep.Count("warn"), rep.Count("info")));
        return rep;
    }

    // ============ 1. 宿主函数完整性 ============
    private static void CheckHostFunctions(string apiDir, Report rep)
    {
        if (!Directory.Exists(apiDir)) return;   // 没有 api/ 由 Run 统一报一次错，这里静默跳过
        var hostFile = Path.Combine(apiDir, "ntl_call_host.gml");
        var luaHostFile = Path.Combine(apiDir, "ntl_lua_host.gml");
        if (!File.Exists(hostFile)) { rep.Add("error", L("宿主"), L("找不到 ntl_call_host.gml")); return; }

        var callHost = File.ReadAllText(hostFile);
        var luaHost = File.Exists(luaHostFile) ? File.ReadAllText(luaHostFile) : "";

        // 1) 收集所有 ntl_lua_fn_host("__x") 注册的名字
        var registered = new HashSet<string>();
        foreach (var f in Directory.GetFiles(apiDir, "*.gml"))
        {
            var src = File.ReadAllText(f);
            foreach (Match m in Regex.Matches(src, @"ntl_lua_fn_host\s*\(\s*""([^""]+)"""))
                registered.Add(m.Groups[1].Value);
        }

        // 2) 收集 ntl_lua_host 里实现的分支（__xxx 字面量 + 前缀判断）
        var implemented = new HashSet<string>();
        foreach (Match m in Regex.Matches(luaHost, @"""(__[a-z0-9_]+)"""))
            implemented.Add(m.Groups[1].Value);
        // 前缀式实现（string_copy(_name, 1, N) == "__pfx_"）
        var prefixes = new List<string>();
        foreach (Match m in Regex.Matches(luaHost, @"==\s*""(__[a-z0-9]+_)"""))
            prefixes.Add(m.Groups[1].Value);

        var missing = new List<string>();
        foreach (var name in registered)
        {
            if (implemented.Contains(name)) continue;
            if (prefixes.Any(p => name.StartsWith(p))) continue;
            if (callHost.Contains(name)) continue;
            // ★ 真函数名（如 ntl_screenshot）：ntl_lua_host 有通用兜底用 asset_get_index 找脚本
            var scriptFile = Path.Combine(apiDir, name + ".gml");
            if (File.Exists(scriptFile)) continue;
            if (name.StartsWith("__co_")) continue;   // coroutine 在 ntl_call_host 里分派
            if (name.StartsWith("__loaded_")) continue;
            missing.Add(name);
        }

        if (missing.Count > 0)
        {
            rep.Add("error", L("宿主"), L("注册但无实现: {0}", string.Join(", ", missing.Take(10))) +
                    (missing.Count > 10 ? L(" 等 {0} 个", missing.Count) : ""),
                    L("这些调用会静默返回 undefined。在 ntl_lua_host 或 ntl_call_host 里补分支。"));
        }
        else
        {
            rep.Add("info", L("宿主"), L("✅ {0} 个宿主函数都有对应实现", registered.Count));
        }
    }

    // ============ 2. 脚本完整性 ============
    private static void CheckScripts(string apiDir, Report rep)
    {
        if (!Directory.Exists(apiDir)) return;   // 没有 api/ 由 Run 统一报一次错，这里静默跳过
        var files = Directory.GetFiles(apiDir, "*.gml", SearchOption.AllDirectories);
        int mismatch = 0;
        var examples = new List<string>();

        foreach (var f in files)
        {
            var baseName = Path.GetFileNameWithoutExtension(f);
            // events/ 目录里的不是"函数脚本"
            if (f.Contains($"{Path.DirectorySeparatorChar}events{Path.DirectorySeparatorChar}")) continue;

            // ★ 真问题检测：脚本里应该**定义**同名函数（或它是事件/引导脚本）
            var src = File.ReadAllText(f);
            // 引导脚本（scr_ntl_init 之类由 builder prepend 到事件里）不算
            if (baseName.StartsWith("scr_")) continue;
            // 老式脚本必须出现 "function <name>" 或注释里提到（宽松）
            bool mentions = src.Contains(baseName);
            if (!mentions)
            {
                mismatch++;
                if (examples.Count < 8) examples.Add(baseName);
            }
        }

        if (mismatch > 0)
            rep.Add("warn", L("脚本"), L("{0} 个脚本的文件头没提到自己的函数名: {1}", mismatch, string.Join(", ", examples)),
                    L("GML 要求文件名 == 函数名，否则跨脚本调用会静默失败。"));
        else
            rep.Add("info", L("脚本"), L("✅ {0} 个脚本文件名规范", files.Length));
    }

    // ============ 3. 不可达代码 ============
    private static void CheckUnreachable(string apiDir, Report rep)
    {
        if (!Directory.Exists(apiDir)) return;   // 没有 api/ 由 Run 统一报一次错，这里静默跳过
        var hits = new List<string>();
        foreach (var f in Directory.GetFiles(apiDir, "*.gml", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(f);
            // 只看"老式脚本"（顶层 return 会终止整个脚本）
            var lines = File.ReadAllLines(f);
            int depth = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                var t = lines[i].Trim();
                if (t.StartsWith("//")) continue;

                // ★ 只有 depth == 0 且 return 在行首才算（缩进的 return 在 if/for 里）
                bool isTopReturn = depth == 0
                                    && Regex.IsMatch(lines[i], @"^return\b")   // 必须是行首（无缩进）
                                    && !Regex.IsMatch(lines[i], @"^\s+");
                if (isTopReturn)
                {
                    int rest = 0;
                    for (int j = i + 1; j < lines.Length; j++)
                    {
                        var r = lines[j].Trim();
                        if (r.Length == 0 || r.StartsWith("//")) continue;
                        rest++;
                    }
                    if (rest > 0)
                        hits.Add(L("{0}:{1} 之后还有 {2} 行", name, i + 1, rest));
                    break;
                }
                depth += t.Count(c => c == '{') - t.Count(c => c == '}');
                if (depth < 0) depth = 0;
            }
        }

        if (hits.Count > 0)
            rep.Add("warn", L("不可达"), L("{0} 处顶层 return 之后还有代码: {1}", hits.Count, string.Join("; ", hits.Take(5))),
                    L("顶层 return 会终止整个脚本，后面的注册代码永不执行（stdlib2 就在这踩过）。"));
        else
            rep.Add("info", L("不可达"), L("✅ 没有发现 return 后代码"));
    }

    // ============ 4. 变量定义 ============
    private static void CheckVariables(string apiDir, Report rep)
    {
        if (!Directory.Exists(apiDir)) return;   // 没有 api/ 由 Run 统一报一次错，这里静默跳过
        var hits = new List<string>();
        foreach (var f in Directory.GetFiles(apiDir, "*.gml", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(f);
            var src = File.ReadAllText(f);

            // 未声明的局部变量（2026-09-27 起交给 lint 规则 16 的同一套扫描）：
            //   _k 漏了 var _k = argument[0] → 真机一进 CONFIG 页就 Code Error；
            //   _a4 漏了 var _a4 = … → 任何 mod 调 __love_gfx_rectangle 都会炸。
            foreach (var (nm, line) in LintRules4.ScanFile(f))
                hits.Add(L("{0}:{1} 用了未声明的 {2}", name, line, nm));
        }

        if (hits.Count > 0)
            rep.Add("error", L("变量"), L("{0} 处未定义变量: {1}", hits.Count, string.Join("; ", hits.Take(5))),
                    L("GML 不报编译错，运行时才炸（还被崩溃隔离吞掉）。"));
        else
            rep.Add("info", L("变量"), L("✅ 未发现明显的未定义变量"));
    }

    // ============ 5. 部署目标一致性 ============
    private static void CheckDeployTargets(string gameRoot, Report rep)
    {
        var rootWin = Path.Combine(gameRoot, "data.win");
        var pairs = new[] { "chapter1", "chapter2", "chapter3", "chapter4", "chapter5" };

        if (!File.Exists(rootWin))
        {
            rep.Add("warn", L("部署"), L("root/data.win 不存在"), L("先运行 --deploy --chapter root"));
            return;
        }

        var rootTime = File.GetLastWriteTime(rootWin);
        var stale = new List<string>();
        foreach (var ch in pairs)
        {
            var p = Path.Combine(gameRoot, ch + "_" + Paths.ChapterSuffix(gameRoot), "data.win");
            if (!File.Exists(p)) continue;
            var t = File.GetLastWriteTime(p);
            var days = Math.Abs((rootTime - t).TotalHours);
            if (days > 48)   // 超过 2 天才提示
                stale.Add(L("{0}（差 {1:F0} 小时）", ch, days));
        }

        if (stale.Count > 0)
            rep.Add("warn", L("部署"), L("root 与章节的时间差较大: {0}", string.Join(", ", stale)),
                    L("如果游戏停在章节选择器，跑的是 root 的代码。部署时务必两个目标都做。"));
        else
            rep.Add("info", L("部署"), L("✅ root 与章节部署时间接近"));
    }

    // ============ 6. config.json 完整性 ============
    /// <summary>重复键检测：JsonObject 是字典，重复键会让**所有**读配置的命令在启动时抛
    /// 「An item with the same key has already been added」（实测 --plugin-hooks / --lang-coverage /
    /// --plugin-list 全崩；--lint / --version 不读配置所以看着正常）。
    /// 成因见 api/ntl_config_set_lang.gml：旧判据「替换后文本没变 = 没有 lang 字段」，
    /// 当要写的语言码与现值相同时会追加第二个 "lang"。ConfigFile.Load 现在会自动清理并写回，
    /// 这里只做「有没有发生过」的体检（出现即 warn，提示成因与自愈行为）。</summary>
    private static void CheckConfigJson(string gameRoot, Report rep)
    {
        var p = Paths.ConfigPath(gameRoot);
        if (!File.Exists(p)) { rep.Add("info", L("配置"), L("没有 config.json（全部用默认值）")); return; }
        try
        {
            // 口径必须与 ConfigFile.ConvertText 一致（容忍尾逗号与 // 注释），
            // 否则手工整理的合法配置会被 --doctor 判成硬错（exit 1）——独立复核发现的 high。
            using var doc = JsonDocument.Parse(File.ReadAllText(p), new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
            var dups = new List<string>();
            FindDupKeys(doc.RootElement, dups, "");
            if (dups.Count > 0)
                rep.Add("warn", L("配置"), L("config.json 有重复键: {0}", string.Join(", ", dups.Take(5))),
                        L("读取时按「后者覆盖」自动清理并写回；成因是旧版 api/ntl_config_set_lang.gml 的判据缺陷。"));
            else
                rep.Add("info", L("配置"), L("✅ config.json 键唯一"));
        }
        catch (Exception ex) { rep.Add("error", L("配置"), L("config.json 解析失败: ") + ex.Message); }
    }

    /// <summary>递归找同层重复键（JsonDocument 允许重复键，所以能读出来体检）。</summary>
    private static void FindDupKeys(JsonElement el, List<string> dups, string path)
    {
        if (el.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var prop in el.EnumerateObject())
            {
                if (!seen.Add(prop.Name)) dups.Add(path + prop.Name);
                FindDupKeys(prop.Value, dups, path + prop.Name + ".");
            }
        }
        else if (el.ValueKind == JsonValueKind.Array)
        {
            int i = 0;
            foreach (var item in el.EnumerateArray()) FindDupKeys(item, dups, path + i++ + ".");
        }
    }

    // ============ 6. mod 加载 ============
    private static void CheckMods(string ntlRoot, Report rep)
    {
        var modsRoot = Path.Combine(ntlRoot, "mods");
        if (!Directory.Exists(modsRoot))
        {
            rep.Add("info", "mod", L("没有 mods/ 目录"));
            return;
        }

        var jsonFiles = Directory.GetFiles(modsRoot, "mod.json", SearchOption.AllDirectories);
        int withScripts = 0, missingScript = 0;
        var missingList = new List<string>();

        foreach (var mj in jsonFiles)
        {
            var dir = Path.GetDirectoryName(mj)!;
            var txt = File.ReadAllText(mj);

            if (!txt.Contains("\"scripts\"")) continue;
            withScripts++;

            // 简易检查：scripts 里提到的 .lua 文件是否存在
            foreach (Match m in Regex.Matches(txt, @"""[^""]*\.lua"""))
            {
                var rel = m.Value.Trim('"').Replace("\\\\", "\\");
                if (!File.Exists(Path.Combine(dir, rel)))
                {
                    missingScript++;
                    if (missingList.Count < 5) missingList.Add(Path.GetFileName(dir) + "/" + rel);
                }
            }
        }

        if (missingScript > 0)
            rep.Add("warn", "mod", L("{0} 个脚本文件缺失: {1}", missingScript, string.Join(", ", missingList)),
                    L("mod 的 main.lua 不会被执行。"));
        else
            rep.Add("info", "mod", L("✅ {0} 个 mod，{1} 个有脚本，文件都存在", jsonFiles.Length, withScripts));
    }

    // ============ 7. 空 catch ============
    private static void CheckEmptyCatch(string apiDir, Report rep)
    {
        if (!Directory.Exists(apiDir)) return;   // 没有 api/ 由 Run 统一报一次错，这里静默跳过
        int empty = 0;
        var examples = new List<string>();

        foreach (var f in Directory.GetFiles(apiDir, "*.gml", SearchOption.AllDirectories))
        {
            var src = File.ReadAllText(f);
            // 匹配 catch (e) { } 或 catch (e) { } 之间只有空白
            foreach (Match m in Regex.Matches(src, @"catch\s*\([^)]*\)\s*\{\s*\}"))
            {
                empty++;
                if (examples.Count < 5) examples.Add(Path.GetFileName(f));
            }
        }

        if (empty > 0)
            rep.Add("warn", L("异常"), L("{0} 处空 catch 块: {1}", empty, string.Join(", ", examples)),
                    L("异常被静默吞掉，功能会「不报错但不工作」（json_encode 那个坑）。加一行 ntl_log。"));
        else
            rep.Add("info", L("异常"), L("✅ 没有空 catch 块"));
    }
}
