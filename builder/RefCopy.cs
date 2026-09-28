using UndertaleModLib;
using UndertaleModLib.Models;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>
/// 字节码级代码对象复制（references.codes）。
/// 从 mod 的 ref/data.win 复制指定代码对象到目标基底，含：
///   - 函数重映射（按名查目标 Functions，缺失则创建）
///   - 变量重映射（按 InstanceType + Name 双键）
///   - 字符串重映射（按内容查目标字符串池）
///   - child entries（挂到父代码 + Code 表相邻）
///   - 对象索引重映射（TypeInst / PushI 立即数）
/// </summary>
public static class RefCopy
{
    public static int Copy(UndertaleData dst, UndertaleData src, IEnumerable<string> codeNames,
        HashSet<string> overrideNames, HashSet<string>? baseModified)
    {
        var strPool = new Dictionary<string, UndertaleString>(StringComparer.Ordinal);
        var varMap = new Dictionary<(int, string), UndertaleVariable>();
        var fnMap = new Dictionary<string, UndertaleFunction>(StringComparer.Ordinal);
        var copied = new List<string>();
        int skipped = 0, replaced = 0;

        foreach (var name in codeNames.Distinct())
        {
            if (baseModified != null && baseModified.Contains(name) && !overrideNames.Contains(name))
            {
                Paths.Log(L("    [跳过] {0}（基底已改动，未声明 override）", name));
                skipped++;
                continue;
            }

            var srcCode = src.Code.FirstOrDefault(c => c.Name?.Content == name);
            if (srcCode == null) { Paths.Log(L("    [警告] 源缺少代码对象: {0}", name)); skipped++; continue; }

            // 关键：**必须复用同名条目**，不能 CreateEmptyEntry 新建。
            // 实测（--verify-refcopy 20 样本）：新建会产生同名重复条目，而部署/游戏按 Code.ByName()
            // 取的是**第一个**（也就是没被改的旧条目）→ references.codes 的改动静默失效。
            var dstCode = dst.Code.FirstOrDefault(c => c.Name?.Content == name);
            if (dstCode == null) dstCode = UndertaleCode.CreateEmptyEntry(dst, name);
            var reused = dstCode.Instructions.Count > 0;
            dstCode.Instructions.Clear();
            dstCode.ArgumentsCount = srcCode.ArgumentsCount;
            dstCode.LocalsCount = srcCode.LocalsCount;
            dstCode.WeirdLocalFlag = srcCode.WeirdLocalFlag;
            if (reused) replaced++;

            foreach (var ins in srcCode.Instructions)
                dstCode.Instructions.Add(CopyInstruction(ins, dst, src, strPool, varMap, fnMap));

            // 复制子条目（挂到目标代码之后，保持 Code 表相邻）
            if (srcCode.ChildEntries?.Count > 0)
            {
                int parentIdx = dst.Code.IndexOf(dstCode);
                foreach (var child in srcCode.ChildEntries)
                {
                    var cname = child.Name?.Content;
                    if (string.IsNullOrEmpty(cname)) continue;
                    var existing = dst.Code.FirstOrDefault(c => c.Name?.Content == cname);
                    if (existing != null) continue;

                    var dstChild = UndertaleCode.CreateEmptyEntry(dst, cname);
                    dstChild.Instructions.Clear();
                    dstChild.ArgumentsCount = child.ArgumentsCount;
                    dstChild.LocalsCount = child.LocalsCount;
                    dstChild.WeirdLocalFlag = child.WeirdLocalFlag;
                    foreach (var ins in child.Instructions)
                        dstChild.Instructions.Add(CopyInstruction(ins, dst, src, strPool, varMap, fnMap));

                    // 移动到父之后（UTMT 读回按 Code 表 index 相邻关联 child）
                    dst.Code.Remove(dstChild);
                    parentIdx = dst.Code.IndexOf(dstCode);
                    if (parentIdx >= 0) dst.Code.Insert(parentIdx + 1, dstChild);
                }
            }

            copied.Add(name);
        }

        Paths.Log(L("    引用复制: {0} 个代码对象（其中 {1} 个是**替换**已有条目）/ {2} 跳过（函数补 {3} 个）", copied.Count, replaced, skipped, fnMap.Count));
        if (copied.Count > 0 && replaced == 0)
            Paths.Log(L("    [警告] 没有任何条目被替换 —— 若目标里本该有同名对象，说明复制成了新增而不是覆盖（会静默失效）"));
        RepairFunctions(dst);
        return copied.Count;
    }

