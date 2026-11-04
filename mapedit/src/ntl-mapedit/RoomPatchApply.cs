using UndertaleModLib;
using UndertaleModLib.Models;

namespace NtlMapEdit;

/// <summary>把 sidecar 里的房间差分回灌进内存 data（打开原版基线时看到上次保存的改动）。</summary>
public sealed class RoomPatchApplyResult
{
    public int RoomsAdded;
    public int RoomsPatched;
    public int InstancesAdded;
    public int InstancesRemoved;
    public int InstancesModified;
    public int TilesWritten;
    public List<string> Warnings { get; } = new();

    public bool Any => RoomsAdded + RoomsPatched + InstancesAdded + InstancesRemoved + InstancesModified + TilesWritten > 0;
    public string Describe() =>
        $"回灌差分：新增房间 {RoomsAdded} / 改房间 {RoomsPatched} / 实例 +{InstancesAdded} -{InstancesRemoved} ~{InstancesModified} / 瓦片 {TilesWritten}";
}

/// <summary>
/// RoomPatch / NewRoomDef 的运行时应用器（与 builder/RoomPatches.cs 的语义保持一致）。
/// 同一份实现给 GUI 打开工程时回灌、给 CLI 自检做差分回环（diff → apply → 再 diff 应为空）。
/// </summary>
public static class RoomPatchApply
{
    public static RoomPatchApplyResult Apply(UndertaleData data, IReadOnlyList<RoomPatch>? patches,
        IReadOnlyList<NewRoomDef>? newRooms)
    {
        var res = new RoomPatchApplyResult();
        if (data == null) return res;

        foreach (var nr in newRooms ?? Array.Empty<NewRoomDef>())
        {
            if (string.IsNullOrWhiteSpace(nr.Name)) continue;
            if (FindRoom(data, nr.Name) != null) continue;
            if (data.Rooms == null || data.Rooms.Count == 0)
            {
                res.Warnings.Add("新增房间 " + nr.Name + " 无法回灌：data 里没有任何模板房间");
                continue;
            }
            var room = RoomEdit.CreateBlankRoom(data, data.Rooms[0], nr.Name);
            room.Width = nr.Width;
            room.Height = nr.Height;
            room.Speed = nr.Speed;
            room.Persistent = nr.Persistent;
            room.Flags = (UndertaleRoom.RoomEntryFlags)nr.Flags;
            room.BackgroundColor = unchecked((uint)nr.BackgroundColor);
            room.DrawBackgroundColor = nr.DrawBackgroundColor;
            res.RoomsAdded++;
        }

        foreach (var p in patches ?? Array.Empty<RoomPatch>())
        {
            if (string.IsNullOrWhiteSpace(p.Name)) continue;
            var room = FindRoom(data, p.Name);
            if (room == null)
            {
                res.Warnings.Add("房间差分目标不存在：" + p.Name);
                continue;
            }
            bool touched = false;
            if (p.Width.HasValue && room.Width != p.Width.Value) { room.Width = p.Width.Value; touched = true; }
            if (p.Height.HasValue && room.Height != p.Height.Value) { room.Height = p.Height.Value; touched = true; }
            if (p.Speed.HasValue && room.Speed != p.Speed.Value) { room.Speed = p.Speed.Value; touched = true; }
            if (p.Persistent.HasValue && room.Persistent != p.Persistent.Value) { room.Persistent = p.Persistent.Value; touched = true; }
            if (p.Flags.HasValue && (int)room.Flags != p.Flags.Value) { room.Flags = (UndertaleRoom.RoomEntryFlags)p.Flags.Value; touched = true; }
            if (p.BackgroundColor.HasValue)
            {
                uint v = unchecked((uint)p.BackgroundColor.Value);
                if (room.BackgroundColor != v) { room.BackgroundColor = v; touched = true; }
            }
            if (p.DrawBackgroundColor.HasValue && room.DrawBackgroundColor != p.DrawBackgroundColor.Value)
            {
                room.DrawBackgroundColor = p.DrawBackgroundColor.Value;
                touched = true;
            }

            foreach (var rf in p.InstancesRemove)
            {
                var inst = FindInstance(room, rf.InstanceId);
                if (inst == null) continue;
                RoomEdit.RemoveInstance(room, inst);
                res.InstancesRemoved++;
                touched = true;
            }

            foreach (var mi in p.InstancesModify)
            {
                var inst = FindInstance(room, mi.InstanceId);
                if (inst == null) continue;
                int n = ApplyFields(data, inst, mi);
                if (n > 0) { res.InstancesModified++; touched = true; }
            }

            foreach (var ai in p.InstancesAdd)
            {
                if (string.IsNullOrWhiteSpace(ai.Object)) continue;
                var obj = FindObject(data, ai.Object!);
                if (obj == null)
                {
                    res.Warnings.Add("新增实例的对象不存在：" + ai.Object + "（房间 " + p.Name + "）");
                    continue;
                }
                var layer = string.IsNullOrWhiteSpace(ai.Layer) ? RoomEdit.InstancesLayer(room) : FindInstancesLayer(room, ai.Layer!);
                if (layer?.InstancesData?.Instances == null)
                {
                    res.Warnings.Add("房间 " + p.Name + " 没有可用的实例图层");
                    continue;
                }
                uint id = ai.InstanceId != 0 && !InstanceIdUsed(data, ai.InstanceId) ? ai.InstanceId : RoomEdit.NextInstanceId(data);
                var inst = RoomEdit.AddInstance(data, room, layer, obj, ai.X ?? 0, ai.Y ?? 0, id);
                ApplyFields(data, inst, ai);
                res.InstancesAdded++;
                touched = true;
            }

            foreach (var ts in p.TilesSet)
            {
                var layer = string.IsNullOrWhiteSpace(ts.Layer)
                    ? room.Layers?.FirstOrDefault(l => l.TilesData?.TileData != null)
                    : FindLayer(room, ts.Layer!);
                var rows = layer?.TilesData?.TileData;
                if (rows == null)
                {
                    res.Warnings.Add("房间里找不到原生瓦片层：" + (ts.Layer ?? "") + "（房间 " + p.Name + "）");
                    continue;
                }
                int set = 0;
                foreach (var c in ts.Cells)
                {
                    if (c.Y < 0 || c.Y >= rows.Length || rows[c.Y] == null || c.X < 0 || c.X >= rows[c.Y].Length)
                    {
                        res.Warnings.Add("房间 " + p.Name + " 的瓦片越界：(" + c.X + "," + c.Y + ")，已忽略");
                        continue;
                    }
                    if (rows[c.Y][c.X] != c.Value) { rows[c.Y][c.X] = c.Value; set++; }
                }
                if (set > 0) { layer!.TilesData!.TileDataUpdated(); res.TilesWritten += set; touched = true; }
            }

            if (touched) res.RoomsPatched++;
        }

        return res;
    }

