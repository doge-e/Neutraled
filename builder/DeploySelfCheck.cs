using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using UndertaleModLib;
using UndertaleModLib.Decompiler;
using UndertaleModLib.Models;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>部署后自检（安全适配）：部署写盘后**重新打开产物 data.win** 做结构化校验，
/// 把「部署报成功、游戏里功能却静默失效」变成「响亮报错」。
///
/// 检查项：
///   (a) 产物 data.win 能被 UTMT 重新打开（Injector.Load 真实解析一遍）；
///   (b) 引导注入存在：bootCodeName 对应代码条目的反编译文本里含 `scr_ntl_init();`；
///   (c) 控制器 obj_ntl_core 的 Create / Step / Draw 事件都绑定了**非空**代码；
///   (d) API 脚本数量：gml_Script_ntl_ 开头的代码对象 ≥ 200（少了说明注入被截断）；
///   (e) Neutraled/chapters.json 与 api-registry.json 能被严格 JSON 解析
///       （GML 侧 json_parse 读的就是这两个文件，解析不了章节选择器会直接失效）；
///   (f) 内容级：语言文本 × 字体字形覆盖（抓「文本全是中文、字体只有 ASCII」⇒ 游戏里一个字都画不出）；
///   (g) 界面增强真的落在产物里：反编译 gml_Object_obj_darkcontroller_Draw_0 / _Step_0，要求出现
///       ntl_settings_row_draw / ntl_cfg_scroll( / ntl_cfg_row( / ntl_cfg_scrollbar_draw /
///       ntl_modmenu_page_draw / ntl_settings_row_press / ntl_modmenu_page_step。
///       **为什么必须反编译**：`增强: {label}` 是 FR 入队后无条件打印的，只看部署日志会被
///       「search 一条都没命中」骗过去（实测 chapter1 的 Draw_0 与 chapter4 不是同一份代码）。
///   (h) 产物侧 JSON 无 \uXXXX 转义：GML 的 json_parse 吃不下 \uXXXX（CLAUDE.md 硬坑 15，
///       2026-09-25 在 chapters.json 上实测：一个中文就让整个注册表解析失败）。
///       **为什么必须扫产物目录**：builder 之前用「默认 JsonSerializerOptions」写 mods.json，
///       中文 mod 名被写成 \uXXXX ⇒ GML 解析异常 ⇒ Mod 设置面板恒显示「已加载 0」，
///       而部署本身完全成功、日志一片绿。CLI 侧产物目录 = 产物 data.win 同级的 Neutraled\。
///
/// 返回 0 = 全部通过；1 = 发现严重问题（含自检本身抛异常、Load 失败）。
/// **不吞异常**：任何一步抛异常都转成该项「失败」并打印异常类型与消息。
///
/// 性能：只 Load 一次产物、GlobalDecompileContext 只建一次并复用。
/// 实测 chapter5（229MB 产物）：Load ≈ 8 秒（冷启动 ≈ 24 秒），是绝对大头；
/// 其余检查（反编译引导 + 事件遍历 + 代码计数 + JSON）合计 &lt; 1 秒。
/// </summary>
public static class DeploySelfCheck
{
    /// <summary>API 脚本数量下限（gml_Script_ntl_ 开头的代码对象）。</summary>
    public const int MinApiScripts = 200;

    /// <summary>控制器对象名。</summary>
    public const string CoreObjectName = "obj_ntl_core";

    /// <summary>引导脚本名（反编译文本里必须出现对它的调用）。</summary>
    public const string BootInitScript = "scr_ntl_init";

