using UndertaleModLib;
using UndertaleModLib.Models;
using UndertaleModLib.Decompiler;
using Underanalyzer.Decompiler;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>
/// RefCopy 保真度验证器 —— 用**反编译器当预言机**，而不是靠猜字节码语义。
///
/// 思路：
///   1) 从源 data.win 反编译若干代码对象 → 文本 A
///   2) 用 RefCopy 把这些对象**复制进目标 data.win**（两者资源池不同，正是跨文件场景）
///   3) 在目标 data.win 里再反编译同一批对象 → 文本 B
///   4) A == B  ⇒ 复制保真（索引重映射正确）
///      A != B  ⇒ 复制有损，报告第一处差异行
///
/// 这个验证不需要事先知道每个 opcode 的操作数语义 —— 反编译器已经把索引解析成名字了。
/// </summary>
public static class RefCopyVerify
{
    public static int Verify(string srcWin, string dstWin, int max = 20, string? filter = null)
    {
        if (!File.Exists(srcWin)) { Console.WriteLine(L("[错误] 找不到源: ") + srcWin); return 1; }
        if (!File.Exists(dstWin)) { Console.WriteLine(L("[错误] 找不到目标: ") + dstWin); return 1; }

        Console.WriteLine(L("===== RefCopy 保真度验证 ====="));
        Console.WriteLine(L("  源:   ") + srcWin);
        Console.WriteLine(L("  目标: ") + dstWin);

        var src = Injector.Load(srcWin);
        var dst = Injector.Load(dstWin);

        var dstNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in dst.Code)
        {
            var n = c.Name?.Content;
            if (!string.IsNullOrEmpty(n)) dstNames.Add(n);
        }

        // 挑"两边都有"且有指令的对象
        var names = new List<string>();
        foreach (var c in src.Code)
        {
            var n = c.Name?.Content;
            if (string.IsNullOrEmpty(n)) continue;
            if (!dstNames.Contains(n)) continue;
            if (c.Instructions.Count == 0) continue;
            if (filter != null && !n.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            names.Add(n);
            if (names.Count >= max) break;
        }
        if (names.Count == 0) { Console.WriteLine(L("[错误] 没有共同代码对象")); return 1; }
        Console.WriteLine(L("  样本: {0} 个对象（max={1}）", names.Count, max));

        // 1) 源侧反编译
        var gctxSrc = new GlobalDecompileContext(src);
        var before = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var n in names)
        {
            var code = src.Code.ByName(n);
            try { before[n] = Normalize(Decompile(gctxSrc, code!)); }
            catch (Exception ex) { Console.WriteLine(L("  [警告] 源反编译失败 {0}: {1}", n, ex.Message)); }
        }

        // 2) 复制进目标
        Console.WriteLine(L("  执行 RefCopy..."));
        var copied = RefCopy.Copy(dst, src, names.Where(before.ContainsKey), new HashSet<string>(StringComparer.Ordinal), null);
        Console.WriteLine(L("  已复制 {0} 个对象", copied));

        // 3) 目标侧反编译
        var gctxDst = new GlobalDecompileContext(dst);
        int ok = 0, diff = 0, fail = 0, dupTotal = 0, dupFixed = 0;
        var diffs = new List<string>();
        foreach (var n in names)
        {
            if (!before.ContainsKey(n)) continue;
            var all = dst.Code.Where(c => c.Name?.Content == n).ToList();
            if (all.Count == 0) { fail++; diffs.Add(n + L(" ← 复制后目标里找不到该对象")); continue; }
            if (all.Count > 1) dupTotal += all.Count - 1;

            // 关键：若复制是"新建同名条目"而不是替换，ByName 会命中旧条目 → 这里对每个同名条目都反编译，取最接近的比
            string after; UndertaleCode? hit = null;
            try
            {
                var best = "";
                foreach (var cand in all)
                {
                    var txt = Normalize(Decompile(gctxDst, cand));
                    if (txt == before[n]) { hit = cand; best = txt; break; }
                    if (best.Length == 0) { best = txt; hit = cand; }
                }
                after = best;
            }
            catch (Exception ex)
            {
                fail++;
                var sc2 = src.Code.ByName(n); var dc2 = dst.Code.ByName(n);
                var info = (sc2 != null && dc2 != null) ? FirstInstrDiff(sc2, dc2) : L("(取不到对象)");
                var kindHist = sc2 == null ? "" : KindHistogram(sc2);
                diffs.Add(n + L(" ← 目标反编译失败: ") + Short(ex.Message) + L("\n        指令级: ") + info + L("\n        源指令类别: ") + kindHist);
                continue;
            }

            if (after == before[n])
            {
                ok++;
                if (all.Count > 1) dupFixed++;
            }
            else
            {
                diff++;
                var sc = src.Code.ByName(n); var dc = dst.Code.ByName(n);
                var instr = (sc != null && dc != null) ? FirstInstrDiff(sc, dc) : L("(取不到对象)");
                diffs.Add(n + L(" ← 文本不一致：") + FirstDiff(before[n], after) + L("\n        指令级: ") + instr);
            }
        }

