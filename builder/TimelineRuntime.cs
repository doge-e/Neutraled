using System.Text.RegularExpressions;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>时间线产物的「运行时文件来源章节」解析 + 语言档守卫。
///
/// 事故背景（2026-09-30 真机截图 ntl_launch_20260930_164037 等）：
///   ntl_timeline_8_…_lab / ntl_timeline_4_…_forest 的产物 data.win 是**第 1 章血统**
///   —— 它的 obj_initializer2 的 Create 事件里写着
///      font_add_sprite_ext(921, scr_84_get_lang_string("obj_initializer2_slash_Create_0_gml_2_0"), 0, 2)
///   而第 3/4/5 章同一处用的是字面量。部署时却按 mod.json 声明的 chapter4 去复制运行时文件
///   （lang/、audiogroup1.dat、options.ini…），于是 global.lang_map（scr_84_lang_load() 从
///   <working_directory>lang/lang_<lang>*.json 载入）里没有那个键 → scr_84_get_lang_string 返回 undefined
///   → font_add_sprite_ext 收到 undefined → 启动第一屏 Code Error（abort 才能退出）。
///
/// 本文件做两件事：
///   1) ResolveRuntimeChapter：**按产物 data.win 实际用到的语言键**（GM 字符串表里形如
///      <名字>_gml_<行>_<列> 的键）在官方章节里挑「覆盖最好」的一章，作为运行时文件的来源，
///      而不是照抄 mod.json 的 chapterN。
///   2) CheckCoverage：部署前守卫 —— 产物 data.win 的语言键若在即将复制的 lang/ 里成片缺失
///      （或引导键缺失），显式报错并让调用方**中止该产物的部署**，绝不静默继续。
///
/// 为什么不「零缺失才放行」：实测 chapter4 自己的产物 data.win 有 14510 个 *_gml_* 键，
/// 而它自带的 lang_en*.json 家族（lang_en / lang_en_names / lang_en_names_recruitable，各 14630 键）
/// 本来就缺 317 个（≈2.2%，上游汉化包与官方文本的既有缺口）—— 零容忍会直接卡死 chapter4 的正常部署。
/// 所以判据是：**引导键一个都不能缺**，且**总缺失比例 ≤ MaxMissingRatio**。
/// 实测口径（2026-09-30 复测：node 按原始字节扫 data.win 取 *_gml_* 键 + JSON.parse 三个 lang_en*.json 取键并集，
/// 与 LangFamilyKeys 同口径）：Lab/Forest（mod.json 声明 chapter4、产物是第 1 章血统）缺 3923/6202 = 63.3%
/// 且引导键缺 1 → 拦；chapter4 自身缺 317/14510 = 2.2% → 放行；chapter1 自身缺 0/6202 → 放行。
/// 注：把 lang_ja.json 也算进并集时 chapter4 自身是 296/14510 = 2.0%；运行时读的是 lang_<global.lang>
/// （汉化补丁把 global.lang 钉成 en），故判据用 lang_en* 家族口径（t31 复核 F3）。
/// </summary>
public static class TimelineRuntime
{
    /// <summary>总缺失比例的容忍上限（超过即中止该产物）。见类注释里的实测依据。</summary>
    public const double MaxMissingRatio = 0.10;

    /// <summary>引导对象（obj_initializer2）Create 事件查表用的语言键前缀。</summary>
    public const string BootKeyPrefix = "obj_initializer2_slash_Create_0_gml_";

    private static readonly Regex GmlKeyRe = new(@"[A-Za-z0-9_]{3,}_gml_\d+_\d+", RegexOptions.Compiled);

    private static readonly Dictionary<string, HashSet<string>> WinKeys = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, HashSet<string>> LangKeys = new(StringComparer.OrdinalIgnoreCase);

    public static string LangDir(string gameRoot, string chapter) =>
        Path.Combine(Paths.ChapterDir(gameRoot, chapter), "lang");

