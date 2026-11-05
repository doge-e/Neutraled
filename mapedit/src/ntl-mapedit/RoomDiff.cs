using UndertaleModLib;
using UndertaleModLib.Models;

namespace NtlMapEdit;

/// <summary>房间差分的构建结果：mod.json 的 roomPatches / rooms 两段 + 无法表达项的警告。</summary>
public sealed class RoomDiffResult
{
    public List<RoomPatch> Patches { get; } = new();
    public List<NewRoomDef> NewRooms { get; } = new();
    public List<string> Warnings { get; } = new();

    /// <summary>
    /// 差异条目总数：房间头算 1 条 + 改名算 1 条 + 每个实例/瓦片格/图层增删算 1 处 +
    /// 每组旧式瓦片替换按瓦片数算 + 每个新增房间算 1 条。
    /// </summary>
    public int ChangeCount
    {
        get
        {
            int n = 0;
            foreach (var p in Patches)
            {
                if (p.Width != null || p.Height != null || p.Speed != null || p.Persistent != null
                    || p.Flags != null || p.BackgroundColor != null || p.DrawBackgroundColor != null) n++;
                if (!string.IsNullOrWhiteSpace(p.Rename)) n++;
                if (p.RemoveFromOrder == true) n++;
                n += p.InstancesAdd.Count + p.InstancesModify.Count + p.InstancesRemove.Count;
                n += p.LayersAdd.Count + p.LayersRemove.Count;
                foreach (var ts in p.TilesSet) n += ts.Cells.Count;
                foreach (var lt in p.LegacyTilesSet) n += Math.Max(1, lt.Tiles.Count);
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
/// ★ 能表达的：房间头、实例增删改、原生瓦片逐格、图层增删（带初始内容）、房间改名、
///   旧式（LegacyTiles）瓦片整层替换、新增房间（带初始图层）。房间删除不支持（响亮报告）。
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
                result.AddWarning("房间重名：" + name + "（后一个会在部署时被 roomPatches 忽略）");
        }

        // 改名配对：同一索引位置上「旧名消失 + 新名出现」，且旧名没有在别处保留 → 视为改名。
        // builder 侧没有房间位置信息，只能靠这种索引配对表达（见 RoomPatch.Rename 文档）。
        var renames = new Dictionary<string, string>(StringComparer.Ordinal); // 新名 -> 旧名
        var renamedOld = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < edited.Rooms.Count; i++)
        {
            var er = edited.Rooms[i];
            if (er == null) continue;
            var name = GameData.Name(er.Name);
            if (baseByName.ContainsKey(name)) continue;           // 这个名字本来就在 = 不是改名
            if (i >= baseline.Rooms.Count) continue;
            var br = baseline.Rooms[i];
            if (br == null) continue;
            var oldName = GameData.Name(br.Name);
            if (editedNames.Contains(oldName)) continue;          // 旧名还被别的房间占着 = 不是改名
            if (renamedOld.Contains(oldName)) continue;
            if (!baseByName.TryGetValue(oldName, out var owner) || !ReferenceEquals(owner, br)) continue;
            renames[name] = oldName;
            renamedOld.Add(oldName);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var er in edited.Rooms)
        {
            if (er == null) continue;
            var name = GameData.Name(er.Name);
            if (!seen.Add(name)) continue;                        // 重名：只处理第一个
            if (baseByName.TryGetValue(name, out var br))
            {
                var patch = DiffRoom(br, er, name, result);
                ApplyOrderRemoval(baseline, edited, br, er, patch);
                if (!patch.IsEmpty) result.Patches.Add(patch);
                continue;
            }
            if (renames.TryGetValue(name, out var oldName2))
            {
                var br2 = baseByName[oldName2];
                var patch = DiffRoom(br2, er, oldName2, result);
                patch.Name = oldName2;
                patch.Rename = name;
                ApplyOrderRemoval(baseline, edited, br2, er, patch);
                result.Patches.Add(patch);                        // 改名本身就算一条改动
                continue;
            }
            result.NewRooms.Add(NewRoomFrom(er, name, result));
        }

        // 原版房间被删除：不能物理删（编号 = ROOM 表下标），表达为「从房间顺序摘除」。
        foreach (var kv in baseByName)
        {
            if (editedNames.Contains(kv.Key) || renamedOld.Contains(kv.Key)) continue;
            if (InRoomOrder(baseline, kv.Value)) result.Patches.Add(new RoomPatch { Name = kv.Key, RemoveFromOrder = true });
            else result.AddWarning("原版房间 " + kv.Key + " 已删除：它本来就不在房间顺序里，无法表达（该改动不会进入 mod）");
        }

        return result;
    }

