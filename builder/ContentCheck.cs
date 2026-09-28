using System.Text;
using System.Text.Json;
using UndertaleModLib;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>内容级自检：**语言文本 × 字体字形覆盖**。
///
/// 为什么需要它（2026-09-25 实测的教训）：
/// 汉化包把 lang/lang_en.json 换成了中文，但基底选错 → fnt_main 只有 95 个 ASCII 字形
/// （正确基底是 3580 字形的中文像素字体）→ 章节里**一个字都画不出来**（传说全空白）。
/// 而当时所有自动化检查**全绿** —— lint / doctor / smoke / 部署自检(DeploySelfCheck) 都只看
/// 「注入是否完整、对象是否绑定」，**不看「文本能不能被真的画出来」**。
/// 于是"全量测试通过"与"游戏里什么都看不见"同时成立。
///
/// 判据：对每个目标，取「游戏真正会显示的字符集合」与「字体字形集合」求覆盖：
///   - 章节：lang/lang_en.json 里所有字符串值（游戏默认英文语言档；汉化即替换此文件）
///   - root：Neutraled/api/ntl_i18n_init.gml 的中文词条 + chapters.json 的章节名
///     （章节选择器/控制台界面就用这些文本绘制）
/// 覆盖不足即失败，并列出**渲染不出来的样例字符** —— 一眼看出是"字体没跟上文本"。
///
/// 用法：ntl-builder --content-check [--chapter all|root|chapterN] [--out report.json]
/// 返回 0 = 全部通过；1 = 有目标失败。
/// </summary>
public static class ContentCheck
{
    /// <summary>覆盖率达到此值算通过。</summary>
    public const double PassCoverage = 0.99;
    /// <summary>低于此值算失败，中间算警告。</summary>
    public const double WarnCoverage = 0.90;
    /// <summary>缺失样例最多列几个字符。</summary>
    public const int SampleMax = 24;

    public sealed class FontStat
    {
        public string Name { get; set; } = "";
        public int Glyphs { get; set; }
        public double Coverage { get; set; }
        public bool HasSpace { get; set; }
        public int SpaceShift { get; set; } = -1;
    }

    public sealed class Report
    {
        public string Chapter { get; set; } = "";
        public string Win { get; set; } = "";
        public long WinBytes { get; set; }
        public string Source { get; set; } = "";
        public int NeedChars { get; set; }
        public int NeedCjk { get; set; }
        public double BestCoverage { get; set; }
        public string BestFont { get; set; } = "";
        public int MainGlyphs { get; set; }
        public double MainCoverage { get; set; }
        public int MissingCount { get; set; }
        public string MissingSample { get; set; } = "";
        public List<FontStat> Fonts { get; set; } = new();
        /// <summary>通过 / 警告 / 失败 / 跳过</summary>
        public string Verdict { get; set; } = "";
        public string Detail { get; set; } = "";
        public bool Ok => Verdict == "通过";
    }

    // ------------------------------------------------------------------ 入口

