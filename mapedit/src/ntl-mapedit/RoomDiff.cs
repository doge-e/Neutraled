using UndertaleModLib;
using UndertaleModLib.Models;

namespace NtlMapEdit;

/// <summary>房间差分的构建结果：mod.json 的 roomPatches / rooms 两段 + 无法表达项的警告。</summary>
public sealed class RoomDiffResult
{
    public List<RoomPatch> Patches { get; } = new();
    public List<NewRoomDef> NewRooms { get; } = new();
    public List<string> Warnings { get; } = new();

    /// <summary>差异条目总数（房间头算 1 条 + 每个实例/瓦片格算 1 处 + 每个新增房间算 1 条）。</summary>
    public int ChangeCount
    {
        get
        {
            int n = 0;
            foreach (var p in Patches)
            {
                if (p.Width != null || p.Height != null || p.Speed != null || p.Persistent != null
                    || p.Flags != null || p.BackgroundColor != null || p.DrawBackgroundColor != null) n++;
                n += p.InstancesAdd.Count + p.InstancesModify.Count + p.InstancesRemove.Count;
                foreach (var ts in p.TilesSet) n += ts.Cells.Count;
            }
            n += NewRooms.Count;
            return n;
        }
    }

    public void AddWarning(string message)
    {
        if (!Warnings.Contains(message)) Warnings.Add(message);
    }
}

/// <summary>
/// 把「编辑后的 data」与「原版基线 data」比较，产出 Neutraled 的 roomPatches / rooms 两段。
/// ★ 为什么不复用 EditorDocument 的 undo 快照：它是「打开之后」的状态，不是原版基线；
///   而且 undo 快照不含 Flags（见 EditorDocument.RoomSnapshot）。这里直接与基线逐字段比。
/// ★ 只比较能表达的东西：房间头、实例增删改、原生瓦片逐格；删房间/改名/加图层/旧式瓦片
///   一律进 Warnings 响亮报告，不静默丢。
/// </summary>
public static class RoomDiff
{
    public static RoomDiffResult Build(UndertaleData baseline, UndertaleData edited)
    {
        var result = new RoomDiffResult();
        if (baseline?.Rooms == null || edited?.Rooms == null) return result;

        var baseByName = new Dictionary<string, UndertaleRoom>(StringComparer.Ordinal);
        foreach (var r in baseline.Rooms)
        {
            if (r == null) continue;
            var name = GameData.Name(r.Name);
            if (!baseByName.ContainsKey(name)) baseByName[name] = r;
        }

        var editedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var er in edited.Rooms)
        {
            if (er == null) continue;
            var name = GameData.Name(er.Name);
            if (!editedNames.Add(name))
            {
                result.AddWarning("房间重名：" + name + "（后一个会在部署时被 roomPatches 忽略）");
                continue;
            }
            if (!baseByName.TryGetValue(name, out var br))
            {
                result.NewRooms.Add(NewRoomFrom(er, name));
                if (HasContent(er, out string why))
                    result.AddWarning("新增房间 " + name + " 含" + why + "：rooms 段只支持空房间，这些内容不会进入 mod");
                continue;
            }
            var patch = DiffRoom(br, er, name, result);
            if (patch != null && !patch.IsEmpty) result.Patches.Add(patch);
        }

        foreach (var kv in baseByName)
            if (!editedNames.Contains(kv.Key))
                result.AddWarning("原版房间 " + kv.Key + " 已删除或改名：roomPatches 暂不支持删除/改名房间（该改动不会进入 mod）");

