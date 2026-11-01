using UndertaleModLib;
using UndertaleModLib.Models;

namespace NtlMapEdit;

/// <summary>
/// Mutating operations on rooms. Kept in one place so the CLI self-tests and the GUI
/// share the exact same semantics.
/// </summary>
public static class RoomEdit
{
    public static IEnumerable<(UndertaleRoom.Layer Layer, UndertaleRoom.GameObject Inst)> AllInstances(UndertaleRoom room)
    {
        if (room.Layers == null) yield break;
        foreach (var l in room.Layers)
        {
            if (l.InstancesData?.Instances == null) continue;
            foreach (var i in l.InstancesData.Instances) yield return (l, i);
        }
    }

    public static uint NextInstanceId(UndertaleData data)
    {
        uint max = 100000;
        foreach (var room in data.Rooms)
        {
            foreach (var (_, inst) in AllInstances(room))
                if (inst.InstanceID > max) max = inst.InstanceID;
            if (room.GameObjects != null)
                foreach (var inst in room.GameObjects)
                    if (inst.InstanceID > max) max = inst.InstanceID;
        }
        return max + 1;
    }

    public static UndertaleRoom.Layer? InstancesLayer(UndertaleRoom room)
        => room.Layers?.FirstOrDefault(l => l.InstancesData?.Instances != null);

    public static UndertaleRoom.GameObject AddInstance(UndertaleData data, UndertaleRoom room, UndertaleRoom.Layer layer,
        UndertaleGameObject obj, int x, int y, uint instanceId = 0)
    {
        if (layer.InstancesData?.Instances == null) throw new InvalidOperationException("layer is not an instance layer");
        var inst = new UndertaleRoom.GameObject
        {
            ObjectDefinition = obj,
            X = x,
            Y = y,
            InstanceID = instanceId != 0 ? instanceId : NextInstanceId(data),
            ScaleX = 1f,
            ScaleY = 1f,
            Rotation = 0f,
            ImageIndex = 0,
            ImageSpeed = 0f,
            Color = 0xFFFFFFFF,
        };
        layer.InstancesData.Instances.Add(inst);
        if (room.GameObjects != null) room.GameObjects.Add(inst);
        if (room.InstanceCreationOrderIDs?.InstanceIDs != null)
            room.InstanceCreationOrderIDs.InstanceIDs.Add((int)inst.InstanceID);
        return inst;
    }

    /// <summary>
    /// 再制/粘贴：把 src 的完整属性（缩放、旋转、颜色、图帧、创建代码…）克隆进 layer，
    /// 换一个新 InstanceID 并整体偏移。和 <see cref="AddInstance"/> 的区别是后者只带对象与坐标。
    /// </summary>
    public static UndertaleRoom.GameObject AddInstanceCopy(UndertaleData data, UndertaleRoom room,
        UndertaleRoom.Layer layer, UndertaleRoom.GameObject src, int dx, int dy)
    {
        if (layer.InstancesData?.Instances == null) throw new InvalidOperationException("layer is not an instance layer");
        var inst = Clone(src);
        inst.X += dx;
        inst.Y += dy;
        inst.InstanceID = NextInstanceId(data);
        layer.InstancesData.Instances.Add(inst);
        if (room.GameObjects != null) room.GameObjects.Add(inst);
        if (room.InstanceCreationOrderIDs?.InstanceIDs != null)
            room.InstanceCreationOrderIDs.InstanceIDs.Add((int)inst.InstanceID);
        return inst;
    }

    public static bool RemoveInstance(UndertaleRoom room, UndertaleRoom.GameObject inst)
    {
        bool removed = false;
        if (room.Layers != null)
        {
            foreach (var l in room.Layers)
            {
                if (l.InstancesData?.Instances == null) continue;
                if (l.InstancesData.Instances.Remove(inst)) removed = true;
            }
        }
        if (room.GameObjects != null && room.GameObjects.Remove(inst)) removed = true;
        if (room.InstanceCreationOrderIDs?.InstanceIDs != null)
        {
            var ids = room.InstanceCreationOrderIDs.InstanceIDs;
            for (int i = ids.Count - 1; i >= 0; i--)
                if (ids[i] == (int)inst.InstanceID) ids.RemoveAt(i);
        }
        return removed;
    }

    public static UndertaleRoom.Layer? LayerOf(UndertaleRoom room, UndertaleRoom.GameObject inst)
    {
        if (room.Layers == null) return null;
        foreach (var l in room.Layers)
            if (l.InstancesData?.Instances != null && l.InstancesData.Instances.Contains(inst)) return l;
        return null;
    }

    public static UndertaleRoom.GameObject Clone(UndertaleRoom.GameObject s) => new()
    {
        X = s.X,
        Y = s.Y,
        ObjectDefinition = s.ObjectDefinition,
        InstanceID = s.InstanceID,
        CreationCode = s.CreationCode,
        ScaleX = s.ScaleX,
        ScaleY = s.ScaleY,
        Color = s.Color,
        Rotation = s.Rotation,
        PreCreateCode = s.PreCreateCode,
        ImageSpeed = s.ImageSpeed,
        ImageIndex = s.ImageIndex,
        Nonexistent = s.Nonexistent,
    };

    // ----------------------------------------------------------------- tiles

    public static IEnumerable<(UndertaleRoom.Layer Layer, UndertaleRoom.Tile Tile)> AllTiles(UndertaleRoom room)
    {
        if (room.Layers == null) yield break;
        foreach (var l in room.Layers)
        {
            var list = l.AssetsData?.LegacyTiles;
            if (list == null) continue;
            foreach (var t in list) yield return (l, t);
        }
    }

    public static UndertaleRoom.Layer? TilesLayer(UndertaleRoom room)
    {
        if (room.Layers == null) return null;
        foreach (var l in room.Layers)
            if (l.AssetsData?.LegacyTiles != null) return l;
        return null;
    }

