using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Neutraled.Gui;

/// <summary>
/// GUI 与 headless 共用的判定逻辑（C4 P1-12）：这里不创建窗口、不碰 WinForms 控件，
/// 所以 <c>ntl-gui --headless ...</c> 能复用同一套规则做自检（告警偏移 / 冲突 ack / 章节解析 / argv）。
/// </summary>
public static class UiLogic
{
    public enum ActionKind
    {
        Deploy, Launch, Restore, ImportMod, Packs, Conflicts, LayerFromBase, Fetch, ReadOnly, Other
    }

    /// <summary>哪些动作允许目标 = all（全部章节）。其余动作必须选具体章节（A3 F01：不允许静默当第 4 章）。</summary>
    public static bool AllowsAll(ActionKind action) =>
        action is ActionKind.Deploy or ActionKind.Launch or ActionKind.Restore;

    /// <summary>下拉留空时的默认目标：写命令默认 root（与 builder 一致），只读命令默认 chapter1。</summary>
    public static string DefaultChapter(ActionKind action) =>
        action is ActionKind.Deploy or ActionKind.Launch or ActionKind.Restore ? "root" : "chapter1";

    public static string ChapterAllowed(string? selection, ActionKind action)
    {
        var ch = (selection ?? "").Trim();
        if (ch.Length == 0) return DefaultChapter(action);
        if (!AllowsAll(action) && ch.Equals("all", StringComparison.OrdinalIgnoreCase)) return "";
        return ch;
    }

    public sealed record Plan(bool Ok, string Chapter, string Args, string? Error);

    /// <summary>把下拉选择 + 动作解析成「章节 + 完整 argv」。all 用在只支持单章的动作上时返回错误（不静默替换）。</summary>
    public static Plan PlanChapter(string? selection, ActionKind action,
        string? input = null, string? name = null, string? author = null,
        string? outPath = null, string? winPath = null)
    {
        var ch = ChapterAllowed(selection, action);
        if (ch.Length == 0)
            return new Plan(false, "", "", Localizer.T("{0}需要选择具体章节（不能选 all）。", ActionLabel(action)));
        return new Plan(true, ch, BuildArgs(action, ch, input, name, author, outPath, winPath), null);
    }

    public static string ActionLabel(ActionKind action) => action switch
    {
        ActionKind.Deploy => Localizer.T("部署"),
        ActionKind.Launch => Localizer.T("启动"),
        ActionKind.Restore => Localizer.T("还原"),
        ActionKind.ImportMod => Localizer.T("导入"),
        ActionKind.Packs => Localizer.T("导出资源包"),
        ActionKind.Conflicts => Localizer.T("检查 mod 冲突"),
        ActionKind.LayerFromBase => Localizer.T("提取差异层"),
        ActionKind.Fetch => Localizer.T("在线下载"),
        _ => Localizer.T("该功能"),
    };

    public static string BuildArgs(ActionKind action, string chapter,
        string? input = null, string? name = null, string? author = null,
        string? outPath = null, string? winPath = null) => action switch
    {
        // UX-04：GUI/headless 的「部署」必须和「部署并启动」走同一条 --launch 链路（缓存 + 部署会话 + 存档联接自愈），
        // 只差最后不拉起 Steam。旧写法 --deploy --chapter 不写 deploy-session.json，玩家点【恢复原版】时检测不到会话。
        ActionKind.Deploy => $"--launch {chapter} --no-start",
        ActionKind.Launch => $"--launch {chapter}",
        ActionKind.Restore => "--restore-session",
        ActionKind.ImportMod => $"--import-mod \"{input}\" --chapter {chapter} --name \"{name}\" --author \"{author}\"",
        ActionKind.Packs => $"--export-packs \"{outPath}\" --chapter {chapter}",
        ActionKind.Conflicts => $"--conflicts --chapter {chapter}",
        ActionKind.LayerFromBase => $"--layer-from-base \"{winPath}\" --chapter {chapter} --name \"{name}\" --author \"{author}\"",
        ActionKind.Fetch => $"--fetch {input} --chapter {chapter}",
        ActionKind.ReadOnly => $"--deploy-state --chapter {chapter}",
        _ => "",
    };

