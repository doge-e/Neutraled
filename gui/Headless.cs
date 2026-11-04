using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Neutraled.Gui;

/// <summary>
/// 无窗口自检/自动化入口（C4 P1-12）：<c>ntl-gui.exe --headless alerts|selftest|plan|deploy-state|lang-report</c>。
/// 约定：这里**绝不**创建窗口 / 不 Initialize WinForms；只读文件或转发 builder，退出码 0=通过、1=自检失败、2=参数错误。
/// </summary>
public static class Headless
{
    public static bool IsHeadless(string[] args) =>
        args.Any(a => a.Equals("--headless", StringComparison.OrdinalIgnoreCase));

    public static int Run(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }

        var rest = new List<string>(args);
        int hi = rest.FindIndex(a => a.Equals("--headless", StringComparison.OrdinalIgnoreCase));
        if (hi >= 0) rest = rest.Skip(hi + 1).ToList();
        bool json = rest.RemoveAll(a => a.Equals("--json", StringComparison.OrdinalIgnoreCase)) > 0;
        var sub = rest.FirstOrDefault(a => !a.StartsWith("-")) ?? "";
        // 位置参数（plan 的 --action 值之外）也允许 --action deploy 这种写法
        string Opt(string name, string dflt = "")
        {
            var k = rest.FindIndex(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
            return k >= 0 && k + 1 < rest.Count ? rest[k + 1] : dflt;
        }

        try { Localizer.Load(Program.NeutraledRoot); } catch { }
        switch (sub)
        {
            case "alerts":
                return Alerts(json, Opt("--log", UiLogic.DefaultGameLogPath()),
                    Opt("--state", UiLogic.AlertsStatePath(Program.NeutraledRoot)));
            case "selftest":
                return SelfTest(json);
            case "plan":
                return Plan(json, Opt("--action", "deploy"), Opt("--chapter"),
                    Opt("--input"), Opt("--name", "demo"), Opt("--author", "tester"),
                    Opt("--out"), Opt("--win"));
            case "deploy-state":
                return DeployState(json, Opt("--chapter", "root"));
            case "lang-report":
                return LangReport(json);
            default:
                Console.WriteLine("[headless] usage: ntl-gui.exe --headless alerts|selftest|plan|deploy-state|lang-report [options] [--json]");
                Console.WriteLine("  alerts       --log <dr-api.log> [--state <gui-alerts.json>]   增量错误行（首次建基线、不重复报）");
                Console.WriteLine("  selftest                                                      断言章节解析/argv/退出码文案/告警偏移/冲突 ack");
                Console.WriteLine("  plan         --action deploy|launch|restore|import|packs|conflicts|layer|fetch|readonly --chapter <root|chapterN|all>");
                Console.WriteLine("  deploy-state --chapter <root|chapterN|all>                    转发 builder --deploy-state（--json 时逐字段相同）");
                Console.WriteLine("  lang-report                                                   读取 config.json 的唯一语言真源");
                // GUI-L10：不带子命令就是用法错误（原先 rc0，脚本无法区分「跑完了」和「什么也没跑」）。
                return 2;
        }
    }

    // ==================== alerts ====================