    /// <summary>房间是否仍在 GeneralInfo.RoomOrder 里（删除表达为摘除；已摘除的不再重复产出）。</summary>
    private static bool InRoomOrder(UndertaleData data, UndertaleRoom room)
    {
        var order = data.GeneralInfo?.RoomOrder;
        if (order == null) return true;                       // 没有顺序表（旧格式？）→ 保守当作仍在
        foreach (var e in order)
            if (e != null && ReferenceEquals(e.Resource, room)) return true;
        return false;
    }

    /// <summary>
    /// 「房间还在、但被从 RoomOrder 摘除」（编辑器里删除原版房间的可逆表达）：
    /// baseline 顺序里有、edited 顺序里没有 ⇒ 产出 removeFromOrder，且与其它差分字段并存。
    /// 这样「保存 → 重开（RoomPatchApply 把房间从顺序里摘掉）→ 再保存」时补丁会再次产出，
    /// 不会把房间悄悄放回顺序。
    /// </summary>
    private static void ApplyOrderRemoval(UndertaleData baseline, UndertaleData edited, UndertaleRoom br,
        UndertaleRoom er, RoomPatch patch)
    {
        if (patch.RemoveFromOrder == true) return;
        if (!InRoomOrder(edited, er) && InRoomOrder(baseline, br)) patch.RemoveFromOrder = true;
    }

    private static NewRoomDef NewRoomFrom(UndertaleRoom r, string name, RoomDiffResult result)
    {
        var def = new NewRoomDef
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
        if (r.Layers != null)
        {
            var allIds = new HashSet<uint>();
            foreach (var (_, inst) in RoomEdit.AllInstances(r)) allIds.Add(inst.InstanceID);
            foreach (var layer in r.Layers)
            {
                if (layer == null) continue;
                var spec = LayerSpec(layer, name, allIds, result);
                if (spec != null) def.Layers.Add(spec);
            }
        }
        return def;
    }

    private static RoomPatch DiffRoom(UndertaleRoom br, UndertaleRoom er, string name, RoomDiffResult result)
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

        var (addedLayers, removedLayers) = DiffLayers(br, er, name, result, patch);

        // 实例差分：新图层的初始实例由 LayersAdd 承载；被删图层里的实例随图层一起删。
        var carriedByAdd = new HashSet<uint>();
        foreach (var kv in baseInst)
        {
            if (editInst.ContainsKey(kv.Key)) continue;
            if (removedLayers.Contains(kv.Value.Layer)) continue;     // 随图层删除
            patch.InstancesRemove.Add(new RoomInstanceRef { InstanceId = kv.Key });
        }

        foreach (var kv in editInst)
        {
            var e = kv.Value;
            if (!baseInst.TryGetValue(kv.Key, out var b))
            {
                if (addedLayers.Contains(e.Layer))
                {
                    carriedByAdd.Add(kv.Key);                          // 新图层里的全新实例
                    continue;
                }
                patch.InstancesAdd.Add(NewInstancePatch(e));
                continue;
            }
            if (!string.Equals(b.Layer, e.Layer, StringComparison.Ordinal)
                && addedLayers.Contains(e.Layer) && removedLayers.Contains(b.Layer))
            {
                carriedByAdd.Add(kv.Key);                              // 图层整体重建：新层承载实例
                continue;
            }
            var mod = DiffInstance(b, e);
            if (mod != null) patch.InstancesModify.Add(mod);
        }

        // 新增图层的初始内容（在实例差分之后，才能知道哪些实例由图层承载）
        if (er.Layers != null)
            foreach (var layer in er.Layers)
            {
                if (layer == null) continue;
                var layerName = GameData.Name(layer.LayerName);
                if (!addedLayers.Contains(layerName)) continue;
                var spec = LayerSpec(layer, name, carriedByAdd, result);
                if (spec != null) patch.LayersAdd.Add(spec);
            }

        DiffTiles(br, er, name, patch, result, addedLayers);
        DiffLegacyTiles(br, er, name, patch, result, addedLayers);
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

