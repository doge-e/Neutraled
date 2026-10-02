using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>游戏更新检测与「版本采纳」（--update-check / --adopt-current）。
///
/// 为什么需要：注入链本身不认识游戏版本。基底恒为**首次安装时备份的那份原版**，
/// Steam 更新之后照旧能注入、照旧能跑，但产物是「新游戏文件 + 旧版本体内容」的混合体
/// （静默回退游戏版本），而且自检/缓存都测不出来。这里把「游戏是否更新过」变成可判定、
/// 可报告、可人工采纳的事实。
///
/// 判据（按可靠度排序，全部是本地零依赖判定，不需要 UTMT 解码）：
///  1) **活跃 data.win 是不是我们的产物**：产物里必然有 obj_ntl_core / scr_ntl_init /
///     ntl_product_scope 三个明文标记（data.win 的字符串表是明文，扫字节即可）。
///  2) 活跃文件是原版、且与 backup 里的原版**逐字节不同** ⇒ Steam 换过本体（或被人手动替换）。
///  3) Steam appmanifest 的 buildid / InstalledDepots manifest、DELTARUNE.exe 的尺寸+时间。
///  4) Neutraled/deploy-state.json 里的上次基线（章节集合、每槽位原版/产物指纹、整包基底 mod 指纹）。
///
/// 纪律：**检测只报告，绝不自动改本体**。--deploy/--deploy-all/--launch 命中「需要确认」时
/// 停止并打印处置步骤，只有用户显式加 --yes（或先手动跑 --adopt-current --yes）才采纳新原版。
/// 旧备份一律**移动**到 Neutraled/backup/history/&lt;buildid&gt;/ 而不删除。</summary>
public static class GameUpdate
{
    public const string StateFileName = "deploy-state.json";
    public const string HistoryDirName = "history";

    /// <summary>产物标记：只在 Neutraled 注入后的 data.win 里出现。
    /// 选这三个是因为最不可能与别人的 mod 撞名：注入器自建的根对象、引导脚本、内容级自检脚本。</summary>
    static readonly string[] Markers = { "obj_ntl_core", "scr_ntl_init", "ntl_product_scope" };

    // ==================== 数据类型 ====================

    /// <summary>Steam 侧元数据（appmanifest_*.acf 里 installdir 与本游戏目录同名的那个）。</summary>
    public sealed class SteamInfo
    {
        public bool Found;
        public string AcfPath = "";
        public string AppId = "";
        public string Name = "";
        public string InstallDir = "";
        public string BuildId = "";
        public string DepotManifest = "";
        public long LastUpdated;
        public long SizeOnDisk;
    }

    /// <summary>deploy-state.json 里的一个槽位记录。</summary>
    public sealed class StateSlot
    {
        [JsonPropertyName("slot")] public string Slot { get; set; } = "";
        [JsonPropertyName("rel")] public string Rel { get; set; } = "";
        [JsonPropertyName("vanillaFp")] public string VanillaFp { get; set; } = "";
        [JsonPropertyName("productFp")] public string ProductFp { get; set; } = "";
        [JsonPropertyName("seenAt")] public string SeenAt { get; set; } = "";
    }

    /// <summary>Neutraled/deploy-state.json：上次「确认过的游戏版本」的事实记录。
    /// 首跑只建基线，不误报；这份文件也是「新章节是否已被用户确认」的唯一来源。</summary>
    public sealed class DeployState
    {
        [JsonPropertyName("version")] public int Version { get; set; } = 1;
        [JsonPropertyName("apiVersion")] public string ApiVersion { get; set; } = "";
        [JsonPropertyName("steamAppId")] public string SteamAppId { get; set; } = "";
        [JsonPropertyName("steamBuildId")] public string SteamBuildId { get; set; } = "";
        [JsonPropertyName("steamDepotManifest")] public string SteamDepotManifest { get; set; } = "";
        [JsonPropertyName("steamLastUpdated")] public long SteamLastUpdated { get; set; }
        [JsonPropertyName("exeFp")] public string ExeFp { get; set; } = "";
        [JsonPropertyName("confirmedChapters")] public List<string> ConfirmedChapters { get; set; } = new();
        [JsonPropertyName("slots")] public List<StateSlot> Slots { get; set; } = new();
        [JsonPropertyName("baseModId")] public string BaseModId { get; set; } = "";
        [JsonPropertyName("baseModFp")] public string BaseModFp { get; set; } = "";
        [JsonPropertyName("baseModGameFp")] public string BaseModGameFp { get; set; } = "";
        [JsonPropertyName("history")] public List<string> History { get; set; } = new();
        [JsonPropertyName("updatedAt")] public string UpdatedAt { get; set; } = "";
    }

    public enum SlotKind { Missing, Vanilla, Product }

    public sealed class SlotReport
    {
        public string Slot = "";
        public string Rel = "";
        public string LivePath = "";
        public bool LiveExists;
        public SlotKind Kind = SlotKind.Missing;
        public string LiveFp = "missing";
        public string BackupPath = "";
        public bool BackupExists;
        public string BackupFp = "missing";
        public bool Recorded;
        public string RecordedVanillaFp = "";
        public string RecordedProductFp = "";
        public bool Confirmed = true;
        /// <summary>活跃是原版 且 与备份不同 ⇒ Steam 换过本体。</summary>
        public bool VanillaChanged;
        /// <summary>活跃是原版 且 没有备份 ⇒ 需要建立基线（首次安装/备份被删）。</summary>
        public bool NeedsBaseline;
        /// <summary>活跃是产物 且 与记录的产物指纹不同 ⇒ 被重装/改写（缓存命中要据此失效）。</summary>
        public bool ProductStale;