    /// <summary>扫 data.win 的字符串表，取出全部 *_gml_* 语言键。
    /// **不 Load UTMT**：GM 的字符串表就是长度前缀的字节串，按 latin1 解码后正则扫足够（实测 chapter1 = 6202 键、
    /// chapter4 = 14510 键），省掉 Load 137MB 的几十秒与 2 倍内存。分块读 + 256B 重叠，避免大字符串分配。
    /// 读失败（文件不存在 / 被占用）返回空集合。</summary>
    public static HashSet<string> DataWinLangKeys(string winPath)
    {
        var empty = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(winPath) || !File.Exists(winPath)) return empty;
        string full;
        try { full = Path.GetFullPath(winPath); } catch { return empty; }
        if (WinKeys.TryGetValue(full, out var cached)) return cached;

        var set = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            const int Chunk = 8 << 20;
            const int Overlap = 256;                     // 最长键 ~100 字符，留足重叠避免切断
            var buf = new byte[Chunk + Overlap];
            using var fs = File.OpenRead(full);
            int keep = 0;
            while (true)
            {
                int read = fs.Read(buf, keep, Chunk);
                if (read <= 0) break;
                int total = keep + read;
                foreach (Match m in GmlKeyRe.Matches(System.Text.Encoding.Latin1.GetString(buf, 0, total)))
                    set.Add(m.Value);
                keep = Math.Min(Overlap, total);
                Array.Copy(buf, total - keep, buf, 0, keep);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(L("  [警告] 扫描 data.win 语言键失败: {0}（{1}）", full, ex.Message));
        }
        WinKeys[full] = set;
        return set;
    }

    /// <summary>某个章节目录的「英文语言档家族」键集合。
    /// 游戏读的是 &lt;working_directory&gt;lang/lang_&lt;global.lang&gt;[_names…].json；汉化补丁把 global.lang 钉成 en，
    /// 所以家族 = lang_en.json + lang_en_names.json + lang_en_names_recruitable.json 的并集。
    /// 目录/文件缺失返回空集合（空集合 = 一个键都没有 → 会被守卫拦下，不会静默放行）。</summary>
    public static HashSet<string> LangFamilyKeys(string langDir)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(langDir) || !Directory.Exists(langDir)) return set;
        string full;
        try { full = Path.GetFullPath(langDir); } catch { return set; }
        if (LangKeys.TryGetValue(full, out var cached)) return cached;

        foreach (var f in Directory.GetFiles(full, "lang_en*.json"))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(f));
                if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                foreach (var p in doc.RootElement.EnumerateObject()) set.Add(p.Name);
            }
            catch (Exception ex)
            {
                Console.WriteLine(L("  [警告] 解析语言档失败: {0}（{1}）", f, ex.Message));
            }
        }
        LangKeys[full] = set;
        return set;
    }

    /// <summary>可作为运行时文件来源的官方章节（本机存在 + 有英文语言档）。</summary>
    public static List<string> CandidateChapters(string gameRoot)
    {
        var list = new List<string>();
        for (int i = 1; i <= Chapters.OfficialCount; i++)
        {
            var ch = "chapter" + i;
            if (!Directory.Exists(Paths.ChapterDir(gameRoot, ch))) continue;
            if (LangFamilyKeys(LangDir(gameRoot, ch)).Count == 0) continue;
            list.Add(ch);
        }
        return list;
    }

    /// <summary>产物语言键里，有多少不在该 lang 家族中（并给出引导键缺失数）。</summary>
    public static (int Missing, int Total, int BootMissing) CountMissing(IEnumerable<string> winKeys, HashSet<string> langKeys)
    {
        int total = 0, missing = 0, boot = 0;
        foreach (var k in winKeys)
        {
            total++;
            if (langKeys.Contains(k)) continue;
            missing++;
            if (k.StartsWith(BootKeyPrefix, StringComparison.Ordinal)) boot++;
        }
        return (missing, total, boot);
    }

    /// <summary>按 data.win 血统挑运行时文件来源章节：在候选官方章节里选「缺失语言键最少」的那一章。
    /// 平局优先用 declaredChapter（部署结果稳定、不打乱既有部署）；候选为空时原样返回 declaredChapter。</summary>
    public static string ResolveRuntimeChapter(string gameRoot, string baseWinPath, string declaredChapter,
        out int missing, out int total)
    {
        var winKeys = DataWinLangKeys(baseWinPath);
        missing = 0;
        total = winKeys.Count;
        if (winKeys.Count == 0) return declaredChapter;

        var best = declaredChapter;
        var bestMissing = int.MaxValue;
        var declaredMissing = int.MaxValue;
        foreach (var ch in CandidateChapters(gameRoot))
        {
            var m = CountMissing(winKeys, LangFamilyKeys(LangDir(gameRoot, ch))).Missing;
            if (string.Equals(ch, declaredChapter, StringComparison.OrdinalIgnoreCase)) declaredMissing = m;
            if (m < bestMissing) { bestMissing = m; best = ch; }
        }
        if (bestMissing == int.MaxValue) return declaredChapter;                 // 没有候选：维持声明
        if (declaredMissing == bestMissing) { missing = declaredMissing; return declaredChapter; }  // 平局优先声明
        missing = bestMissing;
        return best;
    }

    /// <summary>部署前守卫的结果。Ok=false 时调用方必须中止该产物部署。</summary>
    public readonly record struct Coverage(bool Ok, int Missing, int Total, int BootMissing, string Sample, string LangDir);

    /// <summary>产物 data.win 的语言键 vs 即将复制过去的 lang/ 的覆盖检查。
    /// Ok = 引导键一个不缺 **且** 总缺失比例 ≤ MaxMissingRatio。</summary>
    public static Coverage CheckCoverage(string gameRoot, string baseWinPath, string runtimeChapter)
    {
        var dir = LangDir(gameRoot, runtimeChapter);
        var winKeys = DataWinLangKeys(baseWinPath);
        var langKeys = LangFamilyKeys(dir);
        var (missing, total, boot) = CountMissing(winKeys, langKeys);
        var sample = string.Join(", ", winKeys.Where(k => !langKeys.Contains(k)).Take(5));
        // ★ fail-closed：data.win 读不出来（被占用/格式不支持）时扫不出任何 *_gml_* 键 ⇒ total==0。
        //   「没有可判定的键」绝不能当成「通过」—— 那会让守卫在最该报警的时候静默放行（t31 复核 F2）。
        var ok = total > 0 && boot == 0 && Ratio(missing, total) <= MaxMissingRatio;
        return new Coverage(ok, missing, total, boot, sample, dir);
    }

    /// <summary>部署期守卫失败时的报错原文。**DeployTimelines 与只读预检共用**，保证「报错内容」与「实际部署行为」一致
    /// （验收要求：报错含产物名、缺失键样例、目标 lang 目录）。</summary>
    public static void PrintGuardFailure(string id, string name, string baseWinPath, string runtimeChapter, Coverage cov)
    {
        Console.WriteLine(L("  [错误] {0}「{1}」: 语言档与产物 data.win 不匹配 —— 中止该产物部署", id, name));
        Console.WriteLine(L("         产物 data.win : {0}", baseWinPath));
        Console.WriteLine(L("         语言档目录     : {0}（运行时章节 {1}）", cov.LangDir, runtimeChapter));
        Console.WriteLine(L("         语言键缺失     : {0}/{1}（引导键缺 {2} 个）", cov.Missing, cov.Total, cov.BootMissing));
        if (cov.Total == 0)
            Console.WriteLine(L("         ★产物 data.win 里扫不出任何语言键（读取失败或格式不支持）—— 无法证明语言档匹配，按「拦下」处理"));
        if (cov.BootMissing > 0)
            Console.WriteLine(L("         ★引导键缺失：游戏启动第一屏必崩 Code Error（obj_initializer2 Create）"));
        if (!string.IsNullOrEmpty(cov.Sample))
            Console.WriteLine(L("         缺失键样例     : {0}", cov.Sample));
    }

    private static double Ratio(int missing, int total) => total == 0 ? 0d : (double)missing / total;


    /// <summary>只读预检（--probe-timeline-runtime）：**不写盘、不部署、不启动游戏**。
    /// 对每条时间线产物打印：产物 data.win 路径与键数、旧规则（mod.json 声明章节）的缺失数、
    /// 新规则（血统章节）的缺失数、各候选章节缺失数、守卫判定。
    /// 同时展示「修复前 → 修复后」的对比，因此一条命令即可复现验收 ① 与 ③。
    /// 返回码：0 = 全部产物都能取到匹配的语言档；1 = 有产物会被守卫拦下。</summary>
    public static int RunProbe(string gameRoot, string currentChapter)
    {
        Console.WriteLine(L("===== 时间线运行时血统预检（只读：不写盘、不部署、不启动游戏）====="));
        Console.WriteLine(L("游戏根  : {0}", gameRoot));
        Console.WriteLine(L("当前章节: {0}", currentChapter));

        var prevNoCacheWrite = Mods.NoCacheWrite;
        Mods.NoCacheWrite = true;      // ★ 只读：扫描缓存不落盘（否则 ScanMods 会写 <mods>/../cache/modscan-*.json）
        int guarded = 0, changed = 0, noBase = 0;
        try
        {
            var modsRoot = Paths.ModsRoot(gameRoot);
            var allMods = Mods.ScanMods(modsRoot, currentChapter, includeDisabled: true, allChapters: true);
            var registry = Chapters.BuildRegistry(allMods, currentChapter, gameRoot);   // 纯内存：不调 WriteRegistry
            var timelines = registry
                .Where(e => e.Kind == "timeline" || !string.IsNullOrEmpty(e.BaseDir))
                .OrderBy(e => e.Order).ThenBy(e => e.Id, StringComparer.Ordinal)
                .ToList();

            var candidates = CandidateChapters(gameRoot);
            Console.WriteLine(L("扫到 mod {0} 个；时间线 / 引用章 {1} 条", allMods.Count, timelines.Count));
            Console.WriteLine(L("候选官方章节（本机存在且有 lang/lang_en*.json）: {0}",
                candidates.Count == 0 ? L("(无)") : string.Join(" ", candidates)));
            Console.WriteLine();

            if (timelines.Count == 0) { Console.WriteLine(L("没有时间线产物，无需修复。")); return 0; }

            int idx = 0;
            foreach (var e in timelines)
            {
                idx++;
                var modsFor = allMods
                    .Where(m => e.Mods.Any(id => string.Equals(id, m.Id, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                var owner = modsFor.FirstOrDefault(m => !string.IsNullOrEmpty(m.Chapter) &&
                                                         !m.Chapter!.Equals("root", StringComparison.OrdinalIgnoreCase));
                var baseOrder = e.Order <= Chapters.OfficialCount ? e.Order : Chapters.OfficialCount;
                var declared = owner?.Chapter ?? ("chapter" + baseOrder);

                string? baseWin = null;
                if (!string.IsNullOrEmpty(e.BaseDir))
                    baseWin = Path.Combine(gameRoot, e.BaseDir.Replace('/', Path.DirectorySeparatorChar), "data.win");
                else if (!string.IsNullOrEmpty(e.OwnData))
                    baseWin = e.OwnData;

                Console.WriteLine(L("[{0}/{1}] {2}  「{3}」", idx, timelines.Count, e.Id, e.Name));
                Console.WriteLine(L("      kind={0}  order={1}  enabled={2}  dir={3}", e.Kind, e.Order, e.Enabled, e.Dir));
                if (baseWin == null || !File.Exists(baseWin))
                {
                    Console.WriteLine(L("      产物 data.win : 缺失（{0}）", baseWin ?? L("未声明 OwnData / BaseDir")));
                    noBase++;
                    Console.WriteLine();
                    continue;
                }

                var winKeys = DataWinLangKeys(baseWin);
                var bootTotal = winKeys.Count(k => k.StartsWith(BootKeyPrefix, StringComparison.Ordinal));
                Console.WriteLine(L("      产物 data.win : {0}（{1} B）", baseWin, new FileInfo(baseWin).Length));
                Console.WriteLine(L("      *_gml_* 语言键: {0} 个（其中引导键 {1} 个）", winKeys.Count, bootTotal));

                var oldCov = CheckCoverage(gameRoot, baseWin, declared);
                Console.WriteLine(L("      旧规则（mod.json 声明 → {0}）: 缺 {1}/{2}（{3:P1}），引导键缺 {4} → {5}",
                    declared, oldCov.Missing, oldCov.Total, Ratio(oldCov.Missing, oldCov.Total), oldCov.BootMissing,
                    oldCov.Ok ? L("可用") : L("部署出去会启动即崩")));
                if (!oldCov.Ok)
                {
                    Console.WriteLine(L("      ↓ 修复前的旧行为：照 mod.json 声明取 {0} → 部署期守卫会**中止该产物部署**，报错原文如下：", declared));
                    PrintGuardFailure(e.Id, e.Name, baseWin, declared, oldCov);
                }

                var runtimeChapter = ResolveRuntimeChapter(gameRoot, baseWin, declared, out _, out _);
                var newCov = CheckCoverage(gameRoot, baseWin, runtimeChapter);
                var same = runtimeChapter.Equals(declared, StringComparison.OrdinalIgnoreCase);
                if (!same) changed++;
                Console.WriteLine(L("      新规则（data.win 血统）: 运行时文件取自 {0}{1} → 缺 {2}/{3}（{4:P1}），引导键缺 {5}",
                    runtimeChapter, same ? L("（与声明一致）") : L("（修复前按声明取 {0}）", declared),
                    newCov.Missing, newCov.Total, Ratio(newCov.Missing, newCov.Total), newCov.BootMissing));

                var parts = new List<string>();
                foreach (var ch in candidates)
                {
                    var m = CountMissing(winKeys, LangFamilyKeys(LangDir(gameRoot, ch))).Missing;
                    parts.Add(L("{0}={1}", ch, m));
                }
                Console.WriteLine(L("      候选章节缺失数: {0}", string.Join("  ", parts)));
                Console.WriteLine(L("      lang 目录       : {0}", newCov.LangDir));
                if (newCov.Ok)
                    Console.WriteLine(L("      守卫判定        : 通过（部署时从 {0} 复制运行时文件）", runtimeChapter));
                else
                {
                    guarded++;
                    Console.WriteLine(L("      守卫判定        : ★拦下 —— 语言档与产物 data.win 不匹配，中止该产物部署"));
                    Console.WriteLine(L("      缺失键样例      : {0}", string.IsNullOrEmpty(newCov.Sample) ? L("(无)") : newCov.Sample));
                    if (newCov.BootMissing > 0)
                        Console.WriteLine(L("      ★引导键缺失 {0} 个：游戏启动第一屏必崩 Code Error（obj_initializer2 Create）", newCov.BootMissing));
                }
                Console.WriteLine();
            }

            Console.WriteLine(L("===== 结论: {0} 条时间线；血统判定与声明不同的 {1} 条；守卫拦下 {2} 条；产物缺失 {3} 条 =====",
                timelines.Count, changed, guarded, noBase));
            if (guarded > 0)
                Console.WriteLine(L("  ↑ 被拦下的产物在修复前会被部署出去 → 启动第一屏 Code Error（旧行为）；现在直接中止，不打坏产物。"));
            else
                Console.WriteLine(L("  全部产物的运行时文件都能按 data.win 血统取到匹配的语言档。"));
            return guarded > 0 ? 1 : 0;
        }
        finally { Mods.NoCacheWrite = prevNoCacheWrite; }
    }
}
