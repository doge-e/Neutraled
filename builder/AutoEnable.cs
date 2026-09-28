using static Neutraled.Builder.Lang;
namespace Neutraled.Builder;

/// <summary>依赖自动启用 —— B 依赖 A 时，A 自动跟着启用。
///
/// 背景：玩家启用了 Frostveil，但它依赖 FrostveilCore，玩家忘了启用核心 mod
///       → 部署后一堆"找不到 mod"的报错。
///
/// 方案：部署前分析依赖图，把被依赖的 mod 自动加入启用列表（并提示玩家）。
/// </summary>
public static class AutoEnable
{
    public sealed class Result
    {
        public List<string> Added = new();          // 被自动启用的
        public List<string> Missing = new();        // 依赖了但根本没装的
        public List<string> Cycles = new();         // 循环依赖
    }

    public static Result Resolve(List<ModEntry> allMods, List<ModEntry> enabled)
    {
        var res = new Result();
        var byId = new Dictionary<string, ModEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in allMods)
            if (!byId.ContainsKey(m.Id)) byId[m.Id] = m;

        var enabledIds = new HashSet<string>(enabled.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);

        // 迭代直到不再新增（处理多层依赖）
        bool changed = true;
        int guard = 0;
        while (changed && guard < 20)
        {
            changed = false;
            guard++;

            foreach (var m in enabled.ToList())
            {
                foreach (var dep in m.Dependencies)
                {
                    // 解析 "id >= 1.0.0" 形式
                    var depId = dep.Split(' ')[0].Trim();
                    if (depId.Length == 0) continue;
                    if (enabledIds.Contains(depId)) continue;

                    if (byId.TryGetValue(depId, out var target))
                    {
                        // 自动启用
                        enabled.Add(target);
                        enabledIds.Add(depId);
                        res.Added.Add(L("{0}（被 {1} 依赖）", target.Name, m.Name));
                        changed = true;
                    }
                    else
                    {
                        res.Missing.Add(L("{0} 依赖 {1}（未安装）", m.Name, depId));
                    }
                }
            }
        }

        return res;
    }

    public static void PrintReport(Result r)
    {
        if (r.Added.Count > 0)
        {
            Paths.Log(L("  依赖自动启用: {0} 个", r.Added.Count));
            foreach (var a in r.Added) Paths.Log($"    + {a}");
        }
        if (r.Missing.Count > 0)
        {
            Paths.Log(L("  缺失依赖: {0} 个（这些 mod 可能无法正常工作）", r.Missing.Count));
            foreach (var m in r.Missing.Distinct().Take(10)) Paths.Log($"    ! {m}");
        }
        if (r.Cycles.Count > 0)
        {
            Paths.Log(L("  循环依赖: {0}", r.Cycles.Count));
            foreach (var c in r.Cycles) Paths.Log($"    ~ {c}");
        }
    }
}
