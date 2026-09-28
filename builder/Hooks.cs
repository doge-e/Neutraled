using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Models;
using Underanalyzer.Decompiler;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>函数 hook：让 mod 的 Lua/NTL 脚本能在游戏函数执行前(pre)、执行后(post)、或完全接管(override)。
///
/// mod.json 声明：
///   "hooks": [ { "script": "snd_play", "mode": "override", "handler": "hooks/snd.lua" } ]
///
/// 编译期做法（关键：脚本资源 root 里含 function 定义，不能整体替换）：
///   1. 反编译脚本资源 root（gml_GlobalScript_&lt;name&gt;）
///   2. 把其中的 "function &lt;name&gt;(" 改名为 "function ntl_orig_&lt;name&gt;("
///   3. 追加包装函数 "function &lt;name&gt;(...) { hook 分派 + 调用 ntl_orig_&lt;name&gt; }"
///   4. 整段回写
/// </summary>
public static class Hooks
{
    public sealed class HookDecl
    {
        public string Script = "";
        public string Mode = "pre";
        public string Handler = "";
        public string ModId = "";
        public string ModDir = "";
        public string Source = "lua";
    }

    public static List<HookDecl> Collect(List<ModEntry> mods)
    {
        var list = new List<HookDecl>();
        foreach (var mod in mods)
        {
            if (mod.HooksRaw == null) continue;
            foreach (var node in mod.HooksRaw)
            {
                if (node is not JsonObject o) continue;
                string S(string k)
                {
                    var v = o[k];
                    return v == null ? "" : v.ToString().Trim('"');
                }
                var script = S("script");
                var handler = S("handler");
                if (string.IsNullOrEmpty(script) || string.IsNullOrEmpty(handler)) continue;
                var mode = S("mode");
                if (string.IsNullOrEmpty(mode)) mode = "pre";
                list.Add(new HookDecl
                {
                    Script = script,
                    Mode = mode.ToLowerInvariant(),
                    Handler = handler,
                    ModId = mod.Id,
                    ModDir = mod.Dir.Replace('\\', '/').TrimEnd('/') + "/",
                    Source = handler.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) ? "lua" : "ntl"
                });
            }
        }
        return list;
    }

    public static void WriteRegistry(string gameRoot, List<HookDecl> hooks)
    {
        var path = Path.Combine(Paths.NeutraledRoot(gameRoot), "hook-registry.json");
        var arr = new JsonArray();
        foreach (var h in hooks)
        {
            arr.Add(new JsonObject
            {
                ["script"] = h.Script,
                ["mode"] = h.Mode,
                ["handler"] = h.Handler,
                ["mod"] = h.ModId,
                ["moddir"] = h.ModDir,
                ["source"] = h.Source
            });
        }
        var root = new JsonObject { ["version"] = 1, ["hooks"] = arr };
        Paths.SafeWrite(path, root.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }));
        Paths.Log(L("  hook 注册表: {0} 条 -> {1}", hooks.Count, path));
    }

    /// <summary>把被 hook 的函数替换成包装器（原地改写脚本资源）。</summary>
    public static int Wrap(UndertaleData data, List<HookDecl> hooks, CodeImportGroup group)
    {
        int wrapped = 0;
        foreach (var name in hooks.Select(h => h.Script).Distinct())
        {
            var rootName = "gml_GlobalScript_" + name;
            var root = data.Code.ByName(rootName);
            if (root == null)
            {
                rootName = "gml_Script_" + name;
                root = data.Code.ByName(rootName);
            }
            if (root == null)
            {
                Paths.Log(L("  [警告] hook 目标脚本不存在: {0}", name));
                continue;
            }

            string src;
            try { src = Injector.Decompile(data, rootName); }
            catch (Exception ex)
            {
                Paths.Log(L("  [跳过] {0}: 反编译失败（{1}）", name, ex.Message.Split('.')[0]));
                continue;
            }

            var origName = "ntl_orig_" + name;
            if (src.Contains("function " + origName + "("))
            {
                Paths.Log(L("  [跳过] {0}: 已包装过", name));
                continue;
            }

            // 1) 目标函数改名
            var renamed = src.Replace("function " + name + "(", "function " + origName + "(");
            if (renamed == src)
            {
                Paths.Log(L("  [跳过] {0}: 脚本里找不到 function {1}(，无法包装", name, name));
                continue;
            }

            // 2) 追加包装函数
            var modes = hooks.Where(h => h.Script == name).Select(h => h.Mode).Distinct().ToList();
            var wrapper = BuildWrapperFunction(name, origName, modes);

            group.QueueReplace(root, renamed + "\n" + wrapper);
            wrapped++;
            Paths.Log(L("  hook 包装: {0} -> {1}（模式: {2}）", name, origName, string.Join("/", modes)));
        }
        return wrapped;
    }

    /// <summary>生成包装函数（同名，内部调 ntl_orig_&lt;name&gt;）。</summary>
    private static string BuildWrapperFunction(string name, string origName, List<string> modes)
    {
        bool hasOverride = modes.Contains("override");
        bool hasPre = modes.Contains("pre");
        bool hasPost = modes.Contains("post");

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("// Neutraled hook wrapper (auto-generated)");
        sb.AppendLine($"function {(name)}() {{");
        sb.AppendLine("    var _ntlA = [];");
        sb.AppendLine("    var _ntlC = argument_count;");
        sb.AppendLine("    for (var _ntlI = 0; _ntlI < _ntlC; _ntlI += 1) { _ntlA[_ntlI] = argument[_ntlI]; }");
        sb.AppendLine("    var _ntlR = undefined;");

        if (hasOverride)
        {
            sb.AppendLine($"    var _ntlO = ntl_hook_run(\"{(name)}\", \"override\", _ntlA);");
            sb.AppendLine("    if (ds_map_find_value(_ntlO, \"handled\") == 1) {");
            sb.AppendLine("        var _ntlOv = ds_map_find_value(_ntlO, \"value\");");
            sb.AppendLine("        ds_map_destroy(_ntlO);");
            sb.AppendLine("        return _ntlOv;");
            sb.AppendLine("    }");
            sb.AppendLine("    ds_map_destroy(_ntlO);");
        }
        if (hasPre)
        {
            sb.AppendLine($"    var _ntlP = ntl_hook_run(\"{(name)}\", \"pre\", _ntlA);");
            sb.AppendLine("    if (ds_map_find_value(_ntlP, \"handled\") == 1) {");
            sb.AppendLine("        var _ntlPv = ds_map_find_value(_ntlP, \"value\");");
            sb.AppendLine("        ds_map_destroy(_ntlP);");
            sb.AppendLine("        return _ntlPv;");
            sb.AppendLine("    }");
            sb.AppendLine("    ds_map_destroy(_ntlP);");
        }

        sb.AppendLine("    switch (_ntlC) {");
        for (int i = 0; i <= 8; i++)
        {
            var args = string.Join(", ", Enumerable.Range(0, i).Select(k => $"_ntlA[{k}]"));
            sb.AppendLine($"        case {i}: _ntlR = {origName}({args}); break;");
        }
        sb.AppendLine($"        default: _ntlR = {origName}(); break;");
        sb.AppendLine("    }");

        if (hasPost)
        {
            sb.AppendLine($"    var _ntlQ = ntl_hook_run(\"{(name)}\", \"post\", _ntlA);");
            sb.AppendLine("    if (ds_map_find_value(_ntlQ, \"handled\") == 1) {");
            sb.AppendLine("        var _ntlQv = ds_map_find_value(_ntlQ, \"value\");");
            sb.AppendLine("        ds_map_destroy(_ntlQ);");
            sb.AppendLine("        return _ntlQv;");
            sb.AppendLine("    }");
            sb.AppendLine("    ds_map_destroy(_ntlQ);");
        }

        sb.AppendLine("    return _ntlR;");
        sb.AppendLine("}");
        return sb.ToString();
    }
}
