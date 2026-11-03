using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace NtlMapEdit;

/// <summary>
/// 启动便利设置：记住「默认打开哪个章节」，并决定双击/无参数启动时打开哪个 data.win。
/// 设置文件放在 %LOCALAPPDATA%\DRMapEditor\settings.json，与最近文件记录同一个目录（不污染游戏目录）。
/// 解析顺序：显式设置的章节 → 最近打开的文件 → chapter1（再找不到就依次试其它章节 / 原版基线）。
/// </summary>
public sealed class StartupSettings
{
    public const int MinChapter = 1;
    public const int MaxChapter = 5;
    public const string AutoChapter = "auto";

    /// <summary>auto 或 chapter1..chapter5。</summary>
    public string DefaultChapter { get; set; } = AutoChapter;

    /// <summary>左侧房间列表按「剧情线性流程」排序（支线紧跟主线、测试/旧版归到底部分组）。</summary>
    public bool SortRoomsByFlow { get; set; } = true;

    /// <summary>保存文档时自动重新生成 Neutraled mod（对话/战斗/剧情/文本改写都不用手动点生成）。</summary>
    public bool AutoGenerateMod { get; set; } = true;

    public static string DefaultFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DRMapEditor", "settings.json");

    private static readonly JsonDocumentOptions JsonOpts = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public static StartupSettings Load(string? file = null)
    {
        file ??= DefaultFile;
        var s = new StartupSettings();
        try
        {
            if (File.Exists(file))
            {
                using var doc = JsonDocument.Parse(File.ReadAllBytes(file), JsonOpts);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    if (doc.RootElement.TryGetProperty("defaultChapter", out var v) && v.ValueKind == JsonValueKind.String)
                        s.DefaultChapter = NormalizeChapter(v.GetString());
                    if (doc.RootElement.TryGetProperty("sortRoomsByFlow", out var sf)
                        && (sf.ValueKind == JsonValueKind.True || sf.ValueKind == JsonValueKind.False))
                        s.SortRoomsByFlow = sf.GetBoolean();
                    if (doc.RootElement.TryGetProperty("autoGenerateMod", out var ag)
                        && (ag.ValueKind == JsonValueKind.True || ag.ValueKind == JsonValueKind.False))
                        s.AutoGenerateMod = ag.GetBoolean();
                }
            }
        }
        catch
        {
            // 坏设置文件按默认处理；保存时会整体重写（另有 .bad 备份）。
        }
        s.DefaultChapter = NormalizeChapter(s.DefaultChapter);
        return s;
    }

    /// <summary>保存；坏文件先备份成 settings.json.bad-时间戳，再原子替换。</summary>
    public void Save(string? file = null)
    {
        file ??= DefaultFile;
        DefaultChapter = NormalizeChapter(DefaultChapter);
        var dir = Path.GetDirectoryName(file);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        if (File.Exists(file) && !LooksLikeJson(file))
        {
            try { File.Move(file, file + ".bad-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true); } catch { }
        }
        string json = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["defaultChapter"] = DefaultChapter,
            ["sortRoomsByFlow"] = SortRoomsByFlow,
            ["autoGenerateMod"] = AutoGenerateMod,
            ["updated"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
        }, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }) + Environment.NewLine;
        string tmp = file + ".tmp";
        File.WriteAllText(tmp, json, new UTF8Encoding(false));
        File.Move(tmp, file, true);
    }

    private static bool LooksLikeJson(string file)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(file), JsonOpts);
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch { return false; }
    }

    public static string NormalizeChapter(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return AutoChapter;
        var t = raw.Trim().ToLowerInvariant();
        if (t == AutoChapter || t == "default" || t == "last") return AutoChapter;
        if (t.StartsWith("chapter", StringComparison.Ordinal)) t = t["chapter".Length..];
        if (t.StartsWith('_')) t = t[1..];
        return int.TryParse(t, out int n) && n >= MinChapter && n <= MaxChapter ? "chapter" + n : AutoChapter;
    }

    public static int ChapterNumber(string? normalized)
    {
        var t = NormalizeChapter(normalized);
        return t == AutoChapter ? 0 : int.Parse(t["chapter".Length..]);
    }

    public static string Describe(string? normalized)
    {
        int n = ChapterNumber(normalized);
        return n == 0 ? "自动（最近打开，其次 chapter1）" : "Chapter " + n;
    }

    public static string LivePath(string gameRoot, int chapter) =>
        Path.Combine(gameRoot, "chapter" + chapter + "_windows", "data.win");

    public static string BaselinePath(string gameRoot, int chapter) =>
        Path.Combine(gameRoot, "backup", "chapter" + chapter + "_windows", "data.win");

    public sealed record TargetInfo(string Path, bool FromDefault, string Source, int Chapter, bool IsBaseline);

    /// <summary>
    /// 解析启动目标。不修改任何文件；只在磁盘上查找。
    /// </summary>
    public static TargetInfo ResolveTarget(string? gameRoot, string? recentPath, string? settingsFile = null)
    {
        var settings = Load(settingsFile);
        int preferred = ChapterNumber(settings.DefaultChapter);
        var notes = new List<string>();
        if (preferred > 0)
        {
            var p = LivePath(gameRoot ?? "", preferred);
            if (File.Exists(p)) return new TargetInfo(p, true, "默认章节 Chapter " + preferred, preferred, false);
            var b = BaselinePath(gameRoot ?? "", preferred);
            if (File.Exists(b)) return new TargetInfo(b, true, "默认章节 Chapter " + preferred + "（原版基线，只读）", preferred, true);
            notes.Add("Chapter " + preferred + " 的数据文件不存在");
        }
        if (!string.IsNullOrWhiteSpace(recentPath) && File.Exists(recentPath))
        {
            int rc = GuessChapter(recentPath!);
            return new TargetInfo(recentPath!, false, "最近打开" + (notes.Count > 0 ? "（" + string.Join("；", notes) + "）" : ""), rc, IsBaselinePath(recentPath!));
        }
        var order = Enumerable.Range(MinChapter, MaxChapter).OrderBy(n => n == 1 ? 0 : n).ToList();
        foreach (int n in order)
        {
            var p = LivePath(gameRoot ?? "", n);
            if (File.Exists(p)) return new TargetInfo(p, true, "自动：Chapter " + n + "（默认）", n, false);
            var b = BaselinePath(gameRoot ?? "", n);
            if (File.Exists(b)) return new TargetInfo(b, true, "自动：Chapter " + n + "（原版基线，只读）", n, true);
        }
        var fallback = LivePath(gameRoot ?? "", MinChapter);
        return new TargetInfo(fallback, true, "未找到任何章节数据（回退 Chapter 1，启动后会提示打开文件）", MinChapter, false);
    }

    public static int GuessChapter(string path)
    {
        var m = System.Text.RegularExpressions.Regex.Match(path ?? "", @"chapter(\d+)_windows", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out int n) && n >= MinChapter && n <= MaxChapter) return n;
        return 0;
    }

    public static bool IsBaselinePath(string path) =>
        (path ?? "").Replace('/', '\\').Contains(@"\backup\", StringComparison.OrdinalIgnoreCase);
}
