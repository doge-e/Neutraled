using System.Text;
using System.Text.RegularExpressions;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>静态检查 —— 把反复踩到的坑自动化检测出来。
///
/// 本项目实际踩过的坑（每个都浪费了大量调试时间）：
///   1. **脚本文件名 ≠ 函数名** → 跨脚本调用被编译成变量访问 → 静默失败（踩了 3 次！）
///   2. **漏 global. 前缀** → 写成实例变量、下一行读 global → "not set before reading it"
///   3. **global.X 被读但从未被任何地方初始化** → 运行时报错
///   4. **ds_map 函数传非 real 句柄** → Code Error
///   5. **空数组取 [0]** → Execution Error
///   6. **字符串里含 `===`** → NTL tokenizer 出错
///   7. **NTL 用了 for / var / continue** → 语法错误
/// </summary>
public static class Lint
{
    public sealed class Issue
    {
        public string File = "";
        public int Line;
        public string Rule = "";
        public string Message = "";
        public bool IsError = true;
        public override string ToString() =>
            L("{0} {1}:{2}  {3}\n         {4}", (IsError ? L("[错误]") : L("[警告]")), Path.GetFileName(File), Line, Rule, Message);
    }

    public static List<Issue> Run(string neutraledRoot)
    {
        var issues = new List<Issue>();
        var apiDir = Path.Combine(neutraledRoot, "api");
        var liveDir = Path.Combine(neutraledRoot, "live");
        var modsDir = Path.Combine(neutraledRoot, "mods");

        // ---------- 规则 1：api/*.gml 文件名必须等于函数名 ----------
        if (Directory.Exists(apiDir))
        {
            foreach (var f in Directory.GetFiles(apiDir, "*.gml", SearchOption.AllDirectories))
            {
                var baseName = Path.GetFileNameWithoutExtension(f);
                if (baseName.StartsWith("_")) continue;                   // 下划线开头是内部文件
                if (f.Contains(Path.Combine("api", "events"))) continue;  // events/ 是事件代码，不用函数名

                var firstLines = File.ReadAllLines(f).Take(12).ToArray();
                var m = firstLines.Select(l => Regex.Match(l, @"^///?\s*([A-Za-z_][A-Za-z0-9_]*)\s*\("))
                                  .FirstOrDefault(x => x.Success);
                if (m == null) continue;   // 没有函数注释，跳过
                var fn = m.Groups[1].Value;
                if (fn != baseName)
                {
                    issues.Add(new Issue
                    {
                        File = f, Line = 1, Rule = L("文件名≠函数名"),
                        Message = L("文件 '{0}.gml' 注释里声明的是 '{1}()'。\n", baseName, fn) +
                                  L("        → 跨脚本调用会被编译成变量访问，**静默失败**（不报错但函数不执行）\n") +
                                  L("        → 修复：把文件重命名为 '{0}.gml'", fn)
                    });
                }
            }
        }

        // ---------- 规则 2/3：漏 global. 前缀 + 未初始化的 global ----------
        var gmlFiles = new List<string>();
        if (Directory.Exists(apiDir)) gmlFiles.AddRange(Directory.GetFiles(apiDir, "*.gml", SearchOption.AllDirectories));
        if (Directory.Exists(liveDir)) gmlFiles.AddRange(Directory.GetFiles(liveDir, "*.gml", SearchOption.AllDirectories));

        var initialized = new HashSet<string>(StringComparer.Ordinal);
        var readOnlyCheck = new List<(string file, int line, string name)>();

        foreach (var f in gmlFiles)
        {
            var lines = File.ReadAllLines(f);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var code = StripComment(line);

                // 收集初始化：global.X = / variable_global_set("X") / !variable_global_exists("X")
                foreach (Match m in Regex.Matches(code, @"global\.([A-Za-z_][A-Za-z0-9_]*)\s*="))
                    initialized.Add(m.Groups[1].Value);
                foreach (Match m in Regex.Matches(code, @"variable_global_exists\s*\(\s*""([A-Za-z_][A-Za-z0-9_]*)""\s*\)"))
                    initialized.Add(m.Groups[1].Value);
                foreach (Match m in Regex.Matches(code, @"variable_global_set\s*\(\s*""([A-Za-z_][A-Za-z0-9_]*)"""))
                    initialized.Add(m.Groups[1].Value);

                // 收集读取（try{} 包裹的算安全，跳过）
                var inTry = code.Contains("try") && code.Contains("catch");
                foreach (Match m in Regex.Matches(code, @"global\.([A-Za-z_][A-Za-z0-9_]*)"))
                {
                    if (inTry) continue;   // try/catch 保护过的读取不算问题
                    readOnlyCheck.Add((f, i + 1, m.Groups[1].Value));
                }

                // 规则 2：裸变量赋值（疑似漏 global.）
                // 形如 "    ntl_xxx = 0;"（行首缩进 + 标识符 = 值，且不是 var 声明）
                var bare = Regex.Match(code, @"^\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*[^=]");
                if (bare.Success && !GmBuiltins.Contains(bare.Groups[1].Value))
                {
                    var nm = bare.Groups[1].Value;
                    // 排除明显的局部变量（已在同文件用 var 声明的）
                    var isLocal = Regex.IsMatch(string.Join("\n", lines), $@"\bvar\s+{Regex.Escape(nm)}\b");
                    if (!isLocal && !code.TrimStart().StartsWith("//"))
                    {
                        issues.Add(new Issue
                        {
                            File = f, Line = i + 1, Rule = L("疑似漏 global. 前缀"), IsError = false,
                            Message = L("'{0} = ...' 没有 var 也没有 global. → 会写成**实例变量**\n", nm) +
                                      L("        → 若下一行读 'global.{0}' 会报 'not set before reading it'\n", nm) +
                                      L("        → 修复：改成 'global.{0} = ...' 或 'var {1} = ...'", nm, nm)
                        });
                    }
                }
            }
        }

