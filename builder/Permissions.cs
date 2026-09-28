using System.Text.Json;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>mod 权限模型 —— 让玩家知道自己装了什么风险的东西。
///
/// 背景：mod 现在能删任意文件、改任意变量、读写网络。玩家装 mod 等于交出完全控制权。
///
/// 方案：
///   1) mod.json 用 "permissions" 声明它需要的能力
///   2) 部署时统计并在 GUI/日志里展示
///   3) 未声明的敏感权限 → 警告（不阻止，但让玩家知道）
///   4) 危险权限（filesystem-write / network / process）→ 显著标记
///
/// 权限清单：
///   filesystem-read    读游戏文件
///   filesystem-write   写文件（危险：可改存档）
///   instance-modify    改任意实例变量
///   instance-create    创建/销毁实例
///   code-replace       替换原始代码（raw/）
///   builtin-hook       拦截内置函数
///   object-hook        拦截对象事件
///   process           启动外部进程（极危险）
///   network           网络访问（极危险）
/// </summary>
public static class Permissions
{
    public sealed record Perm(string Id, string Level, string Desc);

    public static readonly Perm[] All = new[]
    {
        new Perm("filesystem-read",  "safe",   L("读取游戏文件")),
        new Perm("filesystem-write", "risky",  L("写入文件（可修改存档）")),
        new Perm("instance-modify",  "safe",   L("修改实例变量")),
        new Perm("instance-create",  "safe",   L("创建/销毁实例")),
        new Perm("code-replace",     "risky",  L("替换游戏原始代码")),
        new Perm("builtin-hook",     "safe",   L("拦截内置函数")),
        new Perm("object-hook",      "safe",   L("拦截对象事件")),
        new Perm("process",          "danger", L("启动外部进程")),
        new Perm("network",          "danger", L("网络访问")),
    };

    public sealed class ModPerm
    {
        public string ModId = "";
        public string ModName = "";
        public List<string> Declared = new();
        public List<string> Implied = new();      // 从 mod 内容推断出来的
        public List<string> Undeclared = new();   // 用了但没声明
    }

