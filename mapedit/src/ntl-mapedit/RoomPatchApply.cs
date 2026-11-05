using UndertaleModLib;
using UndertaleModLib.Models;

namespace NtlMapEdit;

/// <summary>把 sidecar 里的房间差分回灌进内存 data（打开原版基线时看到上次保存的改动）。</summary>
public sealed class RoomPatchApplyResult
{
    public int RoomsAdded;
    public int RoomsPatched;
    public int RoomsRenamed;
    public int RoomsRemovedFromOrder;
    public int InstancesAdded;
    public int InstancesRemoved;
    public int InstancesModified;
    public int TilesWritten;
    public int LayersAdded;
    public int LayersRemoved;
    public int LegacyTilesWritten;
    public List<string> Warnings { get; } = new();

    public bool Any => RoomsAdded + RoomsPatched + RoomsRenamed + RoomsRemovedFromOrder + InstancesAdded
                       + InstancesRemoved + InstancesModified + TilesWritten + LayersAdded + LayersRemoved
                       + LegacyTilesWritten > 0;

    public string Describe() =>
        $"回灌差分：新增房间 {RoomsAdded} / 改房间 {RoomsPatched} / 改名 {RoomsRenamed}" +
        $" / 摘除房间 {RoomsRemovedFromOrder}" +
        $" / 实例 +{InstancesAdded} -{InstancesRemoved} ~{InstancesModified} / 瓦片 {TilesWritten}" +
        $" / 图层 +{LayersAdded} -{LayersRemoved} / 旧式瓦片 {LegacyTilesWritten}";
}

