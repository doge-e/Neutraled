using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NtlMapEdit;

/// <summary>Dark "Loenn-like" palette and small drawing helpers.</summary>
public static class Theme
{
    public static readonly Color Bg0 = Color.FromArgb(21, 23, 28);
    public static readonly Color Bg1 = Color.FromArgb(28, 31, 38);
    public static readonly Color Bg2 = Color.FromArgb(35, 39, 48);
    public static readonly Color Bg3 = Color.FromArgb(44, 49, 60);
    public static readonly Color Line = Color.FromArgb(55, 61, 74);
    public static readonly Color Text = Color.FromArgb(218, 222, 232);
    public static readonly Color TextDim = Color.FromArgb(138, 145, 160);
    public static readonly Color Accent = Color.FromArgb(78, 156, 255);
    public static readonly Color AccentDim = Color.FromArgb(48, 96, 160);
    public static readonly Color Green = Color.FromArgb(94, 214, 154);
    public static readonly Color Orange = Color.FromArgb(255, 178, 84);
    public static readonly Color Red = Color.FromArgb(255, 96, 96);
    public static readonly Color SelFill = Color.FromArgb(38, 78, 156, 255);

    private static string _uiFamily = "Microsoft YaHei UI";
    private static string _monoFamily = "Consolas";

    static Theme()
    {
        var families = FontFamily.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!families.Contains(_uiFamily)) _uiFamily = families.Contains("Microsoft YaHei") ? "Microsoft YaHei" : "Segoe UI";
        if (!families.Contains(_monoFamily)) _monoFamily = "Courier New";
    }

    public static Font UI(float size = 9f, FontStyle style = FontStyle.Regular) => new(_uiFamily, size, style, GraphicsUnit.Point);
    public static Font Mono(float size = 9f, FontStyle style = FontStyle.Regular) => new(_monoFamily, size, style, GraphicsUnit.Point);

    public static void StyleButton(Button b, bool accent = false)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderColor = Line;
        b.FlatAppearance.MouseOverBackColor = accent ? Color.FromArgb(60, 120, 210) : Bg3;
        b.FlatAppearance.MouseDownBackColor = accent ? AccentDim : Line;
        b.BackColor = accent ? Accent : Bg2;
        b.ForeColor = Text;
        b.Font = UI(9f);
        b.Cursor = Cursors.Hand;
        b.UseVisualStyleBackColor = false;
    }

    public static void StyleTextBox(TextBox t)
    {
        t.BackColor = Bg2;
        t.ForeColor = Text;
        t.BorderStyle = BorderStyle.FixedSingle;
        t.Font = UI(9f);
    }

    public static void StyleLabel(Label l, Color? fg = null, bool bold = false)
    {
        l.ForeColor = fg ?? Text;
        l.BackColor = Color.Transparent;
        l.Font = UI(9f, bold ? FontStyle.Bold : FontStyle.Regular);
    }

    /// <summary>Header strip used above each dock panel.</summary>
    public static Label Header(string text, int width)
    {
        var l = new Label
        {
            Text = text,
            Dock = DockStyle.Top,
            Height = 26,
            Width = width,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0),
            BackColor = Bg1,
            ForeColor = TextDim,
            Font = UI(8.5f, FontStyle.Bold),
        };
        return l;
    }

    public static Panel SeparatorH()
    {
        return new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Line };
    }

    public static void FillRounded(Graphics g, RectangleF r, float radius, Color fill, Color? border = null)
    {
        using var path = Rounded(r, radius);
        using var b = new SolidBrush(fill);
        g.FillPath(b, path);
        if (border != null)
        {
            using var p = new Pen(border.Value);
            g.DrawPath(p, path);
        }
    }

    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { path.AddRectangle(r); return path; }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("kernel32.dll")]
    private static extern uint GetConsoleProcessList(uint[] processList, uint processCount);

    /// <summary>Hides the console window when the GUI is launched from a console build.
    /// v1.1.0：编辑器并进 ntl-gui.exe（控制台子系统）之后，只有**独占**控制台才隐藏 ——
    /// 从 cmd / PowerShell / 管理器的工具箱启动时，控制台里还有 shell 或管理器进程，
    /// 不能把用户自己的终端窗口藏掉（老版本无条件 ShowWindow(0)，会连 cmd 一起隐藏）。</summary>
    public static void HideConsole()
    {
        try
        {
            var h = GetConsoleWindow();
            if (h == IntPtr.Zero) return;
            var pids = new uint[8];
            if (GetConsoleProcessList(pids, (uint)pids.Length) <= 1) ShowWindow(h, 0);
        }
        catch { /* ignore */ }
    }

    public static void EnableDarkTitleBar(Form f)
    {
        try
        {
            if (!f.IsHandleCreated) return;
            int v = 1;
            DwmSetWindowAttribute(f.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref v, sizeof(int));
        }
        catch { /* older Windows: ignore */ }
    }

    /// <summary>Colour table so standard tool strips render dark.</summary>
    public sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Bg2;
        public override Color ImageMarginGradientBegin => Bg2;
        public override Color ImageMarginGradientMiddle => Bg2;
        public override Color ImageMarginGradientEnd => Bg2;
        public override Color MenuBorder => Line;
        public override Color MenuItemBorder => AccentDim;
        public override Color MenuItemSelected => Bg3;
        public override Color MenuItemSelectedGradientBegin => Bg3;
        public override Color MenuItemSelectedGradientEnd => Bg3;
        public override Color MenuItemPressedGradientBegin => Bg2;
        public override Color MenuItemPressedGradientEnd => Bg2;
        public override Color SeparatorDark => Line;
        public override Color SeparatorLight => Bg1;
        public override Color ToolStripBorder => Line;
        public override Color ToolStripGradientBegin => Bg1;
        public override Color ToolStripGradientMiddle => Bg1;
        public override Color ToolStripGradientEnd => Bg1;
        public override Color ButtonSelectedHighlight => Bg3;
        public override Color ButtonSelectedBorder => AccentDim;
        public override Color StatusStripGradientBegin => Bg1;
        public override Color StatusStripGradientEnd => Bg1;
    }

    public static ToolStripRenderer CreateRenderer() => new ToolStripProfessionalRenderer(new DarkColorTable()) { RoundedEdges = false };

    public static ContextMenuStrip CreateMenu()
    {
        var m = new ContextMenuStrip
        {
            Renderer = CreateRenderer(),
            BackColor = Bg2,
            ForeColor = Text,
            ShowImageMargin = false,
            Font = UI(9f),
        };
        m.Renderer = CreateRenderer();
        return m;
    }

    public static ToolStripMenuItem Item(string text, EventHandler? onClick = null, Keys shortcut = Keys.None)
    {
        var item = new ToolStripMenuItem(text) { ForeColor = Text, BackColor = Bg2, ShortcutKeys = shortcut, ShowShortcutKeys = shortcut != Keys.None };
        if (onClick != null) item.Click += onClick;
        return item;
    }
}
