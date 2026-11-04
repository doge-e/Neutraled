using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using UndertaleModLib.Models;

namespace NtlMapEdit;

/// <summary>
/// 瓦片来源区域选择器：把选中的精灵帧按整数倍缩放显示，拖矩形框选帧内子区域
/// （Loenn 图块调色板的基础版——真实 tileset 都是大图，瓦片只是里面的一小块）。
/// </summary>
internal sealed class TileSourceDialog : Form
{
    private sealed class RegionPanel : Control
    {
        private readonly TileSourceDialog _owner;
        private Bitmap? _frame;
        private Rectangle _sel = Rectangle.Empty;
        private float _zoom = 1f;
        private PointF _origin;
        private bool _dragging;
        private Point _dragStart = Point.Empty;

        public RegionPanel(TileSourceDialog owner)
        {
            _owner = owner;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.Bg0;
            Cursor = Cursors.Cross;
            TabStop = true;
        }

        public Rectangle Selection => _sel;

        public void SetFrame(Bitmap? frame)
        {
            _frame = frame;
            if (_frame == null)
            {
                _sel = Rectangle.Empty;
                Invalidate();
                return;
            }
            _sel = new Rectangle(0, 0, _frame.Width, _frame.Height);
            LayoutImage();
            _owner.NotifySelection(_sel);
            Invalidate();
        }

        public void SelectAll()
        {
            if (_frame == null) return;
            _sel = new Rectangle(0, 0, _frame.Width, _frame.Height);
            _owner.NotifySelection(_sel);
            Invalidate();
        }

        private void LayoutImage()
        {
            if (_frame == null) return;
            float fit = Math.Min((Width - 24f) / _frame.Width, (Height - 24f) / _frame.Height);
            if (fit < 0.25f) fit = 0.25f;
            if (fit >= 1f) fit = MathF.Floor(fit);          // 放大时取整数倍，像素清晰、坐标可预测
            _zoom = fit;
            _origin = new PointF((Width - _frame.Width * _zoom) / 2f, (Height - _frame.Height * _zoom) / 2f);
        }

        private Point ToSource(Point client)
        {
            if (_frame == null || _zoom <= 0) return Point.Empty;
            int x = (int)MathF.Floor((client.X - _origin.X) / _zoom);
            int y = (int)MathF.Floor((client.Y - _origin.Y) / _zoom);
            return new Point(Math.Clamp(x, 0, _frame.Width - 1), Math.Clamp(y, 0, _frame.Height - 1));
        }

        private static Rectangle Normalize(Point a, Point b)
        {
            int x = Math.Min(a.X, b.X);
            int y = Math.Min(a.Y, b.Y);
            return new Rectangle(x, y, Math.Abs(a.X - b.X) + 1, Math.Abs(a.Y - b.Y) + 1);
        }

        private void ApplyDrag(Point src)
        {
            if (_frame == null) return;
            var r = Normalize(_dragStart, src);
            r.Intersect(new Rectangle(0, 0, _frame.Width, _frame.Height));
            if (r.Width < 1 || r.Height < 1) return;
            _sel = r;
            _owner.NotifySelection(_sel);
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (e.Button == MouseButtons.Left && _frame != null)
            {
                _dragging = true;
                _dragStart = ToSource(e.Location);
                ApplyDrag(_dragStart);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging) ApplyDrag(ToSource(e.Location));
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _dragging = false;
            base.OnMouseUp(e);
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            SelectAll();
            base.OnDoubleClick(e);
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
            // 棋盘底，透明像素也能看清边界
            const int cell = 8;
            for (int y = 0; y < Height; y += cell)
                for (int x = 0; x < Width; x += cell)
                {
                    bool odd = ((x / cell) + (y / cell)) % 2 == 1;
                    using var b = new SolidBrush(odd ? Theme.Bg1 : Theme.Bg0);
                    g.FillRectangle(b, x, y, cell, cell);
                }
            if (_frame == null) return;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.SmoothingMode = SmoothingMode.None;
            var dest = new Rectangle((int)MathF.Round(_origin.X), (int)MathF.Round(_origin.Y),
                Math.Max(1, (int)MathF.Round(_frame.Width * _zoom)), Math.Max(1, (int)MathF.Round(_frame.Height * _zoom)));
            g.DrawImage(_frame, dest);

            // 每 8 源像素一条参考线（放大 >=2 倍时才画，免得糊）
            if (_zoom >= 2f)
            {
                using var gp = new Pen(Color.FromArgb(60, Theme.Line));
                for (int sx = 8; sx < _frame.Width; sx += 8)
                {
                    float x = dest.X + sx * _zoom;
                    g.DrawLine(gp, x, dest.Y, x, dest.Bottom);
                }
                for (int sy = 8; sy < _frame.Height; sy += 8)
                {
                    float y = dest.Y + sy * _zoom;
                    g.DrawLine(gp, dest.X, y, dest.Right, y);
                }
            }

            if (_sel.Width > 0 && _sel.Height > 0)
            {
                var selRect = new Rectangle(
                    dest.X + (int)MathF.Round(_sel.X * _zoom),
                    dest.Y + (int)MathF.Round(_sel.Y * _zoom),
                    Math.Max(1, (int)MathF.Round(_sel.Width * _zoom)),
                    Math.Max(1, (int)MathF.Round(_sel.Height * _zoom)));
                using (var dim = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
                {
                    using var region = new Region(dest);
                    region.Exclude(selRect);
                    g.FillRegion(dim, region);
                }
                using var pen = new Pen(Theme.Orange, 2f);
                g.DrawRectangle(pen, selRect.X, selRect.Y, selRect.Width - 1, selRect.Height - 1);
            }
        }
    }