        public string Describe()
        {
            if (Kind == SlotKind.Missing) return BackupExists ? L("产物丢失（有原版备份）") : L("缺失");
            if (Kind == SlotKind.Product) return ProductStale ? L("产物（已被改写）") : L("产物（已部署）");
            if (NeedsBaseline) return L("原版（无备份）");
            return VanillaChanged ? L("★ 原版已变（疑似游戏更新）") : L("原版（与备份一致）");
        }
    }

    public sealed class UpdateReport
    {
        public string GameRoot = "";
        public DeployState State = new();
        public bool HasBaseline;
        public SteamInfo Steam = new();
        public string ExeFp = "";
        public List<string> Discovered = new();
        public List<string> Confirmed = new();
        public List<string> NewChapters = new();
        public List<string> MissingChapters = new();
        public List<SlotReport> Slots = new();
        public bool VanillaChanged;
        public bool ProductsMissing;
        public bool SteamChanged;
        public bool ExeChanged;
        /// <summary>当前原版（root）指纹：活跃是原版取活跃，否则取备份（产物是从它部署出来的）。</summary>
        public string CurrentVanillaFp = "";
        public string BaseModId = "";
        public string BaseModFile = "";
        public string BaseModFp = "";
        public bool BaseModMissing;
        public bool BaseModChanged;
        public bool BaseModDrift;
        /// <summary>first（无基线）/ update / chapters / ok</summary>
        public string Verdict = "ok";
        /// <summary>有槽位需要「采纳当前原版为新基线」。</summary>
        public bool NeedsAdopt => Slots.Any(s => s.VanillaChanged || s.NeedsBaseline);
    }

    // ==================== 基础工具 ====================

    public static string StatePath(string gameRoot) => Path.Combine(Paths.NeutraledRoot(gameRoot), StateFileName);

