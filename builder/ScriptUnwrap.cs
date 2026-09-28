using System.Text;

namespace Neutraled.Builder;

/// <summary>
/// 新脚本「剥壳」。
///
/// 背景（实测）：GML 2.3 的函数库条目源码长这样 ——
///     function dj_path(arg0) { ... }
/// 把它**原样**当脚本体 QueueReplace 到 gml_Script_dj_path 时，UTMT 的 CodeImportGroup 会把这
/// 当成「函数定义」，另外新建一个空壳脚本承载函数绑定（产物里实测多出
/// gml_Script_dj_path_gml_Script_dj_path，0 条指令）。运行期按名字调用 dj_path(...) 于是返回 0
/// —— 这就是 chapter1 Code Error 的真凶：ini_open(0) 静默失败，紧接着的 ini_read_* 报
/// "Trying to read from undefined INI file"。
///
/// 修法：注入前把函数定义拆开，每个函数一条独立脚本（脚本名 = 函数名），源码 = 脚本体 +
/// 开头把 argument0..N 接到同名局部变量（GML 脚本体没有具名参数）。裸函数体的脚本（Neutraled
/// 自己的 api/*.gml、60fps_layer 的 fps_ntl_*）本来就是对的，Split 会原样返回，不受影响。
/// </summary>
public static class ScriptUnwrap
{
    private sealed class FuncDef
    {
        public string Name = "";
        public List<(string Name, string? Default)> Params = new();
        public int Start;          // 'function' 关键字的位置（顶层）
        public int BodyStart;      // '{' 之后
        public int BodyEnd;        // 配对的 '}' 位置（不含）
        public int End;            // 配对的 '}' 之后
        public int NestedFunctions;
    }

    /// <summary>
    /// 把一个 gml 文件拆成若干 (脚本名, 源码)。没有函数定义时原样返回一条。
    /// </summary>
    public static List<(string name, string source)> Split(string baseName, string text, out string note)
    {
        var result = new List<(string name, string source)>();
        var defs = FindTopLevelFunctions(text);
        if (defs.Count == 0)
        {
            note = "";
            result.Add((baseName, text));
            return result;
        }

        // 定义之外的顶层代码（一般只剩空行/注释）→ 仍按文件名当一个脚本
        var glue = new StringBuilder();
        int pos = 0;
        foreach (var d in defs) { glue.Append(text, pos, d.Start - pos); pos = d.End; }
        glue.Append(text, pos, text.Length - pos);
        var glueText = glue.ToString();
        bool pure = Mask(glueText).Replace(";", " ").Trim().Length == 0;

        if (!pure) result.Add((baseName, glueText.Trim()));
        foreach (var d in defs) result.Add((d.Name, BuildBody(text, d)));

        int nested = defs.Sum(d => d.NestedFunctions);
        note = $"函数 {defs.Count} 个 → 独立脚本{(pure ? "" : " + 顶层代码")}" + (nested > 0 ? $"（★ {nested} 个嵌套函数定义未拆）" : "");
        return result;
    }

    private static string BuildBody(string text, FuncDef d)
    {
        var sb = new StringBuilder();
        sb.Append("// [Neutraled] 剥壳自函数定义：function ").Append(d.Name).Append('(')
          .Append(string.Join(", ", d.Params.Select(p => p.Default == null ? p.Name : p.Name + " = " + p.Default)))
          .Append(")\r\n");
        sb.Append("// GML 脚本体没有具名参数，这里把 argument<i> 接到同名局部变量。\r\n");
        for (int i = 0; i < d.Params.Count; i++)
            sb.Append("var ").Append(d.Params[i].Name).Append(" = argument").Append(i).Append(";\r\n");
        for (int i = 0; i < d.Params.Count; i++)
        {
            var p = d.Params[i];
            if (p.Default != null)
                sb.Append("if (is_undefined(").Append(p.Name).Append(")) { ").Append(p.Name).Append(" = ").Append(p.Default).Append("; }\r\n");
        }
        sb.Append(text, d.BodyStart, d.BodyEnd - d.BodyStart);
        return sb.ToString();
    }

    /// <summary>把字符串/注释替换成空格（长度、换行不变），便于安全地数括号和找关键字。</summary>
    public static string Mask(string s)
    {
        var a = s.ToCharArray();
        int i = 0, n = s.Length;
        while (i < n)
        {
            char c = s[i];
            if (c == '/' && i + 1 < n && s[i + 1] == '/')
            {
                while (i < n && s[i] != '\n') { a[i] = ' '; i++; }
            }
            else if (c == '/' && i + 1 < n && s[i + 1] == '*')
            {
                a[i] = ' '; a[i + 1] = ' '; i += 2;
                while (i < n && !(s[i] == '*' && i + 1 < n && s[i + 1] == '/')) { if (s[i] != '\n') a[i] = ' '; i++; }
                if (i < n) { a[i] = ' '; if (i + 1 < n) a[i + 1] = ' '; i += 2; }
            }
            else if (c == '"' || c == '\'')
            {
                char q = c; a[i] = ' '; i++;
                while (i < n && s[i] != q)
                {
                    if (s[i] == '\\') { a[i] = ' '; i++; if (i < n) { if (s[i] != '\n') a[i] = ' '; i++; } }
                    else { if (s[i] != '\n') a[i] = ' '; i++; }
                }
                if (i < n) { a[i] = ' '; i++; }
            }
            else i++;
        }
        return new string(a);
    }