    private readonly UndertaleSprite _spr;
    private readonly Bitmap? _frame;
    private readonly Label _info = new();
    private readonly RegionPanel _region;
    private readonly Button _ok;

    public Rectangle? SelectedRect { get; private set; }

    private TileSourceDialog(GameData gd, UndertaleSprite spr)
    {
        _spr = spr;
        _frame = gd.Frame(spr, 0);
        Text = "选择图块来源区域（" + GameData.Name(spr.Name) + "）";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(720, 560);
        BackColor = Theme.Bg1;
        ForeColor = Theme.Text;
        Font = Theme.UI(9f);
        KeyPreview = true;

        var top = new Panel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(12, 7, 12, 4), BackColor = Theme.Bg1 };
        _info.Dock = DockStyle.Fill;
        _info.Text = "拖动选择子区域（双击全选整帧）";
        Theme.StyleLabel(_info, Theme.TextDim);
        top.Controls.Add(_info);

        _region = new RegionPanel(this) { Dock = DockStyle.Fill };
        var buttons = new Panel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(12, 8, 12, 10), BackColor = Theme.Bg1 };
        _ok = new Button { Text = "确定", Dock = DockStyle.Right, Width = 96, Enabled = false };
        var cancel = new Button { Text = "取消", Dock = DockStyle.Right, Width = 88, DialogResult = DialogResult.Cancel };
        var whole = new Button { Text = "整帧", Dock = DockStyle.Left, Width = 88 };
        Theme.StyleButton(_ok, true);
        Theme.StyleButton(cancel);
        Theme.StyleButton(whole);
        _ok.Click += (s, e) => Accept();
        whole.Click += (s, e) => _region.SelectAll();
        buttons.Controls.Add(_ok);
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(whole);

        Controls.Add(_region);
        Controls.Add(top);
        Controls.Add(buttons);
        CancelButton = cancel;
        AcceptButton = _ok;

        Shown += (s, e) =>
        {
            Theme.EnableDarkTitleBar(this);
            _region.SetFrame(_frame);
            _region.Focus();
        };
    }

    internal void NotifySelection(Rectangle r)
    {
        int fw = _frame?.Width ?? 0, fh = _frame?.Height ?? 0;
        if (fw <= 0 || fh <= 0)
        {
            _info.Text = "该精灵没有可用的贴图帧。";
            _ok.Enabled = false;
            return;
        }
        _info.Text = "选区 " + r.X + "," + r.Y + " · " + r.Width + "×" + r.Height + " 像素    |    整帧 " + fw + "×" + fh + "    |    拖动框选 / 双击全选";
        _ok.Enabled = r.Width > 0 && r.Height > 0;
    }

    private void Accept()
    {
        if (_frame == null) return;
        var r = _region.Selection;
        if (r.Width < 1 || r.Height < 1) return;
        SelectedRect = r;
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            Accept();
            return;
        }
        base.OnKeyDown(e);
    }

    /// <summary>弹区域选择器；取消或该精灵没有帧时返回 null。</summary>
    public static Rectangle? Show(IWin32Window owner, GameData gd, UndertaleSprite spr)
    {
        if (gd.Frame(spr, 0) == null) return null;
        using var dlg = new TileSourceDialog(gd, spr);
        return dlg.ShowDialog(owner) == DialogResult.OK ? dlg.SelectedRect : null;
    }
}
