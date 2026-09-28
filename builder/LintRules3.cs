using System.Text;
using System.Text.RegularExpressions;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>lint 规则扩展（第三批，2026-09-26 第十一批 i18n）—— 文案防回归。
///
/// 新增规则：
///  13. 用户可见的控制台输出里硬编码 CJK 字面量（应改用 ntl_t("键") / ntl_ts("键", [...])）
///  14. ntl_t/ntl_ts/ntl_tf("键") 引用的键在文案表里不存在（运行时只会显示键名本身）
///  15. 文案表 zh / en 键集不对称、值为空、或占位符 {n} 不一致
///
/// 例外：语句所在行含标记 ntl:i18n-exempt 时跳过（用于故意中英并排的语句）。
/// 有意保持中文的部分（ntl_log 开发日志、注入产物、生成文档）本规则不覆盖。
/// </summary>
public static class LintRules3
{
    private const string Exempt = "ntl:i18n-exempt";
    private static readonly string[] ConsoleFns =
        { "ntl_console_log", "ntl_console_write", "ntl_console_warn", "ntl_console_err" };
    private static readonly string[] TableFns = { "ntl_t", "ntl_ts", "ntl_tf" };
    private static readonly string[] TableFiles = { "ntl_i18n_init.gml", "ntl_i18n_out.gml" };

    private static readonly Regex Cjk = new(@"[\u3000-\u303F\u4E00-\u9FFF\uFF00-\uFFEF]", RegexOptions.Compiled);
    private static readonly Regex StrLit = new("\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);
    private static readonly Regex AddEntry = new(@"ds_map_add\(\s*_(zh|en)\s*,\s*""((?:[^""\\]|\\.)*)""\s*,\s*""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled);
    private static readonly Regex Placeholder = new(@"\{\d\}", RegexOptions.Compiled);

    public static void Run(string neutraledRoot, List<Lint.Issue> issues)
    {
        var dirs = new[] { Path.Combine(neutraledRoot, "api"), Path.Combine(neutraledRoot, "live") };
        var files = new List<string>();
        foreach (var d in dirs) if (Directory.Exists(d)) files.AddRange(Directory.GetFiles(d, "*.gml", SearchOption.AllDirectories));
        var apiDir = Path.Combine(neutraledRoot, "api");
        if (files.Count == 0) return;

        // ---------- 规则 14/15：先解析文案表，供规则 13/14 判定用 ----------
        var zh = new Dictionary<string, string>(StringComparer.Ordinal);
        var en = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tf in TableFiles)
        {
            var p = Path.Combine(apiDir, tf);
            if (!File.Exists(p)) continue;
            var t = File.ReadAllText(p);
            foreach (Match m in AddEntry.Matches(t))
            {
                var lang = m.Groups[1].Value;
                var k = Unescape(m.Groups[2].Value);
                var v = Unescape(m.Groups[3].Value);
                if (lang == "zh") zh[k] = v; else en[k] = v;   // 同名键后写覆盖，与运行时 ds_map_add 语义一致
            }
        }

        foreach (var tf in TableFiles)
        {
            var p = Path.Combine(apiDir, tf);
            if (!File.Exists(p)) continue;
            var t = File.ReadAllText(p);
            foreach (Match m in AddEntry.Matches(t))
            {
                var lang = m.Groups[1].Value;
                var k = Unescape(m.Groups[2].Value);
                var v = Unescape(m.Groups[3].Value);
                var line = LineNumberOf(t, m.Index);
                if (v.Length == 0)
                {
                    issues.Add(new Lint.Issue { File = p, Line = line, Rule = L("文案空值"), Message = L("键 '{0}' 的 {1} 译文为空", k, lang) });
                    continue;
                }
                var other = lang == "zh" ? en.GetValueOrDefault(k) : zh.GetValueOrDefault(k);
                if (other == null)
                {
                    issues.Add(new Lint.Issue { File = p, Line = line, Rule = L("文案缺一侧"), Message = L("键 '{0}' 只有 {1} 一侧，另一种语言会显示键名本身", k, lang) });
                    continue;
                }
                var a = string.Join(",", Placeholder.Matches(v).Select(x => x.Value).Distinct().OrderBy(x => x, StringComparer.Ordinal));
                var b = string.Join(",", Placeholder.Matches(other).Select(x => x.Value).Distinct().OrderBy(x => x, StringComparer.Ordinal));
                if (a != b)
                    issues.Add(new Lint.Issue { File = p, Line = line, Rule = L("占位符不一致"), Message = L("键 '{0}'：{1} 用 [{2}] 而另一种语言用 [{3}]", k, lang, a, b) });
            }
        }

        // ---------- 规则 13：控制台输出里的硬编码中文 ----------
        // ---------- 规则 14：引用不存在的键 ----------
        foreach (var f in files)
        {
            var raw = File.ReadAllText(f);
            var code = MaskNonCode(raw);
            bool isTable = TableFiles.Contains(Path.GetFileName(f));

            if (!isTable)
            {
                foreach (var fn in ConsoleFns)
                {
                    int at = 0;
                    while ((at = code.IndexOf(fn, at, StringComparison.Ordinal)) >= 0)
                    {
                        int after = at + fn.Length;
                        if (after < code.Length && (char.IsLetterOrDigit(code[after]) || code[after] == '_')) { at = after; continue; }
                        int open = code.IndexOf('(', after);
                        if (open < 0) break;
                        int close = MatchParen(code, open);
                        if (close < 0) { at = after; continue; }

                        var seg = raw.Substring(open, close - open + 1);
                        var segCode = code.Substring(open, close - open + 1);
                        bool exempt = LineText(raw, at).Contains(Exempt, StringComparison.Ordinal);

                        var wrapped = new List<(int s, int e)>();
                        foreach (var tf in TableFns)
                        {
                            int p = 0;
                            while ((p = segCode.IndexOf(tf, p, StringComparison.Ordinal)) >= 0)
                            {
                                int q = segCode.IndexOf('(', p);
                                if (q < 0) break;
                                int r = MatchParen(segCode, q);
                                if (r < 0) break;
                                wrapped.Add((q, r));
                                p = r;
                            }
                        }

                        if (!exempt)
                        {
                            foreach (Match m in StrLit.Matches(seg))
                            {
                                if (!Cjk.IsMatch(m.Groups[1].Value)) continue;
                                int abs = open + m.Index;
                                if (wrapped.Any(w => abs > open + w.s && abs < open + w.e)) continue;
                                issues.Add(new Lint.Issue
                                {
                                    File = f,
                                    Line = LineNumberOf(raw, abs),
                                    Rule = L("文案硬编码"),
                                    Message = L("控制台输出里有硬编码中文 \"{0}\"。\n", m.Groups[1].Value) +
                                              L("        → 用户可见文本必须走文案表：ntl_console_log(ntl_ts(\"键名\", [参数]));\n") +
                                              L("        → 词条加在 api/ntl_i18n_out.gml（zh 原文 + en 译文）；故意中英并排则加标记 {0}", Exempt)
                                });
                            }
                        }
                        at = close;
                    }
                }
            }

            if (isTable) continue;
            var lines = raw.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var lineCode = code.Substring(LineStart(raw, i), line.Length);
                foreach (var tf in TableFns)
                {
                    int p = 0;
                    while ((p = lineCode.IndexOf(tf + "(", p, StringComparison.Ordinal)) >= 0)
                    {
                        if (p > 0 && (char.IsLetterOrDigit(lineCode[p - 1]) || lineCode[p - 1] == '_')) { p += tf.Length; continue; }
                        int q = p + tf.Length + 1;
                        while (q < line.Length && (line[q] == ' ' || line[q] == '\t')) q++;
                        if (q >= line.Length || line[q] != '"') { p += tf.Length; continue; }
                        int e = line.IndexOf('"', q + 1);
                        if (e < 0) { p += tf.Length; continue; }
                        var key = line.Substring(q + 1, e - q - 1);
                        if (key.Length > 0 && !zh.ContainsKey(key))
                            issues.Add(new Lint.Issue
                            {
                                File = f, Line = i + 1, Rule = L("词条缺失"),
                                Message = L("引用了不存在的键 '{0}' —— ntl_t 会原样返回键名，界面会显示 \"{0}\"", key)
                            });
                        p = e + 1;
                    }
                }
            }
        }
    }