    public static uint NextTileId(UndertaleData data)
    {
        uint max = 0;
        foreach (var room in data.Rooms)
            foreach (var (_, t) in AllTiles(room))
                if (t.InstanceID > max) max = t.InstanceID;
        return max + 1;
    }

    public static UndertaleRoom.Tile AddTile(UndertaleData data, UndertaleRoom room, UndertaleRoom.Layer layer,
        UndertaleRoom.Tile src, int x, int y)
    {
        if (layer.AssetsData?.LegacyTiles == null) throw new InvalidOperationException("layer is not an asset/tile layer");
        var t = CloneTile(src);
        t.X = x;
        t.Y = y;
        t.TileDepth = layer.LayerDepth;
        t.InstanceID = NextTileId(data);
        layer.AssetsData.LegacyTiles.Add(t);
        return t;
    }

    public static bool RemoveTile(UndertaleRoom room, UndertaleRoom.Tile tile)
    {
        if (room.Layers == null) return false;
        foreach (var l in room.Layers)
        {
            var list = l.AssetsData?.LegacyTiles;
            if (list != null && list.Remove(tile)) return true;
        }
        return false;
    }

    public static UndertaleRoom.Layer? LayerOfTile(UndertaleRoom room, UndertaleRoom.Tile tile)
    {
        if (room.Layers == null) return null;
        foreach (var l in room.Layers)
        {
            var list = l.AssetsData?.LegacyTiles;
            if (list != null && list.Contains(tile)) return l;
        }
        return null;
    }

    /// <summary>
    /// 用任意精灵资源造一个「瓦片笔刷」：整帧（或指定子矩形）作为一个图块。
    /// 新房间/新瓦片层没有现成图块可 Alt 拾取时，用它从零开始画。
    /// </summary>
    public static UndertaleRoom.Tile MakeTileBrush(UndertaleData data, UndertaleSprite spr,
        int sourceX = 0, int sourceY = 0, int width = 0, int height = 0)
    {
        var tpag = spr.Textures?.FirstOrDefault()?.Texture;
        if (width <= 0) width = (int)(tpag?.SourceWidth ?? 16);
        if (height <= 0) height = (int)(tpag?.SourceHeight ?? 16);
        var tile = new UndertaleRoom.Tile
        {
            spriteMode = data.IsGameMaker2(),
            SourceX = sourceX,
            SourceY = sourceY,
            Width = (uint)Math.Max(1, width),
            Height = (uint)Math.Max(1, height),
            ScaleX = 1,
            ScaleY = 1,
            Color = 0xFFFFFFFF,
        };
        tile.ObjectDefinition = spr; // setter 按 spriteMode 强转，必须先设好 spriteMode
        return tile;
    }

    /// <summary>
    /// 从精灵帧内切一块子矩形做瓦片笔刷（tileset 调色板的基础版：真实 tileset 是大图，
    /// 一格瓦片只是大图里 SourceX/SourceY 处的一个 Width×Height 小块）。
    /// 越界坐标/尺寸自动夹到帧内（最小 1×1）。
    /// </summary>
    public static UndertaleRoom.Tile MakeTileBrush(UndertaleData data, UndertaleSprite spr, Rectangle source)
    {
        var tpag = spr.Textures?.FirstOrDefault()?.Texture;
        int fw = Math.Max(1, (int)(tpag?.SourceWidth ?? 16));
        int fh = Math.Max(1, (int)(tpag?.SourceHeight ?? 16));
        int x = Math.Clamp(source.X, 0, fw - 1);
        int y = Math.Clamp(source.Y, 0, fh - 1);
        int w = Math.Clamp(source.Width, 1, fw - x);
        int h = Math.Clamp(source.Height, 1, fh - y);
        return MakeTileBrush(data, spr, x, y, w, h);
    }

    // ---------------------------------------------------------- tile palette
    // 调色板（Loenn tileset 面板的简化版）只是把大图按固定单元格切成网格：
    // 格子数学独立成纯函数，GUI/CLI 都能直接验证。

    /// <summary>按单元格尺寸把整帧切成网格后有多少列/行（不足一格按一格算）。</summary>
    public static Size TilePaletteGrid(int frameW, int frameH, int cellW, int cellH)
    {
        frameW = Math.Max(1, frameW);
        frameH = Math.Max(1, frameH);
        cellW = Math.Max(1, cellW);
        cellH = Math.Max(1, cellH);
        return new Size((frameW + cellW - 1) / cellW, (frameH + cellH - 1) / cellH);
    }

    /// <summary>
    /// 调色板第 (col,row) 格对应的帧内源矩形；越界返回 Rectangle.Empty，
    /// 最后一列/行按帧边裁齐（大图尺寸不是单元格整数倍时的尾格）。
    /// </summary>
    public static Rectangle TilePaletteCellRect(int frameW, int frameH, int cellW, int cellH, int col, int row)
    {
        frameW = Math.Max(1, frameW);
        frameH = Math.Max(1, frameH);
        cellW = Math.Max(1, cellW);
        cellH = Math.Max(1, cellH);
        if (col < 0 || row < 0) return Rectangle.Empty;
        int x = col * cellW, y = row * cellH;
        if (x >= frameW || y >= frameH) return Rectangle.Empty;
        int w = Math.Min(cellW, frameW - x);
        int h = Math.Min(cellH, frameH - y);
        if (w < 1 || h < 1) return Rectangle.Empty;
        return new Rectangle(x, y, w, h);
    }