    /// <summary>比较两侧图层名集合，产出 LayersRemove；返回新增图层名集合（内容稍后填充）。</summary>
    private static (HashSet<string> Added, HashSet<string> Removed) DiffLayers(
        UndertaleRoom br, UndertaleRoom er, string name, RoomDiffResult result, RoomPatch patch)
    {
        var baseOrder = new List<string>();
        var baseSet = new HashSet<string>(StringComparer.Ordinal);
        if (br.Layers != null)
            foreach (var l in br.Layers)
            {
                if (l == null) continue;
                var n = GameData.Name(l.LayerName);
                if (baseSet.Add(n)) baseOrder.Add(n);
            }
        var editOrder = new List<string>();
        var editSet = new HashSet<string>(StringComparer.Ordinal);
        if (er.Layers != null)
            foreach (var l in er.Layers)
            {
                if (l == null) continue;
                var n = GameData.Name(l.LayerName);
                if (!editSet.Add(n))
                {
                    result.AddWarning("房间 " + name + " 的图层重名：" + n + "（后一个不会进差分）");
                    continue;
                }
                editOrder.Add(n);
            }

        var added = new HashSet<string>(editOrder.Where(n => !baseSet.Contains(n)), StringComparer.Ordinal);
        var removed = baseOrder.Where(n => !editSet.Contains(n)).ToList();

        // 至少保留一个图层：最后一个删除不能表达
        if (removed.Count >= baseOrder.Count && baseOrder.Count > 0)
        {
            var keep = removed[removed.Count - 1];
            removed.RemoveAt(removed.Count - 1);
            result.AddWarning("房间 " + name + " 的图层全被删除：至少保留一个图层，图层 " + keep + " 的删除不会进入 mod");
        }

        foreach (var n in removed) patch.LayersRemove.Add(n);
        return (added, new HashSet<string>(removed, StringComparer.Ordinal));
    }

    /// <summary>把一个图层转成 roomPatches 的图层规格（新增图层 / 新房间初始图层共用）。</summary>
    private static RoomLayer? LayerSpec(UndertaleRoom.Layer layer, string roomName, HashSet<uint> includeInstanceIds,
        RoomDiffResult result)
    {
        var name = GameData.Name(layer.LayerName);
        switch (layer.LayerType)
        {
            case UndertaleRoom.LayerType.Instances:
            {
                var spec = new RoomLayer
                {
                    Name = name,
                    Type = "instances",
                    Depth = layer.LayerDepth,
                    Visible = layer.IsVisible,
                };
                if (layer.InstancesData?.Instances != null)
                    foreach (var inst in layer.InstancesData.Instances)
                    {
                        if (inst == null || !includeInstanceIds.Contains(inst.InstanceID)) continue;
                        spec.Instances.Add(NewInstancePatch(new InstEntry(inst, name)));
                    }
                return spec;
            }
            case UndertaleRoom.LayerType.Tiles:
            {
                var spec = new RoomLayer
                {
                    Name = name,
                    Type = "tiles",
                    Depth = layer.LayerDepth,
                    Visible = layer.IsVisible,
                };
                var bg = layer.TilesData?.Background;
                if (bg != null) spec.Tileset = GameData.Name(bg.Name);
                var rows = layer.TilesData?.TileData;
                if (rows != null)
                    for (int y = 0; y < rows.Length; y++)
                    {
                        var row = rows[y];
                        if (row == null) continue;
                        for (int x = 0; x < row.Length; x++)
                            if ((row[x] & NativeTiles.IndexMask) != 0)
                                spec.Tiles.Add(new RoomTileCell { X = x, Y = y, Value = row[x] });
                    }
                return spec;
            }
            case UndertaleRoom.LayerType.Assets:
            {
                var spec = new RoomLayer
                {
                    Name = name,
                    Type = "assets",
                    Depth = layer.LayerDepth,
                    Visible = layer.IsVisible,
                };
                var ad = layer.AssetsData;
                if (ad?.LegacyTiles != null)
                    foreach (var t in ad.LegacyTiles)
                        if (t != null) spec.LegacyTiles.Add(NewTileDef(t));
                WarnUnexpressibleAssets(ad, roomName, name, result);
                return spec;
            }
            default:
                result.AddWarning("房间 " + roomName + " 的新增图层 " + name + " 类型为 " + layer.LayerType +
                    "：roomPatches 只能新增 instances/tiles/assets 图层，该层内容不会进入 mod");
                return null;
        }
    }

