using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>原版基线（backup）卫生与不变量。
///
/// 背景（2026-10-31 取证）：<游戏根>\backup\chapter2_windows\data.win 曾被**已部署产物**覆盖
/// （66,949,366 B vs 原版 66,949,110 B），而 deploy-state.json 把污染件指纹当成了原版指纹。
/// 原因之一是 Program.Deploy 的「[1/5] 备份原版」只判断 backup 是否存在，不校验它到底是不是原版；
/// 另一处是 GameUpdate.RecordState 无条件相信 backup 的指纹。于是「原版兜底」这条承诺实际失效，
/// 还原会用被污染的「原版」覆盖 live，等于把 mod 永久固化进基线。
///
/// 本类把「原版兜底」变成可验证的不变量：
///   I1 两处备份（<游戏根>\backup 与 Neutraled\backup）必须哈希一致，二者都是原版。
///   I2 原版基线一旦登记（Neutraled\baseline.json）就是唯一事实来源，**永不隐式改写**。
///   I3 backup 只读：部署只写 live（<游戏根>\data.win / chapterN_*_windows\data.win）与 Neutraled\。
///   I4 首次登记必须有证据：两处一致 / 镜像独存 / 规范独存且不含产物标记 / live 是原版。
///   I5 校验失败时**中止部署**（不写任何产物），并打印可执行的修复命令。
///   I6 修复只从「可信来源」复制：ledger 指纹一致的一方、或 live（且 live 不含产物标记）。
///   I7 时间线/沙箱产物（outDirOverride）不登记 ledger、不写 backup。
///   I8 --update-check/--adopt-current 是唯一改变原版认定的入口（游戏更新后采纳新原版）。
///
/// 产物标记：注入后的 data.win 明文中含 obj_ntl_core / scr_ntl_init / ntl_product_scope。
/// 用它区分「原版」与「产物」，避免拿产物当原版（这也是 2026-10-31 污染的形态）。</summary>
public static class Baseline
{
    public const string LedgerFileName = "baseline.json";
    public const int LedgerVersion = 1;

    // ==================== 数据模型 ====================

    public sealed class HistoryEntry
    {
        [JsonPropertyName("fp")] public string Fp { get; set; } = "";
        [JsonPropertyName("source")] public string Source { get; set; } = "";
        [JsonPropertyName("at")] public string At { get; set; } = "";
    }

    public sealed class SlotRecord
    {
        [JsonPropertyName("slot")] public string Slot { get; set; } = "";
        [JsonPropertyName("rel")] public string Rel { get; set; } = "";
        [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
        [JsonPropertyName("size")] public long Size { get; set; }
        [JsonPropertyName("fp")] public string Fp { get; set; } = "";
        [JsonPropertyName("source")] public string Source { get; set; } = "";
        [JsonPropertyName("establishedAt")] public string EstablishedAt { get; set; } = "";
        [JsonPropertyName("history")] public List<HistoryEntry> History { get; set; } = new();
    }

