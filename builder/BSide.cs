using System.Text;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>B-side（B 面）存档支持。
///
/// 游戏机制（反编译实证）：
///   global.flag[915]  = B 面进度（阶段阈值 1.5 / 4 / 7 / 20）
///   global.flag[916]  = B 面失败/结束标记（0 = 进行中）
///   scr_sideb_get_phase()  → 由 flag[915] 得到阶段 1/2/3
///   scr_sideb_active()     → phase >= 3，即 flag[915] >= 7
///   dr.ini 段名            → scr_ini_chapter(ch, slot) = "G_&lt;ch&gt;_&lt;slot&gt;"（ch>=2）或 "G&lt;slot&gt;"
///   dr.ini 键 "SideB"      → 该存档是否为 B 面（通关时由 scr_complete_save_file 写入）
///   存档文件名             → filech&lt;chapter&gt;_&lt;slot&gt;（纯文本，行序同 scr_saveprocess）
///
/// 正确的做法是：用一份**真实的 B 面存档内容**作为模板写入目标槽位，
/// 并同步 INI 的 SideB 标记 —— 而不是凭空把 SideB 设为 1（那样存档内容与标记不一致）。
/// </summary>
public static class BSide
{
    /// <summary>模板目录：Neutraled/bside/chapterN.sav（由 --import-bside 放入）。</summary>
    public static string TemplateDir(string gameRoot) => Path.Combine(Paths.NeutraledRoot(gameRoot), "bside");

    public static string TemplatePath(string gameRoot, int chapter) =>
        Path.Combine(TemplateDir(gameRoot), $"chapter{chapter}.sav");

    /// <summary>导入一份 B 面存档作为模板。</summary>
    public static int ImportTemplate(string gameRoot, string sourcePath, int chapter)
    {
        if (!File.Exists(sourcePath)) { Paths.Log(L("  [错误] 存档不存在: {0}", sourcePath)); return 1; }
        Directory.CreateDirectory(TemplateDir(gameRoot));
        var dst = TemplatePath(gameRoot, chapter);
        File.Copy(sourcePath, dst, true);

        // 顺带报告存档概要（前几个字段）
        var lines = File.ReadAllLines(sourcePath);
        Paths.Log(L("  B 面模板已导入: {0}（{1} 行）", dst, lines.Length));
        if (lines.Length > 0) Paths.Log($"    true name : {lines[0].Trim()}");
        if (lines.Length > 1) Paths.Log($"    other[0]  : {lines[1].Trim()}");
        return 0;
    }

    /// <summary>把模板应用到指定槽位（生成 filech&lt;ch&gt;_&lt;slot&gt; 并设置 INI 的 SideB=1）。
    ///
    /// **没有模板时的兜底**：如果该章节槽位**已经有存档**（玩家正常开过一次），
    /// 就直接把它标记成 B 面 —— 这样「任意章节直接开 B 面」不必先弄到一份 B 面模板。
    /// （存档内容就是这一章的起始状态，标记与内容一致，不是凭空伪造。）</summary>
    public static int MakeBSideSave(string gameRoot, int chapter, int slot, bool allSlots = false)
    {
        var tmpl = TemplatePath(gameRoot, chapter);
        var saveDir = SaveDir(gameRoot);
        Directory.CreateDirectory(saveDir);

        bool haveTmpl = File.Exists(tmpl);
        var slots = allSlots ? new[] { 0, 1, 2 } : new[] { slot };
        int made = 0, converted = 0, missing = 0;

        foreach (var s2 in slots)
        {
            var target = Path.Combine(saveDir, "filech" + chapter + "_" + s2);
            if (haveTmpl)
            {
                File.Copy(tmpl, target, true);
                Paths.Log(L("  已写入 B 面存档: ") + target);
                made++;
            }
            else if (File.Exists(target))
            {
                Paths.Log(L("  已把现有存档标记为 B 面: ") + target + L("（无模板，按现有内容处理）"));
                converted++;
            }
            else missing++;
        }

        if (made == 0 && converted == 0)
        {
            Paths.Log(L("  [错误] 第 ") + chapter + L(" 章既没有 B 面模板、也没有现成存档，无法凭空造一个（标记与内容必须一致）"));
            Paths.Log(L("         两条路任选："));
            Paths.Log(L("           1) 先正常进一次第 ") + chapter + L(" 章（游戏会生成该槽位存档），再跑一次本命令 → 会把它标记成 B 面"));
            Paths.Log(L("           2) 用 --import-bside <一份真实的B面存档> --chapter ") + chapter + L(" 导入模板后，可一键生成任意槽位"));
            return 1;
        }

        var iniPath = Path.Combine(saveDir, "dr.ini");
        foreach (var s3 in slots)
            if (File.Exists(Path.Combine(saveDir, "filech" + chapter + "_" + s3))) SetIniSideB(iniPath, chapter, s3);
        Paths.Log(L("  已标记 dr.ini SideB=1（章节 ") + chapter + L(" 槽位 ") + string.Join(",", slots) + L("）"));
        if (missing > 0) Paths.Log(L("  [警告] ") + missing + L(" 个槽位既无模板也无存档，已跳过"));
        return 0;
    }

    /// <summary>玩家存档目录（与 junction 目标一致）。</summary>
    public static string SaveDir(string gameRoot) =>
        Path.Combine(Paths.NeutraledRoot(gameRoot), "saves", "DELTARUNE");

    /// <summary>写 dr.ini 的 [G_&lt;ch&gt;_&lt;slot&gt;] SideB=1（不存在则追加段）。</summary>
    private static void SetIniSideB(string iniPath, int chapter, int slot)
    {
        var section = chapter >= 2 ? $"G_{chapter}_{slot}" : $"G{slot}";
        var lines = File.Exists(iniPath) ? File.ReadAllLines(iniPath).ToList() : new List<string>();

        int secStart = -1, secEnd = lines.Count;
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Trim() == $"[{section}]") { secStart = i; continue; }
            if (secStart >= 0 && lines[i].TrimStart().StartsWith("[")) { secEnd = i; break; }
        }

        var entry = "SideB=\"1.000000\"";

        if (secStart < 0)
        {
            lines.Add($"[{section}]");
            lines.Add(entry);
        }
        else
        {
            bool replaced = false;
            for (int i = secStart + 1; i < secEnd; i++)
            {
                if (lines[i].TrimStart().StartsWith("SideB"))
                {
                    lines[i] = entry;
                    replaced = true;
                    break;
                }
            }
            if (!replaced) lines.Insert(secStart + 1, entry);
        }

        File.WriteAllLines(iniPath, lines, new UTF8Encoding(false));
    }
}