/// <summary>
/// RoomPatch / NewRoomDef 的运行时应用器（与 builder/RoomPatches.cs 的语义保持一致）。
/// 同一份实现给 GUI 打开工程时回灌、给 CLI 自检做差分回环（diff → apply → 再 diff 应为空）。
///
/// 应用顺序（镜像 builder）：房间头 → 删图层 → 加图层 → 删实例 → 改实例 → 加实例 →
/// 旧式瓦片整层替换 → 原生瓦片逐格写入 → 房间改名（最后）。
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
            if (nr.Layers.Count > 0)
            {
                // 模板房间的图层结构不一定等于 mod 声明的内容：清掉再用声明的图层重建，
                // 这样 diff → apply → 再 diff 才能收敛为空。
                RoomEdit.ClearLayers(room);
                FillLayers(data, room, nr.Layers, nr.Name, res);
            }
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

            foreach (var layerName in p.LayersRemove)
            {
                if (string.IsNullOrWhiteSpace(layerName)) continue;
                var layer = FindLayer(room, layerName.Trim());
                if (layer == null)
                {
                    res.Warnings.Add("房间 " + p.Name + " 要删的图层不存在：" + layerName);
                    continue;
                }
                if (!RoomEdit.RemoveLayer(room, layer))
                {
                    res.Warnings.Add("房间 " + p.Name + " 只剩一个图层，不能删 " + layerName);
                    continue;
                }
                res.LayersRemoved++;
                touched = true;
            }

            foreach (var spec in p.LayersAdd)
            {
                if (spec == null || string.IsNullOrWhiteSpace(spec.Name)) continue;
                var layer = EnsureLayer(data, room, spec, p.Name, res);
                if (layer == null) continue;
                res.LayersAdded++;
                FillLayerContent(data, room, layer, spec, p.Name, res);
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
                int n = ApplyFields(data, room, inst, mi, p.Name, res);
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
                ApplyFields(data, room, inst, ai, p.Name, res);
                res.InstancesAdded++;
                touched = true;
            }

            foreach (var lt in p.LegacyTilesSet)
            {
                var layer = string.IsNullOrWhiteSpace(lt.Layer)
                    ? room.Layers?.FirstOrDefault(l => l.AssetsData?.LegacyTiles != null)
                    : FindLayer(room, lt.Layer!);
                if (layer?.AssetsData?.LegacyTiles == null)
                {
                    res.Warnings.Add("房间里找不到旧式瓦片层：" + (lt.Layer ?? "") + "（房间 " + p.Name + "）");
                    continue;
                }
                int n = ReplaceLegacyTiles(data, room, layer, lt.Tiles, p.Name, res);
                if (n > 0) { res.LegacyTilesWritten += n; touched = true; }
            }

            foreach (var ts in p.TilesSet)
            {
                var layer = string.IsNullOrWhiteSpace(ts.Layer)
                    ? room.Layers?.FirstOrDefault(l => l.TilesData?.TileData != null)
                    : FindLayer(room, ts.Layer!);
                if (layer?.TilesData?.TileData == null)
                {
                    res.Warnings.Add("房间里找不到原生瓦片层：" + (ts.Layer ?? "") + "（房间 " + p.Name + "）");
                    continue;
                }
                int set = WriteTileCells(layer, ts.Cells, p.Name, res);
                if (set > 0) { res.TilesWritten += set; touched = true; }
            }

            // 改名最后做：其它字段仍按旧名定位房间
            if (!string.IsNullOrWhiteSpace(p.Rename))
            {
                string newName = p.Rename!.Trim();
                string oldName = GameData.Name(room.Name);
                if (!string.Equals(oldName, newName, StringComparison.Ordinal))
                {
                    bool taken = false;
                    if (data.Rooms != null)
                        foreach (var r in data.Rooms)
                            if (r != null && !ReferenceEquals(r, room)
                                && string.Equals(GameData.Name(r.Name), newName, StringComparison.Ordinal))
                            { taken = true; break; }
                    if (taken)
                        res.Warnings.Add("房间改名目标已存在：" + oldName + " -> " + newName + "（跳过改名）");
                    else
                    {
                        room.Name = data.Strings.MakeString(newName);
                        res.RoomsRenamed++;
                        touched = true;
                    }
                }
            }

            // 删除房间的安全表达：从房间顺序摘除（保留资源与编号），最后做
            if (p.RemoveFromOrder == true)
            {
                var order = data.GeneralInfo?.RoomOrder;
                int removed = 0;
                if (order != null)
                    for (int i = order.Count - 1; i >= 0; i--)
                        if (order[i] != null && ReferenceEquals(order[i].Resource, room))
                        {
                            order.RemoveAt(i);
                            removed++;
                        }
                if (removed > 0) { res.RoomsRemovedFromOrder++; touched = true; }
                else res.Warnings.Add("房间 " + p.Name + " 本来就不在房间顺序里（摘除无效果）");
            }

            if (touched) res.RoomsPatched++;
        }

        return res;
    }

    // ------------------------------------------------------------------ 图层

    private static void FillLayers(UndertaleData data, UndertaleRoom room, IReadOnlyList<RoomLayer> specs,
        string roomName, RoomPatchApplyResult res)
    {
        foreach (var spec in specs)
        {
            if (spec == null || string.IsNullOrWhiteSpace(spec.Name)) continue;
            var layer = EnsureLayer(data, room, spec, roomName, res);
            if (layer == null) continue;
            res.LayersAdded++;
            FillLayerContent(data, room, layer, spec, roomName, res);
        }
    }

    /// <summary>按规格建图层：同名已存在就复用（不重复建层），否则按 type 新建。</summary>
    private static UndertaleRoom.Layer? EnsureLayer(UndertaleData data, UndertaleRoom room, RoomLayer spec,
        string roomName, RoomPatchApplyResult res)
    {
        string type = (spec.Type ?? "").Trim().ToLowerInvariant();
        if (type.Length == 0) type = "instances";
        string layerName = spec.Name.Trim();

        var existing = FindLayer(room, layerName);
        if (existing != null)
        {
            res.Warnings.Add("房间 " + roomName + " 已有图层 " + layerName + "，内容叠加到既有层（不重复建层）");
            return existing;
        }
        if (room.Layers == null)
        {
            res.Warnings.Add("房间 " + roomName + " 没有图层表（旧格式房间？），无法新增图层 " + layerName);
            return null;
        }

        UndertaleRoom.Layer? layer;
        switch (type)
        {
            case "instances":
                layer = RoomEdit.AddInstanceLayer(data, room, layerName, spec.Depth);
                break;
            case "tiles":
            {
                UndertaleBackground? bg = null;
                if (!string.IsNullOrWhiteSpace(spec.Tileset))
                {
                    bg = FindBackground(data, spec.Tileset!.Trim());
                    if (bg == null)
                        res.Warnings.Add("tileset 不存在或不可用：" + spec.Tileset + "（房间 " + roomName + "，改为自动挑选）");
                }
                layer = RoomEdit.AddTileLayer(data, room, layerName, spec.Depth, bg);
                if (layer.LayerType != UndertaleRoom.LayerType.Tiles)
                {
                    res.Warnings.Add("房间 " + roomName + " 找不到可用的原生 tileset，瓦片层 " + layerName + " 退回旧式瓦片层");
                }
                break;
            }
            case "assets":
                layer = RoomEdit.AddAssetsLayer(data, room, layerName, spec.Depth);
                break;
            default:
                res.Warnings.Add("房间 " + roomName + " 的图层 " + layerName + " 类型 " + spec.Type + " 无法表达（跳过）");
                return null;
        }

        layer.IsVisible = spec.Visible ?? true;
        layer.LayerDepth = spec.Depth;
        return layer;
    }

    private static void FillLayerContent(UndertaleData data, UndertaleRoom room, UndertaleRoom.Layer layer,
        RoomLayer spec, string roomName, RoomPatchApplyResult res)
    {
        if (spec.Instances.Count > 0)
        {
            if (layer.InstancesData?.Instances == null)
                res.Warnings.Add("房间 " + roomName + " 的图层 " + spec.Name + " 不是实例图层，" + spec.Instances.Count + " 个实例跳过");
            else
                foreach (var ai in spec.Instances)
                {
                    if (string.IsNullOrWhiteSpace(ai.Object))
                    {
                        res.Warnings.Add("新增实例缺 object（房间 " + roomName + "）");
                        continue;
                    }
                    var obj = FindObject(data, ai.Object!);
                    if (obj == null)
                    {
                        res.Warnings.Add("新增实例的对象不存在：" + ai.Object + "（房间 " + roomName + "）");
                        continue;
                    }
                    uint id = ai.InstanceId != 0 && !InstanceIdUsed(data, ai.InstanceId) ? ai.InstanceId : RoomEdit.NextInstanceId(data);
                    var inst = RoomEdit.AddInstance(data, room, layer, obj, ai.X ?? 0, ai.Y ?? 0, id);
                    ApplyFields(data, room, inst, ai, roomName, res);
                    res.InstancesAdded++;
                }
        }
        if (spec.Tiles.Count > 0)
        {
            if (layer.TilesData?.TileData == null)
                res.Warnings.Add("房间 " + roomName + " 的图层 " + spec.Name + " 不是原生瓦片层，" + spec.Tiles.Count + " 格瓦片跳过");
            else
                res.TilesWritten += WriteTileCells(layer, spec.Tiles, roomName, res);
        }
        if (spec.LegacyTiles.Count > 0)
        {
            if (layer.AssetsData?.LegacyTiles == null)
                res.Warnings.Add("房间 " + roomName + " 的图层 " + spec.Name + " 不是旧式瓦片层，" + spec.LegacyTiles.Count + " 个瓦片跳过");
            else
                foreach (var def in spec.LegacyTiles)
                    if (AddLegacyTile(data, layer, def, roomName, res)) res.LegacyTilesWritten++;
        }
    }

    private static int WriteTileCells(UndertaleRoom.Layer layer, IReadOnlyList<RoomTileCell> cells, string roomName,
        RoomPatchApplyResult res)
    {
        var rows = layer.TilesData?.TileData;
        if (rows == null) return 0;
        int set = 0;
        foreach (var c in cells)
        {
            if (c.Y < 0 || c.Y >= rows.Length || rows[c.Y] == null || c.X < 0 || c.X >= rows[c.Y].Length)
            {
                res.Warnings.Add("房间 " + roomName + " 的瓦片越界：(" + c.X + "," + c.Y + ")，已忽略");
                continue;
            }
            if (rows[c.Y][c.X] != c.Value) { rows[c.Y][c.X] = c.Value; set++; }
        }
        if (set > 0) layer.TilesData!.TileDataUpdated();
        return set;
    }

    private static int ReplaceLegacyTiles(UndertaleData data, UndertaleRoom room, UndertaleRoom.Layer layer,
        IReadOnlyList<RoomTileDef> defs, string roomName, RoomPatchApplyResult res)
    {
        var list = layer.AssetsData!.LegacyTiles!;
        var old = list.ToList();
        list.Clear();
        if (room.Tiles != null)
            foreach (var t in old) room.Tiles.Remove(t);
        int n = old.Count;
        foreach (var def in defs)
            if (AddLegacyTile(data, layer, def, roomName, res)) n++;
        return n;
    }

    private static bool AddLegacyTile(UndertaleData data, UndertaleRoom.Layer layer, RoomTileDef def,
        string roomName, RoomPatchApplyResult res)
    {
        UndertaleSprite? spr = null;
        UndertaleBackground? bg = null;
        if (!string.IsNullOrWhiteSpace(def.Sprite))
        {
            spr = FindSprite(data, def.Sprite!.Trim());
            if (spr == null)
                res.Warnings.Add("旧式瓦片图源不存在：" + def.Sprite + "（房间 " + roomName + "）");
        }
        if (spr == null && !string.IsNullOrWhiteSpace(def.Background))
            bg = FindBackground(data, def.Background!.Trim());
        if (spr == null && bg == null) return false;

        uint id = def.InstanceId is int raw && raw > 0 && !TileIdUsed(data, (uint)raw)
            ? (uint)raw
            : RoomEdit.NextTileId(data);
        var tile = new UndertaleRoom.Tile
        {
            spriteMode = spr != null && data.IsGameMaker2(),
            SpriteDefinition = spr,
            BackgroundDefinition = bg,
            X = def.X,
            Y = def.Y,
            SourceX = def.SourceX,
            SourceY = def.SourceY,
            Width = def.Width > 0 ? def.Width : 16,
            Height = def.Height > 0 ? def.Height : 16,
            ScaleX = def.ScaleX ?? 1f,
            ScaleY = def.ScaleY ?? 1f,
            Color = def.Color ?? 0xFFFFFFFF,
            TileDepth = layer.LayerDepth,
            InstanceID = id,
        };
        layer.AssetsData!.LegacyTiles!.Add(tile);
        return true;
    }

    // ------------------------------------------------------------------ 实例字段

    private static int ApplyFields(UndertaleData data, UndertaleRoom room, UndertaleRoom.GameObject inst,
        RoomInstancePatch mi, string roomName, RoomPatchApplyResult res)
    {
        int n = 0;
        if (!string.IsNullOrWhiteSpace(mi.Layer))
        {
            var target = FindInstancesLayer(room, mi.Layer!);
            if (target?.InstancesData?.Instances == null)
                res.Warnings.Add("目标图层不存在或不是实例图层：" + mi.Layer + "（房间 " + roomName + "）");
            else
            {
                var cur = RoomEdit.LayerOf(room, inst);
                if (!ReferenceEquals(cur, target))
                {
                    cur?.InstancesData?.Instances?.Remove(inst);
                    target.InstancesData.Instances.Add(inst);
                    n++;
                }
            }
        }
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

    private static UndertaleSprite? FindSprite(UndertaleData data, string name)
    {
        if (data?.Sprites == null) return null;
        foreach (var s in data.Sprites)
            if (s != null && string.Equals(GameData.Name(s.Name), name, StringComparison.Ordinal)) return s;
        return null;
    }

    private static UndertaleBackground? FindBackground(UndertaleData data, string name)
    {
        if (data?.Backgrounds == null) return null;
        foreach (var b in data.Backgrounds)
            if (b != null && string.Equals(GameData.Name(b.Name), name, StringComparison.Ordinal)) return b;
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

    private static bool TileIdUsed(UndertaleData data, uint id)
    {
        if (data?.Rooms == null) return false;
        foreach (var room in data.Rooms)
            foreach (var (_, tile) in RoomEdit.AllTiles(room))
                if (tile.InstanceID == id) return true;
        return false;
    }
}