    public static int Run(string gameRoot, string chapterArg, string? outPath = null)
    {
        var targets = ResolveTargets(gameRoot, chapterArg);
        Console.WriteLine(L("===== 内容级自检（语言文本 × 字体字形）====="));
        Console.WriteLine(L("  检查目标: ") + string.Join(" ", targets));
        Console.WriteLine(L("  判据: 需要渲染的字符中，能被字体画出的比例（>= ") + Pct(PassCoverage) + L(" 通过 / >= ") + Pct(WarnCoverage) + L(" 警告）"));
        var reports = new List<Report>();
        int fails = 0, warns = 0;

        foreach (var chapter in targets)
        {
            Report rep;
            try { rep = CheckTarget(gameRoot, chapter, null, verbose: true,
                rootUiOnly: string.Equals(chapter, "root", StringComparison.OrdinalIgnoreCase)); }
            catch (Exception ex)
            {
                rep = new Report { Chapter = chapter, Verdict = "失败", Detail = L("自检本身抛异常 —— ") + Describe(ex) };
                Console.WriteLine("[" + chapter + L("] 失败: ") + rep.Detail);
            }
            reports.Add(rep);
            if (rep.Verdict == "失败") fails++;
            else if (rep.Verdict == "警告") warns++;
        }

        Console.WriteLine(L("结果: ") + (fails == 0
            ? (warns == 0 ? L("全部通过（") + reports.Count + L(" 个目标）") : L("通过但含 ") + warns + L(" 个警告（") + reports.Count + L(" 个目标）"))
            : L("发现严重问题（失败 ") + fails + L(" 项") + (warns > 0 ? L("，警告 ") + warns + L(" 项") : "") + L("）")));

        if (!string.IsNullOrWhiteSpace(outPath))
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
                File.WriteAllText(outPath, JsonSerializer.Serialize(reports, Paths.Json), Encoding.UTF8);
                Console.WriteLine(L("  JSON 报告: ") + Path.GetFullPath(outPath));
            }
            catch (Exception ex) { Console.WriteLine(L("  [警告] 写 JSON 报告失败: ") + Describe(ex)); }
        }
        return fails == 0 ? 0 : 1;
    }

    /// <summary>单目标检查。loaded 非空时复用调用方已加载的 UndertaleData（避免二次 Load 大产物）。</summary>
    public static Report CheckTarget(string gameRoot, string chapter, UndertaleData? loaded, bool verbose, string? winOverride = null, bool rootUiOnly = false)
    {
        var rep = new Report { Chapter = chapter };
        var win = string.IsNullOrWhiteSpace(winOverride) ? Paths.ChapterDataWin(gameRoot, chapter) : winOverride!;
        rep.Win = win;

        if (!File.Exists(win))
        {
            rep.Verdict = "跳过";
            rep.Detail = L("产物不存在");
            if (verbose) Console.WriteLine("[" + chapter + L("] 跳过 —— 产物不存在: ") + win);
            return rep;
        }
        rep.WinBytes = new FileInfo(win).Length;

        // ---------- 1. 需要渲染的字符 ----------
        var (need, source) = NeedChars(gameRoot, chapter, win);
        rep.Source = source;
        rep.NeedChars = need.Count;
        rep.NeedCjk = need.Count(IsCjk);
        if (need.Count == 0)
        {
            rep.Verdict = "跳过";
            rep.Detail = L("取不到需要渲染的文本（") + source + L("）");
            if (verbose) Console.WriteLine("[" + chapter + L("] 跳过 —— ") + rep.Detail);
            return rep;
        }

        // ---------- 2. 字体字形 ----------
        var data = loaded ?? Injector.Load(win);
        try
        {
            foreach (var f in data.Fonts)
            {
                var name = f.Name?.Content ?? L("(无名)");
                var glyphs = new HashSet<int>();
                if (f.Glyphs != null) foreach (var g in f.Glyphs) if (g != null) glyphs.Add(g.Character);
                int covered = 0;
                foreach (var c in need) if (glyphs.Contains(c)) covered++;
                var st = new FontStat
                {
                    Name = name,
                    Glyphs = glyphs.Count,
                    Coverage = covered / (double)need.Count
                };
                if (glyphs.Contains(0x20))
                {
                    st.HasSpace = true;
                    var sp = f.Glyphs?.FirstOrDefault(g => g != null && g.Character == 0x20);
                    if (sp != null) st.SpaceShift = sp.Shift;
                }
                rep.Fonts.Add(st);
            }

        // ---------- 3. 判定 ----------
        var best = rep.Fonts.OrderByDescending(x => x.Coverage).ThenByDescending(x => x.Glyphs).FirstOrDefault();
        var main = rep.Fonts.FirstOrDefault(x => x.Name == "fnt_main")
                   ?? rep.Fonts.FirstOrDefault(x => x.Name != null && x.Name.StartsWith("fnt_main", StringComparison.OrdinalIgnoreCase));
        rep.BestCoverage = best?.Coverage ?? 0;
        rep.BestFont = best?.Name ?? L("(无字体)");
        rep.MainGlyphs = main?.Glyphs ?? 0;
        rep.MainCoverage = main?.Coverage ?? 0;

        var missing = new List<char>();
        if (best != null)
        {
            // 缺失样例按**判定字体**取：主字体存在就看主字体缺什么，否则看最佳字体
            var keep = new HashSet<int>();
            foreach (var f in data.Fonts)
            {
                var fn = f.Name?.Content ?? "";
                bool use = rep.MainGlyphs > 0 ? fn == "fnt_main" : fn == best.Name;
                if (use && f.Glyphs != null)
                    foreach (var g in f.Glyphs) if (g != null) keep.Add(g.Character);
            }
            foreach (var c in need) if (!keep.Contains(c)) missing.Add(c);
        }
        missing.Sort();
        rep.MissingCount = missing.Count;
        rep.MissingSample = string.Concat(missing.Take(SampleMax));
        }
        finally
        {
            // 必须在**所有** data 访问之后才释放（实测：Dispose 后再读 data 会 NRE）
            if (loaded == null) (data as IDisposable)?.Dispose();
        }

        // 判据以**主字体**为准：游戏正文（图例 / 存档界面 / 菜单）用 fnt_main 绘制。
        // 只看「最佳字体」会放过假通过（fnt_main 是 ASCII、某个杂项字体恰好覆盖）。
        bool mainOk = rep.MainGlyphs > 0 && rep.MainCoverage >= PassCoverage;
        if (mainOk) rep.Verdict = "通过";
        else if (rep.BestCoverage >= WarnCoverage) rep.Verdict = "警告";
        else rep.Verdict = "失败";
        if (!mainOk && rep.MainGlyphs > 0 && rep.MainCoverage < WarnCoverage && rep.BestCoverage >= PassCoverage)
            // 「主字体不足但有替代字体」只有在**替代字体本身就是游戏正文会用的字体**时才算警告
            // （ja 的 fnt_ja_*、汉化包里被换成 CJK 的 fnt_main…）。否则游戏正文仍旧一个字都画不出来，
            // 记「失败」—— 这与部署自检的口径一致（踩过坑：chapter1 单跑 --content-check 报「警告」，
            // 而 --deploy-all 的自检同一条却是 [失败] exit 1，两套判定打架）。
            rep.Verdict = IsGameTextFont(rep.BestFont) ? "警告" : "失败";
        // root 是我们自己的章节选择器：它画出的每一个字（api/ntl_i18n_*.gml + lang/lang_*.json）都走
        // ntl_font_* 字体，与游戏自带的 fnt_main 无关；而合成基底里 fnt_main 只有 96 个 ASCII 字形
        // （汉化正文各自带自己的字体），所以这里按「我们的字体覆盖」判定，否则每次部署都会假失败 exit 1。
        if (rootUiOnly && !string.IsNullOrEmpty(rep.BestFont) && rep.BestFont.StartsWith("ntl_font_") && rep.BestCoverage >= PassCoverage)
            rep.Verdict = "通过";

        rep.Detail = L("需要 ") + rep.NeedChars + L(" 个字符（CJK ") + rep.NeedCjk + L("）；fnt_main 覆盖 ") + Pct(rep.MainCoverage)
                     + L("（") + rep.MainGlyphs + L(" 字形）")
                     + (rep.MainGlyphs == 0 ? L("【产物里没有 fnt_main】") : "")
                     + L("；最佳字体 ") + rep.BestFont + L(" 覆盖 ") + Pct(rep.BestCoverage)
                     + L("；缺 ") + rep.MissingCount + L(" 个（按") + (rep.MainGlyphs > 0 ? "fnt_main" : rep.BestFont) + L("）");

        if (verbose) Print(rep, source);
        return rep;
    }

    private static void Print(Report rep, string source)
    {
        Console.WriteLine();
        Console.WriteLine("[" + rep.Chapter + "] " + (rep.WinBytes / 1048576.0).ToString("0.0") + " MB  " + rep.Win);
        Console.WriteLine(L("  文本来源: ") + source);
        Console.WriteLine(L("  需要渲染: ") + rep.NeedChars + L(" 个字符（其中 CJK ") + rep.NeedCjk + L("）"));
        foreach (var f in rep.Fonts.OrderByDescending(x => x.Coverage))
        {
            var line = "    " + f.Name.PadRight(18) + " Glyphs=" + f.Glyphs.ToString().PadLeft(6)
                       + L("  覆盖 ") + Pct(f.Coverage).PadLeft(7);
            if (f.Name.StartsWith("fnt_main", StringComparison.OrdinalIgnoreCase))
                line += L("   [主字体]");
            if (f.Name.StartsWith("fnt_main", StringComparison.OrdinalIgnoreCase))
                line += f.HasSpace ? L("  空格 Shift=") + f.SpaceShift + (f.SpaceShift <= 0 ? L(" ⚠(过窄/缺失 → 英文单词会粘连)") : "") : L("  ⚠ 无空格字形");
            Console.WriteLine(line);
        }
        if (rep.MissingCount > 0)
            Console.WriteLine(L("  渲染不出来的样例（") + Math.Min(rep.MissingCount, SampleMax) + "/" + rep.MissingCount + L("）: ") + rep.MissingSample);
        Console.WriteLine(L("  判定: ") + (rep.Verdict == "通过" ? L("通过") : rep.Verdict == "警告" ? L("警告") : rep.Verdict == "跳过" ? L("跳过") : L("失败")) + " —— " + rep.Detail);
    }

    // ------------------------------------------------------------------ 文本来源

    /// <summary>取「游戏真正会显示的字符集合」。章节 = lang/lang_en.json 的所有字符串值；
    /// root = Neutraled 的 i18n 词条 + chapters.json 的章节名。</summary>
    internal static (HashSet<char> chars, string source) NeedChars(string gameRoot, string chapter, string win)
    {
        var chars = new HashSet<char>();
        var src = new List<string>();

        if (!chapter.Equals("root", StringComparison.OrdinalIgnoreCase))
        {
            var langDir = Path.Combine(Path.GetDirectoryName(win) ?? "", "lang");
            var langFile = Path.Combine(langDir, "lang_en.json");
            if (!File.Exists(langFile))
            {
                // 回退：取体积最大的 lang_*.json（游戏默认英文档；没有就尽量取一个能代表文本的）
                var cand = Directory.Exists(langDir)
                    ? Directory.GetFiles(langDir, "lang_*.json").OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault()
                    : null;
                if (cand != null) { langFile = cand; }
            }
            if (File.Exists(langFile))
            {
                AddJsonStrings(chars, langFile);
                src.Add(Path.GetFileName(langFile) + L(" 的全部字符串值"));
            }
            else src.Add(L("(找不到语言文件: ") + langDir + ")");
            // ★ 我们的界面文案（CONFIG 里的「Mod 设置」行 / submenu 51 面板 / 章节选择器 / 控制台）
            //   是用**游戏自己的 mainbig 字体**画的 —— 这些字不在游戏的 lang 文件里，
            //   必须并入字符集，否则字体补全会漏掉它们，真机上界面会出现空洞
            //   （实测：UI 文案有 466 个非 ASCII 字不在 chapter1 的 lang_en.json 里）。
            AddNeutraledUiChars(chars, gameRoot, src);
            return (chars, string.Join(" + ", src));
        }

        // root：我们的界面文本（i18n）+ 章节名
        var i18n = Path.Combine(Paths.NeutraledRoot(gameRoot), "api", "ntl_i18n_init.gml");
        if (File.Exists(i18n))
        {
            foreach (var c in File.ReadAllText(i18n)) if (!char.IsWhiteSpace(c)) chars.Add(c);
            src.Add("api/ntl_i18n_init.gml");
        }
        var chaptersJson = Path.Combine(Paths.NeutraledRoot(gameRoot), "chapters.json");
        if (File.Exists(chaptersJson))
        {
            AddJsonStrings(chars, chaptersJson);
            src.Add(L("chapters.json 的章节名"));
        }
        // ★ 外部语言包（lang/lang_*.json）的译文也必须能渲染 —— 只按 zh 的 i18n 校验时，
        //   繁体/日文独有的字形缺口检不出来（实测 zh-TW 界面整片空洞）。
        var packDir = Path.Combine(Paths.NeutraledRoot(gameRoot), "lang");
        if (Directory.Exists(packDir))
        {
            var packs = Directory.GetFiles(packDir, "lang_*.json");
            foreach (var p in packs) AddJsonStrings(chars, p);
            if (packs.Length > 0) src.Add("lang/lang_*.json (" + packs.Length + ")");
        }
        AddNeutraledUiChars(chars, gameRoot, src);
        return (chars, src.Count == 0 ? L("(没有可用的文本来源)") : string.Join(" + ", src));
    }

    internal static void AddJsonStrings(HashSet<char> into, string jsonPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var sb = new StringBuilder();
        Collect(doc.RootElement, sb);
        foreach (var c in sb.ToString()) if (!char.IsWhiteSpace(c)) into.Add(c);
    }

    /// <summary>Neutraled 自己的界面文案字符集：api/**.gml 的**字符串字面量** + lang/lang_*.json 译文 + chapters.json 章节名。
    /// ★ 为什么不整文件扫：源码注释里的箭头/星号/方框等并不上屏，算进去只会拉低覆盖率判定（甚至误报失败）。
    /// ★ 为什么章节也算：CONFIG 的「Mod 设置」行与 submenu 51 面板用游戏自己的 mainbig 字体画
    ///   （字体补全会把 CJK 字形塞进 fnt_mainbig/fnt_main），这些字不在游戏 lang 文件里，漏了就出空洞。</summary>
    internal static void AddNeutraledUiChars(HashSet<char> into, string gameRoot, List<string> src)
    {
        var root = Paths.NeutraledRoot(gameRoot);
        int n = 0;
        foreach (var dir in new[] { Path.Combine(root, "api"), Path.Combine(root, "api", "events") })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.GetFiles(dir, "*.gml"))
            {
                foreach (System.Text.RegularExpressions.Match m in StrLit.Matches(File.ReadAllText(f)))
                    foreach (var c in m.Groups[1].Value) if (!char.IsWhiteSpace(c)) { into.Add(c); n++; }
            }
        }
        if (n > 0) src.Add("api/**.gml 界面文案");
        // ★ mod 自己的界面文案（mods/<mod>/<author>/<chapter>/gml/*.gml 的字符串字面量）：
        //   mod 作者往 Neutraled 的「Mod 设置」面板加项时（api/ntl_menu_add.gml）会写自己的文案，
        //   那些字同样用游戏字体画 ⇒ 不并入字符集的话，中文 mod 界面在真机上会出空洞。
        //   只扫 gml/（作者手写的脚本），**不扫 patches/**（整包 mod 反编译出来的成品代码，
        //   里面的字符串大量是游戏自己的内部标记，算进来只会拉低覆盖率判定）。
        var modsRoot = Path.Combine(root, "mods");
        if (Directory.Exists(modsRoot))
        {
            int mn = 0;
            foreach (var d1 in Directory.GetDirectories(modsRoot))
                foreach (var d2 in Directory.GetDirectories(d1))
                    foreach (var d3 in Directory.GetDirectories(d2))
                    {
                        var gdir = Path.Combine(d3, "gml");
                        if (!Directory.Exists(gdir)) continue;
                        foreach (var f in Directory.GetFiles(gdir, "*.gml"))
                        {
                            foreach (System.Text.RegularExpressions.Match m in StrLit.Matches(File.ReadAllText(f)))
                                foreach (var c in m.Groups[1].Value) if (!char.IsWhiteSpace(c)) { into.Add(c); mn++; }
                        }
                    }
            if (mn > 0) src.Add("mods/**/gml/*.gml mod 文案");
        }
        var packDir = Path.Combine(root, "lang");
        if (Directory.Exists(packDir))
        {
            var packs = Directory.GetFiles(packDir, "lang_*.json");
            foreach (var f in packs) AddJsonStrings(into, f);
            if (packs.Length > 0) src.Add("lang/lang_*.json (" + packs.Length + ")");
        }
        var cj = Path.Combine(root, "chapters.json");
        if (File.Exists(cj)) { AddJsonStrings(into, cj); src.Add("chapters.json 的章节名"); }
    }

    /// <summary>GML 源码里的双引号字符串字面量（允许 \" 转义）。</summary>
    private static readonly System.Text.RegularExpressions.Regex StrLit =
        new System.Text.RegularExpressions.Regex("\"((?:[^\"\\\\]|\\\\.)*)\"", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static void Collect(JsonElement el, StringBuilder sb)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.String:
                var s = el.GetString();
                if (!string.IsNullOrEmpty(s)) sb.Append(s).Append('\n');
                break;
            case JsonValueKind.Object:
                foreach (var p in el.EnumerateObject()) Collect(p.Value, sb);
                break;
            case JsonValueKind.Array:
                foreach (var it in el.EnumerateArray()) Collect(it, sb);
                break;
        }
    }

    // ------------------------------------------------------------------ 工具

    private static List<string> ResolveTargets(string gameRoot, string chapterArg)
    {
        if (!string.IsNullOrWhiteSpace(chapterArg) && !chapterArg.Equals("all", StringComparison.OrdinalIgnoreCase))
            return new List<string> { chapterArg.Trim() };
        var list = new List<string> { "root" };
        for (int i = 1; i <= 7; i++)
        {
            var ch = "chapter" + i;
            if (File.Exists(Paths.ChapterDataWin(gameRoot, ch))) list.Add(ch);
        }
        return list;
    }

    /// <summary>CJK 判定（含扩展 A 与兼容区，够用）。</summary>
    public static bool IsCjk(char c) =>
        (c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3400 && c <= 0x4DBF) ||
        (c >= 0xF900 && c <= 0xFAFF) || (c >= 0x3000 && c <= 0x303F) || (c >= 0xFF00 && c <= 0xFFEF);

    /// <summary>这个字体名是不是「游戏正文/菜单/图例真的会用它绘制」的字体。
    /// 只有这些字体覆盖了文本，缺字才只算警告；我们自己的 ntl_font_* 不算（游戏正文不走它）。</summary>
    private static bool IsGameTextFont(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (name == "fnt_main" || name == "fnt_mainbig" || name == "fnt_small" || name == "fnt_legend") return true;
        return name.StartsWith("fnt_ja_", StringComparison.Ordinal) || name.StartsWith("fnt_mx_", StringComparison.Ordinal);
    }

    private static string Pct(double v) => (v * 100).ToString("0.0") + "%";

    private static string Describe(Exception ex)
    {
        var msg = (ex.Message ?? "").Replace("\r", " ").Replace("\n", " ");
        if (msg.Length > 300) msg = msg.Substring(0, 300) + "…";
        var frame = "";
        try
        {
            var st = ex.StackTrace ?? "";
            var m = System.Text.RegularExpressions.Regex.Match(st, @"ContentCheck\.cs:line (\d+)");
            if (m.Success) frame = "  @ ContentCheck.cs:" + m.Groups[1].Value;
            else
            {
                var line = st.Split('\n').Select(x => x.Trim()).FirstOrDefault(x => x.Contains("Neutraled.Builder")) ?? "";
                if (line.Length > 200) line = line.Substring(0, 200);
                frame = line.Length > 0 ? "  @ " + line : "";
            }
        }
        catch { }
        return ex.GetType().Name + ": " + msg + frame;
    }
}
