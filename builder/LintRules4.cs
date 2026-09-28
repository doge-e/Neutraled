using System.Text;
using System.Text.RegularExpressions;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>lint 规则扩展（第四批，2026-09-27）—— 本地变量必须先声明。
///
/// 新增规则：
///  16. api/**/*.gml 或 live/**/*.gml 里读了 _xxx 形式的**局部变量**，但整个文件里既没有
///      var _xxx、也不是函数/匿名函数的参数 ⇒ 运行期必炸：
///
///          Variable obj_darkcontroller._k(115339, -2147483648) not set before reading it.
///          at gml_Script_ntl_cfg_row (line -1)
///
///      真实事故：api/ntl_cfg_row.gml 写了 var _vis = (_k >= _off && _k < _off + 7) ? 1 : 0;
///      但漏了 var _k = argument[0]; ⇒ 游戏一进 CONFIG 页就弹 Code Error（真机 nat-13 全废）。
///      注意 _k 曾经被 Lint.GmBuiltins 当成"可裸用的名字"，所以老的规则 2/3 抓不到它 —— 本规则按
///      "文件内是否声明过"判断，不看白名单。
///
/// 例外：文件里任意位置出现标记 ntl:novar-exempt 时跳过该文件（用于确实要裸用实例变量的场合）。
/// </summary>
public static class LintRules4
{
    private const string Exempt = "ntl:novar-exempt";

    private static readonly Regex BlockComment = new(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex LineComment = new(@"//[^\n]*", RegexOptions.Compiled);
    private static readonly Regex StrLit = new("\"(?:[^\"\\\\\n]|\\\\.)*\"", RegexOptions.Compiled);
    private static readonly Regex FnDef = new(@"\bfunction\s+(_?[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);
    private static readonly Regex FnParams = new(@"\bfunction\s*(?:[A-Za-z_][A-Za-z0-9_]*)?\s*\(([^)]*)\)", RegexOptions.Compiled);
    private static readonly Regex CatchParam = new(@"\bcatch\s*\(\s*(_?[A-Za-z_][A-Za-z0-9_]*)\s*\)", RegexOptions.Compiled);
    private static readonly Regex LocalUse = new(@"(?<![.\w$""])(_[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);
    private static readonly Regex Ident = new(@"^_?[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    public static void Run(string neutraledRoot, List<Lint.Issue> issues)
    {
        var dirs = new[] { Path.Combine(neutraledRoot, "api"), Path.Combine(neutraledRoot, "live") };
        var files = new List<string>();
        foreach (var d in dirs) if (Directory.Exists(d)) files.AddRange(Directory.GetFiles(d, "*.gml", SearchOption.AllDirectories));

        foreach (var f in files)
            foreach (var (nm, line) in ScanFile(f))
                issues.Add(new Lint.Issue
                {
                    File = f, Line = line, Rule = L("局部变量未声明"), IsError = true,
                    Message = L("读取了 '{0}'，但整个文件里没有 var {0} = …，也不是函数参数。\n", nm) +
                              L("        → 运行期会直接弹 Code Error：\"Variable <object>.{0} not set before reading it\"\n", nm) +
                              L("        → 修复：改用 argument[n]，或先声明 var {0} = …;", nm)
                });
    }

    /// <summary>扫描单个 .gml 文件，返回所有"读了但没声明"的局部变量（名字 + 行号）。
    /// doctor 的规则 4（变量定义）也复用它 —— 原来 doctor 只盯 _a0/_a1/_a2 三个名字，
    /// 所以漏掉了真机崩过的 _k（api/ntl_cfg_row.gml）与 _a4（api/ntl_love_host.gml）。</summary>
    public static List<(string name, int line)> ScanFile(string path)
    {
        var result = new List<(string, int)>();
        var raw = File.ReadAllText(path);
        if (raw.Contains(Exempt)) return result;

        var code = StrLit.Replace(BlockComment.Replace(raw, " "), "\"\"");
        code = LineComment.Replace(code, " ");

        var declared = new HashSet<string>(StringComparer.Ordinal);
        CollectVars(code, declared);
        foreach (Match m in FnDef.Matches(code)) declared.Add(m.Groups[1].Value);
        foreach (Match m in FnParams.Matches(code))
            foreach (var part in m.Groups[1].Value.Split(','))
            {
                var p2 = part.Trim().Split('=')[0].Trim();
                if (Ident.IsMatch(p2)) declared.Add(p2);
            }
        foreach (Match m in CatchParam.Matches(code)) declared.Add(m.Groups[1].Value);

        var lines = code.Split('\n');
        var reported = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < lines.Length; i++)
            foreach (Match m in LocalUse.Matches(lines[i]))
            {
                var nm = m.Groups[1].Value;
                if (nm.Length < 2 || declared.Contains(nm) || reported.Contains(nm)) continue;
                // 赋值目标（_x = / _x += / _x[0] = …）不是"读取"，GameMaker 会当场创建实例变量
                var after = lines[i].Substring(m.Index + m.Length);
                if (Regex.IsMatch(after, @"^\s*(\[[^\]]*\]\s*)?(=(?!=)|\+=|-=|\*=|/=|\|=|&=|\^=|%=)")) continue;
                reported.Add(nm);
                result.Add((nm, i + 1));
            }
        return result;
    }

    /// <summary>收集一个 GML 文件里所有 var 声明出来的局部变量名。
    /// ★ 不能用「var + 到分号」的整体正则：lambda 体里的 var 会被外层那个贪婪匹配吞掉，
    ///   于是 var _desc = … 这种声明被漏掉 ⇒ 误报（第一版就是这么误报了 7 条）。</summary>
    private static void CollectVars(string code, HashSet<string> declared)
    {
        foreach (Match m in Regex.Matches(code, @"\bvar\b"))
        {
            int i = m.Index + m.Length, depth = 0;
            var sb = new StringBuilder();
            for (; i < code.Length; i++)
            {
                char c = code[i];
                if (c == '(' || c == '[' || c == '{') depth++;
                else if (c == ')' || c == ']' || c == '}') { if (depth == 0) break; depth--; }
                else if (c == ';' && depth == 0) break;
                sb.Append(c);
            }
            var text = sb.ToString();
            var parts = new List<string>();
            int d2 = 0, start = 0;
            for (int k = 0; k < text.Length; k++)
            {
                char c = text[k];
                if (c == '(' || c == '[' || c == '{') d2++;
                else if (c == ')' || c == ']' || c == '}') d2--;
                else if (c == ',' && d2 == 0) { parts.Add(text.Substring(start, k - start)); start = k + 1; }
            }
            parts.Add(text.Substring(start));
            foreach (var p in parts)
            {
                var nm = Regex.Match(p.Trim(), @"^(_?[A-Za-z_][A-Za-z0-9_]*)");
                if (nm.Success) declared.Add(nm.Groups[1].Value);
            }
        }
    }
}