    /// <summary>退出码 → 用户可见文案（退出码驱动文案，而不是一律「失败」）。0 = 成功返回 null。</summary>
    public static string? MessageForExit(int rc, ActionKind action) => rc switch
    {
        0 => null,
        2 => Localizer.T("发现冲突：详见日志与 conflicts.json"),
        3 => Localizer.T("原版基线校验未通过：先运行 --verify-backup 查看，或运行 --repair-backup --yes 修复"),
        _ => Localizer.T("失败（退出码 {0}），详见日志", rc),
    };

    // ==================== 游戏日志告警（增量真值：记偏移，不重复报旧错）====================

    public static string AlertsStatePath(string neutraledRoot) =>
        Path.Combine(neutraledRoot, "gui-alerts.json");

    public static string DefaultGameLogPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DELTARUNE", "Neutraled", "dr-api.log");

    /// <summary>
    /// 返回「自上次调用以来新增的错误行」。首次运行没有状态文件 ⇒ 只记基线、返回空（不把历史错误当新告警）。
    /// 日志被清空/轮转（当前长度 &lt; 记录的偏移）⇒ 从 0 重新读并当作新增。
    /// </summary>
    public static List<string> NewAlerts(string logPath, string statePath, int max = 200)
    {
        var list = new List<string>();
        try
        {
            if (!File.Exists(logPath)) return list;
            long len = new FileInfo(logPath).Length;
            long offset = -1;
            try
            {
                if (File.Exists(statePath) &&
                    JsonNode.Parse(File.ReadAllText(statePath)) is JsonObject obj)
                {
                    var savedPath = obj["path"]?.GetValue<string>();
                    var savedLen = obj["length"]?.GetValue<long>() ?? -1;
                    // 换了日志文件 ⇒ 重新取基线，避免把别的日志当成同一条流
                    if (savedPath != null && string.Equals(Path.GetFullPath(savedPath), Path.GetFullPath(logPath), StringComparison.OrdinalIgnoreCase))
                        offset = savedLen;
                }
            }
            catch { offset = -1; }

            if (offset < 0 || offset > len)   // 首次运行 / 日志被清空：静默建立基线
            {
                WriteAlertsState(statePath, logPath, len);
                return list;
            }
            if (len == offset) return list;

            using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            fs.Seek(offset, SeekOrigin.Begin);
            using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (IsErrorLine(line) && list.Count < max) list.Add(line.Trim());
            }
            WriteAlertsState(statePath, logPath, len);
        }
        catch { }
        return list;
    }

    public static bool IsErrorLine(string line) =>
        line.Contains("ERROR", StringComparison.Ordinal)
        || line.Contains("[错误]", StringComparison.Ordinal)
        || line.Contains("Exception", StringComparison.Ordinal);

    private static void WriteAlertsState(string statePath, string logPath, long len)
    {
        try
        {
            var dir = Path.GetDirectoryName(statePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var obj = new JsonObject { ["path"] = logPath, ["length"] = len };
            File.WriteAllText(statePath, obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    // ==================== 冲突报告 ack（内容哈希：没变就不再弹）====================

    public static string? ConflictsDigest(string conflictsPath)
    {
        try
        {
            if (!File.Exists(conflictsPath)) return null;
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(conflictsPath))).ToLowerInvariant();
        }
        catch { return null; }
    }

    public static bool ConflictsIsNew(string conflictsPath, string ackPath)
    {
        var digest = ConflictsDigest(conflictsPath);
        if (digest == null) return true;   // 没报告文件：交给调用方按退出码提示
        try
        {
            var ack = File.Exists(ackPath) ? File.ReadAllText(ackPath).Trim() : "";
            return !string.Equals(ack, digest, StringComparison.OrdinalIgnoreCase);
        }
        catch { return true; }
    }

    public static void AckConflicts(string conflictsPath, string ackPath)
    {
        try
        {
            var digest = ConflictsDigest(conflictsPath) ?? "";
            var dir = Path.GetDirectoryName(ackPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(ackPath, digest);
        }
        catch { }
    }
}
