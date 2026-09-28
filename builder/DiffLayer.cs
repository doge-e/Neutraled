using UndertaleModLib;
using UndertaleModLib.Models;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>
/// 差异层提取器 —— 把"整包 data.win 型"传统 mod 转换成**可叠加的 ntl mod**。
///
/// 背景：整包 data.win 的 mod 之间天然互斥（每章只能有一个基底）。
/// 但只要把"该 mod 相对官方基线改动的那些代码对象"抽出来，写成
/// references.codes 层，它就能叠加在**任意**基底上（官方 / 汉化 / 另一个 mod），
/// 于是多个功能上不冲突的传统 mod 就能同时运行。
///
///   ntl-builder.exe --extract-diff <mod 的 ref/data.win> --chapter chapter5 --name BossRush --author PrimeStrat
///
/// 产出：mods/NAME/AUTHOR/chapterN/{mod.json(references.codes), ref/data.win}
/// </summary>
public static class DiffLayer
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static int Extract(string gameRoot, string input, string? chapter, string? name, string? author,
        string? baseOverride = null, bool dryRun = false, bool includeOverride = false, bool verbose = false, bool force = false)
    {
        Console.WriteLine(L("===== 差异层提取（整包 mod → 可叠加层） ====="));

        // 1) 定位修改版 data.win
        string modified;
        if (Directory.Exists(input))
        {
            var cand = Directory.GetFiles(input, "data.win", SearchOption.AllDirectories)
                                .OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();
            if (cand == null) { Console.WriteLine(L("[错误] 目录内没有 data.win: ") + input); return 1; }
            modified = cand;
        }
        else if (File.Exists(input)) modified = input;
        else { Console.WriteLine(L("[错误] 找不到: ") + input); return 1; }
        Console.WriteLine(L("  修改版: ") + modified);

        // 2) 章节
        chapter ??= InferChapterFromPath(modified);
        if (chapter == null)
        {
            Console.WriteLine(L("[错误] 无法推断章节，请用 --chapter chapterN 指定"));
            return 1;
        }
        chapter = chapter.ToLowerInvariant();

        // 3) 基线
        var baselinePath = baseOverride ?? Paths.BackupDataWin(gameRoot, chapter);
        if (!File.Exists(baselinePath))
        {
            Console.WriteLine(L("[错误] 缺少官方基线: ") + baselinePath);
            return 1;
        }
        Console.WriteLine(L("  基线: ") + baselinePath);
        Console.WriteLine(L("  章节: ") + chapter);

        var baseData = Injector.Load(baselinePath);
        var modData = Injector.Load(modified);

        // 4) 字节码差异（完整字段比对 —— 比 DiffCodeObjects 更严，漏一个改动层就是坏的）
        var changed = new List<string>();
        var added = new List<string>();
        var diffSamples = new List<string>();
        var baseByName = new Dictionary<string, UndertaleCode>(StringComparer.Ordinal);
        foreach (var c in baseData.Code)
        {
            var n = c.Name?.Content;
            if (!string.IsNullOrEmpty(n)) baseByName[n] = c;
        }
        var modNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in modData.Code)
        {
            var n = c.Name?.Content;
            if (string.IsNullOrEmpty(n)) continue;
            modNames.Add(n);
            if (!baseByName.TryGetValue(n, out var b)) { added.Add(n); continue; }
            if (!PreWriteRepairs.SameCodeContent(b, c))
            {
                changed.Add(n);
                if (diffSamples.Count < 12)
                    diffSamples.Add($"    {n}: {PreWriteRepairs.FirstDifference(b, c)}");
            }
        }
        var removed = baseByName.Keys.Where(n => !modNames.Contains(n)).ToList();

        // 5) 资源层差异（差异层只覆盖字节码，资源差异必须显式告知）
        var resLines = new List<string>();
        bool resDiff = false;
        void Cmp(string label, int bc, int mc, IEnumerable<string?> bn, IEnumerable<string?> mn)
        {
            if (bc == mc) { resLines.Add($"    {label,-10} {bc} = {mc}"); }
            else
            {
                var bs = new HashSet<string>(bn.Where(x => !string.IsNullOrEmpty(x))!);
                var ms = new HashSet<string>(mn.Where(x => !string.IsNullOrEmpty(x))!);
                var add = ms.Except(bs).Take(6).ToList();
                var del = bs.Except(ms).Take(6).ToList();
                resLines.Add($"    {label,-10} {bc} → {mc}  (+{ms.Except(bs).Count()} / -{bs.Except(ms).Count()})"
                    + (add.Count > 0 ? L("  新增: ") + string.Join(", ", add) : "")
                    + (del.Count > 0 ? L("  移除: ") + string.Join(", ", del) : ""));
                resDiff = true;
            }
        }
        Cmp(L("精灵"), baseData.Sprites.Count, modData.Sprites.Count, baseData.Sprites.Select(s => s.Name?.Content), modData.Sprites.Select(s => s.Name?.Content));
        Cmp(L("声音"), baseData.Sounds.Count, modData.Sounds.Count, baseData.Sounds.Select(s => s.Name?.Content), modData.Sounds.Select(s => s.Name?.Content));
        Cmp(L("房间"), baseData.Rooms.Count, modData.Rooms.Count, baseData.Rooms.Select(s => s.Name?.Content), modData.Rooms.Select(s => s.Name?.Content));
        Cmp(L("字体"), baseData.Fonts.Count, modData.Fonts.Count, baseData.Fonts.Select(s => s.Name?.Content), modData.Fonts.Select(s => s.Name?.Content));

        Console.WriteLine(L("  代码对象: 基线 {0} / 修改版 {1}", baseData.Code.Count, modData.Code.Count));
        Console.WriteLine(L("    改动 {0}  新增 {1}  删除 {2}", changed.Count, added.Count, removed.Count));
        Console.WriteLine(L("  池: 字符串 {0}→{1}  函数 {2}→{3}  变量 {4}→{5}  对象 {6}→{7}", baseData.Strings.Count, modData.Strings.Count, baseData.Functions.Count, modData.Functions.Count, baseData.Variables.Count, modData.Variables.Count, baseData.GameObjects.Count, modData.GameObjects.Count));

        // ---- 安全闸：资源池被重建过 → 字节码里的"索引型操作数"不可移植，层一定是坏的 ----
        // 实测：Deltamod / GM3P 打包的 mod 会整体重序列化 data.win，字符串/函数/变量池大小都会变，
        // 于是 push.bltn / call / B 这类指令里的资源索引全体位移。把这种代码对象复制进别的基底，
        // 索引会指向错误的资源 —— 功能静默错乱，比不合并更糟。所以这里**拒绝生成**。
        bool poolsDiffer = baseData.Strings.Count != modData.Strings.Count
                        || baseData.Functions.Count != modData.Functions.Count
                        || baseData.Variables.Count != modData.Variables.Count
                        || baseData.GameObjects.Count != modData.GameObjects.Count;
        if (poolsDiffer && !force)
        {
            Console.WriteLine();
            Console.WriteLine(L("  [拒绝生成] 该包在打包时**重建了资源池**，字节码里的资源索引不可移植："));
            Console.WriteLine(L("    字符串 {0}→{1} / 函数 {2}→{3} / 变量 {4}→{5} / 对象 {6}→{7}", baseData.Strings.Count, modData.Strings.Count, baseData.Functions.Count, modData.Functions.Count, baseData.Variables.Count, modData.Variables.Count, baseData.GameObjects.Count, modData.GameObjects.Count));
            Console.WriteLine(L("    受影响指令示例（索引位移）:"));
            foreach (var s in diffSamples.Take(4)) Console.WriteLine("    " + s);
            Console.WriteLine(L("    把这种对象抽成「差异层」，会把**错误的资源索引**写进基底 → 功能静默错乱。"));
            Console.WriteLine(L("  正确做法（按推荐顺序）:"));
            Console.WriteLine(L("    1) 把它作为该章的**基底**（Neutraled 里它就是合理的整包 mod）"));
            Console.WriteLine(L("    2) 一整包只允许一个 data.win 基底；其余 mod 用 scripts/ hooks/ 资源包/ files 覆盖来叠加"));
            Console.WriteLine(L("    3) 确实要用它自己的资源池 → 用 --mod-force 强制生成（自担风险）"));
            return 2;
        }
        if (verbose)
        {
            Console.WriteLine(L("  --- 前 12 个改动对象的语义差异 ---"));
            if (diffSamples.Count == 0) Console.WriteLine(L("    (无)"));
            foreach (var s in diffSamples) Console.WriteLine(s);
            Console.WriteLine(L("  --- 差异指令明细（Kind / ReferenceType / 值）---"));
            int shown = 0;
            foreach (var cn in changed)
            {
                if (shown >= 6) break;
                var b0 = baseByName[cn]; var m0 = modData.Code.FirstOrDefault(c => c.Name?.Content == cn);
                if (m0 == null) continue;
                for (int i = 0; i < b0.Instructions.Count && i < m0.Instructions.Count; i++)
                {
                    var x = b0.Instructions[i]; var y = m0.Instructions[i];
                    if (x.Kind == y.Kind && x.TypeInst == y.TypeInst && x.ValueInt == y.ValueInt && x.ValueShort == y.ValueShort) continue;
                    Console.WriteLine(L("    {0}[{1}] 基线: Kind={2} RefType={3} Type1={4} Type2={5} TypeInst={6} I={7} S={8} | 修改: Kind={9} RefType={10} Type1={11} Type2={12} TypeInst={13} I={14} S={15}", cn, i, x.Kind, x.ReferenceType, x.Type1, x.Type2, x.TypeInst, x.ValueInt, x.ValueShort, y.Kind, y.ReferenceType, y.Type1, y.Type2, y.TypeInst, y.ValueInt, y.ValueShort));
                    shown++;
                    break;
                }
            }
        }
        Console.WriteLine(L("  资源:"));
        foreach (var l in resLines) Console.WriteLine(l);
        if (removed.Count > 0)
            Console.WriteLine(L("  [警告] {0} 个对象在修改版中不存在，差异层无法表达删除（前 5: {1}）", removed.Count, string.Join(", ", removed.Take(5))));
        if (resDiff)
        {
            Console.WriteLine(L("  [警告] 该 mod 改动了资源表（精灵/声音/房间/字体）。"));
            Console.WriteLine(L("         差异层只覆盖字节码 → 资源改动不会生效。若这些资源是 mod 的核心内容，"));
            Console.WriteLine(L("         请把它作为**基底**（references.assets=inherit），或改用精灵/声音包重新打包。"));
        }

        var codes = changed.Concat(added).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
        if (codes.Count == 0)
        {
            Console.WriteLine(L("[错误] 相对基线没有任何字节码差异，无法生成差异层"));
            return 1;
        }

        // 6) 名称 / 作者
        var layerName = string.IsNullOrWhiteSpace(name) ? "layer_" + Path.GetFileName(Path.GetDirectoryName(modified) ?? "mod") : name!;
        var layerAuthor = string.IsNullOrWhiteSpace(author) ? "converted" : author!;
        var id = "layer." + DeltaImport.Sanitize(layerName) + "." + DeltaImport.Sanitize(layerAuthor);
        var modRoot = Path.Combine(Paths.NeutraledRoot(gameRoot), "mods", DeltaImport.Sanitize(layerName), DeltaImport.Sanitize(layerAuthor), chapter);

        Console.WriteLine(L("  层: {0} / {1} / {2}  id={3}", layerName, layerAuthor, chapter, id));
        Console.WriteLine(L("  references.codes: {0} 个{1}", codes.Count, (includeOverride ? L("（并声明 override：后加载者胜）") : "")));

        if (dryRun)
        {
            Console.WriteLine(L("  [dry-run] 不写盘"));
            return 0;
        }

        Directory.CreateDirectory(Path.Combine(modRoot, "ref"));
        File.Copy(modified, Path.Combine(modRoot, "ref", "data.win"), true);

        var references = new Dictionary<string, object?>
        {
            ["source"] = "ref/data.win",
            ["codes"] = codes
        };
        if (includeOverride) references["override"] = codes;
        references["assets"] = "layer";

        var modJson = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["name"] = layerName,
            ["author"] = layerAuthor,
            ["version"] = "1.0.0",
            ["enabled"] = true,
            ["description"] = $"差异层：由 {Path.GetFileName(modified)} 相对官方基线的 {codes.Count} 个字节码改动提取（改动 {changed.Count} / 新增 {added.Count}）",
            ["references"] = references
        };
        File.WriteAllText(Path.Combine(modRoot, "mod.json"), JsonSerializer.Serialize(modJson, JsonOpts));

        Console.WriteLine(L("  产物: ") + modRoot);
        Console.WriteLine(L("  说明: 该层可叠加在任意基底之上；若与其它层改同一对象，部署时会报冲突（功能冲突，需人工裁决）"));
        return 0;
    }

    public static string? InferChapterFromPath(string path)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
        for (int i = 0; i < 6 && dir != null; i++)
        {
            var n = dir.Name.ToLowerInvariant();
            var m = Regex.Match(n, @"^(chapter\d+)(_(windows|linux|unix|macos))?$");
            if (m.Success) return m.Groups[1].Value;
            if (Regex.IsMatch(n, @"^ch0?(\d+)$")) return "chapter" + Regex.Match(n, @"^ch0?(\d+)$").Groups[1].Value;
            if (n == "root") return "root";
            dir = dir.Parent;
        }
        return null;
    }
}
