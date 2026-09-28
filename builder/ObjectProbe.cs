using System;
using System.Linq;
using UndertaleModLib;
using UndertaleModLib.Models;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>
/// --probe-object &lt;data.win&gt; &lt;对象名|*&gt;
/// 打印对象头部字段 + 事件表（事件名/子类型/代码条目/指令数），用于取证与回归：
///   · 转换前：看整包 mod 的 data.win 里到底有哪些新增对象/事件（LayerFromBase 的依据）；
///   · 部署后：看产物里对象是不是真建出来了、事件是不是真绑上了代码（空代码 = 真机 Code Error 的典型成因）。
/// 传 * 列出资源表计数 + 全部对象名与事件数；传 rooms 列出房间表（校验 room_goto 索引没被改动）。
/// 返回 0 = 一切正常；1 = 找不到对象；2 = 找到但有事件空/未绑定代码。
/// </summary>
public static class ObjectProbe
{
    public static int Dump(string dataWin, string objectName, int fuzzyLimit = 20)
    {
        var data = Injector.Load(dataWin);
        Console.WriteLine(L("data.win: {0}", dataWin));
        Console.WriteLine(L("对象总数: {0}", data.GameObjects.Count));

        if (objectName == "*")
        {
            Console.WriteLine(L("资源表: 对象 {0} / 精灵 {1} / 房间 {2} / 代码 {3} / 脚本 {4} / 声音 {5} / 背景 {6} / 字体 {7} / 路径 {8}",
                data.GameObjects.Count, data.Sprites.Count, data.Rooms.Count, data.Code.Count, data.Scripts.Count,
                data.Sounds.Count, data.Backgrounds.Count, data.Fonts.Count, data.Paths.Count));
            foreach (var o in data.GameObjects.OrderBy(o => o.Name?.Content, StringComparer.Ordinal))
                Console.WriteLine("  " + (o.Name?.Content ?? "?") + L("  事件 {0}", CountEvents(o)));
            return 0;
        }

        if (objectName.StartsWith("room:", StringComparison.OrdinalIgnoreCase))
        {
            var rn = objectName.Substring(5).Trim();
            var room = data.Rooms.FirstOrDefault(r => string.Equals(r.Name?.Content, rn, StringComparison.OrdinalIgnoreCase));
            if (room == null)
            {
                Console.WriteLine(L("找不到房间 {0}（总房间数 {1}）", rn, data.Rooms.Count));
                foreach (var r in data.Rooms.Where(r => (r.Name?.Content ?? "").Contains(rn, StringComparison.OrdinalIgnoreCase)).Take(fuzzyLimit))
                    Console.WriteLine("  " + r.Name?.Content);
                return 1;
            }
            Console.WriteLine(L("房间 {0}: {1} x {2}  速度 {3}  持久 {4}  标志 {5}", rn, room.Width, room.Height, room.Speed, room.Persistent, room.Flags));
            Console.WriteLine(L("  创建代码 {0}  视图 {1}  背景 {2}  实例 {3}  图块 {4}  层 {5}",
                room.CreationCodeId?.Name?.Content ?? "(无)", room.Views?.Count ?? 0, room.Backgrounds?.Count ?? 0,
                room.GameObjects?.Count ?? 0, room.Tiles?.Count ?? 0, room.Layers?.Count ?? 0));
            foreach (var g in room.GameObjects ?? new UndertalePointerList<UndertaleRoom.GameObject>())
                Console.WriteLine(L("  实例 {0} @ ({1},{2}) 缩放 ({3},{4}) 创建代码 {5}", g.ObjectDefinition?.Name?.Content ?? "?",
                    g.X, g.Y, g.ScaleX, g.ScaleY, g.CreationCode?.Name?.Content ?? "-"));
            Console.WriteLine(L("  背景色 {0} 画背景色 {1}  标志值 {2}", room.BackgroundColor, room.DrawBackgroundColor, (int)room.Flags));
            foreach (var v in room.Views ?? new UndertalePointerList<UndertaleRoom.View>())
                Console.WriteLine(L("  视图 启用 {0} 视口 ({1},{2}) {3}x{4} 视界 ({5},{6}) {7}x{8}", v.Enabled,
                    v.PortX, v.PortY, v.PortWidth, v.PortHeight, v.ViewX, v.ViewY, v.ViewWidth, v.ViewHeight));
            foreach (var b in room.Backgrounds ?? new UndertalePointerList<UndertaleRoom.Background>())
                Console.WriteLine(L("  背景 启用 {0} 前景 {1} 定义 {2} tiled ({3},{4})", b.Enabled, b.Foreground, b.BackgroundDefinition?.Name?.Content ?? "-", b.TiledHorizontally, b.TiledVertically));
            foreach (var l in room.Layers ?? new UndertalePointerList<UndertaleRoom.Layer>())
                Console.WriteLine(L("  层 {0} 类型 {1} id {2} 深度 {3}", l.LayerName?.Content ?? "?", l.LayerType, l.LayerId, l.LayerDepth));
            return 0;
        }

        if (objectName == "apicheck")
        {
            var t = data.GeneralInfo.RoomOrder.GetType();
            Console.WriteLine("类型: " + t.FullName);
            foreach (var m in t.GetMethods().Where(m => m.Name is "Add" or "Insert" && m.IsPublic))
                Console.WriteLine("  " + m.ReturnType.Name + " " + m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)) + ")");
            foreach (var c in typeof(UndertaleResourceById<UndertaleRoom, UndertaleChunkROOM>).GetConstructors())
                Console.WriteLine("  ctor UndertaleResourceById(" + string.Join(", ", c.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)) + ")");
            foreach (var tp in new[] { typeof(UndertaleRoom), typeof(UndertaleRoom.View), typeof(UndertaleRoom.Background), typeof(UndertaleRoom.GameObject) })
            {
                Console.WriteLine("== " + tp.Name + " 属性类型");
                foreach (var p in tp.GetProperties().OrderBy(p => p.Name))
                    Console.WriteLine("   " + p.PropertyType.Name + " " + p.Name);
            }
            var fresh = new UndertaleRoom();
            Console.WriteLine(L("  new UndertaleRoom(): 视图 {0} 背景 {1} 层 {2} 实例 {3} 图块 {4} 标志 {5} 尺寸 {6}x{7} 速度 {8}",
                fresh.Views?.Count ?? -1, fresh.Backgrounds?.Count ?? -1, fresh.Layers?.Count ?? -1,
                fresh.GameObjects?.Count ?? -1, fresh.Tiles?.Count ?? -1, (int)fresh.Flags, fresh.Width, fresh.Height, fresh.Speed));
            foreach (var ci in typeof(UndertaleRoom).GetConstructors())
                Console.WriteLine("  ctor UndertaleRoom(" + string.Join(", ", ci.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)) + ")");
            return 0;
        }

        if (objectName == "rooms")
        {
            // 房间顺序 = room_goto(n) 里的 n：层改不了房间，所以基底与 mod 两边的房间表必须逐项一致
            Console.WriteLine(L("RoomOrder: {0} 条  类型 {1}", data.GeneralInfo.RoomOrder.Count, data.GeneralInfo.RoomOrder.GetType().FullName));
            foreach (var e in data.GeneralInfo.RoomOrder.Skip(Math.Max(0, data.GeneralInfo.RoomOrder.Count - 3)))
                Console.WriteLine("  末位 " + (e.Resource?.Name?.Content ?? "?"));
            for (int i = 0; i < data.Rooms.Count; i++)
                Console.WriteLine(i.ToString().PadLeft(4) + "  " + (data.Rooms[i].Name?.Content ?? "?"));
            return 0;
        }

        var obj = data.GameObjects.ByName(objectName);
        if (obj == null)
        {
            Console.WriteLine(L("找不到对象 {0}", objectName));
            var near = data.GameObjects
                .Where(o => o.Name?.Content is string n &&
                            (n.Contains(objectName, StringComparison.OrdinalIgnoreCase) ||
                             objectName.Contains(n, StringComparison.OrdinalIgnoreCase)))
                .Take(fuzzyLimit).ToList();
            Console.WriteLine(near.Count == 0
                ? L("  没有名字相近的对象。")
                : L("  名字相近的对象 {0} 个：", near.Count));
            foreach (var n in near) Console.WriteLine("    " + n.Name!.Content);
            return 1;
        }

        int idx = -1;
        for (int i = 0; i < data.GameObjects.Count; i++)
            if (ReferenceEquals(data.GameObjects[i], obj)) { idx = i; break; }

        Console.WriteLine(L("对象 {0}（GameObjects 下标 {1}）", obj.Name?.Content ?? "?", idx));
        Console.WriteLine(L("  sprite   {0}", obj.Sprite?.Name?.Content ?? L("(无)")));
        Console.WriteLine(L("  parent   {0}", obj.ParentId?.Name?.Content ?? L("(无)")));
        Console.WriteLine(L("  mask     {0}", obj.TextureMaskId?.Name?.Content ?? L("(无)")));
        Console.WriteLine(L("  visible={0} solid={1} persistent={2} depth={3} managed={4}",
            obj.Visible, obj.Solid, obj.Persistent, obj.Depth, obj.Managed));

        int total = 0, empty = 0;
        for (int i = 0; i < obj.Events.Count; i++)
        {
            var inner = obj.Events[i];
            if (inner == null) continue;
            var type = (EventType)i;
            foreach (var ev in inner)
            {
                total++;
                var code = (ev.Actions != null && ev.Actions.Count > 0) ? ev.Actions[0].CodeId : null;
                int instr = code?.Instructions?.Count ?? 0;
                if (code == null || instr == 0) empty++;
                Console.WriteLine("  " + ObjectEvents.TypeName(type, (int)ev.EventSubtype).PadRight(11)
                    + "(" + ev.EventSubtype.ToString().PadLeft(3) + ")  "
                    + L("代码 {0}", code?.Name?.Content ?? L("(无)")).PadRight(8)
                    + L("指令 {0}", instr)
                    + (code == null ? L("   ← ★ 事件没绑代码") : instr == 0 ? L("   ← ★ 空代码") : ""));
            }
        }
        Console.WriteLine(L("事件合计: {0}（空/未绑定 {1}）", total, empty));
        return empty > 0 ? 2 : 0;
    }

    private static int CountEvents(UndertaleGameObject o)
    {
        int n = 0;
        for (int i = 0; i < o.Events.Count; i++)
            if (o.Events[i] != null) n += o.Events[i].Count;
        return n;
    }
}
