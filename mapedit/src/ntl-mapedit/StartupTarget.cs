using System.Text.Json;

namespace NtlMapEdit;

/// <summary>
/// 最近打开文件（%LOCALAPPDATA%\DRMapEditor\recent.json）。
/// 读取失败/文件损坏一律当成「没有最近文件」，不能阻止编辑器启动。
/// </summary>
public static class RecentStore
{
    public static string DefaultFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DRMapEditor", "recent.json");

    public static string? LastPath => ReadLast(DefaultFile);

    public static string? ReadLast(string file)
    {
        try
        {
            if (!File.Exists(file)) return null;
            using var fs = File.OpenRead(file);
            using var doc = JsonDocument.Parse(fs);
            if (doc.RootElement.TryGetProperty("last", out var e) && e.ValueKind == JsonValueKind.String)
            {
                var p = e.GetString();
                return string.IsNullOrWhiteSpace(p) ? null : p;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[recent] 读取失败（忽略）: " + ex.Message);
        }
        return null;
    }

    public static void Record(string path) => WriteLast(DefaultFile, path);

    public static bool WriteLast(string file, string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(file));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(new
            {
                last = Path.GetFullPath(path),
                updated = DateTimeOffset.Now.ToString("o"),
            });
            File.WriteAllText(file, json);
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[recent] 写入失败（忽略）: " + ex.Message);
            return false;
        }
    }
}

/// <summary>
/// 启动默认打开目标：最近文件优先（可能是 baseline，此时由 UI 标只读），
/// 否则 &lt;gameRoot&gt;\chapter1_windows\data.win（live）。
/// 不再回退到 backup——那是 A2 污染案的入口。
/// </summary>
public static class StartupTarget
{
    public const string DefaultLiveRelative = @"chapter1_windows\data.win";

    public static string? Resolve(string? gameRoot, string? recentPath)
    {
        if (!string.IsNullOrWhiteSpace(recentPath) && File.Exists(recentPath))
            return Path.GetFullPath(recentPath);
        if (string.IsNullOrWhiteSpace(gameRoot)) return null;
        try
        {
            var live = Path.Combine(gameRoot, DefaultLiveRelative);
            return File.Exists(live) ? Path.GetFullPath(live) : null;
        }
        catch { return null; }
    }

    public static (string? Path, FileKind Role) ResolveWithRole(string? gameRoot, string? recentPath)
    {
        var p = Resolve(gameRoot, recentPath);
        return (p, p == null ? FileKind.Other : FileRole.Classify(p, gameRoot));
    }
}