    public sealed class Ledger
    {
        [JsonPropertyName("version")] public int Version { get; set; } = LedgerVersion;
        [JsonPropertyName("createdAt")] public string CreatedAt { get; set; } = "";
        [JsonPropertyName("updatedAt")] public string UpdatedAt { get; set; } = "";
        [JsonPropertyName("slots")] public Dictionary<string, SlotRecord> Slots { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>一个槽位的评估结论。</summary>
    public enum SlotState
    {
        Ok,                 // 规范/镜像/ledger 三方一致
        AutoHeal,           // 有一侧缺失或被改，但另一侧与 ledger 一致 → 可无风险修复
        EstablishAgreement, // 未登记，但规范与镜像一致
        EstablishPrimary,   // 未登记，只有规范副本且不含产物标记
        EstablishMirror,    // 未登记，只有镜像副本且不含产物标记
        Drift,              // 与 ledger 不一致（无产物标记：可能是游戏更新/外部改写）
        Polluted,           // 与 ledger 不一致且含产物标记（原版基线被产物覆盖）
        Missing,            // 两处都没有
        Unreadable,         // ledger 损坏
    }

    public sealed class SlotReport
    {
        public string Slot = "";
        public string Rel = "";
        public string PrimaryPath = "";
        public string MirrorPath = "";
        public bool PrimaryExists;
        public bool MirrorExists;
        public bool PrimaryHasMarkers;
        public bool MirrorHasMarkers;
        public string PrimaryFp = "missing";
        public string MirrorFp = "missing";
        public string ExpectedFp = "";
        public SlotState State = SlotState.Missing;
        public string Detail = "";

        public bool IsHealthy => State == SlotState.Ok;
        public bool CanAutoHeal => State is SlotState.AutoHeal or SlotState.EstablishAgreement
                                          or SlotState.EstablishPrimary or SlotState.EstablishMirror;
    }

    /// <summary>一次「文件身份」读取的结果：sha256 + 长度 + 是否含产物标记。</summary>
    public readonly struct Identity
    {
        public readonly string Sha256;
        public readonly long Size;
        public readonly bool HasMarkers;
        public Identity(string sha, long size, bool hasMarkers) { Sha256 = sha; Size = size; HasMarkers = hasMarkers; }
        public string Fp => MakeFp(Sha256, Size);
    }

    // ==================== 路径 ====================

    public static string RelOf(string gameRoot, string chapter) =>
        chapter.Equals("root", StringComparison.OrdinalIgnoreCase)
            ? "data.win"
            : chapter + "_" + Paths.ChapterSuffix(gameRoot) + "/data.win";

    /// <summary>规范位置：<游戏根>\backup\...</summary>
    public static string PrimaryPath(string gameRoot, string chapter) =>
        Path.Combine(gameRoot, "backup", RelOf(gameRoot, chapter).Replace('/', Path.DirectorySeparatorChar));

    /// <summary>镜像位置：Neutraled\backup\...（--install 写的那份；重装 Steam 会清掉规范位置）。</summary>
    public static string MirrorPath(string gameRoot, string chapter) =>
        Path.Combine(Paths.NeutraledRoot(gameRoot), "backup", RelOf(gameRoot, chapter).Replace('/', Path.DirectorySeparatorChar));

    public static string LedgerPath(string gameRoot) => Path.Combine(Paths.NeutraledRoot(gameRoot), LedgerFileName);

    static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    static string EmptyFp => "missing";

    public static string MakeFp(string sha256, long size) =>
        string.IsNullOrEmpty(sha256) ? EmptyFp : sha256[..Math.Min(16, sha256.Length)].ToLowerInvariant() + ":" + size;

    // ==================== 身份读取（sha256 + 产物标记，一遍扫描） ====================

    static readonly byte[][] MarkerBytes =
    {
        Encoding.ASCII.GetBytes("obj_ntl_core"),
        Encoding.ASCII.GetBytes("scr_ntl_init"),
        Encoding.ASCII.GetBytes("ntl_product_scope"),
    };

    sealed class CachedIdentity
    {
        public long Size;
        public long MtimeTicks;
        public Identity Id;
        public bool Valid;
    }

    static readonly Dictionary<string, CachedIdentity> IdentityCache = new(StringComparer.OrdinalIgnoreCase);
    static readonly object IdentityLock = new();

    /// <summary>一次读盘同时得到 sha256、长度与产物标记（大文件不重复读）。
    /// 失败返回 null（文件被占用/无权限），调用方按「无法验证」处理。</summary>
    public static Identity? Identify(string path, bool useCache = true)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) return null;
            lock (IdentityLock)
            {
                if (useCache && IdentityCache.TryGetValue(path, out var hit) && hit.Valid
                    && hit.Size == fi.Length && hit.MtimeTicks == fi.LastWriteTimeUtc.Ticks)
                    return hit.Id;
            }

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20);
            using var sha = SHA256.Create();
            var buf = new byte[1 << 20];
            var tail = new byte[64];      // 跨块边界的重叠窗口（标记最长 16 字节）
            int tailLen = 0;
            bool markers = false;
            long total = 0;
            int n;
            while ((n = fs.Read(buf, 0, buf.Length)) > 0)
            {
                if (!markers)
                {
                    sha.TransformBlock(buf, 0, n, null, 0);
                    // 在 tail+buf 上找标记
                    var window = new byte[tailLen + n];
                    Buffer.BlockCopy(tail, 0, window, 0, tailLen);
                    Buffer.BlockCopy(buf, 0, window, tailLen, n);
                    foreach (var m in MarkerBytes)
                    {
                        if (IndexOf(window, m) >= 0) { markers = true; break; }
                    }
                    tailLen = Math.Min(tail.Length, window.Length);
                    Buffer.BlockCopy(window, window.Length - tailLen, tail, 0, tailLen);
                }
                else
                {
                    sha.TransformBlock(buf, 0, n, null, 0);
                }
                total += n;
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            var hex = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
            var id = new Identity(hex, total, markers);
            lock (IdentityLock)
            {
                IdentityCache[path] = new CachedIdentity { Size = fi.Length, MtimeTicks = fi.LastWriteTimeUtc.Ticks, Id = id, Valid = true };
            }
            return id;
        }
        catch { return null; }
    }

    static int IndexOf(byte[] hay, byte[] needle)
    {
        int last = hay.Length - needle.Length;
        for (int i = 0; i <= last; i++)
        {
            int j = 0;
            while (j < needle.Length && hay[i + j] == needle[j]) j++;
            if (j == needle.Length) return i;
        }
        return -1;
    }

    /// <summary>该文件是否是「已注入的产物」（明文含产物标记）。文件不存在返回 false。</summary>
    public static bool IsProduct(string path) => Identify(path)?.HasMarkers ?? false;

    public static string? Sha256Of(string path) => Identify(path)?.Sha256;

    // ==================== ledger 读写 ====================

    public static Ledger? LoadLedger(string gameRoot, out bool unreadable)
    {
        unreadable = false;
        var path = LedgerPath(gameRoot);
        if (!File.Exists(path)) return new Ledger { CreatedAt = Now(), UpdatedAt = Now() };
        try
        {
            var parsed = JsonSerializer.Deserialize<Ledger>(File.ReadAllText(path), Opts());
            if (parsed == null) { unreadable = true; return new Ledger(); }
            parsed.Slots ??= new Dictionary<string, SlotRecord>(StringComparer.OrdinalIgnoreCase);
            if (parsed.CreatedAt.Length == 0) parsed.CreatedAt = Now();
            return parsed;
        }
        catch { unreadable = true; return new Ledger(); }
    }

