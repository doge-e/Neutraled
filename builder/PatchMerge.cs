using UndertaleModLib;
using UndertaleModLib.Models;
using UndertaleModLib.Decompiler;
using Underanalyzer.Decompiler;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>
/// 行级三方合并（diff3）—— 把「整脚本覆盖 patch」合并到「已被基底改过的对象」上。
///
/// 为什么需要它：
///   patch 是**整脚本覆盖**（内容 = 官方基线 + 该 mod 的改动）。当基底不是原版时
///   （例如把整包 mod「DOJO」当基底），覆盖会把基底对这个对象的改动一起冲掉。
///   实测（DOJO 当基底 + 60 FPS 层）：ch1 真改动重叠 10 个、ch2 15、ch3 18、ch4 16、ch5 18、root 1
///   —— obj_battlecontroller_Step_0 / obj_heart_Step_0 / scr_gamestart 这类核心对象两边都改过，
///   直接覆盖轻则掉帧适配、重则 DOJO 战斗逻辑失效。
///
/// 做法：base = 官方基线源码，ours = 当前基底源码，theirs = patch 源码
///   两边改不同位置 → 自动合到一起
///   同一位置冲突    → 保守取 ours（基底），记入冲突报告（宁可少一个帧率微调，
///                     也不能把基底（DOJO）的改动冲掉）
/// </summary>
public static class PatchMerge
{
    /// <summary>行级三方合并。返回合并文本 + 冲突块数 + 改动块数。</summary>
    ///
    /// 算法（先各自求改动块，再按基线坐标聚类；不是「走一格看一格」）：
    ///   1. 基线↔ours、基线↔theirs 各求一遍改动块 hunk（一段基线行 + 替换它的那段源码行）
    ///   2. 两边的 hunk 按基线坐标聚类：**共享基线行**的进同一组；同一点的**纯插入**也进同一组
    ///   3. 一组只有一边动过 → 用那边的替换文本；两边都动过 → 冲突：保守取 ours（基底）并计数，
    ///      两边改成一模一样的内容则不算冲突
    ///   4. 组与组之间的基线行三边一致，原样输出
    /// 这样「相邻但不相交」的两处改动（基底改第 3 行、patch 改第 4 行）各自落地，
    /// 只有真正改到同一片基线行时才判冲突。
    public static (string text, int conflicts, int hunks) Merge3(string baseText, string oursText, string theirsText)
    {
        var b = SplitLines(baseText);
        var o = SplitLines(oursText);
        var t = SplitLines(theirsText);

        var ho = Hunks(b, o, Align(b, o));
        var ht = Hunks(b, t, Align(b, t));
        var groups = GroupHunks(ho, ht);

        var sb = new System.Text.StringBuilder();
        int conflicts = 0, hunks = 0, i = 0, g = 0;
        while (i < b.Length || g < groups.Count)
        {
            if (g < groups.Count && groups[g].Lo <= i)
            {
                var gr = groups[g++];
                hunks++;
                if (gr.HasO && gr.HasT)
                {
                    var os = Slice(o, gr.OStart, gr.OEnd);
                    var ts = Slice(t, gr.TStart, gr.TEnd);
                    if (SameSeq(os, ts)) Append(sb, os);        // 两边改成一样的东西 → 不算冲突
                    else { conflicts++; Append(sb, os); }       // 真冲突：保守取基底
                }
                else if (gr.HasO) Append(sb, Slice(o, gr.OStart, gr.OEnd));
                else Append(sb, Slice(t, gr.TStart, gr.TEnd));
                if (gr.Hi > i) i = gr.Hi;
                continue;
            }
            if (i < b.Length) { sb.Append(b[i]).Append('\n'); i++; continue; }
            break;
        }
        return (sb.ToString(), conflicts, hunks);
    }

    private struct Hunk { public int B0, B1, S0, S1; }

    /// <summary>基线↔某一边的两路 diff：返回改动块（基线区间 [B0,B1) + 替换它的源码行区间 [S0,S1)）。</summary>
    private static System.Collections.Generic.List<Hunk> Hunks(string[] b, string[] s, int[] map)
    {
        var list = new System.Collections.Generic.List<Hunk>();
        int i = 0, si = 0;
        while (i < b.Length || si < s.Length)
        {
            if (i < b.Length && si < s.Length && map[i] == si) { i++; si++; continue; }
            int j = i, sA = -1;
            while (j < b.Length) { if (map[j] >= si) { sA = map[j]; break; } j++; }
            if (sA < 0) { j = b.Length; sA = s.Length; }
            list.Add(new Hunk { B0 = i, B1 = j, S0 = si, S1 = sA });
            i = j; si = sA;
        }
        return list;
    }

