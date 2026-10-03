using System.Text;
using System.Text.RegularExpressions;
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
///   dr.ini 键 "Name"       → 存档槽显示的名字（与存档文件第 1 行同一个值）
///   存档文件名             → filech&lt;chapter&gt;_&lt;slot&gt;（纯文本，行序同 scr_saveprocess）
///
/// 正确的做法是：用一份**真实的 B 面存档内容**作为模板写入目标槽位，
/// 并同步 INI 的 SideB 标记 —— 而不是凭空把 SideB 设为 1（那样存档内容与标记不一致）。
///
/// 关于「名字」：存档文件**第 1 行就是角色名**（scr_saveprocess 的第一个字段），
/// 玩家在游戏里命名时最多 12 个字母（原版命名界面 DEVICE_CHOICE 的 STRINGMAX = 12）。导入的存档若第 1 行为空（例如纯数值备份），
/// 游戏里就会显示成没有名字/默认值 ⇒ 导入时必须把玩家输入的名字写进第 1 行，
/// 并同步 dr.ini 的 Name，否则「存档内容」与「存档槽显示」会各说各话。
/// </summary>
public static class BSide
{
    /// <summary>游戏内命名界面的名字上限：原版 DEVICE_CHOICE（软键盘）STRINGMAX = 12，即最多 12 个字母。</summary>
    public const int MaxNameChars = 12;

    /// <summary>模板目录 Neutraled/bside/。里面有两份来源不同的模板，**物理分开**是为了让个人存档不可能混进发布包：
    ///   · bside/chapterN.sav            —— 玩家自己用 --import-bside 导入的模板（个人数据，打包时被排除）；
    ///   · bside/templates/chapterN.sav  —— **随包分发的默认模板**（已清洗、不含个人数据；打包器只保留这一层）。
    /// --make-bside 的取用顺序见 ResolveTemplate。</summary>
    public static string TemplateDir(string gameRoot) => Path.Combine(Paths.NeutraledRoot(gameRoot), "bside");

    public static string TemplatePath(string gameRoot, int chapter) =>
        Path.Combine(TemplateDir(gameRoot), $"chapter{chapter}.sav");

    /// <summary>随包默认模板目录：Neutraled/bside/templates/（发布包里只带这里的 *.sav）。</summary>
    public static string ShippedTemplateDir(string gameRoot) => Path.Combine(TemplateDir(gameRoot), "templates");

    /// <summary>随包默认模板：Neutraled/bside/templates/chapterN.sav。</summary>
    public static string ShippedTemplatePath(string gameRoot, int chapter) =>
        Path.Combine(ShippedTemplateDir(gameRoot), $"chapter{chapter}.sav");

    /// <summary>按「先用玩家自己导入的，再用随包默认的」找该章节的 B 面模板。
    /// 返回 null = 两份都没有（调用方走「把现有存档标记为 B 面」的兜底）。
    /// <paramref name="shipped"/> = true 表示用的是随包默认模板（日志要能说清是哪一份，
    /// 否则玩家会以为自己的导入没生效）。</summary>
    public static string? ResolveTemplate(string gameRoot, int chapter, out bool shipped)
    {
        var own = TemplatePath(gameRoot, chapter);
        if (File.Exists(own)) { shipped = false; return own; }
        var def = ShippedTemplatePath(gameRoot, chapter);
        if (File.Exists(def)) { shipped = true; return def; }
        shipped = false;
        return null;
    }

    /// <summary>玩家存档目录（与 junction 目标一致）。</summary>
    public static string SaveDir(string gameRoot) =>
        Path.Combine(Paths.NeutraledRoot(gameRoot), "saves", "DELTARUNE");

    /// <summary>dr.ini 段名（= 游戏里的 scr_ini_chapter(ch, slot)）：ch>=2 是 G_&lt;ch&gt;_&lt;slot&gt;，第 1 章是 G&lt;slot&gt;。</summary>
    public static string Section(int chapter, int slot) => chapter >= 2 ? $"G_{chapter}_{slot}" : $"G{slot}";

