using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace NtlMapEdit;

/// <summary>
/// Loaded DELTARUNE data.win plus lazy caches for texture pages and sprite frames.
/// This is the single source of truth shared by the CLI and the GUI.
/// </summary>
public sealed class GameData : IDisposable
{
    public UndertaleData Data { get; }
    public string SourcePath { get; }

    private readonly Dictionary<UndertaleEmbeddedTexture, Bitmap?> _pages = new();
    private readonly Dictionary<(UndertaleSprite, int), Bitmap?> _frames = new();
    private readonly Dictionary<(UndertaleBackground, int, int), Bitmap?> _tiles = new();
    private bool _disposed;

    public GameData(string path)
    {
        SourcePath = path;
        using var fs = File.OpenRead(path);
        Data = UndertaleIO.Read(fs, (w, imp) => { }, m => { }, false);
        SetWritable();
        SetupRooms();
    }

    /// <summary>Creates an empty model (unused for now, keeps the API symmetrical).</summary>
    public GameData(UndertaleData data, string path)
    {
        Data = data;
        SourcePath = path;
        SetWritable();
        SetupRooms();
    }

    /// <summary>
    /// 给每个房间补齐编辑器侧的父子链（layer.ParentRoom / BackgroundData.ParentLayer）。
    /// UTMT 编辑器在打开房间时会调 UndertaleRoom.SetupRoom()；我们直接 UndertaleIO.Read 出来的数据
    /// 没有这一步，于是 LayerBackgroundData.Sprite 的 setter 在回调 ParentLayer.ParentRoom.UpdateBGColorLayer()
    /// 时会空引用（实测换背景精灵必崩）。这里在加载后统一补一次，参数 false/false 表示不重算房间网格。
    /// </summary>
    private void SetupRooms()
    {
        if (Data?.Rooms is null) return;
        foreach (var room in Data.Rooms)
        {
            if (room?.Layers is null) continue;
            try { room.SetupRoom(false, false); }
            catch (Exception ex) { Console.Error.WriteLine($"[room] setup failed {Name(room.Name)}: {ex.Message}"); }
        }
    }

    /// <summary>
    /// UndertaleModLib marks freshly-read resources as non-writable when they are
    /// declared in the data. Flipping IsSpecialType/markers is not needed, but the
    /// room lists must be editable; nothing else to do here yet.
    /// </summary>
    private void SetWritable() { }

    public static string Name(UndertaleString? s) => s?.Content ?? string.Empty;

    public IEnumerable<UndertaleRoom> Rooms => Data.Rooms;
    public IEnumerable<UndertaleGameObject> GameObjects => Data.GameObjects;
    public IEnumerable<UndertaleSprite> Sprites => Data.Sprites;
    public IEnumerable<UndertaleBackground> Backgrounds => Data.Backgrounds;

