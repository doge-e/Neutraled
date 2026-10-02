using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>部署缓存系统
///
/// 目的：部署一次要 30~60 秒（单章节 128 MB 写盘）。把"某组 mod 配置"的部署产物缓存起来，
/// 下次配置相同时直接硬链接复用，跳过部署。
///
/// 命中条件（全部相同）：Neutraled 版本 + 游戏版本 + 目标章节 + 启用 mod 集合（含各自版本）
///
/// 目录结构：
///   Neutraled/cache/
///     index.json          ← 索引（签名 → 元数据/大小/最后使用/命中次数）
///     &lt;签名前16位&gt;/
///         manifest.json   ← 该缓存的元数据
///         data/&lt;目标名&gt;/data.win
///         data/&lt;目标名&gt;/products/...  （章节/hook/API 注册表等部署产物）
/// </summary>
public static class Cache
{
    /// <summary>--no-cache：保留位（部署缓存主要服务 --launch）。</summary>
    public static bool NoCache = false;

    public const long DefaultMaxBytes = 4L * 1024 * 1024 * 1024;   // 4 GB

    public sealed class Entry
    {
        public string Sig = "";
        public string Created = "";
        public string LastUsed = "";
        public long Size;
        public int Hits;
        public List<string> Targets = new();      // root / chapter4 / ...
        public Dictionary<string,string> Mods = new();
        public string GameVersion = "";
        public string NtlVersion = "";
    }

