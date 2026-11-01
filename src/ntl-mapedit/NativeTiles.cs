using System.Drawing;
using UndertaleModLib.Models;

namespace NtlMapEdit;

/// <summary>
/// Helpers for GMS2 native tile layers (<see cref="UndertaleRoom.Layer.LayerTilesData"/>).
/// A room cell holds a 1-based-ish output tile index in the low 28 bits and three
/// transform bits in the top nibble; 0 means empty. This is exactly what UTMT's room
/// editor uses (UndertaleRoomEditor.xaml.cs:3050-3128 / UndertaleCachedImageLoader.cs:430-476).
/// </summary>
public static class NativeTiles
{
    public const uint IndexMask = 0x0FFFFFFF;
    public const int FlagMask = 7;

    public static bool IsNative(UndertaleRoom.Layer? layer) =>
        layer?.TilesData?.TileData != null && layer.TilesData.TileData.Length > 0;

    public static int TileId(uint value) => (int)(value & IndexMask);
    public static int Flags(uint value) => (int)((value >> 28) & FlagMask);
    public static uint Value(int tileId, int flags) => ((uint)(flags & FlagMask) << 28) | ((uint)tileId & IndexMask);

    public static (int W, int H) CellSize(UndertaleRoom.Layer layer)
    {
        var bg = layer.TilesData?.Background;
        int w = bg?.GMS2TileWidth > 0 ? (int)bg.GMS2TileWidth : 16;
        int h = bg?.GMS2TileHeight > 0 ? (int)bg.GMS2TileHeight : 16;
        return (Math.Max(1, w), Math.Max(1, h));
    }

    public static int Rows(UndertaleRoom.Layer? layer) => layer?.TilesData?.TileData?.Length ?? 0;

    public static int Cols(UndertaleRoom.Layer? layer, int row)
    {
        var rows = layer?.TilesData?.TileData;
        if (rows == null || row < 0 || row >= rows.Length || rows[row] == null) return 0;
        return rows[row].Length;
    }

    public static uint Get(UndertaleRoom.Layer layer, int x, int y)
    {
        var rows = layer.TilesData?.TileData;
        if (rows == null || y < 0 || y >= rows.Length) return 0;
        var row = rows[y];
        if (row == null || x < 0 || x >= row.Length) return 0;
        return row[x];
    }

    public static bool Set(UndertaleRoom.Layer layer, int x, int y, uint value)
    {
        var rows = layer.TilesData?.TileData;
        if (rows == null || y < 0 || y >= rows.Length) return false;
        var row = rows[y];
        if (row == null || x < 0 || x >= row.Length) return false;
        row[x] = value;
        layer.TilesData!.TileDataUpdated();
        return true;
    }

    public static int CountNonZero(UndertaleRoom.Layer? layer)
    {
        var rows = layer?.TilesData?.TileData;
        if (rows == null) return 0;
        int n = 0;
        foreach (var row in rows)
        {
            if (row == null) continue;
            foreach (var v in row) if ((v & IndexMask) != 0) n++;
        }
        return n;
    }

    public static IEnumerable<(int X, int Y, uint Value)> Cells(UndertaleRoom.Layer layer)
    {
        var rows = layer.TilesData?.TileData;
        if (rows == null) yield break;
        for (int y = 0; y < rows.Length; y++)
        {
            var row = rows[y];
            if (row == null) continue;
            for (int x = 0; x < row.Length; x++)
            {
                uint v = row[x];
                if ((v & IndexMask) != 0) yield return (x, y, v);
            }
        }
    }

    public static Rectangle CellRect(UndertaleRoom.Layer layer, int x, int y)
    {
        var (w, h) = CellSize(layer);
        return new Rectangle((int)layer.XOffset + x * w, (int)layer.YOffset + y * h, w, h);
    }

    /// <summary>World point -> tile cell. Returns false outside the layer grid.</summary>
    public static bool CellAt(UndertaleRoom.Layer layer, PointF world, out int x, out int y)
    {
        var (w, h) = CellSize(layer);
        int lx = (int)Math.Floor(world.X - layer.XOffset);
        int ly = (int)Math.Floor(world.Y - layer.YOffset);
        x = lx / w;
        y = ly / h;
        if (lx < 0) x = (lx - (w - 1)) / w; // floor division for negatives
        if (ly < 0) y = (ly - (h - 1)) / h;
        int rows = Rows(layer);
        int cols = Cols(layer, y);
        return y >= 0 && y < rows && x >= 0 && x < cols;
    }