    /// <summary>部署后自检。gameRoot 为游戏根；chapter 为章节名（root/chapter5…），
    /// 也可直接给「平行时间线产物目录」或 data.win 路径；bootCodeName 为空时按章节取默认引导条目。
    /// 返回 0 = 全部通过；1 = 发现严重问题。</summary>
    public static int Run(string gameRoot, string chapter, string bootCodeName)
    {
        var total = Stopwatch.StartNew();
        long loadMs = 0;
        int fails = 0;

        // 每项统一格式：通过/失败 + 关键数字/名字
        void Check(string id, string title, bool ok, string detail)
        {
            if (!ok) fails++;
            Console.WriteLine("  [" + (ok ? L("通过") : L("失败")) + "] " + id + " " + title + " —— " + detail);
        }

        try
        {
            chapter = string.IsNullOrWhiteSpace(chapter) ? "root" : chapter.Trim();
            gameRoot = string.IsNullOrWhiteSpace(gameRoot) ? Paths.DetectGameRoot() : gameRoot.Trim();
            if (string.IsNullOrWhiteSpace(bootCodeName)) bootCodeName = DefaultBootCodeName(chapter);

            var win = ResolveProductDataWin(gameRoot, chapter);
            Console.WriteLine(L("===== 部署后自检 [") + chapter + "] =====");
            Console.WriteLine(L("  产物: ") + win);
            Console.WriteLine(L("  引导目标: ") + bootCodeName);

            // ---------- (e) 注册表 JSON（GML json_parse 读的就是这两个文件；与产物无关，任何分支都要跑） ----------
            void CheckRegistryJson()
            {
                var neutraledRoot = Paths.NeutraledRoot(gameRoot);
                var jsonReport = new List<string>();
                bool jsonOk = true;
                foreach (var name in new[] { "chapters.json", "api-registry.json", "ns-registry.json" })
                {
                    var p = Path.Combine(neutraledRoot, name);
                    if (!File.Exists(p)) { jsonOk = false; jsonReport.Add(name + L(": 缺失（") + p + L("）")); continue; }
                    try
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(p));
                        jsonReport.Add(name + L(": OK（") + JsonCount(doc.RootElement) + L(" 项，") + new FileInfo(p).Length + L(" 字节）"));
                    }
                    catch (Exception ex)
                    {
                        jsonOk = false;
                        jsonReport.Add(name + L(": 解析失败 —— ") + Describe(ex));
                    }
                }
                Check("(e)", L("注册表 JSON 可解析（严格模式）"), jsonOk, string.Join(L("；"), jsonReport));
            }

            // ---------- (h) 产物侧 JSON 无 \uXXXX 转义（与产物无关，任何分支都要跑） ----------
            // 抓「部署全绿、GML 侧 json_parse 静默失败」：GML 的 json_parse 吃不下 \uXXXX，
            // 一个中文 mod 名就让整个 mods.json 解析失败，面板恒显示「已加载 0」。
            void CheckProductJsonEscapes()
            {
                var dir = Path.Combine(Path.GetDirectoryName(win)!, "Neutraled");
                var hits = new List<string>();
                int scanned = 0;
                if (Directory.Exists(dir))
                {
                    foreach (var p in Directory.EnumerateFiles(dir, "*.json"))
                    {
                        scanned++;
                        try
                        {
                            var txt = File.ReadAllText(p);
                            var ms = Regex.Matches(txt, @"\\u[0-9a-fA-F]{4}");
                            if (ms.Count > 0)
                                hits.Add(Path.GetFileName(p) + L("（") + ms.Count + L(" 处，首例 ") + ms[0].Value + L("）"));
                        }
                        catch (Exception ex)
                        {
                            hits.Add(Path.GetFileName(p) + L(" 读取失败 —— ") + Describe(ex));
                        }
                    }
                }
                Check("(h)", L("产物 JSON 无 \\uXXXX 转义（GML json_parse 吃不下转义）"), hits.Count == 0,
                    hits.Count == 0
                        ? L("已扫描 ") + scanned + L(" 个 JSON，0 处转义")
                        : string.Join(L("；"), hits) + L(" —— GML 侧 json_parse 会失败（中文名被转义写出了）"));
            }

