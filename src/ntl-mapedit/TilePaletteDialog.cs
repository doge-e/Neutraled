using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using UndertaleModLib.Models;

namespace NtlMapEdit;

/// <summary>
/// 瓦片调色板（Loenn tileset 面板的简化版）：把整张 tileset 精灵按固定单元格切成网格，
/// 点哪一格就把「那一格的帧内子矩形」设为当前瓦片笔刷。窗口是非模态的，
/// 开着调色板也能回主窗口继续绘制；连续点不同格子即可来回换笔刷。
/// </summary>
internal sealed class TilePaletteDialog : Form
{
    private sealed class PalettePanel : Control
    {
        private readonly TilePaletteDialog _owner;
        private Bitmap? _frame;
        private int _cellW = 16, _cellH = 16;
        private float _zoom = 1f;
        private bool _autoZoom = true;
        private float _manualZoom = 2f;
        private PointF _origin;
        private Rectangle _sel = Rectangle.Empty;
        private Point _hover = new(-1, -1);

        public PalettePanel(TilePaletteDialog owner)
        {
            _owner = owner;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.Bg0;
            Cursor = Cursors.Cross;
            TabStop = true;
        }

        public Rectangle SelectedRect => _sel;

        public void SetFrame(Bitmap? frame)
        {
            _frame = frame;
            _sel = Rectangle.Empty;
            _hover = new Point(-1, -1);
            LayoutImage();
            Invalidate();
        }

        public void SetCellSize(int cellW, int cellH)
        {
            _cellW = Math.Max(1, cellW);
            _cellH = Math.Max(1, cellH);
            LayoutImage();
            Invalidate();
        }

        /// <summary>auto=true 时按面板大小取整数倍缩放；否则用指定倍率（左上对齐，便于放大看细节）。</summary>
        public void SetZoom(bool auto, float manual)
        {
            _autoZoom = auto;
            _manualZoom = Math.Clamp(manual, 0.25f, 16f);
            LayoutImage();
            Invalidate();
        }

        public void SelectCell(int col, int row)
        {
            var r = CellRect(col, row);
            if (r == Rectangle.Empty) return;
            _sel = r;
            Invalidate();
        }

        private void LayoutImage()
        {
            if (_frame == null) return;
            float fit = Math.Min((Width - 24f) / _frame.Width, (Height - 24f) / _frame.Height);
            if (fit < 0.25f) fit = 0.25f;
            if (fit >= 1f) fit = MathF.Floor(fit);   // 放大时取整数倍，像素清晰、格子边界可预测
            _zoom = _autoZoom ? fit : _manualZoom;
            _origin = new PointF((Width - _frame.Width * _zoom) / 2f, (Height - _frame.Height * _zoom) / 2f);
            if (_origin.X < 0) _origin.X = 0;
            if (_origin.Y < 0) _origin.Y = 0;
        }

        private Point ToSource(Point client)
        {
            if (_frame == null || _zoom <= 0) return Point.Empty;
            int x = (int)MathF.Floor((client.X - _origin.X) / _zoom);
            int y = (int)MathF.Floor((client.Y - _origin.Y) / _zoom);
            return new Point(Math.Clamp(x, 0, _frame.Width - 1), Math.Clamp(y, 0, _frame.Height - 1));
        }

        private bool Inside(Point client)
        {
            if (_frame == null) return false;
            float w = _frame.Width * _zoom, h = _frame.Height * _zoom;
            return client.X >= _origin.X && client.Y >= _origin.Y && client.X < _origin.X + w && client.Y < _origin.Y + h;
        }

