using System.Text.Json;

namespace Neutraled.Builder;

/// <summary>路径探测与通用工具（无硬编码路径，发布友好）。</summary>
public static class Paths
{
    /// <summary>从当前 exe 向上寻找游戏根（含 DELTARUNE.exe 与章节目录 chapter1_&lt;平台后缀&gt;）。
    /// 跨平台：后缀可能是 windows / linux / unix / macos，四种都认。</summary>
    public static string DetectGameRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 12 && dir != null; i++)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DELTARUNE.exe")) && HasChapterDir(dir.FullName))
                return dir.FullName;
            dir = dir.Parent;
        }
        // 回退：exe 上溯找 Neutraled 的父目录
        dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 12 && dir != null; i++)
        {
            if (string.Equals(dir.Name, "Neutraled", StringComparison.OrdinalIgnoreCase))
                return dir.Parent?.FullName ?? dir.FullName;
            dir = dir.Parent;
        }
        return AppContext.BaseDirectory;
    }

    /// <summary>Neutraled 根目录（游戏根下的 Neutraled/）。</summary>
    public static string ModsRoot(string gameRoot) => Path.Combine(NeutraledRoot(gameRoot), "mods");

    public static string DistRoot(string gameRoot) => Path.Combine(NeutraledRoot(gameRoot), "dist");

    public static string NeutraledRoot(string gameRoot) => Path.Combine(gameRoot, "Neutraled");

    /// <summary>配置档目录（Neutraled/profiles）。</summary>
    public static string ProfilesRoot(string gameRoot) => Path.Combine(NeutraledRoot(gameRoot), "profiles");

    /// <summary>每 mod 版本快照目录（Neutraled/snapshots/&lt;modId&gt;/&lt;version&gt;）。</summary>
    public static string SnapshotsRoot(string gameRoot) => Path.Combine(NeutraledRoot(gameRoot), "snapshots");

    /// <summary>整游戏恢复点目录（Neutraled/restore）。</summary>
    public static string RestoreRoot(string gameRoot) => Path.Combine(NeutraledRoot(gameRoot), "restore");

    /// <summary>插件目录（Neutraled/plugins）。</summary>
    public static string PluginsRoot(string gameRoot) => Path.Combine(NeutraledRoot(gameRoot), "plugins");

    /// <summary>主题目录（Neutraled/themes）。</summary>
    public static string ThemesRoot(string gameRoot) => Path.Combine(NeutraledRoot(gameRoot), "themes");

    /// <summary>外部语言包目录（Neutraled/lang）。</summary>
    public static string LangRoot(string gameRoot) => Path.Combine(NeutraledRoot(gameRoot), "lang");

    /// <summary>下载队列临时目录（Neutraled/dl）。</summary>
    public static string DownloadsRoot(string gameRoot) => Path.Combine(NeutraledRoot(gameRoot), "dl");

    /// <summary>Neutraled/config.json 的路径（所有设置读写在 ConfigFile）。</summary>
    public static string ConfigPath(string gameRoot) => Path.Combine(NeutraledRoot(gameRoot), "config.json");

    /// <summary>存档区镜像路径：%LOCALAPPDATA%\DELTARUNE\Neutraled\config.json（游戏没建过存档区时返回 null）。
    /// ★ 为什么需要（2026-09-27 真机探针取证）：root 产物（游戏自带启动器 data.win）带 GameMaker 文件沙箱 ——
    ///   **写** bundle 路径会落到存档区 <game_save_id>；**读** bundle 路径时，只要存档区存在同名文件就被它遮蔽
    ///   （逐文件遮蔽：同一次启动读 Neutraled/mods/ 仍是真实 bundle，因为存档区没有 mods 目录）。
    ///   章节产物（chapterN_windows/data.win）没有沙箱，直接用游戏根那份 ⇒ 同一份配置在两个阶段会读到两套内容
    ///   （语言/跳过开关在章节选择器与章节内不一致）。
    ///   游戏侧已经"两份都读、两份都写"（api/ntl_config_paths.gml / ntl_config_load.gml）；这份镜像让
    ///   CLI/GUI 的改动**立刻**对 root 阶段（章节选择器）可见，不必等章节进程下一次镜像。
    ///   只有 DELTARUNE 存档区已存在时才返回路径，避免给其它 GameMaker 游戏造目录。</summary>
    public static string? SaveMirrorConfigPath()
    {
        try
        {
            var local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (string.IsNullOrEmpty(local)) return null;
            var save = Path.Combine(local, "DELTARUNE");
            if (!Directory.Exists(save)) return null;
            return Path.Combine(save, "Neutraled", "config.json");
        }
        catch { return null; }
    }

    /// <summary>当前生效平台的章节目录后缀（windows/linux/unix/macos 自动探测，见 Platform.cs）。</summary>
    public static string ChapterSuffix(string gameRoot) => Platform.ChapterSuffix(gameRoot);

    /// <summary>某目录是否像游戏根（存在 chapter1_&lt;任一平台后缀&gt;）—— DetectGameRoot 用。</summary>
    public static bool HasChapterDir(string dir) =>
        new[] { "windows", "linux", "unix", "macos" }.Any(s => Directory.Exists(Path.Combine(dir, "chapter1_" + s)));
    /// <summary>顶层 data.win（章节选择器）。</summary>
    public static string RootDataWin(string gameRoot) => Path.Combine(gameRoot, "data.win");

    /// <summary>Neutraled API 版本（用于缓存签名）。</summary>
    public static string ApiVersion() => "1.0.6";

    public static string ChapterDir(string gameRoot, string chapter) =>
        chapter.Equals("root", StringComparison.OrdinalIgnoreCase)
            ? gameRoot
            : Path.Combine(gameRoot, chapter + "_" + ChapterSuffix(gameRoot));

    public static string ChapterDataWin(string gameRoot, string chapter) =>
        Path.Combine(ChapterDir(gameRoot, chapter), "data.win");

    /// <summary>官方原版基线路径。**两处都找**：
    ///   ① &lt;游戏根&gt;\backup\...（工具链的规范位置）
    ///   ② &lt;Neutraled&gt;\backup\...（--install 写的位置）
    /// 实测教训（2026-09-25）：重装 Steam 会清掉 ①，而项目搬走再放回只恢复 ②，
    /// 于是"基线消失"、所有导入/差异工具全线报错。这里做回退，任一处存在即可用。</summary>
    public static string BackupDataWin(string gameRoot, string chapter)
    {
        var rel = chapter.Equals("root", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine("backup", "data.win")
            : Path.Combine("backup", chapter + "_" + ChapterSuffix(gameRoot), "data.win");
        var primary = Path.Combine(gameRoot, rel);
        if (File.Exists(primary)) return primary;
        var fallback = Path.Combine(NeutraledRoot(gameRoot), rel);
        return File.Exists(fallback) ? fallback : primary;
    }

    /// <summary>写 JSON 一律**不要** Unicode 转义。
    /// 实测：GameMaker 的 json_parse 吃不下 \uXXXX 转义 —— 只要 chapters.json 里出现一个
    /// 中文（C# 默认编码器会写成 \u7531……），整个注册表解析失败，章节选择器直接失效。
    /// 所以统一用 UnsafeRelaxedJsonEscaping，让非 ASCII 按 UTF-8 原样落盘（GML 读得懂）。</summary>
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static void Log(string msg) => Console.WriteLine(msg);

    /// <summary>并发安全的写文件。
    /// 背景：6 个章节 worker 并行部署时会同时写游戏根 Neutraled/ 下的共享文件
    /// （api-registry.json / ns-registry.json / hook-registry.json / chapters.json / 扫描缓存 / 缓存索引），
    /// 而 File.WriteAllText 默认以 FileShare.Read 打开目标文件 ⇒ 两个进程同时写同一文件时
    /// 后者抛 IOException（共享冲突）。
    /// 实测（PowerShell 6 进程 × 400 次写同一文件，2026-09-26）：
    ///   裸 WriteAllText → 1403/2400 次失败；临时文件 + File.Replace/Move → 2399/2400 次失败
    ///   （ReplaceFile 要求目标无人打开，竞争窗口更小 ⇒ 更差）；WriteAllText + 短退避重试 → 0/2400 次失败。
    /// 这里只对 IO / 权限类异常重试（最多 6 次，退避 40→200ms），其它异常与非最后一次失败照旧抛出。</summary>
    public static void SafeWrite(string path, string contents)
    {
        const int attempts = 6;
        for (int i = 0; ; i++)
        {
            try
            {
                File.WriteAllText(path, contents);
                return;
            }
            catch (Exception ex) when (i < attempts - 1 && (ex is IOException || ex is UnauthorizedAccessException))
            {
                System.Threading.Thread.Sleep(40 * (i + 1));
            }
        }
    }
}
