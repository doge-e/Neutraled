using UndertaleModLib.Models;

namespace Neutraled.Builder;

/// <summary>事件类型名 ↔ EventType 的**唯一**映射表 + 代码条目命名约定。
/// 三处共用（以前各自 switch 过一份，容易分叉）：
///   1) Injector 2.6 段：按 mod.json 的 objects 声明新建对象并绑定事件代码；
///   2) LayerFromBase：把「新增对象/新事件」写成 mod.json 的 objects 声明；
///   3) --probe-object：打印对象的事件绑定用于取证/回归。
/// 命名约定来自 GameMaker 自己：gml_Object_&lt;对象名&gt;_&lt;事件名&gt;_&lt;子类型&gt;
/// （对象名可含下划线，所以解析必须**从右往左**切）。</summary>
public static class ObjectEvents
{
    /// <summary>事件名 → EventType。别名（BeginStep/EndStep/DrawGUI）只是同一 EventType 的不同子类型写法。</summary>
    private static readonly (string Name, EventType Type)[] Table =
    {
        ("Create", EventType.Create),
        ("Destroy", EventType.Destroy),
        ("Alarm", EventType.Alarm),
        ("Step", EventType.Step),
        ("BeginStep", EventType.Step),      // Step 子类型 1
        ("EndStep", EventType.Step),        // Step 子类型 2
        ("Draw", EventType.Draw),
        ("DrawGUI", EventType.Draw),        // Draw 子类型 64
        ("Collision", EventType.Collision),
        ("Keyboard", EventType.Keyboard),
        ("Mouse", EventType.Mouse),
        ("Other", EventType.Other),
        ("KeyPress", EventType.KeyPress),
        ("KeyRelease", EventType.KeyRelease),
        ("Trigger", EventType.Trigger),
        ("CleanUp", EventType.CleanUp),
        ("Gesture", EventType.Gesture),
        ("PreCreate", EventType.PreCreate),
    };

    /// <summary>支持的事件名（用于告警文本）。</summary>
    public static string Supported => string.Join("/", Table.Select(t => t.Name));

    /// <summary>事件名 → EventType（大小写不敏感）。未知名字返回 false（调用方必须响亮告警，不要猜）。</summary>
    public static bool TryParse(string? name, out EventType type)
    {
        type = default;
        if (string.IsNullOrWhiteSpace(name)) return false;
        foreach (var (n, t) in Table)
        {
            if (string.Equals(n, name.Trim(), StringComparison.OrdinalIgnoreCase)) { type = t; return true; }
        }
        return false;
    }

    /// <summary>EventType（+ 子类型）→ 事件名：打印/取证用（TryParse 的反向；别名取规范名）。</summary>
    public static string TypeName(EventType type, int subtype)
    {
        if (type == EventType.Step) return subtype == 1 ? "BeginStep" : subtype == 2 ? "EndStep" : "Step";
        if (type == EventType.Draw) return subtype == 64 ? "DrawGUI" : "Draw";
        foreach (var (n, t) in Table) if (t == type) return n;
        return type.ToString();
    }

    /// <summary>代码条目名（GameMaker 约定）。</summary>
    public static string CodeName(string objectName, string typeName, int subtype)
        => "gml_Object_" + objectName + "_" + typeName + "_" + subtype;

    /// <summary>
    /// gml_Object_&lt;对象&gt;_&lt;事件名&gt;_&lt;子类型&gt; → (对象名, 事件名, 子类型)。
    /// ★ 从右往左切：对象名自己带下划线（obj_dojo_act_projectile），从左切会把对象名切碎。
    /// 不是对象事件条目（例如 gml_Script_*）或事件名不认识 → false。</summary>
    public static bool TrySplitCodeName(string codeName, out string objectName, out string typeName, out int subtype)
    {
        objectName = ""; typeName = ""; subtype = 0;
        const string prefix = "gml_Object_";
        if (string.IsNullOrEmpty(codeName) || !codeName.StartsWith(prefix, StringComparison.Ordinal)) return false;

        var rest = codeName.Substring(prefix.Length);
        int i = rest.LastIndexOf('_');                       // 子类型前的下划线
        if (i <= 0 || i == rest.Length - 1) return false;
        if (!int.TryParse(rest.Substring(i + 1), out subtype)) return false;

        int j = rest.LastIndexOf('_', i - 1);                // 事件名前面的下划线
        int typeStart = j < 0 ? 0 : j + 1;
        typeName = rest.Substring(typeStart, i - typeStart);
        objectName = j < 0 ? "" : rest.Substring(0, j);
        if (objectName.Length == 0) return false;
        return TryParse(typeName, out _);
    }
}
