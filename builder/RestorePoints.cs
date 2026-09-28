using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.Zip;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>恢复点里的一个数据文件（某个章节的 data.win）。
/// Path 是**相对恢复点目录**的路径（形如 data/root/data.win，一律用 / 分隔）：
/// Apply / Export 拼绝对路径之前先过 PathGuard.Under，绝不直接相信 manifest 里的字符串。</summary>
public sealed class PointFile
{
    /// <summary>章节名：root 或 chapterN。</summary>
    [JsonPropertyName("chapter")] public string Chapter { get; set; } = "";

    /// <summary>相对恢复点目录的路径（/ 分隔）。</summary>
    [JsonPropertyName("path")] public string Path { get; set; } = "";

    /// <summary>建立时内容的 SHA256（大写十六进制）；空串表示没算。</summary>
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";

    [JsonPropertyName("bytes")] public long Bytes { get; set; }

    /// <summary>true = 走的是硬链接（0 字节成本），false = 退化成复制。</summary>
    [JsonPropertyName("linked")] public bool Linked { get; set; }
}

/// <summary>一个整游戏恢复点（Neutraled/restore/点id/manifest.json）。
/// 字段一律用属性：Paths.Json 没有开 IncludeFields，公有**字段**会被序列化成空对象（Cache.Entry 那种写法在这里不能用）。</summary>
public sealed class Point
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("created")] public string Created { get; set; } = "";

    /// <summary>live = 启用表取自当前实际状态；profile = 取自某个配置档。</summary>
    [JsonPropertyName("from")] public string From { get; set; } = "live";

    [JsonPropertyName("profileId")] public string ProfileId { get; set; } = "";
    [JsonPropertyName("chapters")] public List<string> Chapters { get; set; } = new();
    [JsonPropertyName("mods")] public List<string> Mods { get; set; } = new();
    [JsonPropertyName("bytes")] public long Bytes { get; set; }

    /// <summary>是否含可回切的数据副本（WebUi 用它决定恢复按钮是否可点，见 SPEC 3.7）。</summary>
    [JsonPropertyName("hasData")] public bool HasData { get; set; }

    [JsonPropertyName("gameVersion")] public string GameVersion { get; set; } = "";

    /// <summary>manifest 结构版本（将来字段变动时用来兼容旧恢复点）。</summary>
    [JsonPropertyName("schema")] public int Schema { get; set; } = 1;

    [JsonPropertyName("files")] public List<PointFile> Files { get; set; } = new();

    /// <summary>建立时的 config.json 快照（整份原样保存，回切时整份写回）。</summary>
    [JsonPropertyName("config")] public JsonObject? Config { get; set; }
}

/// <summary>整游戏恢复点：把「各章节 data.win + config.json + 启用中的 mod 列表」存成一份可回切的快照。
///
/// 实测教训（选型依据，别改成想当然的写法）：
/// · Injector.Save 是「写临时文件再 File.Move 改名」，换的是 inode —— 部署本身不会动旧副本。
/// · 但全仓有一批 File.Copy(..., true) 是**就地截断**的写法（Cache.cs:365、Program.cs:1480 等），
///   它们改的是 inode 指向的那份数据。早期版本因此用**硬链接**存副本（省盘），实测翻车：
///   建立恢复点后就地改花 chapter1_windows\data.win，恢复点里的副本跟着一起变（同一 inode），
///   再 --restore-apply 只能“恢复”出改花后的内容 —— 恢复点没救回任何东西（详见 CopyData 注释）。
///   所以现在：**建立时真实复制**（独立 inode），回切时用「临时文件 + 同目录原子改名」真实复制。
/// · manifest 里给每个文件存 SHA256，Apply 之前校验，不一致就响亮警告
///   （说不出所以然的“恢复成功”比报错更糟）—— 这条留给旧版本建出来的、仍是硬链接的恢复点。
/// </summary>
public static class RestorePoints
{
    private const int SchemaVersion = 1;

    /// <summary>数据文件都放这个子目录下（manifest 里的 files[].path 以它开头）。</summary>
    private const string DataDir = "data";

    /// <summary>章节顺序：游戏根的 data.win 记为 root，其余按 chapter1..chapter5 探测（不存在就跳过）。</summary>
    private static readonly string[] ChapterNames = { "root", "chapter1", "chapter2", "chapter3", "chapter4", "chapter5" };

    /// <summary>写 manifest.json 用：缩进 + 非 ASCII 原样（复制 Paths.Json 的编码器，避免两处不一致）。</summary>
    private static readonly JsonSerializerOptions JsonWrite = new(Paths.Json) { WriteIndented = true };

    private const string IdStamp = "yyyyMMdd-HHmmss";
    private const string HumanStamp = "yyyy-MM-dd HH:mm:ss";