    /// <summary>帧内像素坐标 → 调色板格号（越界返回 (-1,-1)，GUI 命中测试用）。</summary>
    public static Point TilePaletteCellAt(int frameW, int frameH, int cellW, int cellH, int px, int py)
    {
        frameW = Math.Max(1, frameW);
        frameH = Math.Max(1, frameH);
        cellW = Math.Max(1, cellW);
        cellH = Math.Max(1, cellH);
        if (px < 0 || py < 0 || px >= frameW || py >= frameH) return new Point(-1, -1);
        return new Point(px / cellW, py / cellH);
    }

    /// <summary>矩形范围内的图块（按图块起点判定，含边界）。</summary>
    public static List<UndertaleRoom.Tile> TilesInRect(UndertaleRoom.Layer layer, RectangleF world)
    {
        var hits = new List<UndertaleRoom.Tile>();
        var list = layer.AssetsData?.LegacyTiles;
        if (list == null) return hits;
        foreach (var t in list)
            if (world.Contains(t.X, t.Y)) hits.Add(t);
        return hits;
    }

    /// <summary>图层内的全部图块（拷贝成新列表，调用方可安全遍历）。</summary>
    public static List<UndertaleRoom.Tile> AllTilesInLayer(UndertaleRoom.Layer layer)
        => layer.AssetsData?.LegacyTiles != null ? new List<UndertaleRoom.Tile>(layer.AssetsData.LegacyTiles) : new List<UndertaleRoom.Tile>();

