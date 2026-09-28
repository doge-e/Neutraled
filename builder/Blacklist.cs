using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>黑名单条目（Neutraled/blacklist.json 的 entries[] 一项）。</summary>
public sealed class Entry
{
    /// <summary>匹配方式：id（精确）| name（大小写不敏感子串）| category（大小写不敏感子串）。</summary>
    [JsonPropertyName("kind")] public string Kind { get; set; } = "id";

    [JsonPropertyName("value")] public string Value { get; set; } = "";

    /// <summary>备注（给人看的，不参与匹配）。</summary>
    [JsonPropertyName("note")] public string Note { get; set; } = "";

    /// <summary>加入时间 yyyy-MM-dd HH:mm:ss。</summary>
    [JsonPropertyName("added")] public string Added { get; set; } = "";
}

/// <summary>GameBanana 的黑名单：搜索/浏览/下载入口一律先剔掉被拉黑的条目。
///
/// 匹配规则：kind=id 与 GbSearchResult.Id 精确比较（数字 id 一一对应，大小写不敏感无意义）；
/// kind=name 比 Name、kind=category 比 Model，都是**大小写不敏感的子串**。
/// 存储：Neutraled/blacklist.json = {"entries":[{"kind":"id|name|category","value":"…","note":"","added":"…"}]}。
/// 读取一律容错：文件坏了就当成空表（只打印一行警告），绝不让一个坏 JSON 挡住整个搜索。
/// </summary>
public static class Blacklist
{
    /// <summary>写 blacklist.json 用：缩进 + 非 ASCII 原样（复制 Paths.Json 的编码器，避免两处不一致）。</summary>
    private static readonly JsonSerializerOptions JsonWrite = new(Paths.Json) { WriteIndented = true };

    private static readonly string[] Kinds = { "id", "name", "category" };

    private const string HumanStamp = "yyyy-MM-dd HH:mm:ss";

    /// <summary>条目长度上限（正常条目是 id 或 mod 名，不可能这么长；超了多半是拿错参数了）。</summary>
    private const int MaxValueLen = 200;

    /// <summary>黑名单文件路径（Neutraled/blacklist.json）。</summary>
    public static string PathOf(string gameRoot) => Path.Combine(Paths.NeutraledRoot(gameRoot), "blacklist.json");

    /// <summary>这个 mod 是否被拉黑（id 精确；name / category 走大小写不敏感子串）。</summary>
    public static bool IsBlocked(string gameRoot, int modId, string? name = null, string? category = null)
    {
        var list = Load(gameRoot);
        if (list.Count == 0) return false;
        var id = modId.ToString(CultureInfo.InvariantCulture);
        foreach (var e in list)
        {
            if (e.Value.Length == 0) continue;
            switch (NormalizeKind(e.Kind))
            {
                case "name":
                    if (Contains(name, e.Value)) return true;
                    break;
                case "category":
                    if (Contains(category, e.Value)) return true;
                    break;
                default:
                    if (string.Equals(e.Value, id, StringComparison.Ordinal)) return true;
                    break;
            }
        }
        return false;
    }

    /// <summary>加一条（kind: id|name|category）。重复（同 kind 同值）只提示不重复写。</summary>
    public static void Add(string gameRoot, string entry, string kind = "id", string? note = null)
    {
        var k = NormalizeKind(kind);
        if (k.Length == 0)
        {
            Paths.Log(L("[黑名单] 未知 kind：{0}（可用 id/name/category）", kind ?? ""));
            return;
        }
        var v = (entry ?? "").Trim();
        if (!SafeValue(v))
        {
            Paths.Log(L("[黑名单] 拒绝：非法条目 {0}", entry ?? ""));
            return;
        }
        var list = Load(gameRoot);
        foreach (var e in list)
        {
            if (SameKind(e.Kind, k) && SameValue(k, e.Value, v))
            {
                Paths.Log(L("[黑名单] 已存在 {0}", v));
                return;
            }
        }
        list.Add(new Entry { Kind = k, Value = v, Note = note ?? "", Added = DateTime.Now.ToString(HumanStamp, CultureInfo.InvariantCulture) });
        Save(gameRoot, list);
    }

    /// <summary>去掉一条（同 kind 同值）。返回是否真删掉了。</summary>
    public static bool Remove(string gameRoot, string entry, string kind = "id")
    {
        var k = NormalizeKind(kind);
        if (k.Length == 0)
        {
            Paths.Log(L("[黑名单] 未知 kind：{0}（可用 id/name/category）", kind ?? ""));
            return false;
        }
        var v = (entry ?? "").Trim();
        var list = Load(gameRoot);
        var idx = list.FindIndex(e => SameKind(e.Kind, k) && SameValue(k, e.Value, v));
        if (idx < 0)
        {
            Paths.Log(L("[黑名单] 没找到 {0}", v));
            return false;
        }
        var hit = list[idx];
        list.RemoveAt(idx);
        Save(gameRoot, list);
        Paths.Log(L("[黑名单] 已移除 {0}", hit.Value));
        return true;
    }

