using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using UndertaleModLib.Models;

namespace NtlMapEdit;

public enum EditorTool
{
    Select,
    Pan,
    Place,
    Erase,
    Tile,
    TileRect,
    TileLine,
    TileCircle,
    TileEllipse,
    TileFill,
    TileSelect,
}

/// <summary>
/// The map canvas: pan / zoom, room bitmap, selection overlays and the four editing tools.
/// </summary>
public sealed class CanvasPanel : Control
{
    private EditorDocument? _doc;
    private float _zoom = 1f;
    private PointF _offset;

    public EditorTool Tool { get; private set; } = EditorTool.Select;
    public bool ShowGrid { get; set; } = true;
    public bool SnapGrid { get; set; }
    public bool ShowHelpers { get; set; } = true;
    public int GridSize { get; set; } = 8;
    public UndertaleGameObject? PlaceObject { get; set; }
    public UndertaleRoom.Tile? Brush { get; set; }
    /// <summary>Native GMS2 tile brush. Mutually exclusive with <see cref="Brush"/> (legacy tiles).</summary>
    public NativeTileBrush? NativeBrush { get; set; }
    public UndertaleRoom.Layer? TargetLayer { get; set; }
    public bool SpaceHeld { get; set; }

    public event Action<PointF>? CursorMoved;
    public event Action? Edited;
    public event Action? ZoomChanged;
    public event Action<string>? Hint;
    /// <summary>选择工具下的右键菜单请求（参数：命中的实例，屏幕坐标）。</summary>
    public event Action<UndertaleRoom.GameObject?, Point>? ContextRequested;

    private enum DragMode { None, Pan, MoveInstances, Rubber, TilePaint, TileShape, TileRubber, MoveTiles }

    private DragMode _drag = DragMode.None;
    private Point _dragStartScreen;
    private PointF _dragStartWorld;
    private PointF _panStartOffset;
    private List<(UndertaleRoom.GameObject Inst, int X, int Y)> _dragOrig = new();
    private bool _dragMoved;
    private bool _dragUndoPushed;
    private PointF _rubberEndWorld;
    private UndertaleRoom.GameObject? _hover;
    private PointF _lastWorld;
    private Point? _tileLastCell;
    private Point _tileShapeStartCell;
    private Point _tileShapeEndCell;
    private bool _tileErase;
    private int _tileStrokeChanged;
    private PointF _tileRubberStartWorld;
    private PointF _tileRubberEndWorld;
    private List<(UndertaleRoom.Tile Tile, int X, int Y)> _tileMoveOrig = new();
    private bool _tileMoveMoved;
    private bool _tileMoveUndoPushed;

