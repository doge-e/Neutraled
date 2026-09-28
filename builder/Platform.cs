using System.Runtime.InteropServices;

namespace Neutraled.Builder;

/// <summary>平台探测与跨平台兼容层（Windows / Linux / macOS 三平台共用）。
/// 约定：**任何平台差异都必须从这里走**，业务代码不许直接写 OperatingSystem.IsWindows()。
/// Windows 行为与改造前逐字一致（后缀 windows、硬链接可用），因此三套件基线不受影响。</summary>
public static class Platform
{
    public static bool IsWindows => OperatingSystem.IsWindows();
    public static bool IsMacOs => OperatingSystem.IsMacOS();
    public static bool IsLinux => OperatingSystem.IsLinux();

    /// <summary>平台短名：windows / macos / linux。</summary>
    public static string Name => IsWindows ? "windows" : IsMacOs ? "macos" : "linux";

    public static string ExeSuffix => IsWindows ? ".exe" : "";

    private static readonly Dictionary<string, string> SuffixCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>章节目录后缀。判定顺序：
    ///   ① 磁盘上真实存在哪个（chapter1_windows / chapter1_linux / chapter1_unix / chapter1_macos）
    ///   ② 都没有 → 按当前操作系统推断（Windows=windows，macOS=macos，其它=linux）
    /// 这样同一份 Neutraled 目录搬到 Linux 的 DELTARUNE 上也能找对目录。</summary>
    public static string ChapterSuffix(string gameRoot)
    {
        lock (SuffixCache)
        {
            if (SuffixCache.TryGetValue(gameRoot, out var hit)) return hit;
            string found = "";
            foreach (var s in new[] { "windows", "linux", "unix", "macos" })
            {
                if (Directory.Exists(Path.Combine(gameRoot, "chapter1_" + s))) { found = s; break; }
            }
            if (found.Length == 0) found = IsWindows ? "windows" : IsMacOs ? "macos" : "linux";
            SuffixCache[gameRoot] = found;
            return found;
        }
    }

    /// <summary>章节目录名（root 返回空串，调用方自行回退到游戏根）。</summary>
    public static string ChapterDirName(string gameRoot, string chapter) =>
        chapter.Equals("root", StringComparison.OrdinalIgnoreCase) ? "" : chapter + "_" + ChapterSuffix(gameRoot);

    // ---------------------------------------------------------------- 硬链接

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLinkW(string lpFileName, string lpExistingFileName, IntPtr reserved);

    [DllImport("libc", SetLastError = true)]
    private static extern int link(string oldpath, string newpath);

    /// <summary>尝试创建硬链接；任何平台失败都返回 false（调用方必须回退为复制）。
    /// 语义：linkPath 指向 targetPath。</summary>
    public static bool TryHardLink(string linkPath, string targetPath)
    {
        try
        {
            if (IsWindows) return CreateHardLinkW(linkPath, targetPath, IntPtr.Zero);
            return link(targetPath, linkPath) == 0;
        }
        catch { return false; }
    }

    /// <summary>硬链接优先、失败自动回退复制（跨平台缓存/产物落地的统一入口）。</summary>
    public static bool LinkOrCopy(string linkPath, string targetPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(linkPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            if (File.Exists(linkPath)) return true;
            if (TryHardLink(linkPath, targetPath)) return true;
            File.Copy(targetPath, linkPath, overwrite: true);
            return false;   // false = 走了复制（无硬链接）
        }
        catch { return false; }
    }

    // ---------------------------------------------------------------- 自检

    /// <summary>平台自检报告（--platform-info）。每行“项目: 值”，供 CI 与用户排障。</summary>
    public static string Describe(string gameRoot)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"OS: {RuntimeInformation.OSDescription}");
        sb.AppendLine($"Arch: {RuntimeInformation.OSArchitecture} / {RuntimeInformation.ProcessArchitecture}");
        sb.AppendLine($"Runtime: {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"Platform: {Name}");
        sb.AppendLine($"Chapter suffix: {ChapterSuffix(gameRoot)}");
        sb.AppendLine($"Game root: {gameRoot}");
        bool writable;
        try { var probe = Path.Combine(Paths.NeutraledRoot(gameRoot), ".write-probe"); Directory.CreateDirectory(Paths.NeutraledRoot(gameRoot)); File.WriteAllText(probe, "ok"); File.Delete(probe); writable = true; }
        catch { writable = false; }
        sb.AppendLine($"Game root writable: {writable}");
        sb.AppendLine($"Hardlink: {(HardlinkWorks(Paths.NeutraledRoot(gameRoot)) ? "yes" : "no")}");
        sb.AppendLine($"curl: {(Which("curl.exe") ?? Which("curl") ?? "(none)")}");
        return sb.ToString();
    }

    /// <summary>硬链接可用性探测（探测完立刻删掉，不在目录里留垃圾）。
    /// ⚠ 必须在**同一个卷**里放源与链接：早期版本拿 Path.GetTempPath()（通常在 C:）去链接本体（E:），
    /// 跨卷必然失败，于是 --platform-info 永远报「Hardlink: no」，与缓存实际行为不符。</summary>
    private static bool HardlinkWorks(string? dir = null)
    {
        var d = dir;
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d))
            d = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? Path.GetTempPath();
        var src = Path.Combine(d, "ntl-hl-src-" + Guid.NewGuid().ToString("N") + ".tmp");
        var dst = Path.Combine(d, "ntl-hl-dst-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllBytes(src, new byte[] { 0x4E, 0x54, 0x4C });   // "NTL"
            if (TryHardLink(dst, src)) return true;
        }
        catch { }
        finally
        {
            try { if (File.Exists(src)) File.Delete(src); } catch { }
            try { if (File.Exists(dst)) File.Delete(dst); } catch { }
        }
        return false;
    }

    /// <summary>PATH 查找可执行文件（跨平台；Windows 会补 .exe）。</summary>
    public static string? Which(string exe)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var full = Path.Combine(dir.Trim().Trim('"'), exe);
                if (File.Exists(full)) return full;
                if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && IsWindows && File.Exists(full + ".exe")) return full + ".exe";
            }
            catch { }
        }
        return null;
    }
}