    static JsonSerializerOptions Opts() => new(Paths.Json) { WriteIndented = true };

    static void SaveLedger(string gameRoot, Ledger ledger)
    {
        ledger.Version = LedgerVersion;
        ledger.UpdatedAt = Now();
        Directory.CreateDirectory(Paths.NeutraledRoot(gameRoot));
        Paths.SafeWrite(LedgerPath(gameRoot), JsonSerializer.Serialize(ledger, Opts()));
    }

    /// <summary>ledger 里已登记的原版指纹（没有 ledger/条目时返回 null）。给 GameUpdate 做真相来源。</summary>
    public static string? TryGetFp(string gameRoot, string chapter)
    {
        var ledger = LoadLedger(gameRoot, out var bad);
        if (bad) return null;
        return ledger.Slots.TryGetValue(chapter, out var rec) && rec.Fp.Length > 0 ? rec.Fp : null;
    }

    public static IEnumerable<string> SlotsOf(string gameRoot)
    {
        yield return "root";
        foreach (var c in GameUpdate.DiscoverChapters(gameRoot)) yield return c;
    }

    // ==================== 评估 ====================

    public static SlotReport Evaluate(string gameRoot, string chapter)
    {
        chapter = string.IsNullOrWhiteSpace(chapter) ? "root" : chapter.Trim();
        var ledger = LoadLedger(gameRoot, out var unreadable);
        var rep = new SlotReport
        {
            Slot = chapter,
            Rel = RelOf(gameRoot, chapter),
            PrimaryPath = PrimaryPath(gameRoot, chapter),
            MirrorPath = MirrorPath(gameRoot, chapter),
        };
        if (unreadable)
        {
            rep.State = SlotState.Unreadable;
            rep.Detail = L("Neutraled/baseline.json 解析失败（已忽略）—— 请检查该文件或删掉后重建");
            return rep;
        }

        var pid = Identify(rep.PrimaryPath);
        var mid = Identify(rep.MirrorPath);
        rep.PrimaryExists = pid != null;
        rep.MirrorExists = mid != null;
        rep.PrimaryHasMarkers = pid?.HasMarkers ?? false;
        rep.MirrorHasMarkers = mid?.HasMarkers ?? false;
        rep.PrimaryFp = pid?.Fp ?? EmptyFp;
        rep.MirrorFp = mid?.Fp ?? EmptyFp;
        ledger.Slots.TryGetValue(chapter, out var rec);
        rep.ExpectedFp = rec?.Fp ?? "";

        if (rec != null && rec.Fp.Length > 0)
        {
            bool pOk = pid != null && pid.Value.Sha256 == rec.Sha256;
            bool mOk = mid != null && mid.Value.Sha256 == rec.Sha256;
            if (pOk && mOk) { rep.State = SlotState.Ok; rep.Detail = L("规范/镜像/基线三方一致"); return rep; }
            if (pOk && !mOk)
            {
                rep.State = SlotState.AutoHeal;
                rep.Detail = mid == null ? L("镜像副本缺失，可从规范副本补建") : L("镜像副本与基线不一致（{0}），可从规范副本补建", rep.MirrorFp);
                return rep;
            }
            if (!pOk && mOk && mid != null)
            {
                rep.State = SlotState.AutoHeal;
                rep.Detail = pid == null ? L("规范副本缺失，可从镜像副本恢复") : L("规范副本与基线不一致（{0}），可从镜像副本恢复", rep.PrimaryFp);
                return rep;
            }
            // 两侧都不是已知原版
            bool marker = rep.PrimaryHasMarkers || rep.MirrorHasMarkers;
            rep.State = marker ? SlotState.Polluted : SlotState.Drift;
            rep.Detail = marker
                ? L("副本里含产物标记（原版基线被部署产物覆盖：规范 {0} / 镜像 {1}，期望 {2}）", rep.PrimaryFp, rep.MirrorFp, rec.Fp)
                : L("副本与基线不一致（规范 {0} / 镜像 {1}，期望 {2}）：可能是游戏更新或外部改动", rep.PrimaryFp, rep.MirrorFp, rec.Fp);
            return rep;
        }

        // 未登记：只能用「证据」建立基线
        if (pid != null && mid != null)
        {
            if (pid.Value.Sha256 == mid.Value.Sha256)
            {
                if (pid.Value.HasMarkers)
                {
                    rep.State = SlotState.Polluted;
                    rep.Detail = L("两处副本一致但都是产物（含产物标记）—— 不能登记为原版；请用 --adopt-current --yes 或指定 --from 修复");
                }
                else
                {
                    rep.State = SlotState.EstablishAgreement;
                    rep.Detail = L("两处副本一致（{0}），可登记为原版基线", rep.PrimaryFp);
                }
                return rep;
            }
            bool marker = rep.PrimaryHasMarkers || rep.MirrorHasMarkers;
            rep.State = marker ? SlotState.Polluted : SlotState.Drift;
            rep.Detail = marker
                ? L("两处副本不一致且含产物标记（规范 {0} / 镜像 {1}）", rep.PrimaryFp, rep.MirrorFp)
                : L("两处副本不一致（规范 {0} / 镜像 {1}），无法判断谁是原版", rep.PrimaryFp, rep.MirrorFp);
            return rep;
        }
        if (pid != null)
        {
            if (pid.Value.HasMarkers)
            {
                rep.State = SlotState.Polluted;
                rep.Detail = L("只有规范副本 {0}，但它含产物标记（不是原版）", rep.PrimaryFp);
            }
            else
            {
                rep.State = SlotState.EstablishPrimary;
                rep.Detail = L("只有规范副本 {0}（无产物标记），可登记为原版基线", rep.PrimaryFp);
            }
            return rep;
        }
        if (mid != null)
        {
            if (mid.Value.HasMarkers)
            {
                rep.State = SlotState.Polluted;
                rep.Detail = L("只有镜像副本 {0}，但它含产物标记（不是原版）", rep.MirrorFp);
            }
            else
            {
                rep.State = SlotState.EstablishMirror;
                rep.Detail = L("只有镜像副本 {0}（无产物标记），可登记为原版基线", rep.MirrorFp);
            }
            return rep;
        }
        rep.State = SlotState.Missing;
        rep.Detail = L("两处都没有原版备份");
        return rep;
    }

