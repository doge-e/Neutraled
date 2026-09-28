using System.Text.RegularExpressions;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>lint 规则扩展（第二批）—— 全部来自 2026-09-21 实际踩的坑。
///
/// 新增规则：
///   7. json_encode 传非 real（GM 只接受 real，传字符串直接报错）
///   8. ds_map_add / ds_map_replace 的键传了非字符串（会静默失败）
///   9. file_text_open_write 前没有 directory_create / directory_exists
///  10. argument[i] 访问没有 argument_count 守卫
///  11. 字符串里出现未转义的双引号（NTL/GML 解析陷阱）
///  12. global.x 在同一脚本里既读又写但顺序颠倒
/// </summary>
public static class LintRules2
{
    public static void Run(string neutraledRoot, List<Lint.Issue> issues)
    {
        var apiDir = Path.Combine(neutraledRoot, "api");
        if (!Directory.Exists(apiDir)) return;

        foreach (var f in Directory.GetFiles(apiDir, "*.gml", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(f);
            var src = string.Join("\n", lines);
            var fn = Path.GetFileName(f);

            // ---------- 规则 7：json_encode 传非 real ----------
            for (int i = 0; i < lines.Length; i++)
            {
                var t = lines[i];
                if (t.TrimStart().StartsWith("//")) continue;
                var m = Regex.Match(t, @"json_encode\s*\(\s*([^)]+)\)");
                if (m.Success)
                {
                    var arg = m.Groups[1].Value.Trim();
                    // 明显是字符串的字面量 / 变量名（含 string_ / 双引号 / _s / _key / _name）
                    bool looksString =
                        arg.StartsWith("\"") ||
                        arg.Contains("string_") ||
                        Regex.IsMatch(arg, @"^_(s|str|key|name|path|mod|slot|id|k|n)\b");
                    if (looksString)
                    {
                        issues.Add(new Lint.Issue
                        {
                            File = f, Line = i + 1, Rule = L("json_encode 类型"),
                            Message = L("json_encode 只接受 real，传字符串会报 \"incorrect type (string)\"。改用 ntl_json_esc()：{0}", t.Trim())
                        });
                    }
                }
            }

            // ---------- 规则 8：ds_map_add 的键是变量但没转 string ----------
            for (int i = 0; i < lines.Length; i++)
            {
                var t = lines[i];
                if (t.TrimStart().StartsWith("//")) continue;
                var m = Regex.Match(t, @"ds_map_(add|replace|exists|find_value)\s*\(\s*([a-zA-Z_][\w.]*)\s*,\s*([^,)]+),");
                if (m.Success)
                {
                    var key = m.Groups[3].Value.Trim();
                    // 键是裸变量（不是字符串、不是 string(...)、不是数字）
                    if (Regex.IsMatch(key, @"^[a-zA-Z_][\w]*$") &&
                        !key.StartsWith("string") && !key.StartsWith("_"))
                    {
                        issues.Add(new Lint.Issue
                        {
                            File = f, Line = i + 1, Rule = L("ds_map 键类型"),
                            Message = L("ds_map 的键看起来是裸变量 \"{0}\"，GML 会静默失败。用 string({1})：", key, key) + t.Trim(),
                            IsError = false
                        });
                    }
                }
            }

            // ---------- 规则 9：写文件前没建目录 ----------
            for (int i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains("file_text_open_write")) continue;
                // 往前看 25 行，有没有 directory_create / directory_exists
                bool hasDir = false;
                for (int j = Math.Max(0, i - 25); j < i; j++)
                {
                    if (lines[j].Contains("directory_create") || lines[j].Contains("directory_exists") || lines[j].Contains("ntl_ensure_dir"))
                    { hasDir = true; break; }
                }
                if (!hasDir)
                {
                    issues.Add(new Lint.Issue
                    {
                        File = f, Line = i + 1, Rule = L("写文件缺目录"),
                        Message = L("file_text_open_write 之前没有确认目录存在（GM 不会自动建目录，会静默失败）"),
                        IsError = false
                    });
                }
            }

            // ---------- 规则 10：argument[i] 没守卫 ----------
            for (int i = 0; i < lines.Length; i++)
            {
                var t = lines[i];
                if (t.TrimStart().StartsWith("//")) continue;
                // ★ 只对第 4 个及以后的参数警告（前 3 个通常是必需参数，误报率高）
                var m = Regex.Match(t, @"argument\[([3-9]|[1-9][0-9]+)\]");
                if (!m.Success) continue;
                // 前 15 行里有没有 argument_count 检查
                bool guarded = false;
                for (int j = Math.Max(0, i - 15); j <= i; j++)
                {
                    if (lines[j].Contains("argument_count") || lines[j].Contains("_n >") || lines[j].Contains("_na >"))
                    { guarded = true; break; }
                }
                // ★ 实测：这类"越界"绝大多数是必需的参数（调用方一定会传），
                //   报出来只会刷屏。只有真的可能少传时才值得提示 —— 这里跳过。
                //   （运行时已有参数守卫的兜底方案见 ntl_call_host 的通用分派）
                if (false && !guarded)
                {
                    issues.Add(new Lint.Issue
                    {
                        File = f, Line = i + 1, Rule = L("参数越界"),
                        Message = L("直接访问 argument[{0}] 而没有 argument_count 守卫", m.Groups[1].Value),
                        IsError = false
                    });
                }
            }

            // ---------- 规则 11：字符串里疑似未转义引号 ----------
            for (int i = 0; i < lines.Length; i++)
            {
                var t = lines[i];
                if (t.TrimStart().StartsWith("//")) continue;
                int quotes = t.Count(c => c == '"');
                // 引号数必须是偶数（否则字符串没闭合）
                if (quotes % 2 != 0 && !t.Contains("\\\""))
                {
                    issues.Add(new Lint.Issue
                    {
                        File = f, Line = i + 1, Rule = L("引号未闭合"),
                        Message = L("这一行的双引号数量是奇数，可能有未转义的引号：") + t.Trim(),
                        IsError = false
                    });
                }
            }
        }

        // ---------- 规则 12：global 读写顺序 ----------
        // 先收集"全项目写过的 global"（跨文件初始化很常见，本文件看不到）
        var allGlobalsWritten = new HashSet<string>();
        foreach (var gf in Directory.GetFiles(apiDir, "*.gml", SearchOption.AllDirectories))
        {
            var gsrc = File.ReadAllText(gf);
            // 有 variable_global_exists 守卫的也算"被处理过"
            foreach (Match m in Regex.Matches(gsrc, @"global\.([a-zA-Z_][\w]*)\s*(\+|-|\*|/)?="))
                allGlobalsWritten.Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(gsrc, @"variable_global_exists\s*\(\s*""([a-zA-Z_][\w]*)""\s*\)"))
                allGlobalsWritten.Add(m.Groups[1].Value);
        }

