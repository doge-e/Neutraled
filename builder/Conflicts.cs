using static Neutraled.Builder.Lang;
namespace Neutraled.Builder;

/// <summary>mod 冲突分析与友好报告。
///
/// 目标：让玩家和新手开发者**看得懂**冲突，而不是从日志里猜。
///
/// 三类情况：
///   ✅ 可共存 —— 两个 mod 用 Lua hook 改同一函数（NTL 的核心优势）
///   ⚠️ 覆盖   —— 都声明了 override（后者胜出）
///   ❌ 冲突   —— 都用传统 patch 改同一函数（后者覆盖前者，前者的修改丢失）
/// </summary>
public static class Conflicts
{
    public enum Level { Ok, Warn, Error }

    public sealed class Finding
    {
        public Level Level;
        public string Target = "";        // 函数/对象名
        public string Detail = "";
        public List<string> Mods = new();
    }

    public sealed class Report
    {
        public List<Finding> Findings = new();
        public int OkCount => Findings.Count(f => f.Level == Level.Ok);
        public int WarnCount => Findings.Count(f => f.Level == Level.Warn);
        public int ErrorCount => Findings.Count(f => f.Level == Level.Error);
    }

    public static Report Analyze(List<ModEntry> mods)
    {
        var report = new Report();

        // ---------- 1) 收集 hook 声明（按目标函数分组）----------
        var hookByTarget = new Dictionary<string, List<(string mod, string mode)>>(StringComparer.Ordinal);
        List<Hooks.HookDecl> decls;
        try { decls = Hooks.Collect(mods); } catch { decls = new List<Hooks.HookDecl>(); }
        var modNameById = mods.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.Ordinal);
        foreach (var h in decls)
        {
            if (string.IsNullOrWhiteSpace(h.Script)) continue;
            var label = modNameById.TryGetValue(h.ModId, out var nm) ? nm : h.ModId;
            if (!hookByTarget.TryGetValue(h.Script, out var list))
                hookByTarget[h.Script] = list = new List<(string, string)>();
            list.Add((label, h.Mode ?? "pre"));
        }

        foreach (var (target, list) in hookByTarget)
        {
            if (list.Count < 2) continue;
            var overrides = list.Where(x => x.mode == "override").ToList();
            var f = new Finding { Target = target, Mods = list.Select(x => x.mod).Distinct().ToList() };

            if (overrides.Count > 1)
            {
                f.Level = Level.Warn;
                f.Detail = L("有 {0} 个 mod 声明了 override —— 只有第一个会生效，", overrides.Count) +
                           L("后面的被跳过。\n          受影响: ") + string.Join(", ", overrides.Select(x => $"{x.mod}")) +
                           L("\n          → 建议：只保留一个 override，其余改成 pre/post");
            }
            else
            {
                f.Level = Level.Ok;
                var modes = string.Join(" + ", list.Select(x => $"{x.mod}({x.mode})"));
                f.Detail = L("{0} 个 mod 都通过 Lua hook 修改它 → **可以共存**（依次执行）\n          {1}", list.Count, modes);
            }
            report.Findings.Add(f);
        }

        // ---------- 1.5) 整包基底互斥：一章只能有一个 data.win 基底 ----------
        var inheritMods = mods.Where(m => string.Equals(m.RefAssets, "inherit", StringComparison.OrdinalIgnoreCase)
                                       && !string.IsNullOrEmpty(m.RefSource)).ToList();
        if (inheritMods.Count > 1)
        {
            report.Findings.Add(new Finding
            {
                Level = Level.Error,
                Target = L("整包 data.win 基底"),
                Mods = inheritMods.Select(m => m.Name).ToList(),
                Detail = L("{0} 个 mod 都提供**整包 data.win**，但一章只能有一个基底\n          ", inheritMods.Count) +
                         string.Join(", ", inheritMods.Select(m => m.Name)) +
                         L("\n          → 实际结果：只有第一个生效，其余被静默忽略（它们的脚本/资源/覆盖文件仍会生效）\n") +
                         L("          → 解决：用 --base-mod <id> 明确指定基底；其余改用 scripts/hooks/资源包/files 覆盖叠加")
            });
        }