    private static void WarnUnexpressibleAssets(UndertaleRoom.Layer.LayerAssetsData? ad, string roomName,
        string layerName, RoomDiffResult result)
    {
        if (ad == null) return;
        int n = (ad.Sprites?.Count ?? 0) + (ad.Sequences?.Count ?? 0) + (ad.NineSlices?.Count ?? 0)
                + (ad.ParticleSystems?.Count ?? 0) + (ad.TextItems?.Count ?? 0);
        if (n > 0)
            result.AddWarning("房间 " + roomName + " 的图层 " + layerName + " 含 " + n +
                " 个精灵/序列/九宫格等资源实例：roomPatches 只能表达旧式瓦片，这些内容不会进入 mod");
    }

    private static RoomTileDef NewTileDef(UndertaleRoom.Tile t) => new()
    {
        InstanceId = t.InstanceID <= int.MaxValue ? (int)t.InstanceID : null,
        X = t.X,
        Y = t.Y,
        Sprite = t.SpriteDefinition == null ? null : GameData.Name(t.SpriteDefinition.Name),
        Background = t.BackgroundDefinition == null ? null : GameData.Name(t.BackgroundDefinition.Name),
        SourceX = t.SourceX,
        SourceY = t.SourceY,
        Width = t.Width,
        Height = t.Height,
        ScaleX = t.ScaleX,
        ScaleY = t.ScaleY,
        Color = t.Color,
    };

    private static void DiffTiles(UndertaleRoom br, UndertaleRoom er, string name, RoomPatch patch,
        RoomDiffResult result, HashSet<string> addedLayers)
    {
        if (er.Layers == null) return;
        foreach (var el in er.Layers)
        {
            var rows = el.TilesData?.TileData;
            if (rows == null || rows.Length == 0) continue;
            string layerName = GameData.Name(el.LayerName);
            if (addedLayers.Contains(layerName)) continue;            // 内容由 LayersAdd 承载
            var bl = FindLayer(br, layerName);
            var bRows = bl?.TilesData?.TileData;
            if (bRows == null || bRows.Length == 0) continue;         // 基线没有同名瓦片层：按新增图层处理
            if (bl?.TilesData?.Background != null && el.TilesData?.Background != null
                && !ReferenceEquals(bl.TilesData.Background, el.TilesData.Background))
                result.AddWarning("房间 " + name + " 的瓦片层 " + layerName + " 换了 tileset：" +
                    GameData.Name(el.TilesData.Background.Name) + "（roomPatches 不支持改 tileset，会沿用基线）");
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
                result.AddWarning("房间 " + name + " 的瓦片层 " + layerName + " 有 " + oob +
                    " 格超出基线尺寸：请先恢复房间尺寸或重开工程（超出部分不会进入 mod）");
            if (cells.Count > 0) patch.TilesSet.Add(new RoomTileSet { Layer = layerName, Cells = cells });
        }
    }

    /// <summary>逐图层比较旧式瓦片（按 TileKey 集合），有变化就整层替换。</summary>
    private static void DiffLegacyTiles(UndertaleRoom br, UndertaleRoom er, string name, RoomPatch patch,
        RoomDiffResult result, HashSet<string> addedLayers)
    {
        if (er.Layers == null) return;
        foreach (var el in er.Layers)
        {
            if (el.LayerType != UndertaleRoom.LayerType.Assets) continue;
            string layerName = GameData.Name(el.LayerName);
            if (addedLayers.Contains(layerName)) continue;            // 内容由 LayersAdd 承载
            var bl = FindLayer(br, layerName);
            if (bl?.AssetsData?.LegacyTiles == null) continue;        // 基线没有同名旧式瓦片层
            var a = bl.AssetsData.LegacyTiles.Where(t => t != null).Select(TileKey).OrderBy(s => s, StringComparer.Ordinal).ToList();
            var b = el.AssetsData!.LegacyTiles!.Where(t => t != null).Select(TileKey).OrderBy(s => s, StringComparer.Ordinal).ToList();
            if (a.SequenceEqual(b)) continue;
            var set = new RoomLegacyTiles { Layer = layerName };
            foreach (var t in el.AssetsData!.LegacyTiles!)
                if (t != null) set.Tiles.Add(NewTileDef(t));
            patch.LegacyTilesSet.Add(set);
        }
    }

    private static string TileKey(UndertaleRoom.Tile t) =>
        t.InstanceID + "|" + t.X + "|" + t.Y + "|" + GameData.Name(t.SpriteDefinition?.Name) + "|" +
        GameData.Name(t.BackgroundDefinition?.Name) + "|" +
        t.SourceX + "|" + t.SourceY + "|" + t.Width + "|" + t.Height + "|" + t.ScaleX + "|" + t.ScaleY + "|" + t.Color;

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
