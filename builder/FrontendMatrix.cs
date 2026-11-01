using System.Text.Json.Nodes;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>C4 P1-11：四前端（CLI / GUI / Studio / Web）能力矩阵的单一事实源。
/// 目的不是"再写一份功能列表"，而是让三件事可校验：
///   1) --frontend-matrix --json 给出机器可读矩阵（Web 的 /api/capabilities 返回同一份 JSON）；
///   2) 能力为 false 的入口应在 UI 里隐藏/禁用，而不是摆一个假按钮（A3 F19/R8）；
///   3) README/docs 的"哪个前端能做什么"段落由这张表生成，避免文档漂移。
/// 约定：新增前端能力时先改这里，再改前端；--help --audit / lint 会抓开关目录漂移。
/// 注意：矩阵必须诚实反映**当前实现**（例如 Studio 还没有章节选择就写 false），
/// 不然测试与文档都会骗人。哪天实现了就把对应位改 true 并同步前端。</summary>
public static class FrontendMatrix
{
    /// <summary>一个能力项（key 稳定，zh/en 只用于人读的表格与文档）。</summary>
    public sealed record Capability(string Key, string Zh, string En, string NoteZh = "", string NoteEn = "");

    /// <summary>一个前端。</summary>
    public sealed record Frontend(string Id, string Zh, string En, Dictionary<string, bool> Support);

    public static readonly Capability[] Capabilities =
    {
        new("deploy",           "部署",         "deploy",
            "把 mod 注入 root / chapterN 产物（含缓存与幂等跳过）",
            "inject mods into root / chapterN products (cache + idempotent skip)"),
        new("launch",           "启动游戏",     "launch",
            "部署后交给 Steam 启动（CLI 走 --launch；GUI/Studio 走同一条链）",
            "deploy then launch through Steam (CLI --launch; GUI/Studio use the same chain)"),
        new("chapterSelect",    "章节选择",     "chapter select",
            "让用户选目标章节；false 表示该前端只会用默认 root",
            "let the user pick a chapter; false means the frontend only uses the default root"),
        new("import",           "导入 mod",     "import mods",
            "把外部 mod 包/目录导入 Neutraled/mods",
            "import external mod packages/directories into Neutraled/mods"),
        new("exportPack",       "导出资源包",   "export packs",
            "把当前产物/资源导出成可分发包（--export-packs / --pack）",
            "export current products/assets as distributable packs (--export-packs / --pack)"),
        new("profiles",         "配置档",       "mod profiles",
            "保存/切换 mod 启用集合（套用仍走 CLI 与 Web；GUI 工具箱里有只读列表）",
            "save/switch mod enable sets (apply via CLI/Web; GUI toolbox has a read-only list)"),
        new("snapshots",        "单 mod 快照",  "mod snapshots",
            "单个 mod 的版本快照（--snapshot-*）",
            "per-mod version snapshots (--snapshot-*)"),
        new("restorePoints",    "整游戏恢复点","restore points",
            "整游戏恢复点（--restore-*）",
            "whole-game restore points (--restore-*)"),
        new("console",          "游戏内控制台","in-game console",
            "游戏内 F2 控制台需要游戏在跑，前端只能查看日志",
            "the F2 in-game console needs the game running; frontends can only read the log"),
        new("externalWatch",    "外部章节守候","external watcher",
            "守候进程（--watch-external）拉起 Kristal/外部章节",
            "watcher (--watch-external) that starts Kristal/external chapters"),
    };

    public static readonly Frontend[] Frontends = BuildFrontends();

    private static Frontend[] BuildFrontends()
    {
        // CLI 是基准：所有能力都在 CLI 有入口（GUI/Studio/Web 是它的子集）。
        var cli = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var c in Capabilities) cli[c.Key] = true;
        var gui = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["deploy"] = true, ["launch"] = true, ["chapterSelect"] = true, ["import"] = true,
            ["exportPack"] = false, ["profiles"] = false, ["snapshots"] = false, ["restorePoints"] = false,
            ["console"] = true, ["externalWatch"] = true,
        };
        var studio = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["deploy"] = true, ["launch"] = true, ["chapterSelect"] = false, ["import"] = false,
            ["exportPack"] = false, ["profiles"] = false, ["snapshots"] = false, ["restorePoints"] = false,
            ["console"] = true, ["externalWatch"] = false,
        };
        var web = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["deploy"] = true, ["launch"] = false, ["chapterSelect"] = true, ["import"] = true,
            ["exportPack"] = false, ["profiles"] = true, ["snapshots"] = true, ["restorePoints"] = true,
            ["console"] = false, ["externalWatch"] = false,
        };
        return new[]
        {
            new Frontend("cli",    "命令行", "CLI",    cli),
            new Frontend("gui",    "管理器 GUI", "manager GUI", gui),
            new Frontend("studio", "脚本编辑器", "script Studio", studio),
            new Frontend("web",    "网页界面", "web UI", web),
        };
    }

    /// <summary>机器可读矩阵。**不含时间戳**（方便 --gen-docs --check 与 /api/capabilities 逐字段比对）。</summary>
    public static JsonObject BuildJson()
    {
        var caps = new JsonArray();
        foreach (var c in Capabilities)
            caps.Add(new JsonObject
            {
                ["key"] = c.Key, ["zh"] = c.Zh, ["en"] = c.En,
                ["noteZh"] = c.NoteZh, ["noteEn"] = c.NoteEn,
            });
        var fes = new JsonObject();
        foreach (var f in Frontends)
        {
            var support = new JsonObject();
            foreach (var c in Capabilities) support[c.Key] = f.Support.TryGetValue(c.Key, out var v) && v;
            fes[f.Id] = new JsonObject
            {
                ["zh"] = f.Zh, ["en"] = f.En, ["support"] = support,
            };
        }
        return new JsonObject
        {
            ["schemaVersion"] = 1,
            ["source"] = "builder/FrontendMatrix.cs",
            ["capabilities"] = caps,
            ["frontends"] = fes,
        };
    }

    public static int Cli(string gameRoot, bool json)
    {
        if (json)
        {
            Console.WriteLine(BuildJson().ToJsonString(JsonOpts()));
            return 0;
        }
        Console.WriteLine(L("===== 前端能力矩阵 ====="));
        Console.WriteLine(L("  说明: true = 该前端有入口且走同一条 builder 链；false = 还没实现（UI 应隐藏，不要摆假按钮）"));
        Console.WriteLine();
        var idWidth = 8;
        var header = L("能力").PadRight(22) + "CLI".PadRight(idWidth) + "GUI".PadRight(idWidth) + "Studio".PadRight(idWidth) + "Web";
        Console.WriteLine("  " + header);
        foreach (var c in Capabilities)
        {
            var line = (Lang.IsEn ? c.En : c.Zh).PadRight(22);
            foreach (var f in Frontends)
                line += (f.Support.TryGetValue(c.Key, out var v) && v ? "true" : "false").PadRight(idWidth);
            Console.WriteLine("  " + line);
        }
        Console.WriteLine();
        Console.WriteLine(L("  机器可读: --frontend-matrix --json（Web: GET /api/capabilities 返回同一份 JSON）"));
        return 0;
    }

    private static System.Text.Json.JsonSerializerOptions JsonOpts() => new(Paths.Json) { WriteIndented = true };
}