        return result;
    }

    private static NewRoomDef NewRoomFrom(UndertaleRoom r, string name) => new()
    {
        Name = name,
        Width = r.Width,
        Height = r.Height,
        Speed = r.Speed,
        Persistent = r.Persistent,
        Flags = (int)r.Flags,
        BackgroundColor = r.BackgroundColor,
        DrawBackgroundColor = r.DrawBackgroundColor,
    };

    private static bool HasContent(UndertaleRoom room, out string why)
    {
        int inst = RoomEdit.AllInstances(room).Count();
        int tiles = 0;
        if (room.Layers != null)
            foreach (var l in room.Layers)
                tiles += NativeTiles.CountNonZero(l);
        int legacy = RoomEdit.AllTiles(room).Count();
        why = "";
        if (inst > 0) why = " " + inst + " 个实例";
        if (tiles > 0) why += (why.Length > 0 ? " / " : " ") + tiles + " 个原生瓦片";
        if (legacy > 0) why += (why.Length > 0 ? " / " : " ") + legacy + " 个旧式瓦片";
        return why.Length > 0;
    }

    private static RoomPatch? DiffRoom(UndertaleRoom br, UndertaleRoom er, string name, RoomDiffResult result)
    {
        var patch = new RoomPatch { Name = name };
        if (br.Width != er.Width) patch.Width = er.Width;
        if (br.Height != er.Height) patch.Height = er.Height;
        if (br.Speed != er.Speed) patch.Speed = er.Speed;
        if (br.Persistent != er.Persistent) patch.Persistent = er.Persistent;
        if ((int)br.Flags != (int)er.Flags) patch.Flags = (int)er.Flags;
        if (br.BackgroundColor != er.BackgroundColor) patch.BackgroundColor = er.BackgroundColor;
        if (br.DrawBackgroundColor != er.DrawBackgroundColor) patch.DrawBackgroundColor = er.DrawBackgroundColor;

        var baseInst = Instances(br, name, result);
        var editInst = Instances(er, name, result);

        foreach (var kv in baseInst)
            if (!editInst.ContainsKey(kv.Key))
                patch.InstancesRemove.Add(new RoomInstanceRef { InstanceId = kv.Key });

        foreach (var kv in editInst)
        {
            var e = kv.Value;
            if (!baseInst.TryGetValue(kv.Key, out var b))
            {
                patch.InstancesAdd.Add(NewInstancePatch(e));
                continue;
            }
            var mod = DiffInstance(b, e);
            if (mod != null) patch.InstancesModify.Add(mod);
        }

        DiffTiles(br, er, name, patch, result);
        DiffLegacyTiles(br, er, name, result);
        DiffLayers(br, er, name, result);
        return patch;
    }

    private sealed record InstEntry(UndertaleRoom.GameObject Inst, string Layer);

    private static Dictionary<uint, InstEntry> Instances(UndertaleRoom room, string roomName, RoomDiffResult result)
    {
        var map = new Dictionary<uint, InstEntry>();
        foreach (var (layer, inst) in RoomEdit.AllInstances(room))
        {
            if (inst == null) continue;
            if (map.ContainsKey(inst.InstanceID))
            {
                result.AddWarning("房间 " + roomName + " 的实例 ID 重复：" + inst.InstanceID + "（后一个不会进差分）");
                continue;
            }
            map[inst.InstanceID] = new InstEntry(inst, GameData.Name(layer.LayerName));
        }
        return map;
    }

    private static RoomInstancePatch NewInstancePatch(InstEntry e)
    {
        var inst = e.Inst;
        return new RoomInstancePatch
        {
            InstanceId = inst.InstanceID,
            Object = GameData.Name(inst.ObjectDefinition?.Name),
            X = inst.X,
            Y = inst.Y,
            Layer = e.Layer,
            ScaleX = inst.ScaleX,
            ScaleY = inst.ScaleY,
            Rotation = inst.Rotation,
            Color = inst.Color,
            ImageIndex = inst.ImageIndex,
            ImageSpeed = inst.ImageSpeed,
            Nonexistent = inst.Nonexistent,
        };
    }

    private static RoomInstancePatch? DiffInstance(InstEntry b, InstEntry e)
    {
        var bi = b.Inst;
        var ei = e.Inst;
        var mod = new RoomInstancePatch { InstanceId = ei.InstanceID };
        bool any = false;
        string bObj = GameData.Name(bi.ObjectDefinition?.Name), eObj = GameData.Name(ei.ObjectDefinition?.Name);
        if (!string.Equals(bObj, eObj, StringComparison.Ordinal)) { mod.Object = eObj; any = true; }
        if (bi.X != ei.X) { mod.X = ei.X; any = true; }
        if (bi.Y != ei.Y) { mod.Y = ei.Y; any = true; }
        if (!string.Equals(b.Layer, e.Layer, StringComparison.Ordinal)) { mod.Layer = e.Layer; any = true; }
        if (bi.ScaleX != ei.ScaleX) { mod.ScaleX = ei.ScaleX; any = true; }
        if (bi.ScaleY != ei.ScaleY) { mod.ScaleY = ei.ScaleY; any = true; }
        if (bi.Rotation != ei.Rotation) { mod.Rotation = ei.Rotation; any = true; }
        if (bi.Color != ei.Color) { mod.Color = ei.Color; any = true; }
        if (bi.ImageIndex != ei.ImageIndex) { mod.ImageIndex = ei.ImageIndex; any = true; }
        if (bi.ImageSpeed != ei.ImageSpeed) { mod.ImageSpeed = ei.ImageSpeed; any = true; }
        if (bi.Nonexistent != ei.Nonexistent) { mod.Nonexistent = ei.Nonexistent; any = true; }
        return any ? mod : null;
    }

    private static void DiffTiles(UndertaleRoom br, UndertaleRoom er, string name, RoomPatch patch, RoomDiffResult result)
    {
        if (er.Layers == null) return;
        foreach (var el in er.Layers)
        {
            var rows = el.TilesData?.TileData;
            if (rows == null || rows.Length == 0) continue;
            string layerName = GameData.Name(el.LayerName);
            var bl = FindLayer(br, layerName);
            var bRows = bl?.TilesData?.TileData;
            if (bRows == null || bRows.Length == 0)
            {
                result.AddWarning("房间 " + name + " 的新增瓦片层 " + layerName + "：roomPatches 只能写已有瓦片层，该层内容不会进入 mod");
                continue;
            }
            var cells = new List<RoomTileCell>();
            int oob = 0;
            for (int y = 0; y < rows.Length; y++)
            {
                var row = rows[y];
                if (row == null) continue;
                if (y >= bRows.Length || bRows[y] == null)
                {
                    int nz = row.Count(v => (v & NativeTiles.IndexMask) != 0);
                    if (nz > 0) oob += nz;
                    continue;
                }
                var brow = bRows[y];
                for (int x = 0; x < row.Length; x++)
                {
                    if (x >= brow.Length)
                    {
                        if ((row[x] & NativeTiles.IndexMask) != 0) oob++;
                        continue;
                    }
                    if (row[x] != brow[x]) cells.Add(new RoomTileCell { X = x, Y = y, Value = row[x] });
                }
            }
            if (oob > 0)
                result.AddWarning("房间 " + name + " 的瓦片层 " + layerName + " 有 " + oob + " 格超出基线尺寸：请先恢复房间尺寸或重开工程（超出部分不会进入 mod）");
            if (cells.Count > 0) patch.TilesSet.Add(new RoomTileSet { Layer = layerName, Cells = cells });
        }
    }

    private static void DiffLegacyTiles(UndertaleRoom br, UndertaleRoom er, string name, RoomDiffResult result)
    {
        var a = RoomEdit.AllTiles(br).Select(t => TileKey(t.Tile)).OrderBy(s => s, StringComparer.Ordinal).ToList();
        var b = RoomEdit.AllTiles(er).Select(t => TileKey(t.Tile)).OrderBy(s => s, StringComparer.Ordinal).ToList();
        if (!a.SequenceEqual(b))
            result.AddWarning("房间 " + name + " 的旧式（LegacyTiles）瓦片有改动：roomPatches 只支持原生瓦片层，该改动不会进入 mod");
    }

    private static string TileKey(UndertaleRoom.Tile t) =>
        t.InstanceID + "|" + t.X + "|" + t.Y + "|" + GameData.Name(t.SpriteDefinition?.Name) + "|" +
        t.SourceX + "|" + t.SourceY + "|" + t.Width + "|" + t.Height + "|" + t.ScaleX + "|" + t.ScaleY + "|" + t.Color;

    private static void DiffLayers(UndertaleRoom br, UndertaleRoom er, string name, RoomDiffResult result)
    {
        var b = new HashSet<string>(StringComparer.Ordinal);
        var e = new HashSet<string>(StringComparer.Ordinal);
        if (br.Layers != null) foreach (var l in br.Layers) b.Add(GameData.Name(l.LayerName));
        if (er.Layers != null) foreach (var l in er.Layers) e.Add(GameData.Name(l.LayerName));
        foreach (var n in e) if (!b.Contains(n)) result.AddWarning("房间 " + name + " 新增图层 " + n + "：roomPatches 不支持新增图层，该层内容不会进入 mod");
        foreach (var n in b) if (!e.Contains(n)) result.AddWarning("房间 " + name + " 删除图层 " + n + "：roomPatches 不支持删除图层，该改动不会进入 mod");
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
}
