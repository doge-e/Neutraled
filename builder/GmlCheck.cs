using System.Text.Json;
using System.Text.RegularExpressions;
using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Decompiler;
using UndertaleModLib.Models;
using Underanalyzer.Decompiler;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>**编译自检**：把某个 mod/层目录里的 GML 源，按部署时的**同一套规则**排进 CodeImportGroup 编译一遍，
/// 逐个文件报告「哪些 .gml 在真编译时过不去」，并把报错行附近的源码打出来。
///
/// ★ 为什么需要它：--layer-from-base 是「反编译 → 存 .gml → 部署时重新编译」，而反编译器的输出
///   **不保证**能原样编译回去（实测：dojo 的 gml_Script_dj_form_apply 在部署到 [4/5] 注入时才炸
///   「Expression floating outside of any statement around line 23」，白等几分钟）。
///   这条命令只加载 data.win 后逐个文件编译，秒级定位到文件与行。
///
/// 用法: ntl-builder.exe --gml-check &lt;data.win&gt; &lt;mod 章节目录&gt;
/// 退出码: 0 = 全部通过（或有缺文件） / 1 = 有编译失败（CI 友好）</summary>
public static class GmlCheck
{
    public static int Run(string dataWin, string modDir, string? mode = null)
    {
        Opt opt;
        try { opt = Opt.Parse(mode); }
        catch (Exception ex) { Console.WriteLine(L("[错误] ") + ex.Message); return 1; }
        if (!File.Exists(dataWin)) { Console.WriteLine(L("找不到: ") + dataWin); return 1; }

        Console.WriteLine(L("编译自检: {0}", dataWin));
        Console.WriteLine(L("  层: {0}", modDir));
        if (opt.Any) Console.WriteLine(L("  模拟部署差异: {0}", opt.Describe));

        var data = LoadData(dataWin);
        var used = new HashSet<string>(data.Scripts.Select(s => s.Name?.Content ?? ""), StringComparer.Ordinal);
        var items = new List<(string CodeName, string Path, string What)>();
        var dirs = modDir.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int nPatch = 0, nEvt = 0;
        foreach (var dir in dirs)
        {
            var jsonPath = Path.Combine(dir, "mod.json");
            if (!File.Exists(jsonPath)) { Console.WriteLine(L("找不到: ") + jsonPath); return 1; }
            ModEntry meta;
            try { meta = JsonSerializer.Deserialize<ModEntry>(File.ReadAllText(jsonPath), Paths.Json)!; }
            catch (Exception ex) { Console.WriteLine(L("[错误] mod.json 解析失败: ") + ex.Message); return 1; }
            if (meta == null) { Console.WriteLine(L("[错误] mod.json 是空的")); return 1; }
            var got = Collect(data, meta, dir, opt, used);
            items.AddRange(got);
            nPatch += meta.Patches.Count;
            nEvt += meta.Objects.Sum(o => o.Events.Count);
            if (dirs.Length > 1) Console.WriteLine(L("  + {0}: 排入 {1} 条", meta.Id, got.Count));
        }
        Console.WriteLine(L("  待编译: {0} 条（新脚本 {1} / patch {2} / 对象事件 {3}）",
            items.Count, items.Count(x => x.What == "新脚本"), nPatch, nEvt));

        // 第一遍：与部署完全一致（一个 group 全排进去）
        var bad = new List<string>();
        var oneShot = TryOneGroup(data, items, opt, dirs, out var bigError);
        if (oneShot)
        {
            Console.WriteLine(L("  [通过] 全部 {0} 条一次性编译成功", items.Count));
            return 0;
        }

        Console.WriteLine(L("  [失败] 整批编译不通过，改为逐个文件定位（只报失败项）"));
        Console.WriteLine("         " + string.Join("\n         ", FirstLines(bigError, 4)));

        // 第二遍：重新载入 data.win（第一遍的 Import 可能已经改了内存里的数据），逐个文件编译
        data = LoadData(dataWin);
        var seen = new HashSet<int>();
        int ok = 0, missing = 0;
        foreach (var it in items)
        {
            if (!File.Exists(it.Path)) { missing++; Console.WriteLine(L("  [缺文件] {0} ← {1}", it.CodeName, it.Path)); continue; }
            var src = File.ReadAllText(it.Path);
            if (TryOne(data, it, src, opt, out var err)) { ok++; continue; }
            bad.Add(it.CodeName);
            Console.WriteLine(L("  [失败] {0}（{1}）", it.CodeName, it.What));
            Console.WriteLine("         " + it.Path);
            foreach (var l in FirstLines(err, 6)) Console.WriteLine("         " + l);
            foreach (Match m in Regex.Matches(err ?? "", @"line (\d+)"))
            {
                if (!int.TryParse(m.Groups[1].Value, out var ln) || !seen.Add(ln)) continue;
                foreach (var t in Around(src, ln)) Console.WriteLine("         " + t);
            }
        }
        Console.WriteLine(L("  逐个编译: 通过 {0} / 失败 {1} / 缺文件 {2}", ok, bad.Count, missing));
        if (bad.Count > 0)
        {
            Console.WriteLine(L("  [结论] {0} 个文件编译不过 ⇒ 部署会在 [4/5] 注入处直接失败（游戏不会更新）", bad.Count));
            foreach (var b in bad) Console.WriteLine("    " + b);
            return 1;
        }
        return 0;
    }