    /// <summary>恢复点根目录（Neutraled/restore）。</summary>
    public static string Root(string gameRoot) => Paths.RestoreRoot(gameRoot);

    /// <summary>某个恢复点的目录；id 由调用方先过 PathGuard.IsSafeName。</summary>
    private static string DirOf(string gameRoot, string id) => Path.Combine(Root(gameRoot), id);

    private static string Now() => DateTime.Now.ToString(HumanStamp, CultureInfo.InvariantCulture);

    /// <summary>新 id：rp-yyyyMMdd-HHmmss；同一秒内重复创建就加 -2/-3…（绝不覆盖已有恢复点）。</summary>
    private static string NewId(string restoreDir)
    {
        var stamp = DateTime.Now.ToString(IdStamp, CultureInfo.InvariantCulture);
        var id = "rp-" + stamp;
        for (var i = 2; Directory.Exists(Path.Combine(restoreDir, id)); i++) id = "rp-" + stamp + "-" + i;
        return id;
    }

    /// <summary>列恢复点（新到旧）。目录名不像恢复点 id 的直接跳过（restore/ 里还可能有导入用的临时目录）。</summary>
    public static List<Point> List(string gameRoot)
    {
        var list = new List<Point>();
        var root = Root(gameRoot);
        try
        {
            if (!Directory.Exists(root)) return list;
            foreach (var dir in Directory.GetDirectories(root))
            {
                var id = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(id) || id[0] == '.') continue;
                if (!PathGuard.IsSafeName(id)) continue;
                var p = ReadManifest(dir, id);
                if (p != null) list.Add(p);
            }
        }
        catch (Exception ex) { Paths.Log(L("[恢复点] 读取失败：{0}", ex.Message)); }
        list.Sort((a, b) => string.CompareOrdinal(b.Created, a.Created));
        return list;
    }

    /// <summary>建立一个恢复点：manifest.json + 各章节 data.win（硬链接优先）+ config.json 快照 + 当前启用 mod 列表。
    /// withBackup=false 时只写 manifest（可重放配置，但没有数据可回切）。</summary>
    public static Point Create(string gameRoot, string? name = null, string from = "live", string? profileId = null, bool withBackup = true)
    {
        var restoreDir = Root(gameRoot);
        Directory.CreateDirectory(restoreDir);
        var id = NewId(restoreDir);
        var dir = System.IO.Path.Combine(restoreDir, id);
        var pid = profileId ?? "";
        if (pid.Length > 0 && !PathGuard.IsSafeName(pid))
        {
            Paths.Log(L("[恢复点] 拒绝：非法配置档 id {0}", pid));
            pid = "";
        }
        var p = new Point
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(name) ? id : name!.Trim(),
            Created = Now(),
            From = string.Equals(from, "profile", StringComparison.OrdinalIgnoreCase) ? "profile" : "live",
            ProfileId = pid,
            GameVersion = GameVersionOf(gameRoot),
            Schema = SchemaVersion,
            Chapters = DetectChapters(gameRoot),
        };
        p.Mods = EnabledModIds(gameRoot, p.From, pid);
        Directory.CreateDirectory(dir);
        if (withBackup)
        {
            CopyData(gameRoot, dir, p);
            p.HasData = p.Files.Count > 0;
            if (p.HasData) Paths.Log(L("[恢复点] 已建立数据副本：{0} 个章节，{1} 字节", p.Chapters.Count, p.Bytes));
            else Paths.Log(L("[恢复点] [警告] 没找到任何 data.win，只写了清单"));
        }
        else
        {
            Paths.Log(L("[恢复点] 只写了清单（没有备份数据），回切时无法还原 data.win"));
        }
        SnapshotConfig(gameRoot, p);
        WriteManifest(dir, p);
        return p;
    }

    /// <summary>回切：校验游戏未运行 → 覆盖各章节 data.win + 写回 config.json + 还原 mod 启用态。
    /// 返回**改动的文件数**；特殊返回码：1 = 参数非法，2 = 恢复点不存在或没有数据副本，3 = 游戏正在运行。</summary>
    public static int Apply(string gameRoot, string pointId, bool force = false)
    {
        if (!PathGuard.IsSafeName(pointId))
        {
            Paths.Log(L("[恢复点] 拒绝：非法恢复点 id {0}", pointId ?? ""));
            return 1;
        }
        if (GameRunning())
        {
            Paths.Log(L("[恢复点] 拒绝：游戏正在运行（DELTARUNE），请先关闭游戏再恢复"));
            return 3;
        }
        var root = Root(gameRoot);
        var dir = DirOf(gameRoot, pointId);
        if (!Directory.Exists(dir) || !PathGuard.Under(root, dir))
        {
            Paths.Log(L("[恢复点] 找不到恢复点 {0}", pointId));
            return 2;
        }
        var p = ReadManifest(dir, pointId);
        if (p == null) return 2;
        RefreshData(dir, p, warn: true);
        if (p.Files.Count == 0)
        {
            Paths.Log(L("[恢复点] {0} 没有数据副本（建立时用了 --no-backup），无法恢复", pointId));
            return 2;
        }

        // 除非 force：先把「现在」存成一个 auto-<时间戳> 恢复点，回切之后还来得及退回。
        if (!force)
        {
            var auto = Create(gameRoot, "auto-" + DateTime.Now.ToString(IdStamp, CultureInfo.InvariantCulture));
            Paths.Log(L("[恢复点] 恢复前已自动备份当前状态 → {0}", auto.Id));
        }

        var changed = 0;
        foreach (var f in p.Files)
        {
            var src = System.IO.Path.Combine(dir, Local(f.Path));
            if (!PathGuard.Under(dir, src))
            {
                Paths.Log(L("[恢复点] 拒绝：路径越界 {0}", f.Path));
                continue;
            }
            var dst = DataWinOf(gameRoot, f.Chapter);
            if (dst.Length == 0)
            {
                Paths.Log(L("[恢复点] [警告] 未知章节 {0}", f.Chapter));
                continue;
            }
            VerifyCopy(src, f);
            try
            {
                // 「临时文件 + 同目录原子改名」覆盖目标：
                // · 绝不硬链接 —— 回切时硬链接等于把游戏文件和恢复点重新绑在一起，用户下次就地改
                //   data.win 又会污染恢复点（早期版本就是这么翻车的，见 CopyData 注释）。
                // · 先写临时文件再改名，避免「先删后建」中途失败时把游戏文件弄丢。
                var tmp = dst + ".ntl-apply";
                if (File.Exists(tmp)) TryDelete(tmp);
                File.Copy(src, tmp, overwrite: true);
                if (File.Exists(dst))
                {
                    var attr = File.GetAttributes(dst);
                    if ((attr & FileAttributes.ReadOnly) != 0) File.SetAttributes(dst, attr & ~FileAttributes.ReadOnly);
                }
                File.Move(tmp, dst, overwrite: true);
                Paths.Log(L("[恢复点]   {0} → {1}", f.Chapter, L("复制")));
                changed++;
            }
            catch (Exception ex) { Paths.Log(L("[恢复点] [警告] 恢复 {0} 失败：{1}", f.Chapter, ex.Message)); }
        }
        changed += RestoreConfig(gameRoot, p);
        changed += RestoreMods(gameRoot, p);
        Paths.Log(L("[恢复点] 已应用 {0}（改动 {1} 个文件）", pointId, changed));
        return changed;
    }

    /// <summary>把一个恢复点导出成单个 .ntlrestore（zip，用 SharpCompress），内含 manifest.json 与 data/。
    /// 失败返回空串。</summary>
    public static string Export(string gameRoot, string pointId, string zipPath)
    {
        if (!PathGuard.IsSafeName(pointId))
        {
            Paths.Log(L("[恢复点] 拒绝：非法恢复点 id {0}", pointId ?? ""));
            return "";
        }
        if (string.IsNullOrWhiteSpace(zipPath))
        {
            Paths.Log(L("[恢复点] 拒绝：路径越界 {0}", zipPath ?? ""));
            return "";
        }
        var dir = DirOf(gameRoot, pointId);
        if (!Directory.Exists(dir) || !PathGuard.Under(Root(gameRoot), dir))
        {
            Paths.Log(L("[恢复点] 找不到恢复点 {0}", pointId));
            return "";
        }
        var p = ReadManifest(dir, pointId);
        if (p == null) return "";
        // 手抄/解压出来的恢复点可能没有 manifest.json：导出包必须自带清单（导入端只认它），缺了就补写一份。
        var manifest = System.IO.Path.Combine(dir, "manifest.json");
        if (!File.Exists(manifest)) WriteManifest(dir, p);
        if (!File.Exists(manifest))
        {
            Paths.Log(L("[恢复点] 找不到恢复点 {0}", pointId));
            return "";
        }

        // 硬链接副本与游戏里的 data.win 共享 inode：打包时先冻结成独立副本，否则可能压进“正在被改写”的内容。
        Paths.Log(L("[恢复点] 冻结副本（硬链接可能被就地改写）→ 打包含 manifest.json"));
        var frozen = new List<string>();
        try
        {
            foreach (var f in p.Files)
            {
                var src = System.IO.Path.Combine(dir, Local(f.Path));
                if (f.Path.Length == 0 || !PathGuard.Under(dir, src) || !File.Exists(src)) continue;
                var frz = src + ".ntl-export";
                File.Copy(src, frz, true);
                frozen.Add(frz);
            }
            var parent = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(zipPath));
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            using (var w = WriterFactory.OpenWriter(zipPath, ArchiveType.Zip, new ZipWriterOptions(CompressionType.Deflate)))
            {
                w.Write("manifest.json", new FileInfo(manifest));
                foreach (var frz in frozen)
                {
                    // 条目名 = 相对恢复点目录的路径（/ 分隔），导入时按同样规则还原。
                    var rel = RelPath(dir, frz[..^".ntl-export".Length]);
                    w.Write(rel, new FileInfo(frz));
                }
            }
            if (frozen.Count == 0) Paths.Log(L("[恢复点] 没有可打包的数据（先用 --restore-create 建立含数据的恢复点）"));
            else Paths.Log(L("[恢复点] 打包 {0} 个文件 → {1}", frozen.Count + 1, zipPath));
            return zipPath;
        }
        catch (Exception ex)
        {
            Paths.Log(L("[恢复点] 导出失败：{0}", ex.Message));
            return "";
        }
        finally
        {
            foreach (var frz in frozen) TryDelete(frz);
        }
    }

    /// <summary>导入一个 .ntlrestore：先解到临时目录校验 manifest，再整体落进 restore/。失败返回空 Point
    /// （不能返回 null：CliFeatures 与 WebUi 会直接取 p.Id）。</summary>
    public static Point Import(string gameRoot, string zipPath, bool overwrite = false)
    {
        if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
        {
            Paths.Log(L("[恢复点] 找不到恢复包 {0}", zipPath ?? ""));
            return new Point();
        }
        var restoreDir = Root(gameRoot);
        var tmp = System.IO.Path.Combine(restoreDir, ".import-" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        try
        {
            Directory.CreateDirectory(restoreDir);
            if (Directory.Exists(tmp) && PathGuard.Under(restoreDir, tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);
            var n = Extract(zipPath, tmp);
            var mf = System.IO.Path.Combine(tmp, "manifest.json");
            if (!File.Exists(mf))
            {
                Paths.Log(L("[恢复点] 拒绝：恢复包里没有 manifest.json"));
                return new Point();
            }
            Point? p;
            try { p = JsonSerializer.Deserialize<Point>(File.ReadAllText(mf), Paths.Json); }
            catch (Exception ex)
            {
                Paths.Log(L("[恢复点] [警告] manifest.json 无法解析：{0}", ex.Message));
                p = null;
            }
            if (p == null || string.IsNullOrEmpty(p.Id))
            {
                Paths.Log(L("[恢复点] 拒绝：manifest.json 里没有恢复点 id"));
                return new Point();
            }
            if (!PathGuard.IsSafeName(p.Id))
            {
                Paths.Log(L("[恢复点] 拒绝：非法恢复点 id {0}", p.Id));
                return new Point();
            }
            var dest = System.IO.Path.Combine(restoreDir, p.Id);
            if (!PathGuard.Under(restoreDir, dest))
            {
                Paths.Log(L("[恢复点] 拒绝：路径越界 {0}", p.Id));
                return new Point();
            }
            if (Directory.Exists(dest) && !overwrite)
            {
                Paths.Log(L("[恢复点] 已存在同名恢复点 {0}，用 --force 覆盖", p.Id));
                return ReadManifest(dest, p.Id) ?? new Point();
            }

            // 覆盖也要能退回：先把旧点挪到 .old-*（以 . 开头，List 不会把它当恢复点）。
            string? old = null;
            if (Directory.Exists(dest))
            {
                old = System.IO.Path.Combine(restoreDir, "." + p.Id + ".old-" + DateTime.Now.ToString(IdStamp, CultureInfo.InvariantCulture));
                Directory.Move(dest, old);
                Paths.Log(L("[恢复点] 已覆盖恢复点 {0}", p.Id));
            }
            try { Directory.Move(tmp, dest); }
            catch
            {
                if (old != null && !Directory.Exists(dest)) { try { Directory.Move(old, dest); } catch { } }
                throw;
            }
            if (old != null) TryDeleteDir(old);

            // 按磁盘实际内容刷新 hasData/files/chapters 再落盘：压缩包里的清单可能与实际不符。
            RefreshData(dest, p, warn: false);
            WriteManifest(dest, p);
            Paths.Log(L("[恢复点] 解包 {0} 个文件 → {1}", n, dest));
            return p;
        }
        catch (InvalidDataException ex)
        {
            Paths.Log(L("[恢复点] 拒绝：压缩包条目路径越界 {0}", ex.Message));
            return new Point();
        }
        catch (Exception ex)
        {
            Paths.Log(L("[恢复点] 导入失败：{0}", ex.Message));
            return new Point();
        }
        finally
        {
            TryDeleteDir(tmp);
        }
    }

    /// <summary>删除一个恢复点（只删 restore/ 下这一个目录；id 不合法或不在 restore/ 下都拒绝）。
    /// force：删不掉时清掉只读属性再试一次（不加 force 也能删，force 只是更狠一点）。</summary>
    public static bool Delete(string gameRoot, string pointId, bool force = false)
    {
        if (!PathGuard.IsSafeName(pointId))
        {
            Paths.Log(L("[恢复点] 拒绝：非法恢复点 id {0}", pointId ?? ""));
            return false;
        }
        var root = Root(gameRoot);
        var dir = DirOf(gameRoot, pointId);
        if (!Directory.Exists(dir) || !PathGuard.Under(root, dir))
        {
            Paths.Log(L("[恢复点] 找不到恢复点 {0}", pointId));
            return false;
        }
        try { Directory.Delete(dir, true); }
        catch (Exception ex)
        {
            if (!force)
            {
                Paths.Log(L("[恢复点] 删除失败：{0}", ex.Message));
                return false;
            }
            ClearReadOnly(dir);
            try { Directory.Delete(dir, true); }
            catch (Exception ex2) { Paths.Log(L("[恢复点] 删除失败：{0}", ex2.Message)); return false; }
        }
        Paths.Log(L("[恢复点] 已删除 {0}", pointId));
        return true;
    }

    /// <summary>--restore-list：人可读 + 可 grep（一行一个点，字段名固定）。</summary>
    public static void PrintList(string gameRoot)
    {
        var pts = List(gameRoot);
        Paths.Log(L("[恢复点] 目录：{0}", Root(gameRoot)));
        if (pts.Count == 0)
        {
            Paths.Log(L("[恢复点] 没有恢复点"));
            return;
        }
        foreach (var p in pts)
            Paths.Log(L("[恢复点] {0}  name={1}  created={2}  from={3}  hasData={4}  chapters={5}  mods={6}  bytes={7}",
                p.Id, p.Name, p.Created, p.From, p.HasData ? "true" : "false",
                string.Join(",", p.Chapters), p.Mods.Count, p.Bytes));
    }

    // ---------------- 内部实现 ----------------

    /// <summary>把各章节 data.win 存进恢复点：**真实复制**（独立 inode），绝不用硬链接。
    ///
    /// 为什么不走「硬链接 0 字节成本」那条路（实测复现，别改回去）：
    /// · 硬链接副本与游戏里的 data.win 共享同一份数据，而全仓有一批 File.Copy(..., true) 是就地截断写法
    ///   （Cache.cs:365、Program.cs:1480 等），任何外部就地写都会把副本一起改掉。
    /// · 实测（假游戏根 rp-20260926-122834）：建立恢复点后就地改花 chapter1_windows\data.win
    ///   （4096B/2D69F59F… → 4096B/D349A508…）→ 恢复点里的副本同步变成 D349A508…（同一 inode，
    ///   fsutil hardlink list 三条路径共享），manifest 记的 SHA256 立刻成为陈旧值；
    ///   随后 --restore-apply 只打印「SHA256 校验失败」警告却照样按“成功”退出（exit 0），
    ///   游戏文件仍是改花后的内容。
    /// 代价是盘：本游戏 6 个 data.win 合计约 530 MB，建立一次恢复点就多占这些空间 —— 认了。
    /// </summary>
    private static void CopyData(string gameRoot, string dir, Point p)
    {
        foreach (var chapter in ChapterNames)
        {
            var src = Paths.ChapterDataWin(gameRoot, chapter);
            if (!File.Exists(src)) continue;
            var dst = System.IO.Path.Combine(dir, DataDir, chapter, "data.win");
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dst)!);
                if (File.Exists(dst))
                {
                    var attr = File.GetAttributes(dst);
                    if ((attr & FileAttributes.ReadOnly) != 0) File.SetAttributes(dst, attr & ~FileAttributes.ReadOnly);
                    File.Delete(dst);
                }
                File.Copy(src, dst, overwrite: true);
                var len = new FileInfo(src).Length;
                p.Files.Add(new PointFile
                {
                    Chapter = chapter,
                    Path = DataDir + "/" + chapter + "/data.win",
                    Sha256 = Sha256Of(dst),
                    Bytes = len,
                    // 字段保留（manifest schema 不变、旧恢复点仍能读），但真实复制后永远是 false。
                    Linked = false,
                });
                p.Bytes += len;
                Paths.Log(L("[恢复点]   {0} → {1}", chapter, L("复制")));
            }
            catch (Exception ex)
            {
                // 半截文件不能留在恢复点里冒充副本（RefreshData 只认 manifest，但它会污染导出与人工检查）。
                try
                {
                    if (File.Exists(dst))
                    {
                        var a2 = File.GetAttributes(dst);
                        if ((a2 & FileAttributes.ReadOnly) != 0) File.SetAttributes(dst, a2 & ~FileAttributes.ReadOnly);
                        File.Delete(dst);
                    }
                }
                catch { }
                Paths.Log(L("[恢复点] [警告] 备份 {0} 失败：{1}", chapter, ex.Message));
            }
        }
    }

    /// <summary>存一份 config.json 快照（读失败/为空只提示，不挡建立恢复点）。</summary>
    private static void SnapshotConfig(string gameRoot, Point p)
    {
        try
        {
            var cfg = ConfigFile.Load(gameRoot);
            if (cfg.Count > 0) p.Config = cfg;
            else Paths.Log(L("[恢复点] 没有 config.json 可存（跳过）"));
        }
        catch (Exception ex) { Paths.Log(L("[恢复点] [警告] 读取 config.json 失败：{0}", ex.Message)); }
    }

    /// <summary>当前启用中的 mod id 列表；--from profile 时以该档记录的启用表为准（回切能整体还原那套搭配）。</summary>
    private static List<string> EnabledModIds(string gameRoot, string from, string profileId)
    {
        var ids = new List<string>();
        if (from == "profile" && profileId.Length > 0)
        {
            try
            {
                var pr = Profiles.Load(gameRoot, profileId);
                if (pr != null) return new List<string>(pr.Enabled);
                Paths.Log(L("[恢复点] [警告] 找不到配置档 {0}，改用当前实际启用态", profileId));
            }
            catch (Exception ex) { Paths.Log(L("[恢复点] [警告] 读取配置档失败：{0}", ex.Message)); }
        }
        try
        {
            foreach (var m in Mods.ScanMods(Paths.ModsRoot(gameRoot), "", includeDisabled: true, allChapters: true))
                if (m.Enabled && !ids.Contains(m.Id)) ids.Add(m.Id);
        }
        catch (Exception ex) { Paths.Log(L("[恢复点] [警告] 扫描 mod 失败：{0}", ex.Message)); }
        return ids;
    }

    /// <summary>探测存在的章节（游戏根的 data.win 记为 root）。</summary>
    private static List<string> DetectChapters(string gameRoot)
    {
        var list = new List<string>();
        foreach (var chapter in ChapterNames)
        {
            try { if (File.Exists(Paths.ChapterDataWin(gameRoot, chapter))) list.Add(chapter); }
            catch { }
        }
        return list;
    }

    /// <summary>章节名（白名单内）→ 该章节 data.win 的绝对路径；未知章节返回空串。</summary>
    private static string DataWinOf(string gameRoot, string chapter)
    {
        if (string.IsNullOrEmpty(chapter)) return "";
        if (Array.FindIndex(ChapterNames, c => string.Equals(c, chapter, StringComparison.OrdinalIgnoreCase)) < 0) return "";
        return Paths.ChapterDataWin(gameRoot, chapter);
    }

    /// <summary>校验副本内容是否还是建立时的样子；不一致只警告（照样恢复，但用户得知道）。
    /// 为什么还要查：副本现在是真实复制，但①旧版本建出来的恢复点可能仍是硬链接，②副本文件本身也可能
    /// 被外部工具就地改写过；有这一条，用户至少能在“恢复成功”之前看到不对劲。</summary>
    private static void VerifyCopy(string src, PointFile f)
    {
        if (f.Sha256.Length == 0) return;
        var now = Sha256Of(src);
        if (now.Length == 0 || string.Equals(now, f.Sha256, StringComparison.OrdinalIgnoreCase)) return;
        Paths.Log(L("[恢复点] [警告] 副本与建立时不一致（SHA256 校验失败）：{0}", f.Path));
    }

    /// <summary>按磁盘实际内容刷新 files/chapters/bytes/hasData（导入、手抄、被外部删掉文件的情况都会用到）。</summary>
    private static void RefreshData(string dir, Point p, bool warn)
    {
        var present = new List<PointFile>();
        long bytes = 0;
        foreach (var f in p.Files)
        {
            if (f.Path.Length == 0) continue;
            var abs = System.IO.Path.Combine(dir, Local(f.Path));
            if (!PathGuard.Under(dir, abs) || !File.Exists(abs))
            {
                if (warn) Paths.Log(L("[恢复点] [警告] 缺少数据文件 {0}", f.Path));
                continue;
            }
            present.Add(f);
            bytes += new FileInfo(abs).Length;
        }
        p.Files = present;
        p.Bytes = bytes;
        p.HasData = present.Count > 0;
        p.Chapters = present.Select(f => f.Chapter).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>写回 config.json 快照：改动了返回 1，没写返回 0。</summary>
    private static int RestoreConfig(string gameRoot, Point p)
    {
        if (p.Config == null || p.Config.Count == 0)
        {
            Paths.Log(L("[恢复点] {0} 没有配置快照，跳过 config.json", p.Id));
            return 0;
        }
        try
        {
            // 现状与快照等价就不写盘：重复 Apply 不该改动文件数、不该刷 mtime（幂等）
            if (SameConfig(ConfigFile.Load(gameRoot), p.Config)) return 0;
            ConfigFile.Save(gameRoot, p.Config);
            Paths.Log(L("[恢复点] 已写回 config.json 快照"));
            return 1;
        }
        catch (Exception ex) { Paths.Log(L("[恢复点] [警告] 写 config.json 失败：{0}", ex.Message)); return 0; }
    }

    /// <summary>现状 config.json 与快照是否等价（键集合相同、每个键的 JSON 值逐字相同）。
    /// 等价就不写盘 —— 否则重复 Apply 会把 config.json 的 mtime 刷一遍、还谎报改了 1 个文件。</summary>
    private static bool SameConfig(JsonObject cur, JsonObject want)
    {
        if (cur.Count != want.Count) return false;
        foreach (var kv in want)
        {
            if (!cur.ContainsKey(kv.Key)) return false;
            if ((cur[kv.Key]?.ToJsonString() ?? "") != (kv.Value?.ToJsonString() ?? "")) return false;
        }
        return true;
    }

    /// <summary>还原 mod 启用态：恢复点里记的启用集合为准，缺失的 mod 只警告（不清也不建）。
    /// 改动 mod.json 的文件数就是返回值。</summary>
    private static int RestoreMods(string gameRoot, Point p)
    {
        var wanted = new HashSet<string>(p.Mods, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var n = 0;
        List<ModEntry> mods;
        try { mods = Mods.ScanMods(Paths.ModsRoot(gameRoot), "", includeDisabled: true, allChapters: true); }
        catch (Exception ex) { Paths.Log(L("[恢复点] [警告] 扫描 mod 失败：{0}", ex.Message)); return 0; }
        foreach (var m in mods)
        {
            seen.Add(m.Id);
            var on = wanted.Contains(m.Id);
            if (m.Enabled == on) continue;
            if (SetModEnabled(gameRoot, m, on)) n++;
        }
        foreach (var id in p.Mods)
            if (!seen.Contains(id)) Paths.Log(L("[恢复点] 忽略：找不到 mod {0}", id));
        return n;
    }

    /// <summary>改 mods 下某个 mod.json 的 enabled（启用态的唯一真相，见 gui/MainForm.cs:322）。
    /// 只动 ModsRoot 下面的文件，写入前过前缀校验。</summary>
    private static bool SetModEnabled(string gameRoot, ModEntry m, bool on)
    {
        if (string.IsNullOrWhiteSpace(m.Dir)) return false;
        var mj = System.IO.Path.Combine(m.Dir, "mod.json");
        if (!File.Exists(mj))
        {
            Paths.Log(L("[恢复点] 忽略：找不到 mod.json（{0}）", mj));
            return false;
        }
        if (!PathGuard.Under(Paths.ModsRoot(gameRoot), mj))
        {
            Paths.Log(L("[恢复点] 拒绝：路径越界 {0}", mj));
            return false;
        }
        try
        {
            var obj = JsonNode.Parse(File.ReadAllText(mj), null,
                new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip })?.AsObject();
            if (obj == null)
            {
                Paths.Log(L("[恢复点] 忽略：mod.json 不是对象（{0}）", mj));
                return false;
            }
            obj["enabled"] = on;
            Paths.SafeWrite(mj, obj.ToJsonString(JsonWrite));
            m.Enabled = on;
            Paths.Log(on ? L("[恢复点] {0}: 启用", m.Id) : L("[恢复点] {0}: 禁用", m.Id));
            return true;
        }
        catch (Exception ex) { Paths.Log(L("[恢复点] [警告] 写 mod.json 失败 {0}：{1}", mj, ex.Message)); return false; }
    }

    /// <summary>写 manifest.json（写失败只警告：目录里没清单还能靠 List 的兜底路径列出来）。</summary>
    private static void WriteManifest(string dir, Point p)
    {
        try
        {
            Directory.CreateDirectory(dir);
            Paths.SafeWrite(System.IO.Path.Combine(dir, "manifest.json"), JsonSerializer.Serialize(p, JsonWrite));
        }
        catch (Exception ex) { Paths.Log(L("[恢复点] [警告] 写 manifest.json 失败：{0}", ex.Message)); }
    }

    /// <summary>读 manifest.json；损坏或缺失时退化成「按目录内容」的点（目录里扫到的 data.win 也算数据），
    /// 这样手抄/解压出来的备份不会从列表里消失。</summary>
    private static Point? ReadManifest(string dir, string id)
    {
        var mf = System.IO.Path.Combine(dir, "manifest.json");
        if (File.Exists(mf))
        {
            try
            {
                var p = JsonSerializer.Deserialize<Point>(File.ReadAllText(mf), Paths.Json);
                if (p != null)
                {
                    if (string.IsNullOrEmpty(p.Id)) p.Id = id;
                    if (string.IsNullOrEmpty(p.Name)) p.Name = p.Id;
                    if (p.Files == null) p.Files = new List<PointFile>();
                    if (p.Chapters == null) p.Chapters = new List<string>();
                    if (p.Mods == null) p.Mods = new List<string>();
                    if (p.Files.Count > 0)
                    {
                        p.HasData = true;
                        if (p.Bytes <= 0) foreach (var f in p.Files) p.Bytes += f.Bytes;
                    }
                    return p;
                }
            }
            catch (Exception ex) { Paths.Log(L("[恢复点] [警告] manifest.json 损坏（{0}）：{1}", id, ex.Message)); }
        }
        var fb = new Point { Id = id, Name = id, From = "", GameVersion = "", Schema = 0 };
        try
        {
            fb.Created = Directory.GetLastWriteTime(dir).ToString(HumanStamp, CultureInfo.InvariantCulture);
            foreach (var f in Directory.GetFiles(dir, "data.win", SearchOption.AllDirectories))
            {
                var ch = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(f) ?? "");
                if (ch.Length == 0) continue;
                var len = new FileInfo(f).Length;
                fb.Files.Add(new PointFile { Chapter = ch, Path = RelPath(dir, f), Bytes = len });
                fb.Bytes += len;
            }
            fb.Chapters = fb.Files.Select(f => f.Chapter).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            fb.HasData = fb.Files.Count > 0;
        }
        catch { }
        return fb;
    }

    /// <summary>解压全部条目到 destDir，返回文件数。条目路径越界（zip-slip）抛 InvalidDataException。
    /// 注意：既有 ModdingImport.ExtractAny 没有做这个校验，这里必须自己做。</summary>
    private static int Extract(string zipPath, string destDir)
    {
        var n = 0;
        using var archive = ArchiveFactory.OpenArchive(zipPath);
        foreach (var entry in archive.Entries)
        {
            if (entry.IsDirectory) continue;
            var key = (entry.Key ?? "").Replace('\\', '/').TrimStart('/');
            while (key.StartsWith("./", StringComparison.Ordinal)) key = key[2..];
            if (key.Length == 0) continue;
            if (key.Contains("..", StringComparison.Ordinal)) throw new InvalidDataException(key);
            var dest = System.IO.Path.Combine(destDir, key.Replace('/', System.IO.Path.DirectorySeparatorChar));
            if (!PathGuard.Under(destDir, dest)) throw new InvalidDataException(key);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest)!);
            using var es = entry.OpenEntryStream();
            using var fso = File.Create(dest);
            es.CopyTo(fso);
            n++;
        }
        return n;
    }

    /// <summary>游戏版本指纹：直接复用 Cache.GameVersion（它读 backup 里的原版 data.win，
    /// 不会因为部署改写顶层 data.win 而变化）。</summary>
    private static string GameVersionOf(string gameRoot)
    {
        try { return Cache.GameVersion(gameRoot); } catch { return "unknown"; }
    }

    /// <summary>DELTARUNE 是否正在运行（跨平台；游戏在跑时覆盖 data.win 会被系统拒绝，或让游戏读到半个文件）。</summary>
    private static bool GameRunning()
    {
        try { return System.Diagnostics.Process.GetProcessesByName("DELTARUNE").Length > 0; }
        catch { return false; }
    }

    /// <summary>文件 SHA256（大写十六进制）；读不了返回空串（调用方按「校验不了」处理）。</summary>
    private static string Sha256Of(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(fs));
        }
        catch { return ""; }
    }

    /// <summary>相对路径 → 本地分隔符。</summary>
    private static string Local(string rel) => rel.Replace('/', System.IO.Path.DirectorySeparatorChar);

    /// <summary>绝对路径 → 相对 baseDir 的 / 分隔路径。</summary>
    private static string RelPath(string baseDir, string abs) => System.IO.Path.GetRelativePath(baseDir, abs).Replace('\\', '/');

    /// <summary>清掉只读属性（Windows 上被标只读的副本会让 Directory.Delete 抛 UnauthorizedAccessException）。</summary>
    private static void ClearReadOnly(string dir)
    {
        try
        {
            foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var a = File.GetAttributes(f);
                    if ((a & FileAttributes.ReadOnly) != 0) File.SetAttributes(f, a & ~FileAttributes.ReadOnly);
                }
                catch { }
            }
        }
        catch { }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void TryDeleteDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
    }
}
