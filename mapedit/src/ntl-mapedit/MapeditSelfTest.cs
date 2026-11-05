using System.IO.Compression;
using System.Security.Cryptography;

namespace NtlMapEdit;

/// <summary>
/// 无 UI 的 mer 编辑安全自检：FileRole 四类、baseline/live 的 data.win 与 sidecar 写盘、
/// 启动默认目标。fixture 只建在调用者给的临时目录里，不读写真实 backup。
/// 用法：ntl-mapedit mapedit-test &lt;src data.win&gt; &lt;workDir&gt;
/// </summary>
public static class MapeditSelfTest
{
    public static int Run(string src, string workDir)
    {
        int fails = 0;
        void Check(bool ok, string label)
        {
            Console.WriteLine((ok ? "PASS " : "FAIL ") + label);
            if (!ok) fails++;
        }
        void Note(string text) => Console.WriteLine("  " + text);

        try
        {
            if (!File.Exists(src))
            {
                Console.WriteLine("MAPEDIT-TEST FAIL: src 不存在: " + src);
                return 2;
            }
            var srcInfo = new FileInfo(src);
            if (srcInfo.Length < 1024 * 1024)
            {
                Console.WriteLine($"MAPEDIT-TEST FAIL: src 太小（{srcInfo.Length} B），不像是 data.win: {src}");
                return 2;
            }

            string fullWork;
            try { fullWork = Path.GetFullPath(workDir); }
            catch (Exception ex)
            {
                Console.WriteLine("MAPEDIT-TEST FAIL: 无效 workDir: " + ex.Message);
                return 2;
            }

            // 安全闸：fixture 绝不能落在真实游戏目录（尤其 backup）里。
            var realRoot = FileRole.FindGameRoot();
            if (realRoot != null && IsUnder(fullWork, realRoot))
            {
                Console.WriteLine("MAPEDIT-TEST FAIL: workDir 位于真实游戏根内，拒绝运行（fixture 只能放 tmp）: " + fullWork);
                return 2;
            }

            var fixture = Path.Combine(fullWork, "maptest-" + Environment.ProcessId);
            if (Directory.Exists(fixture)) Directory.Delete(fixture, true);
            var game = Path.Combine(fixture, "game");
            var liveWin = Path.Combine(game, "chapter1_windows", "data.win");
            var baselineWin = Path.Combine(game, "backup", "chapter1_windows", "data.win");
            var neutralBackupWin = Path.Combine(game, "Neutraled", "backup", "data.win");
            var otherWin = Path.Combine(game, "chapter2_windows", "other.win");
            Directory.CreateDirectory(Path.GetDirectoryName(liveWin)!);
            Directory.CreateDirectory(Path.GetDirectoryName(baselineWin)!);
            Directory.CreateDirectory(Path.GetDirectoryName(neutralBackupWin)!);

            File.Copy(src, liveWin);
            File.Copy(src, baselineWin);
            // File.Copy 会继承原版 backup 的 ReadOnly 属性；这里主动清掉，
            // 确保 baseline 被拒是 FileRole 守卫的结果，而不是操作系统的只读权限。
            File.SetAttributes(liveWin, FileAttributes.Normal);
            File.SetAttributes(baselineWin, FileAttributes.Normal);
            Console.WriteLine("FIXTURE " + fixture);
            Note($"src={src} ({srcInfo.Length} B)");
            Note("真实 backup 未触碰；以下写操作只发生在 fixture。");

            // ---------------- FileRole.Classify 四类 ----------------
            Check(FileRole.Classify(Path.Combine(game, "data.win"), game) == FileKind.LiveRoot,
                "classify: <gameRoot>\\data.win -> LiveRoot");
            Check(FileRole.Classify(liveWin, game) == FileKind.LiveChapter,
                "classify: <gameRoot>\\chapterN_windows\\data.win -> LiveChapter");
            Check(FileRole.Classify(baselineWin, game) == FileKind.Baseline,
                "classify: <gameRoot>\\backup\\... -> Baseline");
            Check(FileRole.Classify(neutralBackupWin, game) == FileKind.Baseline,
                "classify: <gameRoot>\\Neutraled\\backup\\... -> Baseline");
            Check(FileRole.Classify(otherWin, game) == FileKind.Other,
                "classify: 其它路径 -> Other");
            Check(FileRole.Classify(baselineWin, null) == FileKind.Baseline,
                "classify: gameRoot 未知时 backup 目录仍是 Baseline（堵住绕过口）");
            Check(FileRole.Badge(FileKind.Baseline) == "原版基线 · 只读", "badge: baseline 文案");
            Check(FileRole.Badge(FileKind.LiveChapter) == "游戏文件 · 可保存", "badge: live 文案");
            Check(EditorForm.IsEngineRoom("ROOM_INITIALIZE"), "engine-room: ROOM_INITIALIZE 视为引擎房");
            Check(!EditorForm.IsEngineRoom("room_krisroom"), "engine-room: 普通 room_* 不视为引擎房（区分大小写回归）");
            Check(!EditorForm.IsEngineRoom("room_initialize"), "engine-room: 小写 room_initialize 不误判");
            Check(!EditorForm.IsEngineRoom(""), "engine-room: 空名不是引擎房");
            Check(!EditorForm.IsEngineRoom(null), "engine-room: null 不是引擎房");

            var realRoot2 = FileRole.FindGameRoot();
            if (realRoot2 != null)
            {
                Check(FileRole.Classify(Path.Combine(realRoot2, "data.win"), realRoot2) == FileKind.LiveRoot,
                    "classify(real): <gameRoot>\\data.win -> LiveRoot");
                Check(FileRole.Classify(Path.Combine(realRoot2, "chapter1_windows", "data.win"), realRoot2) == FileKind.LiveChapter,
                    "classify(real): <gameRoot>\\chapter1_windows\\data.win -> LiveChapter");
                Check(FileRole.Classify(Path.Combine(realRoot2, "backup", "chapter1_windows", "data.win"), realRoot2) == FileKind.Baseline,
                    "classify(real): <gameRoot>\\backup\\chapter1_windows\\data.win -> Baseline");
                Check(FileRole.Classify(Path.Combine(realRoot2, "Neutraled", "backup", "data.win"), realRoot2) == FileKind.Baseline,
                    "classify(real): <gameRoot>\\Neutraled\\backup\\data.win -> Baseline");
            }
            else Note("SKIP classify(real)：未找到游戏根");

            // ---------------- baseline data.win：拒绝写盘且字节/目录不变 ----------------
            var baselineHashBefore = Sha256(baselineWin);
            long baselineLenBefore = new FileInfo(baselineWin).Length;
            using (var gd = new GameData(baselineWin))
            using (var doc = new EditorDocument(gd, baselineWin))
            {
                doc.MarkDirty("fixture");
                var res = doc.Save(baselineWin, false, game);
                Check(!res.Ok, "baseline: Save 被拒绝");
                Check((res.Error ?? "").Contains("原版基线", StringComparison.Ordinal) &&
                      (res.Error ?? "").Contains("只读", StringComparison.Ordinal),
                    "baseline: 返回明确「原版基线 · 只读」错误");
                var res2 = doc.Save(baselineWin); // 不传 gameRoot，走自动探测 + backup 结构兜底
                Check(!res2.Ok, "baseline: 默认 gameRoot 路径也拒绝（不依赖游戏根探测）");
            }
            Check(Sha256(baselineWin) == baselineHashBefore, "baseline: data.win 字节不变（SHA256）");
            Check(new FileInfo(baselineWin).Length == baselineLenBefore, "baseline: data.win 大小不变");
            Check(!File.Exists(baselineWin + ".bak"), "baseline: 未创建 data.win.bak");
            Check(!File.Exists(baselineWin + ".ntlsave.tmp"), "baseline: 未留下 .ntlsave.tmp");

            // ---------------- baseline 编辑：sidecar 改道 Neutraled/editor（data.win 不动）----------------
            // Round3 语义：打开原版基线就能直接编辑对话/战斗/剧情；编辑数据写 Neutraled/editor/，不碰 backup。
            var editSidecar = BattleProject.SidecarForEdit(baselineWin, game);
            Check(editSidecar.Contains(Path.Combine("Neutraled", "editor"), StringComparison.OrdinalIgnoreCase),
                "baseline 编辑 sidecar: 落在 Neutraled/editor");
            Check(FileRole.Classify(editSidecar, game) != FileKind.Baseline,
                "baseline 编辑 sidecar: 目标不是基线路径（可写）");
            Check(!editSidecar.StartsWith(Path.Combine(game, "backup"), StringComparison.OrdinalIgnoreCase),
                "baseline 编辑 sidecar: 不在 backup/ 下");
            Check(BattleProject.ChapterLeaf(baselineWin) == "chapter1",
                "baseline 编辑 sidecar: 叶子名从路径推断为 chapter1");
            var bp = BattleProject.LoadOrNew(editSidecar);
            bp.File.Battles.Add(new BattleDef
            {
                Id = "fixture-battle",
                Name = "fixture",
                Room = "room_krisroom",
                ObjectName = "obj_fixture",
                InstanceId = 1,
            });
            var sideRes = bp.Save(editSidecar, true, game);
            Check(sideRes.Ok, "baseline 编辑 sidecar: Save 成功（allowBaselineWrite=true）");
            Check(File.Exists(editSidecar), "baseline 编辑 sidecar: 文件已创建");
            Check(BattleProject.Load(editSidecar).File.Battles.Count == 1, "baseline 编辑 sidecar: 重读内容正确");
            Check(Sha256(baselineWin) == baselineHashBefore, "baseline 编辑: 原版 data.win 仍未被改动");

            // backup 内的 sidecar 仍受基线闸门保护（显式 allow 才会写）。
            var baselineSidecar = BattleProject.SidecarFor(baselineWin);
            File.WriteAllText(baselineSidecar, "{\"sentinel\":\"untouched\"}");
            var sideHashBefore = Sha256(baselineSidecar);
            var guarded = BattleProject.LoadOrNew(baselineSidecar);
            guarded.File.Battles.Add(new BattleDef { Id = "fixture-guard", Name = "guard", Room = "room_krisroom", ObjectName = "obj_guard", InstanceId = 3 });
            var guardedRes = guarded.Save(baselineSidecar, false, game);
            Check(!guardedRes.Ok, "backup sidecar: 未显式放行时仍被拒绝");
            Check(Sha256(baselineSidecar) == sideHashBefore, "backup sidecar: 原内容未被覆盖");
            Check(!File.Exists(baselineSidecar + ".bak"), "backup sidecar: 未创建 .bak");
            var absentSidecar = Path.Combine(game, "backup", "chapter1_windows", "data.absent.ntlbattle.json");
            var bpAbsent = BattleProject.LoadOrNew(absentSidecar);
            bpAbsent.File.Battles.Add(new BattleDef { Id = "fixture-absent", Name = "absent", Room = "room_krisroom", ObjectName = "obj_absent", InstanceId = 2 });
            var absentRes = bpAbsent.Save(absentSidecar, false, game);
            Check(!absentRes.Ok && !File.Exists(absentSidecar), "backup sidecar: 不存在的 sidecar 也不会被创建");

            // ---------------- 房间流程排序：每房一次、主线编号递增、支线紧随前置主线 ----------------
            using (var gdFlow = new GameData(liveWin))
            {
                var rooms = gdFlow.Rooms.ToList();
                var flow = RoomFlow.Build(rooms);
                var members = flow.Where(e => !e.IsHeader).Select(e => e.Room!).ToList();
                Check(members.Count == rooms.Count, $"roomflow: 房间不多不少（{members.Count}/{rooms.Count}）");
                Check(members.Distinct().Count() == rooms.Count, "roomflow: 没有重复房间");
                var mains = flow.Where(e => e.Section == RoomFlowSection.Main).ToList();
                bool ascending = true;
                for (int i = 0; i < mains.Count; i++)
                    if (mains[i].Order != i + 1) ascending = false;
                Check(ascending, "roomflow: 主线编号 1..N 连续递增");
                Check(flow.Count(e => e.IsHeader) <= 1, "roomflow: 最多一个分组标题");
                bool sideFollows = true;
                for (int i = 0; i < flow.Count; i++)
                {
                    if (flow[i].Section != RoomFlowSection.Side) continue;
                    if (i == 0)
                    {
                        if (flow[i].Order != 0) sideFollows = false;
                        continue;
                    }
                    // 同一前置主线下的多条支线会连续排在一起：前一行可以是主线，也可以是同编号的支线。
                    bool prevOk = (flow[i - 1].Section == RoomFlowSection.Main && flow[i - 1].Order == flow[i].Order)
                                  || (flow[i - 1].Section == RoomFlowSection.Side && flow[i - 1].Order == flow[i].Order
                                      && flow[i - 1].Depth == flow[i].Depth);
                    if (!prevOk) sideFollows = false;
                }
                Check(sideFollows, "roomflow: 支线紧跟其前置主线（同编号、缩进）");
                var dataOrder = RoomFlow.BuildDataOrder(rooms);
                Check(dataOrder.Count == rooms.Count, "roomflow: 原始顺序模式房间数一致");
                Check(dataOrder.Select(e => e.DataIndex).SequenceEqual(Enumerable.Range(0, rooms.Count)),
                    "roomflow: 原始顺序模式保持 data.win 顺序");
            }

            // ---------------- 文本改写 / 原版代码步骤：sidecar 往返 ----------------
            var roundtripPath = Path.Combine(fixture, "roundtrip.ntlbattle.json");
            var rp = BattleProject.LoadOrNew(roundtripPath);
            rp.File.TextOverrides.Add(new TextOverrideDef
            {
                Key = "obj_door_Draw_0_gml_5_0",
                Text = "门锁上了。",
                Room = "room_krisroom",
                ObjectName = "obj_door",
                EventName = "Draw_0",
                Original = "The door is locked.",
            });
            rp.File.Cutscenes.Add(new CutsceneDef
            {
                Id = "cut-fixture",
                Name = "fixture",
                Room = "room_krisroom",
                Steps =
                {
                    new CutsceneStep { Kind = "event", EvObject = "obj_door", Script = "gml_Script_scr_sideb_active", EvType = 3, EvSub = 0, Frames = 0, Note = "Step 0" },
                    new CutsceneStep { Kind = "script", Script = "gml_Script_scr_84_get_lang_string", Frames = 2 },
                },
            });
            rp.Normalize();
            var rtRes = rp.Save(roundtripPath, true, game);
            Check(rtRes.Ok, "roundtrip: sidecar 写出成功");
            var rt = BattleProject.Load(roundtripPath);
            Check(rt.File.TextOverrides.Count == 1 && rt.File.TextOverrides[0].Text == "门锁上了。",
                "roundtrip: 文本改写 key/text 保留");
            var rtSteps = rt.File.Cutscenes.FirstOrDefault()?.Steps ?? new List<CutsceneStep>();
            Check(rtSteps.Count == 2 && rtSteps[0].Kind == "event" && rtSteps[0].Script == "gml_Script_scr_sideb_active"
                  && rtSteps[0].EvType == 3 && rtSteps[0].EvSub == 0,
                "roundtrip: event 步骤（对象/脚本/事件号）保留");
            Check(rtSteps.Count == 2 && rtSteps[1].Kind == "script" && rtSteps[1].EvObject == "",
                "roundtrip: script 步骤保留且不绑定对象");

            // ---------------- live data.win：允许原地保存并真正落盘 ----------------
            var liveHashBefore = Sha256(liveWin);
            uint oldWidth = 0, newWidth = 0;
            using (var gd = new GameData(liveWin))
            using (var doc = new EditorDocument(gd, liveWin))
            {
                var room = gd.Rooms.First();
                oldWidth = room.Width;
                newWidth = oldWidth + 1;
                room.Width = newWidth;
                doc.MarkDirty(GameData.Name(room.Name));
                var res = doc.Save(liveWin, false, game);
                Check(res.Ok, "live: Save 成功");
                if (!res.Ok) Note(res.Error ?? "");
            }
            Check(Sha256(liveWin) != liveHashBefore, "live: 文件字节已变化（确实写盘）");
            using (var gd2 = new GameData(liveWin))
            {
                var room2 = gd2.Rooms.First();
                Check(room2.Width == newWidth && newWidth == oldWidth + 1,
                    $"live: 重载后宽度 {oldWidth} -> {newWidth} 生效");
            }

            // ---------------- live sidecar：允许写并重读 ----------------
            var liveSidecar = BattleProject.SidecarFor(liveWin);
            var lp = BattleProject.LoadOrNew(liveSidecar);
            lp.File.Battles.Add(new BattleDef
            {
                Id = "fixture-live",
                Name = "fixture live",
                Room = "room_krisroom",
                ObjectName = "obj_fixture_live",
                InstanceId = 7,
            });
            var liveSideRes = lp.Save(liveSidecar, false, game);
            Check(liveSideRes.Ok, "live sidecar: Save 成功");
            Check(File.Exists(liveSidecar), "live sidecar: 文件存在");
            var liveSideReloaded = BattleProject.Load(liveSidecar);
            Check(liveSideReloaded.File.Battles.Count == 1 &&
                  liveSideReloaded.File.Battles[0].Id == "fixture-live",
                "live sidecar: 重读内容正确");

            // ---------------- 房间差分：diff -> RoomPatchApply -> 再 diff 为空（地图改动进 mod，data.win 只是中间量）----------------
            var editedWin = Path.Combine(fixture, "edited_chapter1.win");
            File.Copy(baselineWin, editedWin);
            RoomDiffResult roomDiff;
            string diffRoom;
            using (var baseGd = new GameData(baselineWin))
            using (var editGd = new GameData(editedWin))
            {
                var first = editGd.Rooms.First();
                diffRoom = GameData.Name(first.Name);
                first.Width += 16;
                RoomEdit.CreateBlankRoom(editGd.Data, editGd.Data.Rooms[0], "room_ntl_diff_new");
                roomDiff = RoomDiff.Build(baseGd.Data, editGd.Data);
                Check(roomDiff.Patches.Count == 1 && roomDiff.Patches[0].Name == diffRoom
                      && roomDiff.Patches[0].Width == first.Width,
                    "roomdiff: 房间头改动被检出（名字 + 新宽度）");
                Check(roomDiff.NewRooms.Count == 1 && roomDiff.NewRooms[0].Name == "room_ntl_diff_new",
                    "roomdiff: 新房间进入 rooms 段");
                Check(roomDiff.ChangeCount >= 2, "roomdiff: ChangeCount 含房间头与新增房间");
            }
            using (var baseGd = new GameData(baselineWin))
            using (var editGd = new GameData(editedWin))
            {
                // 内存里重放同一份编辑（editedWin 只是磁盘副本，编辑本身没写盘），再验证回灌闭环。
                editGd.Rooms.First().Width += 16;
                RoomEdit.CreateBlankRoom(editGd.Data, editGd.Data.Rooms[0], "room_ntl_diff_new");
                var applied = RoomPatchApply.Apply(baseGd.Data, roomDiff.Patches, roomDiff.NewRooms);
                Check(applied.RoomsPatched == 1, "roomdiff: RoomPatchApply 回灌房间头");
                Check(applied.RoomsAdded == 1, "roomdiff: RoomPatchApply 回灌新房间");
                var again = RoomDiff.Build(baseGd.Data, editGd.Data);
                if (again.ChangeCount != 0 || again.NewRooms.Count != 0)
                    Note("again-diff: patches=" + again.Patches.Count + " newRooms=" + again.NewRooms.Count + " changes=" + again.ChangeCount);
                Check(again.ChangeCount == 0 && again.NewRooms.Count == 0,
                    "roomdiff: 回灌后再 diff 为空（幂等闭环）");
            }

            // ---------------- .ntlmod 装机闭环：导出包 -> Neutraled/mods/<id>/<author>/<leaf> ----------------
            string pkgDir = Path.Combine(fixture, "packages");
            Directory.CreateDirectory(pkgDir);
            string mkPkg(string fileName, string json, params (string Name, string Content)[] files)
            {
                string p = Path.Combine(pkgDir, fileName);
                if (File.Exists(p)) File.Delete(p);
                using var zip = ZipFile.Open(p, ZipArchiveMode.Create);
                var me = zip.CreateEntry("mod.json");
                using (var w = new StreamWriter(me.Open())) w.Write(json);
                foreach (var f in files)
                {
                    var en = zip.CreateEntry(f.Name);
                    using var ws = new StreamWriter(en.Open());
                    ws.Write(f.Content);
                }
                return p;
            }
            string pkg = mkPkg("ntl.selftest-chapter1.ntlmod",
                "{\"id\":\"ntl.selftest\",\"author\":\"doge-e\",\"chapter\":\"chapter1\"}",
                ("gml/main.gml", "function main() {}\n"), ("files/marker.txt", "ok"));
            string gameRootSelftest = Path.Combine(fixture, "ntlmod-game");
            var inst = NtlmodInstaller.Install(gameRootSelftest, pkg);
            Check(inst.Ok, "ntlmod: 安装成功（未传 leaf，取自包名后缀）");
            string wantLeaf = Path.Combine(gameRootSelftest, "Neutraled", "mods", "ntl.selftest", "doge-e", "chapter1");
            Check(string.Equals(Path.GetFullPath(inst.Target), Path.GetFullPath(wantLeaf), StringComparison.OrdinalIgnoreCase),
                "ntlmod: 按 id/author/leaf 落位（与 builder --mod-install 同布局）");
            Check(File.Exists(Path.Combine(wantLeaf, "mod.json")) && File.Exists(Path.Combine(wantLeaf, "gml", "main.gml")),
                "ntlmod: 包内条目已解出");
            File.WriteAllText(Path.Combine(wantLeaf, "gml", "stale.gml"), "removed by reinstall");
            var inst2 = NtlmodInstaller.Install(gameRootSelftest, pkg);
            Check(inst2.Ok && !File.Exists(Path.Combine(wantLeaf, "gml", "stale.gml")),
                "ntlmod: 重复安装整体替换（旧文件不残留，避免旧脚本覆盖新包）");
            Check(!NtlmodInstaller.Install(gameRootSelftest, pkg, "chapter9").Ok, "ntlmod: 非法 leaf 被拒");
            string pkgNoLeaf = mkPkg("noleaf.ntlmod", "{\"id\":\"ntl.noleaf\",\"author\":\"doge-e\"}", ("mod.marker", "x"));
            Check(!NtlmodInstaller.Install(gameRootSelftest, pkgNoLeaf).Ok, "ntlmod: 无法确定叶子时拒装（不默认 root）");
            Check(NtlmodInstaller.LeafFromFileName("battle.demo-chapter1.ntlmod") == "chapter1"
                && NtlmodInstaller.LeafFromFileName("pkg-root.ntlmod") == "root"
                && NtlmodInstaller.LeafFromFileName("plain.ntlmod") == "", "ntlmod: 包名后缀解析叶子");

            // ---------------- 启动默认目标：最近文件 -> live chapter1，绝不 backup ----------------
            var recentFile = Path.Combine(fixture, "recent.json");
            Check(RecentStore.WriteLast(recentFile, baselineWin), "recent: 写出成功");
            Check(string.Equals(RecentStore.ReadLast(recentFile), Path.GetFullPath(baselineWin), StringComparison.OrdinalIgnoreCase),
                "recent: 读回最近文件");
            var (recentTarget, recentRole) = StartupTarget.ResolveWithRole(game, RecentStore.ReadLast(recentFile));
            Check(string.Equals(recentTarget, Path.GetFullPath(baselineWin), StringComparison.OrdinalIgnoreCase),
                "startup: 最近文件优先");
            Check(recentRole == FileKind.Baseline, "startup: 最近文件是 baseline -> 只读角色");
            RecentStore.WriteLast(recentFile, Path.Combine(fixture, "missing.win"));
            var (fallbackTarget, fallbackRole) = StartupTarget.ResolveWithRole(game, RecentStore.ReadLast(recentFile));
            Check(string.Equals(fallbackTarget, Path.GetFullPath(liveWin), StringComparison.OrdinalIgnoreCase),
                "startup: 最近文件失效 -> 打开 live chapter1");
            Check(fallbackRole == FileKind.LiveChapter, "startup: fallback 角色 LiveChapter");
            var (noRecentTarget, noRecentRole) = StartupTarget.ResolveWithRole(game, null);
            Check(string.Equals(noRecentTarget, Path.GetFullPath(liveWin), StringComparison.OrdinalIgnoreCase),
                "startup: 无最近文件 -> live chapter1");
            Check(noRecentRole == FileKind.LiveChapter, "startup: 无最近文件角色 LiveChapter");
            Check(!string.Equals(noRecentTarget, Path.GetFullPath(baselineWin), StringComparison.OrdinalIgnoreCase),
                "startup: 默认目标不是 backup（A2 回归）");

            Console.WriteLine(fails == 0 ? "MAPEDIT-TEST PASS" : $"MAPEDIT-TEST FAIL ({fails})");
            return fails == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine("MAPEDIT-TEST FAIL: " + ex);
            return 1;
        }
    }

    private static bool IsUnder(string full, string dir)
    {
        var d = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), d, StringComparison.OrdinalIgnoreCase) ||
               full.StartsWith(d + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string Sha256(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(fs));
    }
}
