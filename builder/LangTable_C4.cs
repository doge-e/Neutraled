using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>C4 P0-1：--deploy-state（按内容哈希判定 live 是原版还是已部署）的中英对照。
/// 新增条目的规则与其它 LangTable 一致：key 是 L() 里的中文原文，value 是英文（只允许 ASCII 标点，占位符必须与 key 一致）。</summary>
public static class LangTable_C4
{
    public static void Fill(Dictionary<string, string> d)
    {
        d["live data.win 不存在"] = "live data.win does not exist";
        d["与原版基线一致"] = "matches the vanilla baseline";
        d["与原版基线不同，且含部署产物标记"] = "differs from the vanilla baseline and contains deploy product markers";
        d["与原版基线不同，但不含部署产物标记（可能被外部工具直接改写）"] = "differs from the vanilla baseline but has no product markers (maybe rewritten by an external tool)";
        d["基线未登记（请跑 --verify-backup / --repair-backup），live 含部署产物标记"] = "baseline not registered (run --verify-backup / --repair-backup); live has deploy product markers";
        d["基线未登记且 live 不含部署产物标记，无法判断是否已部署"] = "baseline not registered and live has no product markers; cannot tell whether it is deployed";
        d["判定失败: {0}"] = "state check failed: {0}";
        d["[错误] 无效章节: {0}（可用 root / chapter1..chapterN / all）"] = "[error] invalid chapter: {0} (use root / chapter1..chapterN / all)";
        d["汇总: 原版 {0} / 已部署 {1} / 缺失 {2} / 未知 {3}"] = "Summary: vanilla {0} / deployed {1} / missing {2} / unknown {3}";
        d["基线={0}"] = "baseline={0}";
        d["未登记"] = "not registered";
        d["大小={0}"] = "size={0}";
        d["原版"] = "vanilla";
        d["缺失"] = "missing";
        d["未知"] = "unknown";
        d["  --deploy-state [--chapter all|root|chapterN] [--json]  只读查询 live 是原版还是已部署（按内容哈希判定，退出码 0）"] = "  --deploy-state [--chapter all|root|chapterN] [--json]  read-only: is live vanilla or deployed (by content hash, exit 0)";
        d["  --deploy-state [--chapter all|root|chapterN] [--json]  只读查询 live 是原版还是已部署（按内容哈希判定，退出码 0）"] = "  --deploy-state [--chapter all|root|chapterN] [--json]  read-only: is live vanilla or deployed (by content hash, exit 0)";
        d["                                  state = baseline / deployed / missing / unknown；GUI 的部署状态标签也用它"] = "                                  state = baseline / deployed / missing / unknown; also used by the GUI status label";
        d["[错误] 功能开关不能和主命令混用：{0} + {1}"] = "[error] a feature switch cannot be combined with a main command: {0} + {1}";
        d["       功能命令（--profile-* / --snapshot-* / --gb-* / --queue-* / --plugin-* / --theme-* / --lang-* / --web 等）必须单独运行。"] = "       feature commands (--profile-* / --snapshot-* / --gb-* / --queue-* / --plugin-* / --theme-* / --lang-* / --web ...) must run on their own.";
        d["[错误] 命令行参数有问题（共 {0} 处）:"] = "[error] command-line argument problem(s): {0}";
        d["  …还有 {0} 处"] = "  ...and {0} more";
        d["       用 --help 查看全部开关；确需忽略未知开关可加 --ignore-unknown。"] = "       run --help to list all switches; add --ignore-unknown if you really need to pass unknown switches through.";
        d["[未知开关] {0}（是否想用 {1}？）"] = "[unknown switch] {0} (did you mean {1}?)";
        d["[未知开关] {0}"] = "[unknown switch] {0}";
        d["[缺少取值] {0} 后面必须跟一个值"] = "[missing value] {0} must be followed by a value";
        d["[多余参数] {0}（不是任何开关的取值）"] = "[stray argument] {0} (not a value of any switch)";
        d["[错误] 未指定命令或命令不完整。"] = "[error] no command given, or the command is incomplete.";
        d["       最常用: --deploy / --deploy-all / --launch / --verify / --doctor / --verify-backup / --deploy-state"] = "       common: --deploy / --deploy-all / --launch / --verify / --doctor / --verify-backup / --deploy-state";
        d["       用 --help 查看全部命令；或用 --help all 查看全部开关。"] = "       run --help for all commands, or --help all for every switch.";
        d["  --ignore-unknown                跳过未知开关校验（高级/脚本透传用）"] = "  --ignore-unknown                skip unknown-switch validation (advanced / script pass-through)";

        // ---------- C4 P1-4：备份语义收敛（--baseline / --backup-status）+ 写命令默认目标提示 ----------
        d["[错误] 无效章节: {0}（可用 root / chapter1 .. chapter9 / all）"] = "[error] invalid chapter: {0} (use root / chapter1 .. chapter9 / all)";
        d["目标: root（未指定 --chapter；写命令默认只部署 root，显式 --chapter chapterN 会连带 root）"] = "Target: root (no --chapter given; write commands deploy root by default, explicit --chapter chapterN also deploys root)";
        d["目标: all（全部章节）"] = "Target: all (every chapter)";
        d["目标: {0}"] = "Target: {0}";
        d["目标: root + {0}（--no-root 可只部署 {0}）"] = "Target: root + {0} (use --no-root to deploy only {0})";
        d["  --restore-chapter all|root|chapterN [--dry-run]   把 live 还原成原版（单章或 all 全部；备份校验不通过则拒绝，退出码 3；--dry-run 只预演）"] = "  --restore-chapter all|root|chapterN [--dry-run]   restore live to vanilla (one chapter or all; refuses when the backup check fails, exit 3; --dry-run only previews)";
        d["  --baseline verify|repair|restore|status [--json]   备份收敛入口（分别等价 --verify-backup / --repair-backup / --restore-chapter / --backup-status）"] = "  --baseline verify|repair|restore|status [--json]   unified backup entry (same as --verify-backup / --repair-backup / --restore-chapter / --backup-status)";
        d["  --backup-status [--json]        只读：四层备份现状（baseline/snapshot/restorepoint/save）+ 可重建缓存 + legacy .backup_files"] = "  --backup-status [--json]        read-only: the four backup layers (baseline/snapshot/restorepoint/save) + rebuildable cache + legacy .backup_files";
        d["[预演] {0}: data.win 与原版一致（内容哈希相同，无需还原）"] = "[dry-run] {0}: data.win already matches vanilla (same content hash, nothing to restore)";
        d["[预演] {0}: 将还原 data.win <- {1}（不写盘）"] = "[dry-run] {0}: would restore data.win <- {1} (nothing written)";
        d["[预演] {0}: 将还原 {1} 个外部文件"] = "[dry-run] {0}: would restore {1} external file(s)";
        d["[预演] {0}: 将删除运行时清单 mods.json / scope.json"] = "[dry-run] {0}: would delete the runtime manifests mods.json / scope.json";
        d["（预演，不写盘）"] = " (dry-run, nothing written)";
        d["[错误] --baseline 需要子命令: verify|repair|restore|status（收到: {0}）"] = "[error] --baseline needs a subcommand: verify|repair|restore|status (got: {0})";
        d["       例: --baseline status --json / --baseline verify / --baseline restore --chapter chapter2 --dry-run"] = "       e.g. --baseline status --json / --baseline verify / --baseline restore --chapter chapter2 --dry-run";
        d["官方原版基线（权威、只读）；规范 {0} 个文件 / 镜像 {1} 个文件；账本 {2}"] = "official vanilla baseline (authoritative, read-only); {0} file(s) in primary / {1} file(s) in mirror; ledger {2}";
        d["单 mod 版本快照（--snapshot-*）"] = "single-mod version snapshots (--snapshot-*)";
        d["整游戏恢复点（--restore-*）"] = "whole-game restore points (--restore-*)";
        d["玩家存档（不要当备份删）"] = "player saves (do not delete as a backup)";
        d["部署缓存：可重建，不是备份"] = "deploy cache: rebuildable, not a backup";
        d["部署期外部文件覆盖（legacy，不是备份）"] = "deploy-time external file overrides (legacy, not a backup)";
        d["===== 备份 / 还原现状 ====="] = "===== backup / restore status =====";
        d["  游戏根: {0}"] = "  Game root: {0}";
        d["[baseline ] 权威基线"] = "[baseline ] authoritative baseline";
        d["[snapshot ] 单 mod 版本"] = "[snapshot ] per-mod version";
        d["[restorept] 整游戏恢复点"] = "[restorept] whole-game restore point";
        d["[save     ] 玩家存档"] = "[save     ] player saves";
        d["[cache    ] 可重建（不是备份）"] = "[cache    ] rebuildable (not a backup)";
        d["[legacy   ] 旧机制（不是备份）"] = "[legacy   ] old mechanism (not a backup)";
        d["文件 {0} 个 / {1} / 最近 {2}"] = "{0} file(s) / {1} / latest {2}";
        d["结论: 权威备份（baseline/snapshot/restorepoint/save）合计 {0}；可重建缓存 {1}（删了会重新生成）"] = "Result: authoritative backups (baseline/snapshot/restorepoint/save) total {0}; rebuildable cache {1} (regenerated if deleted)";
        d["提示: .backup_files 是部署期写的外部文件覆盖，不要当备份；确认不再需要可手工清理。"] = "Note: .backup_files holds deploy-time external file overrides, not backups; clean it up manually once you are sure.";
        d["深入: --verify-backup（哈希校验）/ --baseline status --json（机器可读）/ --doctor"] = "More: --verify-backup (hash check) / --baseline status --json (machine readable) / --doctor";
        d["无"] = "none";
        // C4 P1-9：LintRules7 的告警文案
        d["写死章节默认值"] = "hardcoded chapter default";
        d["写死了 \"chapter4\" 兜底（C4 P1-9：禁止 all/空值静默降级成第 4 章）。要章节就显式传/选；确属合法列表请在行尾加 // ntl:chapter4-ok"] =
            "hardcoded a \"chapter4\" fallback (C4 P1-9 forbids silently degrading all/empty to chapter 4). Pass or select a chapter explicitly; if this is a legitimate list, append // ntl:chapter4-ok";
        // C4 P1-6：--launch 可裸用 + --no-start 统一启动链
        d["  --launch [章节]                 部署（命中缓存则硬链接秒开）并交给 Steam 启动；裸 --launch = root"] =
            "  --launch [chapter]              deploy (hard-link instant apply on cache hit) then launch via Steam; bare --launch = root";
        d["  --launch --no-start             同上但不拉起 Steam（只部署；Studio/自动化用这个）"] =
            "  --launch --no-start             same but do not launch Steam (deploy only; used by Studio/automation)";
        d["  [no-start] --no-start → 已部署完成，不拉起 Steam"] =
            "  [no-start] --no-start -> deploy finished, Steam launch skipped";
        // C4 P1-11：前端能力矩阵
        d["  --frontend-matrix [--json]      四前端（CLI/GUI/Studio/Web）能力矩阵（Web 的 /api/capabilities 同源）"] =
            "  --frontend-matrix [--json]      capability matrix for CLI/GUI/Studio/Web (same source as web /api/capabilities)";
        d["===== 前端能力矩阵 ====="] = "===== frontend capability matrix =====";
        d["能力"] = "capability";
        d["  说明: true = 该前端有入口且走同一条 builder 链；false = 还没实现（UI 应隐藏，不要摆假按钮）"] =
            "  Legend: true = the frontend has an entry and uses the same builder chain; false = not implemented yet (hide it in the UI, do not show a fake button)";
        d["  机器可读: --frontend-matrix --json（Web: GET /api/capabilities 返回同一份 JSON）"] =
            "  Machine readable: --frontend-matrix --json (web: GET /api/capabilities returns the same JSON)";
    }
}