    private struct Group { public int Lo, Hi; public bool HasO, HasT; public int OStart, OEnd, TStart, TEnd; }

    /// <summary>共享基线行（或同点纯插入）的改动块并成一组；组内两边都动过就是冲突。</summary>
    private static System.Collections.Generic.List<Group> GroupHunks(
        System.Collections.Generic.List<Hunk> ho, System.Collections.Generic.List<Hunk> ht)
    {
        var groups = new System.Collections.Generic.List<Group>();
        int po = 0, pt = 0;
        while (po < ho.Count || pt < ht.Count)
        {
            bool seedO;
            if (po >= ho.Count) seedO = false;
            else if (pt >= ht.Count) seedO = true;
            else if (ho[po].B0 != ht[pt].B0) seedO = ho[po].B0 < ht[pt].B0;
            else seedO = (ho[po].B1 - ho[po].B0) >= (ht[pt].B1 - ht[pt].B0);   // 同起点先吃长块，纯插入不吞改动

            int lo, hi;
            bool hasO = false, hasT = false;
            int oLo = 0, oHi = 0, oLoBase = 0, oHiBase = 0, tLo = 0, tHi = 0, tLoBase = 0, tHiBase = 0;
            if (seedO)
            {
                var h = ho[po++];
                lo = h.B0; hi = h.B1;
                hasO = true; oLo = h.S0; oHi = h.S1; oLoBase = h.B0; oHiBase = h.B1;
            }
            else
            {
                var h = ht[pt++];
                lo = h.B0; hi = h.B1;
                hasT = true; tLo = h.S0; tHi = h.S1; tLoBase = h.B0; tHiBase = h.B1;
            }

            bool grew = true;
            while (grew)
            {
                grew = false;
                while (po < ho.Count && Overlaps(lo, hi, ho[po]))
                {
                    var h = ho[po++];
                    if (!hasO) { hasO = true; oLo = h.S0; oLoBase = h.B0; }
                    oHi = h.S1; oHiBase = h.B1;
                    if (h.B1 > hi) hi = h.B1;
                    grew = true;
                }
                while (pt < ht.Count && Overlaps(lo, hi, ht[pt]))
                {
                    var h = ht[pt++];
                    if (!hasT) { hasT = true; tLo = h.S0; tLoBase = h.B0; }
                    tHi = h.S1; tHiBase = h.B1;
                    if (h.B1 > hi) hi = h.B1;
                    grew = true;
                }
            }

            groups.Add(new Group
            {
                Lo = lo,
                Hi = hi,
                HasO = hasO,
                HasT = hasT,
                OStart = hasO ? oLo - (oLoBase - lo) : 0,
                OEnd = hasO ? oHi + (hi - oHiBase) : 0,
                TStart = hasT ? tLo - (tLoBase - lo) : 0,
                TEnd = hasT ? tHi + (hi - tHiBase) : 0,
            });
        }
        // 同一基线下标上的纯插入要排在改动之前，否则插入的行会落到被改块之后
        groups.Sort((x, y) => x.Lo != y.Lo ? x.Lo.CompareTo(y.Lo) : x.Hi.CompareTo(y.Hi));
        return groups;
    }

    /// <summary>改动块 h 是否与已聚的基线区间 [lo,hi) 重叠（共享基线行，或同点纯插入）。</summary>
    private static bool Overlaps(int lo, int hi, Hunk h)
    {
        if (lo == hi) return h.B0 == h.B1 && h.B0 == lo;   // 组是纯插入：只有同点纯插入算重叠
        if (h.B0 == h.B1) return h.B0 > lo && h.B0 < hi;   // 对方是纯插入：插在组内部才算
        return h.B0 < hi;                                  // 共享基线行
    }


    private static void Append(System.Text.StringBuilder sb, string[] lines)
    {
        foreach (var l in lines) sb.Append(l).Append('\n');
    }

    private static string[] Slice(string[] a, int lo, int hi)
    {
        if (hi <= lo) return System.Array.Empty<string>();
        var r = new string[hi - lo];
        System.Array.Copy(a, lo, r, 0, hi - lo);
        return r;
    }