    private static int Alerts(bool json, string logPath, string statePath)
    {
        // GUI-L11：日志不存在 ≠ 没有新告警。旧实现返回空列表并 rc0，调用方会以为「一切正常」。
        if (!File.Exists(logPath))
        {
            if (json)
            {
                var err = new JsonObject
                {
                    ["error"] = "log-not-found",
                    ["log"] = logPath,
                    ["state"] = statePath,
                };
                Console.WriteLine(err.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                Console.Error.WriteLine("[headless] ERROR: 日志文件不存在: " + logPath);
                Console.Error.WriteLine("[headless] 提示：游戏至少启动过一次后才有 dr-api.log；或用 --log <路径> 指定。");
            }
            return 2;
        }
        var list = UiLogic.NewAlerts(logPath, statePath);
        if (json)
        {
            var obj = new JsonObject
            {
                ["log"] = logPath,
                ["state"] = statePath,
                ["count"] = list.Count,
                ["alerts"] = new JsonArray(list.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()),
            };
            Console.WriteLine(obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            Console.WriteLine($"[headless] log   = {logPath}");
            Console.WriteLine($"[headless] state = {statePath}");
            Console.WriteLine(list.Count == 0 ? "[headless] no new alerts" : $"[headless] {list.Count} new alert(s):");
            foreach (var line in list) Console.WriteLine("  " + line);
        }
        return 0;
    }

    // ==================== plan ====================

    private static int Plan(bool json, string actionRaw, string chapter, string input, string name, string author, string output, string win)
    {
        if (!TryParseAction(actionRaw, out var action))
        {
            Console.WriteLine(Localizer.T("[错误] 未知动作: {0}（可用 deploy|launch|restore|import|packs|conflicts|layer|fetch|readonly）", actionRaw));
            return 2;
        }
        var plan = UiLogic.PlanChapter(chapter, action, input, name, author, output, win);
        if (json)
        {
            var obj = new JsonObject
            {
                ["action"] = action.ToString(),
                ["ok"] = plan.Ok,
                ["chapter"] = plan.Chapter,
                ["args"] = plan.Args,
                ["error"] = plan.Error,
            };
            Console.WriteLine(obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        else if (!plan.Ok)
        {
            Console.WriteLine("[headless] " + plan.Error);
        }
        else
        {
            Console.WriteLine($"[headless] action  = {action}");
            Console.WriteLine($"[headless] chapter = {plan.Chapter}");
            Console.WriteLine($"[headless] argv    = {plan.Args}");
        }
        return plan.Ok ? 0 : 2;
    }

    private static bool TryParseAction(string raw, out UiLogic.ActionKind action)
    {
        switch (raw.Trim().ToLowerInvariant())
        {
            case "deploy": action = UiLogic.ActionKind.Deploy; return true;
            case "launch": action = UiLogic.ActionKind.Launch; return true;
            case "restore": case "restore-session": action = UiLogic.ActionKind.Restore; return true;
            case "import": case "import-mod": action = UiLogic.ActionKind.ImportMod; return true;
            case "packs": case "export-packs": action = UiLogic.ActionKind.Packs; return true;
            case "conflicts": action = UiLogic.ActionKind.Conflicts; return true;
            case "layer": case "layer-from-base": action = UiLogic.ActionKind.LayerFromBase; return true;
            case "fetch": action = UiLogic.ActionKind.Fetch; return true;
            case "readonly": case "read-only": action = UiLogic.ActionKind.ReadOnly; return true;
            default: action = UiLogic.ActionKind.Other; return false;
        }
    }

    // ==================== deploy-state（转发 builder，--json 时逐字段相同）====================

    private static int DeployState(bool json, string chapter)
    {
        var ch = (chapter ?? "").Trim();
        if (ch.Length == 0) ch = "root";
        var args = "--deploy-state --chapter " + ch + (json ? " --json" : "");
        var lines = new List<string>();
        int rc = Program.RunBuilderAsync(args, l => lines.Add(l)).GetAwaiter().GetResult();
        if (!json)
        {
            foreach (var l in lines) Console.WriteLine(l);
            return rc;
        }

        // builder 在 JSON 前后还打「游戏根: …」等人类可读行：取出第一个 '{' 到最后一个 '}' 再重新格式化输出，
        // 这样字段与 CLI --json 完全一致，便于逐字段比对。
        var raw = string.Join("\n", lines);
        int a = raw.IndexOf('{'), b = raw.LastIndexOf('}');
        if (a < 0 || b <= a)
        {
            foreach (var l in lines) Console.WriteLine(l);
            if (rc == 0) Console.WriteLine(Localizer.T("[错误] builder --deploy-state --json 没有输出 JSON"));
            return rc == 0 ? 1 : rc;
        }
        try
        {
            var node = JsonNode.Parse(raw.Substring(a, b - a + 1));
            Console.WriteLine(node!.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return rc;
        }
        catch
        {
            Console.WriteLine(raw.Substring(a, b - a + 1));
            return rc;
        }
    }

    // ==================== lang-report ====================

    private static int LangReport(bool json)
    {
        var configPath = Path.Combine(Program.NeutraledRoot, "config.json");
        string lang = "zh", source = "default";
        try
        {
            if (File.Exists(configPath) &&
                JsonNode.Parse(File.ReadAllText(configPath), null,
                    new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip })
                is JsonObject obj)
            {
                var v = obj["lang"]?.GetValue<string>()?.Trim();
                if (!string.IsNullOrEmpty(v)) { lang = v!; source = "config"; }
            }
        }
        catch { }
        var effective = string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "zh";
        if (json)
        {
            var node = new JsonObject
            {
                ["lang"] = lang,
                ["source"] = source,
                ["effective"] = effective,
                ["configPath"] = configPath,
            };
            Console.WriteLine(node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            Console.WriteLine($"[headless] lang      = {lang}");
            Console.WriteLine($"[headless] effective = {effective}（来源: {(source == "config" ? "config.json" : "默认值")}）");
            Console.WriteLine($"[headless] config    = {configPath}");
        }
        return 0;
    }

    // ==================== selftest ====================

    private static int SelfTest(bool json)
    {
        int pass = 0, fail = 0;
        var checks = new List<(string Name, bool Ok, string Detail)>();
        void Check(string name, bool ok, string detail = "")
        {
            checks.Add((name, ok, detail));
            if (ok) pass++; else fail++;
            Console.WriteLine($"[headless-selftest] {(ok ? "PASS" : "FAIL")} {name}{(detail.Length > 0 ? "  (" + detail + ")" : "")}");
        }

        Check("chapter: all 不允许用于 import", !UiLogic.PlanChapter("all", UiLogic.ActionKind.ImportMod).Ok);
        Check("chapter: all 允许用于 deploy", UiLogic.PlanChapter("all", UiLogic.ActionKind.Deploy).Ok);
        Check("chapter: 空选择只读默认 chapter1", UiLogic.ChapterAllowed("", UiLogic.ActionKind.ReadOnly) == "chapter1");
        Check("chapter: 空选择写命令默认 root", UiLogic.ChapterAllowed("", UiLogic.ActionKind.Deploy) == "root");
        Check("argv: deploy chapter1", UiLogic.BuildArgs(UiLogic.ActionKind.Deploy, "chapter1") == "--launch chapter1 --no-start");   // UX-04
        Check("argv: launch root", UiLogic.BuildArgs(UiLogic.ActionKind.Launch, "root") == "--launch root");
        Check("argv: conflicts chapter2", UiLogic.BuildArgs(UiLogic.ActionKind.Conflicts, "chapter2") == "--conflicts --chapter chapter2");
        Check("exit: 0 -> 无文案", UiLogic.MessageForExit(0, UiLogic.ActionKind.Deploy) == null);
        Check("exit: 2 -> 冲突文案", (UiLogic.MessageForExit(2, UiLogic.ActionKind.Conflicts) ?? "").Contains("冲突"));
        Check("exit: 3 -> 基线文案", (UiLogic.MessageForExit(3, UiLogic.ActionKind.Deploy) ?? "").Contains("基线"));

        // 告警偏移：首次静默建基线；追加 ERROR 后只报新增；再跑一次为空
        var tmp = Path.Combine(Program.NeutraledRoot, ".tmp", "gui-headless-selftest");
        try
        {
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);
            var log = Path.Combine(tmp, "dr-api.log");
            var state = Path.Combine(tmp, "alerts-state.json");
            File.WriteAllText(log, "[info] first line\n[info] second line\n");
            var first = UiLogic.NewAlerts(log, state);
            File.AppendAllText(log, "[错误] boom\nERROR boom2\n[info] ok\n");
            var second = UiLogic.NewAlerts(log, state);
            var third = UiLogic.NewAlerts(log, state);
            Check("alerts: 首次静默（历史错误不算新增）", first.Count == 0, $"count={first.Count}");
            Check("alerts: 追加 ERROR 后报 2 条", second.Count == 2, $"count={second.Count}");
            Check("alerts: 不重复报旧行", third.Count == 0, $"count={third.Count}");

            var conflicts = Path.Combine(tmp, "conflicts.json");
            var ack = Path.Combine(tmp, "conflicts-ack.txt");
            File.WriteAllText(conflicts, "{\"error\":0}");
            var newBefore = UiLogic.ConflictsIsNew(conflicts, ack);
            UiLogic.AckConflicts(conflicts, ack);
            var newAfter = UiLogic.ConflictsIsNew(conflicts, ack);
            File.WriteAllText(conflicts, "{\"error\":1}");
            var newChanged = UiLogic.ConflictsIsNew(conflicts, ack);
            Check("conflicts: 首次算新/ack 后不再新/内容变了又算新", newBefore && !newAfter && newChanged);
        }
        catch (Exception ex)
        {
            Check("fixture", false, ex.Message);
        }
        finally
        {
            try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
        }

        Console.WriteLine($"[headless-selftest] RESULT: {pass} pass / {fail} fail");
        if (json)
        {
            var arr = new JsonArray(checks.Select(c => (JsonNode?)new JsonObject
            {
                ["name"] = c.Name,
                ["ok"] = c.Ok,
                ["detail"] = c.Detail,
            }).ToArray());
            Console.WriteLine(new JsonObject { ["pass"] = pass, ["fail"] = fail, ["checks"] = arr }
                .ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        return fail == 0 ? 0 : 1;
    }
}