    // ==================== 修复与建立 ====================

    static bool CopyFile(string from, string to)
    {
        try
        {
            // 源与目标同一文件：--from file:<规范路径> 修一侧时，本来就要保留该侧，直接当成功
            if (string.Equals(Path.GetFullPath(from), Path.GetFullPath(to), StringComparison.OrdinalIgnoreCase))
                return File.Exists(to);
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            Unprotect(to);                        // 备份是只读的（防外部原地重写），复制前先解除
            File.Copy(from, to, true);
            try { File.SetAttributes(to, File.GetAttributes(to) | FileAttributes.ReadOnly); } catch { }
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine(L("  [错误] 复制失败 {0} -> {1}: {2}", from, to, ex.Message));
            return false;
        }
    }

    /// <summary>备份只读保护：挡住「编辑器/脚本把原版备份当文档原地重写」这类事故（2026-10-31 的污染形态）。
    /// 只读不是安全边界（管理员/属性可绕过），但足以让 File.Write / File.Copy(覆盖) 直接失败。</summary>
    static void Protect(SlotReport rep)
    {
        foreach (var p in new[] { rep.PrimaryPath, rep.MirrorPath })
        {
            try
            {
                if (!File.Exists(p)) continue;
                var at = File.GetAttributes(p);
                if ((at & FileAttributes.ReadOnly) == 0)
                {
                    File.SetAttributes(p, at | FileAttributes.ReadOnly);
                    Console.WriteLine(L("  [提示] {0}: 已设为只读（防原地重写）：{1}", rep.Slot, p));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(L("  [警告] 设置只读失败 {0}: {1}", p, ex.Message));
            }
        }
    }

    static void Unprotect(string path)
    {
        try { if (File.Exists(path)) File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly); }
        catch { }
    }