    private static bool SameSeq(string[] a, string[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (!string.Equals(a[i], b[i], System.StringComparison.Ordinal)) return false;
        return true;
    }

    public static string[] SplitLines(string s)
    {
        var lines = s.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        if (lines.Length > 0 && lines[^1].Length == 0)
            lines = lines[..^1];   // 结尾换行不算一行
        return lines;
    }

    /// <summary>把 a 的每一行对齐到 b 的下标（无匹配 = -1）。</summary>
    public static int[] Align(string[] a, string[] b)
    {
        var map = new int[a.Length];
        System.Array.Fill(map, -1);
        var pairs = new System.Collections.Generic.List<(int a, int b)>();
        AlignRec(a, 0, a.Length, b, 0, b.Length, pairs);
        pairs.Sort((x, y) => x.a.CompareTo(y.a));
        foreach (var p in pairs) map[p.a] = p.b;
        return map;
    }

    private static void AlignRec(string[] a, int alo, int ahi, string[] b, int blo, int bhi,
        System.Collections.Generic.List<(int a, int b)> outp)
    {
        while (alo < ahi && blo < bhi && a[alo] == b[blo]) { outp.Add((alo, blo)); alo++; blo++; }
        var suffix = new System.Collections.Generic.List<(int a, int b)>();
        while (alo < ahi && blo < bhi && a[ahi - 1] == b[bhi - 1]) { ahi--; bhi--; suffix.Add((ahi, bhi)); }

        int na = ahi - alo, nb = bhi - blo;
        if (na > 0 && nb > 0)
        {
            long prod = (long)na * nb;
            if (prod <= 4_000_000) DpAlign(a, alo, ahi, b, blo, bhi, outp);
            else UniqueAnchorAlign(a, alo, ahi, b, blo, bhi, outp);
        }
        suffix.Reverse();
        outp.AddRange(suffix);
    }

    /// <summary>小规模：标准 LCS 动态规划（区域先剪掉公共前后缀，实际规模很小）。</summary>
    private static void DpAlign(string[] a, int alo, int ahi, string[] b, int blo, int bhi,
        System.Collections.Generic.List<(int a, int b)> outp)
    {
        int na = ahi - alo, nb = bhi - blo;
        var dp = new int[(na + 1) * (nb + 1)];
        int W = nb + 1;
        for (int i = na - 1; i >= 0; i--)
            for (int j = nb - 1; j >= 0; j--)
                dp[i * W + j] = a[alo + i] == b[blo + j]
                    ? dp[(i + 1) * W + j + 1] + 1
                    : System.Math.Max(dp[(i + 1) * W + j], dp[i * W + j + 1]);
        int x = 0, y = 0;
        while (x < na && y < nb)
        {
            if (a[alo + x] == b[blo + y]) { outp.Add((alo + x, blo + y)); x++; y++; }
            else if (dp[(x + 1) * W + y] >= dp[x * W + y + 1]) x++;
            else y++;
        }
    }

    /// <summary>大规模：唯一行锚点（patience）+ 递归（区域通常很快变小）。</summary>
    private static void UniqueAnchorAlign(string[] a, int alo, int ahi, string[] b, int blo, int bhi,
        System.Collections.Generic.List<(int a, int b)> outp)
    {
        var ca = new System.Collections.Generic.Dictionary<string, int>(System.StringComparer.Ordinal);
        for (int i = alo; i < ahi; i++) ca[a[i]] = ca.TryGetValue(a[i], out var c) ? c + 1 : 1;
        var cb = new System.Collections.Generic.Dictionary<string, int>(System.StringComparer.Ordinal);
        for (int j = blo; j < bhi; j++) cb[b[j]] = cb.TryGetValue(b[j], out var c) ? c + 1 : 1;
        var posB = new System.Collections.Generic.Dictionary<string, int>(System.StringComparer.Ordinal);
        for (int j = blo; j < bhi; j++) if (cb[b[j]] == 1) posB[b[j]] = j;

        var cand = new System.Collections.Generic.List<(int a, int b)>();
        for (int i = alo; i < ahi; i++)
            if (ca[a[i]] == 1 && posB.TryGetValue(a[i], out var j) && j >= blo && j < bhi) cand.Add((i, j));

        // 按 a 递增的 b 序列取 LIS（保证锚点互不交叉）
        var tails = new System.Collections.Generic.List<int>();
        var prev = new int[cand.Count];
        var idxOfTail = new System.Collections.Generic.List<int>();
        for (int k = 0; k < cand.Count; k++)
        {
            int v = cand[k].b;
            int lo = 0, hi = tails.Count;
            while (lo < hi) { int mid = (lo + hi) / 2; if (tails[mid] < v) lo = mid + 1; else hi = mid; }
            prev[k] = lo > 0 ? idxOfTail[lo - 1] : -1;
            if (lo == tails.Count) { tails.Add(v); idxOfTail.Add(k); }
            else { tails[lo] = v; idxOfTail[lo] = k; }
        }
        var anchors = new System.Collections.Generic.List<(int a, int b)>();
        if (idxOfTail.Count > 0)
        {
            int cur = idxOfTail[^1];
            while (cur >= 0) { anchors.Add(cand[cur]); cur = prev[cur]; }
            anchors.Reverse();
        }
        if (anchors.Count == 0) return;

        int pa = alo, pb = blo;
        foreach (var (ai, bi) in anchors)
        {
            AlignRec(a, pa, ai, b, pb, bi, outp);
            outp.Add((ai, bi));
            pa = ai + 1; pb = bi + 1;
        }
        AlignRec(a, pa, ahi, b, pb, bhi, outp);
    }
}

/// <summary>
/// 部署期的「基底 + patch」合并器：基底不是原版时，patch 目标若与基底改动重叠就做三方合并。
/// 官方基线源码从 backup/<chapter>_windows/data.win 反编译（只在真的需要时加载一次）。
/// </summary>
public sealed class BasePatchMerger
{
    private readonly UndertaleData _cur;
    private readonly UndertaleData _van;
    private readonly GlobalDecompileContext _gCur;
    private readonly GlobalDecompileContext _gVan;
    private readonly System.Collections.Generic.HashSet<string> _baseModified;
    //   ★ 链式合并（2026-09-27 修）：patch 是 QueueReplace **排队**写入的，队列里的文本在 data.Code 里还看不到
    //     —— 于是第二个层来合并时，看到的 ours 仍是「第一个层之前」的旧文本 ⇒ 合并结果里没有前一个层的改动，
    //     最后写入队列的那个层把前面的全部吃掉（实例：chapter1 的 DEVICE_MENU_Step_0 被 60fps+dojo+pct 争用，
    //     产物里只剩 pct 的版本，dojo 的 dj_open() 菜单入口消失）。
    //     这里自己记一份「该目标当前会写成什么」，后续层以此作为 ours 链下去。
    private readonly System.Collections.Generic.Dictionary<string, string> _pending = new(System.StringComparer.Ordinal);
    private readonly System.Collections.Generic.Dictionary<string, string> _curCache = new(System.StringComparer.Ordinal);
    private readonly System.Collections.Generic.Dictionary<string, string> _vanCache = new(System.StringComparer.Ordinal);

