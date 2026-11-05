using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Encodings.Web;

namespace Neutraled.Studio;

/// <summary>Studio 双语（中/英）。语言唯一真源与 ntl-gui 相同：Neutraled/config.json 的 lang
/// （C4 P1-5；旧 gui_lang.txt 已由 <see cref="MigrateLegacy"/> 一次性并入后归档）。</summary>
public static class Localizer
{
    public static string Current { get; set; } = "zh";

    private static readonly Dictionary<string, string> En = new(StringComparer.Ordinal)
    {
        // ---- 窗体 / 工具栏 ----
        ["Neutraled Studio — DELTARUNE Mod 编辑器"] = "Neutraled Studio — DELTARUNE Mod IDE",
        ["Neutraled Studio — {0}"] = "Neutraled Studio — {0}",
        ["未命名"] = "Untitled",
        ["新建"] = "New",
        ["打开"] = "Open",
        ["保存"] = "Save",
        ["部署当前章节"] = "Deploy Current Chapter",
        ["章节"] = "Chapter",
        ["部署章节"] = "Deploy chapter",
        ["部署章节: {0}"] = "Deploy chapter: {0}",
        ["启动游戏"] = "Launch Game",
        ["热重载 live"] = "Hot Reload live",
        ["补全 (Ctrl+Space)"] = "Complete (Ctrl+Space)",
        ["刷新文件树"] = "Refresh File Tree",
        ["打开 mod 目录"] = "Open mod Folder",
        ["打开 live 目录"] = "Open live Folder",
        ["已触发补全列表"] = "Completion list triggered",
        ["就绪"] = "Ready",
        ["游戏根: {0}"] = "Game root: {0}",
        ["当前文件未保存，仍要退出？"] = "The current file is not saved. Exit anyway?",

        // ---- 自检 ----
        ["===== 自检开始 ====="] = "===== Self-test start =====",
        ["高亮: OK（4 行脚本）"] = "Highlight: OK (4-line script)",
        ["补全: 候选 {0} 项  样例: {1}"] = "Completion: {0} candidate(s)  sample: {1}",
        ["接受补全后首行: {0}"] = "First line after accepting completion: {0}",
        ["===== 自检结束 ====="] = "===== Self-test end =====",
        ["[自检错误] {0}"] = "[Self-test error] {0}",

        // ---- 日志 ----
        ["[游戏] {0}"] = "[Game] {0}",
        ["已打开: {0}"] = "Opened: {0}",
        ["已保存: {0}"] = "Saved: {0}",
        ["===== 部署中（先关闭游戏）====="] = "===== Deploying (close the game first) =====",
        ["部署完成 ✓"] = "Deploy done ✓",
        ["部署失败（退出码 {0}）"] = "Deploy failed (exit code {0})",
        ["已请求热重载（游戏内下帧生效；若未运行请先启动游戏）"] =
            "Hot reload requested (applies on the next frame; launch the game first if it is not running)",
        ["符号表已加载: {0} 项（Ctrl+Space 触发补全）"] = "Symbol table loaded: {0} entries (Ctrl+Space for completion)",
        ["[警告] 符号表加载失败: {0}"] = "[Warning] failed to load symbol table: {0}",
        ["[错误] 找不到 ntl-gui.exe"] = "[Error] ntl-gui.exe not found",
        ["启动游戏失败: {0}"] = "Failed to launch game: {0}",
        ["启动失败（退出码 {0}）；详见日志"] = "Launch failed (exit code {0}); see the log",

        // ---- 文件树 ----
        ["live（运行时脚本）"] = "live (runtime scripts)",
        ["mods（编译期 mod）"] = "mods (compile-time mods)",
        ["docs（文档）"] = "docs (documentation)",
        ["文件树已刷新（live: {0}）"] = "File tree refreshed (live: {0})",
        ["当前文件有未保存修改，是否保存？"] = "The current file has unsaved changes. Save?",
        ["当前文件未保存。\n\n是 = 保存并退出\n否 = 不保存退出\n取消 = 返回编辑器"] =
            "The current file is not saved.\n\nYes = save and exit\nNo = exit without saving\nCancel = return to editor",
        ["目录不存在: {0}"] = "Directory not found: {0}",
        ["无法打开目录: {0}"] = "Failed to open folder: {0}",
        ["[错误] 无法打开目录 {0}: {1}"] = "[Error] Failed to open folder {0}: {1}",

        // ---- 新建模板 / 文件对话框 ----
        ["// 新的 NTL Script\n// 保存到 Neutraled/live/<ModName>/*.ntl 即可被运行时加载\n\nlog(\"hello from NTL Script\");\n"] =
            "// New NTL Script\n// Save it to Neutraled/live/<ModName>/*.ntl and the runtime loads it\n\nlog(\"hello from NTL Script\");\n",
        ["NTL/GML 脚本|*.ntl;*.gml|GML|*.gml|JSON|*.json|所有文件|*.*"] =
            "NTL/GML script|*.ntl;*.gml|GML|*.gml|JSON|*.json|All files|*.*",

        // ---- 编辑器状态栏 ----
        ["行 {0}, 列 {1}  |  字符 {2}"] = "Line {0}, Col {1}  |  Chars {2}",
        ["补全不可用：符号表为空"] = "Completion unavailable: symbol table is empty",
        ["补全候选 {0} 项（前缀 \"{1}\"）"] = "{0} completion candidate(s) (prefix \"{1}\")"
    };

