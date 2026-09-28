using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UndertaleModLib;
using UndertaleModLib.Models;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>**资源名守卫**（部署时自动跑；**只告警，不中止**）。
///
/// ★ 为什么需要它：层只带**代码**、不带精灵/音效等资源。代码按名字引用资源时若产物里没有这个名字：
///     - 裸标识符形式：编译期报 unknown identifier，部署直接失败（看得见）；
///     - **字符串形式**（`asset_get_index("snd_x")`）：编译期一路通过，运行期静默返回 -1 ——
///       要等到某个房间/某个事件才炸，甚至永远不炸，只是特效、声音悄悄没了。
///   这条守卫就是把后者在**部署期**点名出来。
///
/// 判据两条：
///   A) 字符串型：扫 `asset_get_index("X")` / `sprite_exists("X")` … 的字面量参数，X 不在产物里 ⇒ 报（无假阳性）。
///   B) 声明型：mod 自带 `ref/data.win` 时（整包 mod / 转换产物），拿它的名字表当证据 ——
///      代码里出现的「像资源名 + 产物里没有 + 在该 mod 自己的 data.win 里确实存在」的标识符 ⇒ 报。
///      没有 ref/data.win 的纯层不做 B（否则会把局部变量、函数名当资源名报出来）。
///
/// 用法：部署时自动调用；也可单独跑
///   ntl-builder.exe --asset-guard &lt;data.win&gt; &lt;层目录[;目录…]&gt;</summary>
public static class AssetNameGuard
{
    /// <summary>「像资源名」的前缀（GameMaker 惯例）：B 判据靠它挡掉局部变量与函数名。</summary>
    private static readonly string[] ResourcePrefixes =
    {
        "spr_", "snd_", "mus_", "obj_", "rm_", "room_", "fnt_", "bg_", "bgr_",
        "path_", "tl_", "sh_", "ps_", "seq_", "animcu_", "sprt_", "cfg_",
    };

    private static readonly Regex StringRef = new(
        "\\b(?:asset_get_index|sprite_exists|sound_exists|audio_exists|font_exists|background_exists|" +
        "path_exists|shader_exists|script_exists|room_exists|object_exists|sequence_exists|" +
        "timeline_exists|particle_exists)\\s*\\(\\s*\"([^\"\\r\\n]+)\"",
        RegexOptions.Compiled);

    private static readonly Regex Token = new("[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled);

    /// <summary>ref/data.win 超过这个大小就不做 B 判据（部署期不想再多背几百 MB）。</summary>
    private const long RefLimit = 128L * 1024 * 1024;

    public static void Run(UndertaleData product, List<ModEntry> mods)
    {
        var present = AllNames(product);
        int files = 0, warnMods = 0, warnNames = 0, dynSkipped = 0;
        foreach (var m in mods)
        {
            var srcs = Sources(m.Dir);
            if (srcs.Count == 0) continue;
            files += srcs.Count;
            var hits = new Dictionary<string, string>(StringComparer.Ordinal);   // 资源名 → 证据

            // ---- A) 字符串型：无假阳性（拼接出来的片段不算，见 IsConcatenated）----
            foreach (var (rel, text) in srcs)
                foreach (Match mm in StringRef.Matches(text))
                {
                    var nm = mm.Groups[1].Value;
                    if (nm.Length == 0 || present.Contains(nm) || hits.ContainsKey(nm)) continue;
                    if (IsConcatenated(text, mm, nm)) { dynSkipped++; continue; }   // "spr_" + checkstring + "_idle"
                    hits[nm] = L("{0}:{1}（asset_get_index 字面量）", rel, LineOf(text, mm.Index));
                }

            // ---- B) 声明型：只有自带 ref/data.win 时才做 ----
            string? refNote = null;
            var refPath = RefDataWin(m);
            if (refPath != null)
            {
                var size = new FileInfo(refPath).Length;
                if (size > RefLimit)
                {
                    refNote = L("ref/data.win 过大（{0:F1} MB），跳过声明型判据", size / 1048576.0);
                }
                else
                {
                    var cand = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var (rel, text) in srcs)
                        foreach (Match tm in Token.Matches(text))
                        {
                            var t = tm.Value;
                            if (t.Length < 4 || !ResourceLike(t)) continue;
                            if (present.Contains(t) || hits.ContainsKey(t) || cand.ContainsKey(t)) continue;
                            cand[t] = L("{0}:{1}（标识符）", rel, LineOf(text, tm.Index));
                        }
                    if (cand.Count > 0)
                    {
                        var table = Encoding.Latin1.GetString(File.ReadAllBytes(refPath));
                        foreach (var kv in cand)
                            if (table.Contains(kv.Key, StringComparison.Ordinal))
                                hits[kv.Key] = kv.Value + L("；该 mod 的 ref/data.win 里有这个名字");
                    }
                }
            }

            if (hits.Count == 0) continue;
            warnMods++;
            warnNames += hits.Count;
            Paths.Log(L("    [警告] {0}: 代码引用了产物里不存在的资源名 {1} 个", m.Id, hits.Count));
            foreach (var kv in hits.OrderBy(k => k.Key, StringComparer.Ordinal).Take(12))
                Paths.Log(L("        {0} ← {1}", kv.Key, kv.Value));
            if (hits.Count > 12) Paths.Log(L("        …另有 {0} 个未列出", hits.Count - 12));
            if (refNote != null) Paths.Log("        " + refNote);
        }

        if (files == 0) return;
        if (warnMods == 0)
        {
            Paths.Log(L("  资源名守卫: 未发现引用产物里不存在的资源名（扫了 {0} 个 .gml）", files));
            if (dynSkipped > 0) Paths.Log(L("      （另有 {0} 处拼接出来的名字，如 \"spr_\" + x，运行期才成形，不判）", dynSkipped));
            return;
        }
        Paths.Log(L("  资源名守卫: {0} 个 mod / {1} 个资源名（**只告警不中止**）", warnMods, warnNames));
        if (dynSkipped > 0) Paths.Log(L("      （另有 {0} 处拼接出来的名字，如 \"spr_\" + x，运行期才成形，不判）", dynSkipped));
        Paths.Log(L("      层只带代码不带资源：要带资源请用 --export-packs，或在代码里加 sprite_exists()/audio_exists() 守卫"));
    }

