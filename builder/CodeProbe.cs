using UndertaleModLib;
using UndertaleModLib.Models;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>
/// 指令级取证工具 —— 打印某个代码对象每条指令的**真实字段类型与解析目标**。
///
/// 用途：判断 RefCopy 在跨文件复制代码对象时，哪些操作数必须做**索引重映射**。
/// 只看反汇编文本看不出来（ValueInt 里到底装的是"函数索引""内置变量索引"还是"字节码地址"，
/// 必须看运行时类型 + 解析出来的名字）。
/// </summary>
public static class CodeProbe
{
    public static int Dump(string winPath, string codeName, int maxInstructions = 30)
    {
        if (!File.Exists(winPath)) { Console.WriteLine(L("[错误] 找不到: ") + winPath); return 1; }
        var data = Injector.Load(winPath);
        var code = data.Code.ByName(codeName);
        if (code == null)
        {
            Console.WriteLine(L("[错误] 找不到代码对象: ") + codeName);
            Console.WriteLine(L("  提示：对象名形如 gml_GlobalScript_xxx / gml_Object_obj_xxx_Step_0"));
            var near = data.Code.Where(c => (c.Name?.Content ?? "").Contains(codeName, StringComparison.OrdinalIgnoreCase))
                                .Take(8).Select(c => c.Name!.Content!).ToList();
            if (near.Count > 0) Console.WriteLine(L("  相近的对象: ") + string.Join(", ", near));
            return 1;
        }

        Console.WriteLine(L("代码对象: {0}", codeName));
        Console.WriteLine(L("  指令 {0} / 参数 {1} / 局部 {2}", code.Instructions.Count, code.ArgumentsCount, code.LocalsCount));
        Console.WriteLine();

        int i = 0;
        foreach (var ins in code.Instructions.Take(maxInstructions))
        {
            Console.WriteLine($"  [{i}] Kind={ins.Kind} RefType={ins.ReferenceType} Type1={ins.Type1} Type2={ins.Type2} TypeInst={ins.TypeInst}");
            foreach (var p in ins.GetType().GetProperties())
            {
                if (!p.CanRead) continue;
                object? v;
                try { v = p.GetValue(ins); } catch { continue; }
                var extra = ResolveExtra(v);
                if (extra.Length > 0 || IsInteresting(p.Name, p.PropertyType.Name))
                    Console.WriteLine($"        {p.Name,-16} ({p.PropertyType.Name}) = {Describe(v)}{extra}");
            }
            i++;
        }
        if (code.Instructions.Count > maxInstructions)
            Console.WriteLine(L("  ...（共 {0} 条，只显示前 {1} 条）", code.Instructions.Count, maxInstructions));
        return 0;
    }