        // 规则 3：读了但从没初始化过的 global
        foreach (var (file, line, name) in readOnlyCheck)
        {
            if (name == "ntl_" || name.Length < 3) continue;
            if (!initialized.Contains(name) && !name.StartsWith("flag"))
            {
                issues.Add(new Issue
                {
                    File = file, Line = line, Rule = L("global 变量从未初始化"),
                    Message = L("读取了 'global.{0}'，但整个 api/live 里没有任何地方初始化它\n", name) +
                              L("        → 运行时会报 'global variable name \'{0}\' ... not set before reading it'", name)
                });
            }
        }

        // ---------- 规则 4：ds_map 函数传非 real ----------
        foreach (var f in gmlFiles)
        {
            var lines = File.ReadAllLines(f);
            for (int i = 0; i < lines.Length; i++)
            {
                var code = StripComment(lines[i]);
                if (Regex.IsMatch(code, @"ds_map_(exists|find_value|size|keys_to_array|clear|destroy)\s*\(\s*"""))
                {
                    issues.Add(new Issue
                    {
                        File = f, Line = i + 1, Rule = L("ds_map 传了字符串句柄"),
                        Message = L("ds_map_* 的第一个参数必须是 real 句柄；传字符串会直接 Code Error\n") +
                                  L("        → 修复：先 is_real() 守卫，或改用登记表判定")
                    });
                }
            }
        }

        // ---------- 规则 5：live/*.ntl 的语法雷区 ----------
        if (Directory.Exists(liveDir))
        {
            foreach (var f in Directory.GetFiles(liveDir, "*.ntl", SearchOption.AllDirectories))
            {
                var lines = File.ReadAllLines(f);
                for (int i = 0; i < lines.Length; i++)
                {
                    var code = StripComment(lines[i]);
                    if (Regex.IsMatch(code, @"\bfor\s*\("))
                        issues.Add(new Issue { File = f, Line = i + 1, Rule = L("NTL 不支持 for"), Message = L("NTL 只有 while 循环，请改用 while") });
                    if (Regex.IsMatch(code, @"(^|[^.\w])var\s+[A-Za-z_]"))
                        issues.Add(new Issue { File = f, Line = i + 1, Rule = L("NTL 不支持 var"), Message = L("NTL 用 'let x = 1;' 而不是 var") });
                    if (Regex.IsMatch(code, @"\bcontinue\s*;"))
                        issues.Add(new Issue { File = f, Line = i + 1, Rule = L("NTL 不支持 continue"), Message = L("用 if 包裹代替 continue") });
                    if (Regex.IsMatch(code, @"\[[0-9]+\]\s*[=;.)]") )
                        issues.Add(new Issue { File = f, Line = i + 1, Rule = L("NTL 不支持数组下标"), Message = L("用 arr_get(a, i) / arr_set(a, i, v)") });
                    var strMatch = Regex.Match(code, @"""[^""]*===[^""]*""");
                    if (strMatch.Success)
                        issues.Add(new Issue { File = f, Line = i + 1, Rule = L("字符串含 ==="), Message = L("=== 会破坏 NTL tokenizer，请改用 [标记] 之类") });
                }
            }
        }

