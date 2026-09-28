using UndertaleModLib;
using UndertaleModLib.Models;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>写盘前的三道必修（避免 UTMT 序列化/游戏加载失败）。</summary>
public static class PreWriteRepairs
{
    /// <summary>1) 去重 CodeLocals（同名重复会导致写盘 "same key" 断言）。</summary>
    public static int DedupeCodeLocals(UndertaleData data)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int removed = 0;
        for (int i = data.CodeLocals.Count - 1; i >= 0; i--)
        {
            var name = data.CodeLocals[i].Name?.Content;
            if (string.IsNullOrEmpty(name)) continue;
            if (!seen.Add(name)) { data.CodeLocals.RemoveAt(i); removed++; }
        }
        if (removed > 0) Paths.Log(L("  CodeLocals 去重: {0} 个", removed));
        return removed;
    }

    /// <summary>2) 重建被引用但 occ=0 / FirstAddress 为空的函数条目。</summary>
    public static int RebuildDeadFunctionReferences(UndertaleData data)
    {
        var refs = new Dictionary<UndertaleFunction, UndertaleCode>();
        foreach (var code in data.Code)
        {
            foreach (var ins in code.Instructions)
            {
                if (ins.ValueFunction != null && !refs.ContainsKey(ins.ValueFunction))
                    refs[ins.ValueFunction] = code;
            }
        }

        int fixedCount = 0;
        foreach (var fn in data.Functions)
        {
            if (fn.Occurrences > 0 && fn.FirstAddress != null) continue;
            if (string.IsNullOrEmpty(fn.Name?.Content)) continue;
            if (!refs.TryGetValue(fn, out var code)) continue;
            fn.Occurrences = Math.Max(fn.Occurrences, 1);
            if (fn.FirstAddress == null && code.Instructions.Count > 0)
                fn.FirstAddress = code.Instructions[0];
            fixedCount++;
        }
        if (fixedCount > 0) Paths.Log(L("  函数引用修复: {0} 个", fixedCount));
        return fixedCount;
    }

    /// <summary>3) 从引用源补齐"被目标代码引用但缺失"的 child 函数（GML patch 覆盖时可能丢失）。</summary>
    public static int RepairMissingChildFunctions(UndertaleData dst, IEnumerable<UndertaleData> refSources)
    {
        var missing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var code in dst.Code)
        {
            foreach (var ins in code.Instructions)
            {
                var fname = ins.ValueFunction?.Name?.Content;
                if (string.IsNullOrEmpty(fname)) continue;
                if (dst.Code.FirstOrDefault(c => c.Name?.Content == fname) == null)
                    missing.Add(fname);
            }
        }
        if (missing.Count == 0) return 0;

        int repaired = 0;
        foreach (var src in refSources)
        {
            foreach (var name in missing.ToList())
            {
                var srcCode = src.Code.FirstOrDefault(c => c.Name?.Content == name);
                if (srcCode == null) continue;

                var dstCode = UndertaleCode.CreateEmptyEntry(dst, name);
                dstCode.Instructions.Clear();
                dstCode.ArgumentsCount = srcCode.ArgumentsCount;
                dstCode.LocalsCount = srcCode.LocalsCount;
                foreach (var ins in srcCode.Instructions)
                {
                    // 同 RefCopy.CopyInstruction：元数据字段先设、值字段最后设
                    // （ReferenceType 的 setter 会重写值与 Long/Double 共享的 8 字节存储）
                    var ni = new UndertaleInstruction
                    {
                        Kind = ins.Kind, ComparisonKind = ins.ComparisonKind,
                        Type1 = ins.Type1, Type2 = ins.Type2, TypeInst = ins.TypeInst,
                        ReferenceType = ins.ReferenceType, JumpOffset = ins.JumpOffset,
                        ArgumentsCount = ins.ArgumentsCount, Extra = ins.Extra,
                        SwapExtra = ins.SwapExtra, ExtendedKind = ins.ExtendedKind,
                        IntArgument = ins.IntArgument,
                        ValueShort = ins.ValueShort, ValueInt = ins.ValueInt,
                        ValueLong = ins.ValueLong, ValueDouble = ins.ValueDouble
                    };
                    dstCode.Instructions.Add(ni);
                }

                // 父代码之后（Code 表相邻）
                if (srcCode.ParentEntry != null)
                {
                    var parentName = srcCode.ParentEntry.Name?.Content;
                    var dstParent = parentName != null ? dst.Code.FirstOrDefault(c => c.Name?.Content == parentName) : null;
                    if (dstParent != null)
                    {
                        dst.Code.Remove(dstCode);
                        int pIdx = dst.Code.IndexOf(dstParent);
                        if (pIdx >= 0) dst.Code.Insert(pIdx + 1, dstCode);
                        dstCode.ParentEntry = dstParent;
                    }
                }
                missing.Remove(name);
                repaired++;
            }
        }
        if (repaired > 0) Paths.Log(L("  缺失 child 函数补齐: {0} 个", repaired));
        return repaired;
    }

    /// <summary>诊断用：返回两条代码对象"第一个语义差异"的描述（含指令下标与字段名）。</summary>
    public static string? FirstDifference(UndertaleCode a, UndertaleCode b)
    {
        if (a.ArgumentsCount != b.ArgumentsCount) return $"ArgumentsCount {a.ArgumentsCount}->{b.ArgumentsCount}";
        if (a.LocalsCount != b.LocalsCount) return $"LocalsCount {a.LocalsCount}->{b.LocalsCount}";
        if (a.Instructions.Count != b.Instructions.Count) return L("指令数 {0}->{1}", a.Instructions.Count, b.Instructions.Count);
        for (int i = 0; i < a.Instructions.Count; i++)
        {
            var x = a.Instructions[i]; var y = b.Instructions[i];
            if (x.Kind != y.Kind) return $"[{i}] Kind {x.Kind}->{y.Kind}";
            if (x.ComparisonKind != y.ComparisonKind) return $"[{i}] Cmp {x.ComparisonKind}->{y.ComparisonKind}";
            if (x.Type1 != y.Type1) return $"[{i}] Type1 {x.Type1}->{y.Type1}";
            if (x.Type2 != y.Type2) return $"[{i}] Type2 {x.Type2}->{y.Type2}";
            if (x.TypeInst != y.TypeInst) return $"[{i}] TypeInst {x.TypeInst}->{y.TypeInst}";
            if (x.ValueShort != y.ValueShort) return $"[{i}] ValueShort {x.ValueShort}->{y.ValueShort}";
            if (x.ValueInt != y.ValueInt) return $"[{i}] ValueInt {x.ValueInt}->{y.ValueInt}";
            if (x.ValueLong != y.ValueLong) return $"[{i}] ValueLong {x.ValueLong}->{y.ValueLong}";
            if (!Nullable.Equals(x.ValueDouble, y.ValueDouble)) return $"[{i}] ValueDouble {x.ValueDouble}->{y.ValueDouble}";
            if (x.ReferenceType != y.ReferenceType) return $"[{i}] ReferenceType {x.ReferenceType}->{y.ReferenceType}";
            if (x.ArgumentsCount != y.ArgumentsCount) return $"[{i}] ArgumentsCount {x.ArgumentsCount}->{y.ArgumentsCount}";
            if (x.ExtendedKind != y.ExtendedKind) return $"[{i}] ExtendedKind {x.ExtendedKind}->{y.ExtendedKind}";
            var fx = x.ValueFunction?.Name?.Content ?? ""; var fy = y.ValueFunction?.Name?.Content ?? "";
            if (fx != fy) return $"[{i}] Function '{fx}'->'{fy}'";
            var vx = x.ValueVariable?.Name?.Content ?? ""; var vy = y.ValueVariable?.Name?.Content ?? "";
            if (vx != vy) return $"[{i}] Variable '{vx}'->'{vy}'";
            if ((int?)x.ValueVariable?.InstanceType != (int?)y.ValueVariable?.InstanceType) return $"[{i}] VarInstance";
            var sx = x.ValueString?.Resource?.Content ?? ""; var sy = y.ValueString?.Resource?.Content ?? "";
            if (sx != sy) return $"[{i}] String '{sx}'->'{sy}'";
        }
        return null;
    }

    /// <summary>**语义级**字节码比对：逐指令比较"这条指令在做什么"，而不是"它在文件里怎么编码"。
    ///
    /// 关键教训（实测数据说话）：Deltamod / GM3P 打包的 mod 会把整个 data.win 用 UTMT 重新序列化，
    /// 于是 **跳转链的编码形式会被规范化** —— 语义没变，字节变了。实测：60fps（只改帧率）
    /// 若按"逐字节字段"比对会报 6046 个对象"改动"，按语义比对只剩个位数。
    /// 把编码噪声当改动，会让差异层膨胀到上万个对象、部署变慢、还会误判 mod 冲突。
    ///
    /// 因此**忽略纯编码字段**：JumpOffset / Extra / SwapExtra / IntArgument（跳转链与多字操作数的编码形式，
    /// UTMT 写盘时会重新生成）。语义字段全部保留：Kind / Type1 / Type2 / ComparisonKind / TypeInst /
    /// ValueShort / ValueInt / ValueLong / ValueDouble / ReferenceType / ArgumentsCount /
    /// 函数引用 / 变量引用(名+InstanceType) / 字符串引用(内容)。</summary>
    public static bool SameCodeContent(UndertaleCode a, UndertaleCode b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.ArgumentsCount != b.ArgumentsCount || a.LocalsCount != b.LocalsCount) return false;
        return SameInstructions(a, b);
    }

    /// <summary>只比**指令流**，忽略 ArgumentsCount / LocalsCount 这类**元数据**。
    ///
    /// 为什么需要区分：GM3P/Deltamod 重打包会改动局部变量表，于是大量对象"看起来被改"，
    /// 但其实一条指令都没变（纯元数据差异，不影响行为）。
    /// 实测 60fps：按 SameCodeContent 有 6046 个对象"改动"，
    /// 其中绝大多数只差元数据；用指令流筛过后候选大幅缩小，源码级过滤才有意义。</summary>
    public static bool SameInstructions(UndertaleCode a, UndertaleCode b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.Instructions.Count != b.Instructions.Count) return false;
        for (int i = 0; i < a.Instructions.Count; i++)
        {
            var x = a.Instructions[i]; var y = b.Instructions[i];
            if (x.Kind != y.Kind || x.ComparisonKind != y.ComparisonKind) return false;
            if (x.Type1 != y.Type1 || x.Type2 != y.Type2 || x.TypeInst != y.TypeInst) return false;
            if (x.ValueShort != y.ValueShort || x.ValueInt != y.ValueInt || x.ValueLong != y.ValueLong) return false;
            if (!Nullable.Equals(x.ValueDouble, y.ValueDouble)) return false;
            if (x.ReferenceType != y.ReferenceType || x.ArgumentsCount != y.ArgumentsCount) return false;
            if (x.ExtendedKind != y.ExtendedKind) return false;
            if ((x.ValueFunction?.Name?.Content ?? "") != (y.ValueFunction?.Name?.Content ?? "")) return false;
            if ((x.ValueVariable?.Name?.Content ?? "") != (y.ValueVariable?.Name?.Content ?? "")) return false;
            if ((int?)x.ValueVariable?.InstanceType != (int?)y.ValueVariable?.InstanceType) return false;
            if ((x.ValueString?.Resource?.Content ?? "") != (y.ValueString?.Resource?.Content ?? "")) return false;
        }
        return true;
    }

    /// <summary>对比两个 data.win，返回"相对基线有改动"的代码对象名集合（含新增）。</summary>
    public static HashSet<string> DiffCodeObjects(UndertaleData baseline, UndertaleData modified)
    {
        var changed = new HashSet<string>(StringComparer.Ordinal);
        var byName = new Dictionary<string, UndertaleCode>(StringComparer.Ordinal);
        foreach (var c in baseline.Code)
        {
            var n = c.Name?.Content;
            if (!string.IsNullOrEmpty(n)) byName[n] = c;
        }
        foreach (var m in modified.Code)
        {
            var name = m.Name?.Content;
            if (string.IsNullOrEmpty(name)) continue;
            if (!byName.TryGetValue(name, out var b) || !SameCodeContent(b, m)) changed.Add(name);
        }
        return changed;
    }
}
