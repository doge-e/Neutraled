using System.Text;

namespace NtlMapEdit;

/// <summary>
/// 「默认启动章节」设置自检：设置读写/归一化、坏文件自愈、启动目标解析优先级（显式章节 → 最近打开 → auto → 回退）。
/// 全部在 workDir 下的临时目录里造，不碰真实游戏目录与真实设置文件。
/// </summary>
public static class StartupSettingsSelfTest
{
    private static int _fails;

    private static void Check(bool ok, string name)
    {
        if (!ok) _fails++;
        Console.WriteLine((ok ? "  [OK] " : "  [FAIL] ") + name);
    }

    public static int Run(string workDir)
    {
        _fails = 0;
        Console.WriteLine("SETTINGS-TEST: " + workDir);
        var root = Path.Combine(Path.GetFullPath(workDir), "settings-test-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(root);
        try
        {
            // 1) 归一化
            Check(StartupSettings.NormalizeChapter(null) == "auto", "null → auto");
            Check(StartupSettings.NormalizeChapter("") == "auto", "空 → auto");
            Check(StartupSettings.NormalizeChapter("default") == "auto", "default → auto");
            Check(StartupSettings.NormalizeChapter("last") == "auto", "last → auto");
            Check(StartupSettings.NormalizeChapter("chapter3") == "chapter3", "chapter3 → chapter3");
            Check(StartupSettings.NormalizeChapter("4") == "chapter4", "4 → chapter4");
            Check(StartupSettings.NormalizeChapter(" Chapter 2 ") == "chapter2", "空格/大写 → chapter2");
            Check(StartupSettings.NormalizeChapter("chapter9") == "auto", "chapter9（越界）→ auto");
            Check(StartupSettings.NormalizeChapter("abc") == "auto", "abc → auto");
            Check(StartupSettings.ChapterNumber("chapter5") == 5, "ChapterNumber(chapter5)=5");
            Check(StartupSettings.ChapterNumber("auto") == 0, "ChapterNumber(auto)=0");

            // 2) 缺省加载 + 保存/回读
            string sfile = Path.Combine(root, "settings.json");
            var s0 = StartupSettings.Load(sfile);
            Check(s0.DefaultChapter == "auto", "缺省设置 → auto");
            s0.DefaultChapter = "chapter4";
            s0.Save(sfile);
            Check(File.Exists(sfile), "保存后文件存在");
            var s1 = StartupSettings.Load(sfile);
            Check(s1.DefaultChapter == "chapter4", "回读 = chapter4");

            // 3) 坏文件自愈：加载回 auto，保存时备份 .bad-*
            File.WriteAllText(sfile, "这不是 json\n");
            var sBad = StartupSettings.Load(sfile);
            Check(sBad.DefaultChapter == "auto", "坏文件加载 → auto（不抛）");
            sBad.DefaultChapter = "chapter2";
            sBad.Save(sfile);
            Check(Directory.GetFiles(root, "settings.json.bad-*").Length == 1, "坏文件保存时备份 .bad-*");
            Check(StartupSettings.Load(sfile).DefaultChapter == "chapter2", "自愈后回读 = chapter2");

            // 4) 启动目标解析优先级
            string gameRoot = Path.Combine(root, "game");
            void Fake(string rel)
            {
                var p = Path.Combine(gameRoot, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                if (!File.Exists(p)) File.WriteAllText(p, "x");
            }
            Fake(Path.Combine("chapter2_windows", "data.win"));
            Fake(Path.Combine("backup", "chapter3_windows", "data.win"));
            string recent = Path.Combine(gameRoot, "chapter2_windows", "data.win");

            string cfgA = Path.Combine(root, "a.json");
            var a = new StartupSettings { DefaultChapter = "chapter4" }; a.Save(cfgA); // ch4 不存在 → 最近打开
            var ta = StartupSettings.ResolveTarget(gameRoot, recent, cfgA);
            Check(ta.Path == recent && !ta.FromDefault, "ch4 缺失 → 回退最近打开");
            Check(ta.Source.Contains("最近打开"), "回退来源标注为最近打开");

            string cfgB = Path.Combine(root, "b.json");
            var b = new StartupSettings { DefaultChapter = "chapter2" }; b.Save(cfgB);
            var tb = StartupSettings.ResolveTarget(gameRoot, null, cfgB);
            Check(tb.FromDefault && !tb.IsBaseline && tb.Chapter == 2, "显式 chapter2 → live 文件");

            string cfgC = Path.Combine(root, "c.json");
            var c = new StartupSettings { DefaultChapter = "chapter3" }; c.Save(cfgC);
            var tc = StartupSettings.ResolveTarget(gameRoot, null, cfgC);
            Check(tc.FromDefault && tc.IsBaseline && tc.Chapter == 3, "显式 chapter3 无 live → 原版基线只读");

            string cfgD = Path.Combine(root, "d.json");
            var d = new StartupSettings { DefaultChapter = "auto" }; d.Save(cfgD);
            var td = StartupSettings.ResolveTarget(gameRoot, null, cfgD);
            Check(td.FromDefault && td.Chapter == 2 && !td.IsBaseline, "auto 且无 chapter1 → chapter2");

            string emptyRoot = Path.Combine(root, "empty");
            Directory.CreateDirectory(emptyRoot);
            var te = StartupSettings.ResolveTarget(emptyRoot, null, cfgD);
            Check(te.Path.EndsWith(Path.Combine("chapter1_windows", "data.win"), StringComparison.OrdinalIgnoreCase), "全缺失 → 回退 chapter1 路径");

            Check(StartupSettings.GuessChapter(Path.Combine(gameRoot, "chapter3_windows", "data.win")) == 3, "GuessChapter(chapter3)=3");
            Check(StartupSettings.IsBaselinePath(Path.Combine(gameRoot, "backup", "chapter3_windows", "data.win")), "IsBaselinePath 识别 backup");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
        Console.WriteLine(_fails == 0 ? "SETTINGS-TEST PASS" : $"SETTINGS-TEST FAIL ({_fails})");
        return _fails == 0 ? 0 : 1;
    }
}
