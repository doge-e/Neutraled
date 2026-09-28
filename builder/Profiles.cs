using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>单段名字（配置档 id / mod id / 快照版本）的安全校验与「路径必须在根目录之下」的前缀校验。
/// 实测教训：这些名字全都来自命令行参数、mod.json、导入的档文件或 Web 请求 ——
/// 只要混进一个 ".." 或绝对路径，删除/覆盖就会跑到 Neutraled 目录外面。
/// 所以全项目只在入口判定一次：非法名字一律拒绝，绝不拼进路径。</summary>
internal static class PathGuard
{
    /// <summary>名字长度上限（正常 id 不会超过 64，留一倍余量）。</summary>
    private const int MaxLen = 128;

    /// <summary>是否是安全的单段名字：非空、≤128、不含 ".."、不含路径分隔符/通配符/控制字符，
    /// 只允许 字母或数字（含中文等非 ASCII 字母）/ _ - . @ +；"." 与 ".." 本身也拒绝。</summary>
    public static bool IsSafeName(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        if (s.Length > MaxLen) return false;
        if (s == ".") return false;
        if (s.Contains("..", StringComparison.Ordinal)) return false;
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch)) continue;
            if (ch is '_' or '-' or '.' or '@' or '+') continue;
            return false;
        }
        return true;
    }

    /// <summary>candidate 是否位于 root 之下（先归一化再比较；Windows 忽略大小写，Linux/macOS 不忽略）。
    /// 任何删除/覆盖操作动手之前都必须先过这一关。</summary>
    public static bool Under(string? root, string? candidate)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(candidate)) return false;
        var cmp = Platform.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        try
        {
            var r = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root!));
            var c = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate!));
            return string.Equals(r, c, cmp) || c.StartsWith(r + Path.DirectorySeparatorChar, cmp);
        }
        catch { return false; }
    }
}

/// <summary>一个配置档（profiles/&lt;id&gt;.json）：一套「哪些 mod 开着 + 一份 config.json 设置」。
/// 字段名全部小写下划线风格（JsonPropertyName），与 SPEC / GML / Web 端约定一致；
/// 落盘走 Paths.SafeWrite + Paths.Json 的编码器（非 ASCII 原样 UTF-8，不带 \uXXXX 转义）。</summary>
public sealed class Profile
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("created")] public string Created { get; set; } = "";
    [JsonPropertyName("updated")] public string Updated { get; set; } = "";
    [JsonPropertyName("enabled")] public List<string> Enabled { get; set; } = new();
    [JsonPropertyName("settings")] public Dictionary<string, JsonNode?> Settings { get; set; } = new(StringComparer.Ordinal);
    [JsonPropertyName("chapters")] public List<string> Chapters { get; set; } = new();
    [JsonPropertyName("mods_root")] public string ModsRoot { get; set; } = "mods";
    [JsonPropertyName("note")] public string Note { get; set; } = "";
}

/// <summary>配置档（profiles/&lt;id&gt;.json）读写、切换与导入导出。
/// 语义要点：
/// · 启用态的唯一真相是每个 mod 自己的 mods/&lt;...&gt;/mod.json 的 "enabled"（缺失 = 启用），
///   所以 Apply 是「逐个 mod.json 读-改-写」，只动 enabled 一个键，别的字段一律原样保留；
/// · config.json 一律走 ConfigFile（读-改-写、保留未知键），且**比较后只写真正变化的键**（幂等）；
/// · 活动档 id 写在 config.json 的 active_profile，同时镜像一份 profiles/active.json 给外部工具读；
/// · 删配置档绝不删 mod 文件。</summary>
public static class Profiles
{
    /// <summary>默认配置档 id。</summary>
    public const string DefaultId = "default";

    /// <summary>会被配置档记录的 config.json 键（也是打印顺序）。
    /// 特意**不含** active_profile —— 它是「当前活动档指针」，不属于某个档的内容。
    /// 档文件里手工加过的未知键只要仍存在于 config.json，CaptureFromLive 也会继续收进来（见 CaptureInto）。</summary>
    private static readonly List<string> SettingKeys = new()
    {
        "lang", "theme", "base_mod", "auto_skip_selector", "auto_chapter",
        "skip_legend", "debug_live", "cache_max_mb"
    };

