using System.Text.RegularExpressions;

namespace Neutraled.Studio;

/// <summary>NTL Script / GML 语法高亮规则。</summary>
public static class Syntax
{
    public const string Keywords = "let|if|else|while|for|return|break|continue|true|false|undefined|null|function";

    public static readonly string[] Builtins =
    {
        "log", "debug_on", "debug_off", "version",
        "get_global", "set_global", "var_get", "var_set",
        "inst_all", "inst_nth", "inst_count", "inst_first", "inst_exists",
        "file_read", "file_write", "json_parse_safe",
        "arr_new", "arr_push", "arr_get", "arr_set", "arr_len", "arr_pop", "arr_join",
        "map_new", "map_set", "map_get", "map_has", "map_del", "map_keys",
        "str_len", "str_sub", "str_find", "str_upper", "str_lower", "str_char", "str_split", "str_contains",
        "len", "floor", "ceil", "round", "abs", "min", "max", "sqrt", "power", "sign", "clamp", "lerp",
        "random", "irandom", "string", "real"
    };

    public sealed class Rule
    {
        public string Name = "";
        public Regex Pattern = new("$^");
        public Color Color = Color.Black;
        public bool Bold;
        public bool Italic;
    }

    /// <summary>返回按优先级排列的高亮规则（先匹配者优先）。</summary>
    public static List<Rule> Build()
    {
        var kw = "\b(?:" + Keywords + ")\b";
        var bi = "\b(?:" + string.Join("|", Builtins) + ")\b";
        return new List<Rule>
        {
            new() { Name = "comment",  Pattern = new Regex(@"//[^\n]*|/\*[\s\S]*?\*/", RegexOptions.Compiled), Color = Color.FromArgb(0, 128, 0) },
            new() { Name = "string",   Pattern = new Regex("\"(?:[^\"\\\\]|\\\\.)*\"", RegexOptions.Compiled), Color = Color.FromArgb(163, 21, 21) },
            new() { Name = "keyword",  Pattern = new Regex(kw, RegexOptions.Compiled), Color = Color.FromArgb(0, 0, 255), Bold = true },
            new() { Name = "builtin",  Pattern = new Regex(bi, RegexOptions.Compiled), Color = Color.FromArgb(43, 145, 175) },
            new() { Name = "nsfunc",   Pattern = new Regex(@"[A-Za-z_]\w*\.[A-Za-z_]\w*", RegexOptions.Compiled), Color = Color.FromArgb(128, 0, 160) },
            new() { Name = "number",   Pattern = new Regex(@"\b\d+(?:\.\d+)?\b", RegexOptions.Compiled), Color = Color.FromArgb(128, 64, 0) }
        };
    }
}