    public int MergedObjects;
    public int Conflicts;
    public int Failed;
    public readonly System.Collections.Generic.List<string> Report = new();

    private BasePatchMerger(UndertaleData cur, UndertaleData van, System.Collections.Generic.HashSet<string> baseModified)
    {
        _cur = cur; _van = van; _baseModified = baseModified;
        _gCur = new GlobalDecompileContext(cur);
        _gVan = new GlobalDecompileContext(van);
    }

    /// <summary>基底非原版时创建；任何一步失败都返回 null（退回今天的整体覆盖行为）。
    /// ★ extraTargets：Neutraled 自己注入过代码的对象（ReservedPatchTargets）也要合并 —— 层整脚本覆盖
    ///   会把内置注入点冲掉（bossrush 的 obj_darkcontroller_Draw_0 / _Step_0 就是实例），而这类目标
    ///   不属于「基底改动过」的集合，所以单独传进来。</summary>
    public static BasePatchMerger? TryCreate(UndertaleData cur, string gameRoot, string chapter,
        System.Collections.Generic.HashSet<string>? baseModified,
        System.Collections.Generic.IEnumerable<string>? extraTargets = null)
    {
        var set = new System.Collections.Generic.HashSet<string>(
            baseModified ?? new System.Collections.Generic.HashSet<string>(), System.StringComparer.Ordinal);
        if (extraTargets != null) foreach (var t in extraTargets) set.Add(t);
        if (set.Count == 0) return null;
        try
        {
            var backup = Paths.BackupDataWin(gameRoot, chapter);
            if (!System.IO.File.Exists(backup)) { Paths.Log(L("  [合并] 缺少官方基线，跳过三方合并: ") + backup); return null; }
            var van = Injector.Load(backup);
            return new BasePatchMerger(cur, van, set);
        }
        catch (System.Exception ex)
        {
            Paths.Log(L("  [警告] 三方合并初始化失败（patch 将整体覆盖）: {0}", ex.Message));
            return null;
        }
    }