    public static bool SamePath(string a, string b)
    {
        try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    static JsonSerializerOptions Opts() => new(Paths.Json) { WriteIndented = true };

    static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

    static string Stamp(long unix)
    {
        if (unix <= 0) return "?";
        try { return DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().ToString("yyyy-MM-dd HH:mm"); }
        catch { return "?"; }
    }

    static string Short(string p)
    {
        try
        {
            var ntl = Path.GetFullPath(p);
            var root = Path.GetPathRoot(ntl) ?? "";
            return ntl.Length <= 64 ? ntl : "…" + ntl.Substring(Math.Max(0, ntl.Length - 60));
        }
        catch { return p; }
    }

    /// <summary>文件指纹：sha256 前 16 字节 + 长度（与部署签名的产物指纹同口径）。</summary>
    public static string FileFp(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) return "missing";
            using var fs = File.OpenRead(path);
            var h = SHA256.HashData(fs);
            return Convert.ToHexString(h)[..16].ToLowerInvariant() + ":" + fi.Length;
        }
        catch { return "?"; }
    }

    /// <summary>扫明文标记判定「这份 data.win 是不是 Neutraled 产物」。分块流式读，126 MB 约 50 ms。</summary>
    public static bool HasProductMarkers(string path)
    {
        try
        {
            const int Chunk = 4 << 20;
            var needles = new byte[Markers.Length][];
            for (int i = 0; i < Markers.Length; i++) needles[i] = Encoding.UTF8.GetBytes(Markers[i]);
            var buf = new byte[Chunk + 64];
            int carry = 0;
            using var fs = File.OpenRead(path);
            while (true)
            {
                int read = fs.Read(buf, carry, Chunk);
                int total = carry + read;
                if (total > 0)
                {
                    var span = new ReadOnlySpan<byte>(buf, 0, total);
                    foreach (var n in needles) if (span.IndexOf(n) >= 0) return true;
                }
                if (read <= 0) break;
                carry = Math.Min(64, total);
                Array.Copy(buf, total - carry, buf, 0, carry);
                if (read < Chunk) break;
            }
        }
        catch { return false; }
        return false;
    }

    public static string RelDataWin(string gameRoot, string slot)
        => Path.GetRelativePath(gameRoot, Paths.ChapterDataWin(gameRoot, slot)).Replace('\\', '/');

    static string ExeFp(string gameRoot)
    {
        try
        {
            var exe = Path.Combine(gameRoot, "DELTARUNE.exe");
            var fi = new FileInfo(exe);
            if (!fi.Exists) return "noexe";
            return fi.Length + "@" + fi.LastWriteTimeUtc.Ticks;
        }
        catch { return "?"; }
    }

    // ==================== 章节目录发现 ====================

    /// <summary>实探游戏根下的章节目录（chapter&lt;n&gt;_&lt;平台后缀&gt; 且带 data.win），按章节号升序返回槽位名。</summary>
    public static List<string> DiscoverChapters(string gameRoot)
    {
        var found = new List<(int N, string Slot)>();
        try
        {
            foreach (var dir in Directory.GetDirectories(gameRoot))
            {
                var name = Path.GetFileName(dir);
                var m = Regex.Match(name, @"^chapter(\d+)_(windows|linux|unix|macos)$", RegexOptions.IgnoreCase);
                if (!m.Success) continue;
                if (!File.Exists(Path.Combine(dir, "data.win"))) continue;
                if (!int.TryParse(m.Groups[1].Value, out var n)) continue;
                var slot = "chapter" + n;
                if (!found.Any(x => x.Slot == slot)) found.Add((n, slot));
            }
        }
        catch { }
        return found.OrderBy(x => x.N).Select(x => x.Slot).ToList();
    }

    /// <summary>实探到的最大章节号（部署并行分片要用；官方 5 章之外出现新章节时不能再靠硬编码 7）。</summary>
    public static int DiscoveredSlotCount(string gameRoot)
    {
        int max = 0;
        foreach (var s in DiscoverChapters(gameRoot))
            if (s.StartsWith("chapter", StringComparison.Ordinal) && int.TryParse(s[7..], out var n) && n > max) max = n;
        return max;
    }

    // ==================== Steam 元数据 ====================

    static string AcfVal(string txt, string key)
    {
        var m = Regex.Match(txt, "\"" + Regex.Escape(key) + "\"\\s+\"([^\"]*)\"");
        return m.Success ? m.Groups[1].Value : "";
    }

    /// <summary>InstalledDepots 块里的 depot:manifest 列表（多 depot 时按 depot 号排序拼起来）。</summary>
    static string DepotManifests(string txt)
    {
        var list = new List<string>();
        foreach (Match m in Regex.Matches(txt, "\"(?<depot>\\d+)\"\\s*\\{\\s*\"manifest\"\\s*\"(?<man>\\d+)\""))
            list.Add(m.Groups["depot"].Value + ":" + m.Groups["man"].Value);
        if (list.Count == 0)
        {
            var one = AcfVal(txt, "manifest");
            if (one.Length > 0) list.Add(one);
        }
        list.Sort(StringComparer.Ordinal);
        return string.Join("+", list);
    }

    /// <summary>读 Steam appmanifest：扫 library 的 steamapps 目录里所有 acf，取 installdir == 游戏目录名的那个。
    /// 不写死 appid（1671210）：新章节/新 DLC 可能是**另一个 appid**，靠 installdir 匹配才能发现。</summary>
    public static SteamInfo ReadSteam(string gameRoot)
    {
        var info = new SteamInfo();
        try
        {
            var dir = new DirectoryInfo(Path.GetFullPath(gameRoot).TrimEnd('\\', '/'));
            if (!dir.Exists) return info;
            var want = dir.Name;
            var cands = new List<string>();
            // 典型布局：<library>/steamapps/common/<installdir> ⇒ steamapps 在上两级
            var common = dir.Parent;
            var steamapps = common?.Parent;
            if (steamapps != null && steamapps.Exists) cands.Add(steamapps.FullName);
            // 兜底：上溯 5 层找带 appmanifest_*.acf 的目录（非典型/自定义布局）
            var up = dir;
            for (int i = 0; i < 5 && up?.Parent != null; i++)
            {
                up = up.Parent;
                var acfs = Directory.Exists(up.FullName) ? Directory.GetFiles(up.FullName, "appmanifest_*.acf") : Array.Empty<string>();
                if (acfs.Length > 0 && !cands.Contains(up.FullName)) { cands.Add(up.FullName); break; }
            }
            foreach (var sa in cands)
            {
                foreach (var acf in Directory.GetFiles(sa, "appmanifest_*.acf"))
                {
                    string txt;
                    try { txt = File.ReadAllText(acf); } catch { continue; }
                    var inst = AcfVal(txt, "installdir");
                    if (!string.Equals(inst, want, StringComparison.OrdinalIgnoreCase)) continue;
                    info.Found = true;
                    info.AcfPath = acf;
                    info.AppId = AcfVal(txt, "appid");
                    info.Name = AcfVal(txt, "name");
                    info.InstallDir = inst;
                    info.BuildId = AcfVal(txt, "buildid");
                    info.DepotManifest = DepotManifests(txt);
                    long.TryParse(AcfVal(txt, "LastUpdated"), out info.LastUpdated);
                    long.TryParse(AcfVal(txt, "SizeOnDisk"), out info.SizeOnDisk);
                    return info;
                }
            }
        }
        catch { }
        return info;
    }

    // ==================== 状态读写 ====================

    static DeployState? _cache;
    static string _cacheKey = "";

    public static DeployState Load(string gameRoot)
    {
        var path = StatePath(gameRoot);
        string key;
        try { key = path + "|" + (File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks.ToString() : "none"); }
        catch { key = path; }
        if (_cache != null && _cacheKey == key) return _cache;
        var st = new DeployState();
        try
        {
            if (File.Exists(path))
            {
                var parsed = JsonSerializer.Deserialize<DeployState>(File.ReadAllText(path), Opts());
                if (parsed != null) st = parsed;
            }
        }
        catch { /* 坏文件当没有：检测是只读动作，绝不因状态文件损坏而崩 */ }
        _cache = st;
        _cacheKey = key;
        return st;
    }

    public static void Save(string gameRoot, DeployState st)
    {
        try
        {
            st.Version = 1;
            st.ApiVersion = Paths.ApiVersion();
            st.UpdatedAt = Now();
            var dir = Paths.NeutraledRoot(gameRoot);
            Directory.CreateDirectory(dir);
            Paths.SafeWrite(StatePath(gameRoot), JsonSerializer.Serialize(st, Opts()));
            _cache = st;
            _cacheKey = StatePath(gameRoot) + "|" + File.GetLastWriteTimeUtc(StatePath(gameRoot)).Ticks;
        }
        catch (Exception ex) { Console.WriteLine(L("  [警告] 写 {0} 失败: {1}", StateFileName, ex.Message)); }
    }

    /// <summary>某个槽位是否「已确认」（可以用它部署/注入）。官方 1..5 章恒为已确认，
    /// 之后的新章节必须由用户跑一次 --adopt-current --yes 才登记，符合「新章节只登记、确认后才执行」。</summary>
    public static bool IsConfirmed(string gameRoot, string chapter)
    {
        var m = Regex.Match(chapter ?? "", @"^chapter(\d+)$");
        if (m.Success && int.TryParse(m.Groups[1].Value, out var n) && n >= 1 && n <= Chapters.OfficialCount) return true;
        var st = Load(gameRoot);
        return st.ConfirmedChapters.Contains(chapter ?? "");
    }

    // ==================== 检查 ====================

    public static UpdateReport Check(string gameRoot)
    {
        var rep = new UpdateReport { GameRoot = gameRoot };
        var st = Load(gameRoot);
        rep.State = st;
        rep.HasBaseline = st.Slots.Count > 0;
        rep.Steam = ReadSteam(gameRoot);
        rep.ExeFp = ExeFp(gameRoot);

        var discovered = DiscoverChapters(gameRoot);
        rep.Discovered = discovered;
        rep.Confirmed = st.ConfirmedChapters.Count > 0
            ? new List<string>(st.ConfirmedChapters)
            : DefaultConfirmed(discovered);
        foreach (var c in discovered) if (!rep.Confirmed.Contains(c)) rep.NewChapters.Add(c);
        foreach (var c in rep.Confirmed) if (c != "root" && !discovered.Contains(c)) rep.MissingChapters.Add(c);

        var slots = new List<string> { "root" };
        foreach (var c in discovered) if (!slots.Contains(c)) slots.Add(c);
        foreach (var c in rep.Confirmed) if (!slots.Contains(c)) slots.Add(c);

        foreach (var slot in slots)
        {
            var live = Paths.ChapterDataWin(gameRoot, slot);
            var bk = Paths.BackupDataWin(gameRoot, slot);
            var bkReal = File.Exists(bk) && !SamePath(bk, live);
            var sr = new SlotReport
            {
                Slot = slot,
                Rel = RelDataWin(gameRoot, slot),
                LivePath = live,
                LiveExists = File.Exists(live),
                BackupPath = bkReal ? bk : "",
                BackupExists = bkReal,
                Confirmed = rep.Confirmed.Contains(slot)
            };
            if (sr.LiveExists)
            {
                sr.Kind = HasProductMarkers(live) ? SlotKind.Product : SlotKind.Vanilla;
                sr.LiveFp = FileFp(live);
            }
            if (bkReal) sr.BackupFp = FileFp(bk);
            var rec = st.Slots.FirstOrDefault(x => x.Slot == slot);
            if (rec != null) { sr.Recorded = true; sr.RecordedVanillaFp = rec.VanillaFp; sr.RecordedProductFp = rec.ProductFp; }
            if (sr.Kind == SlotKind.Vanilla)
            {
                if (!sr.BackupExists) sr.NeedsBaseline = true;
                else if (!string.Equals(sr.BackupFp, sr.LiveFp, StringComparison.Ordinal)) sr.VanillaChanged = true;
            }
            else if (sr.Kind == SlotKind.Product && sr.Recorded && sr.RecordedProductFp.Length > 0
                     && !string.Equals(sr.RecordedProductFp, sr.LiveFp, StringComparison.Ordinal))
                sr.ProductStale = true;
            rep.Slots.Add(sr);
        }

        rep.VanillaChanged = rep.Slots.Any(s => s.VanillaChanged);
        rep.ProductsMissing = rep.Slots.Any(s => s.Kind == SlotKind.Missing && s.BackupExists);
        rep.SteamChanged = rep.HasBaseline && rep.Steam.Found &&
            ((st.SteamBuildId.Length > 0 && !string.Equals(st.SteamBuildId, rep.Steam.BuildId, StringComparison.Ordinal)) ||
             (st.SteamDepotManifest.Length > 0 && !string.Equals(st.SteamDepotManifest, rep.Steam.DepotManifest, StringComparison.Ordinal)));
        rep.ExeChanged = rep.HasBaseline && st.ExeFp.Length > 0 && !string.Equals(st.ExeFp, rep.ExeFp, StringComparison.Ordinal);

        var rootSlot = rep.Slots.First(s => s.Slot == "root");
        rep.CurrentVanillaFp = rootSlot.Kind == SlotKind.Vanilla && rootSlot.LiveExists ? rootSlot.LiveFp
                             : (rootSlot.BackupExists ? rootSlot.BackupFp : "");

        rep.BaseModId = (ConfigFile.GetString(gameRoot, "base_mod") ?? "").Trim();
        // none = 显式「不要整包基底」，不是「这个 mod 找不到」
        if (string.Equals(rep.BaseModId, "none", StringComparison.OrdinalIgnoreCase)) rep.BaseModId = "";
        if (rep.BaseModId.Length > 0)
        {
            rep.BaseModFile = ResolveBaseModFile(gameRoot, rep.BaseModId);
            if (rep.BaseModFile.Length > 0 && File.Exists(rep.BaseModFile))
            {
                rep.BaseModFp = FileFp(rep.BaseModFile);
                // 「整包 mod 被作者更新过」：指纹与上次记录不同 ⇒ 视为已适配当前游戏版本，漂移解除。
                rep.BaseModChanged = st.BaseModFp.Length > 0 && !string.Equals(st.BaseModFp, rep.BaseModFp, StringComparison.Ordinal);
                // 「整包基底漂移」：这个 mod 文件没变，但游戏原版已经换过（buildid/本体变了）
                // ⇒ 用它当基底部署就等于把游戏本体回退到旧版本内容。
                rep.BaseModDrift = st.BaseModGameFp.Length > 0 && rep.CurrentVanillaFp.Length > 0
                                   && !string.Equals(st.BaseModGameFp, rep.CurrentVanillaFp, StringComparison.Ordinal)
                                   && !rep.BaseModChanged;
            }
            else rep.BaseModMissing = true;
        }

        rep.Verdict = !rep.HasBaseline ? "first"
                    : (rep.VanillaChanged || rep.SteamChanged || rep.ExeChanged) ? "update"
                    : (rep.NewChapters.Count > 0 || rep.MissingChapters.Count > 0) ? "chapters"
                    : "ok";
        return rep;
    }

    /// <summary>首次运行时的「已确认章节」= 官方 1..5 里实际存在的那些（新章节留待用户确认）。</summary>
    static List<string> DefaultConfirmed(List<string> discovered)
    {
        var list = new List<string>();
        foreach (var c in discovered)
        {
            var m = Regex.Match(c, @"^chapter(\d+)$");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var n) && n >= 1 && n <= Chapters.OfficialCount) list.Add(c);
        }
        return list;
    }

    /// <summary>把 config.json 的 base_mod（Id / 名字 / 父目录名）解析成整包 mod 的 data.win 路径。</summary>
    public static string ResolveBaseModFile(string gameRoot, string baseModId)
    {
        if (string.IsNullOrWhiteSpace(baseModId)) return "";
        if (string.Equals(baseModId.Trim(), "none", StringComparison.OrdinalIgnoreCase)) return "";
        try
        {
            var modsRoot = Path.Combine(Paths.NeutraledRoot(gameRoot), "mods");
            var mods = Mods.ScanMods(modsRoot, "root", true, true);
            var hit = mods.FirstOrDefault(m => string.Equals(m.Id, baseModId, StringComparison.OrdinalIgnoreCase))
                   ?? mods.FirstOrDefault(m => string.Equals(m.Name, baseModId, StringComparison.OrdinalIgnoreCase))
                   ?? mods.FirstOrDefault(m => string.Equals(Path.GetFileName(m.Dir), baseModId, StringComparison.OrdinalIgnoreCase));
            if (hit == null) return "";
            var rel = !string.IsNullOrWhiteSpace(hit.RefSource) ? hit.RefSource! : "ref/data.win";
            var path = Path.Combine(hit.Dir, rel.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(path) ? path : "";
        }
        catch { return ""; }
    }

    // ==================== 报告 ====================

    public static void Print(UpdateReport r)
    {
        Console.WriteLine(L("===== 游戏更新检测 ====="));
        Console.WriteLine(L("  游戏目录 : {0}", r.GameRoot));
        if (r.Steam.Found)
            Console.WriteLine(L("  Steam    : appid {0}（{1}）buildid {2}  depot {3}  更新于 {4}",
                r.Steam.AppId, r.Steam.Name, r.Steam.BuildId, r.Steam.DepotManifest.Length > 0 ? r.Steam.DepotManifest : "?", Stamp(r.Steam.LastUpdated)));
        else
            Console.WriteLine(L("  Steam    : 未找到 appmanifest（非 Steam 安装 / 库在别处）—— 只用 data.win 与章节目录判定"));
        Console.WriteLine(L("  基线     : {0}", r.HasBaseline
            ? L("{0}（记录于 {1}）", StateFileName, r.State.UpdatedAt)
            : L("无 —— 首次运行，本次只建基线、不报更新")));
        Console.WriteLine(L("  可注入槽位: {0}", r.Slots.Count == 0 ? L("（无）") : string.Join(", ", r.Slots.Select(s => s.Slot))));
        foreach (var s in r.Slots)
        {
            var detail = s.Kind == SlotKind.Product
                ? L("产物 {0}", s.LiveFp)
                : L("活跃 {0}  备份 {1}", s.LiveFp, s.BackupFp);
            Console.WriteLine(L("    [{0}] {1}  {2}", s.Slot.PadRight(9), s.Describe(), detail));
        }
        if (r.NewChapters.Count > 0)
            Console.WriteLine(L("  新章节   : {0}（已登记，等待确认后才注入）", string.Join(", ", r.NewChapters)));
        if (r.MissingChapters.Count > 0)
            Console.WriteLine(L("  章节消失 : {0}（已确认但目录不见了）", string.Join(", ", r.MissingChapters)));
        if (r.SteamChanged) Console.WriteLine(L("  Steam    : buildid/depot 与上次记录不同 ⇒ 游戏本体更新过"));
        if (r.ExeChanged) Console.WriteLine(L("  可执行   : DELTARUNE.exe 变了（{0}）", r.ExeFp));
        if (r.ProductsMissing) Console.WriteLine(L("  注意     : 有槽位的产物丢了（只剩原版备份）⇒ 部署即可恢复"));

        if (r.BaseModId.Length > 0)
        {
            if (r.BaseModMissing)
                Console.WriteLine(L("  整包基底 : config.json 的 base_mod=「{0}」没找到 ⇒ 部署会退回用原版备份（该 mod 的内容会丢）", r.BaseModId));
            else
            {
                Console.WriteLine(L("  整包基底 : {0} → {1}  指纹 {2}", r.BaseModId, Short(r.BaseModFile), r.BaseModFp));
                if (r.BaseModDrift)
                {
                    Console.WriteLine(L("  ⛔ 基底漂移: 这个整包 mod 的文件没变，但游戏原版已经换过 ⇒ 现在部署会用它的**旧版本内容**当基底，等于把游戏本体回退到更新前。"));
                    Console.WriteLine(L("     处理：① 等该 mod 作者更新（推荐，更新后本检测会自动解除）"));
                    Console.WriteLine(L("           ② ntl-builder --deploy-all --force --base-mod none（用新原版做基底：不丢游戏版本，但会失去该 mod 的整包内容，例如汉化字体/剧情文本）"));
                    Console.WriteLine(L("           ③ 明知风险继续：加 --accept-base-drift（只在游戏已能跑、你确认过差异时才用）"));
                }
                else if (r.BaseModChanged)
                    Console.WriteLine(L("    （该 mod 的文件与上次记录不同 ⇒ 视为已适配当前游戏版本）"));
            }
        }

        switch (r.Verdict)
        {
            case "first":
                Console.WriteLine(L("  ℹ 结论：还没有基线。用 --deploy（或 --adopt-current）建立基线后，后续更新就能被判出来。"));
                break;
            case "update":
                Console.WriteLine(L("  ⛔ 结论：检测到游戏更新（活跃 data.win 已被换回原版 / buildid 变了）"));
                Console.WriteLine(L("     直接部署会用**旧的原版备份**当基底 ⇒ 静默回退游戏版本。按顺序处理："));
                Console.WriteLine(L("       1) 退出游戏"));
                Console.WriteLine(L("       2) ntl-builder --adopt-current --yes      采纳当前原版为新基底（旧备份移到 backup/{0}/，不删）", HistoryDirName));
                Console.WriteLine(L("       3) ntl-builder --deploy-all --force      重新注入（字体/文本/注册表按新版本重建）"));
                Console.WriteLine(L("       4) 想回滚：备份都在 Neutraled/backup/{0}/ 里，拷回对应路径即可", HistoryDirName));
                break;
            case "chapters":
                Console.WriteLine(L("  ℹ 结论：游戏本体没变，但章节集合变了（见上）。新章节需要确认后才注入：--adopt-current --yes"));
                break;
            default:
                Console.WriteLine(L("  ✅ 结论：没有检测到游戏更新（{0} 个槽位的原版备份与当前一致）", r.Slots.Count));
                break;
        }
    }

    // ==================== 基线 / 采纳 ====================

    /// <summary>建立基线（首跑）：只记录事实 + 把缺备份的原版槽位补一份备份。返回是否有写入。</summary>
    public static bool EstablishBaseline(string gameRoot, UpdateReport rep)
    {
        bool wrote = false;
        foreach (var s in rep.Slots)
        {
            if (s.Kind != SlotKind.Vanilla || !s.LiveExists || s.BackupExists) continue;
            try
            {
                CopyToBackupRoots(gameRoot, s.Rel, s.LivePath, archive: false, label: "");
                s.BackupExists = true;
                s.BackupPath = Paths.BackupDataWin(gameRoot, s.Slot);
                s.BackupFp = s.LiveFp;
                s.NeedsBaseline = false;
                wrote = true;
                Console.WriteLine(L("  [基线] 已为 {0} 补一份原版备份: {1}", s.Slot, Short(s.BackupPath)));
            }
            catch (Exception ex) { Console.WriteLine(L("  [警告] 备份 {0} 失败: {1}", s.Slot, ex.Message)); }
        }
        RecordState(gameRoot, rep, baseModUsed: true);
        return true;
    }

    /// <summary>把活跃文件复制到两个 backup 根（&lt;gameRoot&gt;/backup 与 Neutraled/backup）。</summary>
    static void CopyToBackupRoots(string gameRoot, string rel, string src, bool archive, string label)
    {
        foreach (var root in new[] { Path.Combine(gameRoot, "backup"), Path.Combine(Paths.NeutraledRoot(gameRoot), "backup") })
        {
            var dst = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            if (SamePath(dst, src)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            if (archive && File.Exists(dst) && !SamePath(dst, src))
            {
                // 两个备份根的区分**必须**全等比较：曾经写成 root.StartsWith(gameRoot)，
                // 而 Neutraled/backup 也以 gameRoot 开头 ⇒ 两根都被算成 game ⇒ 第二份（Neutraled/backup
                // 里的旧备份）撞名后走 File.Delete 被删掉，违反「旧备份全留」纪律。
                // 沙箱 S2 实测：history/<buildid>/neutraled/ 是空的、旧备份凭空消失。
                var isCanonical = SamePath(root, Path.Combine(gameRoot, "backup"));
                var hid = Path.Combine(Paths.NeutraledRoot(gameRoot), "backup", HistoryDirName, label,
                                       (isCanonical ? "game" : "neutraled"),
                                       rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(hid)!);
                if (!File.Exists(hid)) File.Move(dst, hid); else File.Delete(dst);
            }
            File.Copy(src, dst, true);
        }
    }

    /// <summary>把当前活跃的原版采纳为新的基线：旧备份移动进 backup/history/（不删），活跃原版复制成新备份。</summary>
    public static int AdoptCurrent(string gameRoot, bool yes, bool force)
    {
        var rep = Check(gameRoot);
        Print(rep);

        if (!yes)
        {
            Console.WriteLine(L("  ⚠ 预览：未加 --yes，什么都没改。确认无误后重跑：ntl-builder --adopt-current --yes"));
            return 0;
        }

        // 保护 1：活跃文件不能是我们的产物（那种情况先卸载/还原，别把产物当原版）
        var products = rep.Slots.Where(s => s.Kind == SlotKind.Product && (s.VanillaChanged || s.Confirmed)).ToList();
        var blocking = rep.Slots.Where(s => s.Kind == SlotKind.Product).ToList();
        if (rep.NeedsAdopt && blocking.Count > 0)
        {
            var overlap = blocking.Where(s => s.VanillaChanged).ToList();
            if (overlap.Count > 0 && !force)
            {
                Console.WriteLine(L("  ⛔ 拒绝采纳：这些槽位的产物还在活跃位置，不能把产物当原版备份：{0}", string.Join(", ", overlap.Select(s => s.Slot))));
                Console.WriteLine(L("     先 ntl-builder --uninstall（或 --restore-chapter &lt;章&gt;）还原原版，再重跑本命令。"));
                return 3;
            }
        }

        if (!rep.NeedsAdopt)
        {
            Console.WriteLine(L("  ✅ 无需采纳：没有「活跃原版与备份不一致」的槽位（只刷新基线记录）"));
            RecordState(gameRoot, rep, baseModUsed: true, confirmDiscovered: true);
            return 0;
        }

        var label = rep.Steam.BuildId.Length > 0 ? rep.Steam.BuildId : DateTime.Now.ToString("yyyyMMdd-HHmmss");
        int adopted = 0, archived = 0;
        foreach (var s in rep.Slots)
        {
            if (s.Kind != SlotKind.Vanilla || !s.LiveExists) continue;
            if (!s.VanillaChanged && !s.NeedsBaseline) continue;
            try
            {
                CopyToBackupRoots(gameRoot, s.Rel, s.LivePath, archive: true, label: label);
                if (s.VanillaChanged) archived++;
                adopted++;
                Console.WriteLine(L("  [采纳] {0}: 新原版 {1}（旧备份已存入 backup/{2}/{3}/）", s.Slot, s.LiveFp, HistoryDirName, label));
            }
            catch (Exception ex) { Console.WriteLine(L("  [错误] 采纳 {0} 失败: {1}", s.Slot, ex.Message)); return 1; }
        }
        // 清理旧缓存目录：签名口径含游戏版本，采纳后旧条目永远命中不了，留着只占空间
        try { Program.CacheClear(gameRoot, quiet: true); } catch { }
        // --yes 语义 = 用户确认了「当前这套章节集合」（含新出现的章节）
        RecordState(gameRoot, rep, baseModUsed: true, confirmDiscovered: true);
        Console.WriteLine(L("  ✅ 已采纳 {0} 个槽位的原版为新基线（归档 {1} 份旧备份）", adopted, archived));
        Console.WriteLine(L("     下一步：ntl-builder --deploy-all --force"));
        return 0;
    }

    /// <summary>按报告刷新 deploy-state.json（记录当前 steam/exe/章节/槽位/base 事实）。</summary>
    public static void RecordState(string gameRoot, UpdateReport rep, bool baseModUsed, bool confirmDiscovered = false)
    {
        var st = Load(gameRoot);
        st.SteamAppId = rep.Steam.AppId;
        st.SteamBuildId = rep.Steam.BuildId;
        st.SteamDepotManifest = rep.Steam.DepotManifest;
        st.SteamLastUpdated = rep.Steam.LastUpdated;
        st.ExeFp = rep.ExeFp;

        var confirmed = new List<string>(st.ConfirmedChapters);
        foreach (var c in rep.Confirmed) if (!confirmed.Contains(c) && c != "root") confirmed.Add(c);
        if (confirmDiscovered) foreach (var c in rep.Discovered) if (!confirmed.Contains(c) && c != "root") confirmed.Add(c);
        if (confirmed.Count == 0) confirmed = DefaultConfirmed(rep.Discovered);
        st.ConfirmedChapters = confirmed;

        foreach (var s in rep.Slots)
        {
            var rec = st.Slots.FirstOrDefault(x => x.Slot == s.Slot);
            if (rec == null) { rec = new StateSlot { Slot = s.Slot }; st.Slots.Add(rec); }
            rec.Rel = s.Rel;
            rec.SeenAt = Now();
            if (s.Kind == SlotKind.Product) rec.ProductFp = s.LiveFp;
            else if (s.Kind == SlotKind.Vanilla) { rec.ProductFp = ""; rec.VanillaFp = s.LiveFp; }
            if (s.BackupExists && s.Kind != SlotKind.Vanilla) rec.VanillaFp = s.BackupFp;
        }
        if (baseModUsed && rep.BaseModFp.Length > 0)
        {
            st.BaseModId = rep.BaseModId;
            st.BaseModFp = rep.BaseModFp;
            st.BaseModGameFp = rep.CurrentVanillaFp;
        }
        st.History.Add("adopt/buildid=" + rep.Steam.BuildId + " @" + Now());
        if (st.History.Count > 40) st.History.RemoveRange(0, st.History.Count - 40);
        Save(gameRoot, st);
    }

    /// <summary>某个槽位部署成功后调用：记下产物指纹与当前原版指纹（含整包基底配对，用于漂移判定）。</summary>
    public static void RecordSlotAfterDeploy(string gameRoot, string chapter, string? baseModId, string? baseModFile)
    {
        try
        {
            var live = Paths.ChapterDataWin(gameRoot, chapter);
            var bk = Paths.BackupDataWin(gameRoot, chapter);
            var bkReal = File.Exists(bk) && !SamePath(bk, live);

            var st = Load(gameRoot);
            var rec = st.Slots.FirstOrDefault(x => x.Slot == chapter);
            if (rec == null) { rec = new StateSlot { Slot = chapter }; st.Slots.Add(rec); }
            rec.Rel = RelDataWin(gameRoot, chapter);
            rec.ProductFp = FileFp(live);
            if (bkReal) rec.VanillaFp = FileFp(bk);
            rec.SeenAt = Now();

            var steam = ReadSteam(gameRoot);
            if (steam.Found)
            {
                st.SteamAppId = steam.AppId;
                st.SteamBuildId = steam.BuildId;
                st.SteamDepotManifest = steam.DepotManifest;
                st.SteamLastUpdated = steam.LastUpdated;
            }
            st.ExeFp = ExeFp(gameRoot);

            var conf = new List<string>(st.ConfirmedChapters);
            foreach (var c in DiscoverChapters(gameRoot)) if (!conf.Contains(c) && c != "root") conf.Add(c);
            if (conf.Count == 0) conf = DefaultConfirmed(DiscoverChapters(gameRoot));
            st.ConfirmedChapters = conf;

            if (chapter == "root")
            {
                if (!string.IsNullOrWhiteSpace(baseModFile) && File.Exists(baseModFile!))
                {
                    st.BaseModId = baseModId ?? "";
                    st.BaseModFp = FileFp(baseModFile!);
                    st.BaseModGameFp = bkReal ? FileFp(bk) : FileFp(live);
                }
                else if (string.Equals((baseModId ?? "").Trim(), "none", StringComparison.OrdinalIgnoreCase))
                {
                    // 显式不用整包基底：清掉配对，避免留下假的漂移判定
                    st.BaseModId = "";
                    st.BaseModFp = "";
                    st.BaseModGameFp = "";
                }
            }
            if (st.ApiVersion.Length == 0) st.ApiVersion = Paths.ApiVersion();
            Save(gameRoot, st);
        }
        catch (Exception ex) { Console.WriteLine(L("  [警告] 记录部署状态失败: {0}", ex.Message)); }
    }

    /// <summary>--deploy-all 收尾 / --doctor 用：重新扫描一遍并刷新基线（不采纳、不备份）。</summary>
    public static int Refresh(string gameRoot)
    {
        var rep = Check(gameRoot);
        Print(rep);
        RecordState(gameRoot, rep, baseModUsed: false);
        return rep.Verdict == "update" || rep.BaseModDrift ? 2 : 0;
    }

    /// <summary>--update-check：只读检测（不写任何文件）。
    /// 退出码：0 = 没有检测到更新 / 2 = 需要用户处理（更新、新章节、基底漂移、产物缺失）/ 3 = 检测本身失败。</summary>
    public static int CheckCli(string gameRoot)
    {
        UpdateReport rep;
        try { rep = Check(gameRoot); }
        catch (Exception ex)
        {
            Console.WriteLine(L("  [错误] 更新检测失败: {0}", ex.Message));
            return 3;
        }
        Print(rep);
        if (rep.Verdict == "first")
        {
            Console.WriteLine(L("  （--update-check 只读：本次没有写任何文件；跑一次 --deploy 会建立基线）"));
            return 0;
        }
        bool need = rep.Verdict == "update" || rep.BaseModDrift || rep.Verdict == "chapters" || rep.ProductsMissing;
        if (need)
        {
            Console.WriteLine(L("  退出码 2 = 需要处理：更新 ⇒ --adopt-current --yes；基底漂移 ⇒ --base-mod none 或 --accept-base-drift；新章节 ⇒ --adopt-current --yes；产物缺失 ⇒ --deploy-all"));
            return 2;
        }
        Console.WriteLine(L("  退出码 0 = 没有检测到更新"));
        return 0;
    }

    /// <summary>部署/启动前置检测。
    /// 返回 true = 可以继续；false = 需要用户确认（已打印指引，调用方直接返回非 0）。
    /// 规则：命中「游戏更新」时必须显式 --yes（--yes 会先采纳新原版再继续）；
    /// 命中「整包基底漂移」时必须 --base-mod none 或 --accept-base-drift（否则部署必然回退游戏版本）。</summary>
    public static bool Gate(string gameRoot, bool yes, string what)
    {
        UpdateReport rep;
        try { rep = Check(gameRoot); }
        catch (Exception ex) { Console.WriteLine(L("  [警告] 更新检测失败（{0}）⇒ 继续", ex.Message)); return true; }

        if (rep.Verdict == "first")
        {
            Console.WriteLine(L("  [更新检测] 首次运行：建立基线（不报更新）"));
            try { EstablishBaseline(gameRoot, rep); } catch { }
            return true;
        }

        bool updated = rep.Verdict == "update";
        bool drift = rep.BaseModDrift;
        if (!updated && !drift)
        {
            if (rep.Verdict == "chapters")
            {
                Print(rep);
                Console.WriteLine(L("  [更新检测] 有新章节未确认 ⇒ 本次不注入它们（确认：--adopt-current --yes）"));
            }
            else if (rep.NewChapters.Count == 0 && rep.MissingChapters.Count == 0 && rep.ProductsMissing)
                Console.WriteLine(L("  [更新检测] 产物缺失（{0}）⇒ 本次会重建", string.Join(", ", rep.Slots.Where(s => s.Kind == SlotKind.Missing).Select(s => s.Slot))));
            return true;
        }

        Print(rep);
        // config.json 里写 base_mod=none 与命令行 --base-mod none 等价（后者在 Program 里已展开）
        var cfgBase = "";
        try { cfgBase = (ConfigFile.GetString(gameRoot, "base_mod") ?? "").Trim(); } catch { }
        bool baseNone = Program.BaseModNone || string.Equals(cfgBase, "none", StringComparison.OrdinalIgnoreCase);
        if (drift && !(baseNone || Program.AcceptBaseDrift))
        {
            Console.WriteLine(L("  ⛔ 停止（--{0} 前置检测）：整包基底 mod 与当前游戏版本不匹配，继续部署会把游戏本体回退到更新前。", what));
            Console.WriteLine(L("     要么等该 mod 更新，要么显式选择：--base-mod none（用新原版）或 --accept-base-drift（明知风险继续）。"));
            return false;
        }
        if (updated && !yes)
        {
            Console.WriteLine(L("  ⛔ 停止（--{0} 前置检测）：检测到游戏更新，需要你确认。", what));
            Console.WriteLine(L("     确认后：ntl-builder --{0} --yes（会先 --adopt-current，再继续）", what));
            Console.WriteLine(L("     或手动两步：--adopt-current --yes 然后 --deploy-all --force"));
            return false;
        }
        if (rep.NeedsAdopt)
        {
            var rc = AdoptCurrent(gameRoot, yes: true, force: true);
            if (rc != 0) return false;
        }
        else
        {
            Console.WriteLine(L("  [更新检测] data.win 未被 Steam 改写（更新在别处）⇒ 刷新基线后继续"));
            RecordState(gameRoot, rep, baseModUsed: false);
        }
        return true;
    }
}