    /// <summary>字面量是拼接片段吗（`"spr_" + x` / `x + "spr_..."`）？
    /// 这种名字运行期才成形，判据 A 无从判断 —— 之前把 `sprite_exists(asset_get_index("spr_" + checkstring + "_idle"))`
    /// 里的 `spr_` 报成缺资源，是 2026-09-27 修掉的假阳性。</summary>
    private static bool IsConcatenated(string text, Match mm, string nm)
    {
        int end = mm.Index + mm.Length;                       // 字面量闭合引号之后
        int i = end;
        while (i < text.Length && (text[i] == ' ' || text[i] == '\t')) i++;
        if (i < text.Length && text[i] == '+') return true;
        int litStart = end - nm.Length - 1;                   // 字面量开引号
        int j = litStart - 1;
        while (j >= 0 && (text[j] == ' ' || text[j] == '\t')) j--;
        return j >= 0 && text[j] == '+';
    }

    /// <summary>收集 data.win 里所有「有名资源」的名字（精灵/音效/对象/房间/字体/背景/路径/时间线/着色器/脚本/序列…）。
    /// 用反射遍历，免得每次 UndertaleModLib 加一类资源就要改这里。</summary>
    public static HashSet<string> AllNames(UndertaleData d)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in typeof(UndertaleData).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanRead || p.GetIndexParameters().Length != 0) continue;
            var t = p.PropertyType;
            if (!t.IsGenericType || t.GetGenericArguments().Length != 1) continue;
            if (!typeof(UndertaleNamedResource).IsAssignableFrom(t.GetGenericArguments()[0])) continue;
            object? v;
            try { v = p.GetValue(d); } catch { continue; }
            if (v is not System.Collections.IEnumerable items) continue;
            foreach (var it in items)
                if (it is UndertaleNamedResource nr && nr.Name?.Content is { Length: > 0 } s) set.Add(s);
        }
        return set;
    }

    /// <summary>层/mod 章节目录里所有参与编译的 GML 源（与 Injector 读的目录一致）。</summary>
    private static List<(string Rel, string Text)> Sources(string dir)
    {
        var list = new List<(string, string)>();
        foreach (var sub in new[] { "gml", "patches", "objects", "raw" })
        {
            var d = Path.Combine(dir, sub);
            if (!Directory.Exists(d)) continue;
            foreach (var f in Directory.GetFiles(d, "*.gml", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
            {
                string text;
                try { text = File.ReadAllText(f); } catch { continue; }
                list.Add((Path.GetRelativePath(dir, f).Replace('\\', '/'), text));
            }
        }
        return list;
    }

    private static string? RefDataWin(ModEntry m)
    {
        if (!string.IsNullOrWhiteSpace(m.RefSource))
        {
            var p = Path.IsPathRooted(m.RefSource!) ? m.RefSource! : Path.Combine(m.Dir, m.RefSource!);
            if (File.Exists(p)) return p;
        }
        var def = Path.Combine(m.Dir, "ref", "data.win");
        return File.Exists(def) ? def : null;
    }

    private static bool ResourceLike(string t)
    {
        foreach (var p in ResourcePrefixes)
            if (t.StartsWith(p, StringComparison.Ordinal)) return true;
        return false;
    }

    private static int LineOf(string text, int index)
    {
        int line = 1;
        for (int i = 0; i < index && i < text.Length; i++)
            if (text[i] == '\n') line++;
        return line;
    }

    /// <summary>独立入口：--asset-guard &lt;data.win&gt; &lt;层目录[;目录…]&gt;</summary>
    public static int Cli(string dataWin, string modDirs)
    {
        if (!File.Exists(dataWin)) { Console.WriteLine(L("找不到: ") + dataWin); return 1; }
        var mods = new List<ModEntry>();
        foreach (var dir in modDirs.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var json = Path.Combine(dir, "mod.json");
            if (!File.Exists(json)) { Console.WriteLine(L("找不到: ") + json); return 1; }
            try
            {
                var m = JsonSerializer.Deserialize<ModEntry>(File.ReadAllText(json), Paths.Json);
                if (m == null) { Console.WriteLine(L("[错误] mod.json 是空的")); return 1; }
                m.Dir = dir;                       // JSON 里没有 Dir，必须补上（Sources 靠它找 gml/patches/objects）
                mods.Add(m);
            }
            catch (Exception ex) { Console.WriteLine(L("[错误] mod.json 解析失败: ") + ex.Message); return 1; }
        }
        Console.WriteLine(L("资源名守卫: {0}", dataWin));
        Console.WriteLine(L("  待查: {0} 个目录", mods.Count));
        Run(Injector.Load(dataWin), mods);
        return 0;
    }
}
