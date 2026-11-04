using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using UndertaleModLib.Models;

namespace NtlMapEdit;

/// <summary>
/// Renders an UndertaleRoom to a 32bpp bitmap (used by the CLI `render` command and the GUI canvas).
/// GameMaker depth ordering: larger depth draws first (farther back).
/// </summary>
public static class RoomRender
{
    /// <summary>
    /// Canvas colour used when the room does not draw its background colour. This is an editor
    /// convention: an opaque <c>0xFF000000</c> colour-only background layer is treated as an
    /// unset backdrop so native tiles remain readable (the layer data itself is never changed; see
    /// DrawBackgroundLayer). A real black game background would make the tile editor unusable.
    /// </summary>
    public static readonly Color CanvasBackground = Color.FromArgb(255, 28, 28, 34);

    public static Bitmap Render(GameData gd, UndertaleRoom room, int scale = 1, bool trueBackground = false)
    {
        scale = Math.Max(1, scale);
        int w = Math.Max(1, (int)room.Width * scale);
        int h = Math.Max(1, (int)room.Height * scale);
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(room.DrawBackgroundColor && trueBackground ? GameData.ColorFromGM(room.BackgroundColor) : CanvasBackground);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.SmoothingMode = SmoothingMode.None;
        g.CompositingMode = CompositingMode.SourceOver;
        if (scale != 1) g.ScaleTransform(scale, scale);

        var tints = new Dictionary<uint, ImageAttributes>();
        try
        {
            if (room.Layers != null)
            {
                foreach (var layer in room.Layers.OrderByDescending(l => l.LayerDepth))
                {
                    if (!layer.IsVisible) continue;
                    switch (layer.LayerType)
                    {
                        case UndertaleRoom.LayerType.Instances:
                            DrawInstances(gd, g, room, layer, tints);
                            break;
                        case UndertaleRoom.LayerType.Background:
                            // Foreground background layers are drawn once in the pass below (never twice).
                            if (layer.BackgroundData?.Foreground != true)
                                DrawBackgroundLayer(gd, g, room, layer, tints, trueBackground);
                            break;
                        case UndertaleRoom.LayerType.Assets:
                            DrawAssetsLayer(gd, g, room, layer, tints);
                            break;
                        case UndertaleRoom.LayerType.Tiles:
                            DrawNativeTiles(gd, g, room, layer);
                            break;
                        case UndertaleRoom.LayerType.Path:
                        case UndertaleRoom.LayerType.Path2:
                            DrawPathPlaceholder(g, layer);
                            break;
                    }
                }
                // GMS2 "foreground" background layers are drawn above every other layer.
                foreach (var layer in room.Layers.OrderByDescending(l => l.LayerDepth))
                {
                    if (!layer.IsVisible) continue;
                    if (layer.LayerType == UndertaleRoom.LayerType.Background && layer.BackgroundData?.Foreground == true)
                        DrawBackgroundLayer(gd, g, room, layer, tints, trueBackground);
                }
            }
        }
        finally
        {
            foreach (var a in tints.Values) a.Dispose();
        }
        return bmp;
    }

    /// <summary>Draw one sprite frame with GM semantics (origin, scale, rotation, tint).</summary>
    public static void DrawSpriteFrame(Graphics g, Bitmap bmp, UndertaleSprite spr, float x, float y,
        float scaleX, float scaleY, float rotation, uint color, ImageAttributes? tint = null)
    {
        var state = g.Save();
        try
        {
            g.TranslateTransform(x, y);
            if (rotation != 0f) g.RotateTransform(-rotation);
            g.ScaleTransform(scaleX, scaleY);
            var dest = new RectangleF(-spr.OriginX, -spr.OriginY, bmp.Width, bmp.Height);
            if (tint == null)
                g.DrawImage(bmp, dest.X, dest.Y, dest.Width, dest.Height);
            else
                g.DrawImage(bmp, Rectangle.Round(dest), 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, tint);
        }
        finally { g.Restore(state); }
    }