    /// <summary>从存档文件名识别章节 / 槽位：filech2 / filech2_0 / filech2_9 / chapter2.sav / ch2-1。
    /// 识别不出返回 false（章节 0）—— 调用方应提示显式 --chapter。</summary>
    public static bool TryParseTarget(string sourcePath, out int chapter, out int slot)
    {
        chapter = 0; slot = 0;
        if (string.IsNullOrWhiteSpace(sourcePath)) return false;
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        var m = Regex.Match(name, @"^(?:file)?ch(?:apter)?[_\-\s]*(\d{1,2})(?:[_\-\s]+(\d{1,2}))?$", RegexOptions.IgnoreCase);
        if (!m.Success) return false;
        if (!int.TryParse(m.Groups[1].Value, out var c) || c < 1 || c > 9) return false;
        chapter = c;
        if (m.Groups[2].Success && int.TryParse(m.Groups[2].Value, out var s) && s >= 0) slot = s;
        return true;
    }

    /// <summary>校验并规范化玩家名字。失败返回 null 并给出原因（error）。
    /// 规范化 = 去掉换行/首尾空白、禁止控制字符、长度 1..<see cref="MaxNameChars"/>、
    /// **转大写**（游戏内命名界面只能输入大写字母，跟着同一约定才和游戏自己的存档一致）。</summary>
    public static string? NormalizeName(string? raw, out string? error)
    {
        error = null;
        var s = (raw ?? "").Replace("\r", "").Replace("\n", "").Trim();
        if (s.Length == 0) { error = L("名字不能是空的"); return null; }
        foreach (var ch in s)
            if (char.IsControl(ch)) { error = L("名字里不能有控制字符"); return null; }
        if (s.Length > MaxNameChars)
        {
            error = L("名字太长：最多 {0} 个字母（游戏内命名上限），当前 {1} 个", MaxNameChars, s.Length);
            return null;
        }
        return s.ToUpperInvariant();
    }

    /// <summary>导入选项。</summary>
    public sealed class ImportOptions
    {
        /// <summary>源存档路径（例如 E:\aiwork\in\filech2）。</summary>
        public string Source = "";
        /// <summary>目标章节；0 = 从文件名自动识别。</summary>
        public int Chapter;
        /// <summary>目标槽位；-1 = 从文件名自动识别（识别不出用 0）。</summary>
        public int Slot = -1;
        /// <summary>玩家名字；空 = 交互式询问（终端）/ 报错（非终端，需要 --save-name）。</summary>
        public string? Name;
        /// <summary>true（默认）＝ 写进 Neutraled/saves/DELTARUNE/filech&lt;ch&gt;_&lt;slot&gt; 并标记 SideB；
        /// false = 只导入模板（--template-only）。</summary>
        public bool Apply = true;
        /// <summary>Apply 时写入 0/1/2 三个槽位。</summary>
        public bool AllSlots;
        /// <summary>允许交互式询问名字（GUI/管道调用时为 false）。</summary>
        public bool AskName = true;
    }