    /// <summary>分析所有 mod 的权限</summary>
    public static List<ModPerm> Analyze(List<ModEntry> mods)
    {
        var result = new List<ModPerm>();

        foreach (var m in mods)
        {
            var mp = new ModPerm { ModId = m.Id, ModName = m.Name };

            // 1) 读 mod.json 的 permissions
            var json = Path.Combine(m.Dir, "mod.json");
            if (File.Exists(json))
            {
                try
                {
                    var doc = JsonDocument.Parse(File.ReadAllText(json),
                        new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                    if (doc.RootElement.TryGetProperty("permissions", out var arr) && arr.ValueKind == JsonValueKind.Array)
                        foreach (var e in arr.EnumerateArray())
                            if (e.GetString() is string s) mp.Declared.Add(s);
                }
                catch { }
            }

            // 2) 从 mod 的实际内容推断用到的权限
            void Imply(string p)
            {
                if (!mp.Implied.Contains(p)) mp.Implied.Add(p);
            }

            // 有 raw/ 目录 → code-replace
            if (Directory.Exists(Path.Combine(m.Dir, "raw")) &&
                Directory.GetFiles(Path.Combine(m.Dir, "raw"), "*.gml").Length > 0)
                Imply("code-replace");

            // 有 gml/ 或 patches/ → 也是改代码
            if ((Directory.Exists(Path.Combine(m.Dir, "gml")) &&
                 Directory.GetFiles(Path.Combine(m.Dir, "gml"), "*.gml").Length > 0) ||
                (Directory.Exists(Path.Combine(m.Dir, "patches")) &&
                 Directory.GetFiles(Path.Combine(m.Dir, "patches"), "*").Length > 0))
                Imply("code-replace");

            // mod.json 里有 builtin_hooks → builtin-hook
            if (File.Exists(json))
            {
                try
                {
                    var txt = File.ReadAllText(json);
                    if (txt.Contains("\"builtin_hooks\"")) Imply("builtin-hook");
                    if (txt.Contains("\"object_hooks\"")) Imply("object-hook");
                }
                catch { }
            }

            // 扫描 Lua 脚本里的敏感调用
            foreach (var lua in Directory.GetFiles(m.Dir, "*.lua", SearchOption.AllDirectories))
            {
                try
                {
                    var src = File.ReadAllText(lua);
                    if (src.Contains("file_write") || src.Contains("io.write") || src.Contains("love.filesystem.write"))
                        Imply("filesystem-write");
                    if (src.Contains("file_read") || src.Contains("love.filesystem.read"))
                        Imply("filesystem-read");
                    if (src.Contains("ntl_inst_set") || src.Contains("ntl_inst_get"))
                        Imply("instance-modify");
                    if (src.Contains("ntl_inst_create") || src.Contains("ntl_inst_destroy"))
                        Imply("instance-create");
                    if (src.Contains("ntl_bh_") || src.Contains("builtin_hook"))
                        Imply("builtin-hook");
                    if (src.Contains("ntl_oev_") || src.Contains("object_hook"))
                        Imply("object-hook");
                    if (src.Contains("os.execute") || src.Contains("io.popen") || src.Contains("process"))
                        Imply("process");
                    if (src.Contains("http") || src.Contains("socket") || src.Contains("network"))
                        Imply("network");
                }
                catch { }
            }

            // 3) 找出"用了但没声明"的
            foreach (var p in mp.Implied)
                if (!mp.Declared.Contains(p)) mp.Undeclared.Add(p);

            if (mp.Declared.Count > 0 || mp.Implied.Count > 0) result.Add(mp);
        }

        return result;
    }

    public static void PrintReport(List<ModPerm> perms)
    {
        if (perms.Count == 0)
        {
            Paths.Log(L("  权限分析: 所有 mod 都没有敏感操作"));
            return;
        }

        Paths.Log(L("  权限分析: {0} 个 mod 需要关注", perms.Count));
        foreach (var mp in perms)
        {
            var risky = mp.Declared.Concat(mp.Implied)
                .Distinct()
                .Select(p => All.FirstOrDefault(x => x.Id == p))
                .Where(x => x != null && x.Level != "safe")
                .ToList();

            if (mp.Undeclared.Count > 0)
            {
                Paths.Log(L("    [未声明] {0}: ", mp.ModName) +
                          string.Join(", ", mp.Undeclared.Select(p =>
                          {
                              var info = All.FirstOrDefault(x => x.Id == p);
                              return info?.Level == "danger" ? L("⚠️ {0}（{1}）", p, info.Desc) : p;
                          })));
            }
            if (risky.Count > 0)
            {
                foreach (var r in risky)
                    Paths.Log(L("    [{0}] {1}: {2} - {3}", (r!.Level == "danger" ? L("危险") : L("注意")), mp.ModName, r.Id, r.Desc));
            }
        }
    }

    /// <summary>写出 JSON 供 GUI 显示</summary>
    public static void WriteJson(List<ModPerm> perms, string path)
    {
        var arr = new System.Text.Json.Nodes.JsonArray();
        foreach (var mp in perms)
        {
            var o = new System.Text.Json.Nodes.JsonObject
            {
                ["id"] = mp.ModId,
                ["name"] = mp.ModName,
                ["declared"] = new System.Text.Json.Nodes.JsonArray(mp.Declared.Select(x => (System.Text.Json.Nodes.JsonNode)System.Text.Json.Nodes.JsonValue.Create(x)).ToArray()),
                ["implied"] = new System.Text.Json.Nodes.JsonArray(mp.Implied.Select(x => (System.Text.Json.Nodes.JsonNode)System.Text.Json.Nodes.JsonValue.Create(x)).ToArray()),
                ["undeclared"] = new System.Text.Json.Nodes.JsonArray(mp.Undeclared.Select(x => (System.Text.Json.Nodes.JsonNode)System.Text.Json.Nodes.JsonValue.Create(x)).ToArray()),
            };
            arr.Add(o);
        }
        try { File.WriteAllText(path, arr.ToJsonString(new JsonSerializerOptions { WriteIndented = true })); }
        catch { }
    }
}