    private static int ApplyFields(UndertaleData data, UndertaleRoom.GameObject inst, RoomInstancePatch mi)
    {
        int n = 0;
        if (!string.IsNullOrWhiteSpace(mi.Object))
        {
            var obj = FindObject(data, mi.Object!);
            if (obj != null && !ReferenceEquals(inst.ObjectDefinition, obj)) { inst.ObjectDefinition = obj; n++; }
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

    // ------------------------------------------------------------------ 查找

    public static UndertaleRoom? FindRoom(UndertaleData data, string name)
    {
        if (data?.Rooms == null) return null;
        foreach (var r in data.Rooms)
            if (r != null && string.Equals(GameData.Name(r.Name), name, StringComparison.Ordinal)) return r;
        foreach (var r in data.Rooms)
            if (r != null && string.Equals(GameData.Name(r.Name), name, StringComparison.OrdinalIgnoreCase)) return r;
        return null;
    }

    private static UndertaleGameObject? FindObject(UndertaleData data, string name)
    {
        if (data?.GameObjects == null) return null;
        foreach (var o in data.GameObjects)
            if (o != null && string.Equals(GameData.Name(o.Name), name, StringComparison.Ordinal)) return o;
        return null;
    }

    private static UndertaleRoom.GameObject? FindInstance(UndertaleRoom room, uint instanceId)
    {
        if (instanceId == 0) return null;
        foreach (var (_, inst) in RoomEdit.AllInstances(room))
            if (inst.InstanceID == instanceId) return inst;
        return null;
    }

    private static UndertaleRoom.Layer? FindInstancesLayer(UndertaleRoom room, string name)
    {
        if (room.Layers == null) return null;
        foreach (var l in room.Layers)
            if (l.InstancesData?.Instances != null && string.Equals(GameData.Name(l.LayerName), name, StringComparison.Ordinal))
                return l;
        foreach (var l in room.Layers)
            if (l.InstancesData?.Instances != null && string.Equals(GameData.Name(l.LayerName), name, StringComparison.OrdinalIgnoreCase))
                return l;
        return null;
    }

    private static UndertaleRoom.Layer? FindLayer(UndertaleRoom room, string name)
    {
        if (room.Layers == null) return null;
        foreach (var l in room.Layers)
            if (string.Equals(GameData.Name(l.LayerName), name, StringComparison.Ordinal)) return l;
        foreach (var l in room.Layers)
            if (string.Equals(GameData.Name(l.LayerName), name, StringComparison.OrdinalIgnoreCase)) return l;
        return null;
    }

    private static bool InstanceIdUsed(UndertaleData data, uint id)
    {
        if (data?.Rooms == null) return false;
        foreach (var room in data.Rooms)
            foreach (var (_, inst) in RoomEdit.AllInstances(room))
                if (inst.InstanceID == id) return true;
        return false;
    }
}