    /// <summary>把注释（// 到行尾、/* */ 区间）与字符串字面量换成等长空白，保持下标/行号一一对应。</summary>
    private static string MaskNonCode(string src)
    {
        var a = src.ToCharArray();
        bool inStr = false, inLine = false, inBlock = false;
        for (int i = 0; i < a.Length; i++)
        {
            char c = a[i], n = i + 1 < a.Length ? a[i + 1] : '\0';
            if (inLine) { if (c == '\n') inLine = false; else a[i] = ' '; continue; }
            if (inBlock) { if (c == '*' && n == '/') { a[i] = ' '; a[i + 1] = ' '; i++; inBlock = false; } else if (c != '\n') a[i] = ' '; continue; }
            if (inStr)
            {
                if (c == '\\') { a[i] = ' '; if (i + 1 < a.Length && a[i + 1] != '\n') { a[i + 1] = ' '; i++; } continue; }
                if (c == '"') { inStr = false; a[i] = ' '; continue; }
                if (c != '\n') a[i] = ' ';
                continue;
            }
            if (c == '"') { inStr = true; a[i] = ' '; continue; }
            if (c == '/' && n == '/') { inLine = true; a[i] = ' '; continue; }
            if (c == '/' && n == '*') { inBlock = true; a[i] = ' '; a[i + 1] = ' '; i++; continue; }
        }
        return new string(a);
    }

    private static int MatchParen(string s, int open)
    {
        int depth = 0;
        for (int i = open; i < s.Length; i++)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')') { depth--; if (depth == 0) return i; }
        }
        return -1;
    }

    private static int LineNumberOf(string src, int index)
    {
        int line = 1;
        for (int i = 0; i < index && i < src.Length; i++) if (src[i] == '\n') line++;
        return line;
    }

    /// <summary>第 line（0 基）行的起始下标；越界返回 0。</summary>
    private static int LineStart(string src, int line)
    {
        int at = 0;
        for (int i = 0; i < line; i++)
        {
            int nx = src.IndexOf('\n', at);
            if (nx < 0) return 0;
            at = nx + 1;
        }
        return at;
    }

    /// <summary>index 所在行的整行原文（不含换行）。</summary>
    private static string LineText(string src, int index)
    {
        int s = index, e = index;
        while (s > 0 && src[s - 1] != '\n') s--;
        while (e < src.Length && src[e] != '\n') e++;
        return src.Substring(s, e - s);
    }

    private static string Unescape(string s) => s.Replace("\\\"", "\"").Replace("\\\\", "\\");
}
