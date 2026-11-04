using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using UndertaleModLib.Models;

namespace NtlMapEdit;

/// <summary>
/// Dark modal picker over the game's sprite resources. Used when a tile brush needs a
/// source that does not exist in the current room yet (new tile layers / empty rooms) and
/// when a background layer gets a different sprite.
/// </summary>
internal sealed class AssetPickerDialog : Form
{
    private sealed class Row
    {
        public UndertaleSprite Spr = null!;
        public string Name = "";
        public string Detail = "";
        public Bitmap? Thumb;
    }

    private readonly GameData _gd;
    private readonly List<Row> _all = new();
    private readonly List<Row> _rows = new();
    private readonly TextBox _filter;
    private readonly ListBox _list = new();
    private readonly PictureBox _preview = new();
    private readonly Label _info = new();
    private readonly Button _ok;

    public UndertaleSprite? Selected { get; private set; }

    private AssetPickerDialog(GameData gd, string title)
    {
        _gd = gd;
        Text = title;
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(680, 460);
        MinimumSize = new Size(520, 320);
        BackColor = Theme.Bg1;
        ForeColor = Theme.Text;
        Font = Theme.UI(9f);
        KeyPreview = true;

        _filter = new TextBox { Dock = DockStyle.Top };
        Theme.StyleTextBox(_filter);
        _filter.PlaceholderText = "筛选精灵名…";
        var filterHost = new Panel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(10, 8, 10, 4), BackColor = Theme.Bg1 };
        filterHost.Controls.Add(_filter);
        _filter.Dock = DockStyle.Fill;

        _list.Dock = DockStyle.Fill;
        _list.DrawMode = DrawMode.OwnerDrawFixed;
        _list.ItemHeight = 52;
        _list.BorderStyle = BorderStyle.None;
        _list.BackColor = Theme.Bg0;
        _list.ForeColor = Theme.Text;
        _list.IntegralHeight = false;
        _list.DrawItem += DrawRow;
        _list.SelectedIndexChanged += (s, e) => UpdatePreview();
        _list.DoubleClick += (s, e) => Accept();

        var right = new Panel { Dock = DockStyle.Right, Width = 220, Padding = new Padding(10), BackColor = Theme.Bg1 };
        _preview.Dock = DockStyle.Top;
        _preview.Height = 170;
        _preview.BackColor = Theme.Bg0;
        _preview.SizeMode = PictureBoxSizeMode.Zoom;
        _info.Dock = DockStyle.Fill;
        Theme.StyleLabel(_info, Theme.TextDim);
        right.Controls.Add(_info);
        right.Controls.Add(_preview);

