using System.Drawing;
using System.Drawing.Drawing2D;
using UndertaleModLib.Models;

namespace NtlMapEdit;

/// <summary>
/// A native GMS2 tile brush: a tile index + the three transform flag bits from one tileset
/// (<see cref="UndertaleBackground"/>). Unlike <see cref="UndertaleRoom.Tile"/> there is no
/// per-tile object in the data — a brush is just a value to stamp into a LayerTilesData grid.
/// </summary>
public sealed class NativeTileBrush
{
    public UndertaleBackground? Background { get; init; }
    public int TileId { get; init; }
    public int Flags { get; init; }

    public uint Value => NativeTiles.Value(TileId, Flags);

    public (int W, int H) CellSize
    {
        get
        {
            int w = Background?.GMS2TileWidth > 0 ? (int)Background.GMS2TileWidth : 16;
            int h = Background?.GMS2TileHeight > 0 ? (int)Background.GMS2TileHeight : 16;
            return (Math.Max(1, w), Math.Max(1, h));
        }
    }

    public SizeF WorldSize => new(CellSize.W, CellSize.H);

    public static NativeTileBrush FromTile(NativeTileRef tile) => new()
    {
        Background = tile.Layer.TilesData?.Background,
        TileId = tile.TileId,
        Flags = tile.Flags,
    };

    public static NativeTileBrush FromValue(UndertaleBackground? bg, uint value) => new()
    {
        Background = bg,
        TileId = NativeTiles.TileId(value),
        Flags = NativeTiles.Flags(value),
    };

    public NativeTileBrush WithFlags(int flags) => new() { Background = Background, TileId = TileId, Flags = flags & 7 };

    public override string ToString() =>
        $"{GameData.Name(Background?.Name)} id={TileId} flags={Flags} {CellSize.W}x{CellSize.H}";
}

/// <summary>One selectable native tileset entry (a GMS2 background used as a tileset).</summary>
public sealed class NativeTilesetInfo
{
    public UndertaleBackground Background { get; init; } = null!;
    public string Name => GameData.Name(Background.Name);
    public int TileWidth => Background.GMS2TileWidth > 0 ? (int)Background.GMS2TileWidth : 16;
    public int TileHeight => Background.GMS2TileHeight > 0 ? (int)Background.GMS2TileHeight : 16;
    public int Columns => Background.GMS2TileColumns > 0 ? (int)Background.GMS2TileColumns : 1;
    public int TileCount
    {
        get
        {
            if (Background.GMS2TileCount > 0) return (int)Background.GMS2TileCount;
            int texH = Background.Texture?.SourceHeight ?? 0;
            int rowH = TileHeight + 2 * Math.Max(0, (int)Background.GMS2OutputBorderY);
            int rows = rowH > 0 ? Math.Max(1, texH / rowH) : 1;
            return Math.Max(1, Columns * rows);
        }
    }

    public override string ToString() => $"{Name}  {TileWidth}×{TileHeight} · {Columns} 列 · {TileCount} tiles";
}

/// <summary>
/// Pure functions for editing native GMS2 tile layers. Kept separate from RoomEdit (which is
/// LegacyTiles only) so the legacy path stays byte-for-byte unchanged.
/// </summary>
public static class NativeTileEdit
{
    public static SizeF CellSize(UndertaleRoom.Layer layer)
    {
        var (w, h) = NativeTiles.CellSize(layer);
        return new SizeF(w, h);
    }

    public static Point CellIndex(UndertaleRoom.Layer layer, int x, int y) => new(x, y);

    public static bool SnapCell(UndertaleRoom.Layer layer, PointF world, out Point cell)
    {
        if (NativeTiles.CellAt(layer, world, out int x, out int y)) { cell = new Point(x, y); return true; }
        cell = new Point(-1, -1);
        return false;
    }

    public static List<Point> CellsInRect(UndertaleRoom.Layer layer, RectangleF world)
    {
        var hits = new List<Point>();
        var rows = layer.TilesData?.TileData;
        if (rows == null) return hits;
        for (int y = 0; y < rows.Length; y++)
        {
            var row = rows[y];
            if (row == null) continue;
            for (int x = 0; x < row.Length; x++)
            {
                var r = NativeTiles.CellRect(layer, x, y);
                if (new RectangleF(r.X, r.Y, r.Width, r.Height).IntersectsWith(world)) hits.Add(new Point(x, y));
            }
        }
        return hits;
    }