    /// <summary>
    /// Draw a sprite frame, applying GMS2 nine-slice stretching when the sprite has an enabled
    /// nine-slice and is actually scaled. Unscaled draws are pixel-identical to a plain frame.
    /// </summary>
    public static void DrawSprite(Graphics g, Bitmap bmp, UndertaleSprite spr, float x, float y,
        float scaleX, float scaleY, float rotation, uint color, ImageAttributes? tint = null)
    {
        var ns = spr.V3NineSlice;
        bool scaled = Math.Abs(scaleX - 1f) > 0.0001f || Math.Abs(scaleY - 1f) > 0.0001f;
        if (ns != null && ns.Enabled && scaled && NineSliceFits(bmp, ns))
            DrawNineSlice(g, bmp, spr, ns, x, y, scaleX, scaleY, rotation, tint);
        else
            DrawSpriteFrame(g, bmp, spr, x, y, scaleX, scaleY, rotation, color, tint);
    }

    private static bool NineSliceFits(Bitmap bmp, UndertaleSprite.NineSlice ns)
    {
        if (ns.Left < 0 || ns.Top < 0 || ns.Right < 0 || ns.Bottom < 0) return false;
        if (ns.Left + ns.Right > bmp.Width || ns.Top + ns.Bottom > bmp.Height) return false;
        return ns.Left + ns.Right > 0 || ns.Top + ns.Bottom > 0;
    }