    /// <summary>写档文件用：缩进 + 非 ASCII 原样（复制 Paths.Json 的编码器设置，避免两处不一致）。</summary>
    private static readonly JsonSerializerOptions JsonWrite = new(Paths.Json) { WriteIndented = true };

    // ———————————————————————————— 基础路径 ————————————————————————————

    /// <summary>配置档目录（Neutraled/profiles）。</summary>
    public static string Root(string gameRoot) => Paths.ProfilesRoot(gameRoot);

    /// <summary>单个配置档的文件路径；id 非法时返回空串（调用方必须先判空，绝不能拿它去写盘）。</summary>
    public static string PathOf(string gameRoot, string id)
        => PathGuard.IsSafeName(id) ? Path.Combine(Root(gameRoot), id + ".json") : "";

    private static string Now() => DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");

    private static int ChapterRank(string c)
        => string.Equals(c, "root", StringComparison.OrdinalIgnoreCase) ? 0 : 1;

    /// <summary>JSON 值比较（比 ToJsonString 文本）：写入前判断是否真变了。
    /// 两边都 null 也算相等；嵌套对象若键序不同会判为不等 —— 只会多写一次，不会漏写。</summary>
    private static bool JsonEquals(JsonNode? a, JsonNode? b)
        => string.Equals(a?.ToJsonString(Paths.Json), b?.ToJsonString(Paths.Json), StringComparison.Ordinal);

    private static Dictionary<string, JsonNode?> CloneSettings(Dictionary<string, JsonNode?> src)
    {
        var d = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        foreach (var kv in src) d[kv.Key] = kv.Value?.DeepClone();
        return d;
    }

    /// <summary>反序列化后的兜底：JSON 里显式写 null / 空文件会让引用类型字段变成 null，
    /// 这里统一补成空集合或空串，后续代码就不必到处判空。</summary>
    private static void Normalize(Profile p)
    {
        p.Id = p.Id ?? "";
        p.Name = p.Name ?? "";
        p.Description = p.Description ?? "";
        p.Created = p.Created ?? "";
        p.Updated = p.Updated ?? "";
        p.Note = p.Note ?? "";
        p.ModsRoot = string.IsNullOrWhiteSpace(p.ModsRoot) ? "mods" : p.ModsRoot;
        p.Enabled = p.Enabled ?? new List<string>();
        p.Settings = p.Settings ?? new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        p.Chapters = p.Chapters ?? new List<string>();
    }

    // ———————————————————————————— 读 ————————————————————————————

    /// <summary>列出全部配置档（按 id 升序）。目录不存在或无文件 → 空表；
    /// 单个档 JSON 损坏只打警告并跳过该档，绝不让整表失败。</summary>
    public static List<Profile> List(string gameRoot)
    {
        var result = new List<Profile>();
        var root = Root(gameRoot);
        if (!Directory.Exists(root)) return result;
        foreach (var f in Directory.EnumerateFiles(root, "*.json"))
        {
            var stem = Path.GetFileNameWithoutExtension(f);
            // active.json 是「当前活动档」指针（见 ActiveId），不是配置档本体
            if (string.Equals(stem, "active", StringComparison.OrdinalIgnoreCase)) continue;
            // <id>.export.json 是 --profile-export 的默认落点（CliFeatures.cs:161），同样不是配置档本体，
            // 不排除掉的话导出一次，列表里就会多出一个同 id 的重复档
            if (stem.EndsWith(".export", StringComparison.OrdinalIgnoreCase)) continue;
            var p = LoadFile(f, out var err);
            if (p == null)
            {
                Paths.Log(L("[配置档] 警告：跳过损坏的配置档 {0}（{1}）", f, err ?? ""));
                continue;
            }
            if (string.IsNullOrEmpty(p.Id)) p.Id = stem;
            result.Add(p);
        }
        result.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        return result;
    }

    private static Profile? LoadFile(string path, out string? err)
    {
        err = null;
        try
        {
            var p = JsonSerializer.Deserialize<Profile>(File.ReadAllText(path), Paths.Json);
            if (p == null) { err = L("JSON 为 null"); return null; }
            Normalize(p);
            return p;
        }
        catch (Exception ex) { err = ex.Message; return null; }
    }