    /// <summary>收集要编译的条目：代码条目名 + 源文件 + 类别（与 Injector 的排布规则一致）。</summary>
    private static List<(string CodeName, string Path, string What)> Collect(UndertaleData data, ModEntry meta, string modDir, Opt opt, HashSet<string> used)
    {
        var items = new List<(string, string, string)>();
        // gml/ → 新脚本：Injector 0) 预扫描 = 文件名优先，与已有脚本撞名时加 ntl_<id>_ 前缀
        var gmlDir = Path.Combine(modDir, "gml");
        if (Directory.Exists(gmlDir))
        {
            var safeId = DeltaImport.Sanitize(meta.Id);
            foreach (var f in Directory.GetFiles(gmlDir, "*.gml").OrderBy(x => x, StringComparer.Ordinal))
            {
                var stem = Path.GetFileNameWithoutExtension(f);
                var sn = stem;
                if (used.Contains(sn)) sn = "ntl_" + safeId + "_" + stem;
                used.Add(sn);
                items.Add(((opt.Global ? "gml_GlobalScript_" : "gml_Script_") + sn, f, "新脚本"));
            }
        }
        foreach (var p in meta.Patches)
            items.Add((p.Target, Path.Combine(modDir, p.File), "patch"));
        foreach (var o in meta.Objects)
            foreach (var e in o.Events)
                items.Add((ObjectEvents.CodeName(o.Name, e.Type, e.Subtype), Path.Combine(modDir, e.File), "对象事件 " + o.Name));
        return items;
    }

    private static UndertaleData LoadData(string path)
    {
        using var fs = File.OpenRead(path);
        return UndertaleIO.Read(fs, null, m => { });
    }

