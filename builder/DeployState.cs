using System.Text.Json;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>只读查询：某个槽位的 live data.win 现在是「原版」还是「已部署」。
///
/// 背景（A3 审计 F08）：GUI 与 --restore-chapter 以前用「文件长度相等」判断是否已部署，
/// 整个 chapter2 被整包重写后长度只差 256 B 也能被判成「与原版一致」。这里改为按内容判定：
///   · 基线账本（Neutraled/baseline.json）里的原版指纹 = 权威；
///   · 账本未登记时退到「两处备份副本一致且不含产物标记」这个证据；
///   · 产物标记（obj_ntl_core / scr_ntl_init / ntl_product_scope）作为「已部署」的旁证。
///
/// 状态取值：baseline（与原版一致） / deployed（已部署） / missing（live 不存在） / unknown（无法判定）。
/// 复用于 CLI（--deploy-state）与 GUI 部署状态标签；不写任何文件。</summary>
public static class DeployState
{
    public const string StateBaseline = "baseline";
    public const string StateDeployed = "deployed";
    public const string StateMissing = "missing";
    public const string StateUnknown = "unknown";

    public sealed class SlotState
    {
        public string Slot = "";
        public string State = StateUnknown;
        public string Reason = "";
        public string WinPath = "";
        public long WinBytes;
        public string WinFp = "missing";
        public string WinSha256 = "";
        public string BaseFp = "";
        public string BackupState = "";
        public string BackupDetail = "";
        public bool WinHasMarkers;
        public string WinMtimeUtc = "";
    }

    /// <summary>判定单个槽位；任何异常都收敛成 unknown（只读查询不能把 GUI/脚本打崩）。</summary>
    public static SlotState Evaluate(string gameRoot, string chapter)
    {
        var rep = new SlotState { Slot = chapter };
        try
        {
            var win = Paths.ChapterDataWin(gameRoot, chapter);
            rep.WinPath = win;
            var idn = Baseline.Identify(win, useCache: false);
            var brep = Baseline.Evaluate(gameRoot, chapter);
            rep.BackupState = brep.State.ToString();
            rep.BackupDetail = brep.Detail;

            if (idn == null)
            {
                rep.State = StateMissing;
                rep.Reason = L("live data.win 不存在");
                return rep;
            }
            var id = idn.Value;
            rep.WinBytes = id.Size;
            rep.WinFp = id.Fp;
            rep.WinSha256 = id.Sha256;
            rep.WinHasMarkers = id.HasMarkers;
            rep.WinMtimeUtc = File.GetLastWriteTimeUtc(win).ToString("o");

            // 基线指纹：优先账本；账本没登记时，两处备份都能当「原版」证据。
            var baseFp = brep.ExpectedFp;
            if (baseFp.Length == 0 && brep.PrimaryExists && !brep.PrimaryHasMarkers) baseFp = brep.PrimaryFp;
            if (baseFp.Length == 0 && brep.MirrorExists && !brep.MirrorHasMarkers) baseFp = brep.MirrorFp;
            rep.BaseFp = baseFp;

            if (baseFp.Length > 0 && !baseFp.Equals("missing", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(id.Fp, baseFp, StringComparison.OrdinalIgnoreCase))
                {
                    rep.State = StateBaseline;
                    rep.Reason = L("与原版基线一致");
                    return rep;
                }
                if (id.HasMarkers)
                {
                    rep.State = StateDeployed;
                    rep.Reason = L("与原版基线不同，且含部署产物标记");
                    return rep;
                }
                // 不含产物标记但与基线不同：不是原版（部署会覆盖它，C2 会先归档），
                // 状态仍算「已部署」（语义 = live 不是原版），原因写清楚便于排查。
                rep.State = StateDeployed;
                rep.Reason = L("与原版基线不同，但不含部署产物标记（可能被外部工具直接改写）");
                return rep;
            }

            // 没有可信基线：产物标记是唯一证据。
            if (id.HasMarkers)
            {
                rep.State = StateDeployed;
                rep.Reason = L("基线未登记（请跑 --verify-backup / --repair-backup），live 含部署产物标记");
                return rep;
            }
            rep.State = StateUnknown;
            rep.Reason = L("基线未登记且 live 不含部署产物标记，无法判断是否已部署");
            return rep;
        }
        catch (Exception ex)
        {
            rep.State = StateUnknown;
            rep.Reason = L("判定失败: {0}", ex.Message);
            return rep;
        }
    }