        Console.WriteLine();
        Console.WriteLine(L("  结果: 保真 {0} / 不一致 {1} / 失败 {2}   （共 {3}）", ok, diff, fail, ok + diff + fail));
        if (dupTotal > 0)
        {
            Console.WriteLine(L("  [重要] 出现同名重复代码条目 {0} 个（{1} 个样本经新条目才保真）", dupTotal, dupFixed));
            Console.WriteLine(L("         → 说明 RefCopy 是**新建条目**而不是**替换同名条目**；"));
            Console.WriteLine(L("           部署时 ByName() 会命中旧条目，改动静默不生效。"));
        }
        if (diffs.Count > 0)
        {
            Console.WriteLine(L("  --- 问题明细（前 10）---"));
            foreach (var d in diffs.Take(10)) Console.WriteLine("    " + d);
        }
        Console.WriteLine(diff == 0 && fail == 0
            ? L("  [结论] RefCopy 在本批样本上**保真**：跨资源池复制后语义一致")
            : L("  [结论] 存在有损复制，需要针对上面这些对象补索引重映射"));
        return (diff == 0 && fail == 0) ? 0 : 2;
    }

    private static string Decompile(GlobalDecompileContext gctx, UndertaleCode code)
    {
        var dctx = new Underanalyzer.Decompiler.DecompileContext(gctx, code, null!);
        return dctx.DecompileToString();
    }

    private static string Normalize(string s)
    {
        var lines = s.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        return string.Join("\n", lines.Select(l => l.TrimEnd())).Trim();
    }

    /// <summary>逐条指令比字段，定位 RefCopy 到底丢了什么（尤其是 double 常量）。</summary>
    private static string FirstInstrDiff(UndertaleCode a, UndertaleCode b)
    {
        if (a.Instructions.Count != b.Instructions.Count)
            return L("指令数 {0} vs {1}", a.Instructions.Count, b.Instructions.Count);
        for (int i = 0; i < a.Instructions.Count; i++)
        {
            var x = a.Instructions[i]; var y = b.Instructions[i];
            if (x.Kind != y.Kind) return $"[{i}] Kind {x.Kind} vs {y.Kind}";
            bool intDiff = x.ValueInt != y.ValueInt, longDiff = x.ValueLong != y.ValueLong;
            bool dblDiff = !Nullable.Equals(x.ValueDouble, y.ValueDouble);
            bool shortDiff = x.ValueShort != y.ValueShort;
            if (intDiff || longDiff || dblDiff || shortDiff)
                return $"[{i}] {x.Kind}  I:{x.ValueInt}->{y.ValueInt}  L:{x.ValueLong}->{y.ValueLong}  D:{x.ValueDouble}->{y.ValueDouble}  S:{x.ValueShort}->{y.ValueShort}";
        }
        return L("(指令流字段完全一致 → 差异来自上下文)");
    }

    private static string FirstDiff(string a, string b)
    {
        var la = a.Split('\n'); var lb = b.Split('\n');
        int n = Math.Min(la.Length, lb.Length);
        for (int i = 0; i < n; i++)
            if (la[i] != lb[i]) return L("第 {0} 行 源「{1}」 vs 目标「{2}」", i + 1, Clip(la[i]), Clip(lb[i]));
        return L("行数不同 {0} vs {1}", la.Length, lb.Length);
    }

    private static string Clip(string s) { s = s.Trim(); return s.Length <= 200 ? s : s[..200] + "..."; }

    /// <summary>统计对象里分支类指令占比（用于判断控制流错误是否来自跳转指令）。</summary>
    private static string KindHistogram(UndertaleCode c)
    {
        var d = new Dictionary<string, int>();
        foreach (var i in c.Instructions)
        {
            var k = i.Kind.ToString();
            d[k] = d.TryGetValue(k, out var v) ? v + 1 : 1;
        }
        return string.Join(", ", d.OrderByDescending(kv => kv.Value).Take(6).Select(kv => kv.Key + "x" + kv.Value));
    }
    private static string Short(string s) => s.Length <= 100 ? s : s[..100] + "...";
}
