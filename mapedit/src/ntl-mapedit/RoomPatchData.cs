namespace NtlMapEdit;

/// <summary>
/// 房间差分的数据模型 —— 字段名/类型与 Neutraled builder 的 ModRoomPatch 一一对应
/// （builder/Mods.cs:100-216），生成的 mod.json 由部署器应用。
///
/// 语义：只带「相对原版基线的差异」。房间头字段为 null 表示不改；实例按 InstanceId 定位；
/// 原生瓦片逐格写入（0 = 擦除）；旧式（LegacyTiles）瓦片没有网格索引，只能按图层整层替换；
/// 图层可以增删（新增图层可带初始实例/瓦片/旧式瓦片）；房间可以改名（最后应用，重名会被拒绝）；
/// 新增房间走 rooms 段，可带初始图层内容（实例 / 原生瓦片 / 旧式瓦片）。
/// </summary>
public sealed class RoomPatch
{
    /// <summary>目标房间名（原版 data 里已有的房间；改名时这里是<b>旧名</b>）。</summary>
    public string Name { get; set; } = "";

    // ---- 房间头字段（null = 不改） ----
    public uint? Width { get; set; }
    public uint? Height { get; set; }
    public uint? Speed { get; set; }
    public bool? Persistent { get; set; }
    public int? Flags { get; set; }
    public long? BackgroundColor { get; set; }
    public bool? DrawBackgroundColor { get; set; }

    public List<RoomInstancePatch> InstancesAdd { get; set; } = new();
    public List<RoomInstancePatch> InstancesModify { get; set; } = new();
    public List<RoomInstanceRef> InstancesRemove { get; set; } = new();
    public List<RoomTileSet> TilesSet { get; set; } = new();

    /// <summary>房间改名（旧名 = <see cref="Name"/>，新名 = 这里）。最后应用；重名会被拒绝。</summary>
    public string? Rename { get; set; }

    /// <summary>新增图层（可带初始内容：实例 / 原生瓦片 / 旧式瓦片）；同名已存在则复用并警告。</summary>
    public List<RoomLayer> LayersAdd { get; set; } = new();

    /// <summary>删除图层（按名字；连同层内实例/旧式瓦片一起删；最后一个图层不删）。</summary>
    public List<string> LayersRemove { get; set; } = new();

    /// <summary>整层替换旧式（LegacyTiles）瓦片：先清空该层再按列表重建。</summary>
    public List<RoomLegacyTiles> LegacyTilesSet { get; set; } = new();

    /// <summary>
    /// 把这间房从房间顺序（GeneralInfo.RoomOrder）里摘除：保留房间资源与编号，
    /// 静态 room_goto(编号) 仍能进，但 room_goto_next/previous 的门链会跳过它。
    /// 物理删除原版房间不可行（房间编号 = ROOM 表下标，删中间房间会让其后编号全体 -1，
    /// 而原版 bytecode 里的房间常量是裸 int、不会被重写）——所以「删除房间」在产品里一律表达为这个字段。
    /// </summary>
    public bool? RemoveFromOrder { get; set; }

    public bool IsEmpty => Width == null && Height == null && Speed == null && Persistent == null
        && Flags == null && BackgroundColor == null && DrawBackgroundColor == null
        && InstancesAdd.Count == 0 && InstancesModify.Count == 0 && InstancesRemove.Count == 0
        && TilesSet.Count == 0 && string.IsNullOrWhiteSpace(Rename)
        && LayersAdd.Count == 0 && LayersRemove.Count == 0 && LegacyTilesSet.Count == 0
        && RemoveFromOrder != true;
}

/// <summary>房间实例字段集合：add 时全字段写入，modify 时只写改动字段。</summary>
public sealed class RoomInstancePatch
{
    public uint InstanceId { get; set; }
    /// <summary>对象名（add 必填；modify 时可换对象）。</summary>
    public string? Object { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
    /// <summary>所在实例图层名（缺省第一层）。</summary>
    public string? Layer { get; set; }
    public float? ScaleX { get; set; }
    public float? ScaleY { get; set; }
    public float? Rotation { get; set; }
    public uint? Color { get; set; }
    public int? ImageIndex { get; set; }
    public float? ImageSpeed { get; set; }
    public bool? Nonexistent { get; set; }
}

/// <summary>按 InstanceId 引用一个已有实例。</summary>
public sealed class RoomInstanceRef
{
    public uint InstanceId { get; set; }
}

/// <summary>一组瓦片写入（同一图层）。</summary>
public sealed class RoomTileSet
{
    public string? Layer { get; set; }
    public List<RoomTileCell> Cells { get; set; } = new();
}

/// <summary>一个瓦片格：Value 低 28 位 tileId、高 4 位 flags（与 UTMT 房间编辑器一致，0=空）。</summary>
public sealed class RoomTileCell
{
    public int X { get; set; }
    public int Y { get; set; }
    public uint Value { get; set; }
}

/// <summary>一个图层规格（新增图层 / 新房间初始图层共用；对应 builder 的 ModRoomLayer）。
/// type = instances（实例层）/ tiles（原生瓦片层）/ assets（旧式瓦片层）；内容按 type 取对应字段。</summary>
public sealed class RoomLayer
{
    public string Name { get; set; } = "";
    /// <summary>instances / tiles / assets。</summary>
    public string Type { get; set; } = "instances";
    public int Depth { get; set; }
    public bool? Visible { get; set; }
    /// <summary>tiles 层用的 tileset（background 资源名）；缺省自动挑一个可用的。</summary>
    public string? Tileset { get; set; }
    /// <summary>instances 层的初始实例。</summary>
    public List<RoomInstancePatch> Instances { get; set; } = new();
    /// <summary>tiles 层的初始原生瓦片格（0 = 空）。</summary>
    public List<RoomTileCell> Tiles { get; set; } = new();
    /// <summary>assets 层的初始旧式瓦片。</summary>
    public List<RoomTileDef> LegacyTiles { get; set; } = new();
}

/// <summary>旧式瓦片（UndertaleRoom.Tile）的可移植字段：图源与几何，不含对象引用。
/// Sprite / Background 二选一；InstanceId 保留以便差分/回灌对齐（冲突时由部署器另分配）。</summary>
public sealed class RoomTileDef
{
    public int? InstanceId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public string? Sprite { get; set; }
    public string? Background { get; set; }
    public int SourceX { get; set; }
    public int SourceY { get; set; }
    public uint Width { get; set; } = 16;
    public uint Height { get; set; } = 16;
    public float? ScaleX { get; set; }
    public float? ScaleY { get; set; }
    public uint? Color { get; set; }
}

/// <summary>整层旧式瓦片替换（旧式瓦片没有网格索引，只能按层整体重建）。</summary>
public sealed class RoomLegacyTiles
{
    public string? Layer { get; set; }
    public List<RoomTileDef> Tiles { get; set; } = new();
}

/// <summary>新增房间（写入 mod.json 的 rooms 段；头字段 + 初始图层/实例/瓦片）。</summary>
public sealed class NewRoomDef
{
    public string Name { get; set; } = "";
    public uint Width { get; set; } = 640;
    public uint Height { get; set; } = 480;
    public uint Speed { get; set; } = 30;
    public bool Persistent { get; set; }
    public int Flags { get; set; }
    public long BackgroundColor { get; set; }
    public bool DrawBackgroundColor { get; set; } = true;
    /// <summary>新房间的初始图层（含实例 / 原生瓦片 / 旧式瓦片）；空 = 沿用模板房间的图层结构。</summary>
    public List<RoomLayer> Layers { get; set; } = new();
}