    /// <summary>备份目录卫生：不应出现编辑器/旧部署留下的旁支文件（.bak / *.ntlbattle.json / *.orig / *.tmp）。
    /// 这类文件说明「备份曾被当成可写文档」——A2 审计认定的 2026-10-31 污染入口。</summary>
    public static List<string> StrayBackupFiles(string gameRoot)
    {
        var found = new List<string>();
        var roots = new[] { Path.Combine(gameRoot, "backup"), Path.Combine(Paths.NeutraledRoot(gameRoot), "backup") };
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                foreach (var f in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                {
                    var name = Path.GetFileName(f);
                    if (name.Equals("data.win", StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.Contains(".bak", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("ntlbattle", StringComparison.OrdinalIgnoreCase)
                        || name.EndsWith(".orig", StringComparison.OrdinalIgnoreCase)
                        || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                        found.Add(f);
                }
            }
            catch { }
        }
        return found;
    }

    /// <summary>把评估结果落地：补副本、登记 ledger（只在证据充分时）。返回是否健康。
    /// allowLiveFallback：两处备份都没有时，是否允许用「live 是原版」建立基线（部署路径需要，验证路径不需要）。</summary>
    static bool Apply(string gameRoot, SlotReport rep, bool allowLiveFallback, string origin)
    {
        var ledger = LoadLedger(gameRoot, out var bad);
        if (bad) return false;
        var chapter = rep.Slot;

        void Record(Identity id, string source)
        {
            ledger.Slots.TryGetValue(chapter, out var old);
            var rec = new SlotRecord
            {
                Slot = chapter,
                Rel = rep.Rel,
                Sha256 = id.Sha256,
                Size = id.Size,
                Fp = id.Fp,
                Source = source,
                EstablishedAt = Now(),
                History = old?.History ?? new List<HistoryEntry>(),
            };
            rec.History.Add(new HistoryEntry { Fp = id.Fp, Source = source, At = Now() });
            if (rec.History.Count > 20) rec.History.RemoveRange(0, rec.History.Count - 20);
            ledger.Slots[chapter] = rec;
            SaveLedger(gameRoot, ledger);
            Console.WriteLine(L("  [提示] {0}: 已登记原版基线 {1}（来源：{2}）", chapter, id.Fp, source));
            GameUpdate.SyncVanillaFp(gameRoot, chapter, id.Fp);
        }

        switch (rep.State)
        {
            case SlotState.Ok:
                return true;

            case SlotState.AutoHeal:
            {
                ledger.Slots.TryGetValue(chapter, out var expectRec);
                var expectSha = expectRec?.Sha256 ?? "";
                if (expectSha.Length == 0)
                {
                    Console.WriteLine(L("  [错误] {0}: 基线账本缺少该槽位指纹，无法自愈", chapter));
                    return false;
                }
                var p = Identify(rep.PrimaryPath);
                var m = Identify(rep.MirrorPath);
                var good = p != null && p.Value.Sha256 == expectSha ? p : m;
                if (good == null) return false;
                if (!rep.PrimaryExists || p?.Sha256 != good.Value.Sha256)
                    if (!CopyFile(rep.MirrorPath, rep.PrimaryPath)) return false;
                if (!rep.MirrorExists || m?.Sha256 != good.Value.Sha256)
                    if (!CopyFile(rep.PrimaryPath, rep.MirrorPath)) return false;
                // 复制后复查
                var np = Identify(rep.PrimaryPath, useCache: false);
                var nm = Identify(rep.MirrorPath, useCache: false);
                if (np == null || nm == null || np.Value.Sha256 != nm.Value.Sha256 || np.Value.Sha256 != good.Value.Sha256)
                {
                    Console.WriteLine(L("  [错误] {0}: 补建副本后校验仍不一致（规范 {1} / 镜像 {2}）", chapter, np?.Fp, nm?.Fp));
                    return false;
                }
                Console.WriteLine(L("  [提示] {0}: 已按基线补建副本（{1}）", chapter, good.Value.Fp));
                return true;
            }

            case SlotState.EstablishAgreement:
                Record(Identify(rep.PrimaryPath)!.Value, "agreement");
                SaveLedger(gameRoot, ledger);
                return true;

            case SlotState.EstablishPrimary:
            {
                var id = Identify(rep.PrimaryPath)!.Value;
                if (!CopyFile(rep.PrimaryPath, rep.MirrorPath)) return false;
                Record(id, origin + ":primary");
                return true;
            }

            case SlotState.EstablishMirror:
            {
                var id = Identify(rep.MirrorPath)!.Value;
                if (!CopyFile(rep.MirrorPath, rep.PrimaryPath)) return false;
                Record(id, origin + ":mirror");
                return true;
            }

            case SlotState.Missing:
            {
                if (!allowLiveFallback) return false;
                var live = Path.Combine(Paths.ChapterDir(gameRoot, chapter), "data.win");
                var lid = Identify(live);
                if (lid == null)
                {
                    Console.WriteLine(L("  [错误] {0}: 两处原版备份都缺失，且找不到 live data.win（{1}）", chapter, live));
                    return false;
                }
                if (lid.Value.HasMarkers)
                {
                    Console.WriteLine(L("  [错误] {0}: 两处原版备份都缺失，而 live 是部署产物（含产物标记）—— 无法凭空重建原版", chapter));
                    Console.WriteLine(L("         修复：用 Steam 校验文件完整性，或用 --repair-backup --from file:<干净的原版 data.win> --yes"));
                    return false;
                }
                if (!CopyFile(live, rep.PrimaryPath)) return false;
                if (!CopyFile(live, rep.MirrorPath)) return false;
                Record(lid.Value, origin + ":live-vanilla");
                return true;
            }

            default:
                return false;
        }
    }

    /// <summary>部署前调用：保证 backup 是**已验证的原版**，否则中止部署。
    /// 会把首次登记/补建副本/修正 deploy-state 一次性做完（幂等，可反复调用）。</summary>
    public static bool PrepareForDeploy(string gameRoot, string chapter)
    {
        chapter = string.IsNullOrWhiteSpace(chapter) ? "root" : chapter.Trim();
        var rep = Evaluate(gameRoot, chapter);
        if (rep.State == SlotState.Ok) { Protect(rep); return true; }
        if (Apply(gameRoot, rep, allowLiveFallback: true, origin: "deploy"))
        {
            Protect(rep);
            if (rep.State == SlotState.EstablishAgreement)
                Console.WriteLine(L("  [提示] {0}: 两处备份一致，已登记为原版基线（{1}）", chapter, rep.PrimaryFp));
            else if (rep.State is SlotState.EstablishPrimary or SlotState.EstablishMirror)
                Console.WriteLine(L("  [警告] {0}: 首次登记原版基线（只有一侧副本，已复制到另一侧）：{1}", chapter, rep.PrimaryFp));
            else if (rep.State == SlotState.Missing)
                Console.WriteLine(L("  [警告] {0}: 原版备份缺失，已用 live（确认无产物标记）建立两处备份：{1}", chapter, rep.PrimaryFp));
            return true;
        }

        Console.WriteLine(L("  [错误] {0}: 原版备份校验未通过 —— {1}", chapter, rep.Detail));
        Console.WriteLine(L("         规范位置: {0}", rep.PrimaryPath));
        Console.WriteLine(L("         镜像位置: {0}", rep.MirrorPath));
        if (rep.State == SlotState.Polluted || rep.State == SlotState.Drift || rep.State == SlotState.Missing)
        {
            Console.WriteLine(L("         修复：1) --verify-backup 查看全部槽位；2) --repair-backup --from mirror|live|file:<路径> --yes"));
            Console.WriteLine(L("               游戏更新后确认是新原版：--adopt-current --yes"));
        }
        return false;
    }

    /// <summary>游戏更新后「采纳 live 为新原版」——唯一会改变原版认定的入口之一（另一个是 --repair-backup）。
    /// 只允许 live 是**无产物标记的原版**（已部署产物永远不能当原版）；旧两处备份整体归档到
    /// Neutraled\backup\history\<label>\{game|neutraled}\<rel>，然后两处备份 ← live，重写 ledger，最后复查。
    /// 调用方：GameUpdate.AdoptCurrent（--adopt-current / 更新检测 --yes）。</summary>
    public static bool AdoptLiveAsBaseline(string gameRoot, string chapter, string label)
    {
        chapter = string.IsNullOrWhiteSpace(chapter) ? "root" : chapter.Trim();
        label = string.IsNullOrWhiteSpace(label) ? DateTime.Now.ToString("yyyyMMdd-HHmmss") : label.Trim();
        var live = Paths.ChapterDataWin(gameRoot, chapter);
        if (!File.Exists(live))
        {
            Console.WriteLine(L("  [错误] {0}: 找不到 live data.win，无法采纳（{1}）", chapter, live));
            return false;
        }
        var lid = Identify(live, useCache: false);
        if (lid == null)
        {
            Console.WriteLine(L("  [错误] {0}: live data.win 读取失败，无法采纳（{1}）", chapter, live));
            return false;
        }
        var id = lid.Value;
        if (id.HasMarkers)
        {
            Console.WriteLine(L("  [错误] {0}: live 含产物标记（已部署），拒绝采纳为原版", chapter));
            return false;
        }

        var rep = Evaluate(gameRoot, chapter);
        var rel = rep.Rel;
        var histRoot = Path.Combine(Paths.NeutraledRoot(gameRoot), "backup", GameUpdate.HistoryDirName, label);
        if (!ArchiveBackup(rep.PrimaryPath, Path.Combine(histRoot, "game", rel), chapter)) return false;
        if (!ArchiveBackup(rep.MirrorPath, Path.Combine(histRoot, "neutraled", rel), chapter)) return false;
        if (!CopyFile(live, rep.PrimaryPath)) return false;
        if (!CopyFile(live, rep.MirrorPath)) return false;

        var ledger = LoadLedger(gameRoot, out var bad);
        if (bad) return false;
        ledger.Slots.TryGetValue(chapter, out var oldRec);
        var rec = new SlotRecord
        {
            Slot = chapter,
            Rel = rel,
            Sha256 = id.Sha256,
            Size = id.Size,
            Fp = id.Fp,
            Source = "adopt:" + label,
            EstablishedAt = Now(),
            History = oldRec?.History ?? new List<HistoryEntry>(),
        };
        rec.History.Add(new HistoryEntry { Fp = id.Fp, Source = "adopt:" + label, At = Now() });
        if (rec.History.Count > 20) rec.History.RemoveRange(0, rec.History.Count - 20);
        ledger.Slots[chapter] = rec;
        SaveLedger(gameRoot, ledger);
        Console.WriteLine(L("  [提示] {0}: 已登记原版基线 {1}（来源：{2}）", chapter, id.Fp, "adopt:" + label));
        GameUpdate.SyncVanillaFp(gameRoot, chapter, id.Fp);

        var check = Evaluate(gameRoot, chapter);
        if (check.State != SlotState.Ok)
        {
            Console.WriteLine(L("  [错误] {0}: 采纳后校验未通过：{1}", chapter, check.Detail));
            return false;
        }
        Protect(check);
        return true;
    }

    /// <summary>采纳前归档旧备份（移动，不删除；同名目标存在则先删目标）。</summary>
    static bool ArchiveBackup(string from, string to, string chapter)
    {
        try
        {
            if (!File.Exists(from)) return true;
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            Unprotect(from);
            if (File.Exists(to)) { Unprotect(to); File.Delete(to); }
            File.Move(from, to);
            Console.WriteLine(L("  [提示] {0}: 旧备份已归档 -> {1}", chapter, to));
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine(L("  [错误] {0}: 归档旧备份失败 {1} -> {2}: {3}", chapter, from, to, ex.Message));
            return false;
        }
    }

    // ==================== CLI：--verify-backup ====================

    public static int VerifyCli(string gameRoot, string chapterArg, bool json = false)
    {
        var slots = ResolveSlots(gameRoot, chapterArg);
        if (slots == null) return 1;
        if (json) return VerifyJson(gameRoot, slots);
        Console.WriteLine(L("===== 原版备份校验（baseline） ====="));
        Console.WriteLine(L("  账本: {0}", LedgerPath(gameRoot)));
        Console.WriteLine(L("  严格校验不修改任何文件；--repair-backup 才会写盘"));
        int bad = 0, unledgered = 0;
        foreach (var chapter in slots)
        {
            var rep = Evaluate(gameRoot, chapter);
            var tag = rep.State switch
            {
                SlotState.Ok => L("[通过]"),
                SlotState.EstablishAgreement or SlotState.EstablishPrimary or SlotState.EstablishMirror => L("[未登记]"),
                SlotState.AutoHeal => L("[可修复]"),
                _ => L("[失败]"),
            };
            var line = "  " + tag + " " + chapter + L(": ");
            switch (rep.State)
            {
                case SlotState.Ok:
                    line += L("原版备份 {0}（规范/镜像/基线一致）", rep.PrimaryFp);
                    break;
                case SlotState.EstablishAgreement:
                    line += L("尚未登记基线，但两处副本一致 {0}（部署时会自动登记）", rep.PrimaryFp);
                    unledgered++;
                    break;
                case SlotState.EstablishPrimary:
                case SlotState.EstablishMirror:
                    line += L("尚未登记基线，只有一侧副本 {0}（部署时会补另一侧）", rep.PrimaryFp == EmptyFp ? rep.MirrorFp : rep.PrimaryFp);
                    unledgered++;
                    break;
                case SlotState.AutoHeal:
                    line += rep.Detail;
                    bad++;
                    break;
                default:
                    line += rep.Detail;
                    bad++;
                    break;
            }
            Console.WriteLine(line);
            if (!rep.IsHealthy && rep.State is not (SlotState.EstablishAgreement or SlotState.EstablishPrimary or SlotState.EstablishMirror))
            {
                Console.WriteLine(L("         规范: {0}", rep.PrimaryPath));
                Console.WriteLine(L("         镜像: {0}", rep.MirrorPath));
                if (rep.ExpectedFp.Length > 0) Console.WriteLine(L("         期望: {0}", rep.ExpectedFp));
            }
        }
        var stray = StrayBackupFiles(gameRoot);
        if (stray.Count > 0)
        {
            Console.WriteLine(L("  [提示] 备份目录里有 {0} 个非原版旁支文件（编辑器/旧部署残留，建议移走或删掉）：", stray.Count));
            foreach (var f in stray) Console.WriteLine("    " + f);
        }
        Console.WriteLine();
        if (bad == 0)
        {
            Console.WriteLine(unledgered > 0
                ? L("结果: 0 个槽位有问题；{0} 个槽位尚未登记基线（部署时会自动登记）", unledgered)
                : L("结果: 全部槽位原版备份健康 ✓"));
            return 0;
        }
        Console.WriteLine(L("结果: {0} 个槽位有问题（见上）—— 部署会在这些槽位上中止，直到修复", bad));
        Console.WriteLine(L("修复: --repair-backup [--chapter X|all] --from mirror|live|file:<路径> --yes"));
        return 3;
    }

    /// <summary>--verify-backup --json / --baseline verify --json：机器可读结果（与文本输出同源同判）。</summary>
    static int VerifyJson(string gameRoot, List<string> slots)
    {
        var rows = new List<object>();
        int bad = 0, unledgered = 0;
        foreach (var chapter in slots)
        {
            var rep = Evaluate(gameRoot, chapter);
            var healthy = rep.IsHealthy || rep.State is SlotState.EstablishAgreement or SlotState.EstablishPrimary or SlotState.EstablishMirror;
            if (!healthy) bad++;
            if (rep.State is SlotState.EstablishAgreement or SlotState.EstablishPrimary or SlotState.EstablishMirror) unledgered++;
            rows.Add(new
            {
                slot = chapter,
                state = rep.State.ToString(),
                ok = rep.State == SlotState.Ok,
                healthy,
                primaryPath = rep.PrimaryPath,
                mirrorPath = rep.MirrorPath,
                primaryFp = rep.PrimaryFp,
                mirrorFp = rep.MirrorFp,
                expectedFp = rep.ExpectedFp,
                detail = rep.Detail,
            });
        }
        var stray = StrayBackupFiles(gameRoot);
        var payload = new
        {
            schemaVersion = 1,
            ledgerPath = LedgerPath(gameRoot),
            slots = rows,
            strayFiles = stray,
            bad,
            unledgered,
            ok = bad == 0,
        };
        Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }));
        return bad == 0 ? 0 : 3;
    }

    // ==================== CLI：--repair-backup ====================

    public static int RepairCli(string gameRoot, string chapterArg, string from, bool yes)
    {
        var slots = ResolveSlots(gameRoot, chapterArg);
        if (slots == null) return 1;
        if (!yes)
        {
            Console.WriteLine(L("===== 修复原版备份（repair-backup，预演） ====="));
            foreach (var c in slots)
            {
                var rep = Evaluate(gameRoot, c);
                Console.WriteLine("  " + c + L(": ") + DescribeFix(rep, from));
            }
            Console.WriteLine(L("需要 --yes 才会写盘（先看 --verify-backup 的报告）"));
            return 1;
        }

        Console.WriteLine(L("===== 修复原版备份（repair-backup） ====="));
        int fixedCount = 0, bad = 0;
        foreach (var chapter in slots)
        {
            var rep = Evaluate(gameRoot, chapter);
            if (rep.State == SlotState.Ok) { Protect(rep); Console.WriteLine(L("  [跳过] {0}: 备份正常，无需修复（已确保只读保护）", chapter)); continue; }
            var ok = RepairOne(gameRoot, rep, from);
            if (ok) { Protect(rep); fixedCount++; Console.WriteLine(L("  [完成] {0}: 已修复原版备份（{1}）", chapter, rep.PrimaryFp)); }
            else { bad++; Console.WriteLine(L("  [错误] {0}: 无法修复 —— {1}", chapter, rep.Detail)); }
        }
        Console.WriteLine(L("结果: {0} 个槽位修复成功，{1} 个仍有问题", fixedCount, bad));
        return bad == 0 ? 0 : 3;
    }

    static string DescribeFix(SlotReport rep, string from)
    {
        var plan = rep.State switch
        {
            SlotState.Ok => L("无需修复"),
            SlotState.AutoHeal => L("从与基线一致的一侧补建另一侧"),
            SlotState.EstablishAgreement or SlotState.EstablishPrimary or SlotState.EstablishMirror => L("登记基线并补齐两处副本"),
            SlotState.Polluted => L("丢弃产物副本，按基线指纹重建（需要可信来源）"),
            SlotState.Drift => L("副本与基线不一致：需人工确认是游戏更新还是被改写"),
            SlotState.Missing => L("两处都缺失：需指定 --from file:<原版 data.win>"),
            _ => rep.Detail,
        };
        return from.Length == 0 ? plan : plan + L("（来源参数：{0}）", from);
    }

    static bool RepairOne(string gameRoot, SlotReport rep, string from)
    {
        var chapter = rep.Slot;
        var ledger = LoadLedger(gameRoot, out var bad);
        if (bad) return false;

        // 1) 从指定来源恢复（file:<路径> / mirror / live）
        string? srcPath = null;
        if (from.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            var p = from[5..].Trim().Trim('"');
            if (File.Exists(p)) srcPath = p;
        }
        else if (from.Equals("mirror", StringComparison.OrdinalIgnoreCase) && rep.MirrorExists) srcPath = rep.MirrorPath;
        else if (from.Equals("live", StringComparison.OrdinalIgnoreCase))
        {
            var live = Path.Combine(Paths.ChapterDir(gameRoot, chapter), "data.win");
            if (File.Exists(live) && !(Identify(live)?.HasMarkers ?? true)) srcPath = live;
        }

        if (srcPath != null)
        {
            var id = Identify(srcPath);
            if (id == null) return false;
            // 来源是否可信：对 ledger 已有条目 ⇒ 必须指纹一致；未登记 ⇒ 必须无产物标记
            if (ledger.Slots.TryGetValue(chapter, out var rec) && rec.Fp.Length > 0)
            {
                if (id.Value.Sha256 != rec.Sha256)
                {
                    Console.WriteLine(L("  [错误] {0}: 来源 {1} 的指纹 {2} 与基线 {3} 不一致，拒绝覆盖", chapter, srcPath, id.Value.Fp, rec.Fp));
                    return false;
                }
            }
            else if (id.Value.HasMarkers)
            {
                Console.WriteLine(L("  [错误] {0}: 来源 {1} 含产物标记，拒绝登记为原版", chapter, srcPath));
                return false;
            }
            // 未登记基线的槽位：--from mirror|live 是「猜测」，两处副本不一致时不能猜——
            // 猜错就会把被改写的副本登记成原版（等同于事故原型）。只有显式 file:<路径> 才算证据。
            if (!ledger.Slots.TryGetValue(chapter, out var known) || known.Fp.Length == 0)
            {
                var isGuess = !from.StartsWith("file:", StringComparison.OrdinalIgnoreCase);
                if (isGuess && rep.PrimaryExists && rep.MirrorExists && !rep.PrimaryFp.Equals(rep.MirrorFp, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(L("  [错误] {0}: 两处副本不一致且尚未登记基线，无法判断谁是原版——请用 --from file:<干净的原版 data.win>", chapter));
                    return false;
                }
            }
            if (!CopyFile(srcPath, rep.PrimaryPath)) return false;
            if (!CopyFile(srcPath, rep.MirrorPath)) return false;
            var after = Identify(rep.PrimaryPath, useCache: false);
            if (after == null || after.Value.Sha256 != id.Value.Sha256) return false;
            if (!ledger.Slots.ContainsKey(chapter)) RecordLedger(gameRoot, ledger, chapter, rep.Rel, id.Value, "repair:" + Path.GetFileName(srcPath));
            return true;
        }

        // 2) 没有来源参数：按基线自愈
        if (string.IsNullOrEmpty(from))
        {
            var applied = Apply(gameRoot, rep, allowLiveFallback: false, origin: "repair");
            if (applied) return true;
        }
        return false;
    }

    static void RecordLedger(string gameRoot, Ledger ledger, string chapter, string rel, Identity id, string source)
    {
        ledger.Slots.TryGetValue(chapter, out var old);
        var rec = new SlotRecord
        {
            Slot = chapter, Rel = rel, Sha256 = id.Sha256, Size = id.Size, Fp = id.Fp,
            Source = source, EstablishedAt = Now(), History = old?.History ?? new List<HistoryEntry>(),
        };
        rec.History.Add(new HistoryEntry { Fp = id.Fp, Source = source, At = Now() });
        ledger.Slots[chapter] = rec;
        SaveLedger(gameRoot, ledger);
        GameUpdate.SyncVanillaFp(gameRoot, chapter, id.Fp);
    }

    static List<string>? ResolveSlots(string gameRoot, string chapterArg)
    {
        if (string.IsNullOrWhiteSpace(chapterArg) || chapterArg.Equals("all", StringComparison.OrdinalIgnoreCase))
            return SlotsOf(gameRoot).ToList();
        var c = chapterArg.Trim();
        if (c != "root" && !c.StartsWith("chapter", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(L("[错误] 章节名无效: {0}（应为 root|chapterN|all）", chapterArg));
            return null;
        }
        return new List<string> { c };
    }
}