    private static bool TryOneGroup(UndertaleData data, List<(string CodeName, string Path, string What)> items, Opt opt, string[] dirs, out string? error)
    {
        error = null;
        try
        {
            var group = NewGroup(data);
            foreach (var it in items)
            {
                if (!File.Exists(it.Path)) continue;
                var code = GetOrCreate(data, it.CodeName, it.What, opt);
                group.QueueReplace(code, File.ReadAllText(it.Path));
            }
            if (opt.Res) ImportResources(data, opt, dirs);
            group.Import(true);
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    private static bool TryOne(UndertaleData data, (string CodeName, string Path, string What) it, string src, Opt opt, out string? error)
    {
        error = null;
        try
        {
            var group = NewGroup(data);
            var code = GetOrCreate(data, it.CodeName, it.What, opt);
            group.QueueReplace(code, src);
            group.Import(true);
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    /// <summary>按模式差异造/取代码条目（部署是「无条件 CreateEmptyEntry + 建脚本资源」）。</summary>
    private static UndertaleCode GetOrCreate(UndertaleData data, string codeName, string what, Opt opt)
    {
        var code = opt.Dup
            ? UndertaleCode.CreateEmptyEntry(data, codeName)
            : (data.Code.ByName(codeName) ?? UndertaleCode.CreateEmptyEntry(data, codeName));
        if (opt.Asset && what.StartsWith("新脚本", StringComparison.Ordinal))
        {
            var asset = AssetName(codeName);
            if (asset != null && !data.Scripts.Any(s => string.Equals(s.Name?.Content, asset, StringComparison.Ordinal)))
                data.Scripts.Add(new UndertaleScript { Name = data.Strings.MakeString(asset), Code = code });
        }
        return code;
    }

    private static string? AssetName(string codeName)
    {
        if (codeName.StartsWith("gml_Script_", StringComparison.Ordinal)) return codeName.Substring("gml_Script_".Length);
        if (codeName.StartsWith("gml_GlobalScript_", StringComparison.Ordinal)) return codeName.Substring("gml_GlobalScript_".Length);
        return null;
    }

    /// <summary>模拟部署时的条目语义：asset / dup / global，逗号可组合；deploy = asset+dup。</summary>
    private sealed class Opt
    {
        public bool Asset, Dup, Global;
        public bool Sprites, Sounds;
        public bool Res => Sprites || Sounds;
        public bool Any => Asset || Dup || Global || Res;
        public string Describe => string.Join("+", new[] { Asset ? "asset" : null, Dup ? "dup" : null, Global ? "global" : null, Sprites ? "sprites" : null, Sounds ? "sounds" : null }.Where(x => x != null));

        public static Opt Parse(string? mode)
        {
            var o = new Opt();
            if (string.IsNullOrWhiteSpace(mode)) return o;
            foreach (var t in mode.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                switch (t.ToLowerInvariant())
                {
                    case "check": break;
                    case "asset": o.Asset = true; break;
                    case "dup": o.Dup = true; break;
                    case "global": o.Global = true; break;
                    case "deploy": o.Asset = true; o.Dup = true; break;
                    case "res": o.Sprites = true; o.Sounds = true; break;
                    case "sprites": o.Sprites = true; break;
                    case "sounds": o.Sounds = true; break;
                    default: throw new ArgumentException("未知模式: " + t + "（可用: check / asset / dup / global / deploy / res / sprites / sounds，逗号组合）");
                }
            return o;
        }
    }

    /// <summary>模拟部署 2.2/2.3 段：排完队列、编译之前导入精灵/声音资源包（这是复现「资源导入 + 编译」组合问题的关键一步）。</summary>
    private static void ImportResources(UndertaleData data, Opt opt, string[] dirs)
    {
        foreach (var dir in dirs)
        {
            if (opt.Sprites)
            {
                var spDir = Path.Combine(dir, "sprites");
                if (Directory.Exists(spDir))
                {
                    Console.WriteLine(L("  资源: 精灵包 {0}", spDir));
                    SpriteImport.Import(data, spDir);
                }
            }
            if (opt.Sounds)
            {
                var sndDir = Path.Combine(dir, "sounds");
                if (Directory.Exists(sndDir))
                {
                    Console.WriteLine(L("  资源: 声音包 {0}", sndDir));
                    SoundImport.Import(data, sndDir);
                }
            }
        }
    }

    private static CodeImportGroup NewGroup(UndertaleData data)
    {
        IDecompileSettings? settings = null;
        return new CodeImportGroup(data, new GlobalDecompileContext(data), settings!);
    }

    private static IEnumerable<string> FirstLines(string? text, int n)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
        return text.Replace("\r", "").Split('\n').Where(x => x.Trim().Length > 0).Take(n).Select(x => x.TrimEnd());
    }

    /// <summary>把报错行附近的源码打出来（带行号），省一次开文件。</summary>
    private static IEnumerable<string> Around(string src, int line)
    {
        var all = src.Replace("\r", "").Split('\n');
        int from = Math.Max(1, line - 3), to = Math.Min(all.Length, line + 3);
        for (int i = from; i <= to; i++)
            yield return (i == line ? "  > " : "    ") + i.ToString().PadLeft(4) + "| " + all[i - 1];
    }
}
