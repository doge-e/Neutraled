using UndertaleModLib;
using UndertaleModLib.Models;
using UndertaleModLib.Decompiler;
using Underanalyzer.Decompiler;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>
/// 源码级差异层 —— 把"整包 data.win 型"mod 变成**可叠加的源码补丁层**。
///
/// 为什么需要它：整包 mod 之间天然互斥（一章一个 data.win 基底）。想共存就得合并，
/// 而字节码级合并会死在"资源池索引不可移植"上（Deltamod/GM3P 打包会重建资源池，
/// 实测字符串 +278 / 函数 +8 / 变量 +252，所有索引型操作数全体位移）。
///
/// 本模块换一条路：**不搬字节码，搬源码**。
///   1) 指令级差异 → 候选集合（快，但含大量"索引位移"噪声）
///   2) 反编译候选对象 → **比较反编译文本**。索引位移不会改变反编译文本（索引已被解析成名字），
///      只有真实语义改动才会 → 噪声自动过滤掉
///   3) 真实改动的对象 → 导出成 GML 源码 + mod.json 的 patches 段
///      → 部署时由**目标基底自己重新编译**，索引由目标分配，索引问题彻底消失
///
/// 产出：
///   mods/NAME/AUTHOR/chapterN/mod.json      （patches 列表，不带 references.assets）
///   mods/NAME/AUTHOR/chapterN/patches/*.gml （每个改动对象一个文件）
///   mods/NAME/AUTHOR/chapterN/LAYER_REPORT.txt
/// </summary>
public static class LayerFromBase
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static int Extract(string gameRoot, string sourceWin, string? chapter, string? name, string? author,
        string? baselineOverride = null, bool dryRun = false, int maxObjects = 4000, bool verbose = false)
    {
        Console.WriteLine(L("===== 源码级差异层提取（--layer-from-base）====="));
        if (!File.Exists(sourceWin)) { Console.WriteLine(L("[错误] 找不到: ") + sourceWin); return 1; }

        chapter ??= DiffLayer.InferChapterFromPath(sourceWin);
        if (chapter == null) { Console.WriteLine(L("[错误] 无法推断章节，请用 --chapter chapterN")); return 1; }
        chapter = chapter.ToLowerInvariant();

        var baselinePath = baselineOverride ?? Paths.BackupDataWin(gameRoot, chapter);
        if (!File.Exists(baselinePath)) { Console.WriteLine(L("[错误] 缺少官方基线: ") + baselinePath); return 1; }

        Console.WriteLine(L("  源:   ") + sourceWin);
        Console.WriteLine(L("  基线: ") + baselinePath);
        Console.WriteLine(L("  章节: ") + chapter);

        var baseData = Injector.Load(baselinePath);
        var modData = Injector.Load(sourceWin);

        // ---------- 1) 指令级差异 → 候选 ----------
        var baseByName = new Dictionary<string, UndertaleCode>(StringComparer.Ordinal);
        foreach (var c in baseData.Code)
        {
            var n = c.Name?.Content;
            if (!string.IsNullOrEmpty(n)) baseByName[n] = c;
        }

        var candidates = new List<(string name, UndertaleCode mod, UndertaleCode? bas)>();
        int addedCount = 0, changedCount = 0, metaOnly = 0, childSkipped = 0;
        foreach (var c in modData.Code)
        {
            var n = c.Name?.Content;
            if (string.IsNullOrEmpty(n)) continue;
            // ★ 子条目（函数内部的嵌套 struct/闭包/匿名函数）不单独产出：它们的源码由父条目的反编译
            //   文本一起输出（实测 DOJO：gml_Script____struct___1_... 与 gml_Script_dj_xxx 都是子条目，
            //   单独反编译时报 "Expected code entry to be root level"）。
            if (c.ParentEntry != null) { childSkipped++; continue; }
            if (!baseByName.TryGetValue(n, out var b)) { candidates.Add((n, c, null)); addedCount++; continue; }
            // 只差局变表等元数据 → 不是行为改动，不进候选（否则 60fps 会虚报 6000+ 个）
            if (PreWriteRepairs.SameInstructions(b, c)) { metaOnly++; continue; }
            candidates.Add((n, c, b)); changedCount++;
        }
        Console.WriteLine(L("  指令级候选: 改动 {0} + 新增 {1} = {2}（另排除 {3} 个纯元数据差异、{4} 个子条目）", changedCount, addedCount, candidates.Count, metaOnly, childSkipped));

        if (candidates.Count > maxObjects)
        {
            Console.WriteLine(L("  [警告] 候选超过上限 {0}，只处理前 {1} 个（其余见报告）", maxObjects, maxObjects));
            candidates = candidates.Take(maxObjects).ToList();
        }

        // ---------- 2) 反编译 + 源码文本比对（噪声过滤器）----------
        var gctxBase = new GlobalDecompileContext(baseData);
        var gctxMod = new GlobalDecompileContext(modData);

        var real = new List<(string name, string source)>();
        var failed = new List<(string name, string reason)>();
        var samples = new List<(string name, string diff)>();
        var noise = 0;

        for (int i = 0; i < candidates.Count; i++)
        {
            var (nm, modCode, baseCode) = candidates[i];
            if ((i + 1) % 500 == 0) Console.WriteLine(L("    ...已处理 {0}/{1}", i + 1, candidates.Count));

            string src;
            try { src = Decompile(gctxMod, modCode); }
            catch (Exception ex) { failed.Add((nm, Short(ex.Message))); continue; }
            if (string.IsNullOrWhiteSpace(src)) { failed.Add((nm, "反编译结果为空")); continue; }

            if (baseCode == null) { real.Add((nm, src)); continue; }   // 新增对象：本来就是真改动

            string baseSrc;
            try { baseSrc = Decompile(gctxBase, baseCode); }
            catch (Exception ex) { failed.Add((nm, "基线反编译失败: " + Short(ex.Message))); continue; }

            var ns = Normalize(src); var nb = Normalize(baseSrc);
            if (ns != nb)
            {
                real.Add((nm, src));
                if (samples.Count < 5) samples.Add((nm, FirstDiff(nb, ns)));
                if (verbose && real.Count <= 12)
                    Console.WriteLine(L("    [真改动] {0}（{1} 字符）", nm, src.Length));
            }
            else noise++;
        }

        Console.WriteLine(L("  源码级过滤: 真实改动 {0} / 索引噪声 {1} / 反编译失败 {2}", real.Count, noise, failed.Count));

        // ---------- 2.5) 新增房间 ----------
        //   ★ 房间在指令级 diff 里**不出现**（ROOM 段的资源，不是代码条目），所以早先 dojo 转换报
        //     「无法表达 0 个」却仍缺 room_dojo —— 拿代码 diff 根本看不见它。这里显式比较房间名集合。
        //   基线已有的房间不碰；新增的只表达**空房间**（入口型 mod 的房间就是一块画布，场地全靠
        //   对象自己的 Draw 画），有实例/图块/层/创建代码/启用背景的房间要完整的房间序列化 ⇒ 报告。
        var roomDecls = new List<Dictionary<string, object?>>();
        var roomRich = new List<string>();
        foreach (var r in modData.Rooms)
        {
            var rn = r.Name?.Content;
            if (string.IsNullOrEmpty(rn)) continue;
            if (baseData.Rooms.Any(b => string.Equals(b.Name?.Content, rn, StringComparison.Ordinal))) continue;
            int inst = r.GameObjects?.Count ?? 0, tiles = r.Tiles?.Count ?? 0, layers = r.Layers?.Count ?? 0;
            int bgOn = r.Backgrounds?.Count(b => b.Enabled) ?? 0;
            bool hasCode = r.CreationCodeId != null;
            if (inst > 0 || tiles > 0 || layers > 0 || bgOn > 0 || hasCode)
            {
                roomRich.Add($"  {rn}  实例 {inst} / 图块 {tiles} / 层 {layers} / 启用背景 {bgOn} / 创建代码 {(hasCode ? "有" : "无")}");
                continue;
            }
            var rd = new Dictionary<string, object?>
            {
                ["name"] = rn,
                ["width"] = r.Width,
                ["height"] = r.Height,
                ["speed"] = r.Speed,
                ["persistent"] = r.Persistent,
                ["flags"] = (int)r.Flags,
                ["backgroundColor"] = (long)r.BackgroundColor,
                ["drawBackgroundColor"] = r.DrawBackgroundColor
            };
            var vws = new List<Dictionary<string, object?>>();
            if (r.Views != null)
                for (int vi = 0; vi < r.Views.Count; vi++)
                {
                    var v = r.Views[vi];
                    if (!v.Enabled) continue;
                    var vd = new Dictionary<string, object?>
                    {
                        ["index"] = vi,
                        ["portX"] = v.PortX, ["portY"] = v.PortY,
                        ["portWidth"] = v.PortWidth, ["portHeight"] = v.PortHeight,
                        ["viewX"] = v.ViewX, ["viewY"] = v.ViewY,
                        ["viewWidth"] = v.ViewWidth, ["viewHeight"] = v.ViewHeight,
                        ["speedX"] = v.SpeedX, ["speedY"] = v.SpeedY,
                        ["borderX"] = v.BorderX, ["borderY"] = v.BorderY
                    };
                    var vo = v.ObjectId?.Name?.Content;
                    if (!string.IsNullOrEmpty(vo)) vd["object"] = vo;
                    vws.Add(vd);
                }
            if (vws.Count > 0) rd["views"] = vws;
            roomDecls.Add(rd);
        }
        if (roomDecls.Count > 0) Console.WriteLine(L("  新增房间: {0} 个（空房间，写入 mod.json 的 rooms 段）", roomDecls.Count));
        if (roomRich.Count > 0) Console.WriteLine(L("  [房间有内容] {0} 个新增房间带实例/图块/层，暂时表达不了（见报告）", roomRich.Count));

        if (real.Count == 0 && roomDecls.Count == 0 && roomRich.Count == 0)
        {
            Console.WriteLine(L("[结论] 相对基线没有真实语义改动（差异全是索引位移噪声），无需生成层"));
            return 0;
        }

        // ---------- 3) 产出 ----------
        var layerName = string.IsNullOrWhiteSpace(name) ? "layer_" + Path.GetFileName(Path.GetDirectoryName(sourceWin) ?? "mod") : name!;
        var layerAuthor = string.IsNullOrWhiteSpace(author) ? "converted" : author!;
        var id = "layer." + DeltaImport.Sanitize(layerName) + "." + DeltaImport.Sanitize(layerAuthor);
        var modRoot = Path.Combine(Paths.NeutraledRoot(gameRoot), "mods", DeltaImport.Sanitize(layerName), DeltaImport.Sanitize(layerAuthor), chapter);

        Console.WriteLine(L("  层: {0} / {1} / {2}  id={3}", layerName, layerAuthor, chapter, id));
        if (dryRun) { Console.WriteLine(L("  [dry-run] 不写盘")); return 0; }

        var patchDir = Path.Combine(modRoot, "patches");
        var gmlDir = Path.Combine(modRoot, "gml");
        var objectsDir = Path.Combine(modRoot, "objects");   // 新增对象的事件源码（mod.json 的 objects 段引用它）
        Directory.CreateDirectory(patchDir);
        Directory.CreateDirectory(gmlDir);

        // 两类产物走**不同机制**（这是实测踩出来的）：
        //   改动（目标里已存在）→ patches：整脚本覆盖，目标必须存在
        //   新增（目标里没有）  → gml/：由 Injector 作为**新脚本**导入（gml_Script_<名字>）
        //                       注意只有 gml_Script_* 能这样表达；gml_Object_* 事件等没有对应机制 → 进报告
        var patches = new List<Dictionary<string, object?>>();
        var newScripts = new List<string>();
        // 新增对象（含既有对象补挂的新事件）→ mod.json 的 objects 段；键 = 对象名
        var objByName = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        var objEventCount = 0;
        var missingRefs = new List<string>();        // 新对象引用、但基线里没有的资源（精灵/父对象/遮罩）
        var headDiffs = new List<string>();          // 已存在对象的头部差异（层表达不了，部署时用基底的值）
        var headChecked = new HashSet<string>(StringComparer.Ordinal);   // 头部只比对一次
        var unexpressible = new List<string>();
        var conflicts = new List<string>();          // 与 Neutraled 内置补丁撞车 → 写 patch_skip
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (nm, src) in real.OrderBy(x => x.name, StringComparer.Ordinal))
        {
            bool isAdded = !baseByName.ContainsKey(nm);
            if (isAdded)
            {
                // ★ 两种前缀都算「新脚本」：gml_Script_xxx 与 gml_GlobalScript_xxx（DELTARUNE 的函数库
                //   多数以后者存储 —— DOJO 的 150 个 dj_* 全是 gml_GlobalScript_*，早期只认前者 ⇒ 全进"无法表达"）。
                var stripped = StripScriptPrefix(nm);
                if (stripped != null)
                {
                    var file = SafeFileName(stripped) + ".gml";
                    var kk = 1;
                    while (!used.Add("g:" + file)) { file = SafeFileName(stripped) + "_" + (kk++) + ".gml"; }
                    File.WriteAllText(Path.Combine(gmlDir, file), src);
                    newScripts.Add(nm);
                    continue;
                }
                // ★ 新增对象事件（dojo 的 obj_dojo_Create_0 这类）→ mod.json 的 objects 段：
                //   Injector 2.6 段会在 data.win 里真新建/复用对象，并把这段源码编译成事件代码挂上去。
                //   判定用「代码条目名 + 对象确实存在于 mod 数据」两道：名字对得上但对象不存在的条目
                //   （例如新增房间的 Create 代码）不能瞎猜，仍进「无法表达」。
                if (ObjectEvents.TrySplitCodeName(nm, out var ownerName, out var evType, out var evSub)
                    && modData.GameObjects.ByName(ownerName) != null)
                {
                    var of = nm + ".gml";    // 代码条目名当文件名：唯一，且能一眼对上 data.win 里的条目
                    Directory.CreateDirectory(objectsDir);   // 只在真有新增对象时才建目录（避免空目录噪声）
                    File.WriteAllText(Path.Combine(objectsDir, of), src);
                    var decl = GetObjectDecl(objByName, ownerName);
                    ((List<Dictionary<string, object?>>)decl["events"]!).Add(new Dictionary<string, object?>
                    {
                        ["type"] = evType,
                        ["subtype"] = evSub,
                        ["file"] = "objects/" + of
                    });
                    objEventCount++;

                    // ★ 头部字段只在「对象在基线里不存在」（= 真新建）时才写；已存在对象只补事件，头部保持基底原样
                    //   （否则会把基线对象的头部按 mod 的值重写，等于偷偷改了一个我们并不拥有的对象）。
                    var baseObj = baseData.GameObjects.ByName(ownerName);
                    if (baseObj == null)
                    {
                        if (!decl.ContainsKey("visible"))
                        {
                            var mo = modData.GameObjects.ByName(ownerName)!;
                            decl["visible"] = mo.Visible;
                            decl["solid"] = mo.Solid;
                            decl["persistent"] = mo.Persistent;
                            decl["depth"] = mo.Depth;
                            var spName = mo.Sprite?.Name?.Content;
                            var paName = mo.ParentId?.Name?.Content;
                            var mkName = mo.TextureMaskId?.Name?.Content;
                            if (!string.IsNullOrEmpty(spName)) decl["sprite"] = spName;
                            if (!string.IsNullOrEmpty(paName)) decl["parent"] = paName;
                            if (!string.IsNullOrEmpty(mkName)) decl["mask"] = mkName;

                            // 资源缺口：新对象引用的精灵/父对象/遮罩，基线里没有
                            // ⇒ 该层必须自带 sprites/ 资源包（--export-packs），否则对象「存在但画不出来」。
                            if (!string.IsNullOrEmpty(spName) && baseData.Sprites.ByName(spName!) == null)
                                missingRefs.Add(ownerName + " 的精灵 " + spName);
                            if (!string.IsNullOrEmpty(paName) && baseData.GameObjects.ByName(paName!) == null)
                                missingRefs.Add(ownerName + " 的父对象 " + paName);
                            if (!string.IsNullOrEmpty(mkName) && baseData.Sprites.ByName(mkName!) == null)
                                missingRefs.Add(ownerName + " 的遮罩 " + mkName);
                        }
                    }
                    else if (headChecked.Add(ownerName))
                    {
                        // 已存在对象：层只能补事件，表达不了头部改动 ⇒ 逐字段比对，有差异就报告（不静默丢）
                        var mo = modData.GameObjects.ByName(ownerName)!;
                        var diffs = new List<string>();
                        if (mo.Visible != baseObj.Visible) diffs.Add($"visible {baseObj.Visible}→{mo.Visible}");
                        if (mo.Solid != baseObj.Solid) diffs.Add($"solid {baseObj.Solid}→{mo.Solid}");
                        if (mo.Persistent != baseObj.Persistent) diffs.Add($"persistent {baseObj.Persistent}→{mo.Persistent}");
                        if (mo.Depth != baseObj.Depth) diffs.Add($"depth {baseObj.Depth}→{mo.Depth}");
                        var s1 = mo.Sprite?.Name?.Content; var s0 = baseObj.Sprite?.Name?.Content;
                        if (!string.Equals(s1, s0, StringComparison.Ordinal)) diffs.Add($"sprite {s0 ?? "(无)"}→{s1 ?? "(无)"}");
                        var p1 = mo.ParentId?.Name?.Content; var p0 = baseObj.ParentId?.Name?.Content;
                        if (!string.Equals(p1, p0, StringComparison.Ordinal)) diffs.Add($"parent {p0 ?? "(无)"}→{p1 ?? "(无)"}");
                        var m1 = mo.TextureMaskId?.Name?.Content; var m0 = baseObj.TextureMaskId?.Name?.Content;
                        if (!string.Equals(m1, m0, StringComparison.Ordinal)) diffs.Add($"mask {m0 ?? "(无)"}→{m1 ?? "(无)"}");
                        if (diffs.Count > 0) headDiffs.Add(ownerName + "：" + string.Join("，", diffs));
                    }
                    continue;
                }
                unexpressible.Add(nm);   // 其它新增条目（序列/别的新资源、以及不属于已知对象的代码条目）：patch 与 gml 都表达不了
                continue;
            }

            // ★ 与 Neutraled 内置补丁撞车的目标：Neutraled 自己对这几个对象做 find/replace
            //   （设置菜单「Mod 设置」行/取模/按键分派、存档菜单循环、音频钩子…），整脚本覆盖会把那些
            //   注入点一起冲掉 —— 实测 60 FPS 层的 obj_darkcontroller_Draw_0 覆盖后，
            //   设置菜单里「Mod 设置」行与「Return to Title」叠在同一个 y（395 处堆了 4 行）。
            //   ⇒ 不产出 patch 文件，改写入 mod.json 的 patch_skip（部署时 Injector 会打印 [跳过]）。
            if (Injector.IsReservedPatchTarget(nm)) { conflicts.Add(nm); continue; }

            var pf = SafeFileName(nm) + ".gml";
            var k = 1;
            while (!used.Add("p:" + pf)) { pf = SafeFileName(nm) + "_" + (k++) + ".gml"; }
            File.WriteAllText(Path.Combine(patchDir, pf), src);
            patches.Add(new Dictionary<string, object?> { ["target"] = nm, ["file"] = "patches/" + pf });
        }

        var modJson = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["name"] = layerName,
            ["author"] = layerAuthor,
            ["version"] = "1.0.0",
            ["enabled"] = true,
            ["description"] = $"源码级差异层：由 {Path.GetFileName(sourceWin)} 相对官方基线反编译提取（改动 {patches.Count} 个对象 → patches；新增脚本 {newScripts.Count} 个 → gml/"
                + (objByName.Count > 0 ? $"；新增对象 {objByName.Count} 个 / 事件 {objEventCount} 个 → objects/" : "")
                + (roomDecls.Count > 0 ? $"；新增房间 {roomDecls.Count} 个 → rooms" : "")
                + (conflicts.Count > 0 ? $"；{conflicts.Count} 个目标与 Neutraled 内置补丁撞车 → patch_skip" : ""),
            ["patches"] = patches
        };
        if (conflicts.Count > 0) modJson["patch_skip"] = conflicts;
        // 新增对象（含既有对象补挂的新事件）→ Injector 2.6 段据此在 data.win 里真新建对象
        if (objByName.Count > 0) modJson["objects"] = objByName.Values.ToList();
        // 新增房间 → Injector 2.7 段建空房间并追加进 RoomOrder（room_goto 的索引就是 RoomOrder 的下标）
        if (roomDecls.Count > 0) modJson["rooms"] = roomDecls;
        File.WriteAllText(Path.Combine(modRoot, "mod.json"), JsonSerializer.Serialize(modJson, JsonOpts));

        var report = new System.Text.StringBuilder();
        report.AppendLine("===== 源码级差异层报告 =====");
        report.AppendLine("源:      " + sourceWin);
        report.AppendLine("基线:    " + baselinePath);
        report.AppendLine("章节:    " + chapter);
        report.AppendLine("指令级候选: 改动 " + changedCount + " / 新增 " + addedCount + "（已排除 " + metaOnly + " 个纯元数据差异）");
        report.AppendLine("源码级过滤: 真实改动 " + real.Count + " / 索引噪声 " + noise + " / 失败 " + failed.Count);
        report.AppendLine();
        if (samples.Count > 0)
        {
            report.AppendLine("--- 改动样例（基线 → 改后，各取第一处差异行）---");
            foreach (var (nm, d) in samples) { report.AppendLine("  " + nm); report.AppendLine("    " + d); }
            report.AppendLine();
        }
        if (objByName.Count > 0)
        {
            report.AppendLine("--- 新增对象 / 新事件（已写入 mod.json 的 objects 段；Injector 2.6 段据此新建对象或补挂事件）---");
            foreach (var (on, decl) in objByName.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                report.AppendLine("  " + on
                    + (decl.ContainsKey("sprite") ? "  精灵 " + decl["sprite"] : "")
                    + (decl.ContainsKey("parent") ? "  父对象 " + decl["parent"] : "")
                    + (decl.ContainsKey("visible")
                        ? "  [新建] visible=" + decl["visible"] + " solid=" + decl["solid"] + " persistent=" + decl["persistent"] + " depth=" + decl["depth"]
                        : "  [基线已有该对象，只补事件]"));
                foreach (var ev in (List<Dictionary<string, object?>>)decl["events"]!)
                    report.AppendLine("    " + ev["type"] + "(" + ev["subtype"] + ") ← " + ev["file"]);
            }
            report.AppendLine();
        }
        if (roomDecls.Count > 0)
        {
            report.AppendLine("--- 新增房间（已写入 mod.json 的 rooms 段；Injector 2.7 段建空房间并追加到 RoomOrder 末尾）---");
            report.AppendLine("    房间在指令级 diff 里不出现（ROOM 段资源，不是代码条目）⇒ 早先版本会漏掉它，");
            report.AppendLine("    而入口型 mod（dojo 的 room_goto(147)）必须靠它落地。索引 = 它在 RoomOrder 里的下标。");
            foreach (var rd in roomDecls)
                report.AppendLine("  " + rd["name"] + "  " + rd["width"] + "x" + rd["height"]
                    + "  速度 " + rd["speed"] + "  持久 " + rd["persistent"] + "  标志 " + rd["flags"]
                    + ((rd.ContainsKey("views") ? "  启用视图 " + ((List<Dictionary<string, object?>>)rd["views"]!).Count + " 个" : "")));
            report.AppendLine();
        }
        if (roomRich.Count > 0)
        {
            report.AppendLine("--- 新增房间（有内容，层暂时表达不了）---");
            report.AppendLine("    层只会建**空房间**：有实例/图块/层/创建代码/启用背景的房间需要完整的房间序列化，");
            report.AppendLine("    目前只能靠整包基底表达。下面这些房间在部署后**不存在**（room_goto 会失败）。");
            foreach (var x in roomRich) report.AppendLine(x);
            report.AppendLine();
        }
        if (missingRefs.Count > 0)
        {
            report.AppendLine("--- 资源缺口（新对象引用了基线里没有的资源）---");
            report.AppendLine("    这些对象在部署后会是「存在但画不出来/找不到父对象」的状态；");
            report.AppendLine("    该层需要自带资源包（Neutraled\\builder --export-packs 导出 sprites/），或确认基底里确实有这些名字。");
            foreach (var x in missingRefs) report.AppendLine("  " + x);
            report.AppendLine();
        }
        if (headDiffs.Count > 0)
        {
            report.AppendLine("--- 头部差异（已存在对象的头部改动，层表达不了）---");
            report.AppendLine("    层只给已存在的对象补事件，不会改它的 visible/solid/persistent/depth/精灵/父对象/遮罩；");
            report.AppendLine("    下面这些字段在部署后会保持**基底**的值（= 该改动丢失）。要紧的话请把该对象放进整包基底 mod。");
            foreach (var x in headDiffs) report.AppendLine("  " + x);
            report.AppendLine();
        }
        if (unexpressible.Count > 0)
        {
            report.AppendLine("--- 无法表达的新增条目（前 100）---");
            report.AppendLine("    这些是 mod 新增的、且不是 gml_Script_*/gml_GlobalScript_*、也不属于任何**已知对象**的条目");
            report.AppendLine("    （例如新增房间、序列、别的资源表）。patch 需要目标已存在、gml/ 只能加脚本、objects 段只管对象事件");
            report.AppendLine("    → 目前只能靠整包基底表达。");
            foreach (var n in unexpressible.Take(100)) report.AppendLine("  " + n);
            if (unexpressible.Count > 100) report.AppendLine("  ...（其余 " + (unexpressible.Count - 100) + " 个省略）");
            report.AppendLine();
        }
        if (conflicts.Count > 0)
        {
            report.AppendLine("--- 与 Neutraled 内置补丁撞车（已写入 mod.json 的 patch_skip，这部分改动**不生效**）---");
            report.AppendLine("    Neutraled 自己会 find/replace 这些代码对象（设置菜单「Mod 设置」行 / 取模 / 按键分派、");
            report.AppendLine("    存档菜单循环、音频钩子…）。整脚本覆盖会把那些注入点一起冲掉（真机事故：设置菜单里");
            report.AppendLine("    「Mod 设置」行与「Return to Title」叠在同一个 y）⇒ 层主动放弃这些目标的改动。");
            foreach (var n in conflicts.OrderBy(x => x, StringComparer.Ordinal)) report.AppendLine("  " + n);
            report.AppendLine();
        }
        report.AppendLine("--- 真实改动对象（已生成 patch）---");
        foreach (var (nm, src) in real.OrderBy(x => x.name)) report.AppendLine("  " + nm + "  (" + src.Length + " 字符)");
        if (failed.Count > 0)
        {
            report.AppendLine();
            report.AppendLine("--- 反编译失败（这些对象的改动**不会**生效，需要单独处理）---");
            foreach (var (nm, rs) in failed.Take(200)) report.AppendLine("  " + nm + "  ← " + rs);
            if (failed.Count > 200) report.AppendLine("  ...（其余 " + (failed.Count - 200) + " 个省略）");
        }
        report.AppendLine();
        report.AppendLine("说明：本层不含 references.assets，因此**不参与基底竞争**，可叠加在任意基底上。");
        report.AppendLine("      若与别的 mod 改同一对象，部署时的冲突报告会指出；必要时用 patch_skip 排除。");
        File.WriteAllText(Path.Combine(modRoot, "LAYER_REPORT.txt"), report.ToString());

        Console.WriteLine(L("  产物: ") + modRoot);
        Console.WriteLine(L("    patches/  {0} 个 .gml（覆盖已存在对象）", patches.Count));
        Console.WriteLine(L("    gml/      {0} 个 .gml（作为新脚本导入）", newScripts.Count));
        if (objByName.Count > 0)
            Console.WriteLine(L("    objects/  {0} 个 .gml（{1} 个新增对象 / {2} 个事件，写入 mod.json 的 objects 段）", objEventCount, objByName.Count, objEventCount));
        if (roomDecls.Count > 0)
            Console.WriteLine(L("    rooms      {0} 个空房间（写入 mod.json 的 rooms 段；Injector 2.7 段建房间 + 追加 RoomOrder）", roomDecls.Count));
        if (missingRefs.Count > 0)
            Console.WriteLine(L("    [资源缺口] {0} 处新对象引用的资源基线里没有（见报告；该层需要自带 sprites/ 资源包）", missingRefs.Count));
        if (unexpressible.Count > 0)
            Console.WriteLine(L("    [无法表达] {0} 个新增资源（非脚本条目，patch/gml 都覆盖不到，见报告）", unexpressible.Count));
        if (conflicts.Count > 0)
            Console.WriteLine(L("    [撞车] {0} 个目标与 Neutraled 内置补丁冲突（已写入 patch_skip，改动不生效）", conflicts.Count));
        Console.WriteLine("    LAYER_REPORT.txt");
        if (failed.Count > 0) Console.WriteLine(L("  [注意] {0} 个对象反编译失败，改动不会生效（见报告）", failed.Count));
        return 0;
    }

    /// <summary>取（或新建）某个对象的 objects 声明骨架。键顺序固定，方便人读报告与 diff。</summary>
    private static Dictionary<string, object?> GetObjectDecl(Dictionary<string, Dictionary<string, object?>> map, string objectName)
    {
        if (map.TryGetValue(objectName, out var d)) return d;
        d = new Dictionary<string, object?>
        {
            ["name"] = objectName,
            ["events"] = new List<Dictionary<string, object?>>()
        };
        map[objectName] = d;
        return d;
    }

    private static string Decompile(GlobalDecompileContext gctx, UndertaleCode code)
    {
        // ⚠ 第 3 个参数是 IDecompileSettings，不是父条目（写 code.ParentEntry 编译不过 CS1503）。
        //   子条目（父条目里的嵌套函数/struct 构造器）由**父条目**的反编译文本内联输出，
        //   单独反编译会抛 "Expected code entry to be root level." —— 所以调用方直接跳过子条目。
        var dctx = new Underanalyzer.Decompiler.DecompileContext(gctx, code, null!);
        return dctx.DecompileToString();
    }

    /// <summary>gml_Script_xxx / gml_GlobalScript_xxx → xxx；不是脚本条目则返回 null。</summary>
    private static string? StripScriptPrefix(string nm)
    {
        foreach (var sp in new[] { "gml_GlobalScript_", "gml_Script_" })
            if (nm.StartsWith(sp, StringComparison.Ordinal)) return nm[sp.Length..];
        return null;
    }

    /// <summary>归一化反编译文本：索引位移不影响文本（索引已解析成名字），只有真实改动才会不同。</summary>
    private static string Normalize(string src)
    {
        var lines = src.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        return string.Join("\n", lines.Select(l => l.TrimEnd())).Trim();
    }

    private static string SafeFileName(string s)
    {
        var t = Regex.Replace(s, @"[^\w\-]+", "_").Trim('_');
        if (t.Length > 80) t = t[..80];
        return t.Length == 0 ? "code" : t;
    }

    private static string Short(string s) => s.Length <= 120 ? s : s[..120] + "...";

    /// <summary>取两份源码的第一处差异行，便于人工核对"这真的是语义改动吗"。</summary>
    private static string FirstDiff(string a, string b)
    {
        var la = a.Split('\n'); var lb = b.Split('\n');
        int n = Math.Min(la.Length, lb.Length);
        for (int i = 0; i < n; i++)
            if (la[i] != lb[i]) return $"第 {i + 1} 行: 基线「{Clip(la[i])}」 → 改后「{Clip(lb[i])}」";
        return $"行数不同: 基线 {la.Length} 行 / 改后 {lb.Length} 行";
    }

    private static string Clip(string s) { s = s.Trim(); return s.Length <= 90 ? s : s[..90] + "..."; }
}
