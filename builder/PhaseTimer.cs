using System.Diagnostics;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>
/// 部署分阶段计时（2026-09-27 加）。目的：整章重建 100~170 s，先量出「加载产物 / 基底改动集合 / 注入各段 / 写盘」
/// 各占多少，再决定优化点。开销可忽略（只读 Stopwatch），--deploy 全程开启，摘要走 Paths.Log。
/// </summary>
public static class PhaseTimer
{
    private static readonly Stopwatch Sw = new();
    private static readonly List<(string Label, double Ms)> Marks = new();
    private static long _last;

    /// <summary>开关：默认开。</summary>
    public static bool Enabled = true;

    /// <summary>一章的部署开始时调用（清空上一次的 mark）。</summary>
    public static void Reset()
    {
        if (!Enabled) return;
        Marks.Clear();
        Sw.Restart();
        _last = 0;
    }

    /// <summary>记一段：自上一个 mark（或 Reset）以来的耗时，标签写**刚结束的那一段**。</summary>
    public static void Mark(string label)
    {
        if (!Enabled) return;
        var now = Sw.ElapsedMilliseconds;
        Marks.Add((label, now - _last));
        _last = now;
    }

    /// <summary>打印本段汇总：每段秒数 + 占比 + 合计。</summary>
    public static void Summary(string scope)
    {
        if (!Enabled || Marks.Count == 0) return;
        var total = Sw.ElapsedMilliseconds;
        Paths.Log(L("  [计时] {0} 合计 {1} s", scope, (total / 1000.0).ToString("F1")));
        foreach (var m in Marks)
        {
            var sec = (m.Ms / 1000.0).ToString("F1");
            var pct = (total > 0 ? m.Ms * 100.0 / total : 0).ToString("F0");
            Paths.Log(L("    {0} — {1} s（{2}%）", m.Label, sec, pct));
        }
    }
}
