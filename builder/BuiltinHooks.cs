namespace Neutraled.Builder;

/// <summary>内置函数 Hook —— 让 mod 能拦截 GameMaker 的**任何内置函数**。
///
/// 背景：GML 的内置函数（draw_text / keyboard_check / instance_create_depth …）
///       不像脚本资源那样能被改名包装，所以无法用普通的 hook 机制。
///
/// 方案：**全代码块重定向**
///   1) 扫描所有代码块的字节码/源码，把 `内置函数名(` 替换成 `ntl_bh_<名字>(`
///   2) 为每个被 hook 的函数生成一个 GML 包装，它先问 mod 要不要接管，
///      不接管就调用原内置函数（同参数个数）
///   3) 之后游戏里所有对它的调用都会经过 mod
///
/// mod.json 声明：
///   "builtin_hooks": [
///     { "func": "draw_text", "mode": "pre", "handler": "hooks/drawtext.lua" }
///   ]
/// </summary>
public static class BuiltinHooks
{
    public sealed class Decl
    {
        public string Func = "";
        public string Mode = "pre";     // pre / post / override
        public string Handler = "";
        public string ModId = "";
        public string ModDir = "";
    }

    /// <summary>从所有 mod 的 mod.json 收集 builtin_hooks 声明</summary>
    public static List<Decl> Collect(List<ModEntry> mods)
    {
        var list = new List<Decl>();
        foreach (var m in mods)
        {
            var json = Path.Combine(m.Dir, "mod.json");
            if (!File.Exists(json)) continue;
            try
            {
                var txt = File.ReadAllText(json);
                var doc = System.Text.Json.JsonDocument.Parse(txt, new System.Text.Json.JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = System.Text.Json.JsonCommentHandling.Skip
                });
                if (!doc.RootElement.TryGetProperty("builtin_hooks", out var arr)) continue;
                if (arr.ValueKind != System.Text.Json.JsonValueKind.Array) continue;
                foreach (var e in arr.EnumerateArray())
                {
                    var d = new Decl
                    {
                        ModId = m.Id,
                        ModDir = m.Dir,
                        Func = e.TryGetProperty("func", out var f) ? (f.GetString() ?? "") : "",
                        Mode = e.TryGetProperty("mode", out var md) ? (md.GetString() ?? "pre") : "pre",
                        Handler = e.TryGetProperty("handler", out var h) ? (h.GetString() ?? "") : ""
                    };
                    if (!string.IsNullOrWhiteSpace(d.Func)) list.Add(d);
                }
            }
            catch { }
        }
        return list;
    }

    /// <summary>生成包装脚本（返回要注入的 GML 源码，按脚本名分组）</summary>
    public static Dictionary<string, string> GenerateWrappers(IEnumerable<string> funcNames)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var fn in funcNames.Distinct())
        {
            var scriptName = "ntl_bh_" + fn;
            var sb = new System.Text.StringBuilder();

            sb.AppendLine("/// " + scriptName + " —— 由 Neutraled 生成的内置函数包装（Hook: " + fn + "）");
            sb.AppendLine("/// 游戏里所有对 " + fn + "(...) 的调用都被重定向到这里");
            sb.AppendLine("if (!variable_global_exists(\"ntl_bh_map\")) return " + fn + "(argument0, argument1, argument2, argument3, argument4, argument5, argument6, argument7);");
            sb.AppendLine("var _lst = ds_map_find_value(global.ntl_bh_map, \"" + fn + "\");");
            sb.AppendLine("if (_lst == undefined) return " + fn + "(argument0, argument1, argument2, argument3, argument4, argument5, argument6, argument7);");
            sb.AppendLine();
            sb.AppendLine("// 收集实际传入的参数个数（GML 用 argument_count）");
            sb.AppendLine("var _args = [];");
            sb.AppendLine("for (var _i = 0; _i < argument_count; _i += 1) array_push(_args, argument[_i]);");
            sb.AppendLine();
            sb.AppendLine("// 先跑 pre / override");
            sb.AppendLine("var _r = ntl_bh_run(\"" + fn + "\", \"pre\", _args);");
            sb.AppendLine("if (_r[0] == 1) return _r[1];");
            sb.AppendLine();
            sb.AppendLine("// 调用原内置函数（按参数个数分派）");
            sb.AppendLine("var _v;");
            sb.AppendLine("switch (argument_count) {");
            for (int n = 0; n <= 8; n++)
            {
                var ps = string.Join(", ", Enumerable.Range(0, n).Select(i => "argument" + i));
                sb.AppendLine("    case " + n + ": _v = " + fn + "(" + ps + "); break;");
            }
            sb.AppendLine("    default: _v = " + fn + "(argument0, argument1, argument2, argument3, argument4, argument5, argument6, argument7); break;");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("// 跑 post");
            sb.AppendLine("var _r2 = ntl_bh_run(\"" + fn + "\", \"post\", _args, _v);");
            sb.AppendLine("if (_r2[0] == 1) return _r2[1];");
            sb.AppendLine("return _v;");

            result[scriptName] = sb.ToString();
        }
        return result;
    }
}