    /// <summary>微实验：确认 UndertaleInstruction 的 ValueInt / ValueLong / ValueDouble 三个 setter
    /// 到底哪个真正落盘（它们共享同一段 8 字节存储，赋值顺序/条件会影响结果）。
    /// 实测背景：跨文件复制 Push 指令时 ValueDouble(0.7) 丢失 → 目标里变成 denormal 垃圾。</summary>
    public static int SelfTest()
    {
        Console.WriteLine(L("  --- UndertaleInstruction 关键字段的真实类型 ---"));
        foreach (var pn in new[] { "Extra", "SwapExtra", "JumpOffset", "IntArgument", "ValueLong", "ValueDouble", "ReferenceType", "ExtendedKind" })
        {
            var p = typeof(UndertaleInstruction).GetProperty(pn);
            Console.WriteLine(L("    {0,-14} : {1}", pn, (p == null ? L("(无此属性)") : p.PropertyType.FullName)));
        }

        static void Show(string tag, UndertaleInstruction i) =>
            Console.WriteLine($"  {tag,-28} I={i.ValueInt}  L={i.ValueLong}  D={i.ValueDouble}");

        var a = new UndertaleInstruction();
        a.ValueInt = 1717986918; a.ValueLong = 4604480259023595110; a.ValueDouble = 0.7;
        Show(L("默认 Type1: I,L,D 顺序"), a);

        var b = new UndertaleInstruction();
        b.ValueDouble = 0.7;
        Show(L("默认 Type1: 只设 D"), b);

        var c = new UndertaleInstruction { Type1 = UndertaleInstruction.DataType.Double };
        c.ValueDouble = 0.7;
        Show(L("Type1=Double: 只设 D"), c);

        var d = new UndertaleInstruction { Type1 = UndertaleInstruction.DataType.Int32 };
        d.ValueDouble = 0.7;
        Show(L("Type1=Int32: 只设 D"), d);
        d.ValueLong = 4604480259023595110;
        Show(L("Type1=Int32: 再设 L"), d);

        var e = new UndertaleInstruction { Type1 = UndertaleInstruction.DataType.Int32 };
        e.ValueLong = 4604480259023595110;
        e.ValueInt = 1717986918;
        Show(L("Type1=Int32: L 后 I"), e);

        var f = new UndertaleInstruction { Type1 = UndertaleInstruction.DataType.Double };
        f.ValueLong = 4604480259023595110;
        Show(L("Type1=Double: 只设 L"), f);

        Console.WriteLine(L("  --- 复刻 CopyInstruction 的完整 initializer 顺序 ---"));
        var g = new UndertaleInstruction
        {
            Kind = UndertaleInstruction.Opcode.Push,
            Type1 = UndertaleInstruction.DataType.Int32,
            Type2 = UndertaleInstruction.DataType.Double,
            TypeInst = UndertaleInstruction.InstanceType.Undefined,
            ValueShort = 0,
            ValueInt = 1717986918,
            ValueLong = 4604480259023595110,
            ValueDouble = 0.7,
            ReferenceType = UndertaleInstruction.VariableType.Array,
            JumpOffset = 0,
            ArgumentsCount = 0
        };
        Show(L("完整(CopyInstruction 同序)"), g);

        var h = new UndertaleInstruction
        {
            Kind = UndertaleInstruction.Opcode.Push,
            Type1 = UndertaleInstruction.DataType.Int32,
            Type2 = UndertaleInstruction.DataType.Double,
            TypeInst = UndertaleInstruction.InstanceType.Undefined,
            ValueShort = 0,
            ValueInt = 1717986918,
            ValueLong = 4604480259023595110,
            ValueDouble = 0.7
        };
        Show(L("只到 ValueDouble 为止"), h);

        h.ReferenceType = UndertaleInstruction.VariableType.Array;
        Show(L("再加 ReferenceType"), h);
        h.JumpOffset = 0;
        Show(L("再加 JumpOffset"), h);
        h.ArgumentsCount = 0;
        Show(L("再加 ArgumentsCount"), h);
        return 0;
    }

    private static bool IsInteresting(string prop, string typeName) =>
        prop is "ValueInt" or "ValueShort" or "ValueLong" or "ValueDouble" or "TypeInst" or "Kind" or "ReferenceType"
        || typeName.Contains("Reference") || typeName.Contains("ResourceById");

    /// <summary>把引用型字段解析成可读目标（Reference&lt;T&gt;.Target / ResourceById.Resource）。</summary>
    private static string ResolveExtra(object? v)
    {
        if (v == null) return "";
        var t = v.GetType();
        foreach (var name in new[] { "Target", "Resource" })
        {
            var p = t.GetProperty(name);
            if (p == null) continue;
            try
            {
                var tg = p.GetValue(v);
                if (tg != null) return "  → " + name + ": " + Describe(tg);
            }
            catch { }
        }
        return "";
    }

    private static string Describe(object? o)
    {
        if (o == null) return "null";
        var t = o.GetType();
        if (t.IsEnum) return o.ToString()!;
        var nameProp = t.GetProperty("Name");
        if (nameProp != null)
        {
            try
            {
                var nm = nameProp.GetValue(o);
                if (nm != null)
                {
                    var content = nm.GetType().GetProperty("Content");
                    var s = content != null ? content.GetValue(nm) as string : nm.ToString();
                    if (!string.IsNullOrEmpty(s)) return s!;
                }
            }
            catch { }
        }
        var s2 = o.ToString();
        return s2 == t.FullName ? t.Name : (s2 ?? "");
    }
}
