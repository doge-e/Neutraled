using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>一份 mod 版本快照的元数据（snapshots/&lt;modId&gt;/&lt;version&gt;/.snapshot.json）。
///
/// 键名严格按 SPEC §3.2 的示例：modId / sourceRef 是驼峰，其余全小写；
/// api 层（builder/Api.cs）与网页端直接读这些键，改名会让前端读不到值。</summary>
public sealed class SnapInfo
{
    [JsonPropertyName("modId")] public string ModId { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("author")] public string Author { get; set; } = "";
    [JsonPropertyName("created")] public string Created { get; set; } = "";

    /// <summary>来源：live（当前装着的这份）| dir（导入的目录）| zip（导入的压缩包）| gamebanana。</summary>
    [JsonPropertyName("source")] public string Source { get; set; } = "live";

    /// <summary>来源引用：导入时的原始路径 / 下载地址；live 快照为空。</summary>
    [JsonPropertyName("sourceRef")] public string SourceRef { get; set; } = "";

    [JsonPropertyName("files")] public int Files { get; set; }
    [JsonPropertyName("bytes")] public long Bytes { get; set; }

    /// <summary>内容树指纹（TreeHash）：相对路径 + 长度 + 单文件 SHA256 汇总后再哈希。</summary>
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";

    [JsonPropertyName("note")] public string Note { get; set; } = "";

    /// <summary>这份快照是怎么来的（SPEC 里的「Action 枚举值」，取值见 SnapActions）。
    /// 纯记录用：回切时不看 action，只看目录内容。</summary>
    [JsonPropertyName("action")] public string Action { get; set; } = SnapActions.Create;
}

/// <summary>SnapInfo.Action 的取值。用字符串常量而不是 enum：
/// Paths.Json 没装 JsonStringEnumConverter，enum 会落成数字，人看 .snapshot.json 时读不懂。</summary>
public static class SnapActions
{
    /// <summary>手动 --snapshot-create。</summary>
    public const string Create = "create";

    /// <summary>部署前的保底快照（AutoSnapshotAll / 回切前自动存的那份）。</summary>
    public const string Auto = "auto";

    /// <summary>从目录或压缩包导入。</summary>
    public const string Import = "import";

    /// <summary>回切动作本身（保留位，目前不写这种 action）。</summary>
    public const string Use = "use";

    /// <summary>GameBanana 一键安装时留的档。</summary>
    public const string GameBanana = "gamebanana";
}

/// <summary>每 mod 版本快照：把 mods/&lt;id&gt; 的整个目录复制成 snapshots/&lt;modId&gt;/&lt;version&gt;/，随时原地回切。
///
/// 设计要点（改动前先读）：
///   · 建立（Create）与回切（Use）**两个方向都真实复制**（File.Copy），绝不硬链接。
///     实测翻车记录：早期版本建立快照时走 Platform.LinkOrCopy（硬链接省盘），快照与 mods/<id> 共享 inode；
///     就地改一下 live 的 gml\main.gml（编辑器保存、安装器解包覆盖、本仓大量 File.Copy(...,true) 都是
///     就地截断写法），快照里的这份内容跟着一起变，--snapshot-use 回切出来的还是被改过的内容。
///     硬链接会让回切后的 live 文件和快照共享同一块数据，用户之后改一次 mod 文件就把快照也改了 —— 宁可多占盘。
///     ⚠ 真需要“共享数据”的调用方（缓存之类）请直接用 Platform.TryHardLink，别在快照这里改回去。
///   · 删除只允许删 snapshots/ 下的路径，动手前一律过 PathGuard.Under + PathGuard.IsSafeName（防 .. 越界）。
///   · TreeHash 跳过 .snapshot.json 自身（否则写一次元数据指纹就变一次）。
/// </summary>
public static class Snapshots
{
    /// <summary>元数据文件名（放在每份快照目录内，不进内容指纹、也不参与复制）。</summary>
    private const string MetaName = ".snapshot.json";

    /// <summary>写元数据用（缩进 + 非 ASCII 原样，与 Profiles 的 JsonWrite 保持一致）。</summary>
    private static readonly JsonSerializerOptions JsonWrite = new JsonSerializerOptions(Paths.Json) { WriteIndented = true };

    /// <summary>快照根目录（Neutraled/snapshots）。</summary>
    public static string Root(string gameRoot) => Paths.SnapshotsRoot(gameRoot);

    /// <summary>某份快照的目录；modId / version 非法时返回空串（调用方必须先判空，绝不能拿它去写盘或删盘）。</summary>
    public static string DirOf(string gameRoot, string modId, string version)
        => PathGuard.IsSafeName(modId) && PathGuard.IsSafeName(version)
            ? Path.Combine(Root(gameRoot), modId, version)
            : "";

