using static Neutraled.Builder.Lang;
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
    /// <summary>读取整个 config.json（不存在或损坏时返回空对象，不抛）。
    /// ★ 2026-09-29 重复键容错：JsonObject 是字典，遇到重复键会抛
    ///   「An item with the same key has already been added」。历史缺陷（游戏内按 L 切语言时
    ///   api/ntl_config_set_lang.gml 用「替换后文本没变」当判据，语言码与现值相同时会追加第二个 "lang"）
    ///   会让这里抛异常，而旧代码 catch 后返回**空对象** ⇒ 玩家所有设置被静默丢弃、
    ///   且 --plugin-hooks / --lang-coverage / --plugin-list 等命令直接崩在启动。
    ///   现在改用 JsonDocument（允许重复键）重解析，按「后者覆盖」重建对象，并把清理结果写回磁盘（自愈）。</summary>
    public static JsonObject Load(string gameRoot)
    {
        try
        {
            var p = Paths.ConfigPath(gameRoot);
            if (!File.Exists(p)) return new JsonObject();
            var text = File.ReadAllText(p);
            var obj = ConvertText(text, out bool dup);
            if (obj == null) { Paths.Log(L("[配置] config.json 解析失败，本次按空配置继续（文件保持原样，可用 --doctor 查看原因）")); return new JsonObject(); }
            if (dup)
            {
                // 有重复键：把清理后的版本写回（自愈），下次读取就是干净的
                try { Save(gameRoot, obj); Paths.Log(L("[配置] config.json 里有重复键，已自动清理并写回")); }
                catch { /* 自愈失败不影响读取 */ }
            }
            return obj;
        }
        catch { return new JsonObject(); }
    }

    /// <summary>把 JSON 文本转成 JsonObject，同时报告同层重复键（按「后者覆盖」取值）。
    /// ★ 为什么不能只用 JsonNode.Parse + try/catch 兜重复键：**异常不在 Parse 时抛**。
    ///   JsonObject 内部的字典是**惰性**建立的（JsonObject.InitializeDictionary），重复键的
    ///   ArgumentException 要等第一次访问属性（`obj["lang"]`）才从 GetItem → InitializeDictionary 里冒出来 ——
    ///   那时已经跑出 Load 的 try 范围，表现为「命令一启动就崩，而且配置永远修不好」。实测调用栈：
    ///     ThrowDuplicateKey ← OrderedDictionary.Add ← JsonObject.InitializeDictionary
    ///     ← JsonObject.GetItem ← ConfigFile.GetString ← CliFeatures.LangPackRequested
    ///   所以这里改用 JsonDocument（允许重复键）＋ 自己递归重建，重复键在赋值时自然被覆盖。
    ///   同时比 JsonNode.Parse 更宽容：容忍尾逗号与 // 注释（手工改配置时常见）。</summary>
    private static JsonObject? ConvertText(string text, out bool dup)
    {
        dup = false;
        try
        {
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            return ConvertElement(doc.RootElement, ref dup) as JsonObject;
        }
        catch { return null; }
    }

    /// <summary>递归重建 JSON 树：对象/数组自己建（重复键在这里被覆盖并记录），标量交给 JsonNode.Parse。</summary>
    private static JsonNode? ConvertElement(JsonElement el, ref bool dup)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                var o = new JsonObject();
                foreach (var prop in el.EnumerateObject())
                {
                    if (o.ContainsKey(prop.Name)) dup = true;
                    o[prop.Name] = ConvertElement(prop.Value, ref dup);
                }
                return o;
            case JsonValueKind.Array:
                var arr = new JsonArray();
                foreach (var item in el.EnumerateArray()) arr.Add(ConvertElement(item, ref dup));
                return arr;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return null;
            default:
                return JsonNode.Parse(el.GetRawText());
        }
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