    /// <summary>api/ 目录内容指纹（文件名 + 大小 + 修改时间）。</summary>
    public static string ApiFingerprint()
    {
        try
        {
            var apiDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "api");
            apiDir = Path.GetFullPath(apiDir);
            if (!Directory.Exists(apiDir)) return "noapi";
            var files = Directory.GetFiles(apiDir, "*.gml", SearchOption.AllDirectories)
                                 .OrderBy(f => f, StringComparer.Ordinal).ToArray();
            var sb = new StringBuilder();
            foreach (var f in files)
            {
                var fi = new FileInfo(f);
                sb.Append(fi.Name).Append(':').Append(fi.Length).Append(':')
                  .Append(fi.LastWriteTimeUtc.Ticks).Append(';');
            }
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
            return Convert.ToHexString(hash)[..16].ToLowerInvariant();
        }
        catch { return "err"; }
    }

    /// <summary>fonts/ 目录内容指纹（文件名 + 大小 + 修改时间）。
    /// 为什么必须有：字体包（ntl_font_cjk.json 与各 sheet PNG）不在 api/ 指纹里，
    /// 补字形或修字形 Offset 之后 --deploy 会打印「内容未变」直接跳过，
    /// 游戏里依旧是旧字体（实测踩过：zh-TW 繁体空洞、德语变音基线修好后都被静默跳过）。</summary>
    public static string FontsFingerprint()
    {
        try
        {
            var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fonts"));
            if (!Directory.Exists(dir)) return "nofonts";
            // ★ 只统计**影响渲染**的文件（*.json 与 *.png）：fonts/ 下的许可文档（OFL-NOTICE.txt、
            //   ofl/*.txt）与渲染无关，算进指纹会让「改文档」触发一次没必要的全量重建
            //   （独立复核发现的 medium：加 OFL-NOTICE.txt 已让 root/chapter1/chapter2 必然重建）。
            var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                                 .Where(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                                          || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                                 .OrderBy(f => f, StringComparer.Ordinal).ToArray();
            var sb = new StringBuilder();
            foreach (var f in files)
            {
                var fi = new FileInfo(f);
                sb.Append(fi.Name).Append(':').Append(fi.Length).Append(':')
                  .Append(fi.LastWriteTimeUtc.Ticks).Append(';');
            }
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
            return Convert.ToHexString(hash)[..16].ToLowerInvariant();
        }
        catch { return "err"; }
    }

    /// <summary>游戏版本指纹。
    /// ⚠️ 必须用 **backup 里的原版 data.win**：顶层 data.win 会被部署改写，
    ///    用它会导致每次部署后签名都变，缓存永远不命中。</summary>
    public static string GameVersion(string gameRoot)
    {
        var candidates = new[]
        {
            Path.Combine(gameRoot, "backup", "data.win"),
            Path.Combine(gameRoot, "backup", "DELTARUNE", "data.win"),
        };
        foreach (var p in candidates)
        {
            try
            {
                if (!File.Exists(p)) continue;
                var fi = new FileInfo(p);
                return $"{fi.Length}-{fi.LastWriteTimeUtc.Ticks}";
            }
            catch { }
        }
        // 退化：用游戏主程序版本（也稳定）
        try
        {
            var exe = Path.Combine(gameRoot, "DELTARUNE.exe");
            if (File.Exists(exe))
            {
                var fi = new FileInfo(exe);
                return $"exe-{fi.Length}-{fi.LastWriteTimeUtc.Ticks}";
            }
        }
        catch { }
        return "unknown";
    }

    private static string CacheRoot(string gameRoot) => Path.Combine(Paths.NeutraledRoot(gameRoot), "cache");
    private static string IndexPath(string gameRoot) => Path.Combine(CacheRoot(gameRoot), "index.json");

    /// <summary>计算配置签名。</summary>
    /// <summary>构建器自身的指纹（exe 大小 + 修改时间）：注入补丁的逻辑改了 ⇒ 必须重新部署。</summary>
    private static string BuilderFingerprint()
    {
        try
        {
            var self = Environment.ProcessPath;
            if (string.IsNullOrEmpty(self) || !File.Exists(self)) return "nobuild";
            var fi = new FileInfo(self);
            return fi.Length.ToString() + "@" + fi.LastWriteTimeUtc.Ticks.ToString();
        }
        catch { return "err"; }
    }

    public static string Signature(string gameVersion, string chapter, IEnumerable<ModEntry> mods, string extra = "")
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(SignatureRaw(gameVersion, chapter, mods, extra)))).ToLowerInvariant();

    /// <summary>签名原文（未哈希）。只给 NTL_SIG_DEBUG=1 的诊断输出用：
    /// 出现「签名每轮都变、跳过永不生效」时，把检查时与写回时的原文一比就知道哪个输入项在动。
    /// 正常路径不要拿它做比较（很长：含每个 mod 目录的完整文件清单）。</summary>
    public static string SignatureRaw(string gameVersion, string chapter, IEnumerable<ModEntry> mods, string extra = "")
    {
        var sb = new StringBuilder();
        sb.Append("ntl=").Append(Paths.ApiVersion()).Append('|');
        // api/ 目录内容指纹：改任何 GML 脚本都会让签名变化，避免命中旧缓存
        sb.Append("api=").Append(ApiFingerprint()).Append('|');
        // ★ fonts/ 目录指纹：只改字体包（补繁体字形、修字形 Offset）时 api/ 不变，
        //   必须靠这一项让缓存失效，否则部署被「内容未变」跳过（实测踩过）。
        sb.Append("fonts=").Append(FontsFingerprint()).Append('|');
        sb.Append("game=").Append(gameVersion).Append('|');
        sb.Append("chapter=").Append(chapter).Append('|');
        // extra：调用方给的额外指纹（例如外部章节清单）—— 它变了就必须重新部署，
        // 否则会被"内容未变"跳过（踩过：注册外部章节后 deploy 直接跳过了）。
        if (!string.IsNullOrEmpty(extra)) sb.Append("extra=").Append(extra).Append('|');
        // ★ 必须纳入部署档位：否则两种档位共用一个缓存键 —— 关掉开关仍会复用旧的加速产物，
        //   玩家以为已恢复，实际输入屏蔽还是坏的（开发者 1 实测发现）。
        sb.Append("fast=").Append(Injector.FastDeploy ? '1' : '0').Append('|');
        // ★ 也要纳入**构建器自身**的指纹：注入补丁（Injector.cs 里的 FR 查找替换）改了以后
        //   api/ 与 fonts/ 都没变，缓存会命中旧产物 —— 明明加了新补丁却被"内容未变"直接跳过
        //   （实测踩过：设置菜单第 6 行绘制 / 第 8 项解夹 等补丁加完，--deploy 直接说内容未变）。
        sb.Append("build=").Append(BuilderFingerprint()).Append('|');
        // mod 按 id 排序，保证顺序无关
        // ★ 还要带上**整包基底的内容指纹**（ref/data.win 的大小+时间）：
        //   汉化这类整包 mod 更新时版本号常常不变，只算 Id@Version 的话缓存会命中旧产物
        //   —— 实测踩过：换了汉化字体后 --launch 仍复用旧缓存，章节里中文照样空白。
        foreach (var m in mods.Where(m => m.Enabled).OrderBy(m => m.Id, StringComparer.Ordinal))
        {
            sb.Append(m.Id).Append('@').Append(m.Version);
            try
            {
                var rf = Path.Combine(m.Dir ?? "", "ref", "data.win");
                if (File.Exists(rf))
                {
                    var fi = new FileInfo(rf);
                    sb.Append('#').Append(fi.Length).Append('@').Append(fi.LastWriteTimeUtc.Ticks);
                }
            }
            catch { }
            // ★ mod 目录**自身**的文件清单也要进签名：原来只有 id@version + ref/data.win，
            //   于是改了 gml/*.gml 但不改版本号时，--deploy 会「内容未变」静默跳过，
            //   游戏一直跑旧脚本（实测踩过 —— task-5）。
            AppendModDirFingerprint(sb, m);
            sb.Append(';');
        }
        // 显式指定/配置指定的基底也要进签名（换基底必须让缓存失效）
        sb.Append("basemod=").Append(Program.BaseModId ?? "").Append('|');
        return sb.ToString();
    }

    /// <summary>单个 mod 目录指纹的文件数上限：超过就走降级分支（见 AppendModDirFingerprint）。
    /// 本机实测最大的启用 mod 目录 337 个文件，正常远低于此值。</summary>
    private const int MaxFingerprintFilesPerMod = 5000;

    /// <summary>把 mod **目录自身**的文件清单追加进签名（相对路径 + 大小 + 修改时间）。
    ///
    /// 为什么必须有：签名原来只有 id@version + ref/data.win，改 gml/*.gml 不改版本号时
    /// --deploy 会打印「内容未变」直接跳过 → 改动永远不生效（实测踩过）。
    /// 为什么用「大小 + 修改时间」而不是内容哈希：整包 mod 的 data/data.win 有 13 MB，
    /// 每次算签名都逐字节哈希太慢；大小+时间戳对「改脚本」这类改动足够灵敏、而且稳定。
    /// 为什么排除 .backup* / *.bak* / .tmp / bin / obj：部署自己会写备份文件，
    /// 不排除的话签名每轮都变、幂等跳过永久失效。
    /// 降级（文件数 &gt; MaxFingerprintFilesPerMod）：只逐条写「相对路径+大小」，
    /// 另加时间戳总和 —— 仍然覆盖 新增/删除/改大小/改时间，只是不逐条写时间，避免签名串涨到 MB 级。</summary>
    private static void AppendModDirFingerprint(StringBuilder sb, ModEntry m)
    {
        var dir = m.Dir;
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) { sb.Append("#nodir"); return; }
        try
        {
            var list = new List<(string rel, long len, long ticks)>();
            long tickSum = 0;
            CollectModFiles(dir, "", 0, list, ref tickSum);
            // 必须排序：目录枚举顺序由文件系统决定，不排序会让同一份内容算出不同签名
            list.Sort(static (a, b) => string.CompareOrdinal(a.rel, b.rel));
            if (list.Count <= MaxFingerprintFilesPerMod)
            {
                sb.Append('#').Append(list.Count);
                foreach (var e in list)
                    sb.Append('#').Append(e.rel).Append(':').Append(e.len).Append('@').Append(e.ticks);
            }
            else
            {
                sb.Append("#big=").Append(list.Count).Append('@').Append(tickSum);
                foreach (var e in list)
                    sb.Append('#').Append(e.rel).Append(':').Append(e.len);
            }
        }
        catch (Exception ex)
        {
            // 读不动就记一个**稳定**的错误标记：绝不能带时间戳，否则每轮签名都不同、幂等跳过永久失效
            sb.Append("#direrr=").Append(ex.GetType().Name);
        }
    }

    /// <summary>递归收集 mod 目录里的文件（逐目录容错：某个子目录读不动就跳过，
    /// 不让整份指纹退化成常量；深度上限防 junction 成环）。</summary>
    private static void CollectModFiles(string dir, string relBase, int depth,
        List<(string rel, long len, long ticks)> list, ref long tickSum)
    {
        if (depth > 24) return;
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir))
            {
                var name = Path.GetFileName(f);
                var rel = relBase.Length == 0 ? name : relBase + "/" + name;
                if (IsBuildArtifact(rel)) continue;
                var fi = new FileInfo(f);
                list.Add((rel, fi.Length, fi.LastWriteTimeUtc.Ticks));
                tickSum += fi.LastWriteTimeUtc.Ticks;
            }
        }
        catch { }
        try
        {
            foreach (var d in Directory.EnumerateDirectories(dir))
            {
                var name = Path.GetFileName(d);
                var rel = relBase.Length == 0 ? name : relBase + "/" + name;
                if (IsBuildArtifact(rel)) continue;
                CollectModFiles(d, rel, depth + 1, list, ref tickSum);
            }
        }
        catch { }
    }

    /// <summary>构建/备份副产物：不进签名。</summary>
    private static bool IsBuildArtifact(string relPath)
    {
        foreach (var seg in relPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (seg.Length == 0 || seg == "." || seg == "..") continue;
            if (seg.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                seg.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                seg.Equals(".tmp", StringComparison.OrdinalIgnoreCase)) return true;
            if (seg.StartsWith(".backup", StringComparison.OrdinalIgnoreCase)) return true;
            if (seg.Contains(".bak", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public static string SigShort(string sig) => sig.Length >= 16 ? sig[..16] : sig;

    /// <summary>读取索引（不存在则返回空表）。</summary>
    public static Dictionary<string, Entry> LoadIndex(string gameRoot)
    {
        var dict = new Dictionary<string, Entry>();
        var p = IndexPath(gameRoot);
        if (!File.Exists(p)) return dict;
        try
        {
            var j = JsonNode.Parse(File.ReadAllText(p)) as JsonObject;
            if (j?["entries"] is not JsonArray arr) return dict;
            foreach (var n in arr)
            {
                if (n is not JsonObject o) continue;
                var e = new Entry
                {
                    Sig = o["sig"]?.ToString() ?? "",
                    Created = o["created"]?.ToString() ?? "",
                    LastUsed = o["lastUsed"]?.ToString() ?? "",
                    Size = o["size"]?.GetValue<long>() ?? 0,
                    Hits = o["hits"]?.GetValue<int>() ?? 0,
                    GameVersion = o["gameVersion"]?.ToString() ?? "",
                    NtlVersion = o["ntlVersion"]?.ToString() ?? ""
                };
                if (o["targets"] is JsonArray ta)
                    foreach (var t in ta) if (t != null) e.Targets.Add(t.ToString());
                if (o["mods"] is JsonObject mo)
                    foreach (var kv in mo) e.Mods[kv.Key] = kv.Value?.ToString() ?? "";
                if (!string.IsNullOrEmpty(e.Sig)) dict[e.Sig] = e;
            }
        }
        catch (Exception ex) { Paths.Log(L("  [警告] 缓存索引读取失败: {0}", ex.Message)); }
        return dict;
    }

    public static void SaveIndex(string gameRoot, Dictionary<string, Entry> index)
    {
        Directory.CreateDirectory(CacheRoot(gameRoot));
        var arr = new JsonArray();
        foreach (var e in index.Values.OrderByDescending(x => x.LastUsed))
        {
            var mo = new JsonObject();
            foreach (var kv in e.Mods) mo[kv.Key] = kv.Value;
            arr.Add(new JsonObject
            {
                ["sig"] = e.Sig,
                ["created"] = e.Created,
                ["lastUsed"] = e.LastUsed,
                ["size"] = e.Size,
                ["hits"] = e.Hits,
                ["targets"] = new JsonArray(e.Targets.Select(t => (JsonNode)t!).ToArray()),
                ["mods"] = mo,
                ["gameVersion"] = e.GameVersion,
                ["ntlVersion"] = e.NtlVersion
            });
        }
        var root = new JsonObject { ["version"] = 1, ["maxBytes"] = MaxBytes(gameRoot), ["entries"] = arr };
        Paths.SafeWrite(IndexPath(gameRoot), root.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }));
    }

    /// <summary>玩家设置的缓存上限（config.json 的 cache_max_mb，默认 4096）。</summary>
    public static long MaxBytes(string gameRoot)
    {
        try
        {
            // ★ 走 ConfigFile（统一入口，且重复键会自愈）；裸 JsonNode.Parse 的重复键异常
            //   会在访问下标时才抛，见 ConfigFile.Load 的注释。
            var mb = ConfigFile.GetInt(gameRoot, "cache_max_mb");
            if (mb.HasValue && mb.Value > 0) return (long)mb.Value * 1024 * 1024;
        }
        catch { }
        return DefaultMaxBytes;
    }

    /// <summary>查缓存：命中返回条目，否则 null。</summary>
    public static Entry? Lookup(string gameRoot, string sig)
    {
        var idx = LoadIndex(gameRoot);
        if (!idx.TryGetValue(sig, out var e)) return null;
        // 目录必须真实存在
        var dir = Path.Combine(CacheRoot(gameRoot), SigShort(sig));
        if (!Directory.Exists(dir)) return null;
        // 每个目标都要有 data.win
        foreach (var t in e.Targets)
            if (!File.Exists(Path.Combine(dir, "data", t, "data.win"))) return null;
        return e;
    }

    /// <summary>把当前部署产物收进缓存。</summary>
    public static Entry Store(string gameRoot, string sig, string gameVersion,
                              string chapter, IEnumerable<ModEntry> mods, IEnumerable<string> targets)
    {
        var idx = LoadIndex(gameRoot);
        var dir = Path.Combine(CacheRoot(gameRoot), SigShort(sig));
        var dataDir = Path.Combine(dir, "data");
        Directory.CreateDirectory(dataDir);

        var entry = idx.TryGetValue(sig, out var old) ? old : new Entry { Sig = sig, Created = DateTime.Now.ToString("s") };
        entry.GameVersion = gameVersion;
        entry.NtlVersion = Paths.ApiVersion();
        entry.LastUsed = DateTime.Now.ToString("s");
        entry.Targets = targets.Distinct().ToList();
        entry.Mods = new Dictionary<string, string>();
        foreach (var m in mods.Where(m => m.Enabled))
            entry.Mods[m.Id] = m.Version;   // 同 id 多章节条目时后者覆盖（避免重复键异常）

        long total = 0;
        foreach (var t in entry.Targets)
        {
            var dst = Path.Combine(dataDir, t);
            Directory.CreateDirectory(dst);
            // 目标文件（顶层 data.win 或 章节目录/data.win）
            var win = t == "root" ? Paths.RootDataWin(gameRoot) : Paths.ChapterDataWin(gameRoot, t);
            if (File.Exists(win))
            {
                var target = Path.Combine(dst, "data.win");
                File.Copy(win, target, true);
                total += new FileInfo(target).Length;
            }
            // 部署时生成的产物（注册表等）
            foreach (var prod in new[] { "chapters.json", "hook-registry.json", "api-registry.json", "ns-registry.json" })
            {
                var src = Path.Combine(Paths.NeutraledRoot(gameRoot), prod);
                if (File.Exists(src))
                {
                    var pd = Path.Combine(dst, "products");
                    Directory.CreateDirectory(pd);
                    File.Copy(src, Path.Combine(pd, prod), true);
                    total += new FileInfo(Path.Combine(pd, prod)).Length;
                }
            }
        }
        entry.Size = total;
        idx[sig] = entry;
        File.WriteAllText(Path.Combine(dir, "manifest.json"),
            JsonSerializer.Serialize(entry, new JsonSerializerOptions(Paths.Json) { WriteIndented = true }));
        SaveIndex(gameRoot, idx);
        EnforceLimit(gameRoot, idx);
        Paths.Log(L("  缓存已保存: {0} ({1} MB, 目标 {2} 个)", SigShort(sig), total / 1024 / 1024, entry.Targets.Count));
        return entry;
    }

    /// <summary>把缓存产物应用到章节目录（硬链接，0 秒 0 额外空间）。</summary>
    public static int Apply(string gameRoot, Entry entry)
    {
        var dir = Path.Combine(CacheRoot(gameRoot), SigShort(entry.Sig));
        int n = 0;
        foreach (var t in entry.Targets)
        {
            var src = Path.Combine(dir, "data", t, "data.win");
            if (!File.Exists(src)) continue;
            var dst = t == "root" ? Paths.RootDataWin(gameRoot) : Paths.ChapterDataWin(gameRoot, t);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            if (File.Exists(dst)) { try { File.SetAttributes(dst, FileAttributes.Normal); File.Delete(dst); } catch { } }
            try
            {
                if (Platform.TryHardLink(dst, src)) { n++; continue; }   // 跨平台：Windows CreateHardLinkW / Unix link()
            }
            catch { }
            // 硬链接失败（跨卷等）→ 退回复制
            File.Copy(src, dst, true);
            n++;
        }
        // 应用部署产物
        foreach (var t in entry.Targets)
        {
            var pd = Path.Combine(dir, "data", t, "products");
            if (!Directory.Exists(pd)) continue;
            foreach (var f in Directory.GetFiles(pd))
                File.Copy(f, Path.Combine(Paths.NeutraledRoot(gameRoot), Path.GetFileName(f)), true);
        }
        // 更新命中统计
        var idx = LoadIndex(gameRoot);
        if (idx.TryGetValue(entry.Sig, out var e))
        {
            e.Hits++;
            e.LastUsed = DateTime.Now.ToString("s");
            SaveIndex(gameRoot, idx);
        }
        return n;
    }

    /// <summary>LRU 淘汰：超出上限时删除最久未使用的缓存。</summary>
    public static int EnforceLimit(string gameRoot, Dictionary<string, Entry>? idx = null)
    {
        idx ??= LoadIndex(gameRoot);
        var max = MaxBytes(gameRoot);
        long total = idx.Values.Sum(e => e.Size);
        if (total <= max) return 0;

        int removed = 0;
        foreach (var e in idx.Values.OrderBy(x => x.LastUsed).ToList())
        {
            if (total <= max) break;
            var dir = Path.Combine(CacheRoot(gameRoot), SigShort(e.Sig));
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
            total -= e.Size;
            idx.Remove(e.Sig);
            removed++;
            Paths.Log(L("  缓存淘汰: {0} (释放 {1} MB)", SigShort(e.Sig), e.Size / 1024 / 1024));
        }
        SaveIndex(gameRoot, idx);
        return removed;
    }

}
