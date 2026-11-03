using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using UndertaleModLib.Models;

namespace NtlMapEdit;

/// <summary>
/// 「原版交互/对话」扫描器自检：证明能从真实 data.win 里反编译对象事件、抽 global.msg 行、
/// 三态判定条件、并解析 lang 文本。只读源文件，不产生任何写盘副作用（可选 outJson 报告）。
/// </summary>
public static class VanillaScanSelfTest
{
    private static int _fails;
    private static readonly List<string> _lines = new();

    private static void Check(bool ok, string name)
    {
        if (!ok) _fails++;
        Console.WriteLine((ok ? "  [OK] " : "  [FAIL] ") + name);
    }

    private static void Info(string s) => Console.WriteLine("  " + s);

    public static int Run(string srcPath, string? outJson)
    {
        _fails = 0;
        _lines.Clear();
        Console.WriteLine("VANILLA-SCAN-TEST: " + srcPath);
        using var gd = new GameData(srcPath);
        var data = gd.Data;
        var vc = new VanillaCode(data, srcPath);

        Info("lang: " + vc.LangStatus + (vc.LangPath != null ? "  (" + vc.LangPath + ", " + vc.LangEntryCount + " 条)" : ""));

        var obj = data.GameObjects.FirstOrDefault(o => o.Name?.Content == "obj_readable_room1");
        Check(obj != null, "找到对象 obj_readable_room1");
        if (obj == null)
        {
            Console.WriteLine("VANILLA-SCAN-TEST FAIL (obj not found)");
            return 1;
        }

        var events = vc.EnumerateEvents(obj).ToList();
        Info("事件 " + events.Count + " 个: " + string.Join(", ", events.Take(20).Select(e => e.Name)) + (events.Count > 20 ? " …" : ""));
        Check(events.Count > 0, "对象事件可枚举");
        Check(events.Any(e => e.Name == "Other_10"), "含交互事件 Other_10");

        int roomIndex = vc.RoomIndex(data.Rooms.FirstOrDefault(r => r.Name?.Content == "room_krisroom"));
        Info("room_krisroom 在 data.Rooms 的索引 = " + roomIndex);
        Check(roomIndex >= 0, "房间索引可解析");

        // 用房间内该对象的真实实例坐标扫描（GUI 里点实例走的就是这条路径；没有实例则坐标未知=NaN）
        UndertaleRoom? scanRoom = data.Rooms.FirstOrDefault(r => r.Name?.Content == "room_krisroom");
        float instX = float.NaN, instY = float.NaN;
        if (scanRoom != null)
        {
            foreach (var layer in scanRoom.Layers)
            {
                if (layer.InstancesData?.Instances == null) continue;
                foreach (var inst in layer.InstancesData.Instances)
                {
                    if (!ReferenceEquals(inst.ObjectDefinition, obj)) continue;
                    instX = inst.X; instY = inst.Y;
                    break;
                }
                if (!float.IsNaN(instX)) break;
            }
        }
        Info("room_krisroom 内 obj_readable_room1 实例坐标 = " + (float.IsNaN(instX) ? "(无实例，坐标未知)" : $"{instX},{instY}"));
        var info = vc.ScanObject(obj, roomIndex, instX, instY);
        Info($"扫描结果: 事件={info.Events.Count}, 有对白事件={info.DialogueEventCount}, 对白行={info.DialogueLineCount}, 总字符={info.TotalChars}, 交互事件={(info.InteractionEvent?.Name ?? "(无)")}");
        Check(info.DialogueLineCount > 0, "交互对象能扫出对白行");

        int resolved = info.Events.SelectMany(e => e.Lines).Count(l => l.Truth != TriTruth.False);
        Info("非 False 分支的对白行 = " + resolved + " / " + info.DialogueLineCount);
        Check(resolved > 0, "条件判定后仍有可达对白行");

        var firstLines = info.Events.SelectMany(e => e.Lines).Where(l => l.Truth != TriTruth.False).Take(8).ToList();
        foreach (var l in firstLines)
        {
            Info($"  msg[{l.MsgIndex}] truth={l.Truth} key={l.Key} text={(l.Text.Length > 40 ? l.Text[..40] + "…" : l.Text)} cond={(l.Condition.Length > 60 ? l.Condition[..60] + "…" : l.Condition)}");
        }
        if (vc.LangEntryCount > 0)
            Check(firstLines.Any(l => !string.IsNullOrEmpty(l.Text)), "lang 文本可解析（至少一行非空）");
        else
            Info("（无 lang 文件，跳过文本解析判据；仍应能显示 key）");
        Check(firstLines.Any(l => !string.IsNullOrEmpty(l.Key)), "对白行保留语言 key");

        // 其它事件 key（有些对白不写在 msg 里，而是别的事件里挂 key）
        int otherKeys = info.Events.Sum(e => e.OtherKeys.Count);
        Info("事件内其它 lang key（非 msg 赋值）行 = " + otherKeys);

        // 实例创建代码 / 房间创建代码是否可达（只读展示用）
        UndertaleRoom? krisroom = data.Rooms.FirstOrDefault(r => r.Name?.Content == "room_krisroom");
        int instCodes = 0, instPre = 0;
        if (krisroom != null)
        {
            foreach (var layer in krisroom.Layers)
                if (layer.InstancesData?.Instances != null)
                    foreach (var inst in layer.InstancesData.Instances)
                    {
                        if (inst.CreationCode != null) instCodes++;
                        if (inst.PreCreateCode != null) instPre++;
                    }
        }
        Info($"room_krisroom: 实例创建代码={instCodes}, 预创建代码={instPre}, 房间创建代码={(krisroom?.CreationCodeId != null ? "有" : "无")}");
        bool roomCreationOk = true;
        if (krisroom?.CreationCodeId != null)
        {
            var code = vc.Decompile(krisroom.CreationCodeId);
            roomCreationOk = code != null;
            Info("房间创建代码长度 = " + (code?.Length ?? -1));
        }
        Check(roomCreationOk, "房间创建代码（若有）可反编译");

        // ── Round2 验收：行数下限 / 截断 / msc / 带参数 / with / 章节推断 / 非交互事件 ──
        var allLines = info.Events.SelectMany(e => e.Lines).ToList();
        int guessChapter = VanillaCode.GuessChapter(srcPath);
        Info("章节推断 = " + guessChapter + "（" + srcPath + "）");
        Check(VanillaCode.GuessChapter("chapter2_windows/data.win") == 2
              && VanillaCode.GuessChapter("ch1-edit.win") == 1
              && VanillaCode.GuessChapter(@"C:\games\DELTARUNE\backup\chapter5_windows\data.win") == 5,
              "章节推断支持 chapterN / chN / backup 路径");
        Check(!info.Events.Any(e => e.Truncated), "对白行未截断（MaxLines=4000）");
        if (guessChapter == 1) Check(info.DialogueLineCount >= 400, "ch1 obj_readable_room1 对白行 ≥ 400（实测 " + info.DialogueLineCount + "）");
        else if (guessChapter == 2) Check(info.DialogueLineCount >= 700, "ch2 obj_readable_room1 对白行 ≥ 700（实测 " + info.DialogueLineCount + "）");
        Check(allLines.Any(l => l.MscNo > 0 && !string.IsNullOrWhiteSpace(l.Text)), "scr_text 旧式 msc 文本可解析（非占位）");
        int mscWithCond = allLines.Count(l => l.MscNo > 0 && !string.IsNullOrEmpty(l.Condition));
        Check(mscWithCond > 0, "msc 展开行带上调用点条件（实测 " + mscWithCond + " 条）");
        Check(allLines.Count(l => l.Truth == TriTruth.Unknown) > 0, "未知条件保留为 ?（不误判为 False）");
        Info("可阅读正文行 = " + info.DisplayableLineCount + "；运行期文本/控制码行 = " + info.RuntimeTextLineCount);
        Check(info.DisplayableLineCount > 0, "可阅读正文行 > 0（displayableLines）");
        Check(info.ToString() != null && info.DialogueLineCount == info.DisplayableLineCount + info.RuntimeTextLineCount,
              "总量守恒：对白行 = 可阅读 + 运行期文本（" + info.DialogueLineCount + "）");
        // 控制码残留：lang 值与字面量都只有 \C2/\M1 这类控制码时，不能把 "C2" 当正文。
        int ctrlLeak = allLines.Count(l => l.Text.Length <= 3 && System.Text.RegularExpressions.Regex.IsMatch(l.Text, @"^[A-Za-z]\d$"));
        Check(ctrlLeak == 0, "控制码清洗后没有 C2/F0 这类残字（实测 " + ctrlLeak + " 行）");
        // 有/无 lang 两种 fixture（静态判定，不依赖打开第二个 data.win）
        string noLangDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outJson ?? srcPath)) ?? ".", "nolang-fixture");
        Directory.CreateDirectory(noLangDir);
        string noLangWin = Path.Combine(noLangDir, "unknownchapter.win");
        Check(VanillaCode.LangCandidatesFor(noLangWin).All(p => !File.Exists(p)), "无 lang 场景：候选路径全部不存在");
        Check(VanillaCode.LangStatusFor(noLangWin).Contains("未找到"), "无 lang 场景文案为「未找到 lang」");
        Check(VanillaCode.LangStatusFor(srcPath).Contains("文本来源"), "有 lang 场景文案带「文本来源」");
        if (!float.IsNaN(instX))
        {
            int reach = allLines.Count(l => l.Truth != TriTruth.False);
            // 注意：可达行数取决于坐标 —— 房间 2 实例 (196,99) 只有 11 行（x>220/其它房间分支都不可达），这是正确值。
            Check(reach >= 5, "实例坐标下可达对白行 ≥ 5（实测 " + reach + "）");
            Check(allLines.Any(l => l.Truth == TriTruth.False), "同房间其它分支判 False（else 三态修复）");
        }
        if (guessChapter == 2) Check(allLines.Any(l => l.RequiresArgs), "ch2 含参数替换的对白行可识别");
        // with 内文本：扫一批带 Other_10 的对象，确认 x/y 置 Unknown 且文本不丢
        bool sawWith = false, sawWithText = false;
        int scanned = 0;
        foreach (var o in data.GameObjects)
        {
            if (o == null || scanned >= 80) continue;
            var evs = vc.EnumerateEvents(o);
            if (!evs.Any(e => e.IsInteraction)) continue;
            scanned++;
            var wi = vc.ScanObject(o, roomIndex, float.NaN, float.NaN);
            foreach (var l in wi.Events.SelectMany(e => e.Lines))
            {
                if (l.Condition.Contains("within with", StringComparison.Ordinal)) { sawWith = true; if (!string.IsNullOrEmpty(l.Text)) sawWithText = true; }
            }
            if (sawWithText) break;
        }
        if (sawWith) Check(sawWithText, "with 内文本保留（条件标 ?，文本不丢）");
        else Info("扫描 " + scanned + " 个交互对象：本章没有 with 内对白样本（跳过 with 判据）");
        // 非交互 Other_* 标注
        bool nonInterFriendly = false;
        foreach (var o in data.GameObjects)
        {
            if (o == null) continue;
            foreach (var e in vc.EnumerateEvents(o))
            {
                if (e.Name.StartsWith("Other_", StringComparison.Ordinal) && e.Name != "Other_10"
                    && e.Friendly.Contains("非交互事件", StringComparison.Ordinal)) { nonInterFriendly = true; break; }
            }
            if (nonInterFriendly) break;
        }
        Check(nonInterFriendly, "非交互 Other_* 事件有「非交互事件」标注");

        // ── 事件继承（GameMaker 规则：子对象没定义的事件运行父对象的）──
        // 不处理继承就会把「原版实体交互有对话」显示成「无对话绑定」（典型：可读物件的 Other_10 写在父对象里）。
        int inheritedObjects = 0;
        foreach (var o in data.GameObjects)
        {
            if (o == null) continue;
            if (vc.EnumerateEvents(o).Any(e => e.Inherited)) inheritedObjects++;
        }
        Info("含继承事件的对象 = " + inheritedObjects + " / " + data.GameObjects.Count);
        Check(inheritedObjects > 0, "事件继承可枚举（子对象取父对象事件）");

        bool dupOther = false;
        foreach (var o in data.GameObjects)
        {
            if (o == null) continue;
            if (vc.EnumerateEvents(o).Count(x => x.Name == "Other_10") > 1) { dupOther = true; break; }
        }
        Check(!dupOther, "子对象覆盖父事件时不重复列出");

        var inhCand = data.GameObjects.FirstOrDefault(o => o != null && vc.EnumerateEvents(o).Any(e => e.Inherited));
        Check(inhCand != null, "存在继承事件的对象");
        if (inhCand != null)
        {
            var ci = vc.ScanObject(inhCand, roomIndex, float.NaN, float.NaN);
            Check(ci.AllEventNames.Any(n => n.Contains("（继承）")),
                  $"扫描结果标记了继承事件: {GameData.Name(inhCand.Name)}");
            Info($"  继承事件对白行 = {ci.InheritedDialogueLineCount} / 总 {ci.DialogueLineCount}；父链 = {string.Join(" → ", ci.ParentChain)}");
        }
        var childInh = data.GameObjects.FirstOrDefault(o => o != null && vc.EnumerateEvents(o).Any(e => e.Inherited && e.IsInteraction));
        if (childInh != null)
        {
            var ci = vc.ScanObject(childInh, roomIndex, float.NaN, float.NaN);
            var ie = ci.InteractionEvent;
            Check(ie != null && ie.Inherited && ie.OwnerObject.Length > 0,
                  $"继承的交互事件被识别为交互: {GameData.Name(childInh.Name)} ← {ie?.OwnerObject}");
            Info($"  继承交互事件对白行 = {ci.InheritedDialogueLineCount} / 总 {ci.DialogueLineCount}");
        }
        else
        {
            Info("本章没有「继承来的 Other_10」样本：跳过交互继承专项判据（继承枚举/去重已由上面两判据覆盖）");
        }

        if (outJson != null)
        {
            var payload = new Dictionary<string, object?>
            {
                ["dataWin"] = srcPath,
                ["langPath"] = vc.LangPath,
                ["langEntries"] = vc.LangEntryCount,
                ["langStatus"] = vc.LangStatus,
                ["roomIndex"] = roomIndex,
                ["object"] = info.ObjectName,
                ["eventNames"] = info.AllEventNames,
                ["dialogueEvents"] = info.DialogueEventCount,
                ["dialogueLines"] = info.DialogueLineCount,
                ["displayableLines"] = info.DisplayableLineCount,
                ["runtimeTextLines"] = info.RuntimeTextLineCount,
                ["totalChars"] = info.TotalChars,
                ["interactionEvent"] = info.InteractionEvent?.Name,
                ["interactionInherited"] = info.InteractionEvent?.Inherited ?? false,
                ["interactionOwner"] = info.InteractionEvent?.OwnerObject,
                ["parentChain"] = info.ParentChain,
                ["inheritedDialogueLines"] = info.InheritedDialogueLineCount,
                ["inheritedObjects"] = inheritedObjects,
                ["events"] = info.Events.Select(e => new Dictionary<string, object?>
                {
                    ["name"] = e.Name,
                    ["friendly"] = e.Friendly,
                    ["codeLength"] = e.Chars,
                    ["msgAssigns"] = e.MsgAssignCount,
                    ["dynamic"] = e.DynamicCount,
                    ["truncated"] = e.Truncated,
                    ["lines"] = e.Lines.Select(l => new Dictionary<string, object?>
                    {
                        ["msg"] = l.MsgIndex,
                        ["msc"] = l.MscNo,
                        ["key"] = l.Key,
                        ["text"] = l.DisplayText,
                        ["runtimeText"] = l.RuntimeText,
                        ["condition"] = l.Condition,
                        ["truth"] = l.Truth.ToString(),
                        ["event"] = l.EventName,
                        ["friendly"] = l.EventFriendly,
                    }).ToList(),
                    ["otherKeys"] = e.OtherKeys,
                }).ToList(),
            };
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
            var dir = Path.GetDirectoryName(Path.GetFullPath(outJson));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(outJson, json + Environment.NewLine, new UTF8Encoding(false));
            Info("报告: " + Path.GetFullPath(outJson) + " (" + json.Length + " 字符)");
        }

        Console.WriteLine(_fails == 0 ? "VANILLA-SCAN-TEST PASS" : $"VANILLA-SCAN-TEST FAIL ({_fails})");
        return _fails == 0 ? 0 : 1;
    }

    /// <summary>把一次单对象扫描导出成 JSON（CLI vanilla-scan 用；字段与自检报告同口径，便于和基线做 key 级对账）。</summary>
    public static string BuildScanJson(VanillaCode vc, VanillaObjectInfo info, string srcPath, int roomIndex, float x, float y, string roomName)
    {
        var payload = new Dictionary<string, object?>
        {
            ["dataWin"] = Path.GetFullPath(srcPath),
            ["room"] = roomName,
            ["roomIndex"] = roomIndex,
            ["x"] = float.IsNaN(x) ? null : x,
            ["y"] = float.IsNaN(y) ? null : y,
            ["langPath"] = vc.LangPath,
            ["langEntries"] = vc.LangEntryCount,
            ["langStatus"] = vc.LangStatus,
            ["object"] = info.ObjectName,
            ["eventNames"] = info.AllEventNames,
            ["dialogueEvents"] = info.DialogueEventCount,
            ["dialogueLines"] = info.DialogueLineCount,
            ["displayableLines"] = info.DisplayableLineCount,
            ["runtimeTextLines"] = info.RuntimeTextLineCount,
            ["totalChars"] = info.TotalChars,
            ["interactionEvent"] = info.InteractionEvent?.Name,
            ["interactionInherited"] = info.InteractionEvent?.Inherited ?? false,
            ["interactionOwner"] = info.InteractionEvent?.OwnerObject,
            ["parentChain"] = info.ParentChain,
            ["inheritedDialogueLines"] = info.InheritedDialogueLineCount,
            ["events"] = info.Events.Select(e => new Dictionary<string, object?>
            {
                ["name"] = e.Name,
                ["friendly"] = e.Friendly,
                ["codeLength"] = e.Chars,
                ["msgAssigns"] = e.MsgAssignCount,
                ["dynamic"] = e.DynamicCount,
                ["truncated"] = e.Truncated,
                ["lines"] = e.Lines.Select(l => new Dictionary<string, object?>
                {
                    ["msg"] = l.MsgIndex,
                    ["msc"] = l.MscNo,
                    ["key"] = l.Key,
                    ["text"] = l.DisplayText,
                    ["runtimeText"] = l.RuntimeText,
                    ["rawText"] = l.Text,
                    ["condition"] = l.Condition,
                    ["truth"] = l.Truth.ToString(),
                    ["requiresArgs"] = l.RequiresArgs,
                    ["event"] = l.EventName,
                }).ToList(),
                ["otherKeys"] = e.OtherKeys,
            }).ToList(),
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }
}