    /// <summary>读一个配置档；不存在 / id 非法 / JSON 损坏都返回 null（并打印原因）。</summary>
    public static Profile? Load(string gameRoot, string id)
    {
        if (!PathGuard.IsSafeName(id))
        {
            Paths.Log(L("[配置档] 非法 id：{0}", id ?? ""));
            return null;
        }
        var path = PathOf(gameRoot, id);
        if (!File.Exists(path)) return null;
        var p = LoadFile(path, out var err);
        if (p == null)
        {
            Paths.Log(L("[配置档] 警告：配置档 {0} 解析失败：{1}", id, err ?? ""));
            return null;
        }
        if (string.IsNullOrEmpty(p.Id)) p.Id = id;
        return p;
    }

    /// <summary>当前活动档 id：config.json 的 active_profile &gt; profiles/active.json 的 active &gt; default。
    /// 回退读 active.json 是为了兼容「外部工具只写这一处」的情况；写的时候以 config.json 为准（单一真相源）。</summary>
    public static string ActiveId(string gameRoot)
    {
        // 指针可能悬空（档被删掉、或被外部工具删掉）：指向不存在的档就**自愈**清空，
        // 否则后续命令会一直报「找不到配置档 xxx」。
        var v = ConfigFile.GetString(gameRoot, "active_profile");
        if (!string.IsNullOrWhiteSpace(v))
        {
            var idc = v.Trim();
            if (ProfileExists(gameRoot, idc)) return idc;
            ClearActivePointer(gameRoot, idc);
            return DefaultId;
        }
        try
        {
            var f = Path.Combine(Root(gameRoot), "active.json");
            if (File.Exists(f))
            {
                var a = JsonNode.Parse(File.ReadAllText(f))?["active"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(a))
                {
                    var ida = a.Trim();
                    if (ProfileExists(gameRoot, ida)) return ida;
                    ClearActivePointer(gameRoot, ida);
                }
            }
        }
        catch { /* 指针文件坏了就退回 default，不影响启动 */ }
        return DefaultId;
    }

    /// <summary>档文件是否存在。刻意不走 Load，避免与 ActiveId 递归。</summary>
    private static bool ProfileExists(string gameRoot, string id)
        => PathGuard.IsSafeName(id) && File.Exists(PathOf(gameRoot, id));

    /// <summary>清掉悬空的活动档指针（config.json 是权威，active.json 是镜像，两份一起清）。</summary>
    private static void ClearActivePointer(string gameRoot, string badId)
    {
        Paths.Log(L("[配置档] 活动档指针 {0} 已失效，已清空", badId));
        try { ConfigFile.Set(gameRoot, "active_profile", null); } catch { /* 写不进去也不影响运行 */ }
        try
        {
            var f = Path.Combine(Root(gameRoot), "active.json");
            if (File.Exists(f)) File.Delete(f);
        }
        catch { /* 同上 */ }
    }

    /// <summary>取默认档；第一次使用时按当前实际启用态+设置生成（而不是凭空给个空档）。</summary>
    public static Profile GetOrCreateDefault(string gameRoot)
    {
        var p = Load(gameRoot, DefaultId);
        if (p != null) return p;
        Paths.Log(L("[配置档] 首次使用：按当前实际状态生成默认配置档"));
        p = new Profile { Id = DefaultId, Name = "默认", Created = Now() };
        CaptureInto(gameRoot, p);
        Save(gameRoot, p);
        return p;
    }

    // ———————————————————————————— 写 ————————————————————————————

    /// <summary>保存配置档（自动补 created、刷新 updated、自动建目录）。
    /// id 非法时拒绝落盘 —— 绝不把文件写到 profiles/ 之外。</summary>
    public static void Save(string gameRoot, Profile p)
    {
        if (p == null) return;
        Normalize(p);
        if (!PathGuard.IsSafeName(p.Id))
        {
            Paths.Log(L("[配置档] 非法 id，已拒绝保存：{0}", p.Id ?? ""));
            return;
        }
        var now = Now();
        if (string.IsNullOrWhiteSpace(p.Created)) p.Created = now;
        p.Updated = now;
        Directory.CreateDirectory(Root(gameRoot));
        var path = PathOf(gameRoot, p.Id);
        if (!PathGuard.Under(Root(gameRoot), path))
        {
            Paths.Log(L("[配置档] 拒绝写入配置档目录之外的路径：{0}", path));
            return;
        }
        Paths.SafeWrite(path, JsonSerializer.Serialize(p, JsonWrite));
    }