    /// <summary>Source texture rectangle of a tile id inside the tileset texture page.</summary>
    public static Rectangle SourceRect(UndertaleBackground? bg, int tileId)
    {
        if (bg?.Texture == null || tileId < 0) return Rectangle.Empty;
        int tw = bg.GMS2TileWidth > 0 ? (int)bg.GMS2TileWidth : 16;
        int th = bg.GMS2TileHeight > 0 ? (int)bg.GMS2TileHeight : 16;
        int cols = bg.GMS2TileColumns > 0 ? (int)bg.GMS2TileColumns : 1;
        int bx = Math.Max(0, (int)bg.GMS2OutputBorderX);
        int by = Math.Max(0, (int)bg.GMS2OutputBorderY);
        int sx = bg.Texture.SourceX + (tileId % cols) * (tw + 2 * bx) + bx;
        int sy = bg.Texture.SourceY + (tileId / cols) * (th + 2 * by) + by;
        return new Rectangle(sx, sy, tw, th);
    }

    public static bool IsTileIdValid(UndertaleBackground? bg, int tileId)
    {
        if (bg == null || tileId < 0) return false;
        int count = (int)bg.GMS2TileCount;
        if (count > 0) return tileId < count;
        int cols = bg.GMS2TileColumns > 0 ? (int)bg.GMS2TileColumns : 1;
        int tw = bg.GMS2TileWidth > 0 ? (int)bg.GMS2TileWidth : 16;
        int th = bg.GMS2TileHeight > 0 ? (int)bg.GMS2TileHeight : 16;
        int rows = bg.Texture != null ? (bg.Texture.SourceHeight - 1) / Math.Max(1, th + 2 * (int)bg.GMS2OutputBorderY) : 0;
        return tileId < Math.Max(1, cols * Math.Max(1, rows));
    }

    public static string DescribeSource(UndertaleRoom.Layer layer, uint value)
    {
        var bg = layer.TilesData?.Background;
        int id = TileId(value), flags = Flags(value);
        var src = SourceRect(bg, id);
        return $"id={id} flags={flags} tileset={GameData.Name(bg?.Name)} src=({src.X},{src.Y},{src.Width},{src.Height})";
    }

    public static int CountAssets(UndertaleRoom.Layer? layer)
    {
        var ad = layer?.AssetsData;
        if (ad == null) return 0;
        return (ad.LegacyTiles?.Count ?? 0) + (ad.Sprites?.Count ?? 0) + (ad.NineSlices?.Count ?? 0)
             + (ad.Sequences?.Count ?? 0) + (ad.ParticleSystems?.Count ?? 0) + (ad.TextItems?.Count ?? 0);
    }

    public static int CountContent(UndertaleRoom.Layer? layer)
    {
        if (layer == null) return 0;
        int n = layer.InstancesData?.Instances?.Count ?? 0;
        n += CountAssets(layer);
        n += CountNonZero(layer);
        return n;
    }

    public static bool IsPathLayer(UndertaleRoom.Layer? layer) =>
        layer?.LayerType == UndertaleRoom.LayerType.Path || layer?.LayerType == UndertaleRoom.LayerType.Path2;
}

/// <summary>A reference to one cell in a native tile layer (there is no per-tile object in UTMT).</summary>
public sealed class NativeTileRef : IEquatable<NativeTileRef>
{
    public UndertaleRoom.Layer Layer { get; }
    public int X { get; }
    public int Y { get; }
    public NativeTileRef(UndertaleRoom.Layer layer, int x, int y) { Layer = layer; X = x; Y = y; }
    public uint Value => NativeTiles.Get(Layer, X, Y);
    public int TileId => NativeTiles.TileId(Value);
    public int Flags => NativeTiles.Flags(Value);
    public Rectangle Rect => NativeTiles.CellRect(Layer, X, Y);
    public bool Equals(NativeTileRef? other) => other != null && ReferenceEquals(Layer, other.Layer) && X == other.X && Y == other.Y;
    public override bool Equals(object? obj) => Equals(obj as NativeTileRef);
    public override int GetHashCode() => HashCode.Combine(Layer, X, Y);
    public override string ToString() => $"{GameData.Name(Layer.LayerName)}[{X},{Y}] id={TileId} flags={Flags}";
}