    /// <summary>载入界面语言：唯一真源 = Neutraled/config.json 的 lang（与 ntl-gui、builder、游戏内文字同一个键）。
    /// en → en；zh / auto / 缺省 / 其它 → zh。旧 gui_lang.txt 不再参与解析。</summary>
    public static void Load(string neutraledRoot)
    {
        try
        {
            var cfg = Path.Combine(neutraledRoot, "config.json");
            if (!File.Exists(cfg)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(cfg),
                new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (doc.RootElement.TryGetProperty("lang", out var l) && l.ValueKind == JsonValueKind.String)
            {
                var v = (l.GetString() ?? "").Trim().ToLowerInvariant();
                // en → en；zh / auto / 其它取值 → 一律 zh（与 gui 侧逐字一致，两个编辑器同一个开关）
                Current = (v == "en") ? "en" : "zh";
            }
        }
        catch { }
    }

    /// <summary>一次性迁移旧 gui_lang.txt（C4 P1-5）：config.json 没写 lang 时把旧值并进去，
    /// 随后把旧文件改名 .migrated 归档（不再被任何代码读取）。失败不抛：语言真源始终以 config 为准。</summary>
    public static void MigrateLegacy(string neutraledRoot)
    {
        try
        {
            var f = Path.Combine(neutraledRoot, "gui_lang.txt");
            if (!File.Exists(f)) return;
            var v = File.ReadAllText(f).Trim().ToLowerInvariant();
            var cfg = Path.Combine(neutraledRoot, "config.json");
            var obj = new JsonObject();
            var hasLang = false;
            if (File.Exists(cfg))
            {
                var node = JsonNode.Parse(File.ReadAllText(cfg),
                    documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                if (node is JsonObject o) obj = o;
                hasLang = obj["lang"] is JsonValue lv && lv.TryGetValue<string>(out var s) && s.Trim().Length > 0;
            }
            if (!hasLang && v is "zh" or "en")
            {
                obj["lang"] = v;
                File.WriteAllText(cfg, obj.ToJsonString(new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                }));
            }
            File.Move(f, f + ".migrated", overwrite: true);
        }
        catch { /* 迁移失败不影响启动；语言继续以 config.json 为准 */ }
    }

    /// <summary>把界面语言写进 Neutraled/config.json 的 lang（就地合并：只动 lang 这一个键，
    /// 其它键、键序与 _comment 里的中文原样保留）。序列化沿用 gui 侧既有写法：WriteIndented +
    /// UnsafeRelaxedJsonEscaping（中文原样 UTF-8，不转成 \uXXXX —— 游戏侧 GML 的 json_parse 吃不下转义序列）。
    /// 失败不抛：文件保持原样，界面上只是语言没落到 config。</summary>
    public static void SaveConfigLang(string neutraledRoot)
    {
        try
        {
            var path = Path.Combine(neutraledRoot, "config.json");
            var obj = new JsonObject();
            if (File.Exists(path))
                obj = JsonNode.Parse(File.ReadAllText(path), null,
                    new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip })
                    as JsonObject ?? new JsonObject();
            obj["lang"] = Current;
            File.WriteAllText(path, obj.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }));
        }
        catch { }
    }

    public static string T(string zh, params object[] args)
    {
        var s = zh;
        if (Current == "en" && En.TryGetValue(zh, out var en)) s = en;
        return args.Length > 0 ? string.Format(s, args) : s;
    }
}
