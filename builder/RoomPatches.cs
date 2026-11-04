using UndertaleModLib;
using UndertaleModLib.Models;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>
/// 应用 mod.json 的 room_patches 段（见 <see cref="ModRoomPatch"/>）：在「新增房间」(2.7) 之后、
/// 资源名守卫 (2.8) 之前，把地图编辑器保存的「相对原版基线的房间差分」注入产物。
/// 设计原则：只改差异、按名字/ID 定位、找不到就响亮警告（不静默吞掉）。
/// </summary>
public static class RoomPatches
{
    public static void Apply(UndertaleData data, IReadOnlyList<ModEntry> mods)
    {
        int total = 0, changed = 0, bad = 0;
        foreach (var m in mods)
        {
            if (m.RoomPatches == null || m.RoomPatches.Count == 0) continue;
            Paths.Log(L("  房间差分: {0}（{1} 条声明）", m.Id, m.RoomPatches.Count));
            foreach (var p in m.RoomPatches)
            {
                total++;
                if (string.IsNullOrWhiteSpace(p.Name))
                {
                    Paths.Log(L("    [警告] {0}: room_patches 条目缺 name，跳过", m.Id));
                    bad++;
                    continue;
                }
                var room = data.Rooms.FirstOrDefault(r => r != null &&
                    string.Equals(r.Name?.Content, p.Name, StringComparison.Ordinal));
                if (room == null)
                {
                    Paths.Log(L("    [警告] {0}: 房间差分目标不存在: {1}（整条跳过）", m.Id, p.Name));
                    bad++;
                    continue;
                }
                int n = ApplyOne(data, m, room, p);
                changed += n;
                if (n > 0) Paths.Log(L("    ~房间 {0}: {1} 处改动", p.Name, n));
            }
        }
        if (total > 0)
            Paths.Log(L("  房间差分合计: 声明 {0} 条 / 变更 {1} 处 / 异常 {2}", total, changed, bad));
    }

