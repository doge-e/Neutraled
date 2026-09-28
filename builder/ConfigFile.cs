using System.Text.Json;
using System.Text.Json.Nodes;

namespace Neutraled.Builder;

/// <summary>Neutraled/config.json 的统一读写入口（配置档、主题、语言、缓存上限都经此）。
/// 约定：
///   · **读-改-写**：绝不丢掉未知键与 _comment（GUI 与 GML 都写这个文件）；
///   · 落盘 UTF-8 **无 BOM**（GameMaker 的 json_parse 吃 BOM 会失败）；
///   · 非 ASCII 不转义（Paths.Json 的 UnsafeRelaxedJsonEscaping）；
///   · 并发安全写（Paths.SafeWrite，6 个章节 worker 并行部署时也安全）。
///   · 落盘后 best-effort 镜像到存档区 %LOCALAPPDATA%\DELTARUNE\Neutraled\config.json
///     （root 产物受 GameMaker 沙箱遮蔽，只看得到那份；见 Paths.SaveMirrorConfigPath）。</summary>
public static class ConfigFile
{
    /// <summary>读取整个 config.json（不存在或损坏时返回空对象，不抛）。</summary>
    public static JsonObject Load(string gameRoot)
    {
        try
        {
            var p = Paths.ConfigPath(gameRoot);
            if (!File.Exists(p)) return new JsonObject();
            var node = JsonNode.Parse(File.ReadAllText(p));
            return node as JsonObject ?? new JsonObject();
        }
        catch { return new JsonObject(); }
    }

    public static void Save(string gameRoot, JsonObject obj)
    {
        var dir = Paths.NeutraledRoot(gameRoot);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        var json = obj.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        Paths.SafeWrite(Paths.ConfigPath(gameRoot), json);

        // ★ 镜像到存档区（沙箱原因见 Paths.SaveMirrorConfigPath）：root 产物（章节选择器）只看得到存档区那份，
        //   不镜像的话"CLI/GUI 改了语言 → 打开游戏还是旧语言"。同一份文本，best-effort，失败不影响主流程。
        try
        {
            var mirror = Paths.SaveMirrorConfigPath();
            if (mirror != null)
            {
                var mdir = Path.GetDirectoryName(mirror);
                if (!string.IsNullOrEmpty(mdir) && !Directory.Exists(mdir)) Directory.CreateDirectory(mdir);
                Paths.SafeWrite(mirror, json);
            }
        }
        catch { /* 镜像失败不影响主流程 */ }
    }

    public static string? GetString(string gameRoot, string key)
    {
        var v = Load(gameRoot)[key];
        return v == null ? null : v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : v.ToString();
    }

    public static bool? GetBool(string gameRoot, string key)
    {
        var v = Load(gameRoot)[key];
        if (v == null) return null;
        try { return v.GetValue<bool>(); } catch { }
        var s = v.ToString();
        return s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1";
    }

    public static int? GetInt(string gameRoot, string key)
    {
        var v = Load(gameRoot)[key];
        if (v == null) return null;
        try { return v.GetValue<int>(); } catch { }
        return int.TryParse(v.ToString(), out var n) ? n : null;
    }

    /// <summary>写一个键（读-改-写）。value 为 null 表示删除该键。</summary>
    public static void Set(string gameRoot, string key, JsonNode? value) => SetMany(gameRoot, new Dictionary<string, JsonNode?> { [key] = value });

    /// <summary>批量写（一次读写，避免多次落盘的竞态）。</summary>
    public static void SetMany(string gameRoot, Dictionary<string, JsonNode?> values)
    {
        var obj = Load(gameRoot);
        foreach (var kv in values)
        {
            if (kv.Value == null) obj.Remove(kv.Key);
            else obj[kv.Key] = kv.Value.DeepClone();
        }
        Save(gameRoot, obj);
    }
}
