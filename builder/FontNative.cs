using System.Text;
using System.Text.Json;
using UndertaleModLib;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>部署期「本机字形来源」声明（Neutraled/fonts/ntl_native_sources.json）。
/// 只声明**码位区间与来源种类**，不含任何位图。</summary>
public sealed class NativeSourceConfig
{
    public int Version { get; set; } = 1;
    public string? Note { get; set; }
    public string? License { get; set; }
    /// <summary>可作为基线字形来源的 mod id（按顺序找 &lt;mods&gt;/&lt;id&gt;[/作者]/&lt;章节|root&gt;/ref/data.win）。</summary>
    public List<string> BaseModIds { get; set; } = new();
    public List<NativeSourceRegion> Regions { get; set; } = new();
}

/// <summary>一个码位区间组（拉丁 / 汉字……）与其允许的本机来源。</summary>
public sealed class NativeSourceRegion
{
    public string Name { get; set; } = "";
    public string? Desc { get; set; }
    /// <summary>[起, 止] 码位区间（含端点）。</summary>
    public List<int[]> Chars { get; set; } = new();
    /// <summary>允许的来源种类，按优先级：game=正在打补丁的 data.win；base-mod=基线 mod 的 ref/data.win。</summary>
    public List<string> Sources { get; set; } = new();
    public string Font { get; set; } = "fnt_main";
    /// <summary>该区最少要有多少个本机字形才算「这个来源可用」（防止没有汉化时把 96 字形当成可用汉字源）。</summary>
    public int MinGlyphs { get; set; }

    public bool InRange(int cp) => Chars.Any(r => r.Length >= 2 && cp >= Math.Min(r[0], r[1]) && cp <= Math.Max(r[0], r[1]));
}

/// <summary>一个已建好的本机字形源（从 data.win 搬出来的整页 + 字形表）。</summary>
internal sealed class NativeTable
{
    public string Kind = "";
    public string Sheet = "";
    public string From = "";
    public int Count;
    public double EmSize;
    public Dictionary<int, GlyphDef> Map = new();
}

/// <summary>★ 部署期「本机字形覆盖」（native override）。
///
/// 背景：Neutraled 自绘 UI（章节选择器 / F2 控制台 / toast）用的是自己的字体包
/// <c>Neutraled/fonts/ntl_font_cjk.json</c>。这个包随发布包分发，必须 **OFL-clean**，
/// 于是只能装 OFL 的矢量字体（Noto Sans SC）——但它的观感与游戏（8bitoperator JVE
/// 像素字 + 汉化像素汉字）**完全不同**，用户真机反馈「ntl 的大部分内容都不对，
/// 疑似又把 8bitoperator JVE 换成微软雅黑了」。
///
/// 正解：**部署期**在本机（用户的机器上、不入库、不随包分发）从用户自己的 data.win
/// 里搬字形覆盖进字体包的副本：
///   · 拉丁/ASCII → fnt_main 的 8bitoperator JVE（游戏自带位图）
///   · 汉字/CJK   → 汉化 mod 的像素汉字（已打补丁的 data.win，或 mods/&lt;id&gt;/ref/data.win）
/// 覆盖结果写到 <c>Neutraled/.tmp/font-native/&lt;章节&gt;/</c>，由注入流程 2.41 导入；
/// 本机没有的字符继续用包里的 OFL 字形（回退清单见 _report/ofl-fallback-list.txt）。
///
/// 失败一律**不抛异常**（返回 null → 回退纯 OFL 包），部署绝不因为字体而中断。</summary>
public static class FontNative
{
    public const string ConfigName = "ntl_native_sources.json";
    private const string SrcDirName = "font-native-src";
    private const string OutDirName = "font-native";

    /// <summary>部署流程调用：返回「OFL 包 + 本机字形」的合并目录；没有可用本机字形时返回 null。</summary>
    public static string? Prepare(UndertaleData data, string gameRoot, string fontsDir, string chapter)
    {
        try
        {
            return Build(data, gameRoot, fontsDir, chapter, null, null, false);
        }
        catch (Exception ex)
        {
            Paths.Log(L("    [警告] 本机字形覆盖失败，改用纯 OFL 字形包: {0}", ex.Message));
            return null;
        }
    }