    /// <summary>Stamp the brush into every cell. Returns the number of cells whose value changed.</summary>
    public static int PaintCells(UndertaleRoom.Layer layer, NativeTileBrush brush, IEnumerable<Point> cells)
    {
        var rows = layer.TilesData?.TileData;
        if (rows == null || brush.TileId <= 0) return 0;
        uint value = brush.Value;
        int changed = 0;
        foreach (var c in cells)
        {
            if (c.Y < 0 || c.Y >= rows.Length) continue;
            var row = rows[c.Y];
            if (row == null || c.X < 0 || c.X >= row.Length) continue;
            if (row[c.X] == value) continue;
            row[c.X] = value;
            changed++;
        }
        if (changed > 0) layer.TilesData!.TileDataUpdated();
        return changed;
    }

    /// <summary>Clear every cell in the list (id=0). Returns the number of non-empty cells cleared.</summary>
    public static int EraseCells(UndertaleRoom.Layer layer, IEnumerable<Point> cells)
    {
        var rows = layer.TilesData?.TileData;
        if (rows == null) return 0;
        int changed = 0;
        foreach (var c in cells)
        {
            if (c.Y < 0 || c.Y >= rows.Length) continue;
            var row = rows[c.Y];
            if (row == null || c.X < 0 || c.X >= row.Length) continue;
            if ((row[c.X] & NativeTiles.IndexMask) == 0) continue;
            row[c.X] = 0;
            changed++;
        }
        if (changed > 0) layer.TilesData!.TileDataUpdated();
        return changed;
    }

    /// <summary>4-neighbour flood fill from a cell, replacing the whole same-value region.</summary>
    public static int FloodFill(UndertaleRoom.Layer layer, NativeTileBrush brush, Point origin)
    {
        var rows = layer.TilesData?.TileData;
        if (rows == null || origin.Y < 0 || origin.Y >= rows.Length) return 0;
        var first = rows[origin.Y];
        if (first == null || origin.X < 0 || origin.X >= first.Length) return 0;
        uint target = first[origin.X];
        if (target == brush.Value) return 0;
        return FloodRegion(layer, origin, target, brush.Value);
    }

    public static int FloodErase(UndertaleRoom.Layer layer, Point origin)
    {
        var rows = layer.TilesData?.TileData;
        if (rows == null || origin.Y < 0 || origin.Y >= rows.Length) return 0;
        var first = rows[origin.Y];
        if (first == null || origin.X < 0 || origin.X >= first.Length) return 0;
        uint target = first[origin.X];
        if ((target & NativeTiles.IndexMask) == 0) return 0;
        return FloodRegion(layer, origin, target, 0);
    }

    private static int FloodRegion(UndertaleRoom.Layer layer, Point origin, uint target, uint replacement)
    {
        var rows = layer.TilesData!.TileData!;
        int height = rows.Length;
        int width = rows[origin.Y].Length;
        var visited = new HashSet<long>();
        var stack = new Stack<Point>();
        stack.Push(origin);
        int changed = 0;
        while (stack.Count > 0)
        {
            var p = stack.Pop();
            if (p.X < 0 || p.X >= width || p.Y < 0 || p.Y >= height) continue;
            var row = rows[p.Y];
            if (row == null || p.X >= row.Length) continue;
            if (row[p.X] != target) continue;
            long key = ((long)p.Y << 32) | (uint)p.X;
            if (!visited.Add(key)) continue;
            row[p.X] = replacement;
            changed++;
            stack.Push(new Point(p.X + 1, p.Y));
            stack.Push(new Point(p.X - 1, p.Y));
            stack.Push(new Point(p.X, p.Y + 1));
            stack.Push(new Point(p.X, p.Y - 1));
        }
        if (changed > 0) layer.TilesData.TileDataUpdated();
        return changed;
    }

    // ---------------------------------------------------------- palette / enumeration