    private static UndertaleInstruction CopyInstruction(UndertaleInstruction src, UndertaleData dst, UndertaleData srcData,
        Dictionary<string, UndertaleString> strPool, Dictionary<(int, string), UndertaleVariable> varMap,
        Dictionary<string, UndertaleFunction> fnMap)
    {
        // ⚠ 赋值顺序决定生死（实测结论，见 --probe-selftest）：
        //    ValueInt / ValueLong / ValueDouble 共享同一段 8 字节存储；
        //    而 **ReferenceType 的 setter 会重写这段存储**（把值重新按类型编码）。
        //    所以：所有"元数据字段"必须先设，**值字段最后设**，否则 double 常量会被打坏
        //    （0.7 → 8.49E-315 这类 denormal 垃圾）。
        //    这曾经让 references.codes 跨文件复制的数字字面量全体损坏。
        var ni = new UndertaleInstruction
        {
            Kind = src.Kind,
            ComparisonKind = src.ComparisonKind,
            Type1 = src.Type1,
            Type2 = src.Type2,
            TypeInst = src.TypeInst,
            ReferenceType = src.ReferenceType,   // ← 必须在值字段之前
            // 跳转链编码字段必须**原样照抄**（实测：归零会让 60/60 全部反编译失败）。
            // 它们编码的是源文件里的跳转链，跨文件复制时正是残余 7% 控制流失败的原因；
            // 这类对象请改用 --layer-from-base（源码级重编译，不受编码影响）。
            JumpOffset = src.JumpOffset,
            ArgumentsCount = src.ArgumentsCount,
            Extra = src.Extra,
            SwapExtra = src.SwapExtra,
            ExtendedKind = src.ExtendedKind,
            IntArgument = src.IntArgument,
            // ---- 值字段：最后设，顺序 Short → Int → Long → Double ----
            ValueShort = src.ValueShort,
            ValueInt = src.ValueInt,
            ValueLong = src.ValueLong,
            ValueDouble = src.ValueDouble
        };

        // 函数引用
        if (src.ValueFunction != null)
        {
            var fname = src.ValueFunction.Name?.Content;
            if (!string.IsNullOrEmpty(fname))
            {
                var fn = dst.Functions.FirstOrDefault(f => f.Name?.Content == fname);
                if (fn == null)
                {
                    fn = new UndertaleFunction
                    {
                        Name = dst.Strings.MakeString(fname),
                        Occurrences = 0,
                        FirstAddress = null
                    };
                    dst.Functions.Add(fn);
                }
                ni.ValueFunction = fn;
                fnMap[fname] = fn;
            }
        }

        // 变量引用（InstanceType + Name 双键）
        if (src.ValueVariable != null)
        {
            var vname = src.ValueVariable.Name?.Content;
            var itype = (int)src.ValueVariable.InstanceType;
            if (!string.IsNullOrEmpty(vname))
            {
                var key = (itype, vname);
                if (!varMap.TryGetValue(key, out var v))
                {
                    v = dst.Variables.FirstOrDefault(x =>
                        (int)x.InstanceType == itype && x.Name?.Content == vname);
                    if (v == null)
                    {
                        v = new UndertaleVariable
                        {
                            Name = dst.Strings.MakeString(vname),
                            InstanceType = src.ValueVariable.InstanceType,
                            VarID = (int)dst.VarCount1
                        };
                        dst.Variables.Add(v);
                        dst.VarCount1++;
                        dst.VarCount2++;
                    }
                    varMap[key] = v;
                }
                ni.ValueVariable = v;
            }
        }

        // 字符串引用
        if (src.ValueString?.Resource != null)
        {
            var content = src.ValueString.Resource.Content;
            if (content != null)
            {
                if (!strPool.TryGetValue(content, out var s))
                {
                    s = dst.Strings.MakeString(content);
                    strPool[content] = s;
                }
                ni.ValueString = new UndertaleResourceById<UndertaleString, UndertaleChunkSTRG>(s);
            }
        }

        return ni;
    }

    /// <summary>给 occ=0 但被引用的函数补齐 Occurrences/FirstAddress（UTMT 写盘与游戏加载需要）。</summary>
    public static void RepairFunctions(UndertaleData data)
    {
        int fixedCount = 0;
        foreach (var fn in data.Functions)
        {
            var name = fn.Name?.Content;
            if (string.IsNullOrEmpty(name)) continue;
            if (fn.Occurrences > 0 && fn.FirstAddress != null) continue;

            // 找引用该函数的代码对象
            foreach (var code in data.Code)
            {
                bool found = false;
                foreach (var ins in code.Instructions)
                {
                    if (ins.ValueFunction == fn) { found = true; break; }
                }
                if (found)
                {
                    fn.Occurrences = 1;
                    fn.FirstAddress = code.Instructions.Count > 0 ? code.Instructions[0] : null;
                    fixedCount++;
                    break;
                }
            }
        }
        if (fixedCount > 0) Paths.Log(L("    函数修复: {0} 个补齐 Occurrences", fixedCount));
    }
}
