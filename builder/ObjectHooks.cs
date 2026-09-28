namespace Neutraled.Builder;

/// <summary>对象事件 Hook —— 让 mod 拦截**任意对象的任意事件**（Create / Step / Draw / Alarm / Other…）。
///
/// 与函数 Hook 的区别：
///   函数 Hook 拦的是「脚本函数」，本机制拦的是「对象的事件代码」。
///   对象事件不是函数，所以不能改名包装 —— 改为**前后插入**：
///
///     // 事件代码开头插入：
///     var _ntlOev = ntl_oev_run("obj_xxx", "Step_0", id, "pre");
///     if (_ntlOev[0] == 1) exit;          // override 模式 → 跳过原事件代码
///
///     ...（原事件代码）...
///
///     // 末尾追加：
///     ntl_oev_run("obj_xxx", "Step_0", id, "post");
///
/// mod.json 声明：
///   "object_hooks": [
///     { "object": "obj_kris", "event": "Step_0", "mode": "pre", "handler": "hooks/kris_step.lua" }
///   ]
/// </summary>
public static class ObjectHooks
{
    public sealed class Decl
    {
        public string Object = "";
        public string Event = "";
        public string Mode = "pre";
        public string Handler = "";
        public string ModId = "";
        public string ModDir = "";
    }

    public static List<Decl> Collect(List<ModEntry> mods)
    {
        var list = new List<Decl>();
        foreach (var m in mods)
        {
            var json = Path.Combine(m.Dir, "mod.json");
            if (!File.Exists(json)) continue;
            try
            {
                var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(json),
                    new System.Text.Json.JsonDocumentOptions
                    {
                        AllowTrailingCommas = true,
                        CommentHandling = System.Text.Json.JsonCommentHandling.Skip
                    });
                if (!doc.RootElement.TryGetProperty("object_hooks", out var arr)) continue;
                if (arr.ValueKind != System.Text.Json.JsonValueKind.Array) continue;
                foreach (var e in arr.EnumerateArray())
                {
                    var d = new Decl
                    {
                        ModId = m.Id,
                        ModDir = m.Dir,
                        Object = e.TryGetProperty("object", out var o) ? (o.GetString() ?? "") : "",
                        Event = e.TryGetProperty("event", out var ev) ? (ev.GetString() ?? "") : "",
                        Mode = e.TryGetProperty("mode", out var md) ? (md.GetString() ?? "pre") : "pre",
                        Handler = e.TryGetProperty("handler", out var h) ? (h.GetString() ?? "") : ""
                    };
                    if (d.Object != "" && d.Event != "") list.Add(d);
                }
            }
            catch { }
        }
        return list;
    }
}
