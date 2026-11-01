using System.Text.Json;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>C4 P1-4：把原来散在六套机制里的「备份 / 还原」收敛成一张能一眼看懂的视图。
///
/// 语义四层（名字必须区分「权威基线 / 可重建缓存 / 玩家数据」）：
/// <list type="bullet">
/// <item><b>baseline</b>：官方原版基线（<c>&lt;游戏根&gt;\backup</c> + <c>Neutraled\backup</c> + <c>baseline.json</c>）——权威，只读；</item>
/// <item><b>snapshot</b>：单个 mod 的版本快照（<c>--snapshot-*</c>）；</item>
/// <item><b>restorepoint</b>：整游戏恢复点（<c>--restore-*</c>）；</item>
/// <item><b>save</b>：玩家存档（<c>Neutraled\saves\DELTARUNE</c>）。</item>
/// </list>
/// 另有 <b>cache</b>（可重建，不是备份）与 <b>legacy</b>（<c>.backup_files</c> 部署期外部文件覆盖，不是备份）。
/// 本命令只读：只统计目录/账本，不做哈希（哈希校验用 <c>--verify-backup</c>）。</summary>
public static class BackupStatus
{
    sealed class Layer
    {
        public string Kind = "";
        public string Path = "";
        public int Count;
        public long Bytes;
        public string? LastAt;
        public bool Rebuildable;
        public bool Legacy;
        public string Note = "";
    }

    static (int Count, long Bytes, DateTime? Last) ScanDir(string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return (0, 0, null);
        int count = 0;
        long bytes = 0;
        DateTime? last = null;
        try
        {
            foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var fi = new FileInfo(f);
                    count++;
                    bytes += fi.Length;
                    if (last == null || fi.LastWriteTimeUtc > last.Value) last = fi.LastWriteTimeUtc;
                }
                catch { }
            }
        }
        catch { }
        return (count, bytes, last);
    }

    static Layer DirLayer(string kind, string path, string note, bool rebuildable = false, bool legacy = false)
    {
        var (c, b, l) = ScanDir(path);
        return new Layer
        {
            Kind = kind,
            Path = path,
            Count = c,
            Bytes = b,
            LastAt = l?.ToString("o"),
            Rebuildable = rebuildable,
            Legacy = legacy,
            Note = note,
        };
    }

    static Layer BaselineLayer(string gameRoot)
    {
        var ledger = Baseline.LoadLedger(gameRoot, out _);
        int slots = ledger?.Slots?.Count ?? 0;
        long bytes = 0;
        if (ledger != null) foreach (var kv in ledger.Slots) bytes += kv.Value.Size;
        var primaryRoot = System.IO.Path.Combine(gameRoot, "backup");
        var mirrorRoot = System.IO.Path.Combine(Paths.NeutraledRoot(gameRoot), "backup");
        var (pc, pb, _) = ScanDir(primaryRoot);
        var (mc, mb, _) = ScanDir(mirrorRoot);
        return new Layer
        {
            Kind = "baseline",
            Path = primaryRoot,
            Count = slots,
            Bytes = bytes,
            LastAt = ledger?.UpdatedAt,
            Rebuildable = false,
            Legacy = false,
            Note = L("官方原版基线（权威、只读）；规范 {0} 个文件 / 镜像 {1} 个文件；账本 {2}", pc, mc, Baseline.LedgerPath(gameRoot)),
        };
    }

    public static int Cli(string gameRoot, bool json)
    {
        var layers = new List<Layer>
        {
            BaselineLayer(gameRoot),
            DirLayer("snapshot", Paths.SnapshotsRoot(gameRoot), L("单 mod 版本快照（--snapshot-*）")),
            DirLayer("restorepoint", Paths.RestoreRoot(gameRoot), L("整游戏恢复点（--restore-*）")),
            DirLayer("save", BSide.SaveDir(gameRoot), L("玩家存档（不要当备份删）")),
            DirLayer("cache", System.IO.Path.Combine(Paths.NeutraledRoot(gameRoot), "cache"), L("部署缓存：可重建，不是备份"), rebuildable: true),
            DirLayer("legacy", System.IO.Path.Combine(Paths.NeutraledRoot(gameRoot), ".backup_files"), L("部署期外部文件覆盖（legacy，不是备份）"), legacy: true),
        };
        var authoritative = layers.Where(l => !l.Rebuildable && !l.Legacy).ToList();
        long authoritativeBytes = authoritative.Sum(l => l.Bytes);
        long cacheBytes = layers.Where(l => l.Rebuildable).Sum(l => l.Bytes);

        if (json)
        {
            var payload = new
            {
                schemaVersion = 1,
                gameRoot,
                generatedAt = DateTime.Now.ToString("o"),
                layers = layers.Select(l => new
                {
                    kind = l.Kind,
                    path = l.Path,
                    count = l.Count,
                    bytes = l.Bytes,
                    lastAt = l.LastAt,
                    rebuildable = l.Rebuildable,
                    legacy = l.Legacy,
                    note = l.Note,
                }),
                summary = new
                {
                    authoritativeBytes,
                    cacheBytes,
                    baselineSlots = layers[0].Count,
                    hasLegacy = layers.Any(l => l.Legacy && l.Count > 0),
                },
            };
            Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }));
            return 0;
        }

        Console.WriteLine(L("===== 备份 / 还原现状 ====="));
        Console.WriteLine(L("  游戏根: {0}", gameRoot));
        foreach (var l in layers)
        {
            var tag = l.Kind switch
            {
                "baseline" => L("[baseline ] 权威基线"),
                "snapshot" => L("[snapshot ] 单 mod 版本"),
                "restorepoint" => L("[restorept] 整游戏恢复点"),
                "save" => L("[save     ] 玩家存档"),
                "cache" => L("[cache    ] 可重建（不是备份）"),
                _ => L("[legacy   ] 旧机制（不是备份）"),
            };
            var size = Human(l.Bytes);
            var last = string.IsNullOrEmpty(l.LastAt) ? L("无") : DateTime.TryParse(l.LastAt, out var dt) ? dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : l.LastAt!;
            Console.WriteLine("  " + tag + "  " + L("文件 {0} 个 / {1} / 最近 {2}", l.Count, size, last));
            Console.WriteLine("            " + l.Path);
            if (!string.IsNullOrEmpty(l.Note)) Console.WriteLine("            " + l.Note);
            Console.WriteLine();
        }
        Console.WriteLine(L("结论: 权威备份（baseline/snapshot/restorepoint/save）合计 {0}；可重建缓存 {1}（删了会重新生成）", Human(authoritativeBytes), Human(cacheBytes)));
        if (layers.Any(l => l.Legacy && l.Count > 0))
            Console.WriteLine(L("提示: .backup_files 是部署期写的外部文件覆盖，不要当备份；确认不再需要可手工清理。"));
        Console.WriteLine(L("深入: --verify-backup（哈希校验）/ --baseline status --json（机器可读）/ --doctor"));
        return 0;
    }

    static string Human(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024) return (bytes / 1024.0 / 1024 / 1024).ToString("0.00") + " GB";
        if (bytes >= 1024 * 1024) return (bytes / 1024.0 / 1024).ToString("0.0") + " MB";
        if (bytes >= 1024) return (bytes / 1024.0).ToString("0.0") + " KB";
        return bytes + " B";
    }
}