    /// <summary>新建配置档。fromProfile 指定则复制那份档（启用表+设置+章节），
    /// 否则以**当前实际启用态**为起点（等价于「把现在的状态存成一个档」）。
    /// id 已存在时不覆盖，打印提示并原样返回已存在的那份。</summary>
    public static Profile Create(string gameRoot, string id, string? name = null, string? fromProfile = null, string? description = null)
    {
        if (!PathGuard.IsSafeName(id))
        {
            Paths.Log(L("[配置档] 非法 id：{0}", id ?? ""));
            return new Profile { Id = id ?? "" };
        }
        var exist = Load(gameRoot, id);
        if (exist != null)
        {
            Paths.Log(L("[配置档] 已存在同名档，未覆盖：{0}", id));
            return exist;
        }

        Profile p;
        var src = string.IsNullOrWhiteSpace(fromProfile) ? null : Load(gameRoot, fromProfile!);
        if (!string.IsNullOrWhiteSpace(fromProfile) && src == null)
            Paths.Log(L("[配置档] 找不到来源档 {0}，改为按当前实际状态生成", fromProfile));

        if (src != null)
        {
            p = Clone(src, id, name);
            if (description != null) p.Description = description;
        }
        else
        {
            p = new Profile { Id = id };
            CaptureInto(gameRoot, p);
            p.Name = string.IsNullOrWhiteSpace(name) ? id : name!;
            if (description != null) p.Description = description;
        }
        p.Created = Now();
        Save(gameRoot, p);
        Paths.Log(L("[配置档] 已创建 {0}（启用 {1} 个 mod）", p.Id, p.Enabled.Count));
        return p;
    }

    /// <summary>复制一份配置档为新 id（源不存在或目标已存在 → false，不覆盖）。</summary>
    public static bool Copy(string gameRoot, string srcId, string dstId, string? newName = null)
    {
        if (!PathGuard.IsSafeName(dstId))
        {
            Paths.Log(L("[配置档] 非法 id：{0}", dstId ?? ""));
            return false;
        }
        var src = Load(gameRoot, srcId);
        if (src == null)
        {
            Paths.Log(L("[配置档] 找不到配置档 {0}", srcId));
            return false;
        }
        if (Load(gameRoot, dstId) != null)
        {
            Paths.Log(L("[配置档] 目标已存在，未覆盖：{0}", dstId));
            return false;
        }
        var p = Clone(src, dstId, newName);
        p.Created = Now();
        Save(gameRoot, p);
        Paths.Log(L("[配置档] 已复制 {0} → {1}", srcId, dstId));
        return true;
    }

    private static Profile Clone(Profile src, string newId, string? newName)
    {
        Normalize(src);
        return new Profile
        {
            Id = newId,
            Name = string.IsNullOrWhiteSpace(newName) ? (string.IsNullOrWhiteSpace(src.Name) ? newId : src.Name) : newName!,
            Description = src.Description,
            Created = src.Created,
            Enabled = new List<string>(src.Enabled),
            Settings = CloneSettings(src.Settings),
            Chapters = new List<string>(src.Chapters),
            ModsRoot = string.IsNullOrWhiteSpace(src.ModsRoot) ? "mods" : src.ModsRoot,
            Note = src.Note
        };
    }

    /// <summary>重命名（改 id = 换文件名）。先写新文件成功后再删旧文件，最后同步活动档指针。</summary>
    public static bool Rename(string gameRoot, string id, string newId)
    {
        if (!PathGuard.IsSafeName(newId))
        {
            Paths.Log(L("[配置档] 非法 id：{0}", newId ?? ""));
            return false;
        }
        var p = Load(gameRoot, id);
        if (p == null)
        {
            Paths.Log(L("[配置档] 找不到配置档 {0}", id));
            return false;
        }
        if (string.Equals(id, newId, StringComparison.Ordinal))
        {
            Paths.Log(L("[配置档] 新旧 id 相同：{0}", id));
            return false;
        }
        if (Load(gameRoot, newId) != null)
        {
            Paths.Log(L("[配置档] 目标已存在，未覆盖：{0}", newId));
            return false;
        }

        var oldPath = PathOf(gameRoot, id);
        p.Id = newId;
        Save(gameRoot, p);   // 先保住新文件，再删旧的：中途失败也只是多一份档，不丢数据
        try { if (File.Exists(oldPath) && PathGuard.Under(Root(gameRoot), oldPath)) File.Delete(oldPath); }
        catch (Exception ex) { Paths.Log(L("[配置档] 旧文件删除失败：{0}", ex.Message)); }

        if (string.Equals(ActiveId(gameRoot), id, StringComparison.OrdinalIgnoreCase)) SetActive(gameRoot, newId);
        Paths.Log(L("[配置档] 已重命名 {0} → {1}", id, newId));
        return true;
    }

