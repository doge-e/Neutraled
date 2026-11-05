using UndertaleModLib;
using UndertaleModLib.Models;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>
/// 应用 mod.json 的 room_patches 段（见 <see cref="ModRoomPatch"/>）：在「新增房间」(2.7) 之后、
/// 资源名守卫 (2.8) 之前，把地图编辑器保存的「相对原版基线的房间差分」注入产物。
/// 设计原则：只改差异、按名字/ID 定位、找不到就响亮警告（不静默吞掉）。
///
/// 支持的范围：房间头字段 / 实例增删改 / 原生瓦片逐格 / 房间改名 / 图层增删（可带初始内容）/
/// 旧式（LegacyTiles）瓦片整层替换 / 从房间顺序摘除（删除原版房间的安全表达）。
/// 新房间的初始内容走 <see cref="FillRoom"/>（mod.json 的 rooms 段
/// 带 layers），这样「新增房间 + 摆实例 + 画瓦片」也能完全用 mod 表达，不必改产物 data.win。
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
                if (p.RemoveFromOrder == true)
                {
                    var order = data.GeneralInfo?.RoomOrder;
                    bool removed = false;
                    if (order != null)
                        for (int i = order.Count - 1; i >= 0; i--)
                            if (ReferenceEquals(order[i]?.Resource, room)) { order.RemoveAt(i); removed = true; }
                    if (removed)
                    {
                        n++;
                        Paths.Log(L("    -房间 {0}: 已从房间顺序摘除（保留房间资源与编号；门链 room_goto_next 会跳过它）", p.Name));
                    }
                    else Paths.Log(L("    [注意] {0}: 房间 {1} 本来就不在房间顺序里（摘除无效果）", m.Id, p.Name));
                }
                changed += n;
                if (n > 0) Paths.Log(L("    ~房间 {0}: {1} 处改动", p.Name, n));
            }
        }
        if (total > 0)
            Paths.Log(L("  房间差分合计: 声明 {0} 条 / 变更 {1} 处 / 异常 {2}", total, changed, bad));
    }

    /// <summary>给「新增房间」铺初始图层/实例/瓦片（在 2.7 建好房间之后调用）。</summary>
    public static int FillRoom(UndertaleData data, ModEntry m, UndertaleRoom room, ModRoom rd)
    {
        int n = 0;
        foreach (var spec in rd.Layers)
        {
            var layer = EnsureLayer(data, m, room, spec, "新房间 " + rd.Name);
            if (layer == null) continue;
            n++;
        }
        return n;
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

        // ---- 删除图层（先删，再建；同名的「删了又加」也能表达） ----
        foreach (var ln in p.LayersRemove)
        {
            if (string.IsNullOrWhiteSpace(ln)) continue;
            var layer = FindLayer(room, ln);
            if (layer == null)
            {
                Paths.Log(L("    [警告] {0}: 要删的图层不存在于 {1}: {2}", m.Id, p.Name, ln));
                continue;
            }
            if (!RemoveLayer(room, layer))
            {
                Paths.Log(L("    [警告] {0}: 房间 {1} 只剩一个图层，不能删 {2}", m.Id, p.Name, ln));
                continue;
            }
            n++;
        }

        // ---- 新增图层（可带实例 / 原生瓦片 / 旧式瓦片初始内容） ----
        foreach (var spec in p.LayersAdd)
        {
            if (string.IsNullOrWhiteSpace(spec.Name))
            {
                Paths.Log(L("    [警告] {0}: 房间 {1} 的新增图层缺 name，跳过", m.Id, p.Name));
                continue;
            }
            if (EnsureLayer(data, m, room, spec, p.Name) != null) n++;
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
            if (AddInstance(data, m, room, ai, p.Name)) n++;
        }

        // ---- 旧式瓦片整层替换 ----
        foreach (var lt in p.LegacyTilesSet)
        {
            UndertaleRoom.Layer? layer;
            if (!string.IsNullOrWhiteSpace(lt.Layer))
                layer = FindLayer(room, lt.Layer!);
            else
                layer = room.Layers?.FirstOrDefault(l => l.AssetsData?.LegacyTiles != null);
            if (layer?.AssetsData?.LegacyTiles == null)
            {
                Paths.Log(L("    [警告] {0}: 房间里找不到旧式瓦片层: {1}（房间 {2}）", m.Id, lt.Layer ?? "", p.Name));
                continue;
            }
            n += ReplaceLegacyTiles(data, m, room, layer, lt.Tiles, p.Name);
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
            n += WriteTileCells(m, room, layer, ts.Cells, p.Name);
        }

        // ---- 改名（最后做：上面所有操作都按旧名定位） ----
        if (!string.IsNullOrWhiteSpace(p.Rename) && !string.Equals(p.Rename, p.Name, StringComparison.Ordinal))
        {
            var newName = p.Rename!.Trim();
            if (data.Rooms.Any(r => r != null && !ReferenceEquals(r, room) &&
                    string.Equals(r.Name?.Content, newName, StringComparison.Ordinal)))
            {
                Paths.Log(L("    [警告] {0}: 房间改名目标已存在: {1} -> {2}（跳过改名）", m.Id, p.Name, newName));
            }
            else
            {
                room.Name = data.Strings.MakeString(newName);
                Paths.Log(L("    ~房间改名: {0} -> {1}（GML 里按名字引用该房间的地方会失效，按数字索引不受影响）", p.Name, newName));
                n++;
            }
        }

        return n;
    }

    // ------------------------------------------------------------------ 图层

    /// <summary>按规格取图层：同名已存在则复用（内容叠加上去）；否则新建（instances/tiles/assets）。</summary>
    private static UndertaleRoom.Layer? EnsureLayer(UndertaleData data, ModEntry m, UndertaleRoom room,
        ModRoomLayer spec, string roomName)
    {
        var type = (spec.Type ?? "").Trim().ToLowerInvariant();
        if (type.Length == 0) type = "instances";
        var existing = FindLayer(room, spec.Name);
        UndertaleRoom.Layer layer;
        if (existing != null)
        {
            Paths.Log(L("    [注意] {0}: 房间 {1} 已有图层 {2}，内容叠加到既有层（不重复建层）", m.Id, roomName, spec.Name));
            layer = existing;
        }
        else
        {
            if (room.Layers == null)
            {
                Paths.Log(L("    [警告] {0}: 房间 {1} 没有图层表（旧格式房间？），无法新增图层 {2}", m.Id, roomName, spec.Name));
                return null;
            }
            string unique = UniqueLayerName(room, spec.Name);
            uint layerId = NextLayerId(room);
            switch (type)
            {
                case "instances":
                    layer = new UndertaleRoom.Layer
                    {
                        LayerName = data.Strings.MakeString(unique),
                        LayerId = layerId,
                        LayerType = UndertaleRoom.LayerType.Instances,
                        LayerDepth = spec.Depth,
                        IsVisible = spec.Visible ?? true,
                        Data = new UndertaleRoom.Layer.LayerInstancesData(),
                    };
                    break;
                case "tiles":
                {
                    var bg = PickTileset(data, room, spec.Tileset);
                    if (bg == null)
                    {
                        Paths.Log(L("    [警告] {0}: 房间 {1} 找不到可用的原生 tileset，瓦片层 {2} 跳过", m.Id, roomName, spec.Name));
                        return null;
                    }
                    int tw = bg.GMS2TileWidth > 0 ? (int)bg.GMS2TileWidth : 16;
                    int th = bg.GMS2TileHeight > 0 ? (int)bg.GMS2TileHeight : 16;
                    int cols = Math.Max(1, (int)Math.Ceiling(room.Width / (double)tw));
                    int rows = Math.Max(1, (int)Math.Ceiling(room.Height / (double)th));
                    var cellData = new uint[rows][];
                    for (int y = 0; y < rows; y++) cellData[y] = new uint[cols];
                    layer = new UndertaleRoom.Layer
                    {
                        LayerName = data.Strings.MakeString(unique),
                        LayerId = layerId,
                        LayerType = UndertaleRoom.LayerType.Tiles,
                        LayerDepth = spec.Depth,
                        IsVisible = spec.Visible ?? true,
                        Data = new UndertaleRoom.Layer.LayerTilesData
                        {
                            Background = bg,
                            TileData = cellData,
                            TilesX = (uint)cols,
                            TilesY = (uint)rows,
                        },
                    };
                    Paths.Log(L("    +瓦片层 {0}（tileset {1}，{2}x{3} 格）", unique, bg.Name?.Content ?? "?", cols, rows));
                    break;
                }
                case "assets":
                    layer = new UndertaleRoom.Layer
                    {
                        LayerName = data.Strings.MakeString(unique),
                        LayerId = layerId,
                        LayerType = UndertaleRoom.LayerType.Assets,
                        LayerDepth = spec.Depth,
                        IsVisible = spec.Visible ?? true,
                        Data = NewAssetsData(),
                    };
                    break;
                default:
                    Paths.Log(L("    [警告] {0}: 未知图层类型 {1}（房间 {2} 图层 {3} 跳过）", m.Id, spec.Type, roomName, spec.Name));
                    return null;
            }
            layer.ParentRoom = room;
            room.Layers.Add(layer);
            room.RearrangeLayers();
            room.UpdateBGColorLayer();
        }

        // ---- 初始内容 ----
        if (spec.Instances.Count > 0)
        {
            if (layer.InstancesData?.Instances == null)
                Paths.Log(L("    [警告] {0}: 图层 {1} 不是实例图层，{2} 个实例跳过", m.Id, spec.Name, spec.Instances.Count));
            else
                foreach (var ai in spec.Instances) AddInstance(data, m, room, ai, roomName, layer);
        }
        if (spec.Tiles.Count > 0)
        {
            if (layer.TilesData?.TileData == null)
                Paths.Log(L("    [警告] {0}: 图层 {1} 不是原生瓦片层，{2} 格瓦片跳过", m.Id, spec.Name, spec.Tiles.Count));
            else
                WriteTileCells(m, room, layer, spec.Tiles, roomName);
        }
        if (spec.LegacyTiles.Count > 0)
        {
            if (layer.AssetsData?.LegacyTiles == null)
                Paths.Log(L("    [警告] {0}: 图层 {1} 不是旧式瓦片层，{2} 个瓦片跳过", m.Id, spec.Name, spec.LegacyTiles.Count));
            else
                foreach (var td in spec.LegacyTiles) AddLegacyTile(data, m, layer, td, roomName);
        }
        return layer;
    }

    private static UndertaleRoom.Layer.LayerAssetsData NewAssetsData() => new()
    {
        LegacyTiles = new UndertalePointerList<UndertaleRoom.Tile>(),
        Sprites = new UndertalePointerList<UndertaleRoom.SpriteInstance>(),
        Sequences = new UndertalePointerList<UndertaleRoom.SequenceInstance>(),
        NineSlices = new UndertalePointerList<UndertaleRoom.SpriteInstance>(),
        ParticleSystems = new UndertalePointerList<UndertaleRoom.ParticleSystemInstance>(),
        TextItems = new UndertalePointerList<UndertaleRoom.TextItemInstance>(),
    };

    private static UndertaleRoom.Layer? FindLayer(UndertaleRoom room, string name)
    {
        if (room.Layers == null) return null;
        foreach (var l in room.Layers)
            if (string.Equals(l.LayerName?.Content, name, StringComparison.Ordinal)) return l;
        foreach (var l in room.Layers)
            if (string.Equals(l.LayerName?.Content, name, StringComparison.OrdinalIgnoreCase)) return l;
        return null;
    }

    private static uint NextLayerId(UndertaleRoom room)
    {
        uint max = 0;
        if (room.Layers != null)
            foreach (var l in room.Layers)
                if (l.LayerId > max) max = l.LayerId;
        return max + 1;
    }

    private static string UniqueLayerName(UndertaleRoom room, string baseName)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (room.Layers != null)
            foreach (var l in room.Layers) used.Add(l.LayerName?.Content ?? "");
        string name = baseName;
        for (int i = 2; used.Contains(name); i++) name = baseName + "_" + i;
        return name;
    }

    private static bool RemoveLayer(UndertaleRoom room, UndertaleRoom.Layer layer)
    {
        if (room?.Layers == null || layer == null || room.Layers.Count <= 1 || !room.Layers.Contains(layer))
            return false;
        if (layer.InstancesData?.Instances != null)
            foreach (var inst in layer.InstancesData.Instances.ToList()) RemoveInstance(room, inst);
        if (layer.AssetsData?.LegacyTiles != null && room.Tiles != null)
            foreach (var t in layer.AssetsData.LegacyTiles.ToList()) room.Tiles.Remove(t);
        room.Layers.Remove(layer);
        layer.ParentRoom = null;
        room.UpdateBGColorLayer();
        return true;
    }

    /// <summary>挑一个可用的 tileset：显式名字 &gt; 本房间已有原生层 &gt; 任意房间 &gt; data.win 里第一个带图的。</summary>
    private static UndertaleBackground? PickTileset(UndertaleData data, UndertaleRoom room, string? preferred)
    {
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            var bg = data.Backgrounds.ByName(preferred!);
            if (bg?.Texture != null && bg.GMS2TileWidth > 0 && bg.GMS2TileHeight > 0) return bg;
            Paths.Log(L("    [警告] tileset 不存在或不可用: {0}（改为自动挑选）", preferred));
        }
        if (room.Layers != null)
            foreach (var l in room.Layers)
            {
                var bg = l.TilesData?.Background;
                if (bg?.Texture != null && bg.GMS2TileWidth > 0 && bg.GMS2TileHeight > 0) return bg;
            }
        foreach (var r in data.Rooms)
        {
            if (ReferenceEquals(r, room) || r.Layers == null) continue;
            foreach (var l in r.Layers)
            {
                var bg = l.TilesData?.Background;
                if (bg?.Texture != null && bg.GMS2TileWidth > 0 && bg.GMS2TileHeight > 0) return bg;
            }
        }
        if (data.Backgrounds != null)
            foreach (var bg in data.Backgrounds)
                if (bg.Texture != null && bg.GMS2TileWidth > 0 && bg.GMS2TileHeight > 0) return bg;
        return null;
    }

    // ------------------------------------------------------------------ 实例

    private static bool AddInstance(UndertaleData data, ModEntry m, UndertaleRoom room,
        ModRoomInstance ai, string roomName, UndertaleRoom.Layer? forcedLayer = null)
    {
        if (string.IsNullOrWhiteSpace(ai.Object))
        {
            Paths.Log(L("    [警告] {0}: 新增实例缺 object（房间 {1}）", m.Id, roomName));
            return false;
        }
        var obj = data.GameObjects.ByName(ai.Object!);
        if (obj == null)
        {
            Paths.Log(L("    [警告] {0}: 新增实例的对象不存在: {1}（房间 {2}）", m.Id, ai.Object, roomName));
            return false;
        }
        var layer = forcedLayer ?? (string.IsNullOrWhiteSpace(ai.Layer) ? FirstInstancesLayer(room) : FindInstancesLayer(room, ai.Layer!));
        if (layer?.InstancesData?.Instances == null)
        {
            Paths.Log(L("    [警告] {0}: 房间 {1} 没有可用的实例图层（新增实例跳过）", m.Id, roomName));
            return false;
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
        return true;
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

    // ------------------------------------------------------------ 瓦片写入

    private static int WriteTileCells(ModEntry m, UndertaleRoom room, UndertaleRoom.Layer layer,
        IReadOnlyList<ModRoomTileCell> cells, string roomName)
    {
        int set = 0, oob = 0;
        var rows = layer.TilesData!.TileData!;
        foreach (var c in cells)
        {
            if (c.Y < 0 || c.Y >= rows.Length || rows[c.Y] == null || c.X < 0 || c.X >= rows[c.Y].Length)
            {
                oob++;
                continue;
            }
            if (rows[c.Y][c.X] != c.Value) { rows[c.Y][c.X] = c.Value; set++; }
        }
        if (set > 0) layer.TilesData.TileDataUpdated();
        if (oob > 0)
            Paths.Log(L("    [警告] {0}: 房间 {1} 瓦片越界 {2} 格（已忽略）", m.Id, roomName, oob));
        return set;
    }

    private static int ReplaceLegacyTiles(UndertaleData data, ModEntry m, UndertaleRoom room,
        UndertaleRoom.Layer layer, IReadOnlyList<ModRoomTileDef> tiles, string roomName)
    {
        var list = layer.AssetsData!.LegacyTiles!;
        var old = list.ToList();
        list.Clear();
        if (room.Tiles != null)
            foreach (var t in old) room.Tiles.Remove(t);
        int n = old.Count;   // 清空也算改动（回到空层）
        foreach (var td in tiles)
            if (AddLegacyTile(data, m, layer, td, roomName)) n++;
        return n;
    }

    private static bool AddLegacyTile(UndertaleData data, ModEntry m, UndertaleRoom.Layer layer,
        ModRoomTileDef td, string roomName)
    {
        UndertaleSprite? spr = null;
        UndertaleBackground? bg = null;
        if (!string.IsNullOrWhiteSpace(td.Sprite))
        {
            spr = data.Sprites.ByName(td.Sprite!);
            if (spr == null) Paths.Log(L("    [警告] {0}: 旧式瓦片图源不存在: {1}（房间 {2}）", m.Id, td.Sprite, roomName));
        }
        if (spr == null && !string.IsNullOrWhiteSpace(td.Background))
        {
            bg = data.Backgrounds.ByName(td.Background!);
            if (bg == null) Paths.Log(L("    [警告] {0}: 旧式瓦片图源不存在: {1}（房间 {2}）", m.Id, td.Background, roomName));
        }
        if (spr == null && bg == null) return false;
        uint id = 0;
        if (td.InstanceId.HasValue && td.InstanceId.Value > 0 && !TileIdUsed(data, (uint)td.InstanceId.Value))
            id = (uint)td.InstanceId.Value;
        else
            id = NextTileId(data);
        var tile = new UndertaleRoom.Tile
        {
            spriteMode = spr != null && data.IsGameMaker2(),
            SpriteDefinition = spr,
            BackgroundDefinition = bg,
            X = td.X,
            Y = td.Y,
            SourceX = td.SourceX,
            SourceY = td.SourceY,
            Width = td.Width > 0 ? td.Width : 16,
            Height = td.Height > 0 ? td.Height : 16,
            ScaleX = td.ScaleX ?? 1f,
            ScaleY = td.ScaleY ?? 1f,
            Color = td.Color ?? 0xFFFFFFFF,
            TileDepth = layer.LayerDepth,
            InstanceID = id,
        };
        layer.AssetsData!.LegacyTiles!.Add(tile);
        return true;
    }

    private static bool TileIdUsed(UndertaleData data, uint id)
    {
        foreach (var room in data.Rooms)
            foreach (var (_, t) in AllTiles(room))
                if (t.InstanceID == id) return true;
        return false;
    }

    private static uint NextTileId(UndertaleData data)
    {
        uint max = 0;
        foreach (var room in data.Rooms)
            foreach (var (_, t) in AllTiles(room))
                if (t.InstanceID > max) max = t.InstanceID;
        return max == uint.MaxValue ? max : max + 1;
    }

    private static IEnumerable<(UndertaleRoom.Layer Layer, UndertaleRoom.Tile Tile)> AllTiles(UndertaleRoom room)
    {
        if (room.Layers == null) yield break;
        foreach (var l in room.Layers)
        {
            var list = l.AssetsData?.LegacyTiles;
            if (list == null) continue;
            foreach (var t in list) yield return (l, t);
        }
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