    private static string Now() => DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>空值统一打成 "-"（避免把中文塞进 L 的参数里，见 lint 规则 13）。</summary>
    private static string Dash(string? s) => string.IsNullOrWhiteSpace(s) ? "-" : s!;

    /// <summary>人可读体积（InvariantGlobalization 下也必须显式给 InvariantCulture，免得小数点变逗号）。</summary>
    private static string HumanSize(long bytes)
    {
        if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        double kb = bytes / 1024.0;
        if (kb < 1024) return kb.ToString("0.#", CultureInfo.InvariantCulture) + " KB";
        double mb = kb / 1024.0;
        if (mb < 1024) return mb.ToString("0.#", CultureInfo.InvariantCulture) + " MB";
        return (mb / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " GB";
    }

    /// <summary>版本号的语义序比较：按 . - _ 切段，两边都是数字按数字比（1.10 &gt; 1.9），否则按序号比；
    /// 前缀完全相同则段数多的算新。数字超过 long 范围时退回字符串比较 —— 版本号不会这么长。</summary>
    private static int CompareVersions(string a, string b)
    {
        var xs = a.Split(new[] { '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        var ys = b.Split(new[] { '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < xs.Length && i < ys.Length; i++)
        {
            long xa, yb;
            bool nx = long.TryParse(xs[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out xa);
            bool ny = long.TryParse(ys[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out yb);
            int c = nx && ny ? xa.CompareTo(yb) : string.Compare(xs[i], ys[i], StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
        }
        return xs.Length.CompareTo(ys.Length);
    }

    /// <summary>找某个 mod 在当前 mods/ 里的实际目录。
    /// 优先走 Mods.ScanMods（mods/名字/作者/章节 与旧格式都能解析），扫描抛异常时退回直拼 mods/&lt;id&gt;。</summary>
    private static string LiveDir(string gameRoot, string modId)
    {
        try
        {
            var mods = Mods.ScanMods(Paths.ModsRoot(gameRoot), "root", includeDisabled: true, allChapters: true);
            var hit = mods.FirstOrDefault(m => string.Equals(m.Id, modId, StringComparison.OrdinalIgnoreCase));
            if (hit != null && !string.IsNullOrWhiteSpace(hit.Dir) && Directory.Exists(hit.Dir)) return hit.Dir;
        }
        catch
        {
            // 某个 mod 的 mod.json 坏掉会让扫描整体抛异常；此时退回直拼路径，不让一次快照拖垮整条命令
        }
        var direct = Path.Combine(Paths.ModsRoot(gameRoot), modId);
        return Directory.Exists(direct) ? direct : "";
    }

    /// <summary>某 mod 当前版本号：mod.json 的 version &gt; 扫描结果的 Version &gt; "1.0.0"。</summary>
    private static string LiveVersion(string gameRoot, string modId, string liveDir)
    {
        var v = ModField(liveDir, "version");
        if (!string.IsNullOrWhiteSpace(v) && PathGuard.IsSafeName(v)) return v!;
        try
        {
            var hit = Mods.ScanMods(Paths.ModsRoot(gameRoot), "root", includeDisabled: true, allChapters: true)
                .FirstOrDefault(m => string.Equals(m.Id, modId, StringComparison.OrdinalIgnoreCase));
            if (hit != null && !string.IsNullOrWhiteSpace(hit.Version) && PathGuard.IsSafeName(hit.Version)) return hit.Version.Trim();
        }
        catch
        {
            // 同上：扫描失败不影响默认版本号
        }
        return "1.0.0";
    }

    /// <summary>读 mod.json 里的一个字符串字段；文件缺失/JSON 坏掉/字段是数字都返回 null。</summary>
    private static string? ModField(string dir, string key)
    {
        var mj = Path.Combine(dir, "mod.json");
        if (!File.Exists(mj)) return null;
        try
        {
            var v = JsonNode.Parse(File.ReadAllText(mj))?[key]?.GetValue<string>();
            return string.IsNullOrWhiteSpace(v) ? null : v!.Trim();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>列目录下的所有文件相对路径（统一 / 分隔，跳过 .snapshot.json）。</summary>
    private static List<string> RelativeFiles(string dir)
    {
        var list = new List<string>();
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                if (string.Equals(Path.GetFileName(f), MetaName, StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(Path.GetRelativePath(dir, f).Replace('\\', '/'));
            }
        }
        catch (Exception ex)
        {
            Paths.Log(L("[快照] 读取目录失败: {0}（{1}）", dir, ex.Message));
        }
        return list;
    }

    /// <summary>统计目录内容（跳过 .snapshot.json 自身）。</summary>
    private static (int files, long bytes) Measure(string dir)
    {
        int files = 0;
        long bytes = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                if (string.Equals(Path.GetFileName(f), MetaName, StringComparison.OrdinalIgnoreCase)) continue;
                files++;
                try { bytes += new FileInfo(f).Length; } catch { /* 读不到长度就当 0，不影响文件数 */ }
            }
        }
        catch
        {
            // 目录半路消失：返回已统计到的部分
        }
        return (files, bytes);
    }

    /// <summary>目录创建时间（元数据缺失时的兜底时间戳）。</summary>
    private static string DirTime(string dir)
    {
        try { return Directory.GetCreationTime(dir).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture); }
        catch { return ""; }
    }

    /// <summary>读一份快照的元数据；.snapshot.json 缺失或损坏时按目录实际内容兜底并打印警告 ——
    /// 只要目录还在就能列出来，不会因为一个坏文件让整个列表消失。</summary>
    private static SnapInfo ReadOrSynthesize(string modId, string version, string dir)
    {
        var meta = Path.Combine(dir, MetaName);
        if (File.Exists(meta))
        {
            try
            {
                var info = JsonSerializer.Deserialize<SnapInfo>(File.ReadAllText(meta), Paths.Json);
                if (info != null)
                {
                    if (string.IsNullOrWhiteSpace(info.ModId)) info.ModId = modId;
                    if (string.IsNullOrWhiteSpace(info.Version)) info.Version = version;
                    if (string.IsNullOrWhiteSpace(info.Created)) info.Created = DirTime(dir);
                    if (info.Files <= 0)
                    {
                        var (f, b) = Measure(dir);
                        info.Files = f;
                        info.Bytes = b;
                    }
                    return info;
                }
            }
            catch (Exception ex)
            {
                Paths.Log(L("[快照] 元数据损坏，按目录内容兜底: {0}（{1}）", meta, ex.Message));
            }
        }
        else
        {
            Paths.Log(L("[快照] 缺少元数据文件，按目录内容兜底: {0}", dir));
        }

        var (files, bytes) = Measure(dir);
        return new SnapInfo
        {
            ModId = modId,
            Version = version,
            Name = ModField(dir, "name") ?? modId,
            Author = ModField(dir, "author") ?? "",
            Created = DirTime(dir),
            Source = "live",
            Files = files,
            Bytes = bytes,
            Action = SnapActions.Create
        };
    }

    /// <summary>列出快照（modId 为空 = 全部）。同一 mod 内按版本语义序新→旧，mod 之间按 id 序。
    /// 目录不存在返回空表；单个坏档只警告不影响整表。</summary>
    public static List<SnapInfo> List(string gameRoot, string? modId = null)
    {
        var result = new List<SnapInfo>();
        var root = Root(gameRoot);
        if (!Directory.Exists(root)) return result;

        IEnumerable<string> modDirs;
        if (!string.IsNullOrWhiteSpace(modId))
        {
            if (!PathGuard.IsSafeName(modId))
            {
                Paths.Log(L("[快照] 非法 mod id: {0}", modId));
                return result;
            }
            var one = Path.Combine(root, modId!);
            modDirs = Directory.Exists(one) ? new[] { one } : Array.Empty<string>();
        }
        else
        {
            modDirs = Directory.EnumerateDirectories(root);
        }

        foreach (var md in modDirs)
        {
            var mId = Path.GetFileName(md);
            if (!PathGuard.IsSafeName(mId)) continue;
            foreach (var vd in Directory.EnumerateDirectories(md))
            {
                var ver = Path.GetFileName(vd);
                if (!PathGuard.IsSafeName(ver)) continue;
                result.Add(ReadOrSynthesize(mId, ver, vd));
            }
        }

        result.Sort((a, b) =>
        {
            var c = string.Compare(a.ModId, b.ModId, StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
            c = CompareVersions(b.Version, a.Version);
            if (c != 0) return c;
            return string.Compare(b.Created, a.Created, StringComparison.Ordinal);
        });
        return result;
    }

    /// <summary>读一份快照的元数据；不存在 / 名字非法返回 null（同时打印原因）。</summary>
    public static SnapInfo? Info(string gameRoot, string modId, string version)
    {
        var dir = DirOf(gameRoot, modId, version);
        if (dir.Length == 0)
        {
            Paths.Log(L("[快照] 非法 mod id 或版本: {0} @ {1}", modId, version));
            return null;
        }
        return Directory.Exists(dir) ? ReadOrSynthesize(modId, version, dir) : null;
    }

    /// <summary>该 mod 现有几份快照。</summary>
    private static int CountVersions(string gameRoot, string modId)
    {
        var md = Path.Combine(Root(gameRoot), modId);
        try { return Directory.Exists(md) ? Directory.EnumerateDirectories(md).Count() : 0; }
        catch { return 0; }
    }

    /// <summary>把 mod 当前目录（live）整体存成一份快照。
    /// version 缺省 → 读 mod.json 的 version；同版本已存在则自动加后缀 -2 / -3，**绝不覆盖**旧档。
    /// 非法 modId / 找不到 mod 目录都会抛 InvalidOperationException（CLI 外层会打印并返回 1）。</summary>
    public static SnapInfo Create(string gameRoot, string modId, string? version = null, string source = "live", string? note = null)
    {
        if (!PathGuard.IsSafeName(modId)) throw new InvalidOperationException(L("[快照] 非法 mod id: {0}", modId));
        var live = LiveDir(gameRoot, modId);
        if (live.Length == 0) throw new InvalidOperationException(L("[快照] 找不到 mod 目录: {0}", modId));
        var ver = string.IsNullOrWhiteSpace(version) ? LiveVersion(gameRoot, modId, live) : version!.Trim();
        if (!PathGuard.IsSafeName(ver)) throw new InvalidOperationException(L("[快照] 非法快照版本: {0}", ver));
        return CreateFrom(gameRoot, modId, ver, live, string.IsNullOrWhiteSpace(source) ? "live" : source, "", note, SnapActions.Create);
    }

    /// <summary>把 srcDir 的内容存成 snapshots/&lt;modId&gt;/&lt;version&gt; 并写元数据（Create / Import / AutoSnapshotAll 共用）。</summary>
    private static SnapInfo CreateFrom(string gameRoot, string modId, string version, string srcDir,
        string source, string sourceRef, string? note, string action)
    {
        var root = Root(gameRoot);
        var final = version;
        for (int n = 2; n < 1000 && Directory.Exists(Path.Combine(root, modId, final)); n++)
            final = version + "-" + n;

        var dir = Path.Combine(root, modId, final);
        if (!PathGuard.Under(root, dir)) throw new InvalidOperationException(L("[快照] 拒绝写到快照目录之外: {0}", dir));
        if (Directory.Exists(dir)) throw new InvalidOperationException(L("[快照] 版本号已被占满，无法分配: {0}", version));
        if (!string.Equals(final, version, StringComparison.Ordinal))
            Paths.Log(L("[快照] {0} @ {1} 已存在，改存为 {2}", modId, version, final));

        Directory.CreateDirectory(dir);
        var (files, bytes) = CopyTree(srcDir, dir);

        var info = new SnapInfo
        {
            ModId = modId,
            Version = final,
            Name = ModField(srcDir, "name") ?? modId,
            Author = ModField(srcDir, "author") ?? "",
            Created = Now(),
            Source = source,
            SourceRef = sourceRef,
            Files = files,
            Bytes = bytes,
            Note = note ?? "",
            Action = action
        };
        info.Sha256 = TreeHash(dir);
        WriteMeta(root, dir, info);
        Paths.Log(L("[快照] 已保存 {0} @ {1}: {2} 个文件, {3}", modId, final, files, HumanSize(bytes)));
        return info;
    }

    /// <summary>把目录内容**真实复制**到目标目录（独立 inode；绝不硬链接，理由见类注释里的实测记录）。
    /// 返回 (文件数, 字节数)；.snapshot.json 不参与复制（元数据由 WriteMeta 单独写，避免把上一份的带进来）。</summary>
    private static (int files, long bytes) CopyTree(string srcDir, string dstDir)
    {
        int files = 0;
        long bytes = 0;
        Directory.CreateDirectory(dstDir);

        foreach (var f in Directory.EnumerateFiles(srcDir))
        {
            var name = Path.GetFileName(f);
            if (string.Equals(name, MetaName, StringComparison.OrdinalIgnoreCase)) continue;
            var dst = Path.Combine(dstDir, name);
            long len = 0;
            try { len = new FileInfo(f).Length; } catch { /* 长度拿不到就记 0，文件本身照拷 */ }
            File.Copy(f, dst, overwrite: true);
            // 复制后自己确认落地成功：目标缺了就说明没拷成，宁可整体失败，也别留一份“看起来有”的空快照。
            if (!File.Exists(dst)) throw new IOException(L("[快照] 复制失败: {0}", f));
            files++;
            bytes += len;
        }

        foreach (var d in Directory.EnumerateDirectories(srcDir))
        {
            var name = Path.GetFileName(d);
            if (name.Length == 0) continue;
            var r = CopyTree(d, Path.Combine(dstDir, name));
            files += r.files;
            bytes += r.bytes;
        }
        return (files, bytes);
    }

    /// <summary>写 .snapshot.json（先做前缀校验，绝不写到 snapshots/ 之外）。</summary>
    private static void WriteMeta(string snapshotsRoot, string dir, SnapInfo info)
    {
        var meta = Path.Combine(dir, MetaName);
        if (!PathGuard.Under(snapshotsRoot, meta)) throw new InvalidOperationException(L("[快照] 拒绝写到快照目录之外: {0}", meta));
        Paths.SafeWrite(meta, JsonSerializer.Serialize(info, JsonWrite));
    }

    /// <summary>内容指纹：所有文件（跳过 .snapshot.json）按「相对路径 + 长度 + 单文件 SHA256」排序后汇总再哈希。
    /// 目录不存在返回空串。路径统一用 / 并排序，保证同样的内容在任何机器上算出同一串。</summary>
    public static string TreeHash(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return "";
        var lines = new List<string>();
        CollectHashes(dir, dir, lines);
        lines.Sort(StringComparer.Ordinal);
        var sb = new StringBuilder();
        foreach (var line in lines) sb.Append(line).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    private static void CollectHashes(string root, string cur, List<string> lines)
    {
        foreach (var f in Directory.EnumerateFiles(cur))
        {
            if (string.Equals(Path.GetFileName(f), MetaName, StringComparison.OrdinalIgnoreCase)) continue;
            var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
            long len = 0;
            try { len = new FileInfo(f).Length; } catch { /* 长度读不到记 0 */ }
            string hash;
            try
            {
                using var fs = File.OpenRead(f);
                hash = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
            }
            catch (Exception ex)
            {
                // 单文件读不了（被占用/没权限）不该让整份指纹失败：用异常类型名占位，仍然能区分「这份和别的不一样」
                hash = "err:" + ex.GetType().Name;
            }
            lines.Add(rel + "\t" + len.ToString(CultureInfo.InvariantCulture) + "\t" + hash);
        }
        foreach (var d in Directory.EnumerateDirectories(cur)) CollectHashes(root, d, lines);
    }

    /// <summary>原地回切到某份快照：先把当前内容另存一份 auto-&lt;yyyyMMdd-HHmmss&gt; 快照（force 时跳过），
    /// 再用存档覆盖 mod 目录 —— 存档里有而当前目录没有的文件会被补上，当前目录多出来的文件会被删掉（否则回切不干净）。
    /// 返回恢复的文件数；找不到快照 / 名字非法 / 后路存不下时返回 -1。
    ///
    /// 为什么这里不用硬链接：硬链接会让回切后的 live 文件与存档共享同一块数据，
    /// 之后用户改一次 mod 文件就把存档也改了，快照就白存了 —— 宁可多占盘也用 File.Copy。</summary>
    public static int Use(string gameRoot, string modId, string version, bool force = false)
    {
        var dir = DirOf(gameRoot, modId, version);
        if (dir.Length == 0)
        {
            Paths.Log(L("[快照] 非法 mod id 或版本: {0} @ {1}", modId, version));
            return -1;
        }
        if (!Directory.Exists(dir))
        {
            Paths.Log(L("[快照] 找不到快照: {0} @ {1}", modId, version));
            return -1;
        }

        // 硬链接的副作用：快照文件与 live 文件可能共享同一块数据，安装器/编辑器就地覆盖 live 会把快照一起改掉。
        // 回切前核对一次内容指纹，发现漂移只警告不中止 —— 紧接着当前内容会另存成 auto 快照，用户仍有后路。
        var info = Info(gameRoot, modId, version);
        if (info != null && !string.IsNullOrWhiteSpace(info.Sha256))
        {
            var cur = TreeHash(dir);
            if (!string.Equals(cur, info.Sha256, StringComparison.OrdinalIgnoreCase))
                Paths.Log(L("[快照] 警告：{0} @ {1} 的内容与建立时不一致（可能被就地修改过），回切结果未必可靠", modId, version));
        }

        var modsRoot = Path.GetFullPath(Paths.ModsRoot(gameRoot));
        var live = LiveDir(gameRoot, modId);
        if (live.Length == 0) live = Path.Combine(modsRoot, modId);
        live = Path.GetFullPath(live);
        if (string.Equals(live, modsRoot, StringComparison.OrdinalIgnoreCase) || !PathGuard.Under(modsRoot, live))
        {
            Paths.Log(L("[快照] 拒绝操作 mods 之外的目录: {0}", live));
            return -1;
        }

        // 1) 后路：当前内容先存成 auto 快照；存不下就别动用户目录（宁可失败也不能没有回退点）
        if (!force)
        {
            if (Directory.Exists(live))
            {
                try
                {
                    var auto = "auto-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                    CreateFrom(gameRoot, modId, auto, live, "live", "",
                        L("回切 {0} @ {1} 前的自动快照", modId, version), SnapActions.Use);
                }
                catch (Exception ex)
                {
                    Paths.Log(L("[快照] 回切前无法保存当前内容，已中止: {0}", ex.Message));
                    return -1;
                }
            }
            else
            {
                Paths.Log(L("[快照] 当前没有 mod 目录，跳过自动快照: {0}", modId));
            }
        }

        // 2) 覆盖：先补/盖存档里的文件，再删当前多出来的文件
        int restored = 0;
        int removed = 0;
        Directory.CreateDirectory(live);
        foreach (var rel in RelativeFiles(dir))
        {
            var src = Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar));
            var dst = Path.Combine(live, rel.Replace('/', Path.DirectorySeparatorChar));
            var parent = Path.GetDirectoryName(dst);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            if (File.Exists(dst)) File.Delete(dst);          // 先删再拷：断掉可能存在的硬链接，保证内容就是存档那份
            File.Copy(src, dst, overwrite: true);
            restored++;
        }
        var keep = new HashSet<string>(RelativeFiles(dir), StringComparer.OrdinalIgnoreCase);
        foreach (var rel in RelativeFiles(live))
        {
            if (keep.Contains(rel)) continue;
            var p = Path.Combine(live, rel.Replace('/', Path.DirectorySeparatorChar));
            try { File.Delete(p); removed++; }
            catch (Exception ex) { Paths.Log(L("[快照] 删除失败: {0}（{1}）", p, ex.Message)); }
        }
        CleanEmptyDirs(live);

        Paths.Log(L("[快照] 已回切 {0} @ {1}: 恢复 {2} 个文件, 清理 {3} 个多余文件", modId, version, restored, removed));
        return restored;
    }

    /// <summary>清掉目录下已经空了的子目录（只动 dir 之下，dir 自己保留）。</summary>
    private static void CleanEmptyDirs(string dir)
    {
        try
        {
            var sep = Path.DirectorySeparatorChar;
            foreach (var d in Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories)
                         .OrderByDescending(x => x.Count(c => c == sep)))
            {
                try { if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d); } catch { /* 删不掉就留着，不影响回切结果 */ }
            }
        }
        catch
        {
            // 枚举失败（目录刚被删掉）直接放弃清理
        }
    }

    /// <summary>从目录或压缩包（zip/7z/rar，走 SharpCompress）导入一份快照。
    /// version 缺省 → 包内 mod.json 的 version &gt; 目录名/包名 &gt; 1.0.0。
    /// 非法 modId / 路径不存在 / 解压失败一律抛 InvalidOperationException（CLI 外层打印并返回 1）。</summary>
    public static SnapInfo Import(string gameRoot, string modId, string pathOrZip, string? version = null)
    {
        if (!PathGuard.IsSafeName(modId)) throw new InvalidOperationException(L("[快照] 非法 mod id: {0}", modId));
        if (string.IsNullOrWhiteSpace(pathOrZip)) throw new InvalidOperationException(L("[快照] 路径为空"));
        var src = Path.GetFullPath(pathOrZip);

        if (Directory.Exists(src))
        {
            var ver = string.IsNullOrWhiteSpace(version) ? VersionOfDir(src) : version!.Trim();
            if (!PathGuard.IsSafeName(ver)) throw new InvalidOperationException(L("[快照] 非法快照版本: {0}", ver));
            return CreateFrom(gameRoot, modId, ver, src, "dir", src, null, SnapActions.Import);
        }
        if (!File.Exists(src)) throw new InvalidOperationException(L("[快照] 路径不存在: {0}", pathOrZip));

        // 压缩包：先解到系统临时目录（不是游戏目录，别把半成品留给用户），复制完立刻清掉
        var tmp = Path.Combine(Path.GetTempPath(), "ntl-snap-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        try
        {
            Directory.CreateDirectory(tmp);
            if (ExtractArchive(tmp, src) < 0) throw new InvalidOperationException(L("[快照] 解压失败: {0}", src));
            var inner = SingleRoot(tmp);
            var ver = string.IsNullOrWhiteSpace(version)
                ? FirstNonEmpty(VersionOfDir(inner), VersionOfFile(src), "1.0.0")
                : version!.Trim();
            if (!PathGuard.IsSafeName(ver)) throw new InvalidOperationException(L("[快照] 非法快照版本: {0}", ver));
            return CreateFrom(gameRoot, modId, ver, inner, "zip", src, null, SnapActions.Import);
        }
        finally
        {
            try { if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true); } catch { /* 临时目录清不掉留给系统 */ }
        }
    }

    private static string FirstNonEmpty(params string?[] xs)
    {
        foreach (var x in xs)
            if (!string.IsNullOrWhiteSpace(x)) return x!.Trim();
        return "";
    }

    /// <summary>目录名当版本号（包内没有 mod.json 时的兜底）。</summary>
    private static string VersionOfDir(string dir)
    {
        var v = ModField(dir, "version");
        if (!string.IsNullOrWhiteSpace(v)) return v!;
        try { return Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir))); }
        catch { return ""; }
    }

    /// <summary>压缩包文件名当版本号。</summary>
    private static string VersionOfFile(string file)
    {
        try { return Path.GetFileNameWithoutExtension(file); }
        catch { return ""; }
    }

    /// <summary>压缩包常见的「外面又套一层同名文件夹」：临时目录里只有唯一一个目录且没有文件时一路下沉到真正的内容根。</summary>
    private static string SingleRoot(string dir)
    {
        var cur = dir;
        for (int i = 0; i < 8; i++)
        {
            var hasFile = Directory.EnumerateFiles(cur).Any();
            var dirs = Directory.EnumerateDirectories(cur).ToList();
            if (hasFile || dirs.Count != 1) break;
            cur = dirs[0];
        }
        return cur;
    }

    /// <summary>解压到 destDir（SharpCompress 统一入口，zip/7z/rar 通吃），逐条校验路径防 zip-slip。
    /// 返回写出的文件数；-1 = 失败（原因已打印）。</summary>
    private static int ExtractArchive(string destDir, string archivePath)
    {
        try
        {
            var destFull = Path.GetFullPath(destDir);
            var n = 0;
            using var archive = SharpCompress.Archives.ArchiveFactory.OpenArchive(archivePath);
            foreach (var entry in archive.Entries)
            {
                if (entry.IsDirectory) continue;
                var key = (entry.Key ?? "").Replace('\\', '/').TrimStart('/');
                if (key.Length == 0) continue;
                if (key.Contains(':'))
                {
                    Paths.Log(L("[快照] 压缩包内含非法路径，已拒绝: {0}", key));
                    return -1;
                }
                var dest = Path.GetFullPath(Path.Combine(destFull, key.Replace('/', Path.DirectorySeparatorChar)));
                if (!PathGuard.Under(destFull, dest))
                {
                    Paths.Log(L("[快照] 压缩包内含越界路径，已拒绝: {0}", key));
                    return -1;
                }
                var parent = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                using var es = entry.OpenEntryStream();
                using var fs = File.Create(dest);
                es.CopyTo(fs);
                n++;
            }
            return n;
        }
        catch (Exception ex)
        {
            Paths.Log(L("[快照] 解压失败: {0}", ex.Message));
            return -1;
        }
    }

    /// <summary>删除一份快照（只删 snapshots/&lt;modId&gt;/&lt;version&gt;，**绝不动 mods/ 里的任何文件**）。
    /// 默认拒绝删「该 mod 最后一份快照」（那是唯一的回滚依据），要加 --force；
    /// 非法 modId/version 直接拒绝，动手前过 PathGuard.Under 前缀校验。</summary>
    public static bool Delete(string gameRoot, string modId, string version, bool force = false)
    {
        var root = Root(gameRoot);
        var dir = DirOf(gameRoot, modId, version);
        if (dir.Length == 0)
        {
            Paths.Log(L("[快照] 非法 mod id 或版本: {0} @ {1}", modId, version));
            return false;
        }
        if (!PathGuard.Under(root, dir))
        {
            Paths.Log(L("[快照] 拒绝删除快照目录之外的路径: {0}", dir));
            return false;
        }
        if (!Directory.Exists(dir))
        {
            Paths.Log(L("[快照] 找不到快照: {0} @ {1}", modId, version));
            return false;
        }
        if (!force && CountVersions(gameRoot, modId) <= 1)
        {
            Paths.Log(L("[快照] {0} @ {1} 是该 mod 最后一份快照，加 --force 才会删除", modId, version));
            return false;
        }

        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex)
        {
            Paths.Log(L("[快照] 删除失败: {0}", ex.Message));
            return false;
        }
        Paths.Log(L("[快照] 已删除 {0} @ {1}", modId, version));

        // 该 mod 已无任何快照 → 顺手清掉空的 modId 目录（同样只允许 snapshots/ 之下）
        var md = Path.Combine(root, modId);
        try
        {
            if (Directory.Exists(md) && PathGuard.Under(root, md) && !Directory.EnumerateFileSystemEntries(md).Any())
                Directory.Delete(md);
        }
        catch
        {
            // 空目录删不掉不影响结果
        }
        return true;
    }

    /// <summary>部署前的保底动作：给每个 mod 的当前版本各存一份快照；该版本已有快照就跳过 —— 幂等，可反复调用。
    /// 返回真正新建的快照数；单个 mod 失败只打印警告，不中断其余 mod。</summary>
    public static int AutoSnapshotAll(string gameRoot, List<ModEntry> mods)
    {
        if (mods == null || mods.Count == 0) return 0;
        int made = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in mods)
        {
            if (m == null || string.IsNullOrWhiteSpace(m.Id)) continue;
            if (!seen.Add(m.Id)) continue;                  // 同一 mod 多章节会重复出现，只处理一次
            if (!PathGuard.IsSafeName(m.Id))
            {
                Paths.Log(L("[快照] 跳过非法 mod id: {0}", m.Id));
                continue;
            }
            var ver = string.IsNullOrWhiteSpace(m.Version) ? "1.0.0" : m.Version.Trim();
            if (!PathGuard.IsSafeName(ver))
            {
                Paths.Log(L("[快照] 跳过非法版本号: {0} @ {1}", m.Id, ver));
                continue;
            }
            if (Directory.Exists(Path.Combine(Root(gameRoot), m.Id, ver)))
            {
                Paths.Log(L("[快照] {0} @ {1} 已存在，跳过", m.Id, ver));
                continue;
            }
            var dir = string.IsNullOrWhiteSpace(m.Dir) ? LiveDir(gameRoot, m.Id) : m.Dir;
            if (dir.Length == 0 || !Directory.Exists(dir))
            {
                Paths.Log(L("[快照] 跳过：找不到 mod 目录 {0}", m.Id));
                continue;
            }
            try
            {
                CreateFrom(gameRoot, m.Id, ver, dir, "live", "", null, SnapActions.Auto);
                made++;
            }
            catch (Exception ex)
            {
                Paths.Log(L("[快照] {0} 保存失败: {1}", m.Id, ex.Message));
            }
        }
        return made;
    }

    /// <summary>列出快照：每行以 [快照] 开头（方便 grep / 成帧断言），mod 过滤为空则列全部。</summary>
    public static void PrintList(string gameRoot, string? modId = null)
    {
        var all = List(gameRoot, modId);
        if (!string.IsNullOrWhiteSpace(modId))
        {
            if (all.Count == 0)
            {
                Paths.Log(L("[快照] mod {0} 还没有快照", modId));
                return;
            }
            foreach (var s in all) PrintOne(s);
            return;
        }
        if (all.Count == 0)
        {
            Paths.Log(L("[快照] 还没有快照"));
            return;
        }
        var modCount = all.Select(s => s.ModId).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        Paths.Log(L("[快照] 共 {0} 个 mod，{1} 份快照", modCount, all.Count));
        foreach (var s in all) PrintOne(s);
    }

    private static void PrintOne(SnapInfo s)
        => Paths.Log(L("[快照] {0} @ {1}  {2}  {3}  {4} 个文件  {5}  {6}",
            s.ModId, s.Version, s.Created, s.Source, s.Files, HumanSize(s.Bytes), Dash(s.Note)));

    /// <summary>显示一份快照的详情（每行都以 [快照] 开头）。</summary>
    public static void PrintShow(string gameRoot, string modId, string version)
    {
        var info = Info(gameRoot, modId, version);
        if (info == null)
        {
            Paths.Log(L("[快照] 找不到快照: {0} @ {1}", modId, version));
            return;
        }
        Paths.Log(L("[快照] mod: {0}", info.ModId));
        Paths.Log(L("[快照] 版本: {0}", info.Version));
        Paths.Log(L("[快照] 名称: {0}", Dash(info.Name)));
        Paths.Log(L("[快照] 作者: {0}", Dash(info.Author)));
        Paths.Log(L("[快照] 时间: {0}", Dash(info.Created)));
        Paths.Log(L("[快照] 来源: {0}  {1}", Dash(info.Source), Dash(info.SourceRef)));
        Paths.Log(L("[快照] 文件: {0} 个, {1}", info.Files, HumanSize(info.Bytes)));
        Paths.Log(L("[快照] 指纹: {0}", Dash(info.Sha256)));
        Paths.Log(L("[快照] 备注: {0}", Dash(info.Note)));
        Paths.Log(L("[快照] 路径: {0}", DirOf(gameRoot, info.ModId, info.Version)));
    }
}