    /// <summary>删除配置档（只删 profiles/&lt;id&gt;.json，**绝不动 mod 文件**）。
    /// 默认档与当前活动档需要 force —— 否则打印提示并返回 false（CLI 用它给出退出码 2）。</summary>
    public static bool Delete(string gameRoot, string id, bool force = false)
    {
        if (!PathGuard.IsSafeName(id))
        {
            Paths.Log(L("[配置档] 非法 id：{0}", id ?? ""));
            return false;
        }
        if (string.Equals(id, DefaultId, StringComparison.OrdinalIgnoreCase) && !force)
        {
            Paths.Log(L("[配置档] {0} 是默认档，删除需 --force", DefaultId));
            return false;
        }
        var path = PathOf(gameRoot, id);
        if (!File.Exists(path))
        {
            Paths.Log(L("[配置档] 找不到配置档 {0}", id));
            return false;
        }
        var active = string.Equals(ActiveId(gameRoot), id, StringComparison.OrdinalIgnoreCase);
        if (active && !force)
        {
            Paths.Log(L("[配置档] {0} 是当前活动档，删除需 --force", id));
            return false;
        }
        if (!PathGuard.Under(Root(gameRoot), path))
        {
            Paths.Log(L("[配置档] 拒绝删除配置档目录之外的路径：{0}", path));
            return false;
        }
        try { File.Delete(path); }
        catch (Exception ex)
        {
            Paths.Log(L("[配置档] 删除失败：{0}", ex.Message));
            return false;
        }

        if (active)
        {
            var next = List(gameRoot).FirstOrDefault()?.Id;
            if (next != null) { SetActive(gameRoot, next); Paths.Log(L("[配置档] 活动档已切到 {0}", next)); }
            else
            {
                ConfigFile.Set(gameRoot, "active_profile", null);
                Paths.Log(L("[配置档] 已经没有配置档了，活动档指针已清空"));
            }
        }
        Paths.Log(L("[配置档] 已删除配置档 {0}（mod 文件未动）", id));
        return true;
    }

    /// <summary>活动档指针：config.json 的 active_profile（权威）+ profiles/active.json（镜像给外部工具）。
    /// 两份内容都相同时不写盘（配合 Apply 的幂等要求）。</summary>
    private static void SetActive(string gameRoot, string id)
    {
        if (!PathGuard.IsSafeName(id)) return;
        if (ConfigFile.GetString(gameRoot, "active_profile")?.Trim() != id)
            ConfigFile.Set(gameRoot, "active_profile", JsonValue.Create(id));
        try
        {
            Directory.CreateDirectory(Root(gameRoot));
            var f = Path.Combine(Root(gameRoot), "active.json");
            var want = new JsonObject { ["active"] = id }.ToJsonString(JsonWrite);
            var same = false;
            try { same = File.Exists(f) && string.Equals(File.ReadAllText(f).Trim(), want.Trim(), StringComparison.Ordinal); }
            catch { same = false; }
            if (!same) Paths.SafeWrite(f, want);
        }
        catch (Exception ex) { Paths.Log(L("[配置档] active.json 写入失败：{0}", ex.Message)); }
    }

    // ———————————————————————————— 应用 / 回采 ————————————————————————————

    /// <summary>应用配置档：逐个写 mods/&lt;...&gt;/mod.json 的 enabled + config.json 的设置与活动档。
    /// 返回**实际改动的 mod 数**（按 id 去重）；配置档不存在返回 -1。
    /// 幂等：值已一致的 mod / 设置键一律不写盘；找不到的 mod id 只警告不中断。</summary>
    public static int Apply(string gameRoot, string id)
    {
        var p = Load(gameRoot, id);
        if (p == null)
        {
            Paths.Log(L("[配置档] 找不到配置档 {0}", id));
            return -1;
        }

        var modsRoot = ResolveModsRoot(gameRoot, p);
        var mods = Mods.ScanMods(modsRoot, "root", includeDisabled: true, allChapters: true);
        var want = new HashSet<string>(
            p.Enabled.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()),
            StringComparer.OrdinalIgnoreCase);