    /// <summary>列全部条目（文件不存在/损坏 → 空表）。</summary>
    public static List<Entry> List(string gameRoot) => Load(gameRoot);

    /// <summary>就地剔除命中的搜索结果，返回被剔掉的条数（GbBrowse / WebUi 搜索后直接调）。</summary>
    public static int FilterOut(string gameRoot, List<GbSearchResult> results)
    {
        if (results == null || results.Count == 0) return 0;
        var list = Load(gameRoot);
        if (list.Count == 0) return 0;
        return results.RemoveAll(r => Hit(list, r));
    }

    /// <summary>--block-list：一行一条，字段名固定，方便 grep 断言。</summary>
    public static void PrintList(string gameRoot)
    {
        var list = Load(gameRoot);
        Paths.Log(L("[黑名单] 文件：{0}", PathOf(gameRoot)));
        if (list.Count == 0)
        {
            Paths.Log(L("[黑名单] 没有条目"));
            return;
        }
        Paths.Log(L("[黑名单] 共 {0} 条", list.Count));
        foreach (var e in list)
            Paths.Log(L("[黑名单] kind={0}  value={1}  note={2}  added={3}", e.Kind, e.Value, e.Note, e.Added));
    }

    // ---------------- 内部实现 ----------------

    private static bool Hit(List<Entry> list, GbSearchResult r)
    {
        var id = r.Id.ToString(CultureInfo.InvariantCulture);
        foreach (var e in list)
        {
            if (e.Value.Length == 0) continue;
            switch (NormalizeKind(e.Kind))
            {
                case "name":
                    if (Contains(r.Name, e.Value)) return true;
                    break;
                case "category":
                    if (Contains(r.Model, e.Value)) return true;
                    break;
                default:
                    if (string.Equals(e.Value, id, StringComparison.Ordinal)) return true;
                    break;
            }
        }
        return false;
    }

    /// <summary>读 blacklist.json；文件不存在/损坏都退化成空表（坏文件打印一行警告，绝不抛）。</summary>
    private static List<Entry> Load(string gameRoot)
    {
        var list = new List<Entry>();
        var path = PathOf(gameRoot);
        try
        {
            if (!File.Exists(path)) return list;
            var root = JsonNode.Parse(File.ReadAllText(path), null,
                new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip })?.AsObject();
            if (root?["entries"] is not JsonArray arr)
            {
                Paths.Log(L("[黑名单] 文件损坏，已忽略：{0}", path));
                return list;
            }
            foreach (var n in arr)
            {
                if (n is not JsonObject o) continue;
                var e = new Entry
                {
                    Kind = StrOf(o, "kind"),
                    Value = StrOf(o, "value"),
                    Note = StrOf(o, "note"),
                    Added = StrOf(o, "added"),
                };
                if (e.Value.Length == 0) continue;
                if (NormalizeKind(e.Kind).Length == 0) e.Kind = "id";
                list.Add(e);
            }
        }
        catch (Exception ex) { Paths.Log(L("[黑名单] 文件损坏，已忽略：{0}", ex.Message)); }
        return list;
    }

    private static void Save(string gameRoot, List<Entry> list)
    {
        try
        {
            Directory.CreateDirectory(Paths.NeutraledRoot(gameRoot));
            var arr = new JsonArray();
            foreach (var e in list)
                arr.Add(new JsonObject { ["kind"] = e.Kind, ["value"] = e.Value, ["note"] = e.Note, ["added"] = e.Added });
            var root = new JsonObject { ["entries"] = arr };
            Paths.SafeWrite(PathOf(gameRoot), root.ToJsonString(JsonWrite));
        }
        catch (Exception ex) { Paths.Log(L("[黑名单] [警告] 写入失败：{0}", ex.Message)); }
    }

    private static string StrOf(JsonObject o, string key)
    {
        var n = o[key];
        if (n == null) return "";
        try { return n.GetValue<string>(); }
        catch { return n.ToString(); }
    }

    /// <summary>kind 归一化：id|name|category（大小写不敏感）；未知返回空串（调用方据此拒绝）。</summary>
    private static string NormalizeKind(string? kind)
    {
        var k = (kind ?? "").Trim().ToLowerInvariant();
        foreach (var s in Kinds) if (s == k) return s;
        return "";
    }

    private static bool SameKind(string? a, string b) => NormalizeKind(a) == b;

    /// <summary>同一条目：id 比较大小写敏感，name/category 不敏感。</summary>
    private static bool SameValue(string kind, string a, string b)
        => string.Equals(a, b, kind == "id" ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    /// <summary>子串匹配（大小写不敏感）。haystack 允许为 null（GbSearchResult 的字段可能为空）。</summary>
    private static bool Contains(string? haystack, string needle)
        => !string.IsNullOrEmpty(haystack) && haystack!.Contains(needle, StringComparison.OrdinalIgnoreCase);

    /// <summary>条目值的合法性：非空、不过长、没有控制字符。</summary>
    private static bool SafeValue(string v)
    {
        if (v.Length == 0 || v.Length > MaxValueLen) return false;
        foreach (var ch in v) if (char.IsControl(ch)) return false;
        return true;
    }
}