    /// <summary>
    /// Native tilesets usable as a brush source. Tilesets already used by the room come first,
    /// then every background in data.win that has a texture page and a real GMS2 tile grid.
    /// </summary>
    public static List<NativeTilesetInfo> EnumerateTilesets(GameData gd, UndertaleRoom? room)
    {
        var seen = new HashSet<UndertaleBackground>(ReferenceEqualityComparer.Instance);
        var result = new List<NativeTilesetInfo>();
        void Add(UndertaleBackground? bg)
        {
            if (bg?.Texture == null || bg.GMS2TileWidth <= 0 || bg.GMS2TileHeight <= 0 || !seen.Add(bg)) return;
            result.Add(new NativeTilesetInfo { Background = bg });
        }
        if (room?.Layers != null)
            foreach (var layer in room.Layers) Add(layer.TilesData?.Background);
        foreach (var bg in gd.Data.Backgrounds) Add(bg);
        return result;
    }

    /// <summary>All distinct tilesets already used by native layers of any room.</summary>
    public static List<NativeTilesetInfo> RoomTilesets(GameData gd, UndertaleRoom? room)
    {
        var seen = new HashSet<UndertaleBackground>(ReferenceEqualityComparer.Instance);
        var result = new List<NativeTilesetInfo>();
        if (room?.Layers == null) return result;
        foreach (var layer in room.Layers)
        {
            var bg = layer.TilesData?.Background;
            if (bg?.Texture == null || bg.GMS2TileWidth <= 0 || bg.GMS2TileHeight <= 0 || !seen.Add(bg)) continue;
            result.Add(new NativeTilesetInfo { Background = bg });
        }
        return result;
    }

    /// <summary>
    /// Compose a tile-index-ordered sheet bitmap for the palette UI (every cell = one tile id,
    /// cropped from the real texture page including GMS2 output borders). Returns null when the
    /// tileset has no readable texture.
    /// </summary>
    public static Bitmap? BuildSheet(GameData gd, NativeTilesetInfo info, int maxTiles = 4096)
    {
        var bg = info.Background;
        var page = gd.Page(bg.Texture);
        if (page == null) return null;
        int count = Math.Min(info.TileCount, Math.Max(1, maxTiles));
        int cols = Math.Max(1, info.Columns);
        int rows = Math.Max(1, (count + cols - 1) / cols);
        int tw = info.TileWidth, th = info.TileHeight;
        var bmp = new Bitmap(Math.Max(1, cols * tw), Math.Max(1, rows * th));
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.SmoothingMode = SmoothingMode.None;
        g.Clear(Color.Transparent);
        for (int id = 0; id < count; id++)
        {
            var src = NativeTiles.SourceRect(bg, id);
            if (src.Width <= 0 || src.Height <= 0) continue;
            var clip = Rectangle.Intersect(src, new Rectangle(0, 0, page.Width, page.Height));
            if (clip.Width <= 0 || clip.Height <= 0) continue;
            int dx = (id % cols) * tw + (clip.X - src.X);
            int dy = (id / cols) * th + (clip.Y - src.Y);
            g.DrawImage(page, new Rectangle(dx, dy, clip.Width, clip.Height), clip, GraphicsUnit.Pixel);
        }
        return bmp;
    }

    /// <summary>Pick a default tileset for a new native layer: an existing room tileset first, then data.win backgrounds.</summary>
    public static NativeTilesetInfo? PickDefaultTileset(GameData gd, UndertaleRoom? room)
    {
        var roomSets = RoomTilesets(gd, room);
        if (roomSets.Count > 0) return roomSets[0];
        // Prefer a tileset already used by any other room in the data before falling back to BGND order.
        foreach (var r in gd.Data.Rooms)
        {
            if (ReferenceEquals(r, room) || r.Layers == null) continue;
            foreach (var layer in r.Layers)
            {
                var bg = layer.TilesData?.Background;
                if (bg?.Texture != null && bg.GMS2TileWidth > 0 && bg.GMS2TileHeight > 0)
                    return new NativeTilesetInfo { Background = bg };
            }
        }
        foreach (var bg in gd.Data.Backgrounds)
            if (bg.Texture != null && bg.GMS2TileWidth > 0 && bg.GMS2TileHeight > 0)
                return new NativeTilesetInfo { Background = bg };
        return null;
    }
}