    private static int ApplyOne(UndertaleData data, ModEntry m, UndertaleRoom room, ModRoomPatch p)
    {
        int n = 0;

        // ---- 房间头字段 ----
        if (p.Width.HasValue && room.Width != p.Width.Value) { room.Width = p.Width.Value; n++; }
        if (p.Height.HasValue && room.Height != p.Height.Value) { room.Height = p.Height.Value; n++; }
        if (p.Speed.HasValue && room.Speed != p.Speed.Value) { room.Speed = p.Speed.Value; n++; }
        if (p.Persistent.HasValue && room.Persistent != p.Persistent.Value) { room.Persistent = p.Persistent.Value; n++; }
        if (p.Flags.HasValue && (int)room.Flags != p.Flags.Value) { room.Flags = (UndertaleRoom.RoomEntryFlags)p.Flags.Value; n++; }
        if (p.BackgroundColor.HasValue)
        {
            uint v = unchecked((uint)p.BackgroundColor.Value);
            if (room.BackgroundColor != v) { room.BackgroundColor = v; n++; }
        }
        if (p.DrawBackgroundColor.HasValue && room.DrawBackgroundColor != p.DrawBackgroundColor.Value)
        {
            room.DrawBackgroundColor = p.DrawBackgroundColor.Value; n++;
        }

        // ---- 删除实例 ----
        foreach (var rf in p.InstancesRemove)
        {
            var inst = FindInstance(room, rf.InstanceId);
            if (inst == null)
            {
                Paths.Log(L("    [警告] {0}: 要删的实例不存在于 {1}: {2}", m.Id, p.Name, rf.InstanceId));
                continue;
            }
            RemoveInstance(room, inst);
            n++;
        }

        // ---- 修改实例 ----
        foreach (var mi in p.InstancesModify)
        {
            var inst = FindInstance(room, mi.InstanceId);
            if (inst == null)
            {
                Paths.Log(L("    [警告] {0}: 要改的实例不存在于 {1}: {2}", m.Id, p.Name, mi.InstanceId));
                continue;
            }
            n += ApplyInstanceFields(data, m, room, inst, mi);
        }

        // ---- 新增实例 ----
        foreach (var ai in p.InstancesAdd)
        {
            if (string.IsNullOrWhiteSpace(ai.Object))
            {
                Paths.Log(L("    [警告] {0}: 新增实例缺 object（房间 {1}）", m.Id, p.Name));
                continue;
            }
            var obj = data.GameObjects.ByName(ai.Object!);
            if (obj == null)
            {
                Paths.Log(L("    [警告] {0}: 新增实例的对象不存在: {1}（房间 {2}）", m.Id, ai.Object, p.Name));
                continue;
            }
            var layer = string.IsNullOrWhiteSpace(ai.Layer) ? FirstInstancesLayer(room) : FindInstancesLayer(room, ai.Layer!);
            if (layer?.InstancesData?.Instances == null)
            {
                Paths.Log(L("    [警告] {0}: 房间 {1} 没有可用的实例图层（新增实例跳过）", m.Id, p.Name));
                continue;
            }
            uint id = ai.InstanceId != 0 && !InstanceIdUsed(data, ai.InstanceId) ? ai.InstanceId : NextInstanceId(data);
            var inst = new UndertaleRoom.GameObject
            {
                ObjectDefinition = obj,
                X = ai.X ?? 0,
                Y = ai.Y ?? 0,
                InstanceID = id,
                ScaleX = ai.ScaleX ?? 1f,
                ScaleY = ai.ScaleY ?? 1f,
                Rotation = ai.Rotation ?? 0f,
                ImageIndex = ai.ImageIndex ?? 0,
                ImageSpeed = ai.ImageSpeed ?? 0f,
                Color = ai.Color ?? 0xFFFFFFFF,
                Nonexistent = ai.Nonexistent ?? false,
            };
            layer.InstancesData.Instances.Add(inst);
            if (room.GameObjects != null) room.GameObjects.Add(inst);
            if (room.InstanceCreationOrderIDs?.InstanceIDs != null)
                room.InstanceCreationOrderIDs.InstanceIDs.Add((int)inst.InstanceID);
            n++;
        }

        // ---- 原生瓦片 ----
        foreach (var ts in p.TilesSet)
        {
            UndertaleRoom.Layer? layer = null;
            if (!string.IsNullOrWhiteSpace(ts.Layer))
                layer = FindLayer(room, ts.Layer!);
            else
                layer = room.Layers?.FirstOrDefault(l => l.TilesData?.TileData != null);
            if (layer?.TilesData?.TileData == null)
            {
                Paths.Log(L("    [警告] {0}: 房间里找不到原生瓦片层: {1}（房间 {2}）", m.Id, ts.Layer ?? "", p.Name));
                continue;
            }
            int set = 0, oob = 0;
            var rows = layer.TilesData.TileData;
            foreach (var c in ts.Cells)
            {
                if (c.Y < 0 || c.Y >= rows.Length || rows[c.Y] == null || c.X < 0 || c.X >= rows[c.Y].Length)
                {
                    oob++;
                    continue;
                }
                if (rows[c.Y][c.X] != c.Value) { rows[c.Y][c.X] = c.Value; set++; }
            }
            if (set > 0) { layer.TilesData.TileDataUpdated(); n += set; }
            if (oob > 0)
                Paths.Log(L("    [警告] {0}: 房间 {1} 瓦片越界 {2} 格（已忽略）", m.Id, p.Name, oob));
        }

        return n;
    }