        // ---------- 规则 6：mod.json 必需字段 ----------
        if (Directory.Exists(modsDir))
        {
            foreach (var mj in Directory.GetFiles(modsDir, "mod.json", SearchOption.AllDirectories))
            {
                try
                {
                    var text = File.ReadAllText(mj);
                    foreach (var req in new[] { "\"id\"", "\"name\"" })
                        if (!text.Contains(req))
                            issues.Add(new Issue
                            {
                                File = mj, Line = 1, Rule = L("mod.json 缺字段"), IsError = false,
                                Message = L("缺少 {0} 字段（建议补上，便于依赖引用与显示）", req)
                            });
                }
                catch { }
            }
        }

                // ---------- 第二批规则（LintRules2）----------
        LintRules2.Run(neutraledRoot, issues);

        // ---------- 第三批规则（LintRules3：文案防回归）----------
        LintRules3.Run(neutraledRoot, issues);

        // ---------- 第四批规则（LintRules4：本地变量必须先声明）----------
        LintRules4.Run(neutraledRoot, issues);

        // ---------- 第五批规则（LintRules5：资源索引不许写死）----------
        LintRules5.Run(neutraledRoot, issues);

return issues;
    }

    /// <summary>GML 内置变量（可裸赋值，不是"漏 global."）</summary>
    private static readonly HashSet<string> GmBuiltins = new(StringComparer.Ordinal)
    {
        "keyboard_string", "keyboard_lastchar", "keyboard_lastkey", "keyboard_key",
        "room", "room_speed", "room_width", "room_height",
        "x", "y", "xstart", "ystart", "xprevious", "yprevious",
        "hspeed", "vspeed", "speed", "direction", "gravity", "gravity_direction",
        "friction", "image_index", "image_speed", "image_alpha", "image_angle",
        "image_xscale", "image_yscale", "image_blend", "depth", "visible", "solid",
        "persistent", "alarm", "sprite_index", "mask_index", "path_index",
        "health", "lives", "score", "lives", "object_index", "id",
        "width", "height", "bbox_left", "bbox_right", "bbox_top", "bbox_bottom",
        "mouse_x", "mouse_y", "mouse_button", "view_xview", "view_yview",
        "current_time", "delta_time", "fps", "fps_real", "application_surface",
        "argument", "argument0", "argument1", "argument2", "argument3", "argument4",
        "argument5", "argument6", "argument7", "argument8", "argument9",
        "argument10", "argument11", "argument12", "argument13", "argument14", "argument15",
        "argument_count", "result", "self", "other", "all", "noone", "global",
        "working_directory", "program_directory", "game_save_id", "temp_directory",
        "_x", "_y", "_w", "_h", "_i", "_n", "_k", "_v", "_s", "_t", "_e", "_c", "_r", "_l", "_p"
    };

    private static string StripComment(string line)
    {
        var i = line.IndexOf("//", StringComparison.Ordinal);
        return i >= 0 ? line.Substring(0, i) : line;
    }

    /// <summary>命令行入口：返回退出码（0 = 无错误）</summary>
    public static int RunCli(string neutraledRoot)
    {
        Console.WriteLine(L("===== Neutraled 静态检查 (lint) ====="));
        Console.WriteLine(L("  扫描: {0}", neutraledRoot));
        var issues = Run(neutraledRoot);
        var errors = issues.Where(x => x.IsError).ToList();
        var warns = issues.Where(x => !x.IsError).ToList();

        if (issues.Count == 0)
        {
            Console.WriteLine(L("  ✅ 没有发现问题"));
            return 0;
        }

        if (errors.Count > 0)
        {
            Console.WriteLine(L("\n--- 错误 {0} 个（必须修）---", errors.Count));
            foreach (var e in errors) Console.WriteLine("  " + e);
        }
        if (warns.Count > 0)
        {
            Console.WriteLine(L("\n--- 警告 {0} 个（建议修）---", warns.Count));
            foreach (var w in warns) Console.WriteLine("  " + w);
        }
        Console.WriteLine(L("\n合计: {0} 错误 / {1} 警告", errors.Count, warns.Count));
        return errors.Count > 0 ? 1 : 0;
    }
}