    private static List<FuncDef> FindTopLevelFunctions(string text)
    {
        var mask = Mask(text);
        var list = new List<FuncDef>();
        int depth = 0, i = 0;
        while (i < mask.Length)
        {
            char c = mask[i];
            if (c == '{') { depth++; i++; continue; }
            if (c == '}') { depth--; i++; continue; }
            if (depth == 0 && c == 'f' && IsWordStart(mask, i) && string.CompareOrdinal(mask, i, "function", 0, 8) == 0 && IsWordEnd(mask, i + 8))
            {
                var d = ParseDef(text, mask, i);
                if (d != null) { list.Add(d); i = d.End; continue; }
            }
            i++;
        }
        return list;
    }

    private static int CountTopLevelFunctions(string text)
    {
        var mask = Mask(text);
        int depth = 0, i = 0, n = 0;
        while (i < mask.Length)
        {
            char c = mask[i];
            if (c == '{') { depth++; i++; continue; }
            if (c == '}') { depth--; i++; continue; }
            if (depth == 0 && c == 'f' && IsWordStart(mask, i) && string.CompareOrdinal(mask, i, "function", 0, 8) == 0 && IsWordEnd(mask, i + 8)) { n++; }
            i++;
        }
        return n;
    }

    private static bool IsWordStart(string mask, int i)
        => i == 0 || !(char.IsLetterOrDigit(mask[i - 1]) || mask[i - 1] == '_');

    private static bool IsWordEnd(string mask, int i)
        => i >= mask.Length || !(char.IsLetterOrDigit(mask[i]) || mask[i] == '_');

    private static FuncDef? ParseDef(string text, string mask, int start)
    {
        int i = start + 8;
        while (i < mask.Length && char.IsWhiteSpace(mask[i])) i++;
        int ns = i;
        while (i < mask.Length && (char.IsLetterOrDigit(mask[i]) || mask[i] == '_')) i++;
        if (i == ns) return null;
        var name = text.Substring(ns, i - ns);
        while (i < mask.Length && char.IsWhiteSpace(mask[i])) i++;
        if (i >= mask.Length || mask[i] != '(') return null;
        int ps = ++i, pdepth = 1;
        while (i < mask.Length)
        {
            if (mask[i] == '(') pdepth++;
            else if (mask[i] == ')') { pdepth--; if (pdepth == 0) break; }
            i++;
        }
        if (i >= mask.Length) return null;
        var paramText = text.Substring(ps, i - ps);
        i++;
        while (i < mask.Length && char.IsWhiteSpace(mask[i])) i++;
        if (i >= mask.Length || mask[i] != '{') return null;
        int bodyStart = i + 1, bdepth = 1, j = bodyStart;
        while (j < mask.Length)
        {
            if (mask[j] == '{') bdepth++;
            else if (mask[j] == '}') { bdepth--; if (bdepth == 0) break; }
            j++;
        }
        if (j >= mask.Length) return null;
        var body = text.Substring(bodyStart, j - bodyStart);
        return new FuncDef
        {
            Name = name,
            Params = ParseParams(paramText),
            Start = start,
            BodyStart = bodyStart,
            BodyEnd = j,
            End = j + 1,
            NestedFunctions = CountTopLevelFunctions(body),
        };
    }

    private static List<(string Name, string? Default)> ParseParams(string s)
    {
        var res = new List<(string, string?)>();
        int depth = 0, start = 0;
        for (int i = 0; i <= s.Length; i++)
        {
            bool split = false;
            if (i == s.Length) split = true;
            else
            {
                char c = s[i];
                if (c == '(' || c == '[') depth++;
                else if (c == ')' || c == ']') depth--;
                else if (c == ',' && depth == 0) split = true;
            }
            if (!split) continue;
            var part = s.Substring(start, i - start).Trim();
            start = i + 1;
            if (part.Length == 0) continue;
            string pname = part; string? pdef = null;
            int eq = IndexOfAssign(part);
            if (eq > 0) { pname = part.Substring(0, eq).Trim(); pdef = part.Substring(eq + 1).Trim(); }
            if (pname.Length > 0 && (char.IsLetter(pname[0]) || pname[0] == '_')) res.Add((pname, pdef));
        }
        return res;
    }

    private static int IndexOfAssign(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] != '=') continue;
            if (i + 1 < s.Length && s[i + 1] == '=') { i++; continue; }
            if (i > 0 && (s[i - 1] == '!' || s[i - 1] == '<' || s[i - 1] == '>' || s[i - 1] == '=')) continue;
            return i;
        }
        return -1;
    }
}
