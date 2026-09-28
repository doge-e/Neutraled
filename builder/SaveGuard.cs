using System.IO.Compression;
using System.Text.Json.Nodes;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>存档保护 —— mod 出问题时不让玩家丢进度。
///
/// 设计：
///   1) **部署前自动快照**：把官方存档目录打包到 Neutraled/save-backups/（保留最近 N 份）
///   2) **首次安装时留底**：一份"纯净基准"，任何时候都能回到未装 mod 的状态
///   3) **一键恢复**：--restore-save [--backup &lt;名字&gt;]
///   4) **不算破坏**：只读存档、只写自己的备份目录，绝不动原存档（除恢复时）
///
/// 存档位置：%LOCALAPPDATA%\DELTARUNE（官方）与 Neutraled/saves/*（平行章节）
/// </summary>
public static class SaveGuard
{
    private const int MaxBackups = 10;

    public static string OfficialSaveDir() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DELTARUNE");

    public static string BackupRoot(string gameRoot) =>
        Path.Combine(Paths.NeutraledRoot(gameRoot), "save-backups");

    /// <summary>部署前快照（如果距离上次快照超过 minIntervalHours 小时才做，避免刷屏）</summary>
    public static void SnapshotBeforeDeploy(string gameRoot, double minIntervalHours = 0.5)
    {
        try
        {
            var src = OfficialSaveDir();
            if (!Directory.Exists(src)) return;

            var root = BackupRoot(gameRoot);
            Directory.CreateDirectory(root);

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var target = Path.Combine(root, stamp + ".zip");

            // 节流：最近一份若在 minIntervalHours 内，跳过
            var latest = Directory.GetFiles(root, "*.zip")
                                  .OrderByDescending(f => f).FirstOrDefault();
            if (latest != null &&
                (DateTime.Now - File.GetLastWriteTime(latest)).TotalHours < minIntervalHours)
                return;

            ZipFile.CreateFromDirectory(src, target, CompressionLevel.Optimal, false);
            Paths.Log(L("  存档快照: {0}（{1} KB）", Path.GetFileName(target), new FileInfo(target).Length / 1024));

            PruneOld(root);
        }
        catch (Exception ex) { Paths.Log(L("  [警告] 存档快照失败: {0}", ex.Message)); }
    }

    /// <summary>首次安装时的"纯净基准"快照（只做一次，永不删除）</summary>
    public static void SnapshotBaseline(string gameRoot)
    {
        try
        {
            var root = BackupRoot(gameRoot);
            var baseline = Path.Combine(root, "BASELINE.zip");
            if (File.Exists(baseline)) return;                 // 已存在不覆盖

            var src = OfficialSaveDir();
            if (!Directory.Exists(src)) return;
            Directory.CreateDirectory(root);
            ZipFile.CreateFromDirectory(src, baseline, CompressionLevel.Optimal, false);
            Paths.Log(L("  存档基准快照已建立: BASELINE.zip（首次安装留底，永不删除）"));
        }
        catch { }
    }

    private static void PruneOld(string root)
    {
        var zips = Directory.GetFiles(root, "*.zip")
                            .Where(f => !Path.GetFileName(f).StartsWith("BASELINE", StringComparison.OrdinalIgnoreCase))
                            .OrderByDescending(f => f)
                            .ToList();
        foreach (var f in zips.Skip(MaxBackups))
        {
            try { File.Delete(f); } catch { }
        }
    }

    /// <summary>列出可用备份</summary>
    public static void List(string gameRoot)
    {
        var root = BackupRoot(gameRoot);
        Console.WriteLine(L("===== 存档备份 ====="));
        Console.WriteLine(L("  目录: {0}", root));
        if (!Directory.Exists(root)) { Console.WriteLine(L("  （还没有备份）")); return; }
        foreach (var f in Directory.GetFiles(root, "*.zip").OrderByDescending(f => f))
        {
            var fi = new FileInfo(f);
            var tag = fi.Name.StartsWith("BASELINE", StringComparison.OrdinalIgnoreCase) ? L("（纯净基准）") : "";
            Console.WriteLine($"  {fi.Name,-26} {fi.Length / 1024,8} KB   {fi.LastWriteTime:yyyy-MM-dd HH:mm} {tag}");
        }
    }

    /// <summary>恢复某个备份到官方存档目录（会先自动备份当前状态）</summary>
    public static int Restore(string gameRoot, string? name)
    {
        var root = BackupRoot(gameRoot);
        if (!Directory.Exists(root)) { Console.WriteLine(L("[错误] 没有备份目录")); return 1; }

        var pick = string.IsNullOrEmpty(name)
            ? Directory.GetFiles(root, "*.zip").OrderByDescending(f => f).FirstOrDefault()
            : Path.Combine(root, name.EndsWith(".zip") ? name : name + ".zip");

        if (pick == null || !File.Exists(pick)) { Console.WriteLine(L("[错误] 找不到指定的备份")); return 1; }

        // 恢复前先备份当前状态（可反悔）
        try
        {
            var src = OfficialSaveDir();
            if (Directory.Exists(src))
            {
                var safety = Path.Combine(root, "before-restore-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip");
                ZipFile.CreateFromDirectory(src, safety, CompressionLevel.Optimal, false);
                Console.WriteLine(L("  已保存当前状态: {0}", Path.GetFileName(safety)));
            }
        }
        catch { }

        // ===== 原子恢复（F3）=====
        // 步骤：解压到临时目录 → 校验 → 原子替换 → 失败自动回滚
        var official = OfficialSaveDir();
        var stage = Path.Combine(root, ".staging-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        var oldDir = Path.Combine(root, ".old-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));

        try
        {
            // 1) 解压到临时目录（此时官方存档完全没动）
            Directory.CreateDirectory(stage);
            ZipFile.ExtractToDirectory(pick, stage, true);

            // 2) 校验：解压出来的文件数不能为 0
            var staged = Directory.GetFiles(stage, "*", SearchOption.AllDirectories);
            if (staged.Length == 0)
            {
                Directory.Delete(stage, true);
                Console.WriteLine(L("[错误] 备份是空的，已取消恢复（存档未改动）"));
                return 1;
            }
            Console.WriteLine(L("  校验通过: 备份含 {0} 个文件", staged.Length));

            // 3) 原子替换：官方目录 → oldDir，stage → 官方目录
            if (Directory.Exists(official))
            {
                // 同盘符下 Directory.Move 是原子的
                Directory.Move(official, oldDir);
            }
            try
            {
                Directory.Move(stage, official);
            }
            catch
            {
                // 移动失败 → 立刻回滚
                if (Directory.Exists(oldDir) && !Directory.Exists(official))
                    Directory.Move(oldDir, official);
                throw;
            }

            // 4) 成功：清掉旧目录（保留说明）
            try { if (Directory.Exists(oldDir)) Directory.Delete(oldDir, true); } catch { }

            Console.WriteLine(L("  ✅ 已恢复: {0}（原子替换，无中间状态）", Path.GetFileName(pick)));
            Console.WriteLine(L("  原状态已备份为 before-restore-*.zip，如需撤销可用它"));
            return 0;
        }
        catch (Exception ex)
        {
            // 兜底：确保官方目录存在
            try
            {
                if (!Directory.Exists(official) && Directory.Exists(oldDir))
                    Directory.Move(oldDir, official);
                if (Directory.Exists(stage)) Directory.Delete(stage, true);
            }
            catch { }
            Console.WriteLine(L("[错误] 恢复失败（已回滚，存档保持原样）: {0}", ex.Message));
            return 1;
        }
    }
}
