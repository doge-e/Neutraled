using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>命令行开关目录（C4 / P0-4）：<b>唯一事实来源</b>，供参数校验、冲突检测、--help 审计与文档生成使用。
///
/// 背景：1.0.7 之前主 switch 没有 default 分支 —— 写错的开关会被**静默忽略**（例如
/// <c>--deploy --chapterr chapter2</c> 会按默认章节部署），而 CliFeatures 抢先扫描更会把
/// <c>--deploy --gb-search x</c> 里的 --deploy 静默丢掉（A3 审计 F02/F03）。这里维护全部合法开关：
/// <see cref="Known"/> 命中才放行，<see cref="RequiresValue"/> 用于「缺少取值」提示，
/// <see cref="FeatureSwitches"/> 用于「功能开关不能和主命令混用」的冲突检测。
///
/// 新增开关时**必须**同时登记在这里（否则会被参数校验判成未知开关，exit 2）——
/// 这是刻意的：宁可让开发者立刻发现，也不要再出现静默丢弃。</summary>
public static class CliCatalog
{
    /// <summary>全部合法开关（主 switch + CliFeatures 的功能开关 + 功能命令的具名选项）。</summary>
    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        "--accept-base-drift", "--add-external", "--adopt-current", "--all-slots", "--api-doc", "--as", "--as-delta", "--asset-guard",
        "--audit-inputscan", "--author", "--auto-stop", "--base", "--base-mod", "--block-add",
        "--block-list", "--block-remove", "--cache-applied", "--cache-check", "--cache-clear", "--cache-dedup", "--cache-max-set",
        "--cache-list", "--cache-max", "--chapter", "--chars", "--charset", "--check", "--conflicts",
        "--content-check", "--convert-kristal", "--delete-after", "--delete-file", "--deploy", "--deploy-all",
        "--deploy-state", "--desc", "--diff-override", "--diff-verbose", "--doctor", "--dry-run",
        "--dump", "--dumpall", "--ensure-watcher", "--export-packs", "--export-shaders", "--ext-args",
        "--extract", "--extract-diff", "--fast-deploy", "--fetch", "--fetch-mod", "--file",
        "--find", "--focus-test", "--font-name", "--font-native", "--font-probe", "--font-source",
        "--force", "--force-timelines", "--fork", "--from", "--from-font", "--full-deploy",
        "--game", "--gameinfo", "--gb-files", "--gb-install", "--gb-search", "--gb-type",
        "--gen-docs", "--gml-check", "--help", "--ignore-unknown", "--import-all", "--import-bside", "--import-delta", "--import-kristal",
        "--import-kristal-map", "--import-mod", "--info", "--install", "--install-id", "--install-url", "--jobs", "--json",
        "--keep", "--kind", "--kristal-console", "--kristal-console-restore", "--kristal-ensure", "--kristal-merge",
        "--lang", "--lang-coverage", "--lang-list", "--lang-template", "--lang-use", "--launch",
        "--layer-from-base", "--link-saves", "--lint", "--list", "--listobjs", "--listrooms",
        "--listsprites", "--make-bside", "--make-cjk-font", "--make-icon", "--map-id", "--map-out",
        "--merge-selftest", "--mod-force", "--mod-install", "--mod-list", "--mode", "--model",
        "--name", "--new-chapter", "--new-mod", "--no-backup", "--no-blocklist", "--no-cache", "--no-install",
        "--no-launch", "--no-open", "--no-root", "--no-save-rename", "--no-start", "--no-timeline", "--no-timelines", "--no-update-check",
        "--note", "--out", "--pack", "--per-page", "--platform-info", "--plugin-disable", "--plugin-enable",
        "--plugin-hooks", "--plugin-info", "--plugin-install", "--plugin-list", "--plugin-remove", "--probe-code",
        "--probe-object", "--probe-selftest", "--probe-timeline-runtime", "--profile", "--profile-copy", "--profile-delete",
        "--profile-export", "--profile-import", "--profile-list", "--profile-new", "--profile-rename", "--profile-show",
        "--profile-use", "--queue-add", "--queue-list", "--queue-remove", "--queue-run", "--repair-backup",
        "--repair-saves", "--restore-apply", "--restore-chapter", "--restore-create", "--restore-delete", "--restore-export",
        "--restore-session",
        "--restore-import", "--restore-list", "--restore-save", "--room", "--route-probe", "--save-list", "--save-name",
        "--search", "--self-check", "--selftest", "--send-f2", "--send-keys", "--send-text",
        "--size", "--slot", "--smoke", "--snapshot-auto", "--snapshot-create", "--snapshot-delete",
        "--snapshot-import", "--snapshot-list", "--snapshot-use", "--source-font", "--sprite", "--src",
        "--baseline", "--backup-status", "--config-get", "--config-keys", "--config-migrate", "--config-set", "--frontend-matrix",
        "--lang-migrate", "--lang-resolve",
        "--template", "--template-only", "--theme-import", "--theme-list", "--theme-show", "--theme-use",
        "--ttf", "--uninstall", "--update-check", "--url", "--validate-kristal", "--verify",
        "--verify-backup", "--verify-refcopy", "--version", "--watch-autostart", "--watch-external", "--web",
        "--with", "--yes",
    };

    /// <summary>必须跟一个取值的开关（缺少取值 → 用法错误 exit 2）。取值以 - 开头视为缺失。</summary>
    private static readonly HashSet<string> RequiresValue = new(StringComparer.Ordinal)
    {
        "--as", "--asset-guard", "--audit-inputscan", "--author", "--base", "--baseline", "--cache-max", "--cache-max-set", "--chapter",
        "--config-get", "--config-set",
        "--add-external", "--chars", "--charset", "--convert-kristal", "--dump", "--dumpall", "--export-packs",
        "--export-shaders", "--ext-args", "--extract", "--extract-diff", "--fetch", "--fetch-mod", "--find",
        "--focus-test", "--font-name", "--font-source", "--fork", "--from", "--from-font", "--gb-type",
        "--game", "--import-bside", "--import-delta", "--import-kristal", "--import-kristal-map", "--import-mod", "--install-id", "--install-url",
        "--jobs", "--kristal-merge", "--lang", "--layer-from-base", "--link-saves",
        "--list", "--listobjs", "--listsprites", "--map-id", "--map-out",
        "--mod-install", "--mode", "--model", "--name", "--new-chapter", "--new-mod",
        "--out", "--pack", "--per-page", "--probe-code", "--probe-object", "--room", "--route-probe",
        "--save-name", "--search", "--send-keys", "--send-text", "--size", "--slot",
        "--source-font", "--sprite", "--src", "--template", "--ttf", "--validate-kristal",
        "--verify-refcopy", "--with",
    };

    /// <summary>主命令开关 → 内部命令名（同一命令可能有多个开关别名）。</summary>
    private static readonly Dictionary<string, string> CommandSwitches = new(StringComparer.Ordinal)
    {
        ["--deploy"] = "deploy",
        ["--lint"] = "lint",
        ["--deploy-all"] = "deploy-all",
        ["--audit-inputscan"] = "audit-inputscan",
        ["--doctor"] = "doctor",
        ["--smoke"] = "smoke",
        ["--api-doc"] = "api-doc",
        ["--selftest"] = "selftest",
        ["--probe-timeline-runtime"] = "probe-timeline-runtime",
        ["--new-mod"] = "new-mod",
        ["--info"] = "info",
        ["--verify"] = "verify",
        ["--dump"] = "dump",
        ["--list"] = "list",
        ["--import-delta"] = "import-delta",
        ["--search"] = "search",
        ["--fetch"] = "fetch",
        ["--sprite"] = "sprite",
        ["--listsprites"] = "listsprites",
        ["--restore-chapter"] = "restore",
        ["--restore-session"] = "restore-session",
        ["--gen-docs"] = "gen-docs",
        ["--verify-backup"] = "verify-backup",
        ["--repair-backup"] = "repair-backup",
        ["--baseline"] = "baseline",
        ["--backup-status"] = "backup-status",
        ["--frontend-matrix"] = "frontend-matrix",
        ["--config-keys"] = "config-keys",
        ["--config-get"] = "config-get",
        ["--config-set"] = "config-set",
        ["--config-migrate"] = "config-migrate",
        ["--lang-resolve"] = "lang-resolve",
        ["--lang-migrate"] = "lang-migrate",
        ["--deploy-state"] = "deploy-state",
        ["--version"] = "version",
        ["--find"] = "find",
        ["--listobjs"] = "listobjs",
        ["--listrooms"] = "listrooms",
        ["--gameinfo"] = "gameinfo",
        ["--import-bside"] = "import-bside",
        ["--import-kristal-map"] = "import-kristal-map",
        ["--install"] = "install",
        ["--install-id"] = "install-id",
        ["--install-url"] = "install-url",
        ["--route-probe"] = "route-probe",
        ["--mod-install"] = "mod-install",
        ["--import-mod"] = "import-mod",
        ["--extract-diff"] = "extract-diff",
        ["--layer-from-base"] = "layer-from-base",
        ["--probe-code"] = "probe-code",
        ["--verify-refcopy"] = "verify-refcopy",
        ["--probe-selftest"] = "probe-selftest",
        ["--merge-selftest"] = "merge-selftest",
        ["--probe-object"] = "probe-object",
        ["--asset-guard"] = "asset-guard",
        ["--export-shaders"] = "export-shaders",
        ["--export-packs"] = "export-packs",
        ["--kristal-merge"] = "kristal-merge",
        ["--conflicts"] = "conflicts",
        ["--watch-external"] = "watch-external",
        ["--ensure-watcher"] = "ensure-watcher",
        ["--focus-test"] = "focus-test",
        ["--send-text"] = "focus-test",
        ["--send-keys"] = "focus-test",
        ["--update-check"] = "update-check",
        ["--adopt-current"] = "adopt-current",
        ["--fetch-mod"] = "fetch-mod",
        ["--mod-list"] = "mod-list",
        ["--uninstall"] = "uninstall",
        ["--save-list"] = "save-list",
        ["--restore-save"] = "restore-save",
        ["--cache-list"] = "cache-list",
        ["--make-icon"] = "make-icon",
        ["--cache-clear"] = "cache-clear",
        ["--cache-max-set"] = "cache-max-set",
        ["--cache-dedup"] = "cache-dedup",
        ["--cache-applied"] = "cache-applied",
        ["--launch"] = "launch",
        ["--make-bside"] = "make-bside",
        ["--dumpall"] = "dumpall",
        ["--link-saves"] = "link-saves",
        ["--repair-saves"] = "repair-saves",
        ["--room"] = "room",
        ["--pack"] = "pack",
        ["--import-kristal"] = "import-kristal",
        ["--convert-kristal"] = "convert-kristal",
        ["--validate-kristal"] = "validate-kristal",
        ["--new-chapter"] = "new-chapter",
        ["--fork"] = "new-chapter",
        // C4 P1-4 / builder-cli-006：这些主命令以前没登记映射 ⇒ 组合检测与拼写建议都看不到它们
        ["--add-external"] = "add-external",
        ["--import-all"] = "import-all",
        ["--extract"] = "extract",
        ["--font-probe"] = "font-probe",
        ["--make-cjk-font"] = "make-cjk-font",
        ["--font-native"] = "font-native",
        ["--content-check"] = "content-check",
        ["--cache-check"] = "cache-check",
        ["--watch-autostart"] = "watch-autostart",
        ["--kristal-console"] = "kristal-console",
        ["--kristal-ensure"] = "kristal-ensure",
        ["--kristal-console-restore"] = "kristal-console-restore",
    };

    /// <summary>CliFeatures 独占的功能开关：与主命令同时出现是用法错误（原先会被静默丢弃）。</summary>
    private static readonly HashSet<string> FeatureSwitches = new(StringComparer.Ordinal)
    {
        "--block-add", "--block-list", "--block-remove", "--gb-files", "--gb-install", "--gb-search",
        "--lang-coverage", "--lang-list", "--lang-template", "--lang-use", "--platform-info", "--plugin-disable",
        "--plugin-enable", "--plugin-hooks", "--plugin-info", "--plugin-install", "--plugin-list", "--plugin-remove",
        "--profile-copy", "--profile-delete", "--profile-export", "--profile-import", "--profile-list", "--profile-new",
        "--profile-rename", "--profile-show", "--profile-use", "--queue-add", "--queue-list", "--queue-remove",
        "--queue-run", "--restore-apply", "--restore-create", "--restore-delete", "--restore-export", "--restore-import",
        "--restore-list", "--snapshot-auto", "--snapshot-create", "--snapshot-delete", "--snapshot-import", "--snapshot-list",
        "--snapshot-use", "--theme-import", "--theme-list", "--theme-show", "--theme-use", "--web",
    };

    /// <summary>全部合法开关（只读遍历用：--help 目录审计、--gen-docs、lint 规则 16）。</summary>
    public static IEnumerable<string> KnownNames => Known;

    public static bool IsKnown(string arg) => Known.Contains(arg);
    public static bool RequiresValueOf(string arg) => RequiresValue.Contains(arg);
    public static bool IsFeatureSwitch(string arg) => FeatureSwitches.Contains(arg);

    /// <summary>返回 argv 里第一个主命令开关对应的命令名；没有主命令返回 null。
    /// 注意：只做**扫描**，不做取值校验 —— 校验在主 switch 的 default 分支里统一做。</summary>
    public static string? MainCommand(string[] args)
    {
        foreach (var a in args)
            if (CommandSwitches.TryGetValue(a, out var cmd)) return cmd;
        return null;
    }

    /// <summary>同 <see cref="MainCommand"/>，但返回命中的开关本身（错误消息用）。
    /// ignoreVersion=true 时跳过 --version（功能命令的 --version 是具名选项，见 builder-cli-004）。</summary>
    public static string? MainCommandSwitch(string[] args, bool ignoreVersion = false)
    {
        foreach (var a in args)
        {
            if (ignoreVersion && a == "--version") continue;
            if (CommandSwitches.ContainsKey(a)) return a;
        }
        return null;
    }

    /// <summary>argv 里出现的全部主命令（按命令名去重、保留首次出现顺序）。
    /// UX-24：以前多个主命令是「最后一个生效」（--deploy --info 只剩 info），用这个列表做冲突检测。</summary>
    public static List<string> MainCommandsDistinct(string[] args, bool ignoreVersion = false)
    {
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var a in args)
        {
            if (ignoreVersion && a == "--version") continue;
            if (CommandSwitches.TryGetValue(a, out var cmd) && seen.Add(cmd)) list.Add(cmd);
        }
        return list;
    }

    /// <summary>主命令列表（未知命令提示的编辑距离候选）。</summary>
    public static IEnumerable<string> CommandNames => CommandSwitches.Keys;

    /// <summary>拼写建议：与输入编辑距离 ≤2 的开关（最多 5 个），没有则返回空。</summary>
    public static List<string> Suggest(string arg)
    {
        var res = new List<(string name, int dist)>();
        foreach (var k in Known)
        {
            var d = Distance(arg, k);
            if (d <= 2) res.Add((k, d));
        }
        return res.OrderBy(x => x.dist).ThenBy(x => x.name, StringComparer.Ordinal)
                  .Take(5).Select(x => x.name).ToList();
    }

    private static int Distance(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}