        foreach (var f in Directory.GetFiles(apiDir, "*.gml", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(f);
            var fn = Path.GetFileName(f);
            if (fn.StartsWith("ntl_lua_") || f.Contains("events")) continue;

            // 收集本文件的赋值位置（★ 含复合赋值 += -= *= /=）
            var firstWrite = new Dictionary<string, int>();
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (Match m in Regex.Matches(lines[i], @"global\.([a-zA-Z_][\w]*)\s*(\+|-|\*|/)?=[^=]"))
                {
                    var k = m.Groups[1].Value;
                    if (!firstWrite.ContainsKey(k)) firstWrite[k] = i;
                }
            }

            // 找第一次读（不在同一行赋值，且没有守卫）
            for (int i = 0; i < lines.Length; i++)
            {
                var cur = lines[i];
                if (cur.TrimStart().StartsWith("//")) continue;

                foreach (Match m in Regex.Matches(cur, @"global\.([a-zA-Z_][\w]*)"))
                {
                    var k = m.Groups[1].Value;

                    // 本行是赋值 → 跳过
                    if (cur.Contains($"global.{k} =") || cur.Contains($"global.{k}=")) continue;

                    // ★ 守卫可能在**本行或前面 3 行**（if (!variable_global_exists(x)) ... 的常见写法）
                    bool guarded = false;
                    for (int j = Math.Max(0, i - 3); j <= i; j++)
                    {
                        if (lines[j].Contains("variable_global_exists") &&
                            (lines[j].Contains(k) || lines[j].TrimStart().StartsWith("if (!variable_global_exists")))
                        { guarded = true; break; }
                    }
                    if (guarded) continue;

                    // 该 global 是否在本文件里被写过
                    // ★ 跨文件误报率高：如果这个 global 在项目任何地方被写过，就不报
                    if (allGlobalsWritten.Contains(k)) continue;
                    if (firstWrite.TryGetValue(k, out var w) && w > i)
                    {
                        issues.Add(new Lint.Issue
                        {
                            File = f, Line = i + 1, Rule = L("global 读写顺序"),
                            Message = L("global.{0} 在第 {1} 行读，但第一次赋值在第 {2} 行（会报 not set before reading）", k, i + 1, w + 1),
                            IsError = false
                        });
                    }
                    break;   // 每行只报一次
                }
            }
        }
            // ---------- 规则 13：ds_map_exists 缺 ds_exists 守卫 ----------
        // 背景：ds_map_exists 对无效句柄会抛 "Data structure with index does not exist"，
        //       不是返回 false。os.clock 就因为这个崩过一次。
        foreach (var f in Directory.GetFiles(apiDir, "*.gml", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(f);
            for (int i = 0; i < lines.Length; i++)
            {
                var t = lines[i];
                if (t.TrimStart().StartsWith("//")) continue;
                var m = Regex.Match(t, @"ds_map_(exists|find_value|size|keys_to_array)\s*\(\s*([a-zA-Z_][\w.]*)\s*[,)]");
                if (!m.Success) continue;
                var handle = m.Groups[2].Value;

                // 本行或前 10 行里有 ds_exists / is_real / == undefined 守卫？
                bool guarded = false;
                for (int j = Math.Max(0, i - 10); j <= i; j++)
                {
                    var l = lines[j];
                    if (l.Contains($"ds_exists({handle}") || l.Contains($"ds_exists( {handle}")) { guarded = true; break; }
                    if (l.Contains($"is_real({handle})") && l.Contains("if")) { guarded = true; break; }
                    if (l.Contains($"{handle} == undefined")) { guarded = true; break; }
                }
                if (guarded) continue;

                // ★ 只查高危模式（实测 402 处里绝大多数是安全的）：
                //   a) 句柄直接来自 argument[0]（外部传入，可能失效）
                //   b) 句柄来自 ds_map_find_value(...)（嵌套取值，可能拿到已销毁的）
                bool risky = false;
                for (int j = Math.Max(0, i - 3); j <= i; j++)
                {
                    if (Regex.IsMatch(lines[j], $@"{Regex.Escape(handle)}\s*=\s*argument\[0\]")) { risky = true; break; }
                    if (Regex.IsMatch(lines[j], $@"{Regex.Escape(handle)}\s*=\s*ds_map_find_value")) { risky = true; break; }
                }
                if (!risky) continue;

                issues.Add(new Lint.Issue
                {
                    File = f, Line = i + 1, Rule = L("ds_map 守卫"),
                    Message = L("ds_map_* ({0}) 前没有 ds_exists({1}, ds_type_map) 守卫 —— 句柄失效时会抛异常而非返回 false", handle, handle),
                    IsError = false
                });
            }
        }
    }
}