        // ---------- 2) 收集 references（传统 patch 路径）----------
        // 同一对象被多个 mod 引用复制 = 潜在冲突
        var refByTarget = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var m in mods)
        {
            if (m.References == null) continue;
            var overrideSet = new HashSet<string>(m.References.Override ?? new List<string>(), StringComparer.Ordinal);
            foreach (var c in m.References.Codes ?? new List<string>())
            {
                if (!refByTarget.TryGetValue(c, out var list))
                    refByTarget[c] = list = new List<string>();
                list.Add(m.Name + (overrideSet.Contains(c) ? "(override)" : ""));
            }
        }

        foreach (var (target, list) in refByTarget)
        {
            if (list.Count < 2) continue;
            var f = new Finding { Target = target, Mods = list.Distinct().ToList() };
            bool anyOverride = list.Any(x => x.EndsWith("(override)"));
            if (anyOverride)
            {
                f.Level = Level.Warn;
                f.Detail = L("多个 mod 引用同一对象，其中声明了 override → 后者覆盖前者\n          ") +
                           string.Join(", ", list) +
                           L("\n          → 如果它们的功能不冲突，建议改用 Lua hook（可共存）");
            }
            else
            {
                f.Level = Level.Error;
                f.Detail = L("**代码冲突**：{0} 个 mod 都要替换 '{1}'，但都没声明 override\n          ", list.Count, target) +
                           string.Join(", ", list) +
                           L("\n          → 实际结果：**只有第一个 mod 的修改生效，其余被静默丢弃**\n          ") +
                           L("→ 解决方案（推荐顺序）：\n") +
                           L("             1) 把其中一个改成 Lua hook（mode: pre/post）—— 可共存\n") +
                           L("             2) 在一个 mod 里声明 references.override 明确覆盖意图\n") +
                           L("             3) 禁用其中一个 mod");
            }
            report.Findings.Add(f);
        }

        // ---------- 3) patches（整脚本覆盖）冲突 —— 这条以前是**静默**的 ----------
        // Injector 对每个 mod 直接 QueueReplace(target)，后加载者覆盖先加载者，没有任何提示。
        // 源码级差异层（--layer-from-base）产出的正是 patches，所以这条检查对共存至关重要。
        var patchByTarget = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var m in mods)
            foreach (var p in m.Patches)
            {
                if (string.IsNullOrWhiteSpace(p.Target)) continue;
                if (m.PatchSkip.Contains(p.Target)) continue;      // 显式跳过的不算冲突
                if (!patchByTarget.TryGetValue(p.Target, out var l)) patchByTarget[p.Target] = l = new List<string>();
                l.Add(m.Name);
            }

        foreach (var (target, list) in patchByTarget)
        {
            if (list.Count < 2) continue;
            report.Findings.Add(new Finding
            {
                Level = Level.Error,
                Target = target,
                Mods = list.Distinct().ToList(),
                Detail = L("**patch 冲突**：{0} 个 mod 都整脚本覆盖 '{1}'，而 patch **没有链式**\n          ", list.Count, target) +
                         string.Join(", ", list) +
                         L("\n          → 实际结果：**只有最后一个生效，其余被静默丢弃**\n") +
                         L("          → 兜底方案（推荐顺序）：\n") +
                         L("             1) 把其中一个改成 Lua hook（mode: pre/post）—— 可共存、且能调用原实现 ntl_orig_") + target + "\n" +
                         L("             2) 给被丢弃的那个加 patch_skip，让意图显式（至少不再是静默丢弃）\n") +
                         L("             3) 禁用其中一个 mod")
            });
        }

        // ---------- 4) patch 与 hook 打在同一目标上（顺序敏感）----------
        foreach (var (target, list) in patchByTarget)
        {
            if (!hookByTarget.TryGetValue(target, out var hooked) || hooked.Count == 0) continue;
            report.Findings.Add(new Finding
            {
                Level = Level.Warn,
                Target = target,
                Mods = list.Concat(hooked.Select(x => x.mod)).Distinct().ToList(),
                Detail = L("'{0}' 既被整脚本覆盖、又被 hook 包装 —— hook 会跑在**覆盖后的**版本上\n          ", target) +
                         L("          → 若 hook 里假设的是原版行为，结果会不对；建议二选一")
            });
        }

        return report;
    }

    public static void PrintReport(Report r)
    {
        if (r.Findings.Count == 0)
        {
            Paths.Log(L("  冲突分析: 未发现 mod 之间的冲突"));
            return;
        }

        Paths.Log(L("  冲突分析: {0} 个冲突 / {1} 个覆盖 / {2} 个可共存", r.ErrorCount, r.WarnCount, r.OkCount));

        foreach (var f in r.Findings.Where(x => x.Level == Level.Error))
            Paths.Log(L("\n  [冲突] {0}\n          {1}", f.Target, f.Detail));
        foreach (var f in r.Findings.Where(x => x.Level == Level.Warn))
            Paths.Log(L("\n  [覆盖] {0}\n          {1}", f.Target, f.Detail));
        foreach (var f in r.Findings.Where(x => x.Level == Level.Ok))
            Paths.Log(L("\n  [共存] {0}\n          {1}", f.Target, f.Detail));
    }

    /// <summary>把报告写成 JSON 供 GUI 读取</summary>
    public static void WriteJson(Report r, string path)
    {
        var obj = new System.Text.Json.Nodes.JsonObject
        {
            ["summary"] = new System.Text.Json.Nodes.JsonObject
            {
                ["errors"] = r.ErrorCount,
                ["warnings"] = r.WarnCount,
                ["ok"] = r.OkCount
            },
            ["findings"] = new System.Text.Json.Nodes.JsonArray(
                r.Findings.Select(f => (System.Text.Json.Nodes.JsonNode)new System.Text.Json.Nodes.JsonObject
                {
                    ["level"] = f.Level.ToString().ToLowerInvariant(),
                    ["target"] = f.Target,
                    ["detail"] = f.Detail,
                    ["mods"] = new System.Text.Json.Nodes.JsonArray(
                        f.Mods.Select(m => (System.Text.Json.Nodes.JsonNode)System.Text.Json.Nodes.JsonValue.Create(m)).ToArray())
                }).ToArray())
        };
        try { File.WriteAllText(path, obj.ToJsonString(new System.Text.Json.JsonSerializerOptions(Paths.Json) { WriteIndented = true })); }
        catch { }
    }
}