    /// <summary>--font-native：同一条管线的命令行入口（可重复生成 / 取证用）。</summary>
    public static int RunCli(string gameRoot, string chapter, string? winPath, string? outDir, string? sourceKind)
    {
        var target = string.IsNullOrEmpty(winPath) ? Paths.ChapterDataWin(gameRoot, chapter) : winPath;
        if (!File.Exists(target))
        {
            Console.WriteLine(L("  [错误] 找不到 {0}", target));
            return 2;
        }
        var fontsDir = Path.Combine(Paths.NeutraledRoot(gameRoot), "fonts");
        Console.WriteLine(L("本机字形覆盖: 目标 {0}", target));
        Console.WriteLine(L("  OFL 字体包: {0}", fontsDir));
        var data = Injector.Load(target);
        var res = Build(data, gameRoot, fontsDir, chapter, outDir, sourceKind, true);
        if (res == null)
        {
            Console.WriteLine(L("  没有可用的本机字形源（保持纯 OFL 字形包）"));
            return 3;
        }
        Console.WriteLine(L("  合并字体包: {0}", res));
        return 0;
    }

    private static string? Build(UndertaleData data, string gameRoot, string fontsDir, string chapter,
                                 string? outDirOverride, string? sourceKind, bool verbose)
    {
        var cfgPath = Path.Combine(fontsDir, ConfigName);
        if (!File.Exists(cfgPath)) return null;
        var cfg = JsonSerializer.Deserialize<NativeSourceConfig>(File.ReadAllText(cfgPath), Paths.Json);
        if (cfg == null || cfg.Regions.Count == 0) return null;

        var packJson = Path.Combine(fontsDir, "ntl_font_cjk.json");
        if (!File.Exists(packJson)) return null;
        var packDef = JsonSerializer.Deserialize<FontDef>(File.ReadAllText(packJson), Paths.Json);
        if (packDef == null || packDef.Glyphs.Count == 0) return null;

        var scope = string.IsNullOrEmpty(chapter) ? "root" : chapter;
        var tmpRoot = Path.Combine(Paths.NeutraledRoot(gameRoot), ".tmp");
        var srcDir = Path.Combine(tmpRoot, SrcDirName, scope);
        var outDir = string.IsNullOrEmpty(outDirOverride) ? Path.Combine(tmpRoot, OutDirName, scope) : outDirOverride!;
        ResetDir(srcDir);
        ResetDir(outDir);

        var fontName = cfg.Regions.Select(r => r.Font).FirstOrDefault(f => !string.IsNullOrEmpty(f)) ?? "fnt_main";
        var tables = new Dictionary<string, NativeTable>(StringComparer.OrdinalIgnoreCase);

        NativeTable? BuildTable(string kind)
        {
            if (tables.TryGetValue(kind, out var cached)) return cached;
            var dir = Path.Combine(srcDir, kind);
            var name = "ntl_native_" + kind.Replace('-', '_');
            string from;
            if (kind.Equals("game", StringComparison.OrdinalIgnoreCase))
            {
                // ★ 正在打补丁的 data（内存里就有）：零额外文件解析，且**就是这个用户机器上的字形**
                from = "(当前 data.win)";
                CjkFont.FromFont(dir, data, fontName, name);
            }
            else if (kind.Equals("base-mod", StringComparison.OrdinalIgnoreCase))
            {
                var win = FindBaseModRef(gameRoot, scope, cfg.BaseModIds);
                if (win == null)
                {
                    if (verbose) Paths.Log(L("  [跳过] base-mod: 没有找到基线 mod 的 ref/data.win"));
                    return null;
                }
                from = win;
                CjkFont.FromDataWin(dir, win, fontName, name);
            }
            else
            {
                if (verbose) Paths.Log(L("  [跳过] 未知来源种类 {0}", kind));
                return null;
            }
            var def = JsonSerializer.Deserialize<FontDef>(File.ReadAllText(Path.Combine(dir, name + ".json")), Paths.Json);
            if (def == null || def.Glyphs.Count == 0) return null;
            var tbl = new NativeTable { Kind = kind, Sheet = string.IsNullOrEmpty(def.Page) ? name + "_page.png" : def.Page, From = from, Count = def.Glyphs.Count, EmSize = def.EmSize };
            foreach (var gl in def.Glyphs) if (!tbl.Map.ContainsKey(gl.Char)) tbl.Map[gl.Char] = gl;
            tables[kind] = tbl;
            if (verbose) Paths.Log(L("  本机字形源 [{0}]: {1} 字形 EmSize={2} 页 {3}  ← {4}", kind, def.Glyphs.Count, def.EmSize, tbl.Sheet, from));
            return tbl;
        }

        // ---------- 1) 每个区挑一个可用来源 ----------
        var regionTable = new Dictionary<string, NativeTable?>(StringComparer.Ordinal);
        var picked = new List<string>();
        foreach (var r in cfg.Regions)
        {
            NativeTable? hit = null;
            foreach (var kind in r.Sources)
            {
                if (!string.IsNullOrEmpty(sourceKind) && !kind.Equals(sourceKind, StringComparison.OrdinalIgnoreCase)) continue;
                NativeTable? t = null;
                try { t = BuildTable(kind); }
                catch (Exception ex) { if (verbose) Paths.Log(L("  [跳过] {0}: {1}", kind, ex.Message)); }
                if (t == null) continue;
                int inRegion = t.Map.Keys.Count(r.InRange);
                int need = Math.Max(1, r.MinGlyphs);
                if (inRegion < need)
                {
                    if (verbose) Paths.Log(L("  [跳过] {0} 区 + {1}: 本机只有 {2} 个字形（需要 ≥{3}）", r.Name, kind, inRegion, need));
                    continue;
                }
                hit = t;
                picked.Add(r.Name + "←" + kind);
                break;
            }
            regionTable[r.Name] = hit;
        }
        if (picked.Count == 0) return null;

        // ---------- 2) 合并（替换同码位字形 + 补本机独有的字形） ----------
        var replaced = new Dictionary<string, int>(StringComparer.Ordinal);
        var added = new Dictionary<string, int>(StringComparer.Ordinal);
        var kept = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in cfg.Regions) { replaced[r.Name] = 0; added[r.Name] = 0; kept[r.Name] = 0; }

