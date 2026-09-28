using static Neutraled.Builder.Lang;
namespace Neutraled.Builder;

/// <summary>builder 自身产生的临时文件清理（不依赖外部定时任务）。</summary>
public static class Cleanup
{
    /// <summary>清理：转换临时目录、过期解包缓存、过期截图。</summary>
    public static void Run(string gameRoot, bool verbose = false)
    {
        long freed = 0;

        // 1) %TEMP%\ntl_conv_* （DeltaImport 的工作目录，正常已自删，异常时可能残留）
        try
        {
            foreach (var d in Directory.GetDirectories(Path.GetTempPath(), "ntl_conv_*"))
            {
                try
                {
                    freed += DirSize(d);
                    Directory.Delete(d, true);
                }
                catch { }
            }
        }
        catch { }

        // 2) .unpacked 里超过 14 天未使用的解包缓存
        try
        {
            var unpacked = Path.Combine(Paths.NeutraledRoot(gameRoot), ".unpacked");
            if (Directory.Exists(unpacked))
            {
                foreach (var d in Directory.GetDirectories(unpacked))
                {
                    if (Directory.GetLastWriteTimeUtc(d) < DateTime.UtcNow.AddDays(-14))
                    {
                        try { freed += DirSize(d); Directory.Delete(d, true); } catch { }
                    }
                }
            }
        }
        catch { }

        // 3) 旧截图（保留最近 10 张）
        try
        {
            var shotDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DELTARUNE");
            if (Directory.Exists(shotDir))
            {
                var shots = Directory.GetFiles(shotDir, "ntl_*.png")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Skip(10)
                    .ToList();
                foreach (var s in shots)
                {
                    try { freed += s.Length; s.Delete(); } catch { }
                }
            }
        }
        catch { }

        if (verbose && freed > 0)
            Paths.Log(L("  临时文件清理: {0:F1} MB", freed / 1024.0 / 1024.0));
    }

    private static long DirSize(string dir)
    {
        try { return new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); }
        catch { return 0; }
    }
}