    /// <summary>导入一份**真实的**存档作为 B 面模板并（默认）直接落到槽位、标记 B 面。
    ///
    /// 步骤（每一步都对应一条真实规则，不是可选装饰）：
    ///   1) 按实际章节把模板改名成 bside/chapter&lt;N&gt;.sav（--import-bside 的源文件名千奇百怪）；
    ///   2) 把玩家名字写进存档第 1 行（≤12 个字母）—— 存档第 1 行就是角色名，空着游戏里就是无名；
    ///   3) --template-only 之外：写入 saves/DELTARUNE/filech&lt;N&gt;_&lt;slot&gt;，并写 dr.ini 的
    ///      SideB="1.000000" + Name="&lt;名字&gt;"（标记与内容必须一致）。
    /// 返回 0 成功 / 1 失败。</summary>
    public static int Import(string gameRoot, ImportOptions opt)
    {
        if (string.IsNullOrWhiteSpace(opt.Source) || !File.Exists(opt.Source))
        {
            Paths.Log(L("  [错误] 存档不存在: {0}", opt.Source ?? ""));
            return 1;
        }

        // ---- 章节 / 槽位 ----
        int chapter = opt.Chapter, slot = opt.Slot;
        if (chapter <= 0 || slot < 0)
        {
            if (TryParseTarget(opt.Source, out var c2, out var s2))
            {
                if (chapter <= 0) chapter = c2;
                if (slot < 0) slot = s2;
                Paths.Log(L("  已按文件名识别: 第 {0} 章 / 槽位 {1}", chapter, slot));
            }
        }
        if (chapter <= 0)
        {
            Paths.Log(L("  [错误] 认不出这是第几章的存档: {0}", Path.GetFileName(opt.Source)));
            Paths.Log(L("         文件名要像 filech2 / filech3_0，或者显式给出 --chapter chapterN"));
            return 1;
        }
        if (slot < 0) slot = 0;

        var lines = File.ReadAllLines(opt.Source);
        if (lines.Length == 0)
        {
            Paths.Log(L("  [错误] 存档是空文件: {0}", opt.Source));
            return 1;
        }

        // ---- 名字（存档第 1 行）----
        var name = string.IsNullOrWhiteSpace(opt.Name) ? null : opt.Name;
        if (name == null && opt.AskName && !Console.IsInputRedirected)
        {
            for (int tries = 0; tries < 3 && name == null; tries++)
            {
                Console.Write(L("  请输入角色名字（最多 {0} 个字母，直接回车取消）: ", MaxNameChars));
                var input = Console.ReadLine();
                if (input == null || input.Trim().Length == 0)
                {
                    Paths.Log(L("  已取消：存档第 1 行就是角色名，没有名字就不导入"));
                    return 1;
                }
                var ok = NormalizeName(input, out var err);
                if (ok == null) Paths.Log("  " + err);
                else name = ok;
            }
        }
        if (name == null)
        {
            var fixedName = NormalizeName(opt.Name, out var err0);
            if (fixedName == null)
            {
                if (!string.IsNullOrWhiteSpace(opt.Name)) Paths.Log("  " + err0);
                Paths.Log(L("  [错误] 没有名字。用 --save-name <名字> 指定（最多 {0} 个字母）：", MaxNameChars));
                Paths.Log(L("         游戏里存档第 1 行就是角色名，名字为空的话存档槽会显示不出来"));
                return 1;
            }
            name = fixedName;
        }
        else
        {
            // ★ 显式传入的名字也要走同一套校验：NormalizeName 返回 null 就是非法（超长/含控制字符），
            //   这里绝不能 `?? name` 把未校验的原值放过去（否则 10 个字母的名字会照写不误）。
            var ok2 = NormalizeName(name, out var err2);
            if (ok2 == null) { Paths.Log("  " + err2); return 1; }
            name = ok2;
        }

        if (!name.Equals(lines[0].Trim(), StringComparison.Ordinal))
            Paths.Log(L("  名字（存档第 1 行）: {0}（原来是 \"{1}\"）", name, lines[0].Trim()));
        else
            Paths.Log(L("  名字（存档第 1 行）: {0}", name));
        lines[0] = name;

        // ---- 写模板（文件名按实际章节改）----
        Directory.CreateDirectory(TemplateDir(gameRoot));
        var tmpl = TemplatePath(gameRoot, chapter);
        var text = string.Join("\r\n", lines) + "\r\n";
        File.WriteAllText(tmpl, text, new UTF8Encoding(false));
        Paths.Log(L("  B 面模板已导入: {0}（{1} 行）", tmpl, lines.Length));
        if (File.Exists(ShippedTemplatePath(gameRoot, chapter)))
            Paths.Log(L("  这是你自己的模板（脚本/打包只认 bside/templates/ 里的随包模板，这份不会进发布包）"));

        if (!opt.Apply)
        {
            Paths.Log(L("  --template-only：没写进游戏存档，也没标记 SideB（之后可用 --make-bside 落槽位）"));
            return 0;
        }

        // ---- 落到槽位 + 标记 SideB ----
        var saveDir = SaveDir(gameRoot);
        Directory.CreateDirectory(saveDir);
        var ini = Path.Combine(saveDir, "dr.ini");
        var slots = opt.AllSlots ? new[] { 0, 1, 2 } : new[] { slot };

        foreach (var s3 in slots)
        {
            var target = Path.Combine(saveDir, "filech" + chapter + "_" + s3);
            File.WriteAllText(target, text, new UTF8Encoding(false));
            Paths.Log(L("  已写入 B 面存档: ") + target);
            SetIniEntry(ini, Section(chapter, s3), "SideB", "1.000000");
            SetIniEntry(ini, Section(chapter, s3), "Name", name);
        }
        Paths.Log(L("  已标记 dr.ini: 第 {0} 章 槽位 {1} → SideB=1 / Name=\"{2}\"",
            chapter, string.Join(",", slots), name));

        return 0;
    }

