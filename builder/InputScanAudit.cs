using UndertaleModLib;
using UndertaleModLib.Decompiler;
using UndertaleModLib.Models;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>输入扫描对照审计：慢判据（反编译找 keyboard_check）vs 快判据（扫指令操作数里的函数引用）。</summary>
public static class InputScanAudit
{
    public static int Run(string winPath)
    {
        var data = Injector.Load(winPath);
        Console.WriteLine(L("审计: {0}  代码对象 {1}", Path.GetFileName(winPath), data.Code.Count));

        // A：慢（真值）
        var swA = System.Diagnostics.Stopwatch.StartNew();
        var gctx = new GlobalDecompileContext(data);
        var A = new HashSet<string>(StringComparer.Ordinal);
        foreach (var code in data.Code)
        {
            var nm = code.Name?.Content;
            if (string.IsNullOrEmpty(nm)) continue;
            string src;
            try { src = Injector.DecompileWith(gctx, data, nm); } catch { continue; }
            if (!string.IsNullOrEmpty(src) && src.Contains("keyboard_check")) A.Add(nm);
        }
        swA.Stop();

        // 先看清指令上到底有哪些"函数引用"字段
        var sample = data.Code.FirstOrDefault(c => A.Contains(c.Name?.Content ?? ""));
        if (sample != null)
        {
            Console.WriteLine(L("样本对象 {0} 的指令字段：", sample.Name?.Content));
            foreach (var instr in sample.Instructions.Take(3))
            {
                Console.WriteLine(L("  指令 ") + instr.Kind);
                foreach (var pr in instr.GetType().GetProperties())
                {
                    object? v = null;
                    try { v = pr.GetValue(instr); } catch { }
                    if (v == null) continue;
                    var t = pr.PropertyType.Name;
                    if (t.Contains("Function") || t.Contains("Reference") || pr.Name.Contains("Function"))
                        Console.WriteLine($"    {pr.Name} : {t} = {v}");
                }
            }
        }

        // B：快（扫指令操作数里的 UndertaleFunction 引用）
        var swB = System.Diagnostics.Stopwatch.StartNew();
        var B = new HashSet<string>(StringComparer.Ordinal);
        int _shown = 0;
        foreach (var code in data.Code)
        {
            var nm = code.Name?.Content;
            if (string.IsNullOrEmpty(nm)) continue;
            bool hit = false;
            foreach (var instr in code.Instructions)
            {
                foreach (var pr in instr.GetType().GetProperties())
                {
                    object? v;
                    try { v = pr.GetValue(instr); } catch { continue; }
                    if (v is UndertaleFunction uf)
                    {
                        var fn = uf.Name?.Content ?? "";
                        if (fn.StartsWith("keyboard_", StringComparison.Ordinal))
                        {
                            if (_shown++ < 5) Console.WriteLine(L("  [命中] {0}  属性 {1} ({2}) = {3}", instr.Kind, pr.Name, pr.PropertyType.Name, fn));
                            hit = true; break;
                        }
                    }
                    else if (v is UndertaleFunction[] arr)
                    {
                        if (arr.Any(x => (x?.Name?.Content ?? "").StartsWith("keyboard_", StringComparison.Ordinal))) { hit = true; break; }
                    }
                }
                if (hit) break;
            }
            if (hit) B.Add(nm);
        }
        swB.Stop();

        var missed = A.Except(B).ToList();
        var extra = B.Except(A).ToList();
        Console.WriteLine(L("A 慢（反编译文本）: {0} 个，{1:N1}s", A.Count, swA.Elapsed.TotalSeconds));
        Console.WriteLine(L("B 快（指令操作数）: {0} 个，{1:N2}s", B.Count, swB.Elapsed.TotalSeconds));
        Console.WriteLine(L("  漏掉: {0}", missed.Count) + (missed.Count == 0 ? L("  ✓ 无漏（快判据可用）") : L("  ✗ 有漏！")));
        Console.WriteLine(L("  多出: {0}（安全）", extra.Count));
        foreach (var m in missed.Take(8)) Console.WriteLine(L("    [漏] ") + m);
        return missed.Count == 0 && B.Count > 0 ? 0 : 1;
    }
}