    private static int ApplyInstanceFields(UndertaleData data, ModEntry m, UndertaleRoom room,
        UndertaleRoom.GameObject inst, ModRoomInstance mi)
    {
        int n = 0;

        // 图层迁移：先从旧层移除，再加到目标层（room.GameObjects / 创建顺序列表不动）。
        if (!string.IsNullOrWhiteSpace(mi.Layer))
        {
            var target = FindInstancesLayer(room, mi.Layer!);
            if (target?.InstancesData?.Instances == null)
            {
                Paths.Log(L("    [警告] {0}: 目标图层不存在或不是实例图层: {1}", m.Id, mi.Layer));
            }
            else
            {
                var cur = LayerOf(room, inst);
                if (cur != target)
                {
                    cur?.InstancesData?.Instances?.Remove(inst);
                    target.InstancesData.Instances.Add(inst);
                    n++;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(mi.Object))
        {
            var obj = data.GameObjects.ByName(mi.Object!);
            if (obj == null) Paths.Log(L("    [警告] {0}: 要改成的对象不存在: {1}", m.Id, mi.Object));
            else if (!ReferenceEquals(inst.ObjectDefinition, obj)) { inst.ObjectDefinition = obj; n++; }
        }
        if (mi.X.HasValue && inst.X != mi.X.Value) { inst.X = mi.X.Value; n++; }
        if (mi.Y.HasValue && inst.Y != mi.Y.Value) { inst.Y = mi.Y.Value; n++; }
        if (mi.ScaleX.HasValue && inst.ScaleX != mi.ScaleX.Value) { inst.ScaleX = mi.ScaleX.Value; n++; }
        if (mi.ScaleY.HasValue && inst.ScaleY != mi.ScaleY.Value) { inst.ScaleY = mi.ScaleY.Value; n++; }
        if (mi.Rotation.HasValue && inst.Rotation != mi.Rotation.Value) { inst.Rotation = mi.Rotation.Value; n++; }
        if (mi.Color.HasValue && inst.Color != mi.Color.Value) { inst.Color = mi.Color.Value; n++; }
        if (mi.ImageIndex.HasValue && inst.ImageIndex != mi.ImageIndex.Value) { inst.ImageIndex = mi.ImageIndex.Value; n++; }
        if (mi.ImageSpeed.HasValue && inst.ImageSpeed != mi.ImageSpeed.Value) { inst.ImageSpeed = mi.ImageSpeed.Value; n++; }
        if (mi.Nonexistent.HasValue && inst.Nonexistent != mi.Nonexistent.Value) { inst.Nonexistent = mi.Nonexistent.Value; n++; }
        return n;
    }

    // ------------------------------------------------------------------ 查找/枚举

    private static UndertaleRoom.GameObject? FindInstance(UndertaleRoom room, uint instanceId)
    {
        if (instanceId == 0) return null;
        if (room.Layers != null)
            foreach (var l in room.Layers)
            {
                var list = l.InstancesData?.Instances;
                if (list == null) continue;
                foreach (var i in list) if (i.InstanceID == instanceId) return i;
            }
        if (room.GameObjects != null)
            foreach (var i in room.GameObjects) if (i.InstanceID == instanceId) return i;
        return null;
    }

    private static UndertaleRoom.Layer? LayerOf(UndertaleRoom room, UndertaleRoom.GameObject inst)
    {
        if (room.Layers == null) return null;
        foreach (var l in room.Layers)
        {
            var list = l.InstancesData?.Instances;
            if (list != null && list.Contains(inst)) return l;
        }
        return null;
    }

    private static void RemoveInstance(UndertaleRoom room, UndertaleRoom.GameObject inst)
    {
        if (room.Layers != null)
            foreach (var l in room.Layers)
                l.InstancesData?.Instances?.Remove(inst);
        room.GameObjects?.Remove(inst);
        var ids = room.InstanceCreationOrderIDs?.InstanceIDs;
        if (ids != null)
            for (int i = ids.Count - 1; i >= 0; i--)
                if (ids[i] == (int)inst.InstanceID) ids.RemoveAt(i);
    }

    private static UndertaleRoom.Layer? FirstInstancesLayer(UndertaleRoom room) =>
        room.Layers?.FirstOrDefault(l => l.InstancesData?.Instances != null);

    private static UndertaleRoom.Layer? FindInstancesLayer(UndertaleRoom room, string name)
    {
        if (room.Layers == null) return null;
        foreach (var l in room.Layers)
            if (l.InstancesData?.Instances != null && string.Equals(l.LayerName?.Content, name, StringComparison.Ordinal))
                return l;
        foreach (var l in room.Layers)
            if (l.InstancesData?.Instances != null && string.Equals(l.LayerName?.Content, name, StringComparison.OrdinalIgnoreCase))
                return l;
        return null;
    }

    private static UndertaleRoom.Layer? FindLayer(UndertaleRoom room, string name)
    {
        if (room.Layers == null) return null;
        foreach (var l in room.Layers)
            if (string.Equals(l.LayerName?.Content, name, StringComparison.Ordinal)) return l;
        foreach (var l in room.Layers)
            if (string.Equals(l.LayerName?.Content, name, StringComparison.OrdinalIgnoreCase)) return l;
        return null;
    }

    private static bool InstanceIdUsed(UndertaleData data, uint id)
    {
        foreach (var room in data.Rooms)
        {
            if (room.Layers != null)
                foreach (var l in room.Layers)
                {
                    var list = l.InstancesData?.Instances;
                    if (list == null) continue;
                    foreach (var i in list) if (i.InstanceID == id) return true;
                }
            if (room.GameObjects != null)
                foreach (var i in room.GameObjects) if (i.InstanceID == id) return true;
        }
        return false;
    }

    private static uint NextInstanceId(UndertaleData data)
    {
        uint max = 100000;
        foreach (var room in data.Rooms)
        {
            if (room.Layers != null)
                foreach (var l in room.Layers)
                {
                    var list = l.InstancesData?.Instances;
                    if (list == null) continue;
                    foreach (var i in list) if (i.InstanceID > max) max = i.InstanceID;
                }
            if (room.GameObjects != null)
                foreach (var i in room.GameObjects) if (i.InstanceID > max) max = i.InstanceID;
        }
        return max == uint.MaxValue ? max : max + 1;
    }
}