    /// <summary>--deploy-state [--chapter all|root|chapterN] [--json]</summary>
    public static int Cli(string gameRoot, string chapterArg, bool json)
    {
        var slots = new List<string>();
        if (string.IsNullOrWhiteSpace(chapterArg) || chapterArg.Equals("all", StringComparison.OrdinalIgnoreCase))
            slots.AddRange(Baseline.SlotsOf(gameRoot));
        else
        {
            var ch = chapterArg.Trim();
            if (!ch.Equals("root", StringComparison.OrdinalIgnoreCase) && !IsChapterName(ch))
            {
                Console.WriteLine(L("[错误] 无效章节: {0}（可用 root / chapter1..chapterN / all）", chapterArg));
                return 2;
            }
            slots.Add(ch.Equals("root", StringComparison.OrdinalIgnoreCase) ? "root" : ch.ToLowerInvariant());
        }

        var reports = slots.Select(s => Evaluate(gameRoot, s)).ToList();

        if (json)
        {
            var payload = new
            {
                schemaVersion = 1,
                gameRoot,
                generatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                slots = reports.Select(r => new
                {
                    slot = r.Slot,
                    state = r.State,
                    reason = r.Reason,
                    winPath = r.WinPath,
                    winBytes = r.WinBytes,
                    winFp = r.WinFp,
                    winSha256 = r.WinSha256,
                    winHasMarkers = r.WinHasMarkers,
                    winMtimeUtc = r.WinMtimeUtc,
                    baseFp = r.BaseFp,
                    backupState = r.BackupState,
                    backupDetail = r.BackupDetail,
                }),
                summary = new
                {
                    baseline = reports.Count(r => r.State == StateBaseline),
                    deployed = reports.Count(r => r.State == StateDeployed),
                    missing = reports.Count(r => r.State == StateMissing),
                    unknown = reports.Count(r => r.State == StateUnknown),
                },
            };
            Console.WriteLine(JsonSerializer.Serialize(payload, Paths.Json));
            return 0;
        }

        foreach (var r in reports)
            Console.WriteLine(Describe(r));
        Console.WriteLine();
        Console.WriteLine(L("汇总: 原版 {0} / 已部署 {1} / 缺失 {2} / 未知 {3}",
            reports.Count(r => r.State == StateBaseline), reports.Count(r => r.State == StateDeployed),
            reports.Count(r => r.State == StateMissing), reports.Count(r => r.State == StateUnknown)));
        return 0;
    }

    public static string Describe(SlotState r)
    {
        var label = r.State switch
        {
            StateBaseline => L("原版"),
            StateDeployed => L("已部署"),
            StateMissing => L("缺失"),
            _ => L("未知"),
        };
        var sb = new System.Text.StringBuilder();
        sb.Append(r.Slot).Append(": ").Append(label).Append("  ")
          .Append(L("live={0}", r.WinFp)).Append("  ")
          .Append(L("基线={0}", r.BaseFp.Length == 0 ? L("未登记") : r.BaseFp)).Append("  ")
          .Append(L("大小={0}", r.WinBytes));
        if (!string.IsNullOrEmpty(r.Reason)) sb.Append("  (").Append(r.Reason).Append(')');
        return sb.ToString();
    }

    static bool IsChapterName(string s)
    {
        var t = s.Trim().ToLowerInvariant();
        return t.StartsWith("chapter", StringComparison.Ordinal) && int.TryParse(t["chapter".Length..], out _);
    }
}
