using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>
/// C4 P0-3：部署会话（Neutraled\deploy-session.json）。
///
/// 解决的问题（A3 审计 F09）：GUI 在「部署并启动」成功后**无条件**起一个后台轮询，
/// 游戏退出 2 秒后自动执行 --restore-chapter &lt;chapter&gt; ——
///   ① 没有开关，想保留部署结果的玩家被强制还原；
///   ② 只还原一个章节，--launch 部署的 root 产物留在 live（下次 Steam 启动会跑「已部署的章节选择器」）；
///   ③ 多开/连点会重复轮询，进程退出后互相抢写。
///
/// 现在的语义：
///   --launch 在真正拉起 Steam 之前，把「这次写了哪些目标」记进会话文件；
///   还原是**显式动作**（默认不自动）：--restore-session 按会话把目标一起还原；
///   GUI 只有在 config.json 的 restore_on_exit=true 时才在游戏退出后调用 --restore-session。
/// </summary>
public static class DeploySession
{
    public const string FileName = "deploy-session.json";

    public sealed class Session
    {
        public int Version { get; set; } = 1;
        public string Id { get; set; } = "";
        public string StartedAtUtc { get; set; } = "";
        /// <summary>--launch 的目标（root / chapterN / all）。</summary>
        public string Launch { get; set; } = "";
        /// <summary>这次部署实际写进 live 的目标列表（root + chapterN 或全部 6 个）。</summary>
        public List<string> Chapters { get; set; } = new();
    }

    public static string PathFor(string gameRoot) => Path.Combine(Paths.NeutraledRoot(gameRoot), FileName);

    /// <summary>--launch 部署完成后、拉起游戏之前调用。失败只警告，不阻断启动（会话只是"方便还原"的线索）。</summary>
    public static bool Write(string gameRoot, string launch, IReadOnlyList<string> chapters)
    {
        try
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<string>();
            foreach (var c in chapters)
            {
                var t = (c ?? "").Trim();
                if (t.Length == 0 || !seen.Add(t)) continue;
                list.Add(t);
            }
            var s = new Session
            {
                Id = DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
                StartedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                Launch = launch,
                Chapters = list
            };
            Directory.CreateDirectory(Paths.NeutraledRoot(gameRoot));
            Paths.SafeWrite(PathFor(gameRoot), JsonSerializer.Serialize(s, Paths.Json));
            Console.WriteLine(L("[会话] 本次部署目标已记录: {0}（退出游戏后可 --restore-session 还原）", string.Join("/", list)));
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine(L("[警告] 写部署会话失败: {0}", ex.Message));
            return false;
        }
    }

    public static Session? Load(string gameRoot, out string? error)
    {
        error = null;
        try
        {
            var p = PathFor(gameRoot);
            if (!File.Exists(p)) return null;
            var s = JsonSerializer.Deserialize<Session>(File.ReadAllText(p), Paths.Json);
            if (s == null) { error = L("会话文件是空内容"); return null; }
            if (s.Chapters == null) s.Chapters = new List<string>();
            return s;
        }
        catch (Exception ex) { error = ex.Message; return null; }
    }

    /// <summary>游戏正在运行就不许写 live（还原会覆盖 data.win，占用时写一半最糟）。</summary>
    public static bool GameRunning() => Process.GetProcessesByName("DELTARUNE").Length > 0;

    private static void DeleteFile(string gameRoot)
    {
        try { var p = PathFor(gameRoot); if (File.Exists(p)) File.Delete(p); }
        catch (Exception ex) { Console.WriteLine(L("[警告] 删除部署会话文件失败: {0}", ex.Message)); }
    }

    /// <summary>--restore-session [--dry-run] [--json]：按会话文件把上次 --launch 写进 live 的目标还原成原版。</summary>
    public static int Cli(string gameRoot, bool dryRun, bool json)
    {
        var session = Load(gameRoot, out var loadError);
        bool running = GameRunning();
        var restored = new List<string>();
        var failed = new List<string>();
        int rc = 0;

        if (session == null)
        {
            if (json) { PrintJson(null, running, dryRun, restored, failed, loadError ?? ""); return 0; }
            if (!string.IsNullOrEmpty(loadError)) Console.WriteLine(L("[警告] 部署会话文件读不了: {0}", loadError));
            Console.WriteLine(L("没有未完成的部署会话（Neutraled/deploy-session.json 不存在）。"));
            return 0;
        }

        var chapters = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in session.Chapters)
        {
            var t = (c ?? "").Trim();
            if (t.Length == 0 || !seen.Add(t)) continue;
            if (!Program.IsValidSessionTarget(t)) { Console.WriteLine(L("[警告] 会话里有无法识别的目标，已跳过: {0}", t)); continue; }
            chapters.Add(t);
        }

        if (!json)
        {
            Console.WriteLine(L("===== 部署会话 ====="));
            Console.WriteLine(L("  记录时间(UTC): {0}   启动目标: {1}", session.StartedAtUtc, session.Launch));
            Console.WriteLine(L("  待还原目标: {0}", chapters.Count == 0 ? L("（空）") : string.Join("/", chapters)));
        }

        if (chapters.Count == 0)
        {
            if (!json) Console.WriteLine(L("[警告] 会话里没有可还原的目标；删除会话文件（可用 --restore-session 重跑或手工删）"));
            if (json) PrintJson(session, running, dryRun, restored, failed, "");
            return running && !dryRun ? 3 : 0;
        }

        if (running && !dryRun)
        {
            if (json) { PrintJson(session, running, dryRun, restored, failed, L("游戏正在运行")); return 3; }
            Console.WriteLine(L("[错误] 游戏正在运行（DELTARUNE）；请先退出游戏再还原（否则 data.win 被占用，可能写一半）"));
            return 3;
        }

        if (dryRun)
        {
            if (!json) Console.WriteLine(L("  预演：将还原 {0} 个目标（不写盘）", chapters.Count));
            if (json) PrintJson(session, running, dryRun, restored, failed, "");
            return 0;
        }

        foreach (var ch in chapters)
        {
            if (!json) Console.WriteLine(L("--- 还原 {0} ---", ch));
            try
            {
                var one = Program.RestoreTarget(gameRoot, ch);
                if (one == 0) restored.Add(ch);
                else { failed.Add(ch); rc = 3; }
            }
            catch (Exception ex)
            {
                failed.Add(ch);
                rc = 3;
                if (!json) Console.WriteLine(L("[错误] 还原 {0} 失败: {1}", ch, ex.Message));
            }
        }

        if (!json)
        {
            if (restored.Count > 0) Console.WriteLine(L("已还原 {0} 个目标: {1}", restored.Count, string.Join("/", restored)));
            if (failed.Count == 0)
            {
                DeleteFile(gameRoot);
                Console.WriteLine(L("会话已结束（deploy-session.json 已删除）"));
            }
            else
                Console.WriteLine(L("[警告] 有 {0} 个目标还原失败，会话文件保留以便重试: {1}", failed.Count, string.Join("/", failed)));
        }
        else PrintJson(session, running, dryRun, restored, failed, "");

        if (failed.Count == 0) DeleteFile(gameRoot);
        return rc;
    }

    private static void PrintJson(Session? s, bool running, bool dryRun, List<string> restored, List<string> failed, string message)
    {
        var payload = new
        {
            session = s == null ? null : new { s.Id, s.StartedAtUtc, s.Launch, s.Chapters },
            gameRunning = running,
            dryRun,
            restored,
            failed,
            restoredCount = restored.Count,
            failedCount = failed.Count,
            message
        };
        Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }));
    }
}
