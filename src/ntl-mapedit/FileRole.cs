using System.Text.RegularExpressions;

namespace NtlMapEdit;

/// <summary>文件相对 DELTARUNE 游戏根的角色。</summary>
public enum FileKind
{
    /// <summary>游戏根之外的文件（普通工作副本、mod 的 ref/data.win 等）。</summary>
    Other = 0,
    /// <summary>原版基线：&lt;gameRoot&gt;\backup\ 或 &lt;gameRoot&gt;\Neutraled\backup\。</summary>
    Baseline = 1,
    /// <summary>游戏根 data.win（root 部署目标）。</summary>
    LiveRoot = 2,
    /// <summary>&lt;gameRoot&gt;\chapterN_windows\data.win（章节部署目标）。</summary>
    LiveChapter = 3,
}

/// <summary>写盘结果；失败时 Error 是可直接展示给用户的原因。</summary>
public readonly record struct SaveResult(bool Ok, string? Error, string? Path)
{
    public static SaveResult Success(string path) => new(true, null, path);
    public static SaveResult Refused(string path, string error) => new(false, error, path);
}

/// <summary>
/// 文件角色判定。反斜杠/大小写/末尾分隔符统一处理；分类只看路径，不要求文件存在
/// （启动参数、另存为目标、审计工具都可能先判角色再决定是否触碰磁盘）。
/// </summary>
public static class FileRole
{
    public const string BaselineBadge = "原版基线 · 只读";
    public const string LiveBadge = "游戏文件 · 可保存";
    public const string OtherBadge = "其它文件 · 可保存";

    private static readonly Regex ChapterLive = new(
        @"^chapter\d+_windows[\\/]data\.win$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>与 EditorForm / Program 相同的游戏根探测顺序。</summary>
    public static string? FindGameRoot()
    {
        var candidates = new List<string>();
        var env = Environment.GetEnvironmentVariable("DELTARUNE_DIR");
        if (!string.IsNullOrWhiteSpace(env)) candidates.Add(env!);
        candidates.Add(@"E:\steam\steamapps\common\DELTARUNE");
        candidates.Add(@"C:\Program Files (x86)\Steam\steamapps\common\DELTARUNE");
        candidates.Add(@"D:\SteamLibrary\steamapps\common\DELTARUNE");
        foreach (var c in candidates)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(c) && File.Exists(Path.Combine(c, "DELTARUNE.exe")))
                    return Path.GetFullPath(c);
            }
            catch { /* 无效路径继续找 */ }
        }
        return null;
    }

    public static FileKind Classify(string path, string? gameRoot)
    {
        if (string.IsNullOrWhiteSpace(path)) return FileKind.Other;
        string full;
        try { full = Path.GetFullPath(path); } catch { return FileKind.Other; }
        var root = NormalizeDir(gameRoot);
        if (root != null)
        {
            if (IsUnder(full, Path.Combine(root, "backup")) ||
                IsUnder(full, Path.Combine(root, "Neutraled", "backup")))
                return FileKind.Baseline;
            if (PathEquals(full, Path.Combine(root, "data.win"))) return FileKind.LiveRoot;
            var rel = RelativeTo(full, root);
            if (rel != null && ChapterLive.IsMatch(rel)) return FileKind.LiveChapter;
            if (IsUnder(full, root)) return FileKind.Other;
        }
        // 游戏根未知（或文件在游戏根之外）时仍按目录结构拦截 backup：
        // 否则「没找到游戏目录」就成了覆盖原版基线的绕过口（A2 污染链的根因之一）。
        if (HasDirectorySegment(full, "backup")) return FileKind.Baseline;
        return FileKind.Other;
    }

    public static bool IsLive(FileKind kind) => kind is FileKind.LiveRoot or FileKind.LiveChapter;

    public static string Badge(FileKind kind) => kind switch
    {
        FileKind.Baseline => BaselineBadge,
        FileKind.LiveRoot or FileKind.LiveChapter => LiveBadge,
        _ => OtherBadge,
    };

    /// <summary>拒绝写原版基线时的统一错误文本。</summary>
    public static string BaselineError(string path) =>
        "原版基线 · 只读：拒绝写入 " + path +
        "（未生成 .bak、未写 sidecar、原文件未改动）。请用「另存为」保存到 backup 之外，或从「文件 → 打开游戏章节 → 游戏文件（可保存）」打开 live data.win。";

    private static string? NormalizeDir(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return null;
        try { return TrimDir(Path.GetFullPath(dir)); }
        catch { return null; }
    }

    private static string TrimDir(string path) => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool PathEquals(string a, string b) =>
        string.Equals(TrimDir(a), TrimDir(b), StringComparison.OrdinalIgnoreCase);

    private static bool IsUnder(string full, string dir)
    {
        var d = TrimDir(Path.GetFullPath(dir));
        return PathEquals(full, d) ||
               full.StartsWith(d + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               full.StartsWith(d + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string? RelativeTo(string full, string root)
    {
        if (!IsUnder(full, root)) return null;
        var rel = Path.GetRelativePath(root, full);
        return rel == "." ? null : rel;
    }

    private static bool HasDirectorySegment(string full, string name)
    {
        var parts = full.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);
        return parts.Any(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
    }
}