    /// <summary>返回真正要写入的源码（不能合并时原样返回 patch）。</summary>
    public string Merge(string target, string patchText)
    {
        if (!_baseModified.Contains(target)) return patchText;
        var van = _van.Code.ByName(target);
        if (van == null) return patchText;
        try
        {
            // ours：优先用「前一个层排队写入的文本」，否则用产物里的当前文本。
            string oursText;
            if (_pending.TryGetValue(target, out var prevText))
            {
                oursText = prevText;
            }
            else
            {
                var cur = _cur.Code.ByName(target);
                if (cur == null) return patchText;
                if (PreWriteRepairs.SameInstructions(van, cur))                    // 只是索引位移 → 原样覆盖
                {
                    _pending[target] = patchText;
                    return patchText;
                }
                if (_curCache.TryGetValue(target, out var cachedCur)) oursText = cachedCur;
                else { oursText = Decompile(_gCur, cur); _curCache[target] = oursText; }
            }
            string vanSrc;
            if (_vanCache.TryGetValue(target, out var cachedVan)) vanSrc = cachedVan;
            else { vanSrc = Decompile(_gVan, van); _vanCache[target] = vanSrc; }
            if (Norm(oursText) == Norm(vanSrc))                                 // 文本一致 → 无真实改动
            {
                _pending[target] = patchText;
                return patchText;
            }
            var (merged, conflicts, hunks) = PatchMerge.Merge3(vanSrc, oursText, patchText);
            _pending[target] = merged;
            MergedObjects++;
            Conflicts += conflicts;
            if (conflicts > 0)
            {
                Report.Add("  [冲突] " + target + L("：冲突 {0} 处 / 改动块 {1} 处（已保守保留基底版本）", conflicts, hunks));
                Paths.Log(L("    [合并冲突] {0}: {1} 处（保留基底版本）", target, conflicts));
            }
            else
            {
                Report.Add("  [合并] " + target + L("：改动块 {0} 处，无冲突", hunks));
            }
            return merged;
        }
        catch (System.Exception ex)
        {
            Failed++;
            Report.Add("  [失败] " + target + " ← " + ex.Message);
            return patchText;
        }
    }

    public void WriteSummary(string neutraledRoot, string chapter)
    {
        if (MergedObjects == 0 && Failed == 0) return;
        Paths.Log(L("  三方合并: {0} 个对象（冲突 {1} 处）/ 失败 {2} 个", MergedObjects, Conflicts, Failed));
        try
        {
            var dir = System.IO.Path.Combine(neutraledRoot, "cache");
            System.IO.Directory.CreateDirectory(dir);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("===== patch 三方合并报告（" + chapter + "）=====");
            sb.AppendLine("基底: 非官方基线（整包 mod 当基底）");
            sb.AppendLine("合并对象: " + MergedObjects + " 个 / 冲突: " + Conflicts + " 处 / 失败: " + Failed + " 个");
            sb.AppendLine("说明: base=官方基线源码, ours=基底源码, theirs=patch 源码；");
            sb.AppendLine("      两边改不同位置自动合并；同一位置冲突时保守保留 ours（基底）。");
            sb.AppendLine();
            foreach (var l in Report) sb.AppendLine(l);
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "merge-" + chapter + ".txt"), sb.ToString());
        }
        catch { }
    }

    private static string Decompile(GlobalDecompileContext gctx, UndertaleCode code)
    {
        var dctx = new DecompileContext(gctx, code, null!);
        return dctx.DecompileToString();
    }

    private static string Norm(string src)
    {
        var lines = src.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        return string.Join("\n", System.Linq.Enumerable.Select(lines, l => l.TrimEnd())).Trim();
    }
}
