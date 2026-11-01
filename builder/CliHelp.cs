using System.Text;
using System.Text.Json;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>命令行帮助（C4 P1-1）：把 130+ 个开关变成「可发现」的。
///
/// 背景（A3 审计）：182 个开关里 63 个不在 --help、47 个不在 docs、26 个两个都没有；
/// 普通用户只能背开关名。现在分三层：
/// <list type="bullet">
/// <item><c>--help</c>：原有富文本帮助（场景化说明）+ 末尾提示怎么查全量；</item>
/// <item><c>--help all|advanced|dev</c>：按类别列出全部开关（数据源 = <see cref="CliCatalogDetails"/>）；</item>
/// <item><c>--help &lt;开关&gt;</c>：单个开关的说明；<c>--help --audit [--json]</c>：目录覆盖审计（missing/extra）。</item>
/// </list>
/// 另配 <c>--gen-docs [--out 文件] [--check]</c> 从同一份数据生成 docs/CLI_REFERENCE.md（--check 用于 CI 防漂移）。</summary>
public static class CliHelp
{
    /// <summary>类别（顺序 = help 里的展示顺序）。</summary>
    private static readonly (string Id, string Zh, string En)[] Groups =
    {
        ("start", "入门", "Getting started"),
        ("deploy", "部署", "Deploying"),
        ("verify", "校验与自检", "Verify \u0026 self-check"),
        ("backup", "原版备份", "Vanilla backup"),
        ("import", "导入 mod", "Importing mods"),
        ("export", "导出资源", "Exporting assets"),
        ("chapter", "章节", "Chapters"),
        ("asset", "资源与取证", "Assets \u0026 forensics"),
        ("font", "字体", "Fonts"),
        ("lang", "语言", "Language"),
        ("mod", "mod 管理", "Mod management"),
        ("save", "存档", "Saves"),
        ("bside", "B 面", "B-side"),
        ("cache", "缓存", "Cache"),
        ("profile", "配置档", "Profiles"),
        ("snapshot", "版本快照", "Snapshots"),
        ("restorepoint", "整游戏恢复点", "Restore points"),
        ("gb", "GameBanana", "GameBanana"),
        ("queue", "下载队列", "Download queue"),
        ("plugin", "插件", "Plugins"),
        ("theme", "主题", "Themes"),
        ("kristal", "Kristal", "Kristal"),
        ("external", "外部章节", "External chapters"),
        ("tool", "工具", "Tools"),
        ("debug", "调试与开发", "Debug \u0026 development"),
        ("platform", "平台", "Platform"),
        ("web", "网页界面", "Web UI"),
        ("security", "安全与拦截", "Safety \u0026 blocking"),
    };

    private static string GroupLabel(string id)
    {
        foreach (var g in Groups) if (g.Id == id) return Lang.IsEn ? g.En : g.Zh;
        return id;
    }

    /// <summary>类别标签（中英并排）。用于 --gen-docs 生成与界面语言无关的文档。</summary>
    private static string GroupBoth(string id)
    {
        foreach (var g in Groups) if (g.Id == id) return g.Zh + " / " + g.En;
        return id;
    }

    private static int LevelOf(string? selector) => selector switch
    {
        "all" => 2,
        "advanced" => 1,
        "dev" => 2,
        _ => 0,
    };