    /// <summary>按引用删除一批图块，返回删除数。</summary>
    public static int RemoveTiles(UndertaleRoom.Layer layer, IEnumerable<UndertaleRoom.Tile> tiles)
    {
        var list = layer.AssetsData?.LegacyTiles;
        if (list == null) return 0;
        var set = new HashSet<UndertaleRoom.Tile>(tiles);
        if (set.Count == 0) return 0;
        int removed = 0;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (!set.Contains(list[i])) continue;
            list.RemoveAt(i);
            removed++;
        }
        return removed;
    }

    public static UndertaleRoom.Tile CloneTile(UndertaleRoom.Tile s) => new()
    {
        X = s.X,
        Y = s.Y,
        // 顺序要紧：UTMT 的 ObjectDefinition setter 按 spriteMode 决定往哪个
        // 槽位强转写入，所以必须先复制 spriteMode，否则 GMS2 的精灵图块会被
        // 当成 background 强转（InvalidCastException）。两个 Definition 直接
        // 各写各的更稳（更不必走 ObjectDefinition）。
        spriteMode = s.spriteMode,
        SpriteDefinition = s.SpriteDefinition,
        BackgroundDefinition = s.BackgroundDefinition,
        SourceX = s.SourceX,
        SourceY = s.SourceY,
        Width = s.Width,
        Height = s.Height,
        TileDepth = s.TileDepth,
        InstanceID = s.InstanceID,
        ScaleX = s.ScaleX,
        ScaleY = s.ScaleY,
        Color = s.Color,
    };

    // -------------------------------------------------------------- tile paint
    // Loenn 式瓦片工具（连续拖拽 / 矩形 / 直线 / 油漆桶）共用的纯函数。
    // GUI 只负责把鼠标轨迹翻译成格子坐标，语义全在这里，CLI 可以直接验证。

    /// <summary>图块占格尺寸（含缩放，至少 1px）。</summary>
    public static SizeF TileWorldSize(UndertaleRoom.Tile t)
        => new(Math.Max(1f, Math.Abs(t.Width * t.ScaleX)), Math.Max(1f, Math.Abs(t.Height * t.ScaleY)));

    /// <summary>同图源判据：同 tileset 引用 + 同源矩形 + 同尺寸（油漆桶连通、覆盖判重用）。</summary>
    public static bool SameTileSource(UndertaleRoom.Tile? a, UndertaleRoom.Tile? b)
        => a != null && b != null &&
           ReferenceEquals(a.ObjectDefinition, b.ObjectDefinition) &&
           a.SourceX == b.SourceX && a.SourceY == b.SourceY &&
           a.Width == b.Width && a.Height == b.Height;

    /// <summary>把图源搬到 target，保留 target 的坐标/图层/ID（刷子覆盖旧图块用）。</summary>
    public static void ApplyTileSource(UndertaleRoom.Tile target, UndertaleRoom.Tile src)
    {
        // 顺序要紧：ObjectDefinition setter 按 spriteMode 强转（同 CloneTile 的注释）
        target.spriteMode = src.spriteMode;
        target.SpriteDefinition = src.SpriteDefinition;
        target.BackgroundDefinition = src.BackgroundDefinition;
        target.SourceX = src.SourceX;
        target.SourceY = src.SourceY;
        target.Width = src.Width;
        target.Height = src.Height;
        target.ScaleX = src.ScaleX;
        target.ScaleY = src.ScaleY;
        target.Color = src.Color;
    }

    /// <summary>把世界坐标吸附成 brush 的格子左上角。</summary>
    public static Point SnapToTileCell(UndertaleRoom.Tile brush, PointF world)
        => SnapToTileCell(TileWorldSize(brush), world);

    public static Point SnapToTileCell(SizeF cell, PointF world)
    {
        int cw = CellSize(cell.Width);
        int ch = CellSize(cell.Height);
        return new Point((int)Math.Floor(world.X / cw) * cw, (int)Math.Floor(world.Y / ch) * ch);
    }

    private static int CellSize(float v) => Math.Max(1, (int)Math.Round(v));

    /// <summary>矩形包含的格子（含端点，左上角坐标）。</summary>
    public static List<Point> TileCellRect(Point a, Point b, SizeF cell)
    {
        int cw = CellSize(cell.Width), ch = CellSize(cell.Height);
        int x0 = Math.Min(a.X, b.X), x1 = Math.Max(a.X, b.X);
        int y0 = Math.Min(a.Y, b.Y), y1 = Math.Max(a.Y, b.Y);
        var list = new List<Point>();
        for (int y = y0; y <= y1; y += ch)
            for (int x = x0; x <= x1; x += cw)
                list.Add(new Point(x, y));
        return list;
    }

    /// <summary>直线经过的格子（Bresenham，含端点）。</summary>
    public static List<Point> TileCellLine(Point a, Point b, SizeF cell)
    {
        int cw = CellSize(cell.Width), ch = CellSize(cell.Height);
        int x0 = a.X / cw, y0 = a.Y / ch, x1 = b.X / cw, y1 = b.Y / ch;
        var list = new List<Point>();
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            list.Add(new Point(x0 * cw, y0 * ch));
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
        return list;
    }

    /// <summary>
    /// 圆形包含的格子：center 是圆心格、edge 是半径端点格（半径取两轴差值较大者），实心填充。
    /// 返回格子的左上角世界坐标（与矩形/直线一致）。
    /// </summary>
    public static List<Point> TileCellCircle(Point center, Point edge, SizeF cell)
    {
        int cw = CellSize(cell.Width), ch = CellSize(cell.Height);
        int cx = center.X / cw, cy = center.Y / ch;
        int ex = edge.X / cw, ey = edge.Y / ch;
        int r = Math.Max(Math.Abs(ex - cx), Math.Abs(ey - cy));
        return EllipseCells(cx - r, cy - r, cx + r, cy + r, cw, ch);
    }

    /// <summary>椭圆包含的格子：a/b 是包围盒的两个对角格（任意方向），实心填充。</summary>
    public static List<Point> TileCellEllipse(Point a, Point b, SizeF cell)
    {
        int cw = CellSize(cell.Width), ch = CellSize(cell.Height);
        int ax = a.X / cw, ay = a.Y / ch, bx = b.X / cw, by = b.Y / ch;
        return EllipseCells(Math.Min(ax, bx), Math.Min(ay, by), Math.Max(ax, bx), Math.Max(ay, by), cw, ch);
    }

    /// <summary>格坐标包围盒内的实心椭圆点集（行优先）。退化成一点/一条线时安全。</summary>
    private static List<Point> EllipseCells(int x0, int y0, int x1, int y1, int cw, int ch)
    {
        var list = new List<Point>();
        double cx = (x0 + x1) / 2.0, cy = (y0 + y1) / 2.0;
        double rx = (x1 - x0) / 2.0, ry = (y1 - y0) / 2.0;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                // 半径为 0 的轴：只有正中那一列/行落在椭圆内，其余视为无穷远（画不出来）
                double nx = rx <= 0 ? (x == x0 ? 0 : double.PositiveInfinity) : (x - cx) / rx;
                double ny = ry <= 0 ? (y == y0 ? 0 : double.PositiveInfinity) : (y - cy) / ry;
                if (nx * nx + ny * ny <= 1.0 + 1e-9) list.Add(new Point(x * cw, y * ch));
            }
        return list;
    }

    /// <summary>该层最后加入（最上层）且覆盖 world 点的图块，没有返回 null。</summary>
    public static UndertaleRoom.Tile? TileAtPoint(UndertaleRoom.Layer layer, PointF world)
    {
        var list = layer.AssetsData?.LegacyTiles;
        if (list == null) return null;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var t = list[i];
            var s = TileWorldSize(t);
            if (world.X >= t.X && world.X < t.X + s.Width &&
                world.Y >= t.Y && world.Y < t.Y + s.Height) return t;
        }
        return null;
    }

    /// <summary>在格子 (x,y) 放一格 brush；该位置已有同尺寸图块则改写图源而不叠加。返回是否有变化。</summary>
    private static bool PaintTileCell(UndertaleRoom.Layer layer, UndertaleRoom.Tile brush, int x, int y, ref uint nextId)
    {
        var list = layer.AssetsData?.LegacyTiles;
        if (list == null) return false;
        var cell = TileWorldSize(brush);
        for (int i = 0; i < list.Count; i++)
        {
            var t = list[i];
            var s = TileWorldSize(t);
            if (t.X == x && t.Y == y &&
                Math.Abs(s.Width - cell.Width) < 0.5f && Math.Abs(s.Height - cell.Height) < 0.5f)
            {
                if (SameTileSource(t, brush)) return false;
                ApplyTileSource(t, brush);
                return true;
            }
        }
        var nt = CloneTile(brush);
        nt.X = x;
        nt.Y = y;
        nt.TileDepth = layer.LayerDepth;
        nt.InstanceID = nextId++;
        list.Add(nt);
        return true;
    }

    /// <summary>把一批格子刷成 brush（连续拖拽 / 矩形 / 直线共用）。返回变化格数。</summary>
    public static int PaintTileCells(UndertaleData data, UndertaleRoom.Layer layer, UndertaleRoom.Tile brush,
        IEnumerable<Point> cells)
    {
        if (layer.AssetsData?.LegacyTiles == null) return 0;
        uint nextId = NextTileId(data);   // 每笔只扫一次，避免逐格 O(rooms×tiles)
        int changed = 0;
        foreach (var c in cells)
            if (PaintTileCell(layer, brush, c.X, c.Y, ref nextId)) changed++;
        return changed;
    }

    /// <summary>擦掉起点落在这些格子里的图块。返回删除数。</summary>
    public static int EraseTileCells(UndertaleRoom.Layer layer, IEnumerable<Point> cells)
    {
        var list = layer.AssetsData?.LegacyTiles;
        if (list == null) return 0;
        var wanted = new HashSet<Point>(cells);
        int removed = 0;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var t = list[i];
            if (wanted.Contains(new Point(t.X, t.Y)))
            {
                list.RemoveAt(i);
                removed++;
            }
        }
        return removed;
    }

    /// <summary>
    /// 油漆桶：把 origin 所在图块的连通（同图源）区域全部换成 brush。
    /// 点在空白处则填满该连通空白区域。
    /// </summary>
    public static int FloodFillTiles(UndertaleData data, UndertaleRoom room, UndertaleRoom.Layer layer,
        UndertaleRoom.Tile brush, PointF origin)
    {
        var region = FloodRegion(room, layer, origin, TileWorldSize(brush), out _, out int cellW, out int cellH);
        if (region == null) return 0;
        uint nextId = NextTileId(data);
        int changed = 0;
        foreach (var c in region)
        {
            if (PaintTileCell(layer, brush, c.X * cellW, c.Y * cellH, ref nextId)) changed++;
        }
        return changed;
    }

    /// <summary>油漆桶擦除：把 origin 所在图块的连通区域整片删掉。返回删除数。</summary>
    public static int FloodEraseTiles(UndertaleRoom room, UndertaleRoom.Layer layer, PointF origin)
    {
        if (TileAtPoint(layer, origin) == null) return 0;
        var region = FloodRegion(room, layer, origin, new SizeF(8, 8), out _, out int cellW, out int cellH);
        if (region == null) return 0;
        var list = layer.AssetsData?.LegacyTiles;
        if (list == null) return 0;
        var seeds = new HashSet<Point>(region);
        int removed = 0;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var t = list[i];
            // 图块起点所在格命中区域即删（与 PaintTileCell 的格对齐一致）
            int col = (int)Math.Floor(t.X / (float)cellW);
            int row = (int)Math.Floor(t.Y / (float)cellH);
            if (seeds.Contains(new Point(col, row)))
            {
                list.RemoveAt(i);
                removed++;
            }
        }
        return removed;
    }

    /// <summary>
    /// 计算 origin 所在的连通格区域（同图源；空白则连通空白）。
    /// 返回格子索引坐标 (col,row)，与格子像素坐标的换算是 (col*cellW, row*cellH)。
    /// </summary>
    private static List<Point>? FloodRegion(UndertaleRoom room, UndertaleRoom.Layer layer, PointF origin,
        SizeF emptyCell, out UndertaleRoom.Tile? seed, out int cellW, out int cellH)
    {
        seed = null;
        cellW = 8;
        cellH = 8;
        var list = layer.AssetsData?.LegacyTiles;
        if (list == null) return null;
        seed = TileAtPoint(layer, origin);
        // 点在空白处时按刷子尺寸切格（否则会按 8x8 落下大图块、互相重叠）
        var size = seed != null ? TileWorldSize(seed) : emptyCell;
        cellW = CellSize(size.Width);
        cellH = CellSize(size.Height);
        int cols = Math.Max(1, (int)((room.Width + (uint)cellW - 1) / (uint)cellW));
        int rows = Math.Max(1, (int)((room.Height + (uint)cellH - 1) / (uint)cellH));
        if ((long)cols * rows > 2_000_000) return null;
        int sx = (int)Math.Floor(origin.X / cellW);
        int sy = (int)Math.Floor(origin.Y / cellH);
        if (sx < 0 || sy < 0 || sx >= cols || sy >= rows) return null;
        // 本地函数不能捕获 out 参数（CS1628）→ 先复制到局部量
        int cw = cellW, ch = cellH;
        var seedTile = seed;
        bool seedEmpty = seedTile == null;
        var seen = new bool[cols * rows];
        var queue = new Queue<Point>();
        var region = new List<Point>();
        queue.Enqueue(new Point(sx, sy));
        seen[sy * cols + sx] = true;
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            region.Add(c);
            void Try(int nx, int ny)
            {
                if (nx < 0 || ny < 0 || nx >= cols || ny >= rows) return;
                int idx = ny * cols + nx;
                if (seen[idx]) return;
                var world = new PointF(nx * cw + cw / 2f, ny * ch + ch / 2f);
                var t = TileAtPoint(layer, world);
                bool same = seedEmpty ? t == null : SameTileSource(t, seedTile);
                if (!same) return;
                seen[idx] = true;
                queue.Enqueue(new Point(nx, ny));
            }
            Try(c.X + 1, c.Y);
            Try(c.X - 1, c.Y);
            Try(c.X, c.Y + 1);
            Try(c.X, c.Y - 1);
        }
        return region;
    }

    // ------------------------------------------------- room / layer structure

    public static string UniqueRoomName(UndertaleData data, string baseName)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in data.Rooms) used.Add(GameData.Name(r.Name));
        string name = baseName;
        for (int n = 2; used.Contains(name); n++) name = baseName + "_" + n;
        return name;
    }

    public static uint NextLayerId(UndertaleRoom room)
    {
        uint max = 0;
        if (room.Layers != null)
            foreach (var l in room.Layers)
                if (l.LayerId > max) max = l.LayerId;
        return max + 1;
    }

    public static string UniqueLayerName(UndertaleRoom room, string baseName)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (room.Layers != null)
            foreach (var l in room.Layers) used.Add(GameData.Name(l.LayerName));
        string name = baseName;
        for (int n = 2; used.Contains(name); n++) name = baseName + "_" + n;
        return name;
    }

    /// <summary>排序后的图层列表（小 depth = 靠前）。依赖 UTMT 的 RearrangeLayers。</summary>
    public static void SortLayers(UndertaleRoom room)
    {
        if (room.Layers == null || room.Layers.Count == 0) return;
        room.RearrangeLayers();
        room.UpdateBGColorLayer();
    }

    private static void AttachLayer(UndertaleRoom room, UndertaleRoom.Layer layer)
    {
        layer.ParentRoom = room;
        room.Layers.Add(layer);
        SortLayers(room);
    }

    public static UndertaleRoom.Layer AddInstanceLayer(UndertaleData data, UndertaleRoom room, string? name, int depth)
    {
        var layer = new UndertaleRoom.Layer
        {
            LayerName = data.Strings.MakeString(UniqueLayerName(room, name ?? "Instances")),
            LayerId = NextLayerId(room),
            LayerType = UndertaleRoom.LayerType.Instances,
            LayerDepth = depth,
            IsVisible = true,
            Data = new UndertaleRoom.Layer.LayerInstancesData(),
        };
        AttachLayer(room, layer);
        return layer;
    }

    /// <summary>
    /// Add a tile layer. Native GMS2 <c>LayerTilesData</c> is preferred (C3c): it is what the game
    /// actually renders in DR chapters. Only when data.win exposes no usable tileset do we fall
    /// back to the old Assets+LegacyTiles layer, and that fallback is logged loudly.
    /// </summary>
    public static UndertaleRoom.Layer AddTileLayer(UndertaleData data, UndertaleRoom room, string? name, int depth,
        UndertaleBackground? preferredTileset = null)
    {
        var bg = PickTileset(data, room, preferredTileset);
        if (bg != null)
        {
            int tw = bg.GMS2TileWidth > 0 ? (int)bg.GMS2TileWidth : 16;
            int th = bg.GMS2TileHeight > 0 ? (int)bg.GMS2TileHeight : 16;
            int cols = Math.Max(1, (int)Math.Ceiling(room.Width / (double)tw));
            int rows = Math.Max(1, (int)Math.Ceiling(room.Height / (double)th));
            var cellData = new uint[rows][];
            for (int y = 0; y < rows; y++) cellData[y] = new uint[cols];
            var layer = new UndertaleRoom.Layer
            {
                LayerName = data.Strings.MakeString(UniqueLayerName(room, name ?? "Tiles")),
                LayerId = NextLayerId(room),
                LayerType = UndertaleRoom.LayerType.Tiles,
                LayerDepth = depth,
                IsVisible = true,
                Data = new UndertaleRoom.Layer.LayerTilesData
                {
                    Background = bg,
                    TileData = cellData,
                    TilesX = (uint)cols,
                    TilesY = (uint)rows,
                },
            };
            AttachLayer(room, layer);
            return layer;
        }

        Console.Error.WriteLine("[mapedit] 房间 " + GameData.Name(room.Name) +
            " 没有可用的原生 tileset（Texture 或 GMS2TileWidth/Height 缺失），退回 LegacyTiles Assets 层");
        var legacy = new UndertaleRoom.Layer
        {
            LayerName = data.Strings.MakeString(UniqueLayerName(room, name ?? "Assets")),
            LayerId = NextLayerId(room),
            LayerType = UndertaleRoom.LayerType.Assets,
            LayerDepth = depth,
            IsVisible = true,
            Data = NewAssetsData(),
        };
        AttachLayer(room, legacy);
        return legacy;
    }

    /// <summary>Pick the tileset for a new native layer: explicit request &gt; this room's native layers &gt; any room's &gt; data.win.</summary>
    public static UndertaleBackground? PickTileset(UndertaleData data, UndertaleRoom room, UndertaleBackground? preferred = null)
    {
        if (preferred?.Texture != null && preferred.GMS2TileWidth > 0 && preferred.GMS2TileHeight > 0) return preferred;
        if (room.Layers != null)
            foreach (var l in room.Layers)
            {
                var bg = l.TilesData?.Background;
                if (bg?.Texture != null && bg.GMS2TileWidth > 0 && bg.GMS2TileHeight > 0) return bg;
            }
        if (data.Rooms != null)
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

    private static UndertaleRoom.Layer.LayerAssetsData NewAssetsData() => new()
    {
        LegacyTiles = new UndertalePointerList<UndertaleRoom.Tile>(),
        Sprites = new UndertalePointerList<UndertaleRoom.SpriteInstance>(),
        Sequences = new UndertalePointerList<UndertaleRoom.SequenceInstance>(),
        NineSlices = new UndertalePointerList<UndertaleRoom.SpriteInstance>(),
        ParticleSystems = new UndertalePointerList<UndertaleRoom.ParticleSystemInstance>(),
        TextItems = new UndertalePointerList<UndertaleRoom.TextItemInstance>(),
    };

    /// <summary>Copy one asset-layer sprite instance (all serialised fields).</summary>
    public static UndertaleRoom.SpriteInstance CloneSpriteInstance(UndertaleRoom.SpriteInstance src) => new()
    {
        Name = src.Name,
        Sprite = src.Sprite,
        X = src.X,
        Y = src.Y,
        ScaleX = src.ScaleX,
        ScaleY = src.ScaleY,
        Color = src.Color,
        AnimationSpeed = src.AnimationSpeed,
        AnimationSpeedType = src.AnimationSpeedType,
        FrameIndex = src.FrameIndex,
        Rotation = src.Rotation,
    };

    /// <summary>
    /// Shallow copy for sequence/particle/text instances. They reference shared resources, so
    /// copying the container object is enough to duplicate the layer without aliasing the list;
    /// their own sub-objects remain shared (documented limitation in the C3b report).
    /// </summary>
    private static T CloneInstanceShallow<T>(T src) where T : class
    {
        var m = typeof(T).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        return m == null ? src : (T)m.Invoke(src, null)!;
    }

    public static bool RemoveLayer(UndertaleRoom room, UndertaleRoom.Layer layer)
    {
        if (room?.Layers == null || layer == null) return false;
        if (room.Layers.Count <= 1 || !room.Layers.Contains(layer)) return false;
        if (layer.InstancesData != null)
            foreach (var inst in layer.InstancesData.Instances.ToList())
                RemoveInstance(room, inst);
        if (layer.AssetsData?.LegacyTiles != null && room.Tiles != null && room.Tiles.Count > 0)
            foreach (var t in layer.AssetsData.LegacyTiles.ToList())
                room.Tiles.Remove(t);
        room.Layers.Remove(layer);
        layer.ParentRoom = null;
        room.UpdateBGColorLayer();
        return true;
    }

    /// <summary>delta&lt;0 = 往列表上方（更靠前、depth 更小）挪一层。</summary>
    public static bool MoveLayer(UndertaleRoom room, UndertaleRoom.Layer layer, int delta)
    {
        if (room?.Layers == null || layer == null || delta == 0) return false;
        int i = room.Layers.IndexOf(layer);
        if (i < 0) return false;
        int j = i + delta;
        if (j < 0 || j >= room.Layers.Count) return false;
        var other = room.Layers[j];
        int a = layer.LayerDepth, b = other.LayerDepth;
        layer.LayerDepth = b;
        other.LayerDepth = a;
        if (a == b) layer.LayerDepth = delta > 0 ? b + 1 : b - 1;
        SortLayers(room);
        return room.Layers.IndexOf(layer) == j;
    }

    public static void RenameLayer(UndertaleData data, UndertaleRoom.Layer layer, string newName)
        => layer.LayerName = data.Strings.MakeString(newName);

    // ------------------------------------------------------------------ cloning

    private static UndertaleRoom.Background CloneBackground(UndertaleRoom.Background b) => new()
    {
        Enabled = b.Enabled,
        Foreground = b.Foreground,
        BackgroundDefinition = b.BackgroundDefinition,
        X = b.X,
        Y = b.Y,
        SpeedX = b.SpeedX,
        SpeedY = b.SpeedY,
        Stretch = b.Stretch,
        TiledHorizontally = b.TiledHorizontally,
        TiledVertically = b.TiledVertically,
    };

    private static UndertaleRoom.View CloneView(UndertaleRoom.View v) => new()
    {
        Enabled = v.Enabled,
        ViewX = v.ViewX,
        ViewY = v.ViewY,
        ViewWidth = v.ViewWidth,
        ViewHeight = v.ViewHeight,
        PortX = v.PortX,
        PortY = v.PortY,
        PortWidth = v.PortWidth,
        PortHeight = v.PortHeight,
        BorderX = v.BorderX,
        BorderY = v.BorderY,
        SpeedX = v.SpeedX,
        SpeedY = v.SpeedY,
        ObjectId = v.ObjectId,
    };

    private static UndertaleRoom.EffectProperty CloneEffectProperty(UndertaleRoom.EffectProperty s) => new()
    {
        Kind = s.Kind,
        Name = s.Name,
        Value = s.Value,
    };

    private static UndertaleSimpleList<UndertaleRoom.EffectProperty> CloneEffectProperties(UndertaleSimpleList<UndertaleRoom.EffectProperty>? src)
    {
        var list = new UndertaleSimpleList<UndertaleRoom.EffectProperty>();
        if (src != null)
            foreach (var p in src) list.Add(CloneEffectProperty(p));
        return list;
    }

    /// <summary>
    /// 深拷贝一层（实例/图块重新分配全局唯一 ID）。withContent=false 只复制结构。
    /// </summary>
    public static UndertaleRoom.Layer CloneLayer(UndertaleData data, UndertaleRoom ownerRoom, UndertaleRoom.Layer src,
        ref uint nextInstanceId, ref uint nextTileId, bool withContent = true)
    {
        var dst = new UndertaleRoom.Layer
        {
            LayerName = src.LayerName,
            LayerId = src.LayerId,
            LayerType = src.LayerType,
            LayerDepth = src.LayerDepth,
            XOffset = src.XOffset,
            YOffset = src.YOffset,
            HSpeed = src.HSpeed,
            VSpeed = src.VSpeed,
            IsVisible = src.IsVisible,
            EffectEnabled = src.EffectEnabled,
            EffectType = src.EffectType,
            EffectProperties = CloneEffectProperties(src.EffectProperties),
        };
        // 注意：ParentRoom 必须在 Data 赋值之后再挂 —— Layer.ParentRoom 的 setter 才会走 UpdateParentRoom()
        // 给 BackgroundData/TilesData 设置 ParentLayer；先挂 ParentRoom 再放 Data 会导致 ParentLayer 永远是 null，
        // 之后给 BackgroundData.Sprite 赋值时 setter 里的 ParentLayer?.ParentRoom?... 解引用就会 NRE（实测）。
        UndertaleRoom.Layer.LayerData newData;
        switch (src.LayerType)
        {
            case UndertaleRoom.LayerType.Instances:
                var insts = new UndertaleRoom.Layer.LayerInstancesData();
                if (withContent && src.InstancesData?.Instances != null)
                {
                    foreach (var i in src.InstancesData.Instances)
                    {
                        var c = Clone(i);
                        c.InstanceID = nextInstanceId++;
                        insts.Instances.Add(c);
                    }
                }
                newData = insts;
                break;
            case UndertaleRoom.LayerType.Assets:
                var assets = NewAssetsData();
                if (withContent && src.AssetsData != null)
                {
                    if (src.AssetsData.LegacyTiles != null)
                    {
                        foreach (var t in src.AssetsData.LegacyTiles)
                        {
                            var c = CloneTile(t);
                            c.InstanceID = nextTileId++;
                            assets.LegacyTiles.Add(c);
                        }
                    }
                    if (src.AssetsData.Sprites != null)
                        foreach (var si in src.AssetsData.Sprites)
                            assets.Sprites.Add(CloneSpriteInstance(si));
                    if (src.AssetsData.NineSlices != null)
                        foreach (var si in src.AssetsData.NineSlices)
                            assets.NineSlices.Add(CloneSpriteInstance(si));
                    if (src.AssetsData.Sequences != null)
                        foreach (var si in src.AssetsData.Sequences)
                            assets.Sequences.Add(CloneInstanceShallow(si));
                    if (src.AssetsData.ParticleSystems != null)
                        foreach (var ps in src.AssetsData.ParticleSystems)
                            assets.ParticleSystems.Add(CloneInstanceShallow(ps));
                    if (src.AssetsData.TextItems != null)
                        foreach (var ti in src.AssetsData.TextItems)
                            assets.TextItems.Add(CloneInstanceShallow(ti));
                }
                newData = assets;
                break;
            case UndertaleRoom.LayerType.Background:
                var bd = new UndertaleRoom.Layer.LayerBackgroundData();
                dst.Data = bd;
                dst.ParentRoom = ownerRoom; // UpdateParentRoom() 才会把 bd.ParentLayer 指向 dst
                if (src.BackgroundData != null)
                {
                    var s = src.BackgroundData;
                    bd.Visible = s.Visible;
                    bd.Foreground = s.Foreground;
                    bd.Sprite = s.Sprite;
                    bd.TiledHorizontally = s.TiledHorizontally;
                    bd.TiledVertically = s.TiledVertically;
                    bd.Stretch = s.Stretch;
                    bd.Color = s.Color;
                    bd.FirstFrame = s.FirstFrame;
                    bd.AnimationSpeed = s.AnimationSpeed;
                    bd.AnimationSpeedType = s.AnimationSpeedType;
                }
                return dst;
            case UndertaleRoom.LayerType.Tiles:
                var td = new UndertaleRoom.Layer.LayerTilesData();
                dst.Data = td;
                dst.ParentRoom = ownerRoom;
                if (src.TilesData != null)
                {
                    td.Background = src.TilesData.Background;
                    td.TilesX = src.TilesData.TilesX;
                    td.TilesY = src.TilesData.TilesY;
                    var rows = src.TilesData.TileData;
                    if (withContent && rows != null)
                    {
                        var copy = new uint[rows.Length][];
                        for (int y = 0; y < rows.Length; y++) copy[y] = (uint[])rows[y].Clone();
                        td.TileData = copy;
                    }
                    else
                    {
                        td.TileData = new uint[0][];
                        td.TilesX = 0;
                        td.TilesY = 0;
                    }
                }
                return dst;
            case UndertaleRoom.LayerType.Effect:
                newData = new UndertaleRoom.Layer.LayerEffectData();
                break;
            default:
                dst.ParentRoom = ownerRoom;
                return dst;
        }
        dst.Data = newData;
        dst.ParentRoom = ownerRoom;
        return dst;
    }

    /// <summary>用模板建一个新房间（withContent=false = 空白房间），并登记进 ROOM / RoomOrder。</summary>
    public static UndertaleRoom CreateRoom(UndertaleData data, UndertaleRoom template, string name, bool withContent)
    {
        var room = new UndertaleRoom
        {
            Name = data.Strings.MakeString(UniqueRoomName(data, name)),
            Caption = template.Caption,
            Width = template.Width,
            Height = template.Height,
            Speed = template.Speed,
            Persistent = template.Persistent,
            BackgroundColor = template.BackgroundColor,
            DrawBackgroundColor = template.DrawBackgroundColor,
            CreationCodeId = template.CreationCodeId,
            Flags = template.Flags,
            World = template.World,
            Top = template.Top,
            Left = template.Left,
            Right = template.Right,
            Bottom = template.Bottom,
            GravityX = template.GravityX,
            GravityY = template.GravityY,
            MetersPerPixel = template.MetersPerPixel,
            GridWidth = template.GridWidth,
            GridHeight = template.GridHeight,
            GridThicknessPx = template.GridThicknessPx,
            InstanceCreationOrderIDs = null,
        };
        room.Backgrounds.Clear();
        if (template.Backgrounds != null)
            foreach (var b in template.Backgrounds) room.Backgrounds.Add(CloneBackground(b));
        room.Views.Clear();
        if (template.Views != null)
            foreach (var v in template.Views) room.Views.Add(CloneView(v));
        room.GameObjects.Clear();
        room.Tiles.Clear();
        room.Layers.Clear();
        uint nextInstId = NextInstanceId(data), nextTileId = NextTileId(data);
        if (template.Layers != null)
            foreach (var layer in template.Layers)
            {
                var l = CloneLayer(data, room, layer, ref nextInstId, ref nextTileId, withContent);
                room.Layers.Add(l);
            }
        foreach (var (_, inst) in AllInstances(room)) room.GameObjects.Add(inst);
        room.SetupRoom(false, false);
        data.Rooms.Add(room);
        var order = data.GeneralInfo?.RoomOrder;
        if (order != null)
        {
            var entry = new UndertaleResourceById<UndertaleRoom, UndertaleChunkROOM>();
            entry.Resource = room;
            order.Add(entry);
        }
        return room;
    }

    public static UndertaleRoom DuplicateRoom(UndertaleData data, UndertaleRoom src, string? name = null)
        => CreateRoom(data, src, name ?? (GameData.Name(src.Name) + "_copy"), true);

    public static UndertaleRoom CreateBlankRoom(UndertaleData data, UndertaleRoom template, string? name = null)
        => CreateRoom(data, template, name ?? "room_new", false);

    public static bool DeleteRoom(UndertaleData data, UndertaleRoom room)
    {
        if (data?.Rooms == null || room == null || !data.Rooms.Contains(room)) return false;
        if (data.Rooms.Count <= 1) return false;
        data.Rooms.Remove(room);
        var order = data.GeneralInfo?.RoomOrder;
        if (order != null)
            for (int i = order.Count - 1; i >= 0; i--)
                if (ReferenceEquals(order[i]?.Resource, room)) order.RemoveAt(i);
        return true;
    }

    public static void RenameRoom(UndertaleData data, UndertaleRoom room, string newName)
        => room.Name = data.Strings.MakeString(newName);
}
