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

            // ---------------- baseline sidecar：不写、不覆盖、不建 .bak ----------------
            var baselineSidecar = BattleProject.SidecarFor(baselineWin);
            File.WriteAllText(baselineSidecar, "{\"sentinel\":\"untouched\"}");
            var sideHashBefore = Sha256(baselineSidecar);
            var bp = BattleProject.LoadOrNew(baselineSidecar);
            bp.File.Battles.Add(new BattleDef
            {
                Id = "fixture-battle",
                Name = "fixture",
                Room = "room_krisroom",
                ObjectName = "obj_fixture",
                InstanceId = 1,
            });
            var sideRes = bp.Save(baselineSidecar, false, game);
            Check(!sideRes.Ok, "baseline sidecar: Save 被拒绝");
            Check(Sha256(baselineSidecar) == sideHashBefore, "baseline sidecar: 原内容未被覆盖");
            Check(!File.Exists(baselineSidecar + ".bak"), "baseline sidecar: 未创建 .bak");
            var sideRes2 = bp.Save(baselineSidecar); // 自动 gameRoot 兜底
            Check(!sideRes2.Ok, "baseline sidecar: 默认 gameRoot 路径也拒绝");
            var absentSidecar = Path.Combine(game, "backup", "chapter1_windows", "data.absent.ntlbattle.json");
            var bpAbsent = BattleProject.LoadOrNew(absentSidecar);
            bpAbsent.File.Battles.Add(new BattleDef { Id = "fixture-absent", Name = "absent", Room = "room_krisroom", ObjectName = "obj_absent", InstanceId = 2 });
            var absentRes = bpAbsent.Save(absentSidecar, false, game);
            Check(!absentRes.Ok && !File.Exists(absentSidecar), "baseline sidecar: 不存在的 sidecar 也不会被创建");

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
