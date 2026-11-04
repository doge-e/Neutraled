namespace NtlMapEdit;

/// <summary>
/// 房间差分的数据模型 —— 字段名/类型与 Neutraled builder 的 ModRoomPatch 一一对应
/// （builder/Mods.cs:78-159），生成的 mod.json 由部署器应用。
///
/// 语义：只带「相对原版基线的差异」。房间头字段为 null 表示不改；实例按 InstanceId 定位；
/// 原生瓦片逐格写入（0 = 擦除）。新增房间走 rooms 段（只支持空房间，与 builder 一致）。
/// </summary>
public sealed class RoomPatch
{
    /// <summary>目标房间名（原版 data 里已有的房间）。</summary>
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

    public bool IsEmpty => Width == null && Height == null && Speed == null && Persistent == null
        && Flags == null && BackgroundColor == null && DrawBackgroundColor == null
        && InstancesAdd.Count == 0 && InstancesModify.Count == 0 && InstancesRemove.Count == 0
        && TilesSet.Count == 0;
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

/// <summary>新增房间（写入 mod.json 的 rooms 段；只表达房间头，不含实例/瓦片）。</summary>
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
}