        var have = new HashSet<int>(packDef.Glyphs.Select(g => g.Char));
        foreach (var gl in packDef.Glyphs)
        {
            var r = cfg.Regions.FirstOrDefault(x => x.InRange(gl.Char));
            if (r == null) continue;
            var t = regionTable[r.Name];
            if (t == null) continue;
            if (!t.Map.TryGetValue(gl.Char, out var ng)) { kept[r.Name]++; continue; }
            gl.SX = ng.SX; gl.SY = ng.SY; gl.SW = ng.SW; gl.SH = ng.SH;
            gl.Shift = ng.Shift ?? Math.Max(1, ng.SW);
            gl.Offset = ng.Offset;
            gl.Sheet = t.Sheet;
            gl.File = "";
            replaced[r.Name]++;
        }
        foreach (var r in cfg.Regions)
        {
            var t = regionTable[r.Name];
            if (t == null) continue;
            foreach (var kv in t.Map)
            {
                if (!r.InRange(kv.Key)) continue;   // ★ 只补「声明区内的」码位：区间外的本机字形（如 U+200B）不进包
                if (!have.Add(kv.Key)) continue;
                var ng = kv.Value;
                packDef.Glyphs.Add(new GlyphDef
                {
                    Char = kv.Key, File = "", Sheet = t.Sheet,
                    SX = ng.SX, SY = ng.SY, SW = ng.SW, SH = ng.SH,
                    Shift = ng.Shift ?? Math.Max(1, ng.SW), Offset = ng.Offset
                });
                added[r.Name]++;
            }
        }
        // 排序必须有：GMS2 运行期对字形数组做二分查找（FontImport.UpsertFont 同样按 Char 升序）
        packDef.Glyphs.Sort((a, b) => a.Char.CompareTo(b.Char));
        packDef.Page = "";   // 本机页 + OFL 页混用 → 走 sheet 模式、按字形逐格裁剪

        // ---------- 3) 落盘（只写 ntl_font_cjk.json + 各页 PNG + 报告子目录） ----------
        File.WriteAllText(Path.Combine(outDir, "ntl_font_cjk.json"),
            JsonSerializer.Serialize(packDef, Paths.Json), new UTF8Encoding(false));
        foreach (var p in Directory.GetFiles(fontsDir, "*.png"))
            File.Copy(p, Path.Combine(outDir, Path.GetFileName(p)), true);
        foreach (var p in Directory.GetFiles(srcDir, "*.png", SearchOption.AllDirectories))
        {
            var dst = Path.Combine(outDir, Path.GetFileName(p));
            if (!File.Exists(dst)) File.Copy(p, dst, true);
        }