            if (!File.Exists(win))
            {
                Console.WriteLine(L("  [失败] (a) 重新打开产物 data.win —— 产物不存在（") + win + L("），自检无法继续"));
                CheckRegistryJson();
                CheckProductJsonEscapes();
                Console.WriteLine(L("结果: 发现严重问题（失败 1 项 + 未能执行 b/c/d，用时 ") + total.ElapsedMilliseconds + L(" ms）"));
                return 1;
            }
            var fi = new FileInfo(win);
            Console.WriteLine(L("  体积: ") + Mb(fi.Length) + L(" MB   修改时间: ") + fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));

            // ---------- (a) 用 UTMT 重新打开产物 ----------
            var swLoad = Stopwatch.StartNew();
            UndertaleData? data = null;
            string loadErr = "";
            try { data = Injector.Load(win); }
            catch (Exception ex) { loadErr = Describe(ex); }
            loadMs = swLoad.ElapsedMilliseconds;
            bool opened = data != null;
            Check("(a)", L("重新打开产物 data.win（UTMT 重新解析）"), opened,
                opened
                    ? L("代码 ") + data!.Code.Count + L(" / 对象 ") + data.GameObjects.Count + L(" / 脚本 ") + data.Scripts.Count + L("，Load ") + loadMs + " ms"
                    : L("Injector.Load 抛异常（") + loadMs + L(" ms）—— ") + loadErr);

            if (!opened)
            {
                // 打不开产物时 b/c/d 全部按失败报（不静默跳过、不算通过）
                Check("(b)", L("引导注入（反编译文本含 ") + BootInitScript + L("();）"), false, L("未执行：产物无法打开"));
                Check("(c)", L("控制器 Create/Step/Draw 事件绑定"), false, L("未执行：产物无法打开"));
                Check("(d)", L("API 脚本数量（gml_Script_ntl_* ≥ ") + MinApiScripts + L("）"), false, L("未执行：产物无法打开"));
            }
            else
            {
                var d = data!;

                // 反编译一次、全局上下文复用（229MB 产物上新建一次 ≈ 0.3~0.5 秒）
                GlobalDecompileContext? gctxShared = null;
                string Decompiled(string codeName, out string err)
                {
                    err = "";
                    try
                    {
                        gctxShared ??= new GlobalDecompileContext(d);
                        return Injector.DecompileWith(gctxShared, d, codeName);
                    }
                    catch (Exception ex) { err = Describe(ex); return ""; }
                }

                // ---------- (b) 引导注入 ----------
                var bootCode = d.Code.ByName(bootCodeName);
                int bootInstr = bootCode?.Instructions?.Count ?? -1;
                string decompiled = "";
                string decErr = "";
                if (bootCode != null) decompiled = Decompiled(bootCodeName, out decErr);
                int initHits = CountHits(decompiled, BootInitScript);
                bool callFound = Regex.IsMatch(decompiled, @"scr_ntl_init\s*\(\s*\)\s*;");
                var initFn = d.Functions.ByName(BootInitScript);
                bool bootOk = bootCode != null && bootInstr > 0 && callFound;
                string bootDetail;
                if (bootCode == null)
                    bootDetail = L("产物里没有代码条目 ") + bootCodeName;
                else if (decErr.Length > 0)
                    bootDetail = L("代码条目存在（") + bootInstr + L(" 指令）但反编译失败 —— ") + decErr;
                else
                    bootDetail = L("代码条目 ") + bootCodeName + L("（") + bootInstr + L(" 指令）；反编译 ") + decompiled.Length
                        + L(" 字符，出现 ") + BootInitScript + " " + initHits + L(" 次，`scr_ntl_init();` ")
                        + (callFound ? L("命中") : L("未命中")) + L("；Functions 引用数 ") + (initFn == null ? 0 : initFn.Occurrences);
                Check("(b)", L("引导注入（反编译文本含 ") + BootInitScript + L("();）"), bootOk, bootDetail);

                // ---------- (c) 控制器事件绑定 ----------
                var core = d.GameObjects.ByName(CoreObjectName);
                if (core == null)
                {
                    Check("(c)", L("控制器 Create/Step/Draw 事件绑定"), false, L("产物里没有对象 ") + CoreObjectName);
                }
                else
                {
                    var create = BoundEvents(core, EventType.Create);
                    var step = BoundEvents(core, EventType.Step);
                    var draw = BoundEvents(core, EventType.Draw);
                    var missing = new List<string>();
                    if (!create.Any(e => e.Bound)) missing.Add(create.Count == 0 ? L("Create（无该事件）") : L("Create（事件代码为空）"));
                    if (!step.Any(e => e.Bound)) missing.Add(step.Count == 0 ? L("Step（无该事件）") : L("Step（事件代码为空）"));
                    if (!draw.Any(e => e.Bound)) missing.Add(draw.Count == 0 ? L("Draw（无该事件）") : L("Draw（事件代码为空）"));
                    bool coreOk = missing.Count == 0;
                    string coreDetail = coreOk
                        ? "Create: " + FmtEvents(create) + L("；Step: ") + FmtEvents(step) + L("；Draw: ") + FmtEvents(draw)
                        : L("缺少/失效: ") + string.Join(L("，"), missing)
                          + L("；实际 Create: ") + FmtEvents(create) + L("；Step: ") + FmtEvents(step) + L("；Draw: ") + FmtEvents(draw);
                    Check("(c)", L("控制器 Create/Step/Draw 事件绑定"), coreOk, coreDetail);
                }

                // ---------- (d) API 脚本数量 ----------
                var apiNames = new List<string>();
                foreach (var c in d.Code)
                {
                    var n = c.Name?.Content;
                    if (n != null && n.StartsWith("gml_Script_ntl_", StringComparison.Ordinal)) apiNames.Add(n);
                }
                // ---------- (f) 内容级：语言文本 × 字体字形覆盖 ----------
                // 抓「部署全绿、游戏里却一个字都画不出来」这类 bug（实测：汉化文本 + 只有 ASCII 的 fnt_main）。
                try
                {
                    var crep = ContentCheck.CheckTarget(gameRoot, chapter, d, verbose: false, winOverride: win,
                    rootUiOnly: string.Equals(chapter, "root", StringComparison.OrdinalIgnoreCase));
                    // 与 ContentCheck 的口径对齐：只有「失败」才拦部署；「警告」= 主字体不足，
                    // 但游戏正文真正会用的某个字体（fnt_main / fnt_ja_* …）覆盖得住，产物仍可用。
                    bool cwarn = crep.Verdict == "警告";
                    bool cok = crep.Verdict == "通过" || crep.Verdict == "跳过" || cwarn;
                    Check("(f)", L("内容级：文本 × 字体字形覆盖"), cok,
                        (cwarn ? L("[警告 主字体覆盖不足，但游戏会用到的字体覆盖得住] ") : "")
                        + crep.Detail + (crep.MissingCount > 0 ? L("；渲染不出样例: ") + crep.MissingSample : ""));
                }
                catch (Exception cex)
                {
                    Check("(f)", L("内容级：文本 × 字体字形覆盖"), false, L("检查抛异常 —— ") + Describe(cex));
                }

                // ---------- (g) 界面增强是否真的写进产物 ----------
                // 抓「FR 没命中却静默通过」：`增强: {label}` 在 Injector 里是入队后无条件打印的，
                // 只有把产物反编译出来才知道补丁到底在不在。
                // 实测踩过：chapter1 的 obj_darkcontroller_Draw_0 用 scr_84_get_lang_string(...) / 红心 sprite 922，
                // 与 chapter4 的 stringsetloc(...) / sprite 3695 不是同一份代码，老的长 search 一条都没命中。
                try
                {
                    var marks = new (string Code, string Needle, string What)[]
                    {
                        ("gml_Object_obj_darkcontroller_Draw_0", "ntl_settings_row_draw", L("设置菜单「Mod 设置」行")),
                        ("gml_Object_obj_darkcontroller_Draw_0", "ntl_cfg_scroll(", L("设置菜单滚动窗口")),
                        ("gml_Object_obj_darkcontroller_Draw_0", "ntl_cfg_row(", L("设置菜单行可见性")),
                        ("gml_Object_obj_darkcontroller_Draw_0", "ntl_cfg_scrollbar_draw", L("设置菜单滚动条")),
                        ("gml_Object_obj_darkcontroller_Draw_0", "ntl_modmenu_page_draw", L("Mod 设置面板绘制")),
                        ("gml_Object_obj_darkcontroller_Step_0", "ntl_settings_row_press", L("Mod 设置行按键分派")),
                        ("gml_Object_obj_darkcontroller_Step_0", "ntl_modmenu_page_step", L("Mod 设置面板输入")),
                    };
                    var cache = new Dictionary<string, string>(StringComparer.Ordinal);
                    var missing = new List<string>();
                    var noCode = new List<string>();
                    int checkedMarks = 0;
                    foreach (var mk in marks)
                    {
                        if (!cache.TryGetValue(mk.Code, out var txt))
                        {
                            txt = d.Code.ByName(mk.Code) == null ? "" : Decompiled(mk.Code, out _);
                            cache[mk.Code] = txt;
                        }
                        if (txt.Length == 0)
                        {
                            if (!noCode.Contains(mk.Code)) noCode.Add(mk.Code);
                            continue;
                        }
                        checkedMarks++;
                        if (!txt.Contains(mk.Needle, StringComparison.Ordinal))
                            missing.Add(mk.What + L("（缺 ") + mk.Needle + L("）"));
                    }
                    bool gok = missing.Count == 0;
                    string gdetail;
                    if (noCode.Count > 0 && checkedMarks == 0)
                        gdetail = L("产物里没有 ") + string.Join(L("、"), noCode) + L("（该产物无暂停菜单，跳过此项）");
                    else if (gok)
                        gdetail = checkedMarks + L(" 项标记全部命中（") + string.Join(L("、"), marks.Select(m => m.What)) + L("）");
                    else
                        gdetail = L("缺失: ") + string.Join(L("；"), missing)
                            + (noCode.Count > 0 ? L("；产物里没有 ") + string.Join(L("、"), noCode) : "");
                    Check("(g)", L("界面增强：设置菜单/面板补丁落在产物里"), gok, gdetail);
                }
                catch (Exception gex)
                {
                    Check("(g)", L("界面增强：设置菜单/面板补丁落在产物里"), false, L("检查抛异常 —— ") + Describe(gex));
                }
                bool countOk = apiNames.Count >= MinApiScripts;
                Check("(d)", L("API 脚本数量（gml_Script_ntl_* ≥ ") + MinApiScripts + L("）"), countOk,
                    countOk
                        ? apiNames.Count + L(" 个")
                        : apiNames.Count + L(" 个，少于下限 ") + MinApiScripts + L("（疑似注入被截断）；尾部样本: ") + Sample(apiNames, 3));
            }

            CheckRegistryJson();
            CheckProductJsonEscapes();
        }
        catch (Exception ex)
        {
            fails++;
            Console.WriteLine(L("  [失败] 自检本身抛异常 —— ") + Describe(ex));
        }

        Console.WriteLine(fails == 0
            ? L("结果: 全部通过（用时 ") + total.ElapsedMilliseconds + L(" ms）")
            : L("结果: 发现严重问题（失败 ") + fails + L(" 项，用时 ") + total.ElapsedMilliseconds + L(" ms）"));
        if (total.ElapsedMilliseconds > 3000)
            Console.WriteLine(L("  [提示] 耗时大头是重新打开产物 data.win（") + loadMs + L(" ms，229MB 的 data.win 解析本身就要数秒），其余检查合计 ")
                + Math.Max(0, total.ElapsedMilliseconds - loadMs) + L(" ms；产物只 Load 一次。"));
        return fails == 0 ? 0 : 1;
    }

    /// <summary>默认引导条目（与 Program.BootCodeName 一致；bootCodeName 传空时的兜底）。</summary>
    private static string DefaultBootCodeName(string chapter) =>
        chapter.Equals("root", StringComparison.OrdinalIgnoreCase)
            ? "gml_Object_obj_init_pc_Create_0"
            : "gml_Object_obj_initializer2_Create_0";

    /// <summary>产物路径：章节名 → &lt;游戏根&gt;/&lt;chapter&gt;_windows/data.win；
    /// 直接给目录或 data.win 路径（平行时间线产物）时按路径解析。</summary>
    private static string ResolveProductDataWin(string gameRoot, string chapter)
    {
        if (chapter.IndexOf('\\') >= 0 || chapter.IndexOf('/') >= 0 || Path.IsPathRooted(chapter))
        {
            var full = Path.GetFullPath(chapter);
            return full.EndsWith(".win", StringComparison.OrdinalIgnoreCase) ? full : Path.Combine(full, "data.win");
        }
        return Paths.ChapterDataWin(gameRoot, chapter);
    }

    /// <summary>取某类事件（Create/Step/Draw…）在控制器对象上的绑定情况。
    /// obj.Events 是「按 EventType 分组」的外层表，外层下标 = (int)EventType。</summary>
    private static List<(uint Sub, bool Bound, string Code, int Instr)> BoundEvents(UndertaleGameObject obj, EventType type)
    {
        var res = new List<(uint, bool, string, int)>();
        int idx = (int)type;
        if (obj.Events == null || idx < 0 || idx >= obj.Events.Count) return res;
        var inner = obj.Events[idx];
        if (inner == null) return res;
        foreach (var ev in inner)
        {
            var code = (ev.Actions != null && ev.Actions.Count > 0) ? ev.Actions[0].CodeId : null;
            int instr = code?.Instructions?.Count ?? 0;
            res.Add((ev.EventSubtype, code != null && instr > 0, code?.Name?.Content ?? L("(无代码)"), instr));
        }
        return res;
    }

    private static string FmtEvents(List<(uint Sub, bool Bound, string Code, int Instr)> list)
    {
        if (list.Count == 0) return L("无事件");
        return string.Join(" + ", list.Select(e => "subtype " + e.Sub + " → " + e.Code + "/" + e.Instr + L(" 指令") + (e.Bound ? "" : L("（空）"))));
    }

    private static int JsonCount(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Array => el.GetArrayLength(),
        JsonValueKind.Object => el.EnumerateObject().Count(),
        _ => 0
    };

    private static string Sample(List<string> names, int n) =>
        names.Count == 0 ? L("(一个都没有)") : string.Join(", ", names.Skip(Math.Max(0, names.Count - n)));

    private static string Mb(long bytes) => (bytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture);

    private static int CountHits(string text, string needle)
    {
        int n = 0, i = 0;
        while ((i = text.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }

    private static string Describe(Exception ex)
    {
        var msg = (ex.Message ?? "").Replace("\r", " ").Replace("\n", " ");
        if (msg.Length > 300) msg = msg.Substring(0, 300) + "…";
        return ex.GetType().Name + ": " + msg;
    }
}