        private Rectangle CellRect(int col, int row)
        {
            if (_frame == null) return Rectangle.Empty;
            return RoomEdit.TilePaletteCellRect(_frame.Width, _frame.Height, _cellW, _cellH, col, row);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (e.Button == MouseButtons.Left && _frame != null && Inside(e.Location))
            {
                var src = ToSource(e.Location);
                var cell = RoomEdit.TilePaletteCellAt(_frame.Width, _frame.Height, _cellW, _cellH, src.X, src.Y);
                var r = CellRect(cell.X, cell.Y);
                if (r != Rectangle.Empty)
                {
                    _sel = r;
                    Invalidate();
                    _owner.NotifyPicked(cell.X, cell.Y, r);
                }
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_frame != null)
            {
                var cell = Inside(e.Location)
                    ? RoomEdit.TilePaletteCellAt(_frame.Width, _frame.Height, _cellW, _cellH, ToSource(e.Location).X, ToSource(e.Location).Y)
                    : new Point(-1, -1);
                if (cell != _hover)
                {
                    _hover = cell;
                    _owner.NotifyHover(cell.X, cell.Y);
                    Invalidate();
                }
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (_hover != new Point(-1, -1))
            {
                _hover = new Point(-1, -1);
                _owner.NotifyHover(-1, -1);
                Invalidate();
            }
            base.OnMouseLeave(e);
        }

        protected override void OnResize(EventArgs e)
        {
            LayoutImage();
            base.OnResize(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Bg0);
            const int check = 8;
            for (int y = 0; y < Height; y += check)
                for (int x = 0; x < Width; x += check)
                {
                    bool odd = ((x / check) + (y / check)) % 2 == 1;
                    using var b = new SolidBrush(odd ? Theme.Bg1 : Theme.Bg0);
                    g.FillRectangle(b, x, y, check, check);
                }

            if (_frame == null)
            {
                using var f = Theme.UI(10f);
                TextRenderer.DrawText(g, "先点上方「选择图集…」挑一张 tileset 精灵", f,
                    new Rectangle(0, 0, Width, Height), Theme.TextDim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.SmoothingMode = SmoothingMode.None;
            var dest = new Rectangle((int)MathF.Round(_origin.X), (int)MathF.Round(_origin.Y),
                Math.Max(1, (int)MathF.Round(_frame.Width * _zoom)), Math.Max(1, (int)MathF.Round(_frame.Height * _zoom)));
            g.DrawImage(_frame, dest);

            // 单元格网格线
            using (var gp = new Pen(Color.FromArgb(80, Theme.Line)))
            {
                var grid = RoomEdit.TilePaletteGrid(_frame.Width, _frame.Height, _cellW, _cellH);
                for (int c = 1; c < grid.Width; c++)
                {
                    float x = dest.X + c * _cellW * _zoom;
                    g.DrawLine(gp, x, dest.Y, x, dest.Bottom);
                }
                for (int r = 1; r < grid.Height; r++)
                {
                    float y = dest.Y + r * _cellH * _zoom;
                    g.DrawLine(gp, dest.X, y, dest.Right, y);
                }
            }

            Rectangle ToScreen(Rectangle src) => new(
                dest.X + (int)MathF.Round(src.X * _zoom),
                dest.Y + (int)MathF.Round(src.Y * _zoom),
                Math.Max(1, (int)MathF.Round(src.Width * _zoom)),
                Math.Max(1, (int)MathF.Round(src.Height * _zoom)));

            if (_hover.X >= 0 && _hover.Y >= 0)
            {
                var hr = ToScreen(CellRect(_hover.X, _hover.Y));
                using var hp = new Pen(Color.FromArgb(160, Theme.Text));
                g.DrawRectangle(hp, hr.X, hr.Y, hr.Width - 1, hr.Height - 1);
            }
            if (_sel.Width > 0 && _sel.Height > 0)
            {
                var sr = ToScreen(_sel);
                using var fill = new SolidBrush(Color.FromArgb(70, Theme.Orange));
                g.FillRectangle(fill, sr);
                using var pen = new Pen(Theme.Orange, 2f);
                g.DrawRectangle(pen, sr.X, sr.Y, sr.Width - 1, sr.Height - 1);
            }
        }
    }

    private readonly GameData _gd;
    private readonly UndertaleRoom? _room;
    private readonly PalettePanel _panel;
    private readonly Button _assetBtn = new();
    private readonly ComboBox _nativeCombo = new();
    private readonly ComboBox _cellCombo = new();
    private readonly ComboBox _zoomCombo = new();
    private readonly Label _info = new();
    private UndertaleSprite? _spr;
    private NativeTilesetInfo? _native;
    private Bitmap? _frame;

    /// <summary>点了某格：图集精灵 + 该格的帧内源矩形（可直接喂 RoomEdit.MakeTileBrush）。</summary>
    public Action<UndertaleSprite, Rectangle>? Picked;

    /// <summary>点了原生 tileset 的某格：直接给出原生笔刷。</summary>
    public Action<NativeTileBrush>? PickedNative;

    private TilePaletteDialog(GameData gd, UndertaleRoom? room, UndertaleSprite? initial)
    {
        _gd = gd;
        _room = room;
        Text = "瓦片调色板";
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        ClientSize = new Size(760, 640);
        MinimumSize = new Size(420, 320);
        BackColor = Theme.Bg1;
        ForeColor = Theme.Text;
        Font = Theme.UI(9f);
        KeyPreview = true;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Theme.Bg1, Margin = new Padding(0) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        var top = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10, 7, 10, 0), BackColor = Theme.Bg1, WrapContents = false, Margin = new Padding(0) };
        _assetBtn.Text = "选择图集…";
        _assetBtn.Width = 96;
        _assetBtn.Height = 26;
        _assetBtn.Margin = new Padding(0, 0, 10, 0);
        Theme.StyleButton(_assetBtn);
        _assetBtn.Click += (s, e) => PickAsset();
        top.Controls.Add(_assetBtn);
        _nativeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _nativeCombo.FlatStyle = FlatStyle.Flat;
        _nativeCombo.BackColor = Theme.Bg2;
        _nativeCombo.ForeColor = Theme.Text;
        _nativeCombo.Width = 190;
        _nativeCombo.Margin = new Padding(0, 1, 10, 0);
        _nativeCombo.Items.Add("原生 tileset…");
        foreach (var info in NativeTileEdit.EnumerateTilesets(gd, room)) _nativeCombo.Items.Add(info);
        _nativeCombo.SelectedIndex = 0;
        _nativeCombo.SelectedIndexChanged += (s, e) => { if (_nativeCombo.SelectedItem is NativeTilesetInfo ni) SetNative(ni); };
        top.Controls.Add(_nativeCombo);
        top.Controls.Add(MakeTopLabel("单元格", 48));
        _cellCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _cellCombo.FlatStyle = FlatStyle.Flat;
        _cellCombo.BackColor = Theme.Bg2;
        _cellCombo.ForeColor = Theme.Text;
        _cellCombo.Width = 62;
        _cellCombo.Margin = new Padding(0, 1, 10, 0);
        foreach (var v in new[] { "8", "16", "24", "32", "48", "64" }) _cellCombo.Items.Add(v);
        _cellCombo.SelectedIndex = 1;
        _cellCombo.SelectedIndexChanged += (s, e) => ApplyCellSize();
        top.Controls.Add(_cellCombo);
        top.Controls.Add(MakeTopLabel("缩放", 34));
        _zoomCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _zoomCombo.FlatStyle = FlatStyle.Flat;
        _zoomCombo.BackColor = Theme.Bg2;
        _zoomCombo.ForeColor = Theme.Text;
        _zoomCombo.Width = 70;
        _zoomCombo.Margin = new Padding(0, 1, 10, 0);
        foreach (var v in new[] { "适应", "1x", "2x", "4x" }) _zoomCombo.Items.Add(v);
        _zoomCombo.SelectedIndex = 0;
        _zoomCombo.SelectedIndexChanged += (s, e) => ApplyZoom();
        top.Controls.Add(_zoomCombo);

        _info.Dock = DockStyle.Fill;
        _info.Padding = new Padding(12, 0, 12, 0);
        _info.TextAlign = ContentAlignment.MiddleLeft;
        Theme.StyleLabel(_info, Theme.TextDim);
        _info.Text = "先选择图集精灵；点格子即把该格设为当前瓦片笔刷";

        _panel = new PalettePanel(this) { Dock = DockStyle.Fill };

        var bottom = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 7, 10, 10), BackColor = Theme.Bg1, Margin = new Padding(0) };
        var close = new Button { Text = "关闭", Dock = DockStyle.Right, Width = 84 };
        Theme.StyleButton(close);
        close.Click += (s, e) => Close();
        var hint = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "调色板非模态：点完格子可直接回主窗口绘制（不同格子 = 不同笔刷）",
        };
        Theme.StyleLabel(hint, Theme.TextDim);
        bottom.Controls.Add(hint);
        bottom.Controls.Add(close);

        layout.Controls.Add(top, 0, 0);
        layout.Controls.Add(_info, 0, 1);
        layout.Controls.Add(_panel, 0, 2);
        layout.Controls.Add(bottom, 0, 3);
        Controls.Add(layout);

        Shown += (s, e) =>
        {
            Theme.EnableDarkTitleBar(this);
            _panel.Focus();
        };

        if (initial != null) SetSprite(initial);
    }

    private static Label MakeTopLabel(string text, int width) => new()
    {
        Text = text,
        Width = width,
        Height = 26,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Theme.TextDim,
        Font = Theme.UI(9f),
        Margin = new Padding(0, 0, 4, 0),
    };

    private void PickAsset()
    {
        var spr = AssetPickerDialog.Show(this, _gd, "选择调色板图集（整张 tileset 精灵）");
        if (spr != null) SetSprite(spr);
    }

    private void SetSprite(UndertaleSprite spr)
    {
        var frame = _gd.Frame(spr, 0);
        if (frame == null)
        {
            _info.Text = "该精灵没有可用的贴图帧";
            return;
        }
        _native = null;
        _spr = spr;
        _frame = frame;
        _panel.SetFrame(_frame);
        Text = "瓦片调色板（" + GameData.Name(spr.Name) + "）";
        ApplyCellSize();
        ApplyZoom();
    }

    private void SetNative(NativeTilesetInfo info)
    {
        var sheet = NativeTileEdit.BuildSheet(_gd, info);
        if (sheet == null)
        {
            _info.Text = "原生 tileset 没有可用贴图：" + info.Name;
            return;
        }
        _spr = null;
        _native = info;
        _frame = sheet;
        _panel.SetFrame(sheet);
        _panel.SetCellSize(info.TileWidth, info.TileHeight);
        Text = "瓦片调色板（原生 " + info.Name + "）";
        ApplyCellSize();
        ApplyZoom();
    }

    private void ApplyCellSize()
    {
        if (_native != null)
        {
            _cellCombo.Enabled = false;
            var ni = _native;
            _panel.SetCellSize(ni.TileWidth, ni.TileHeight);
            int rows = (ni.TileCount + Math.Max(1, ni.Columns) - 1) / Math.Max(1, ni.Columns);
            _info.Text = "原生 tileset " + ni.Name + "    |    tile " + ni.TileWidth + "×" + ni.TileHeight +
                         " · " + ni.Columns + " 列 × " + rows + " 行 · 共 " + ni.TileCount + " tiles    |    点格子即设为笔刷";
            return;
        }
        _cellCombo.Enabled = true;
        int v = 16;
        if (_cellCombo.SelectedItem is string s && int.TryParse(s, out int parsed)) v = parsed;
        _panel.SetCellSize(v, v);
        if (_frame == null)
        {
            _info.Text = "先选择图集精灵；点格子即把该格设为当前瓦片笔刷";
            return;
        }
        var grid = RoomEdit.TilePaletteGrid(_frame.Width, _frame.Height, v, v);
        _info.Text = "图集 " + GameData.Name(_spr?.Name) + "    |    整帧 " + _frame.Width + "×" + _frame.Height +
                     " · 单元格 " + v + "×" + v + " · 网格 " + grid.Width + "×" + grid.Height + "    |    点格子即设为笔刷";
    }

    private void ApplyZoom()
    {
        int idx = _zoomCombo.SelectedIndex;
        _panel.SetZoom(idx <= 0, idx switch { 1 => 1f, 2 => 2f, 3 => 4f, _ => 2f });
    }

    internal void NotifyPicked(int col, int row, Rectangle source)
    {
        if (_native != null)
        {
            int id = row * _native.Columns + col;
            if (id < 0 || id >= _native.TileCount)
            {
                _info.Text = "格 (" + col + "," + row + ") 超出该 tileset 的 " + _native.TileCount + " 个 tile";
                return;
            }
            var brush = NativeTileBrush.FromValue(_native.Background, (uint)id);
            _info.Text = "原生 tile " + id + " · " + _native.TileWidth + "×" + _native.TileHeight +
                         "    |    tileset " + _native.Name + "    |    已设为当前原生图块笔刷";
            PickedNative?.Invoke(brush);
            return;
        }
        _info.Text = "格 (" + col + "," + row + ") · 源 " + source.X + "," + source.Y + " · " + source.Width + "×" + source.Height +
                     "    |    整帧 " + (_frame?.Width ?? 0) + "×" + (_frame?.Height ?? 0) + "    |    已设为当前瓦片笔刷";
        if (_spr != null) Picked?.Invoke(_spr, source);
    }

    internal void NotifyHover(int col, int row)
    {
        if (_frame == null) return;
        if (col < 0 || row < 0)
        {
            ApplyCellSize();
            return;
        }
        if (_native != null)
        {
            int id = row * _native.Columns + col;
            _info.Text = "悬停 格 (" + col + "," + row + ") · tile " + (id >= 0 && id < _native.TileCount ? id.ToString() : "越界") +
                         "    |    tileset " + _native.Name + " · " + _native.TileWidth + "×" + _native.TileHeight;
            return;
        }
        var r = RoomEdit.TilePaletteCellRect(_frame.Width, _frame.Height, CellSize(), CellSize(), col, row);
        if (r == Rectangle.Empty) return;
        _info.Text = "悬停 格 (" + col + "," + row + ") · 源 " + r.X + "," + r.Y + " · " + r.Width + "×" + r.Height +
                     "    |    整帧 " + _frame.Width + "×" + _frame.Height;
    }

    private int CellSize()
    {
        if (_cellCombo.SelectedItem is string s && int.TryParse(s, out int v)) return v;
        return 16;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        // 帧位图由 GameData 缓存并负责释放（Frame() 返回的是共享缓存），这里不能 Dispose。
        _frame = null;
        base.OnFormClosed(e);
    }

    /// <summary>创建调色板窗口（非模态；由调用方 Show(owner)）。</summary>
    public static TilePaletteDialog Create(GameData gd, UndertaleSprite? initial)
        => new(gd, null, initial);

    /// <summary>创建调色板窗口；同时枚举该房间的原生 tileset（每个带 tile 尺寸与列数）。</summary>
    public static TilePaletteDialog Create(GameData gd, UndertaleRoom? room, UndertaleSprite? initial)
        => new(gd, room, initial);
}