    public CanvasPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Theme.Bg0;
        Font = Theme.UI(9f);
        TabStop = true;
    }

    public float Zoom => _zoom;
    public EditorDocument? Document => _doc;
    /// <summary>最近一次鼠标移动的世界坐标（粘贴图块等操作用它当锚点）。</summary>
    public PointF LastWorld => _lastWorld;
    public bool IsTileTool => Tool is EditorTool.Tile or EditorTool.TileRect or EditorTool.TileLine
        or EditorTool.TileCircle or EditorTool.TileEllipse or EditorTool.TileFill;

    // ------------------------------------------------------------- lifecycle

    public void Attach(EditorDocument? doc)
    {
        _doc = doc;
        _hover = null;
        _drag = DragMode.None;
        FitToView();
        Invalidate();
    }

    public void SetTool(EditorTool tool)
    {
        if (Tool == tool) return;
        Tool = tool;
        Cursor = tool == EditorTool.Pan ? Cursors.Hand : Cursors.Cross;
        Invalidate();
    }

    /// <summary>Select a native tile brush (clears the legacy brush and retargets to a native layer).</summary>
    public void SetNativeBrush(NativeTileBrush? brush)
    {
        NativeBrush = brush;
        if (brush != null)
        {
            Brush = null;
            if (TargetLayer == null || !NativeTiles.IsNative(TargetLayer))
                TargetLayer = _doc?.Room?.Layers.OrderBy(l => l.LayerDepth).FirstOrDefault(NativeTiles.IsNative);
        }
        Invalidate();
    }

    /// <summary>Select a legacy tile brush (clears the native brush).</summary>
    public void SetLegacyBrush(UndertaleRoom.Tile? tile)
    {
        Brush = tile;
        if (tile != null)
        {
            NativeBrush = null;
            if (TargetLayer == null || TargetLayer.AssetsData?.LegacyTiles == null)
                TargetLayer = _doc?.Room?.Layers.OrderBy(l => l.LayerDepth)
                                 .FirstOrDefault(l => l.AssetsData?.LegacyTiles != null)
                             ?? (_doc?.Room != null ? RoomEdit.TilesLayer(_doc.Room) : null);
        }
        Invalidate();
    }

    // ------------------------------------------------------------ view maths

    public PointF WorldToScreen(PointF w) => new(w.X * _zoom + _offset.X, w.Y * _zoom + _offset.Y);
    public PointF ScreenToWorld(Point p) => new((p.X - _offset.X) / _zoom, (p.Y - _offset.Y) / _zoom);

    public void FitToView()
    {
        if (_doc?.Room == null || Width <= 0 || Height <= 0)
        {
            _zoom = 1f;
            _offset = PointF.Empty;
            ZoomChanged?.Invoke();
            Invalidate();
            return;
        }
        float rw = Math.Max(1f, _doc.Room.Width);
        float rh = Math.Max(1f, _doc.Room.Height);
        const float pad = 28f;
        float z = Math.Min((Width - pad * 2) / rw, (Height - pad * 2) / rh);
        z = Math.Clamp(z, 0.05f, 8f);
        _zoom = z;
        _offset = new PointF((Width - rw * z) / 2f, (Height - rh * z) / 2f);
        ZoomChanged?.Invoke();
        Invalidate();
    }

    public void SetZoom(float z, Point? anchor = null)
    {
        z = Math.Clamp(z, 0.05f, 12f);
        if (Math.Abs(z - _zoom) < 0.0001f) return;
        var a = anchor ?? new Point(Width / 2, Height / 2);
        var world = ScreenToWorld(a);
        _zoom = z;
        _offset = new PointF(a.X - world.X * z, a.Y - world.Y * z);
        ZoomChanged?.Invoke();
        Invalidate();
    }

    public void ZoomBy(float factor, Point? anchor = null) => SetZoom(_zoom * factor, anchor);

    /// <summary>Drops the cached room bitmap so the next paint re-renders it.</summary>
    public void RefreshRoom()
    {
        if (_doc?.Room != null) _doc.InvalidateRender(_doc.RoomName());
        Invalidate();
    }

    // ---------------------------------------------------------------- painting

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Bg0);
        if (_doc?.Room == null)
        {
            DrawEmpty(g);
            return;
        }
        var room = _doc.Room;
        var dest = new RectangleF(_offset.X, _offset.Y, room.Width * _zoom, room.Height * _zoom);

        using (var shadow = new SolidBrush(Color.FromArgb(80, 0, 0, 0)))
            g.FillRectangle(shadow, dest.X + 4, dest.Y + 4, dest.Width, dest.Height);

        var roomBitmap = _doc.RenderRoom(room);
        if (roomBitmap != null)
        {
            g.InterpolationMode = _zoom >= 1f ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = _zoom >= 1f ? PixelOffsetMode.Half : PixelOffsetMode.HighQuality;
            g.DrawImage(roomBitmap, dest);
        }
        else
        {
            using var ph = new SolidBrush(Theme.Bg2);
            g.FillRectangle(ph, dest);
        }
        g.InterpolationMode = InterpolationMode.Default;
        g.PixelOffsetMode = PixelOffsetMode.Default;

        using (var pen = new Pen(Theme.Line))
            g.DrawRectangle(pen, dest.X, dest.Y, dest.Width, dest.Height);

        if (ShowGrid) DrawGrid(g, dest);
        if (ShowHelpers) DrawHelperMarkers(g);
        DrawSelection(g);
        DrawDragGhosts(g);

        if (_drag == DragMode.Rubber) DrawRubber(g);
        if (_drag == DragMode.TileRubber) DrawTileRubber(g);
        if (Tool == EditorTool.Place && PlaceObject != null) DrawPlacePreview(g);
        DrawTileMarkers(g);
        DrawNativeTileMarker(g);
        DrawSpriteMarker(g);
        DrawTileSelection(g);
        DrawNativeSelection(g);
        DrawTileHover(g);
        if (IsTileTool) DrawBrushPreview(g);
        if (_drag == DragMode.TileShape) DrawTileShapePreview(g);

        DrawRoomBadge(g, room);
    }

    private void DrawEmpty(Graphics g)
    {
        string title = "没有打开 data.win";
        string hint = "文件 → 打开 data.win (Ctrl+O)";
        using var f1 = Theme.UI(14f, FontStyle.Bold);
        using var f2 = Theme.UI(9.5f);
        var c = new Rectangle(0, 0, Width, Height);
        TextRenderer.DrawText(g, title, f1, c, Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, hint, f2, new Rectangle(0, Height / 2 + 22, Width, 24), Theme.Line, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, Environment.GetCommandLineArgs()[0], f2, new Rectangle(0, Height / 2 + 46, Width, 24), Theme.Line, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private void DrawRoomBadge(Graphics g, UndertaleRoom room)
    {
        string text = $"{GameData.Name(room.Name)}  {room.Width}×{room.Height}";
        using var f = Theme.Mono(8.5f);
        var size = TextRenderer.MeasureText(text, f);
        var rect = new Rectangle(8, 8, size.Width + 14, size.Height + 6);
        Theme.FillRounded(g, rect, 4, Color.FromArgb(160, Theme.Bg1), Theme.Line);
        TextRenderer.DrawText(g, text, f, new Rectangle(rect.X + 7, rect.Y + 3, size.Width, size.Height), Theme.TextDim);
    }

    private void DrawGrid(Graphics g, RectangleF dest)
    {
        float step = GridSize * _zoom;
        if (step < 5f) return;
        float major = step * 8f;
        using var minorPen = new Pen(Color.FromArgb(26, 255, 255, 255));
        using var majorPen = new Pen(Color.FromArgb(54, 255, 255, 255));
        int guard = 0;
        for (float x = dest.X; x <= dest.Right + 0.5f && guard < 4000; x += step, guard++)
            g.DrawLine(((int)((x - dest.X) / step) % 8 == 0) ? majorPen : minorPen, x, dest.Y, x, dest.Bottom);
        guard = 0;
        for (float y = dest.Y; y <= dest.Bottom + 0.5f && guard < 4000; y += step, guard++)
            g.DrawLine(((int)((y - dest.Y) / step) % 8 == 0) ? majorPen : minorPen, dest.X, y, dest.Right, y);
    }

    private void DrawHelperMarkers(Graphics g)
    {
        if (_doc?.Room == null) return;
        using var fill = new SolidBrush(Color.FromArgb(28, Theme.Red));
        using var pen = new Pen(Color.FromArgb(110, Theme.Red)) { DashStyle = DashStyle.Dash };
        foreach (var (_, inst) in RoomEdit.AllInstances(_doc.Room))
        {
            if (inst.Nonexistent || !RoomGeometry.IsHelper(inst)) continue;
            var b = RoomGeometry.Bounds(inst);
            var r = new RectangleF(WorldToScreen(new PointF(b.X, b.Y)), new SizeF(b.Width * _zoom, b.Height * _zoom));
            if (r.Width < 6) { r.Inflate((6 - r.Width) / 2, 0); }
            if (r.Height < 6) { r.Inflate(0, (6 - r.Height) / 2); }
            g.FillRectangle(fill, r);
            g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
        }
    }

    private void DrawSelection(Graphics g)
    {
        if (_doc == null) return;
        using var pen = new Pen(Theme.Accent, 1.6f);
        using var handle = new SolidBrush(Theme.Accent);
        using var hoverPen = new Pen(Color.FromArgb(170, Theme.Text));
        if (_hover != null && !_doc.IsSelected(_hover))
        {
            var b = RoomGeometry.Bounds(_hover);
            var r = ToScreenRect(b);
            g.DrawRectangle(hoverPen, r.X, r.Y, r.Width, r.Height);
        }
        foreach (var inst in _doc.Selection)
        {
            var b = RoomGeometry.Bounds(inst);
            var r = ToScreenRect(b);
            g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
            if (_doc.Selection.Count == 1)
            {
                foreach (var p in new[] { new PointF(r.Left, r.Top), new PointF(r.Right, r.Top), new PointF(r.Left, r.Bottom), new PointF(r.Right, r.Bottom) })
                    g.FillRectangle(handle, p.X - 3, p.Y - 3, 6, 6);
            }
            g.DrawLine(pen, WorldToScreen(new PointF(inst.X - 4, inst.Y)), WorldToScreen(new PointF(inst.X + 4, inst.Y)));
            g.DrawLine(pen, WorldToScreen(new PointF(inst.X, inst.Y - 4)), WorldToScreen(new PointF(inst.X, inst.Y + 4)));
        }
    }

    private void DrawDragGhosts(Graphics g)
    {
        if (_drag != DragMode.MoveInstances) return;
        foreach (var (inst, _, _) in _dragOrig) DrawInstanceGhost(g, inst, 150);
    }

    private void DrawInstanceGhost(Graphics g, UndertaleRoom.GameObject inst, int alpha)
    {
        var spr = inst.ObjectDefinition?.Sprite;
        if (spr == null || _doc == null) return;
        var frame = _doc.Game.Frame(spr, inst.ImageIndex);
        if (frame == null) return;
        var state = g.Save();
        g.SmoothingMode = SmoothingMode.None;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        var p = WorldToScreen(new PointF(inst.X, inst.Y));
        g.TranslateTransform(p.X, p.Y);
        if (Math.Abs(inst.Rotation) > 0.01f) g.RotateTransform(-inst.Rotation);
        g.ScaleTransform(inst.ScaleX * _zoom, inst.ScaleY * _zoom);
        var cm = new ColorMatrix { Matrix33 = alpha / 255f };
        using var ia = new ImageAttributes();
        ia.SetColorMatrix(cm);
        var dest = Rectangle.Round(new RectangleF(-spr.OriginX, -spr.OriginY, frame.Width, frame.Height));
        g.DrawImage(frame, dest, 0, 0, frame.Width, frame.Height, GraphicsUnit.Pixel, ia);
        g.Restore(state);
    }

    private void DrawRubber(Graphics g)
    {
        var a = WorldToScreen(_dragStartWorld);
        var b = WorldToScreen(_rubberEndWorld);
        var r = new RectangleF(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
        using var fill = new SolidBrush(Theme.SelFill);
        using var pen = new Pen(Theme.Accent) { DashStyle = DashStyle.Dash };
        g.FillRectangle(fill, r);
        g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
    }

    private void DrawPlacePreview(Graphics g)
    {
        if (_doc?.Room == null || PlaceObject == null) return;
        var world = SnapPoint(_lastWorld);
        var p = WorldToScreen(world);
        var spr = PlaceObject.Sprite;
        if (spr != null && _doc != null)
        {
            var frame = _doc.Game.Frame(spr, 0);
            if (frame != null)
            {
                var state = g.Save();
                g.TranslateTransform(p.X, p.Y);
                g.ScaleTransform(_zoom, _zoom);
                var cm = new ColorMatrix { Matrix33 = 0.62f };
                using var ia = new ImageAttributes();
                ia.SetColorMatrix(cm);
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                var dest = Rectangle.Round(new RectangleF(-spr.OriginX, -spr.OriginY, frame.Width, frame.Height));
                g.DrawImage(frame, dest, 0, 0, frame.Width, frame.Height, GraphicsUnit.Pixel, ia);
                g.Restore(state);
            }
        }
        using var pen = new Pen(Theme.Accent);
        g.DrawLine(pen, p.X - 8, p.Y, p.X + 8, p.Y);
        g.DrawLine(pen, p.X, p.Y - 8, p.X, p.Y + 8);
        using var f = Theme.Mono(8f);
        string name = GameData.Name(PlaceObject.Name);
        var size = TextRenderer.MeasureText(name, f);
        TextRenderer.DrawText(g, name, f, new Point((int)p.X + 10, (int)p.Y + 8), Theme.Accent);
    }

    // ------------------------------------------------------------------ tiles

    private static RectangleF TileWorldRect(UndertaleRoom.Tile t)
    {
        float sx = Math.Abs(t.ScaleX) < 0.0001f ? 1f : Math.Abs(t.ScaleX);
        float sy = Math.Abs(t.ScaleY) < 0.0001f ? 1f : Math.Abs(t.ScaleY);
        return new RectangleF(t.X, t.Y, t.Width * sx, t.Height * sy);
    }

    private void DrawTileMarkers(Graphics g)
    {
        var tile = _doc?.SelectedTile;
        if (tile == null) return;
        var r = ToScreenRect(TileWorldRect(tile));
        using var fill = new SolidBrush(Color.FromArgb(46, Theme.Orange));
        using var pen = new Pen(Theme.Orange, 1.8f);
        g.FillRectangle(fill, r);
        g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
        if (r.Width > 40 && r.Height > 40)
        {
            using var f = Theme.Mono(8f);
            TextRenderer.DrawText(g, "tile", f, new Point((int)r.X + 4, (int)r.Y + 3), Theme.Orange);
        }
    }

    /// <summary>Topmost non-empty cell in a native GMS2 tile layer.</summary>
    public NativeTileRef? PickNativeTile(PointF world)
    {
        if (_doc?.Room?.Layers == null) return null;
        foreach (var layer in _doc.Room.Layers.OrderBy(l => l.LayerDepth))
        {
            if (!layer.IsVisible || !NativeTiles.IsNative(layer)) continue;
            if (!NativeTiles.CellAt(layer, world, out int x, out int y)) continue;
            if (NativeTiles.TileId(NativeTiles.Get(layer, x, y)) == 0) continue;
            return new NativeTileRef(layer, x, y);
        }
        return null;
    }

    /// <summary>Topmost asset-layer sprite (Sprites or NineSlices) under the cursor.</summary>
    public (UndertaleRoom.Layer Layer, UndertaleRoom.SpriteInstance Sprite)? PickSprite(PointF world)
    {
        if (_doc?.Room?.Layers == null) return null;
        foreach (var layer in _doc.Room.Layers.OrderBy(l => l.LayerDepth))
        {
            if (!layer.IsVisible) continue;
            var ad = layer.AssetsData;
            if (ad == null) continue;
            if (HitSpriteList(layer, ad.NineSlices, world, out var nine)) return (layer, nine!);
            if (HitSpriteList(layer, ad.Sprites, world, out var spr)) return (layer, spr!);
        }
        return null;
    }

    private bool HitSpriteList(UndertaleRoom.Layer layer, IReadOnlyList<UndertaleRoom.SpriteInstance>? list,
        PointF world, out UndertaleRoom.SpriteInstance? hit)
    {
        hit = null;
        if (list == null || _doc == null) return false;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (SpriteContains(layer, list[i], world)) { hit = list[i]; return true; }
        }
        return false;
    }

    /// <summary>
    /// 精灵实例在局部坐标里的可点范围。九宫格缩放的最终外框仍是 frame * scale：四角在设备像素上
    /// 保持源大小，中段吸收剩余空间（DrawNineSlice 的 lw + cw + rw 恒等于 frame.Width）。因此逆变换
    /// 回局部空间后命中框就是整帧，不需要按边框重算。
    /// </summary>
    private static SizeF SpriteLocalSize(UndertaleSprite spr, Bitmap frame, float scaleX, float scaleY)
    {
        _ = spr;
        _ = scaleX;
        _ = scaleY;
        return new SizeF(frame.Width, frame.Height);
    }

    private bool SpriteContains(UndertaleRoom.Layer layer, UndertaleRoom.SpriteInstance si, PointF world)
    {
        if (_doc == null) return false;
        var place = RoomRender.SpriteInstancePlacement(_doc.Game, layer, si);
        if (place == null) return false;
        var (frame, spr, _, x, y) = place.Value;
        float sx = Math.Abs(si.ScaleX) < 0.0001f ? 1f : si.ScaleX;
        float sy = Math.Abs(si.ScaleY) < 0.0001f ? 1f : si.ScaleY;
        using var m = new Matrix();
        m.Translate(x, y);
        if (Math.Abs(si.Rotation) > 0.001f) m.Rotate(-si.Rotation);
        m.Scale(sx, sy);
        try { m.Invert(); } catch (InvalidOperationException) { return false; }
        var pts = new[] { world };
        m.TransformPoints(pts);
        var p = pts[0];
        var local = SpriteLocalSize(spr!, frame!, sx, sy);
        return p.X >= -spr!.OriginX && p.X <= -spr.OriginX + local.Width
            && p.Y >= -spr.OriginY && p.Y <= -spr.OriginY + local.Height;
    }

    private void DrawNativeTileMarker(Graphics g)
    {
        var tile = _doc?.SelectedNativeTile;
        if (tile == null || _doc == null || _doc.NativeSelectionCount > 1) return;
        var world = NativeTiles.CellRect(tile.Layer, tile.X, tile.Y);
        var r = ToScreenRect(new RectangleF(world.X, world.Y, world.Width, world.Height));
        using var fill = new SolidBrush(Color.FromArgb(46, Theme.Orange));
        using var pen = new Pen(Theme.Orange, 1.8f);
        g.FillRectangle(fill, r);
        g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
        using var f = Theme.Mono(8f);
        TextRenderer.DrawText(g, $"tile {tile.TileId}", f, new Point((int)r.X + 3, (int)r.Y + 2), Theme.Orange);
    }

    private void DrawSpriteMarker(Graphics g)
    {
        var si = _doc?.SelectedSprite;
        var layer = _doc?.SelectedSpriteLayer;
        if (si == null || layer == null || _doc == null) return;
        var place = RoomRender.SpriteInstancePlacement(_doc.Game, layer, si);
        if (place == null) return;
        var (frame, spr, _, x, y) = place.Value;
        float sx = Math.Abs(si.ScaleX) < 0.0001f ? 1f : Math.Abs(si.ScaleX);
        float sy = Math.Abs(si.ScaleY) < 0.0001f ? 1f : Math.Abs(si.ScaleY);
        var local = SpriteLocalSize(spr!, frame!, sx, sy);
        var world = new RectangleF(x - spr!.OriginX, y - spr.OriginY, local.Width * sx, local.Height * sy);
        var r = ToScreenRect(world);
        using var fill = new SolidBrush(Color.FromArgb(40, Theme.Accent));
        using var pen = new Pen(Theme.Accent, 1.8f);
        g.FillRectangle(fill, r);
        g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
    }

    private void DrawBrushPreview(Graphics g)
    {
        if (_doc?.Room == null) return;
        var layer = TileTargetLayer();
        if (layer == null) return;
        bool native = NativeTiles.IsNative(layer);
        if (native ? NativeBrush == null : Brush == null) return;
        var world = IsTileTool ? SnapBrushCell(layer, _lastWorld) : SnapPoint(_lastWorld);
        var size = native ? NativeTileEdit.CellSize(layer) : RoomEdit.TileWorldSize(Brush!);
        var dest = ToScreenRect(new RectangleF(world.X, world.Y, size.Width, size.Height));
        if (native)
        {
            var page = _doc.Game.Page(NativeBrush!.Background?.Texture);
            var src = NativeTiles.SourceRect(NativeBrush.Background, NativeBrush.TileId);
            if (page != null && src.Width > 0 && src.Height > 0)
            {
                int px = src.X, py = src.Y;
                int w = Math.Min(src.Width, page.Width - px), h = Math.Min(src.Height, page.Height - py);
                if (w > 0 && h > 0)
                {
                    var state = g.Save();
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    using var ia = new ImageAttributes();
                    ia.SetColorMatrix(new ColorMatrix { Matrix33 = 0.62f });
                    g.DrawImage(page, Rectangle.Round(dest), px, py, w, h, GraphicsUnit.Pixel, ia);
                    g.Restore(state);
                }
            }
        }
        else
        {
            var page = _doc.Game.Page(Brush!.Tpag);
            if (page != null)
            {
                int px = (Brush.Tpag?.SourceX ?? 0) + Brush.SourceX;
                int py = (Brush.Tpag?.SourceY ?? 0) + Brush.SourceY;
                int w = Math.Min((int)Brush.Width, page.Width - px);
                int h = Math.Min((int)Brush.Height, page.Height - py);
                if (w > 0 && h > 0)
                {
                    var state = g.Save();
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    var cm = new ColorMatrix { Matrix33 = 0.62f };
                    using var ia = new ImageAttributes();
                    ia.SetColorMatrix(cm);
                    g.DrawImage(page, Rectangle.Round(dest), px, py, w, h, GraphicsUnit.Pixel, ia);
                    g.Restore(state);
                }
            }
        }
        using var pen = new Pen(Theme.Orange);
        g.DrawRectangle(pen, dest.X, dest.Y, dest.Width, dest.Height);
    }

    private void DrawTileShapePreview(Graphics g)
    {
        if (_doc?.Room == null) return;
        var layer = TileTargetLayer();
        if (layer == null) return;
        if (NativeTiles.IsNative(layer) ? NativeBrush == null : Brush == null) return;
        var cellSize = BrushCellSize(layer);
        var cells = ShapeCells(Tool, _tileShapeStartCell, _tileShapeEndCell, cellSize);
        if (cells.Count > 6000) return;
        using var fill = new SolidBrush(Color.FromArgb(38, Theme.Orange));
        using var outline = new Pen(Theme.Orange) { DashStyle = DashStyle.Dash };
        foreach (var c in cells)
        {
            var r = ToScreenRect(new RectangleF(c.X, c.Y, cellSize.Width, cellSize.Height));
            g.FillRectangle(fill, r);
            g.DrawRectangle(outline, r.X, r.Y, r.Width, r.Height);
        }
    }

    public UndertaleRoom.Tile? PickTile(PointF world)
    {
        if (_doc?.Room?.Layers == null) return null;
        foreach (var layer in _doc.Room.Layers.OrderBy(l => l.LayerDepth))
        {
            if (!layer.IsVisible) continue;
            var list = layer.AssetsData?.LegacyTiles;
            if (list == null) continue;
            for (int i = list.Count - 1; i >= 0; i--)
                if (TileWorldRect(list[i]).Contains(world)) return list[i];
        }
        return null;
    }

    private UndertaleRoom.Layer? TileTargetLayer()
    {
        if (_doc?.Room == null) return null;
        if (NativeBrush != null)
        {
            if (TargetLayer != null && NativeTiles.IsNative(TargetLayer)) return TargetLayer;
            return _doc.Room.Layers.OrderBy(l => l.LayerDepth).FirstOrDefault(NativeTiles.IsNative);
        }
        if (TargetLayer != null &&
            (TargetLayer.AssetsData?.LegacyTiles != null || NativeTiles.IsNative(TargetLayer)))
            return TargetLayer;
        return RoomEdit.TilesLayer(_doc.Room)
            ?? _doc.Room.Layers.OrderBy(l => l.LayerDepth).FirstOrDefault(NativeTiles.IsNative);
    }

    private bool EnsureTileBrush(PointF world)
    {
        if (NativeBrush != null || Brush != null) return true;
        PickTileAsBrush(world);
        if (NativeBrush != null || Brush != null) return true;
        Hint?.Invoke("先在右侧「瓦片笔刷」选一个图块，或按住 Alt 点击画布拾取");
        return false;
    }

    /// <summary>Top-left world pixel of the cell under the cursor. Native cells are converted from
    /// grid index back to the same pixel-cell convention the legacy tools use.</summary>
    private Point SnapBrushCell(UndertaleRoom.Layer layer, PointF world)
    {
        if (!NativeTiles.IsNative(layer))
            return Brush != null ? RoomEdit.SnapToTileCell(Brush, world) : new Point((int)MathF.Round(world.X), (int)MathF.Round(world.Y));
        var (w, h) = NativeTiles.CellSize(layer);
        int cx = (int)Math.Floor((world.X - layer.XOffset) / w);
        int cy = (int)Math.Floor((world.Y - layer.YOffset) / h);
        return new Point((int)MathF.Round(layer.XOffset) + cx * w, (int)MathF.Round(layer.YOffset) + cy * h);
    }

    /// <summary>Pixel top-left cells (legacy convention) -> native grid cells.</summary>
    private static List<Point> NativeCells(UndertaleRoom.Layer layer, IEnumerable<Point> pixels)
    {
        var (w, h) = NativeTiles.CellSize(layer);
        int ox = (int)MathF.Round(layer.XOffset);
        int oy = (int)MathF.Round(layer.YOffset);
        var cells = new List<Point>();
        foreach (var p in pixels)
            cells.Add(new Point((int)Math.Floor((p.X - ox) / (double)w), (int)Math.Floor((p.Y - oy) / (double)h)));
        return cells;
    }

    private SizeF BrushCellSize(UndertaleRoom.Layer layer)
    {
        if (NativeTiles.IsNative(layer)) return NativeTileEdit.CellSize(layer);
        if (Brush != null) return RoomEdit.TileWorldSize(Brush);
        return new SizeF(8f, 8f);
    }

    /// <summary>把一批格子提交到房间（绘制或擦除），有改动时刷新渲染并累加本笔计数。</summary>
    private void CommitTileCells(UndertaleRoom.Layer layer, IReadOnlyList<Point> cells)
    {
        if (_doc?.Room == null) return;
        int changed;
        if (NativeTiles.IsNative(layer))
        {
            if (NativeBrush == null) return;
            var nativeCells = NativeCells(layer, cells);
            changed = _tileErase
                ? NativeTileEdit.EraseCells(layer, nativeCells)
                : NativeTileEdit.PaintCells(layer, NativeBrush, nativeCells);
        }
        else
        {
            if (Brush == null) return;
            changed = _tileErase
                ? RoomEdit.EraseTileCells(layer, cells)
                : RoomEdit.PaintTileCells(_doc.Game.Data, layer, Brush, cells);
        }
        if (changed <= 0) return;
        _tileStrokeChanged += changed;
        _doc.MarkDirty(_doc.RoomName());
        RefreshRoom();
        Edited?.Invoke();
        Hint?.Invoke(_tileErase ? $"已擦除 {changed} 个图块" : $"已绘制 {changed} 个图块");
    }

    /// <summary>瓦片工具：按下开始一笔连续绘制（右键为连续擦除）。</summary>
    private void BeginTilePaint(PointF world, bool erase)
    {
        if (_doc?.Room == null) return;
        var layer = TileTargetLayer();
        if (layer == null)
        {
            Hint?.Invoke("当前房间没有图块图层，无法绘制图块");
            return;
        }
        bool native = NativeTiles.IsNative(layer);
        if (native ? NativeBrush == null : Brush == null)
        {
            PickTileAsBrush(world);
            if (native ? NativeBrush == null : Brush == null)
            {
                Hint?.Invoke(native
                    ? "原生图块图层需要在「瓦片调色板」选择原生 tileset 的图块（或 Alt 点击已有原生图块）"
                    : "先在右侧「瓦片笔刷」选一个图块，或按住 Alt 点击画布拾取");
                return;
            }
        }
        _tileErase = erase;
        _tileLastCell = SnapBrushCell(layer, world);
        _tileStrokeChanged = 0;
        _doc.PushUndo();
        CommitTileCells(layer, new[] { _tileLastCell.Value });
        _drag = DragMode.TilePaint;
        Invalidate();
    }

    /// <summary>连续拖拽：上一格到当前格用 Bresenham 补格，快速拖动也不漏。</summary>
    private void TilePaintDrag(PointF world)
    {
        if (_doc?.Room == null || _tileLastCell == null) return;
        var layer = TileTargetLayer();
        if (layer == null) return;
        var cell = SnapBrushCell(layer, world);
        if (cell == _tileLastCell.Value) return;
        var cells = TileLineCells(layer, _tileLastCell.Value, cell);
        _tileLastCell = cell;
        CommitTileCells(layer, cells);
        Invalidate();
    }

    /// <summary>矩形/直线工具：按下记起点，拖动实时预览，松开时一次提交（一次撤销）。</summary>
    private void BeginTileShape(PointF world, bool erase)
    {
        if (_doc?.Room == null) return;
        var layer = TileTargetLayer();
        if (layer == null)
        {
            Hint?.Invoke("当前房间没有图块图层，无法绘制图块");
            return;
        }
        bool native = NativeTiles.IsNative(layer);
        if (native ? NativeBrush == null : Brush == null)
        {
            PickTileAsBrush(world);
            if (native ? NativeBrush == null : Brush == null)
            {
                Hint?.Invoke(native
                    ? "原生图块图层需要在「瓦片调色板」选择原生 tileset 的图块（或 Alt 点击已有原生图块）"
                    : "先在右侧「瓦片笔刷」选一个图块，或按住 Alt 点击画布拾取");
                return;
            }
        }
        _tileErase = erase;
        _tileShapeStartCell = SnapBrushCell(layer, world);
        _tileShapeEndCell = _tileShapeStartCell;
        _tileStrokeChanged = 0;
        _drag = DragMode.TileShape;
        Invalidate();
    }

    /// <summary>
    /// 矩形/直线/圆/椭圆的格子集合（四个工具共用一套提交与预览逻辑）。原生图层的格子坐标
    /// 以图层偏移为原点，而 RoomEdit 的纯函数按世界原点取模；这里先把两点归零再还原，
    /// 否则非零 XOffset/YOffset 的原生图层上直线会整体错位。
    /// </summary>
    private List<Point> ShapeCells(EditorTool tool, Point start, Point end, SizeF cell)
    {
        var layer = TileTargetLayer();
        if (layer != null && NativeTiles.IsNative(layer))
        {
            int ox = (int)MathF.Round(layer.XOffset), oy = (int)MathF.Round(layer.YOffset);
            var a = new Point(start.X - ox, start.Y - oy);
            var b = new Point(end.X - ox, end.Y - oy);
            var cells = tool switch
            {
                EditorTool.TileRect => RoomEdit.TileCellRect(a, b, cell),
                EditorTool.TileCircle => RoomEdit.TileCellCircle(a, b, cell),
                EditorTool.TileEllipse => RoomEdit.TileCellEllipse(a, b, cell),
                _ => RoomEdit.TileCellLine(a, b, cell),
            };
            return cells.Select(p => new Point(p.X + ox, p.Y + oy)).ToList();
        }
        return tool switch
        {
            EditorTool.TileRect => RoomEdit.TileCellRect(start, end, cell),
            EditorTool.TileCircle => RoomEdit.TileCellCircle(start, end, cell),
            EditorTool.TileEllipse => RoomEdit.TileCellEllipse(start, end, cell),
            _ => RoomEdit.TileCellLine(start, end, cell),
        };
    }

    /// <summary>拖拽补格：原生图层同上，先把像素点归零到图层原点。</summary>
    private List<Point> TileLineCells(UndertaleRoom.Layer layer, Point a, Point b)
    {
        var size = BrushCellSize(layer);
        if (!NativeTiles.IsNative(layer)) return RoomEdit.TileCellLine(a, b, size);
        int ox = (int)MathF.Round(layer.XOffset), oy = (int)MathF.Round(layer.YOffset);
        return RoomEdit.TileCellLine(new Point(a.X - ox, a.Y - oy), new Point(b.X - ox, b.Y - oy), size)
            .Select(p => new Point(p.X + ox, p.Y + oy)).ToList();
    }

    private void CommitTileShape()
    {
        if (_doc?.Room == null) return;
        var layer = TileTargetLayer();
        if (layer == null) return;
        if (NativeTiles.IsNative(layer) ? NativeBrush == null : Brush == null) return;
        var cellSize = BrushCellSize(layer);
        var cells = ShapeCells(Tool, _tileShapeStartCell, _tileShapeEndCell, cellSize);
        _doc.PushUndo();
        _tileStrokeChanged = 0;
        CommitTileCells(layer, cells);
        if (_tileStrokeChanged == 0) _doc.DiscardLastUndo();
    }

    /// <summary>油漆桶：左键替换连通区域，右键整片擦除。</summary>
    private void BeginTileFill(PointF world, bool erase)
    {
        if (_doc?.Room == null) return;
        var layer = TileTargetLayer();
        if (layer == null)
        {
            Hint?.Invoke("当前房间没有图块图层，无法绘制图块");
            return;
        }
        bool native = NativeTiles.IsNative(layer);
        int changed;
        if (native)
        {
            if (!NativeTileEdit.SnapCell(layer, world, out var origin))
            {
                Hint?.Invoke("光标不在原生图块图层范围内");
                return;
            }
            if (!erase && NativeBrush == null)
            {
                PickTileAsBrush(world);
                if (NativeBrush == null)
                {
                    Hint?.Invoke("原生图块图层需要在「瓦片调色板」选择原生 tileset 的图块（或 Alt 点击已有原生图块）");
                    return;
                }
            }
            _doc.PushUndo();
            changed = erase
                ? NativeTileEdit.FloodErase(layer, origin)
                : NativeTileEdit.FloodFill(layer, NativeBrush!, origin);
        }
        else
        {
            if (!EnsureTileBrush(world)) return;
            _doc.PushUndo();
            changed = erase
                ? RoomEdit.FloodEraseTiles(_doc.Room, layer, world)
                : RoomEdit.FloodFillTiles(_doc.Game.Data, _doc.Room, layer, Brush!, world);
        }
        if (changed <= 0)
        {
            _doc.DiscardLastUndo();
            Hint?.Invoke("这里没有可填充的连通区域");
            return;
        }
        _doc.MarkDirty(_doc.RoomName());
        RefreshRoom();
        Edited?.Invoke();
        Hint?.Invoke(erase ? $"已整片擦除 {changed} 个图块" : $"已整片替换 {changed} 个图块");
        Invalidate();
    }

    private void PickTileAsBrush(PointF world)
    {
        if (_doc?.Room == null) return;
        var layer = TileTargetLayer();
        if (layer != null && NativeTiles.IsNative(layer) &&
            NativeTiles.CellAt(layer, world, out int nx, out int ny) &&
            NativeTiles.TileId(NativeTiles.Get(layer, nx, ny)) != 0)
        {
            var cell = new NativeTileRef(layer, nx, ny);
            NativeBrush = NativeTileBrush.FromTile(cell);
            Brush = null;
            _doc.SelectNativeTile(cell);
            Hint?.Invoke($"已拾取原生图块 id={cell.TileId} flags={cell.Flags}（{NativeBrush.CellSize.W}×{NativeBrush.CellSize.H}）");
            Invalidate();
            return;
        }
        var tile = PickTile(world);
        if (tile == null)
        {
            Hint?.Invoke("这里没有图块可拾取");
            return;
        }
        Brush = tile;
        NativeBrush = null;
        _doc.SelectTile(tile);
        Hint?.Invoke($"已拾取图块 {GameData.Name(tile.SpriteDefinition?.Name)}（{tile.Width}×{tile.Height}）");
        Invalidate();
    }

    // -------------------------------------------------------- tile selection

    private void BeginTileSelect(bool erase)
    {
        if (_doc?.Room == null) return;
        var layer = TileTargetLayer();
        if (layer == null)
        {
            Hint?.Invoke("当前房间没有图块图层，无法选择图块");
            return;
        }
        bool shift = (ModifierKeys & Keys.Shift) != 0;
        if (NativeTiles.IsNative(layer))
        {
            bool inGrid = NativeTiles.CellAt(layer, _dragStartWorld, out int nx, out int ny);
            var native = inGrid ? new NativeTileRef(layer, nx, ny) : null;
            if (erase)
            {
                if (native == null || native.Value == 0) return;
                _doc.PushUndo();
                if (!NativeTiles.Set(layer, nx, ny, 0)) { _doc.DiscardLastUndo(); return; }
                _doc.SelectNativeTile(null);
                _doc.MarkDirty(_doc.RoomName());
                RefreshRoom();
                Edited?.Invoke();
                Hint?.Invoke("已删除 1 个原生图块");
                Invalidate();
                return;
            }
            if (shift && native != null && native.Value != 0)
            {
                _doc.ToggleNativeSelection(native);
                Hint?.Invoke($"已切换原生图块 {native.TileId}@ {nx},{ny}（共选 {_doc.NativeSelectionCount}）");
                Invalidate();
                return;
            }
            _tileRubberStartWorld = _dragStartWorld;
            _tileRubberEndWorld = _dragStartWorld;
            _drag = DragMode.TileRubber;
            Hint?.Invoke("拖拽选择原生图块区域（Shift 追加）");
            Invalidate();
            return;
        }
        var hit = RoomEdit.TileAtPoint(layer, _dragStartWorld);
        if (erase)
        {
            if (hit == null) return;
            _doc.PushUndo();
            int n = RoomEdit.RemoveTiles(layer, new[] { hit });
            if (n == 0) { _doc.DiscardLastUndo(); return; }
            _doc.MarkDirty(_doc.RoomName());
            RefreshRoom();
            Edited?.Invoke();
            Hint?.Invoke("已删除 1 个图块");
            Invalidate();
            return;
        }
        if (hit != null && shift)
        {
            _doc.ToggleTileSelection(hit);
            Invalidate();
            return;
        }
        if (hit != null)
        {
            if (!_doc.IsTileSelected(hit)) _doc.SetTileSelection(new[] { hit }, false);
            _tileMoveOrig = _doc.TileSelection.Select(t => (t, t.X, t.Y)).ToList();
            _tileMoveMoved = false;
            _tileMoveUndoPushed = false;
            _drag = DragMode.MoveTiles;
            Invalidate();
            return;
        }
        _doc.ClearTileSelection();
        _tileRubberStartWorld = _dragStartWorld;
        _tileRubberEndWorld = _dragStartWorld;
        _drag = DragMode.TileRubber;
        Invalidate();
    }

    private void CommitTileRubber()
    {
        var layer = TileTargetLayer();
        if (_doc?.Room == null || layer == null) return;
        bool shift = (ModifierKeys & Keys.Shift) != 0;
        if (NativeTiles.IsNative(layer))
        {
            var refs = NativeTileEdit.CellsInRect(layer, Normalize(_tileRubberStartWorld, _tileRubberEndWorld))
                .Where(c => NativeTiles.TileId(NativeTiles.Get(layer, c.X, c.Y)) != 0)
                .Select(c => new NativeTileRef(layer, c.X, c.Y));
            _doc.SetNativeSelection(refs, shift);
            Hint?.Invoke($"已选 {_doc.NativeSelectionCount} 个原生图块");
            Invalidate();
            return;
        }
        var hits = RoomEdit.TilesInRect(layer, Normalize(_tileRubberStartWorld, _tileRubberEndWorld));
        _doc.SetTileSelection(hits, shift);
        Hint?.Invoke($"已选 {_doc.TileSelectionCount} 个图块");
        Invalidate();
    }

    private void MoveTilesDrag(PointF world)
    {
        if (_doc == null || _tileMoveOrig.Count == 0) return;
        int dx = (int)MathF.Round(world.X - _dragStartWorld.X);
        int dy = (int)MathF.Round(world.Y - _dragStartWorld.Y);
        if (SnapGrid)
        {
            var cell = RoomEdit.TileWorldSize(_tileMoveOrig[0].Tile);
            int cw = Math.Max(1, (int)MathF.Round(cell.Width));
            int ch = Math.Max(1, (int)MathF.Round(cell.Height));
            int nx = (int)MathF.Round((_tileMoveOrig[0].X + dx) / (float)cw) * cw;
            int ny = (int)MathF.Round((_tileMoveOrig[0].Y + dy) / (float)ch) * ch;
            dx = nx - _tileMoveOrig[0].X;
            dy = ny - _tileMoveOrig[0].Y;
        }
        if (dx == 0 && dy == 0) return;
        if (!_tileMoveUndoPushed) { _doc.PushUndo(); _tileMoveUndoPushed = true; }
        _tileMoveMoved = true;
        foreach (var (tile, ox, oy) in _tileMoveOrig)
        {
            tile.X = ox + dx;
            tile.Y = oy + dy;
        }
        Hint?.Invoke($"移动图块 {dx:+#;-#;0} {dy:+#;-#;0}");
        Invalidate();
    }

    private void DrawTileSelection(Graphics g)
    {
        if (_doc == null) return;
        var tiles = _doc.TileSelection;
        if (tiles.Count == 0) return;
        using var fill = new SolidBrush(Color.FromArgb(46, Theme.Accent));
        using var pen = new Pen(Theme.Accent, 1.4f);
        foreach (var t in tiles)
        {
            var r = ToScreenRect(TileWorldRect(t));
            g.FillRectangle(fill, r);
            g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
        }
    }

    private void DrawTileHover(Graphics g)
    {
        if (_doc?.Room == null || Tool != EditorTool.TileSelect || _drag != DragMode.None) return;
        var layer = TileTargetLayer();
        if (layer == null) return;
        if (NativeTiles.IsNative(layer))
        {
            if (!NativeTiles.CellAt(layer, _lastWorld, out int cx, out int cy)) return;
            if (_doc.NativeSelectionCount > 0 && _doc.NativeSelection.Any(t => t.Layer == layer && t.X == cx && t.Y == cy)) return;
            var cellRect = ToScreenRect(NativeTiles.CellRect(layer, cx, cy));
            using var cellPen = new Pen(Color.FromArgb(170, Theme.Text)) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(cellPen, cellRect.X, cellRect.Y, cellRect.Width, cellRect.Height);
            return;
        }
        var hit = RoomEdit.TileAtPoint(layer, _lastWorld);
        if (hit == null || _doc.IsTileSelected(hit)) return;
        var r = ToScreenRect(TileWorldRect(hit));
        using var pen = new Pen(Color.FromArgb(170, Theme.Text)) { DashStyle = DashStyle.Dash };
        g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
    }

    private void DrawNativeSelection(Graphics g)
    {
        if (_doc == null || _doc.NativeSelectionCount == 0) return;
        using var fill = new SolidBrush(Color.FromArgb(56, Theme.Accent));
        using var pen = new Pen(Theme.Accent, 1.4f);
        foreach (var t in _doc.NativeSelection)
        {
            var world = NativeTiles.CellRect(t.Layer, t.X, t.Y);
            var r = ToScreenRect(new RectangleF(world.X, world.Y, world.Width, world.Height));
            g.FillRectangle(fill, r);
            g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
        }
    }

    private void DrawTileRubber(Graphics g)
    {
        var a = WorldToScreen(_tileRubberStartWorld);
        var b = WorldToScreen(_tileRubberEndWorld);
        var r = new RectangleF(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
        using var fill = new SolidBrush(Theme.SelFill);
        using var pen = new Pen(Theme.Accent) { DashStyle = DashStyle.Dash };
        g.FillRectangle(fill, r);
        g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
    }

    // ---------------------------------------------------------------- helpers

    private RectangleF ToScreenRect(RectangleF world)
    {
        var tl = WorldToScreen(new PointF(world.X, world.Y));
        return new RectangleF(tl.X, tl.Y, world.Width * _zoom, world.Height * _zoom);
    }

    private PointF SnapPoint(PointF world)
    {
        if (!SnapGrid) return world;
        return new PointF(MathF.Round(world.X / GridSize) * GridSize, MathF.Round(world.Y / GridSize) * GridSize);
    }

    private static RectangleF Normalize(PointF a, PointF b)
        => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));

    // ------------------------------------------------------------------ input

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        base.OnMouseDown(e);
        if (_doc?.Room == null) return;
        _dragStartScreen = e.Location;
        _dragStartWorld = ScreenToWorld(e.Location);
        _lastWorld = _dragStartWorld;

        bool panStart = e.Button == MouseButtons.Middle ||
                        (e.Button == MouseButtons.Left && (SpaceHeld || Tool == EditorTool.Pan));
        if (panStart)
        {
            _drag = DragMode.Pan;
            _panStartOffset = _offset;
            Cursor = Cursors.SizeAll;
            return;
        }
        if (Tool == EditorTool.TileSelect)
        {
            BeginTileSelect(e.Button == MouseButtons.Right);
            return;
        }
        if (IsTileTool)
        {
            if (e.Button == MouseButtons.Right)
            {
                switch (Tool)
                {
                    case EditorTool.Tile: BeginTilePaint(_dragStartWorld, true); break;
                    case EditorTool.TileFill: BeginTileFill(_dragStartWorld, true); break;
                    default: BeginTileShape(_dragStartWorld, true); break;
                }
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            if ((ModifierKeys & Keys.Alt) != 0) PickTileAsBrush(_dragStartWorld);
            else
            {
                switch (Tool)
                {
                    case EditorTool.Tile: BeginTilePaint(_dragStartWorld, false); break;
                    case EditorTool.TileFill: BeginTileFill(_dragStartWorld, false); break;
                    default: BeginTileShape(_dragStartWorld, false); break;
                }
            }
            return;
        }
        if (Tool == EditorTool.Select && e.Button == MouseButtons.Right)
        {
            var hitRight = Pick(_dragStartWorld);
            if (hitRight != null)
            {
                if (_doc != null && !_doc.IsSelected(hitRight)) _doc.SelectOnly(hitRight);
            }
            else
            {
                _doc?.ClearSelection();
            }
            Invalidate();
            ContextRequested?.Invoke(hitRight, PointToScreen(e.Location));
            return;
        }
        if (e.Button != MouseButtons.Left) return;

        switch (Tool)
        {
            case EditorTool.Select:
                StartSelect();
                break;
            case EditorTool.Place:
                PlaceAt(_dragStartWorld);
                break;
            case EditorTool.Erase:
                EraseAt(_dragStartWorld);
                break;
        }
    }

    private void StartSelect()
    {
        var hit = Pick(_dragStartWorld);
        var native = PickNativeTile(_dragStartWorld);
        var sprite = PickSprite(_dragStartWorld);
        int instDepth = hit != null && _doc?.Room != null
            ? RoomEdit.LayerOf(_doc.Room, hit)?.LayerDepth ?? int.MaxValue : int.MaxValue;
        int nativeDepth = native?.Layer.LayerDepth ?? int.MaxValue;
        int spriteDepth = sprite?.Layer.LayerDepth ?? int.MaxValue;
        int top = Math.Min(instDepth, Math.Min(nativeDepth, spriteDepth));
        bool shift = (ModifierKeys & Keys.Shift) != 0;
        if (native != null && nativeDepth == top)
        {
            _doc!.SelectNativeTile(native);
            Hint?.Invoke($"原生图块 {native.TileId}（flags {native.Flags}）在 {GameData.Name(native.Layer.LayerName)} [{native.X},{native.Y}]");
            Invalidate();
            return;
        }
        if (sprite != null && spriteDepth == top)
        {
            _doc!.SelectSprite(sprite.Value.Layer, sprite.Value.Sprite);
            Hint?.Invoke($"精灵实例 {GameData.Name(RoomRender.ResolveSprite(_doc.Game, sprite.Value.Sprite)?.Name)} 在 {GameData.Name(sprite.Value.Layer.LayerName)}");
            Invalidate();
            return;
        }
        if (hit != null)
        {
            if (shift) _doc!.ToggleSelection(hit);
            else if (!_doc!.IsSelected(hit)) _doc.SelectOnly(hit);
            if (_doc.IsSelected(hit))
            {
                _drag = DragMode.MoveInstances;
                _dragOrig = _doc.Selection.Select(i => (i, i.X, i.Y)).ToList();
                _dragMoved = false;
                _dragUndoPushed = false;
            }
        }
        else
        {
            if (!shift) _doc!.ClearSelection();
            _drag = DragMode.Rubber;
            _rubberEndWorld = _dragStartWorld;
        }
        Invalidate();
    }

    public UndertaleRoom.GameObject? Pick(PointF world)
    {
        if (_doc?.Room?.Layers == null) return null;
        foreach (var layer in _doc.Room.Layers.OrderBy(l => l.LayerDepth))
        {
            if (!layer.IsVisible) continue;
            var list = layer.InstancesData?.Instances;
            if (list == null) continue;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var inst = list[i];
                if (inst.Nonexistent) continue;
                if (RoomGeometry.IsHelper(inst) && !ShowHelpers) continue;
                if (RoomGeometry.Bounds(inst).Contains(world)) return inst;
            }
        }
        return null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var world = ScreenToWorld(e.Location);
        _lastWorld = world;
        if (_doc?.Room != null) CursorMoved?.Invoke(world);

        switch (_drag)
        {
            case DragMode.None:
                UpdateHover(world);
                if (Tool == EditorTool.Place || IsTileTool || Tool == EditorTool.TileSelect) Invalidate();
                return;
            case DragMode.TileRubber:
                _tileRubberEndWorld = world;
                Invalidate();
                return;
            case DragMode.MoveTiles:
                MoveTilesDrag(world);
                return;
            case DragMode.TilePaint:
                TilePaintDrag(world);
                return;
            case DragMode.TileShape:
                var shapeLayer = TileTargetLayer();
                if (shapeLayer != null)
                {
                    _tileShapeEndCell = SnapBrushCell(shapeLayer, world);
                    Invalidate();
                }
                return;
            case DragMode.Pan:
                _offset = new PointF(_panStartOffset.X + (e.X - _dragStartScreen.X), _panStartOffset.Y + (e.Y - _dragStartScreen.Y));
                Invalidate();
                return;
            case DragMode.MoveInstances:
                MoveDrag(world);
                return;
            case DragMode.Rubber:
                _rubberEndWorld = world;
                Invalidate();
                return;
        }
    }

    private void UpdateHover(PointF world)
    {
        var hit = Pick(world);
        if (ReferenceEquals(hit, _hover)) return;
        _hover = hit;
        Invalidate();
    }

    private void MoveDrag(PointF world)
    {
        if (_doc == null || _dragOrig.Count == 0) return;
        int dx = (int)MathF.Round(world.X - _dragStartWorld.X);
        int dy = (int)MathF.Round(world.Y - _dragStartWorld.Y);
        if (SnapGrid)
        {
            int nx = (int)(MathF.Round((_dragOrig[0].X + dx) / (float)GridSize) * GridSize);
            int ny = (int)(MathF.Round((_dragOrig[0].Y + dy) / (float)GridSize) * GridSize);
            dx = nx - _dragOrig[0].X;
            dy = ny - _dragOrig[0].Y;
        }
        if (dx == 0 && dy == 0) return;
        if (!_dragUndoPushed)
        {
            _doc.PushUndo();
            _dragUndoPushed = true;
        }
        _dragMoved = true;
        foreach (var (inst, ox, oy) in _dragOrig)
        {
            inst.X = ox + dx;
            inst.Y = oy + dy;
        }
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_doc?.Room != null)
        {
            if (_drag == DragMode.Rubber)
            {
                var rect = Normalize(_dragStartWorld, _rubberEndWorld);
                bool shift = (ModifierKeys & Keys.Shift) != 0;
                var hits = new List<UndertaleRoom.GameObject>();
                foreach (var (_, inst) in RoomEdit.AllInstances(_doc.Room))
                {
                    if (inst.Nonexistent) continue;
                    if (RoomGeometry.IsHelper(inst) && !ShowHelpers) continue;
                    if (RoomGeometry.Bounds(inst).IntersectsWith(rect)) hits.Add(inst);
                }
                _doc.SelectMany(hits, shift);
            }
            else if (_drag == DragMode.MoveInstances && _dragMoved)
            {
                _doc.MarkDirty(_doc.RoomName());
                RefreshRoom();
                Edited?.Invoke();
            }
            else if (_drag == DragMode.TileShape) CommitTileShape();
            else if (_drag == DragMode.TileRubber) CommitTileRubber();
            else if (_drag == DragMode.MoveTiles && _tileMoveMoved)
            {
                _doc.MarkDirty(_doc.RoomName());
                RefreshRoom();
                Edited?.Invoke();
            }
            else if (_drag == DragMode.TilePaint && _tileStrokeChanged == 0) _doc.DiscardLastUndo();
        }
        Cursor = Tool == EditorTool.Pan ? Cursors.Hand : Cursors.Cross;
        _drag = DragMode.None;
        _tileLastCell = null;
        _dragOrig.Clear();
        _dragUndoPushed = false;
        _dragMoved = false;
        _tileMoveOrig.Clear();
        _tileMoveUndoPushed = false;
        _tileMoveMoved = false;
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        float factor = e.Delta > 0 ? 1.15f : 1f / 1.15f;
        ZoomBy(factor, e.Location);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover != null)
        {
            _hover = null;
            Invalidate();
        }
    }

    // ----------------------------------------------------------- tool actions

    private void PlaceAt(PointF world)
    {
        if (_doc?.Room == null) return;
        if (PlaceObject == null)
        {
            Hint?.Invoke("先在右侧对象列表选择一个对象，再在画布上点击放置");
            return;
        }
        var layer = TargetLayer != null && TargetLayer.InstancesData?.Instances != null
            ? TargetLayer
            : RoomEdit.InstancesLayer(_doc.Room);
        if (layer == null)
        {
            Hint?.Invoke("当前房间没有实例图层，无法放置");
            return;
        }
        var pt = SnapPoint(world);
        _doc.PushUndo();
        var inst = RoomEdit.AddInstance(_doc.Game.Data, _doc.Room, layer, PlaceObject, (int)MathF.Round(pt.X), (int)MathF.Round(pt.Y));
        _doc.SelectOnly(inst);
        _doc.MarkDirty(_doc.RoomName());
        RefreshRoom();
        Hint?.Invoke($"已放置 {GameData.Name(PlaceObject.Name)} @ ({(int)pt.X}, {(int)pt.Y})");
        Edited?.Invoke();
        Invalidate();
    }

    private void EraseAt(PointF world)
    {
        if (_doc?.Room == null) return;
        var hit = Pick(world);
        var native = PickNativeTile(world);
        int instDepth = hit != null ? RoomEdit.LayerOf(_doc.Room, hit)?.LayerDepth ?? int.MaxValue : int.MaxValue;
        int nativeDepth = native?.Layer.LayerDepth ?? int.MaxValue;
        // X 擦除按最上层内容决定目标：原生 tile 与实例谁在上擦谁；没有原生 tile 时保持旧的实例行为。
        if (native != null && nativeDepth <= instDepth)
        {
            _doc.PushUndo();
            int changed = NativeTileEdit.EraseCells(native.Layer, new[] { new Point(native.X, native.Y) });
            if (changed > 0)
            {
                _doc.SelectNativeTile(null);
                _doc.MarkDirty(_doc.RoomName());
                RefreshRoom();
                Hint?.Invoke($"已擦除原生图块 {native.TileId}（flags {native.Flags}）在 {GameData.Name(native.Layer.LayerName)} [{native.X},{native.Y}]");
                Edited?.Invoke();
                Invalidate();
                return;
            }
            _doc.DiscardLastUndo();
        }
        if (hit == null) return;
        _doc.PushUndo();
        RoomEdit.RemoveInstance(_doc.Room, hit);
        _doc.Selection.Remove(hit);
        _doc.MarkDirty(_doc.RoomName());
        RefreshRoom();
        Hint?.Invoke($"已删除 {GameData.Name(hit.ObjectDefinition?.Name)} (ID {hit.InstanceID})");
        Edited?.Invoke();
        Invalidate();
    }
}