    public UndertaleRoom? FindRoom(string name)
    {
        var exact = Data.Rooms.FirstOrDefault(r => string.Equals(Name(r.Name), name, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;
        return Data.Rooms.FirstOrDefault(r => Name(r.Name).Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Texture page bitmap for a page item (decoded once, cached).</summary>
    public Bitmap? Page(UndertaleTexturePageItem? tpag) => tpag == null ? null : Page(tpag.TexturePage);

    public Bitmap? Page(UndertaleEmbeddedTexture? tex)
    {
        if (tex?.TextureData?.Image is null) return null;
        if (_pages.TryGetValue(tex, out var cached)) return cached;
        Bitmap? bmp = null;
        try
        {
            using var ms = new MemoryStream();
            tex.TextureData.Image.SavePng(ms);
            ms.Position = 0;
            using var tmp = new Bitmap(ms);
            bmp = new Bitmap(tmp.Width, tmp.Height, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(tmp, new Rectangle(0, 0, tmp.Width, tmp.Height));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[tex] page decode failed: {ex.Message}");
            bmp = null;
        }
        _pages[tex] = bmp;
        return bmp;
    }

    /// <summary>Cropped bitmap of one sprite frame (frame index wraps like GM does).</summary>
    public Bitmap? Frame(UndertaleSprite? spr, int index)
    {
        if (spr?.Textures is null || spr.Textures.Count == 0) return null;
        if (index < 0) index = 0;
        index %= (int)spr.Textures.Count;
        if (_frames.TryGetValue((spr, index), out var cached)) return cached;
        Bitmap? bmp = null;
        try
        {
            var tpag = spr.Textures[index]?.Texture;
            var page = Page(tpag);
            if (page != null && tpag != null)
            {
                int sw = Math.Max(1, (int)tpag.SourceWidth);
                int sh = Math.Max(1, (int)tpag.SourceHeight);
                int sx = Math.Clamp(tpag.SourceX, 0, Math.Max(0, page.Width - 1));
                int sy = Math.Clamp(tpag.SourceY, 0, Math.Max(0, page.Height - 1));
                sw = Math.Min(sw, page.Width - sx);
                sh = Math.Min(sh, page.Height - sy);
                if (sw > 0 && sh > 0)
                {
                    bmp = new Bitmap(sw, sh, PixelFormat.Format32bppArgb);
                    using var g = Graphics.FromImage(bmp);
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(page, new Rectangle(0, 0, sw, sh), new Rectangle(sx, sy, sw, sh), GraphicsUnit.Pixel);
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[spr] frame decode failed {Name(spr?.Name)}[{index}]: {ex.Message}");
            bmp = null;
        }
        _frames[(spr!, index)] = bmp;
        return bmp;
    }

    /// <summary>Convert a GameMaker colour integer (BGR + alpha) into a Color.</summary>
    public static Color ColorFromGM(uint c)
    {
        int r = (int)(c & 0xFF);
        int g = (int)((c >> 8) & 0xFF);
        int b = (int)((c >> 16) & 0xFF);
        int a = (int)((c >> 24) & 0xFF);
        if (a == 0) a = 255;
        return Color.FromArgb(a, r, g, b);
    }

    /// <summary>Same as <see cref="ColorFromGM"/> but preserves an explicit alpha of 0 (background/asset colours).</summary>
    public static Color ColorFromGMExact(uint c)
    {
        int r = (int)(c & 0xFF);
        int g = (int)((c >> 8) & 0xFF);
        int b = (int)((c >> 16) & 0xFF);
        int a = (int)((c >> 24) & 0xFF);
        return Color.FromArgb(a, r, g, b);
    }

    /// <summary>
    /// One GMS2 native tile bitmap, cropped from the tileset texture page and rotated/flipped
    /// according to the three high bits of a room tile value (UTMT room editor semantics).
    /// </summary>
    public Bitmap? Tile(UndertaleBackground? bg, int tileId, int flags)
    {
        if (bg == null || tileId < 0) return null;
        flags &= 7;
        if (_tiles.TryGetValue((bg, tileId, flags), out var cached)) return cached;
        Bitmap? bmp = null;
        try
        {
            var tpag = bg.Texture;
            var page = Page(tpag);
            if (page != null && tpag != null)
            {
                int tw = bg.GMS2TileWidth > 0 ? (int)bg.GMS2TileWidth : 16;
                int th = bg.GMS2TileHeight > 0 ? (int)bg.GMS2TileHeight : 16;
                int cols = bg.GMS2TileColumns > 0 ? (int)bg.GMS2TileColumns : Math.Max(1, tpag.SourceWidth / Math.Max(1, tw));
                if (cols <= 0) cols = 1;
                int bx = Math.Max(0, (int)bg.GMS2OutputBorderX);
                int by = Math.Max(0, (int)bg.GMS2OutputBorderY);
                int sx = tpag.SourceX + (tileId % cols) * (tw + 2 * bx) + bx;
                int sy = tpag.SourceY + (tileId / cols) * (th + 2 * by) + by;
                if (sx < 0 || sy < 0 || sx + tw > page.Width || sy + th > page.Height) return null;
                bmp = new Bitmap(tw, th, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(page, new Rectangle(0, 0, tw, th), new Rectangle(sx, sy, tw, th), GraphicsUnit.Pixel);
                }
                if (flags != 0) bmp.RotateFlip(FlagsToRotateFlip(flags));
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[tile] decode failed {Name(bg?.Name)}[{tileId}] flags={flags}: {ex.Message}");
            bmp = null;
        }
        _tiles[(bg!, tileId, flags)] = bmp;
        return bmp;
    }

    /// <summary>UTMT/GMS2 tile flag bits -> System.Drawing rotation/flip order (flip before rotate).</summary>
    public static RotateFlipType FlagsToRotateFlip(int flags) => (flags & 7) switch
    {
        1 => RotateFlipType.RotateNoneFlipX,
        2 => RotateFlipType.RotateNoneFlipY,
        3 => RotateFlipType.RotateNoneFlipXY,
        4 => RotateFlipType.Rotate90FlipNone,
        5 => RotateFlipType.Rotate90FlipY,
        6 => RotateFlipType.Rotate90FlipX,
        7 => RotateFlipType.Rotate90FlipXY,
        _ => RotateFlipType.RotateNoneFlipNone,
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var b in _frames.Values) b?.Dispose();
        foreach (var b in _tiles.Values) b?.Dispose();
        foreach (var b in _pages.Values) b?.Dispose();
        _frames.Clear();
        _tiles.Clear();
        _pages.Clear();
    }
}
