using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>
/// PatchMerge（行级三方合并）的纯逻辑自检 —— 不碰 data.win，毫秒级，可进 test.ps1。
/// 覆盖：无重叠 / 两侧改不同位置 / 同行冲突 / 两侧同点插入 / 首行前插入（锚点位移）/
///       删除 / 两侧同样改动 / 尾部插入 / 超大文件走唯一行锚点分支 / 空输入。
/// </summary>
public static class MergeSelfTest
{
    private static int _pass, _fail;

    public static int Run()
    {
        Console.WriteLine(L("===== PatchMerge 自检（行级三方合并）====="));

        // 1) 两侧都没改：结果 = base
        Check("两侧未改", "a\nb\nc", "a\nb\nc", "a\nb\nc", "a\nb\nc", 0);

        // 2) 只有 patch 改：结果 = patch（今天的整体覆盖行为）
        Check("只有 patch 改", "a\nb\nc", "a\nb\nc", "a\nB2\nc", "a\nB2\nc", 0);

        // 3) 只有基底改：结果 = 基底（patch 无改动）
        Check("只有基底改", "a\nb\nc", "a\nB2\nc", "a\nb\nc", "a\nB2\nc", 0);

        // 4) ★ 核心：两侧改不同位置 → 自动合并
        Check("两侧改不同位置",
            "a\nb\nc\nd\ne",
            "a\nOURS1\nc\nd\ne",
            "a\nb\nc\nTHEIRS1\ne",
            "a\nOURS1\nc\nTHEIRS1\ne", 0);

        // 5) 同一行冲突 → 保守取基底（ours），记 1 处冲突
        Check("同行冲突取基底",
            "a\nb\nc",
            "a\nOURS\nc",
            "a\nTHEIRS\nc",
            "a\nOURS\nc", 1);

        // 6) 两侧在同一位置各插一行（不同内容）→ 冲突，取基底插入
        Check("同点插入冲突",
            "a\nb",
            "a\nOX\nb",
            "a\nTX\nb",
            "a\nOX\nb", 1);

        // 7) 两侧同点插入相同内容 → 不冲突，只留一份
        Check("同点插入相同",
            "a\nb",
            "a\nSAME\nb",
            "a\nSAME\nb",
            "a\nSAME\nb", 0);

        // 8) ★ 首行前插入（对齐锚点位移，旧实现会漏行）
        Check("首行前插入",
            "a\nb",
            "X\nY\na\nb",
            "Z\na\nb",
            "X\nY\na\nb", 1);

        // 9) 尾部插入（双方）
        Check("尾部插入",
            "a\nb",
            "a\nb\nOURLINE",
            "a\nb\nTHEIRLINE",
            "a\nb\nOURLINE", 1);

        // 10) patch 删掉的那一行恰好是基底改过的那一行 → 冲突，保守保留基底的改动
        Check("删除撞上改动",
            "a\nb\nc\nd",
            "a\nB2\nc\nd",
            "a\nc\nd",
            "a\nB2\nc\nd", 1);

        // 10b) 反之：基底删掉一行、patch 改了那一行 → 冲突，保守保留基底的删除
        Check("基底删除撞上 patch 改动",
            "a\nb",
            "a",
            "a\nB",
            "a", 1);

        // 11) patch 把自己改过的行再改（基底也改过同一行）→ 冲突
        Check("同区块双改",
            "x\na\ny",
            "x\nOURS_A\ny",
            "x\nTHEIRS_A\ny",
            "x\nOURS_A\ny", 1);

        // 12) 空输入
        Check("空 base", "", "", "a\nb", "a\nb", 0);
        Check("空 patch（等于删掉整段）", "a\nb", "a\nB2", "", "a\nB2", 1);
        Check("空 ours（基底整段删除，patch 没动）", "a\nb", "", "a\nb", "", 0);

        // 13) 超大文件（>4M 行对）走唯一行锚点分支
        {
            var N = 2200;
            var baseL = new System.Collections.Generic.List<string>();
            var oursL = new System.Collections.Generic.List<string>();
            var theirsL = new System.Collections.Generic.List<string>();
            for (int i = 0; i < N; i++)
            {
                baseL.Add("line_" + i + " = " + (i * 3) + ";");
                oursL.Add("line_" + i + " = " + (i * 3) + ";");
                theirsL.Add("line_" + i + " = " + (i * 3) + ";");
            }
            oursL[10] = "line_10 = 9999;";
            theirsL[2000] = "line_2000 = 7777;";
            var (txt, cf, _) = PatchMerge.Merge3(string.Join("\n", baseL), string.Join("\n", oursL), string.Join("\n", theirsL));
            var ok = cf == 0 && txt.Contains("line_10 = 9999;") && txt.Contains("line_2000 = 7777;") && txt.Contains("line_100 = 300;");
            Report("大文件（唯一行锚点分支）", ok, "冲突=" + cf);
        }

        // 14) 两侧各改一行（不增删）→ 两处改动都保留，行数守恒
        {
            var (txt, cf, _) = PatchMerge.Merge3("a\nb\nc", "a\nB\nc", "a\nb\nC");
            var lines = PatchMerge.SplitLines(txt);
            Report("行数守恒", cf == 0 && lines.Length == 3 && lines[1] == "B" && lines[2] == "C",
                "冲突=" + cf + " 行数=" + lines.Length + " [" + string.Join("|", lines) + "]");
        }

        Console.WriteLine(L("===== PatchMerge 自检结果: {0}/{1} 通过 =====", _pass, _pass + _fail));
        return _fail == 0 ? 0 : 1;
    }

    private static void Check(string name, string baseT, string oursT, string theirsT, string expect, int expectConflicts)
    {
        string txt; int cf;
        try { (txt, cf, _) = PatchMerge.Merge3(baseT, oursT, theirsT); }
        catch (System.Exception ex) { Report(name, false, "异常: " + ex.Message); return; }
        var got = txt.TrimEnd('\n');
        var ok = got == expect && cf == expectConflicts;
        Report(name, ok, ok ? "" : "期望 [" + expect.Replace("\n", " / ") + "] 冲突=" + expectConflicts
                                  + "  实际 [" + got.Replace("\n", " / ") + "] 冲突=" + cf);
    }

    private static void Report(string name, bool ok, string detail)
    {
        if (ok) { _pass++; Console.WriteLine("  [通过] " + name); }
        else { _fail++; Console.WriteLine("  [失败] " + name + (detail.Length > 0 ? "  ← " + detail : "")); }
    }
}