    /// <summary>--help 的入口。args 是整个 argv（用于选出 selector / audit / json）。</summary>
    public static int Dispatch(string[] args, Action printRichHelp)
    {
        bool audit = false, json = false;
        // --help 之外还有几个 token（用于区分「--help all」和「--help --game <路径>」这类带选项的调用）。
        var others = args.Where(a => a is not ("--help" or "-h" or "--audit" or "--json")).ToList();
        string? afterHelp = null;
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a is "--help" or "-h")
            {
                if (i + 1 < args.Length) afterHelp = args[i + 1];
                continue;
            }
            if (a == "--audit") audit = true;
            else if (a == "--json") json = true;
        }

        string? selector = null;
        if (afterHelp == "all" || afterHelp == "advanced" || afterHelp == "dev") selector = afterHelp;
        else if (afterHelp != null && CliCatalog.IsKnown(afterHelp) && others.Count == 1) selector = afterHelp;
        else if (afterHelp != null && !afterHelp.StartsWith("-", StringComparison.Ordinal) && others.Count == 1) selector = afterHelp;

        if (audit) return Audit(json);

        if (selector == null)
        {
            printRichHelp();
            Console.WriteLine();
            Console.WriteLine(L("  更多开关: --help all（全部 {0} 个，按类别） / --help advanced / --help dev / --help <开关> / --help --audit", CliCatalogDetails.All.Length));
            return 0;
        }

        if (selector is "all" or "advanced" or "dev") return PrintGrouped(LevelOf(selector));

        // --help <开关>：允许写 --deploy 或 deploy
        var name = selector.StartsWith("--", StringComparison.Ordinal) ? selector : "--" + selector;
        foreach (var e in CliCatalogDetails.All)
        {
            if (!string.Equals(e.Name, name, StringComparison.Ordinal)) continue;
            Console.WriteLine(e.Name + "   [" + GroupLabel(e.Group) + (e.Level > 0 ? (e.Level == 1 ? L(" · 高级") : L(" · 开发")) : "") + "]");
            Console.WriteLine("  " + (Lang.IsEn ? e.En : e.Zh));
            if (CliCatalog.RequiresValueOf(e.Name)) Console.WriteLine(L("  （需要取值）"));
            return 0;
        }
        Console.WriteLine(L("[错误] 没有这个开关: {0}", selector));
        var sug = CliCatalog.Suggest(name);
        if (sug.Count > 0) Console.WriteLine(L("  你是不是想输入: {0}", string.Join(" / ", sug)));
        Console.WriteLine(L("  查看全部开关: --help all"));
        return 2;
    }

    /// <summary>按类别打印（Level 过滤：0 常用 / 1 进阶 / 2 全部）。</summary>
    public static int PrintGrouped(int maxLevel)
    {
        Console.WriteLine(L("Neutraled 命令行开关（共 {0} 个；★ = 需要取值）", CliCatalogDetails.All.Length));
        Console.WriteLine(L("  --help           场景化帮助（推荐先看）"));
        Console.WriteLine(L("  --help <开关>    单个开关说明"));
        Console.WriteLine();
        foreach (var g in Groups)
        {
            var items = CliCatalogDetails.All
                .Where(e => e.Group == g.Id && e.Level <= maxLevel)
                .OrderBy(e => e.Name, StringComparer.Ordinal)
                .ToList();
            if (items.Count == 0) continue;
            Console.WriteLine("[" + GroupLabel(g.Id) + "]");
            foreach (var e in items)
            {
                var star = CliCatalog.RequiresValueOf(e.Name) ? "*" : " ";
                Console.WriteLine("  " + e.Name.PadRight(26) + star + " " + (Lang.IsEn ? e.En : e.Zh));
            }
            Console.WriteLine();
        }
        if (maxLevel < 2) Console.WriteLine(L("  更全: --help all（含开发/内部开关）"));
        return 0;
    }

    /// <summary>目录审计：CliCatalog.Known ↔ CliCatalogDetails.All 必须一一对应（--help --audit）。</summary>
    public static int Audit(bool json)
    {
        var known = new HashSet<string>(CliCatalog.KnownNames, StringComparer.Ordinal);
        var catalog = new HashSet<string>(CliCatalogDetails.All.Select(e => e.Name), StringComparer.Ordinal);
        var missing = known.Where(k => !catalog.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var extra = catalog.Where(k => !known.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var duplicate = CliCatalogDetails.All.GroupBy(e => e.Name).Where(g => g.Count() > 1)
            .Select(g => g.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var badGroup = CliCatalogDetails.All.Where(e => Groups.All(g => g.Id != e.Group))
            .Select(e => e.Name).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var ok = missing.Count == 0 && extra.Count == 0 && duplicate.Count == 0 && badGroup.Count == 0;

        if (json)
        {
            var payload = new
            {
                schemaVersion = 1,
                totalKnown = known.Count,
                totalCatalog = CliCatalogDetails.All.Length,
                missing,
                extra,
                duplicate,
                badGroup,
                ok,
            };
            Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }));
        }
        else
        {
            Console.WriteLine(L("===== 命令行目录审计 ====="));
            Console.WriteLine(L("  CliCatalog.Known: {0} 个    CliCatalogDetails: {1} 条", known.Count, CliCatalogDetails.All.Length));
            Console.WriteLine(missing.Count == 0 ? L("  [通过] 没有「合法但没说明」的开关") : L("  [缺] {0} 个: {1}", missing.Count, string.Join(", ", missing)));
            Console.WriteLine(extra.Count == 0 ? L("  [通过] 没有「有说明但不合法」的开关") : L("  [多] {0} 个: {1}", extra.Count, string.Join(", ", extra)));
            if (duplicate.Count > 0) Console.WriteLine(L("  [重复] {0}: {1}", duplicate.Count, string.Join(", ", duplicate)));
            if (badGroup.Count > 0) Console.WriteLine(L("  [类别非法] {0}: {1}", badGroup.Count, string.Join(", ", badGroup)));
            Console.WriteLine(ok ? L("结果: 目录完整 ✓") : L("结果: 目录不完整（先补齐 CliCatalogDetails）"));
        }
        return ok ? 0 : 3;
    }

    /// <summary>--gen-docs：从目录生成 CLI_REFERENCE.md；--check 只比对不写（过期 exit 3）。</summary>
    public static int GenDocs(string gameRoot, string? outPath, bool check)
    {
        var path = string.IsNullOrEmpty(outPath)
            ? Path.Combine(gameRoot, "Neutraled", "docs", "CLI_REFERENCE.md")
            : outPath!;
        var md = BuildMarkdown();
        if (check)
        {
            if (!File.Exists(path))
            {
                Console.WriteLine(L("[过期] 文档不存在: {0}（先跑 --gen-docs）", path));
                return 3;
            }
            var cur = File.ReadAllText(path).Replace("\r\n", "\n");
            if (!string.Equals(cur, md.Replace("\r\n", "\n"), StringComparison.Ordinal))
            {
                Console.WriteLine(L("[过期] 文档与开关目录不一致: {0}（重新跑 --gen-docs）", path));
                return 3;
            }
            Console.WriteLine(L("[通过] 文档与开关目录一致: {0}", path));
            return 0;
        }
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, md);
        Console.WriteLine(L("已生成: {0}（{1} 个开关）", path, CliCatalogDetails.All.Length));
        return 0;
    }

    /// <summary>生成 CLI_REFERENCE.md 的正文。
    /// ⚠ 这里**故意不调用 L()/GroupLabel**：文档内容必须与界面语言无关，否则 zh 生成的文档在 en 下跑
    /// <c>--gen-docs --check</c> 会永远「过期」。所以中英说明并排固定输出。</summary>
    private static string BuildMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Neutraled 命令行参考 / CLI reference（自动生成 auto-generated）");
        sb.AppendLine();
        sb.AppendLine("> 本文件由 `ntl-builder --gen-docs` 从 `builder/CliCatalogDetails.cs` 生成，**不要手改**。");
        sb.AppendLine("> Generated from `builder/CliCatalogDetails.cs` by `ntl-builder --gen-docs`; do not edit by hand.");
        sb.AppendLine("> 改开关说明：改 `CliCatalogDetails.cs` 后重新生成；`--gen-docs --check` 可检测过期。");
        sb.AppendLine();
        sb.AppendLine("常用入口 / common entry points: `--deploy` / `--deploy-all` / `--launch root|chapterN|all` / `--help` / `--verify` / `--doctor` / `--deploy-state` / `--restore-session`。");
        sb.AppendLine();
        sb.AppendLine("| 开关 / switch | 说明 / description | 需要取值 / takes value | 级别 / level | 类别 / category |");
        sb.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var e in CliCatalogDetails.All.OrderBy(e => e.Group).ThenBy(e => e.Name, StringComparer.Ordinal))
        {
            var lv = e.Level == 0 ? "常用 / common" : e.Level == 1 ? "进阶 / advanced" : "开发 / dev";
            sb.AppendLine($"| `{e.Name}` | {e.Zh} — {e.En} | {(CliCatalog.RequiresValueOf(e.Name) ? "是 / yes" : "")} | {lv} | {GroupBoth(e.Group)} |");
        }
        sb.AppendLine();
        return sb.ToString();
    }
}