    /// <summary>
    /// GMS2 nine-slice renderer. Corners keep their source pixel size, edges stretch/repeat on one
    /// axis and the centre on both. Coordinates are in the sprite's local space (origin at 0,0),
    /// inside the same translate/rotate/scale transform as a normal frame, so hit-testing with
    /// SpriteContains keeps working. TileModes order is left, top, right, bottom, centre.
    /// </summary>
    private static void DrawNineSlice(Graphics g, Bitmap bmp, UndertaleSprite spr,
        UndertaleSprite.NineSlice ns, float x, float y, float scaleX, float scaleY,
        float rotation, ImageAttributes? tint)
    {
        float ax = Math.Abs(scaleX), ay = Math.Abs(scaleY);
        if (ax < 0.0001f || ay < 0.0001f) return;
        int w = bmp.Width, h = bmp.Height;
        int l = ns.Left, t = ns.Top, r = ns.Right, b = ns.Bottom;
        if (l + r > w || t + b > h) return;

        var state = g.Save();
        try
        {
            g.TranslateTransform(x, y);
            if (rotation != 0f) g.RotateTransform(-rotation);
            g.ScaleTransform(scaleX, scaleY);

            float fx = 1f / ax, fy = 1f / ay;
            float bx = -spr.OriginX, by = -spr.OriginY;
            float lw = l * fx, rw = r * fx, th = t * fy, bh = b * fy;
            float cw = w - lw - rw, ch = h - th - bh;

            int ModeAt(int index) => ns.TileModes != null && index >= 0 && index < ns.TileModes.Length
                ? (int)ns.TileModes[index] : 0;

            void Patch(RectangleF dest, RectangleF src, bool flipX = false, bool flipY = false)
            {
                if (dest.Width <= 0f || dest.Height <= 0f || src.Width <= 0f || src.Height <= 0f) return;
                if (!flipX && !flipY)
                {
                    DrawBitmapPart(g, bmp, dest, src, tint);
                    return;
                }
                var st = g.Save();
                try
                {
                    g.TranslateTransform(dest.X + (flipX ? dest.Width : 0f), dest.Y + (flipY ? dest.Height : 0f));
                    g.ScaleTransform(flipX ? -1f : 1f, flipY ? -1f : 1f);
                    DrawBitmapPart(g, bmp, new RectangleF(0f, 0f, dest.Width, dest.Height), src, tint);
                }
                finally { g.Restore(st); }
            }

            // One-dimensional tiling for an edge. Repeat uses source-sized tiles (in final pixels)
            // and clips the last tile; Mirror alternates flips; BlankRepeat/Hide draw nothing.
            void StripX(RectangleF dest, float srcX, float srcY, float srcW, float srcH, int mode)
            {
                if (dest.Width <= 0f || dest.Height <= 0f || srcW <= 0f || srcH <= 0f) return;
                if (mode == 3 || mode == 4) return;
                if (mode == 0)
                {
                    Patch(dest, new RectangleF(srcX, srcY, srcW, srcH));
                    return;
                }
                float step = srcW * fx;
                if (step <= 0.0001f) return;
                int i = 0;
                for (float px = dest.X; px < dest.Right - 0.0001f; px += step, i++)
                {
                    float dw = Math.Min(step, dest.Right - px);
                    float sw = Math.Min(srcW, dw * ax);
                    Patch(new RectangleF(px, dest.Y, dw, dest.Height), new RectangleF(srcX, srcY, sw, srcH),
                        mode == 2 && (i & 1) != 0, false);
                }
            }

            void StripY(RectangleF dest, float srcX, float srcY, float srcW, float srcH, int mode)
            {
                if (dest.Width <= 0f || dest.Height <= 0f || srcW <= 0f || srcH <= 0f) return;
                if (mode == 3 || mode == 4) return;
                if (mode == 0)
                {
                    Patch(dest, new RectangleF(srcX, srcY, srcW, srcH));
                    return;
                }
                float step = srcH * fy;
                if (step <= 0.0001f) return;
                int i = 0;
                for (float py = dest.Y; py < dest.Bottom - 0.0001f; py += step, i++)
                {
                    float dh = Math.Min(step, dest.Bottom - py);
                    float sh = Math.Min(srcH, dh * ay);
                    Patch(new RectangleF(dest.X, py, dest.Width, dh), new RectangleF(srcX, srcY, srcW, sh),
                        false, mode == 2 && (i & 1) != 0);
                }
            }

            void Center(RectangleF dest, float srcX, float srcY, float srcW, float srcH, int mode)
            {
                if (dest.Width <= 0f || dest.Height <= 0f || srcW <= 0f || srcH <= 0f) return;
                if (mode == 3 || mode == 4) return;
                if (mode == 0)
                {
                    Patch(dest, new RectangleF(srcX, srcY, srcW, srcH));
                    return;
                }
                float stepX = srcW * fx, stepY = srcH * fy;
                if (stepX <= 0.0001f || stepY <= 0.0001f) return;
                int iy = 0;
                for (float py = dest.Y; py < dest.Bottom - 0.0001f; py += stepY, iy++)
                {
                    float dh = Math.Min(stepY, dest.Bottom - py);
                    float sh = Math.Min(srcH, dh * ay);
                    int ix = 0;
                    for (float px = dest.X; px < dest.Right - 0.0001f; px += stepX, ix++)
                    {
                        float dw = Math.Min(stepX, dest.Right - px);
                        float sw = Math.Min(srcW, dw * ax);
                        Patch(new RectangleF(px, py, dw, dh), new RectangleF(srcX, srcY, sw, sh),
                            mode == 2 && (ix & 1) != 0, mode == 2 && (iy & 1) != 0);
                    }
                }
            }

            // Corners: final size is always the source border size (never stretched).
            if (l > 0 && t > 0)
                Patch(new RectangleF(bx, by, lw, th), new RectangleF(0, 0, l, t));
            if (r > 0 && t > 0)
                Patch(new RectangleF(bx + w - rw, by, rw, th), new RectangleF(w - r, 0, r, t));
            if (l > 0 && b > 0)
                Patch(new RectangleF(bx, by + h - bh, lw, bh), new RectangleF(0, h - b, l, b));
            if (r > 0 && b > 0)
                Patch(new RectangleF(bx + w - rw, by + h - bh, rw, bh), new RectangleF(w - r, h - b, r, b));

            // Edges: the span between the fixed corners.
            float cxs = bx + lw, cys = by + th;
            if (t > 0 && cw > 0f)
                StripX(new RectangleF(cxs, by, cw, th), l, 0, w - l - r, t, ModeAt(1));
            if (b > 0 && cw > 0f)
                StripX(new RectangleF(cxs, by + h - bh, cw, bh), l, h - b, w - l - r, b, ModeAt(3));
            if (l > 0 && ch > 0f)
                StripY(new RectangleF(bx, cys, lw, ch), 0, t, l, h - t - b, ModeAt(0));
            if (r > 0 && ch > 0f)
                StripY(new RectangleF(bx + w - rw, cys, rw, ch), w - r, t, r, h - t - b, ModeAt(2));

            // Centre: both axes.
            if (cw > 0f && ch > 0f)
                Center(new RectangleF(cxs, cys, cw, ch), l, t, w - l - r, h - t - b, ModeAt(4));
        }
        finally { g.Restore(state); }
    }

    private static void DrawBitmapPart(Graphics g, Bitmap bmp, RectangleF dest, RectangleF src,
        ImageAttributes? tint)
    {
        if (tint == null)
            g.DrawImage(bmp, dest, src, GraphicsUnit.Pixel);
        else
            g.DrawImage(bmp, Rectangle.Round(dest), src.X, src.Y, src.Width, src.Height,
                GraphicsUnit.Pixel, tint);
    }