        var reportDir = Path.Combine(outDir, "_report");
        Directory.CreateDirectory(reportDir);
        var nativeSheets = regionTable.Values.Where(t => t != null).Select(t => t!.Sheet).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fallback = new List<(string region, int cp)>();
        foreach (var gl in packDef.Glyphs)
        {
            var r = cfg.Regions.FirstOrDefault(x => x.InRange(gl.Char));
            if (r == null) continue;
            if (!nativeSheets.Contains(gl.Sheet)) fallback.Add((r.Name, gl.Char));
        }
        var sb = new StringBuilder();
        sb.AppendLine("# 部署期字形来源（本机 data.win）");
        sb.AppendLine("# 生成时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("# 章节: " + scope + "    字体包: " + packDef.Name + "    EmSize=" + packDef.EmSize + " LineHeight=" + packDef.LineHeight + " Ascender=" + packDef.Ascender);
        sb.AppendLine("# 本机覆盖: " + string.Join("  ", picked));
        sb.AppendLine("# 回退字形（本机没有 → 用包里的 OFL Noto 字形）: " + fallback.Count + " 个");
        foreach (var g in fallback.GroupBy(x => x.region))
            sb.AppendLine("#   " + g.Key + ": " + g.Count() + " 个");
        sb.AppendLine();
        foreach (var g in fallback)
            sb.AppendLine(string.Format("U+{0:X4}	{1}", g.cp, g.cp >= 32 && g.cp < 0x110000 ? char.ConvertFromUtf32(g.cp) : ""));
        File.WriteAllText(Path.Combine(reportDir, "ofl-fallback-list.txt"), sb.ToString(), new UTF8Encoding(false));

        var report = new
        {
            Generated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            Chapter = scope,
            Note = cfg.Note,
            License = cfg.License,
            Config = ConfigName,
            Pack = new { packDef.Name, packDef.EmSize, packDef.LineHeight, packDef.Ascender, packDef.ScaleX, packDef.ScaleY, Glyphs = packDef.Glyphs.Count },
            Sources = tables.Values.Select(t => new { kind = t.Kind, glyphs = t.Count, emSize = t.EmSize, sheet = t.Sheet, from = t.From }).ToList(),
            Regions = cfg.Regions.ToDictionary(r => r.Name, r => new
            {
                source = regionTable[r.Name]?.Kind,
                replaced = replaced[r.Name],
                added = added[r.Name],
                keptOfl = kept[r.Name],
                minGlyphs = Math.Max(1, r.MinGlyphs)
            }),
            Totals = new { replaced = replaced.Values.Sum(), added = added.Values.Sum(), oflFallbackInRegions = fallback.Count }
        };
        File.WriteAllText(Path.Combine(reportDir, "merge-report.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions(Paths.Json) { WriteIndented = true }), new UTF8Encoding(false));

        Paths.Log(L("  本机字形覆盖: {0}（替换 {1}，补充 {2}，回退 OFL {3}）",
            string.Join("  ", picked), replaced.Values.Sum(), added.Values.Sum(), fallback.Count));
        return outDir;
    }

    /// <summary>&lt;mods&gt;/&lt;id&gt;[/作者]/&lt;章节|root&gt;/ref/data.win —— 按 id 顺序、章节优先于 root。</summary>
    private static string? FindBaseModRef(string gameRoot, string scope, List<string> ids)
    {
        var modsRoot = Paths.ModsRoot(gameRoot);
        foreach (var id in ids)
        {
            var modDir = Path.Combine(modsRoot, id);
            if (!Directory.Exists(modDir)) continue;
            var roots = new List<string> { modDir };
            var subs = Directory.GetDirectories(modDir);
            Array.Sort(subs, StringComparer.OrdinalIgnoreCase);   // 固定顺序：目录枚举顺序因文件系统而异，参考包必须可复现
            roots.AddRange(subs);
            foreach (var root in roots)
            {
                foreach (var sc in new[] { scope, "root" })
                {
                    if (string.IsNullOrEmpty(sc)) continue;
                    var p = Path.Combine(root, sc, "ref", "data.win");
                    if (File.Exists(p)) return p;
                }
            }
        }
        return null;
    }

    private static void ResetDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { /* 有 worker 并发时忽略 */ }
        Directory.CreateDirectory(dir);
    }
}