    /// <summary>把模板应用到指定槽位（生成 filech&lt;ch&gt;_&lt;slot&gt; 并设置 INI 的 SideB=1）。
    ///
    /// 模板取用顺序（ResolveTemplate）：玩家自己导入的 bside/chapterN.sav → 随包默认的
    /// bside/templates/chapterN.sav → 都没有才走下面的兜底。
    ///
    /// **没有模板时的兜底**：如果该章节槽位**已经有存档**（玩家正常开过一次），
    /// 就直接把它标记成 B 面 —— 这样「任意章节直接开 B 面」不必先弄到一份 B 面模板。
    /// （存档内容就是这一章的起始状态，标记与内容一致，不是凭空伪造。）</summary>
    public static int MakeBSideSave(string gameRoot, int chapter, int slot, bool allSlots = false)
    {
        var tmpl = ResolveTemplate(gameRoot, chapter, out var shipped);
        var saveDir = SaveDir(gameRoot);
        Directory.CreateDirectory(saveDir);

        bool haveTmpl = tmpl != null;
        if (haveTmpl)
            Paths.Log(L("  B 面模板: {0}{1}", tmpl!, shipped ? L("（随包默认）") : L("（你自己导入的）")));
        var slots = allSlots ? new[] { 0, 1, 2 } : new[] { slot };
        int made = 0, converted = 0, missing = 0;

        foreach (var s2 in slots)
        {
            var target = Path.Combine(saveDir, "filech" + chapter + "_" + s2);
            if (haveTmpl)
            {
                File.Copy(tmpl!, target, true);
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

    /// <summary>写 dr.ini 的 [G_&lt;ch&gt;_&lt;slot&gt;] SideB=1（不存在则追加段）。</summary>
    private static void SetIniSideB(string iniPath, int chapter, int slot) =>
        SetIniEntry(iniPath, Section(chapter, slot), "SideB", "1.000000");

    /// <summary>写 dr.ini 的一个键（值会加双引号，格式与游戏自己写的一致）：段不存在则追加段，键不存在则插到段首。</summary>
    private static void SetIniEntry(string iniPath, string section, string key, string value)
    {
        var lines = File.Exists(iniPath) ? File.ReadAllLines(iniPath).ToList() : new List<string>();

        int secStart = -1, secEnd = lines.Count;
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Trim() == $"[{section}]") { secStart = i; continue; }
            if (secStart >= 0 && lines[i].TrimStart().StartsWith("[")) { secEnd = i; break; }
        }

        var entry = key + "=\"" + value + "\"";

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
                var t = lines[i].TrimStart();
                if (t.StartsWith(key + "=") || t.StartsWith(key + " ="))
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