    private static ImageAttributes? TintFor(uint color, Dictionary<uint, ImageAttributes> cache)
    {
        if (color == 0xFFFFFFFF) return null;
        if (cache.TryGetValue(color, out var cached)) return cached;
        var c = GameData.ColorFromGM(color);
        float a = c.A / 255f, r = c.R / 255f, gg = c.G / 255f, b = c.B / 255f;
        var cm = new ColorMatrix(new[]
        {
            new[] { r, 0f, 0f, 0f, 0f },
            new[] { 0f, gg, 0f, 0f, 0f },
            new[] { 0f, 0f, b, 0f, 0f },
            new[] { 0f, 0f, 0f, a, 0f },
            new[] { 0f, 0f, 0f, 0f, 1f },
        });
        var attr = new ImageAttributes();
        attr.SetColorMatrix(cm, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
        cache[color] = attr;
        return attr;
    }

    /// <summary>Like <see cref="TintFor"/> but keeps an explicit alpha of 0 (asset/background colours).</summary>
    private static ImageAttributes? TintForExact(uint color, Dictionary<uint, ImageAttributes> cache)
    {
        if (color == 0xFFFFFFFF) return null;
        if (cache.TryGetValue(color ^ 0x80000000u, out var cached)) return cached;
        var c = GameData.ColorFromGMExact(color);
        float a = c.A / 255f, r = c.R / 255f, gg = c.G / 255f, b = c.B / 255f;
        var cm = new ColorMatrix(new[]
        {
            new[] { r, 0f, 0f, 0f, 0f },
            new[] { 0f, gg, 0f, 0f, 0f },
            new[] { 0f, 0f, b, 0f, 0f },
            new[] { 0f, 0f, 0f, a, 0f },
            new[] { 0f, 0f, 0f, 0f, 1f },
        });
        var attr = new ImageAttributes();
        attr.SetColorMatrix(cm, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
        cache[color ^ 0x80000000u] = attr;
        return attr;
    }

    private static void DrawInstances(GameData gd, Graphics g, UndertaleRoom room, UndertaleRoom.Layer layer,
        Dictionary<uint, ImageAttributes> tints)
    {
        if (layer.InstancesData?.Instances == null) return;
        float ox = layer.XOffset, oy = layer.YOffset;
        foreach (var inst in layer.InstancesData.Instances)
        {
            if (inst.Nonexistent) continue;
            // Invisible helper objects (collision masks, controllers) must not be drawn.
            if (inst.ObjectDefinition != null && !inst.ObjectDefinition.Visible) continue;
            var spr = inst.ObjectDefinition?.Sprite;
            if (spr == null) continue;
            var frame = gd.Frame(spr, inst.ImageIndex);
            if (frame == null) continue;
            var tint = TintFor(inst.Color, tints);
            // UTMT GameObject.XOffset/YOffset (UndertaleRoom.cs:1087-1115): the target offset comes
            // from the RAW ImageIndex texture entry, while the actual frame index wraps. ElementAtOrDefault
            // semantics mean a negative/out-of-range ImageIndex contributes no target offset.
            var (tx, ty) = SpriteTargetOffset(spr, inst.ImageIndex);
            DrawSprite(g, frame, spr, ox + inst.X + tx, oy + inst.Y + ty, inst.ScaleX, inst.ScaleY,
                inst.Rotation, inst.Color, tint);
        }
    }

    private static void DrawBackgroundLayer(GameData gd, Graphics g, UndertaleRoom room, UndertaleRoom.Layer layer,
        Dictionary<uint, ImageAttributes> tints, bool trueBackground)
    {
        var bd = layer.BackgroundData;
        if (bd == null || !bd.Visible) return;
        var frame = bd.Sprite != null ? gd.Frame(bd.Sprite, (int)bd.FirstFrame) : null;
        if (frame != null)
        {
            var tint = TintForExact(bd.Color, tints);
            // LayerBackgroundData.X/YOffset already include the layer offset and the sprite target/origin delta.
            float ox = bd.XOffset, oy = bd.YOffset;
            if (bd.Stretch)
            {
                var dest = Rectangle.Round(new RectangleF(ox, oy, room.Width, room.Height));
                DrawFrame(g, frame, dest, tint);
                return;
            }
            bool h = bd.TiledHorizontally;
            bool v = bd.TiledVertically;
            if (!h && !v)
            {
                DrawFrame(g, frame, Rectangle.Round(new RectangleF(ox, oy, frame.Width, frame.Height)), tint);
                return;
            }
            int fw = frame.Width, fh = frame.Height;
            int x0 = (int)Math.Floor(ox / (float)fw) * fw;
            int y0 = (int)Math.Floor(oy / (float)fh) * fh;
            int cols = h ? (int)Math.Ceiling(room.Width / (double)fw) + 1 : 1;
            int rows = v ? (int)Math.Ceiling(room.Height / (double)fh) + 1 : 1;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    var dest = new Rectangle(x0 + c * fw, y0 + r * fh, fw, fh);
                    DrawFrame(g, frame, dest, tint);
                }
            return;
        }

        // A background layer without a sprite is a solid colour fill (GMS2 stores it in bd.Color).
        // Do not invent black when the layer/room has no colour at all.
        var color = GameData.ColorFromGMExact(bd.Color);
        if (color.A == 0 && room.DrawBackgroundColor)
            color = Color.FromArgb(255, GameData.ColorFromGMExact(room.BackgroundColor));
        if (color.A == 0) return;
        // Editor convention: an opaque pure-black colour-only layer is indistinguishable from an
        // empty backdrop and hides every native tile. Keep the file value intact but show the
        // neutral canvas instead. `trueBackground` renders the literal game colour for comparison.
        if (!trueBackground && !room.DrawBackgroundColor && color.R == 0 && color.G == 0 && color.B == 0)
            return;
        using var brush = new SolidBrush(color);
        g.FillRectangle(brush, 0, 0, (int)room.Width, (int)room.Height);
    }

    /// <summary>
    /// Room path layers do not serialise their point list in data.win (UTMT UndertaleRoom.cs:1514-1515,
    /// :1545-1557 both skip Path/Path2), so draw an honest read-only placeholder instead of silently
    /// dropping the layer. Path resources themselves remain visible in the resource list.
    /// </summary>
    private static void DrawPathPlaceholder(Graphics g, UndertaleRoom.Layer layer)
    {
        int x = (int)layer.XOffset, y = (int)layer.YOffset;
        using var pen = new Pen(Color.FromArgb(180, 255, 140, 0)) { DashStyle = DashStyle.Dash };
        g.DrawRectangle(pen, x, y, 64, 64);
        using var font = new Font(FontFamily.GenericSansSerif, 9f);
        g.DrawString("PATH: " + GameData.Name(layer.LayerName), font, Brushes.Orange, x, y - 14);
    }

    private static void DrawFrame(Graphics g, Bitmap frame, Rectangle dest, ImageAttributes? tint)
    {
        if (tint == null)
            g.DrawImage(frame, dest);
        else
            g.DrawImage(frame, dest, 0, 0, frame.Width, frame.Height, GraphicsUnit.Pixel, tint);
    }

    private static void DrawAssetsLayer(GameData gd, Graphics g, UndertaleRoom room, UndertaleRoom.Layer layer,
        Dictionary<uint, ImageAttributes> tints)
    {
        var ad = layer.AssetsData;
        if (ad == null) return;
        float ox = layer.XOffset, oy = layer.YOffset;
        if (ad.LegacyTiles != null)
        {
            foreach (var tile in ad.LegacyTiles)
            {
                var tpag = tile.Tpag ?? tile.SpriteDefinition?.Textures?.FirstOrDefault()?.Texture;
                var page = gd.Page(tpag);
                if (page == null || tpag == null) continue;
                int tw = (int)tile.Width, th = (int)tile.Height;
                if (tw <= 0 || th <= 0) continue;
                // DR legacy tiles carry the whole tileset sprite in Tpag; SourceX/Y are the offset inside it.
                int srcX = tpag.SourceX + (int)tile.SourceX;
                int srcY = tpag.SourceY + (int)tile.SourceY;
                // Some tile entries describe a larger cell than the tileset frame; never read past the sprite.
                tw = Math.Min(tw, Math.Max(1, tpag.SourceWidth - (int)tile.SourceX));
                th = Math.Min(th, Math.Max(1, tpag.SourceHeight - (int)tile.SourceY));
                float sw = tw * Math.Abs(tile.ScaleX);
                float sh = th * Math.Abs(tile.ScaleY);
                if (sw <= 0 || sh <= 0) continue;
                var dest = Rectangle.Round(new RectangleF(ox + tile.X, oy + tile.Y, tw * tile.ScaleX, th * tile.ScaleY));
                var tint = TintFor(tile.Color, tints);
                if (tint == null)
                    g.DrawImage(page, dest, srcX, srcY, tw, th, GraphicsUnit.Pixel);
                else
                    g.DrawImage(page, dest, srcX, srcY, tw, th, GraphicsUnit.Pixel, tint);
            }
        }

        // GMS2 sprite instances (LayerAssetsData.Sprites) and nine-slice instances.
        if (ad.Sprites != null)
            foreach (var si in ad.Sprites) DrawSpriteInstance(gd, g, layer, si, tints);
        if (ad.NineSlices != null)
            foreach (var si in ad.NineSlices) DrawSpriteInstance(gd, g, layer, si, tints);
    }

    /// <summary>Resolve an asset sprite instance's sprite (the serialised reference can be null in old data).</summary>
    public static UndertaleSprite? ResolveSprite(GameData gd, UndertaleRoom.SpriteInstance? si)
    {
        if (si == null) return null;
        if (si.Sprite != null) return si.Sprite;
        var name = GameData.Name(si.Name);
        if (string.IsNullOrEmpty(name)) return null;
        return gd.Sprites.FirstOrDefault(s => string.Equals(GameData.Name(s.Name), name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Frame index + world position used for drawing and hit-testing an asset sprite instance.</summary>
    public static (Bitmap? Frame, UndertaleSprite? Sprite, int FrameIndex, float X, float Y)? SpriteInstancePlacement(
        GameData gd, UndertaleRoom.Layer layer, UndertaleRoom.SpriteInstance? si)
    {
        var spr = ResolveSprite(gd, si);
        if (spr == null || si == null) return null;
        int fi = si.WrappedFrameIndex;
        var frame = gd.Frame(spr, fi);
        if (frame == null) return null;
        var tpag = spr.Textures != null && fi >= 0 && fi < spr.Textures.Count ? spr.Textures[fi]?.Texture : null;
        float tx = tpag?.TargetX ?? 0;
        float ty = tpag?.TargetY ?? 0;
        return (frame, spr, fi, layer.XOffset + si.X + tx, layer.YOffset + si.Y + ty);
    }

    private static void DrawSpriteInstance(GameData gd, Graphics g, UndertaleRoom.Layer layer,
        UndertaleRoom.SpriteInstance si, Dictionary<uint, ImageAttributes> tints)
    {
        var place = SpriteInstancePlacement(gd, layer, si);
        if (place == null) return;
        var (frame, spr, _, x, y) = place.Value;
        var tint = TintForExact(si.Color, tints);
        DrawSprite(g, frame!, spr!, x, y, si.ScaleX, si.ScaleY, si.Rotation, si.Color, tint);
    }

    /// <summary>
    /// UTMT GameObject.SpriteXOffset/SpriteYOffset source target offsets. UTMT indexes the texture list
    /// with the raw ImageIndex (not WrappedImageIndex), so out-of-range indices add nothing.
    /// </summary>
    private static (int X, int Y) SpriteTargetOffset(UndertaleSprite spr, int imageIndex)
    {
        if (spr.Textures == null || imageIndex < 0 || imageIndex >= spr.Textures.Count) return (0, 0);
        var tpag = spr.Textures[imageIndex]?.Texture;
        return (tpag?.TargetX ?? 0, tpag?.TargetY ?? 0);
    }

    private static void DrawNativeTiles(GameData gd, Graphics g, UndertaleRoom room, UndertaleRoom.Layer layer)
    {
        var bg = layer.TilesData?.Background;
        if (!NativeTiles.IsNative(layer)) return;
        foreach (var (x, y, value) in NativeTiles.Cells(layer))
        {
            int id = NativeTiles.TileId(value);
            if (id == 0) continue;
            var bmp = gd.Tile(bg, id, NativeTiles.Flags(value));
            if (bmp == null) continue;
            g.DrawImage(bmp, NativeTiles.CellRect(layer, x, y));
        }
    }

    /// <summary>Renders a single sprite frame into a small thumbnail for the object palette.</summary>
    public static Bitmap? Thumbnail(GameData gd, UndertaleSprite? spr, int maxSize = 48)
    {
        var frame = gd.Frame(spr, 0);
        if (frame == null) return null;
        float k = Math.Min(1f, Math.Min(maxSize / (float)frame.Width, maxSize / (float)frame.Height));
        int w = Math.Max(1, (int)(frame.Width * k * 2)), h = Math.Max(1, (int)(frame.Height * k * 2));
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(frame, new Rectangle(0, 0, w, h));
        return bmp;
    }
}