        var hit = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in mods)
        {
            if (string.IsNullOrWhiteSpace(m.Id)) continue;
            var on = want.Contains(m.Id);
            if (on) hit.Add(m.Id);
            if (m.Enabled == on) continue;                       // 已经一致 → 不写盘、不计数
            if (!PathGuard.Under(Paths.ModsRoot(gameRoot), m.Dir))
                Paths.Log(L("[配置档] 提示：{0} 来自打包目录（{1}），启用态写在那里", m.Id, m.Dir));
            RewriteModEnabled(gameRoot, m, on);
            if (changed.Add(m.Id))
                Paths.Log(on ? L("[配置档] {0}: 启用", m.Id) : L("[配置档] {0}: 禁用", m.Id));
        }
        foreach (var miss in want.Where(x => !hit.Contains(x)).OrderBy(x => x, StringComparer.Ordinal))
            Paths.Log(L("[配置档] 忽略：找不到 mod {0}", miss));

        // config.json：只写真正变化的键（一次性读-改-写，未知键原样保留）
        var cfg = ConfigFile.Load(gameRoot);
        var changedKeys = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        foreach (var kv in p.Settings)
        {
            if (string.Equals(kv.Key, "active_profile", StringComparison.Ordinal)) continue;
            var cur = cfg.TryGetPropertyValue(kv.Key, out var c) ? c : null;
            var v = kv.Value?.DeepClone();
            if (!JsonEquals(cur, v)) changedKeys[kv.Key] = v;
        }
        if (changedKeys.Count > 0)
        {
            ConfigFile.SetMany(gameRoot, changedKeys);
            Paths.Log(L("[配置档] 已写入 {0} 项设置", changedKeys.Count));
        }
        SetActive(gameRoot, p.Id);

        Paths.Log(L("[配置档] 已应用 {0}：改动 {1} 个 mod，{2} 项设置", p.Id, changed.Count, changedKeys.Count));
        return changed.Count;
    }

    /// <summary>反向回采：扫描 mods/ 读当前实际启用态 + config.json 当前设置，写回该档。
    /// 返回该档记录的启用 mod 数；id 非法返回 -1。</summary>
    public static int CaptureFromLive(string gameRoot, string id)
    {
        if (!PathGuard.IsSafeName(id))
        {
            Paths.Log(L("[配置档] 非法 id：{0}", id ?? ""));
            return -1;
        }
        var p = Load(gameRoot, id) ?? new Profile { Id = id, Created = Now() };
        if (string.IsNullOrWhiteSpace(p.Name)) p.Name = id;
        CaptureInto(gameRoot, p);
        Save(gameRoot, p);
        Paths.Log(L("[配置档] 已按当前实际状态更新 {0}：启用 {1} 个 mod，{2} 项设置", p.Id, p.Enabled.Count, p.Settings.Count));
        return p.Enabled.Count;
    }

    /// <summary>把「当前实际状态」灌进档对象（不落盘）：启用表、章节表、以及仍未消失的设置键。
    /// 设置键范围 = 内置清单 ∪ 该档原本记过的键 —— 别的模块以后加的新键，只要档里出现过就继续跟着走。</summary>
    private static void CaptureInto(string gameRoot, Profile p)
    {
        Normalize(p);
        var mods = Mods.ScanMods(Paths.ModsRoot(gameRoot), "root", includeDisabled: true, allChapters: true);
        var on = mods.Where(m => m.Enabled && !string.IsNullOrWhiteSpace(m.Id)).ToList();
        p.Enabled = on.Select(m => m.Id)
                      .Distinct(StringComparer.OrdinalIgnoreCase)
                      .OrderBy(x => x, StringComparer.Ordinal)
                      .ToList();
        p.Chapters = on.Select(m => m.Chapter ?? "")
                       .Where(x => !string.IsNullOrWhiteSpace(x))
                       .Distinct(StringComparer.OrdinalIgnoreCase)
                       .OrderBy(ChapterRank).ThenBy(x => x, StringComparer.Ordinal)
                       .ToList();
        p.ModsRoot = "mods";

        var cfg = ConfigFile.Load(gameRoot);
        var keys = new List<string>(SettingKeys);
        foreach (var k in p.Settings.Keys)
            if (!keys.Contains(k, StringComparer.Ordinal)) keys.Add(k);
        var settings = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        foreach (var k in keys)
        {
            if (string.Equals(k, "active_profile", StringComparison.Ordinal)) continue;
            if (cfg.TryGetPropertyValue(k, out var v)) settings[k] = v?.DeepClone();
        }
        p.Settings = settings;
    }

    /// <summary>档里记的 mods 根：只接受「Neutraled 目录之下的相对路径」，
    /// 其它（绝对路径 / 带 .. / 越界）一律忽略并回退到 Paths.ModsRoot。</summary>
    private static string ResolveModsRoot(string gameRoot, Profile p)
    {
        var def = Paths.ModsRoot(gameRoot);
        var mr = (p.ModsRoot ?? "").Trim();
        if (mr.Length == 0) return def;
        var neutraled = Paths.NeutraledRoot(gameRoot);
        if (Path.IsPathRooted(mr) || mr.Contains("..", StringComparison.Ordinal))
        {
            Paths.Log(L("[配置档] 忽略非法 mods_root：{0}", mr));
            return def;
        }
        string full;
        try { full = Path.GetFullPath(Path.Combine(neutraled, mr)); }
        catch { Paths.Log(L("[配置档] 忽略非法 mods_root：{0}", mr)); return def; }
        if (!PathGuard.Under(neutraled, full))
        {
            Paths.Log(L("[配置档] 忽略越界的 mods_root：{0}", mr));
            return def;
        }
        return full;
    }

    /// <summary>把某个 mod 的 mod.json 里的 enabled 改成指定值（读-改-写：其它字段一个都不动）。
    /// 顺带把内存里的 ModEntry.Enabled 也改掉，避免同一次运行里后面再按旧值判断。
    /// 只允许写 Neutraled 目录之下的文件。</summary>
    internal static void RewriteModEnabled(string gameRoot, ModEntry m, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(m.Dir)) return;
        var mj = Path.Combine(m.Dir, "mod.json");
        if (!File.Exists(mj))
        {
            Paths.Log(L("[配置档] 忽略：找不到 mod.json（{0}）", mj));
            return;
        }
        if (!PathGuard.Under(Paths.NeutraledRoot(gameRoot), mj))
        {
            Paths.Log(L("[配置档] 拒绝写入 Neutraled 目录之外的路径：{0}", mj));
            return;
        }
        try
        {
            var obj = JsonNode.Parse(File.ReadAllText(mj), null,
                new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip })?.AsObject();
            if (obj == null)
            {
                Paths.Log(L("[配置档] 忽略：mod.json 不是对象（{0}）", mj));
                return;
            }
            obj["enabled"] = enabled;
            Paths.SafeWrite(mj, obj.ToJsonString(JsonWrite));
            m.Enabled = enabled;
        }
        catch (Exception ex)
        {
            Paths.Log(L("[配置档] 写入失败 {0}：{1}", mj, ex.Message));
        }
    }

    // ———————————————————————————— 导入 / 导出 ————————————————————————————

    /// <summary>导出单个档为一段自包含 JSON 文本（带 schema 版本号，便于以后升级格式）。
    /// 找不到该档返回空串（调用方据此给退出码 2）。</summary>
    public static string Export(string gameRoot, string id)
    {
        var p = Load(gameRoot, id);
        if (p == null)
        {
            Paths.Log(L("[配置档] 找不到配置档 {0}", id));
            return "";
        }
        var doc = new JsonObject
        {
            ["schema"] = 1,
            ["kind"] = "neutraled.profile",
            ["api"] = Paths.ApiVersion(),
            ["exported"] = Now(),
            ["profile"] = JsonSerializer.SerializeToNode(p, JsonWrite)
        };
        return doc.ToJsonString(JsonWrite);
    }

    /// <summary>从 Export 的文本导入一个档（也吃裸的档对象）。成功返回 1；
    /// 内容非法 / id 非法 / 重名且 !overwrite 都返回 0（并打印原因）。</summary>
    public static int Import(string gameRoot, string text, bool overwrite = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            Paths.Log(L("[配置档] 导入内容为空"));
            return 0;
        }
        Profile? p;
        try
        {
            var root = JsonNode.Parse(text, null,
                new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var node = root?["profile"] ?? root;
            p = node?.Deserialize<Profile>(Paths.Json);
        }
        catch (Exception ex)
        {
            Paths.Log(L("[配置档] 导入失败：{0}", ex.Message));
            return 0;
        }
        if (p == null)
        {
            Paths.Log(L("[配置档] 导入失败：内容里没有配置档"));
            return 0;
        }
        Normalize(p);
        if (!PathGuard.IsSafeName(p.Id))
        {
            Paths.Log(L("[配置档] 导入失败：非法 id {0}", p.Id ?? ""));
            return 0;
        }
        if (!overwrite && File.Exists(PathOf(gameRoot, p.Id)))
        {
            Paths.Log(L("[配置档] {0} 已存在（加 --force 覆盖）", p.Id));
            return 0;
        }
        if (string.IsNullOrWhiteSpace(p.Name)) p.Name = p.Id;
        Save(gameRoot, p);
        Paths.Log(L("[配置档] 已导入 {0}（启用 {1} 个 mod）", p.Id, p.Enabled.Count));
        return 1;
    }

    // ———————————————————————————— 人可读输出 ————————————————————————————

    /// <summary>列出所有配置档（每行以 [配置档] 开头，活动档带 *）。</summary>
    public static void PrintList(string gameRoot)
    {
        var all = List(gameRoot);
        var active = ActiveId(gameRoot);
        if (all.Count == 0)
        {
            Paths.Log(L("[配置档] 还没有配置档（用 --profile-new <id> 新建）"));
            return;
        }
        Paths.Log(L("[配置档] 配置档目录：{0}", Root(gameRoot)));
        foreach (var p in all)
        {
            var mark = string.Equals(p.Id, active, StringComparison.OrdinalIgnoreCase) ? "*" : " ";
            Paths.Log(L("[配置档] {0} {1}  {2}  启用 {3} 个 mod  更新 {4}",
                mark, p.Id, string.IsNullOrWhiteSpace(p.Name) ? "-" : p.Name,
                p.Enabled.Count, string.IsNullOrWhiteSpace(p.Updated) ? "-" : p.Updated));
        }
        Paths.Log(L("[配置档] 共 {0} 个配置档（活动：{1}）", all.Count, active));
    }

    /// <summary>显示一个配置档的详情（id 省略时显示活动档）。</summary>
    public static void PrintShow(string gameRoot, string? id = null)
    {
        var pid = string.IsNullOrWhiteSpace(id) ? ActiveId(gameRoot) : id!.Trim();
        var p = Load(gameRoot, pid);
        if (p == null)
        {
            Paths.Log(L("[配置档] 找不到配置档 {0}", pid));
            return;
        }
        Paths.Log(L("[配置档] id: {0}", p.Id));
        Paths.Log(L("[配置档] 名称: {0}", string.IsNullOrWhiteSpace(p.Name) ? "-" : p.Name));
        Paths.Log(L("[配置档] 说明: {0}", string.IsNullOrWhiteSpace(p.Description) ? "-" : p.Description));
        Paths.Log(L("[配置档] 创建: {0}  更新: {1}",
            string.IsNullOrWhiteSpace(p.Created) ? "-" : p.Created,
            string.IsNullOrWhiteSpace(p.Updated) ? "-" : p.Updated));
        Paths.Log(L("[配置档] 章节: {0}", p.Chapters.Count == 0 ? "-" : string.Join(", ", p.Chapters)));
        Paths.Log(L("[配置档] mods 根: {0}", p.ModsRoot));
        Paths.Log(L("[配置档] 启用 mod：{0} 个", p.Enabled.Count));
        foreach (var mid in p.Enabled) Paths.Log(L("[配置档]   - {0}", mid));
        Paths.Log(L("[配置档] 设置：{0} 项", p.Settings.Count));
        foreach (var k in p.Settings.Keys.OrderBy(x => x, StringComparer.Ordinal))
            Paths.Log(L("[配置档]   - {0} = {1}", k, p.Settings[k]?.ToJsonString(Paths.Json) ?? "null"));
        if (!string.IsNullOrWhiteSpace(p.Note)) Paths.Log(L("[配置档] 备注: {0}", p.Note));
    }
}