        var buttons = new Panel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(10, 8, 10, 8), BackColor = Theme.Bg1 };
        _ok = new Button { Text = "确定", Dock = DockStyle.Right, Width = 96 };
        var cancel = new Button { Text = "取消", Dock = DockStyle.Right, Width = 88, DialogResult = DialogResult.Cancel };
        Theme.StyleButton(_ok, true);
        Theme.StyleButton(cancel);
        _ok.Enabled = false;
        _ok.Click += (s, e) => Accept();
        buttons.Controls.Add(_ok);
        buttons.Controls.Add(cancel);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel2,
            SplitterDistance = 450,
            BackColor = Theme.Line,
        };
        split.Panel1.BackColor = Theme.Bg1;
        split.Panel2.BackColor = Theme.Bg1;
        split.Panel1.Controls.Add(_list);
        split.Panel2.Controls.Add(right);

        Controls.Add(split);
        Controls.Add(buttons);
        Controls.Add(filterHost);
        CancelButton = cancel;
        _filter.TextChanged += (s, e) => ApplyFilter();

        Shown += (s, e) =>
        {
            Theme.EnableDarkTitleBar(this);
            BuildRows();
            _filter.Focus();
        };
    }

    private void BuildRows()
    {
        _all.Clear();
        foreach (var spr in _gd.Sprites)
        {
            if (spr.Textures == null || spr.Textures.Count == 0) continue;
            var tpag = spr.Textures[0]?.Texture;
            if (tpag == null) continue;
            _all.Add(new Row
            {
                Spr = spr,
                Name = GameData.Name(spr.Name),
                Detail = $"{tpag.SourceWidth}x{tpag.SourceHeight} · {spr.Textures.Count} 帧",
            });
        }
        _all.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string q = _filter.Text.Trim();
        _rows.Clear();
        foreach (var row in _all)
            if (q.Length == 0 || row.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                _rows.Add(row);
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var row in _rows) _list.Items.Add(row);
        _list.EndUpdate();
        if (_rows.Count > 0) _list.SelectedIndex = 0;
        else
        {
            Selected = null;
            _ok.Enabled = false;
            _preview.Image = null;
            _info.Text = q.Length == 0 ? "没有可用的精灵资源。" : $"没有匹配「{q}」的精灵。";
        }
    }

    private void DrawRow(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _rows.Count) return;
        var row = _rows[e.Index];
        bool sel = (e.State & DrawItemState.Selected) != 0;
        using (var bg = new SolidBrush(sel ? Theme.Bg3 : Theme.Bg0)) e.Graphics.FillRectangle(bg, e.Bounds);
        row.Thumb ??= MakeThumb(row.Spr);
        if (row.Thumb != null)
        {
            var r = new Rectangle(e.Bounds.X + 8, e.Bounds.Y + (e.Bounds.Height - 44) / 2, 44, 44);
            using var p = new Pen(Theme.Line);
            e.Graphics.DrawRectangle(p, r);
            e.Graphics.DrawImage(row.Thumb, new Rectangle(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2));
        }
        TextRenderer.DrawText(e.Graphics, row.Name, Theme.UI(9f), new Rectangle(e.Bounds.X + 62, e.Bounds.Y + 8, e.Bounds.Width - 70, 20),
            sel ? Theme.Text : Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(e.Graphics, row.Detail, Theme.Mono(8f), new Rectangle(e.Bounds.X + 62, e.Bounds.Y + 28, e.Bounds.Width - 70, 18),
            Theme.TextDim, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private Bitmap? MakeThumb(UndertaleSprite spr)
    {
        var frame = _gd.Frame(spr, 0);
        if (frame == null) return null;
        try
        {
            var bmp = new Bitmap(44, 44, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Theme.Bg1);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            float scale = Math.Min(42f / frame.Width, 42f / frame.Height);
            int w = Math.Max(1, (int)Math.Round(frame.Width * scale));
            int h = Math.Max(1, (int)Math.Round(frame.Height * scale));
            g.DrawImage(frame, new Rectangle((44 - w) / 2, (44 - h) / 2, w, h));
            return bmp;
        }
        catch { return null; }
    }

    private void UpdatePreview()
    {
        int i = _list.SelectedIndex;
        if (i < 0 || i >= _rows.Count) return;
        var row = _rows[i];
        Selected = row.Spr;
        _ok.Enabled = true;
        _preview.Image = MakeThumb(row.Spr, 160);
        var tpag = row.Spr.Textures[0].Texture;
        _info.Text = $"名称  {row.Name}\n尺寸  {tpag?.SourceWidth}x{tpag?.SourceHeight}\n帧数  {row.Spr.Textures.Count}";
    }

    private Bitmap? MakeThumb(UndertaleSprite spr, int size)
    {
        var frame = _gd.Frame(spr, 0);
        if (frame == null) return null;
        try
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Theme.Bg0);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            float scale = Math.Min((size - 8f) / frame.Width, (size - 8f) / frame.Height);
            int w = Math.Max(1, (int)Math.Round(frame.Width * scale));
            int h = Math.Max(1, (int)Math.Round(frame.Height * scale));
            g.DrawImage(frame, new Rectangle((size - w) / 2, (size - h) / 2, w, h));
            return bmp;
        }
        catch { return null; }
    }

    private void Accept()
    {
        if (Selected == null) return;
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

    /// <summary>Shows the picker; returns the chosen sprite or null when cancelled.</summary>
    public static UndertaleSprite? Show(IWin32Window owner, GameData gd, string title)
    {
        using var dlg = new AssetPickerDialog(gd, title);
        return dlg.ShowDialog(owner) == DialogResult.OK ? dlg.Selected : null;
    }
}