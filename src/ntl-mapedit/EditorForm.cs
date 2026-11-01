using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using UndertaleModLib.Models;

namespace NtlMapEdit;

/// <summary>Main window: room list, layer list, canvas, inspector and object palette.</summary>
public sealed class EditorForm : Form
{
    private EditorDocument? _doc;
    private FileKind _fileRole = FileKind.Other;
    private bool _warnedDeployed;
    private bool _syncingLists;

    private readonly CanvasPanel _canvas = new();
    private readonly ListBox _roomList = new();
    private readonly ListBox _layerList = new();
    private readonly ListBox _objList = new();
    private readonly ListBox _tileList = new();
    private readonly TextBox _roomFilter = new();
    private readonly TextBox _objFilter = new();
    private readonly Panel _inspector = new();
    private readonly TableLayoutPanel _inspectorGrid = new();
    private readonly Label _statusLeft = new();
    private readonly Label _statusCoords = new();
    private readonly Label _statusZoom = new();
    private readonly Button _pickTileSourceBtn = new();
    private readonly Button _paletteBtn = new();
    private TilePaletteDialog? _palette;
    /// <summary>与当前 data.win 同目录的战斗配置（&lt;win&gt;.ntlbattle.json）。</summary>
    private BattleProject? _battles;

    private readonly List<UndertaleGameObject> _objects = new();
    private readonly Dictionary<UndertaleGameObject, Bitmap?> _thumbs = new();
    private readonly List<RoomRow> _roomRows = new();
    private readonly List<LayerRow> _layerRows = new();
    private int _pasteSerial;
    private readonly List<ObjectRow> _objRows = new();
    private readonly List<TileRow> _tileRows = new();

    private readonly List<Button> _toolButtons = new();
    private ToolStripMenuItem? _gridItem;
    private ToolStripMenuItem? _snapItem;
    private ToolStripMenuItem? _helperItem;
    private Button? _undoBtn;
    private Button? _redoBtn;
    private Label? _zoomLabel;

    private static readonly Font RowFont = Theme.UI(8.5f);
    private static readonly Font RowFontSmall = Theme.UI(8f);
    private static readonly Font RowMono = Theme.Mono(8f);
    private static readonly Font SectionFont = Theme.UI(8f, FontStyle.Bold);

    private sealed class RoomRow
    {
        public UndertaleRoom Room = null!;
        public string Name = "";
        public string SizeText = "";
    }

    private sealed class LayerRow
    {
        public UndertaleRoom.Layer Layer = null!;
        public string Name = "";
        public string Detail = "";
    }

    private sealed class ObjectRow
    {
        public UndertaleGameObject Obj = null!;
        public string Name = "";
        public string Sprite = "";
    }

    private sealed class TileRow
    {
        public UndertaleRoom.Tile Tile = null!;
        public string Name = "";
        public string Detail = "";
        public Bitmap? Thumb;
    }

    public EditorForm(string? initialPath)
    {
        Text = "Neutraled 地图编辑器";
        Size = new Size(1520, 940);
        MinimumSize = new Size(1080, 660);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg0;
        ForeColor = Theme.Text;
        Font = Theme.UI(9f);
        KeyPreview = true;
        DoubleBuffered = true;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Theme.Bg0 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        Controls.Add(root);

        root.Controls.Add(BuildMenu(), 0, 0);
        root.Controls.Add(BuildToolbar(), 0, 1);

        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Theme.Bg0, Margin = new Padding(0) };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 344));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.Controls.Add(BuildLeftPanel(), 0, 0);
        _canvas.Dock = DockStyle.Fill;
        _canvas.Margin = new Padding(0);
        content.Controls.Add(_canvas, 1, 0);
        content.Controls.Add(BuildRightPanel(), 2, 0);
        root.Controls.Add(content, 0, 2);
        root.Controls.Add(BuildStatusBar(), 0, 3);

        _canvas.CursorMoved += w => _statusCoords.Text = $"x {(int)MathF.Round(w.X)}   y {(int)MathF.Round(w.Y)}";
        _canvas.ZoomChanged += () => { if (_zoomLabel != null) _zoomLabel.Text = $"{_canvas.Zoom * 100:0}%"; };
        _canvas.Edited += () => { RebuildLayers(); RebuildInspector(); UpdateTitle(); UpdateUndoState(); };
        _canvas.Hint += msg => _statusLeft.Text = msg;

        if (!string.IsNullOrEmpty(initialPath)) OpenPath(initialPath!, false);
    }

    // ------------------------------------------------------------------ menu

    private MenuStrip BuildMenu()
    {
        var ms = new MenuStrip
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Bg1,
            ForeColor = Theme.Text,
            Renderer = Theme.CreateRenderer(),
            GripStyle = ToolStripGripStyle.Hidden,
            Padding = new Padding(6, 2, 0, 2),
            Font = Theme.UI(9f),
            Margin = new Padding(0),
        };
        var file = TopMenu("文件(&F)");
        file.DropDownItems.Add(Theme.Item("打开 data.win…", (s, e) => OpenDialog(), Keys.Control | Keys.O));
        file.DropDownItems.Add(BuildChapterMenu());
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Theme.Item("保存", (s, e) => Save(false), Keys.Control | Keys.S));
        file.DropDownItems.Add(Theme.Item("另存为…", (s, e) => Save(true), Keys.Control | Keys.Shift | Keys.S));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Theme.Item("重新载入", (s, e) => { if (_doc != null) OpenPath(_doc.FilePath, true); }));
        file.DropDownItems.Add(Theme.Item("退出", (s, e) => Close()));

        var edit = TopMenu("编辑(&E)");
        edit.DropDownItems.Add(Theme.Item("撤销", (s, e) => DoUndo(), Keys.Control | Keys.Z));
        edit.DropDownItems.Add(Theme.Item("重做", (s, e) => DoRedo(), Keys.Control | Keys.Y));
        edit.DropDownItems.Add(new ToolStripSeparator());
        var delItem = Theme.Item("删除选中", (s, e) => DeleteSelection());
        delItem.ShortcutKeyDisplayString = "Del";
        edit.DropDownItems.Add(delItem);
        edit.DropDownItems.Add(Theme.Item("全选实例", (s, e) => SelectAllInstances(), Keys.Control | Keys.A));
        edit.DropDownItems.Add(Theme.Item("反选实例", (s, e) => InvertSelection(), Keys.Control | Keys.I));
        var deselectItem = Theme.Item("取消选择", (s, e) => _doc?.ClearSelection());
        deselectItem.ShortcutKeyDisplayString = "Esc";
        edit.DropDownItems.Add(deselectItem);
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add(Theme.Item("剪切", (s, e) => CutSelection(), Keys.Control | Keys.X));
        edit.DropDownItems.Add(Theme.Item("复制", (s, e) => CopySelection(), Keys.Control | Keys.C));
        edit.DropDownItems.Add(Theme.Item("粘贴", (s, e) => PasteClipboard(), Keys.Control | Keys.V));
        edit.DropDownItems.Add(Theme.Item("再制", (s, e) => DuplicateSelection(), Keys.Control | Keys.D));

        var room = TopMenu("房间(&R)");
        room.DropDownItems.Add(Theme.Item("新建房间（复制当前）…", (s, e) => NewRoomDialog(true), Keys.Control | Keys.N));
        room.DropDownItems.Add(Theme.Item("新建空白房间…", (s, e) => NewRoomDialog(false), Keys.Control | Keys.Shift | Keys.N));
        room.DropDownItems.Add(Theme.Item("复制当前房间", (s, e) => DuplicateRoomQuick(), Keys.Control | Keys.Shift | Keys.D));
        room.DropDownItems.Add(new ToolStripSeparator());
        room.DropDownItems.Add(Theme.Item("重命名房间…", (s, e) => RenameRoomDialog(), Keys.F2));
        room.DropDownItems.Add(Theme.Item("删除房间", (s, e) => DeleteRoomAction(), Keys.Control | Keys.Delete));
        room.DropDownItems.Add(new ToolStripSeparator());
        var layerMenu = Theme.Item("图层");
        layerMenu.DropDownItems.Add(Theme.Item("新建实例层", (s, e) => AddLayerAction(false), Keys.Control | Keys.Shift | Keys.L));
        layerMenu.DropDownItems.Add(Theme.Item("新建瓦片层", (s, e) => AddLayerAction(true), Keys.Control | Keys.Shift | Keys.T));
        layerMenu.DropDownItems.Add(new ToolStripSeparator());
        layerMenu.DropDownItems.Add(Theme.Item("重命名图层…", (s, e) => RenameLayerDialog(), Keys.F3));
        layerMenu.DropDownItems.Add(Theme.Item("上移图层", (s, e) => MoveLayerAction(-1), Keys.Control | Keys.Shift | Keys.Up));
        layerMenu.DropDownItems.Add(Theme.Item("下移图层", (s, e) => MoveLayerAction(1), Keys.Control | Keys.Shift | Keys.Down));
        layerMenu.DropDownItems.Add(new ToolStripSeparator());
        layerMenu.DropDownItems.Add(Theme.Item("删除图层", (s, e) => DeleteLayerAction()));
        room.DropDownItems.Add(layerMenu);

        var view = TopMenu("视图(&V)");
        var fitItem = Theme.Item("适应窗口", (s, e) => _canvas.FitToView());
        fitItem.ShortcutKeyDisplayString = "F";
        view.DropDownItems.Add(fitItem);
        view.DropDownItems.Add(Theme.Item("实际大小", (s, e) => _canvas.SetZoom(1f), Keys.Control | Keys.D0));
        var zoomInItem = Theme.Item("放大", (s, e) => _canvas.ZoomBy(1.25f));
        zoomInItem.ShortcutKeyDisplayString = "＋";
        view.DropDownItems.Add(zoomInItem);
        var zoomOutItem = Theme.Item("缩小", (s, e) => _canvas.ZoomBy(0.8f));
        zoomOutItem.ShortcutKeyDisplayString = "－";
        view.DropDownItems.Add(zoomOutItem);
        view.DropDownItems.Add(new ToolStripSeparator());
        _gridItem = Theme.Item("显示网格", (s, e) => { if (_gridItem != null) _canvas.ShowGrid = _gridItem.Checked; });
        _gridItem.Checked = true;
        view.DropDownItems.Add(_gridItem);
        _snapItem = Theme.Item("吸附网格", (s, e) => { if (_snapItem != null) _canvas.SnapGrid = _snapItem.Checked; });
        view.DropDownItems.Add(_snapItem);
        _helperItem = Theme.Item("显示辅助对象", (s, e) => { if (_helperItem != null) _canvas.ShowHelpers = _helperItem.Checked; });
        _helperItem.Checked = true;
        view.DropDownItems.Add(_helperItem);
        view.DropDownItems.Add(new ToolStripSeparator());
        var rerenderItem = Theme.Item("重新渲染", (s, e) => _canvas.RefreshRoom());
        rerenderItem.ShortcutKeyDisplayString = "F5";
        view.DropDownItems.Add(rerenderItem);
        view.DropDownItems.Add(new ToolStripSeparator());
        view.DropDownItems.Add(Theme.Item("资源浏览器（只读）…", (s, e) => ShowResourceBrowser(), Keys.F4));

        var battleMenu = TopMenu("战斗(&B)");
        battleMenu.DropDownItems.Add(Theme.Item("编辑选中实体的战斗…", (s, e) => EditSelectedBattle(), Keys.Control | Keys.B));
        battleMenu.DropDownItems.Add(Theme.Item("编辑选中实体的对话…", (s, e) => EditSelectedDialogue(), Keys.Control | Keys.Shift | Keys.B));
        battleMenu.DropDownItems.Add(Theme.Item("编辑选中实体的剧情…", (s, e) => EditSelectedCutscene(), Keys.Control | Keys.Shift | Keys.K));
        battleMenu.DropDownItems.Add(Theme.Item("全部战斗…", (s, e) => ShowBattleList()));
        battleMenu.DropDownItems.Add(Theme.Item("全部对话…", (s, e) => ShowDialogueList()));
        battleMenu.DropDownItems.Add(Theme.Item("全部剧情…", (s, e) => ShowCutsceneList()));
        battleMenu.DropDownItems.Add(new ToolStripSeparator());
        battleMenu.DropDownItems.Add(Theme.Item("Mod 设置项…", (s, e) => EditSettings()));
        battleMenu.DropDownItems.Add(new ToolStripSeparator());
        battleMenu.DropDownItems.Add(Theme.Item("生成战斗 mod…", (s, e) => GenerateBattleMod()));
        battleMenu.DropDownItems.Add(Theme.Item("mods 清单（mods.json）…", (s, e) => ShowModsManifest()));
        battleMenu.DropDownItems.Add(new ToolStripSeparator());
        battleMenu.DropDownItems.Add(Theme.Item("保存战斗配置", (s, e) => SaveBattles(true)));

        var help = TopMenu("帮助(&H)");
        help.DropDownItems.Add(Theme.Item("快捷键…", (s, e) => ShowHelp()));
        help.DropDownItems.Add(Theme.Item("关于", (s, e) => MessageBox.Show(this,
            "Neutraled 地图编辑器\n\n仿 Loenn 的 DELTARUNE 房间编辑器：读取 data.win，编辑房间实例、图块与图层，整包写回。\n\n基于 UndertaleModTool 的 UndertaleModLib。",
            "关于", MessageBoxButtons.OK, MessageBoxIcon.Information)));

        ms.Items.Add(file);
        ms.Items.Add(edit);
        ms.Items.Add(room);
        ms.Items.Add(battleMenu);
        ms.Items.Add(view);
        ms.Items.Add(help);
        return ms;
    }

    private ToolStripMenuItem TopMenu(string text)
    {
        var item = Theme.Item(text);
        item.ForeColor = Theme.Text;
        return item;
    }

    private ToolStripMenuItem BuildChapterMenu()
    {
        var root = Theme.Item("打开游戏章节");
        var gameRoot = FindGameRoot();
        var live = Theme.Item("游戏文件（可保存）");
        var baseline = Theme.Item("原版基线（只读）");
        for (int ch = 1; ch <= 5; ch++)
        {
            if (gameRoot != null)
            {
                string d = Path.Combine(gameRoot, $"chapter{ch}_windows", "data.win");
                if (File.Exists(d)) live.DropDownItems.Add(Theme.Item($"Chapter {ch}", (s, e) => OpenPath(d, false)));
                string b = Path.Combine(gameRoot, "backup", $"chapter{ch}_windows", "data.win");
                if (File.Exists(b)) baseline.DropDownItems.Add(Theme.Item($"Chapter {ch}", (s, e) => OpenPath(b, false)));
            }
        }
        // 可编辑的 live 放前面：默认入口不再导向 backup（C3a / A2）。
        root.DropDownItems.Add(live);
        root.DropDownItems.Add(baseline);
        if (gameRoot == null) root.DropDownItems.Add(Theme.Item("（未找到游戏目录：设置 DELTARUNE_DIR）"));
        return root;
    }

    private static string? FindGameRoot() => FileRole.FindGameRoot();

    // --------------------------------------------------------------- toolbar

    private Panel BuildToolbar()
    {
        var bar = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg1, Padding = new Padding(6, 6, 6, 4), Margin = new Padding(0) };
        var left = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Bg1, WrapContents = false, Margin = new Padding(0) };
        var right = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 400, BackColor = Theme.Bg1, WrapContents = false, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0) };

        string[] tools = { "选择 (V)", "平移 (H)", "放置 (B)", "擦除 (X)", "瓦片 (T)", "矩形 (R)", "直线 (L)", "圆形 (C)", "椭圆 (E)", "油漆桶 (K)", "瓦片选择 (S)" };
        EditorTool[] toolIds = { EditorTool.Select, EditorTool.Pan, EditorTool.Place, EditorTool.Erase, EditorTool.Tile,
                                 EditorTool.TileRect, EditorTool.TileLine, EditorTool.TileCircle, EditorTool.TileEllipse,
                                 EditorTool.TileFill, EditorTool.TileSelect };
        for (int i = 0; i < tools.Length; i++)
        {
            // 工具变多后按钮要稍窄一点，否则 1520 宽下右半会被 FlowLayoutPanel 裁掉
            int w = i == tools.Length - 1 ? 96 : 78;
            var btn = new Button { Text = tools[i], Width = w, Height = 30, Margin = new Padding(0, 0, 4, 0) };
            Theme.StyleButton(btn);
            var tool = toolIds[i];
            btn.Click += (s, e) => SetTool(tool);
            _toolButtons.Add(btn);
            left.Controls.Add(btn);
        }

        var sep = new Panel { Width = 1, Height = 30, BackColor = Theme.Line, Margin = new Padding(8, 0, 8, 0) };
        left.Controls.Add(sep);

        _undoBtn = new Button { Text = "撤销", Width = 58, Height = 30, Margin = new Padding(0, 0, 4, 0), Enabled = false };
        Theme.StyleButton(_undoBtn);
        _undoBtn.Click += (s, e) => DoUndo();
        left.Controls.Add(_undoBtn);
        _redoBtn = new Button { Text = "重做", Width = 58, Height = 30, Margin = new Padding(0, 0, 4, 0), Enabled = false };
        Theme.StyleButton(_redoBtn);
        _redoBtn.Click += (s, e) => DoRedo();
        left.Controls.Add(_redoBtn);

        var saveBtn = new Button { Text = "保存", Width = 74, Height = 30, Margin = new Padding(0, 0, 4, 0) };
        Theme.StyleButton(saveBtn, true);
        saveBtn.Click += (s, e) => Save(false);
        right.Controls.Add(saveBtn);

        var fitBtn = new Button { Text = "适应", Width = 56, Height = 30, Margin = new Padding(4, 0, 0, 0) };
        Theme.StyleButton(fitBtn);
        fitBtn.Click += (s, e) => _canvas.FitToView();
        right.Controls.Add(fitBtn);
        _zoomLabel = new Label { Text = "100%", Width = 56, Height = 30, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.TextDim, Margin = new Padding(4, 0, 0, 0) };
        right.Controls.Add(_zoomLabel);
        var zoomIn = new Button { Text = "＋", Width = 34, Height = 30, Margin = new Padding(4, 0, 0, 0) };
        Theme.StyleButton(zoomIn);
        zoomIn.Click += (s, e) => _canvas.ZoomBy(1.25f);
        right.Controls.Add(zoomIn);
        var zoomOut = new Button { Text = "－", Width = 34, Height = 30, Margin = new Padding(4, 0, 0, 0) };
        Theme.StyleButton(zoomOut);
        zoomOut.Click += (s, e) => _canvas.ZoomBy(0.8f);
        right.Controls.Add(zoomOut);

        bar.Controls.Add(left);
        bar.Controls.Add(right);
        SetTool(EditorTool.Select);
        return bar;
    }

    private void SetTool(EditorTool tool)
    {
        _canvas.SetTool(tool);
        EditorTool[] ids = { EditorTool.Select, EditorTool.Pan, EditorTool.Place, EditorTool.Erase, EditorTool.Tile,
                             EditorTool.TileRect, EditorTool.TileLine, EditorTool.TileCircle, EditorTool.TileEllipse,
                             EditorTool.TileFill, EditorTool.TileSelect };
        for (int i = 0; i < _toolButtons.Count; i++)
        {
            bool active = ids[i] == tool;
            var b = _toolButtons[i];
            b.BackColor = active ? Theme.Accent : Theme.Bg2;
            b.FlatAppearance.BorderColor = active ? Theme.Accent : Theme.Line;
        }
    }

    // ------------------------------------------------------------ left panel

    private Control BuildLeftPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Theme.Bg1, Margin = new Padding(0) };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 240));

        panel.Controls.Add(BuildFilterHeader("房间", _roomFilter, "筛选房间…"), 0, 0);

        _roomList.Dock = DockStyle.Fill;
        _roomList.DrawMode = DrawMode.OwnerDrawFixed;
        _roomList.ItemHeight = 24;
        _roomList.BorderStyle = BorderStyle.None;
        _roomList.BackColor = Theme.Bg1;
        _roomList.ForeColor = Theme.Text;
        _roomList.IntegralHeight = false;
        _roomList.Margin = new Padding(0);
        _roomList.DrawItem += DrawRoomRow;
        _roomList.SelectedIndexChanged += (s, e) =>
        {
            if (_syncingLists || _doc == null) return;
            int i = _roomList.SelectedIndex;
            if (i < 0 || i >= _roomRows.Count) return;
            _doc.OpenRoom(_roomRows[i].Room);
            _canvas.FitToView();
        };
        _roomFilter.TextChanged += (s, e) => { if (!_syncingLists) RebuildRoomList(); };
        _roomList.ContextMenuStrip = BuildRoomContextMenu();
        _roomList.MouseDown += (s, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            int idx = _roomList.IndexFromPoint(e.Location);
            if (idx >= 0 && idx < _roomRows.Count) _roomList.SelectedIndex = idx;
        };
        panel.Controls.Add(_roomList, 0, 1);

        var layerHeader = Theme.Header("图层", 260);
        layerHeader.Dock = DockStyle.Fill;
        layerHeader.Margin = new Padding(0);
        panel.Controls.Add(layerHeader, 0, 2);

        _layerList.Dock = DockStyle.Fill;
        _layerList.DrawMode = DrawMode.OwnerDrawFixed;
        _layerList.ItemHeight = 22;
        _layerList.BorderStyle = BorderStyle.None;
        _layerList.BackColor = Theme.Bg1;
        _layerList.ForeColor = Theme.Text;
        _layerList.IntegralHeight = false;
        _layerList.Margin = new Padding(0);
        _layerList.DrawItem += DrawLayerRow;
        _layerList.SelectedIndexChanged += (s, e) =>
        {
            if (_syncingLists) return;
            int i = _layerList.SelectedIndex;
            if (i < 0 || i >= _layerRows.Count) return;
            _canvas.TargetLayer = _layerRows[i].Layer;
            _statusLeft.Text = $"放置目标图层：{_layerRows[i].Name}";
            RebuildInspector();
        };
        _layerList.ContextMenuStrip = BuildLayerContextMenu();
        _layerList.MouseDown += (s, e) =>
        {
            int idx = _layerList.IndexFromPoint(e.Location);
            if (idx < 0 || idx >= _layerRows.Count) return;
            if (e.Button == MouseButtons.Right)
            {
                _layerList.SelectedIndex = idx;
                return;
            }
            if (e.X <= 26) ToggleLayerVisible(_layerRows[idx].Layer);
        };
        panel.Controls.Add(_layerList, 0, 3);
        return panel;
    }

    private Panel BuildFilterHeader(string title, TextBox filter, string placeholder)
    {
        var panel = new Panel { Dock = DockStyle.Fill, AutoSize = true, BackColor = Theme.Bg1, Margin = new Padding(0) };
        var header = Theme.Header(title, 260);
        header.Dock = DockStyle.Top;
        Theme.StyleTextBox(filter);
        filter.Dock = DockStyle.Top;
        filter.Margin = new Padding(0);
        filter.PlaceholderText = placeholder;
        var host = new Panel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(8, 0, 8, 4), BackColor = Theme.Bg1 };
        host.Controls.Add(filter);
        var footer = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Line };
        panel.Controls.Add(host);
        panel.Controls.Add(footer);
        panel.Controls.Add(header);
        return panel;
    }

    private void DrawRoomRow(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _roomRows.Count) return;
        var row = _roomRows[e.Index];
        bool sel = (e.State & DrawItemState.Selected) != 0;
        using var bg = new SolidBrush(sel ? Theme.Bg3 : Theme.Bg1);
        e.Graphics.FillRectangle(bg, e.Bounds);
        if (sel)
        {
            using var accent = new SolidBrush(Theme.Accent);
            e.Graphics.FillRectangle(accent, e.Bounds.X, e.Bounds.Y + 2, 3, e.Bounds.Height - 4);
        }
        TextRenderer.DrawText(e.Graphics, row.Name, RowFont,
            new Rectangle(e.Bounds.X + 10, e.Bounds.Y, e.Bounds.Width - 78, e.Bounds.Height),
            sel ? Theme.Text : Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(e.Graphics, row.SizeText, RowMono,
            new Rectangle(e.Bounds.Right - 68, e.Bounds.Y, 62, e.Bounds.Height),
            Theme.TextDim, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
    }

    private void DrawLayerRow(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _layerRows.Count) return;
        var row = _layerRows[e.Index];
        bool sel = (e.State & DrawItemState.Selected) != 0;
        using var bg = new SolidBrush(sel ? Theme.Bg3 : Theme.Bg1);
        e.Graphics.FillRectangle(bg, e.Bounds);
        var dot = new Rectangle(e.Bounds.X + 10, e.Bounds.Y + e.Bounds.Height / 2 - 4, 8, 8);
        if (row.Layer.IsVisible)
        {
            using var b = new SolidBrush(Theme.Green);
            e.Graphics.FillEllipse(b, dot);
        }
        else
        {
            using var p = new Pen(Theme.TextDim);
            e.Graphics.DrawEllipse(p, dot);
        }
        TextRenderer.DrawText(e.Graphics, row.Name, RowFont,
            new Rectangle(e.Bounds.X + 26, e.Bounds.Y, e.Bounds.Width - 110, e.Bounds.Height),
            sel ? Theme.Text : Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(e.Graphics, row.Detail, RowFontSmall,
            new Rectangle(e.Bounds.Right - 84, e.Bounds.Y, 78, e.Bounds.Height),
            Theme.TextDim, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
    }

    // ----------------------------------------------------------- right panel

    private Control BuildRightPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, BackColor = Theme.Bg1, Margin = new Padding(0) };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 28));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 22));

        var insHeader = Theme.Header("属性", 344);
        insHeader.Dock = DockStyle.Fill;
        insHeader.Margin = new Padding(0);
        panel.Controls.Add(insHeader, 0, 0);

        _inspector.Dock = DockStyle.Fill;
        _inspector.AutoScroll = true;
        _inspector.BackColor = Theme.Bg1;
        _inspector.Margin = new Padding(0);
        // AutoSize=false + 显式高度：Dock=Top + AutoSize 在 AutoScroll 面板里高度会随父行伸缩，
        // 内容超出时既不撑高也不出滚动条（图层属性面板被裁掉）。改为每次 RebuildInspector 后按
        // 内容首选高度写死高度，AutoScroll 才能看见剩余行。
        _inspectorGrid.Dock = DockStyle.Top;
        _inspectorGrid.AutoSize = false;
        _inspectorGrid.ColumnCount = 2;
        _inspectorGrid.RowCount = 0;
        _inspectorGrid.BackColor = Theme.Bg1;
        _inspectorGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        _inspectorGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _inspector.Controls.Add(_inspectorGrid);
        panel.Controls.Add(_inspector, 0, 1);

        var objHeader = Theme.Header("对象", 344);
        objHeader.Dock = DockStyle.Fill;
        objHeader.Margin = new Padding(0);
        panel.Controls.Add(objHeader, 0, 2);
        panel.Controls.Add(BuildFilterHeader("", _objFilter, "筛选对象…"), 0, 3);

        _objList.Dock = DockStyle.Fill;
        _objList.DrawMode = DrawMode.OwnerDrawFixed;
        _objList.ItemHeight = 36;
        _objList.BorderStyle = BorderStyle.None;
        _objList.BackColor = Theme.Bg1;
        _objList.ForeColor = Theme.Text;
        _objList.IntegralHeight = false;
        _objList.Margin = new Padding(0);
        _objList.DrawItem += DrawObjectRow;
        _objList.SelectedIndexChanged += (s, e) =>
        {
            int i = _objList.SelectedIndex;
            if (i < 0 || i >= _objRows.Count) return;
            _canvas.PlaceObject = _objRows[i].Obj;
            SetTool(EditorTool.Place);
            _statusLeft.Text = $"放置对象：{_objRows[i].Name}（B 键回到放置工具）";
            _canvas.Invalidate();
        };
        _objFilter.TextChanged += (s, e) => RebuildObjectList();
        panel.Controls.Add(_objList, 0, 4);

        var tileHeader = Theme.Header("瓦片笔刷", 344);
        tileHeader.Dock = DockStyle.Fill;
        tileHeader.Margin = new Padding(0);
        panel.Controls.Add(tileHeader, 0, 5);

        var brushBtns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Theme.Bg1, Margin = new Padding(0) };
        brushBtns.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        brushBtns.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        _pickTileSourceBtn.Text = "选择图块来源…（精灵帧内选区域）";
        _pickTileSourceBtn.Dock = DockStyle.Fill;
        _pickTileSourceBtn.Margin = new Padding(8, 2, 8, 1);
        Theme.StyleButton(_pickTileSourceBtn);
        _pickTileSourceBtn.Click += (s, e) => PickTileSource();
        _paletteBtn.Text = "瓦片调色板…（点格子选笔刷）";
        _paletteBtn.Dock = DockStyle.Fill;
        _paletteBtn.Margin = new Padding(8, 1, 8, 2);
        Theme.StyleButton(_paletteBtn);
        _paletteBtn.Click += (s, e) => OpenTilePalette();
        brushBtns.Controls.Add(_pickTileSourceBtn, 0, 0);
        brushBtns.Controls.Add(_paletteBtn, 0, 1);
        panel.Controls.Add(brushBtns, 0, 6);

        _tileList.Dock = DockStyle.Fill;
        _tileList.DrawMode = DrawMode.OwnerDrawFixed;
        _tileList.ItemHeight = 30;
        _tileList.BorderStyle = BorderStyle.None;
        _tileList.BackColor = Theme.Bg1;
        _tileList.ForeColor = Theme.Text;
        _tileList.IntegralHeight = false;
        _tileList.Margin = new Padding(0);
        // 显式给无障碍名：UIA 靠前一个 STATIC 猜名字，上面插了两个按钮后就猜不到了（GUI 自动化会找不到这个列表）
        _tileList.AccessibleName = "瓦片笔刷";
        _tileList.DrawItem += DrawTileRow;
        _tileList.SelectedIndexChanged += (s, e) =>
        {
            if (_syncingLists) return;
            int i = _tileList.SelectedIndex;
            if (i < 0 || i >= _tileRows.Count) return;
            _canvas.SetLegacyBrush(_tileRows[i].Tile);
            SetTool(EditorTool.Tile);
            _statusLeft.Text = $"瓦片笔刷：{_tileRows[i].Name}（左键拖动绘制 / 右键拖动擦除 / Alt+点击拾取）";
            _canvas.Invalidate();
        };
        panel.Controls.Add(_tileList, 0, 7);

        return panel;
    }

    private void DrawObjectRow(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _objRows.Count) return;
        var row = _objRows[e.Index];
        bool sel = (e.State & DrawItemState.Selected) != 0;
        using var bg = new SolidBrush(sel ? Theme.Bg3 : Theme.Bg1);
        e.Graphics.FillRectangle(bg, e.Bounds);
        var thumb = ThumbFor(row.Obj);
        var box = new Rectangle(e.Bounds.X + 8, e.Bounds.Y + 4, 28, 28);
        if (thumb != null)
        {
            e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            e.Graphics.DrawImage(thumb, box);
        }
        else
        {
            using var p = new Pen(Theme.Line);
            e.Graphics.DrawRectangle(p, box);
        }
        TextRenderer.DrawText(e.Graphics, row.Name, RowFont,
            new Rectangle(e.Bounds.X + 44, e.Bounds.Y + 2, e.Bounds.Width - 52, 18),
            Theme.Text, TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(e.Graphics, row.Sprite, RowFontSmall,
            new Rectangle(e.Bounds.X + 44, e.Bounds.Y + 18, e.Bounds.Width - 52, 16),
            Theme.TextDim, TextFormatFlags.EndEllipsis);
    }

    private Bitmap? ThumbFor(UndertaleGameObject obj)
    {
        if (_doc == null) return null;
        if (_thumbs.TryGetValue(obj, out var cached)) return cached;
        Bitmap? bmp = null;
        try { bmp = RoomRender.Thumbnail(_doc.Game, obj.Sprite, 28); }
        catch { bmp = null; }
        _thumbs[obj] = bmp;
        return bmp;
    }

    private void DrawTileRow(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _tileRows.Count) return;
        var row = _tileRows[e.Index];
        bool sel = (e.State & DrawItemState.Selected) != 0;
        using var bg = new SolidBrush(sel ? Theme.Bg3 : Theme.Bg1);
        e.Graphics.FillRectangle(bg, e.Bounds);
        var box = new Rectangle(e.Bounds.X + 6, e.Bounds.Y + 2, 26, 26);
        if (row.Thumb != null)
        {
            e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            e.Graphics.DrawImage(row.Thumb, box);
        }
        else
        {
            using var pen = new Pen(Theme.Line);
            e.Graphics.DrawRectangle(pen, box);
        }
        TextRenderer.DrawText(e.Graphics, row.Name, RowFont,
            new Rectangle(e.Bounds.X + 40, e.Bounds.Y + 2, e.Bounds.Width - 48, 17),
            Theme.Text, TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(e.Graphics, row.Detail, RowFontSmall,
            new Rectangle(e.Bounds.X + 40, e.Bounds.Y + 17, e.Bounds.Width - 48, 14),
            Theme.TextDim, TextFormatFlags.EndEllipsis);
    }

    private Bitmap? MakeTileThumb(UndertaleRoom.Tile tile)
    {
        if (_doc == null) return null;
        try
        {
            var page = _doc.Game.Page(tile.Tpag);
            if (page == null) return null;
            int px = (tile.Tpag?.SourceX ?? 0) + tile.SourceX;
            int py = (tile.Tpag?.SourceY ?? 0) + tile.SourceY;
            int w = (int)tile.Width;
            int h = (int)tile.Height;
            if (px + w > page.Width) w = page.Width - px;
            if (py + h > page.Height) h = page.Height - py;
            if (w <= 0 || h <= 0) return null;
            var bmp = new Bitmap(26, 26);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Theme.Bg0);
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                float scale = Math.Min(26f / w, 26f / h);
                int dw = Math.Max(1, (int)MathF.Round(w * scale));
                int dh = Math.Max(1, (int)MathF.Round(h * scale));
                g.DrawImage(page, new Rectangle((26 - dw) / 2, (26 - dh) / 2, dw, dh),
                    new Rectangle(px, py, w, h), GraphicsUnit.Pixel);
            }
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private void RebuildTileList()
    {
        foreach (var row in _tileRows) row.Thumb?.Dispose();
        _tileRows.Clear();
        _syncingLists = true;
        try
        {
            if (_doc?.Room != null)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var (_, tile) in RoomEdit.AllTiles(_doc.Room))
                {
                    string spr = GameData.Name(tile.SpriteDefinition?.Name);
                    string key = spr + "|" + tile.SourceX + "|" + tile.SourceY + "|" + tile.Width + "|" + tile.Height +
                                 "|" + (tile.Tpag?.SourceX ?? 0) + "," + (tile.Tpag?.SourceY ?? 0);
                    if (!seen.Add(key)) continue;
                    _tileRows.Add(new TileRow
                    {
                        Tile = tile,
                        Name = string.IsNullOrEmpty(spr) ? "图块" : spr,
                        Detail = "src " + tile.SourceX + "," + tile.SourceY + " · " + tile.Width + "×" + tile.Height,
                        Thumb = MakeTileThumb(tile),
                    });
                    if (_tileRows.Count >= 300) break;
                }
            }
            _tileList.BeginUpdate();
            _tileList.Items.Clear();
            foreach (var row in _tileRows) _tileList.Items.Add(row);
            _tileList.EndUpdate();
            if (_canvas.Brush != null)
            {
                int idx = _tileRows.FindIndex(r => ReferenceEquals(r.Tile, _canvas.Brush));
                if (idx >= 0) _tileList.SelectedIndex = idx;
            }
            _tileList.Refresh();
        }
        finally { _syncingLists = false; }
    }

    /// <summary>
    /// 给瓦片笔刷挑一个任意精灵资源，并在弹出的区域框里切出帧内子矩形：
    /// 大 tileset 只取其中一格，新房间/新瓦片层没有现成图块时也能从零画。
    /// </summary>
    private void PickTileSource()
    {
        if (_doc?.Game == null) return;
        // 原生 Tiles 图层的“来源”是一整张 tileset 里的等尺寸 tile，不是精灵帧内任意矩形：
        // 直接转到瓦片调色板（它已枚举原生 tileset、显示 tile 尺寸，并用 tile id 生成笔刷）。
        var target = TileEditLayer();
        if (target != null && NativeTiles.IsNative(target))
        {
            _statusLeft.Text = "当前目标是原生 Tiles 图层：在瓦片调色板里选择原生 tileset 的 tile（尺寸随 tileset）";
            OpenTilePalette();
            return;
        }
        var spr = AssetPickerDialog.Show(this, _doc.Game, "选择图块来源（再框选帧内区域）");
        if (spr == null) return;
        if (_doc.Game.Frame(spr, 0) == null)
        {
            _statusLeft.Text = "该精灵没有可用的贴图帧";
            return;
        }
        var rect = TileSourceDialog.Show(this, _doc.Game, spr);
        if (rect == null) return;
        ApplyTileBrush(spr, rect.Value);
    }

    /// <summary>把「图集精灵 + 帧内源矩形」设成当前瓦片笔刷（选来源/调色板共用）。</summary>
    private void ApplyTileBrush(UndertaleSprite spr, Rectangle source, bool fromPalette = false)
    {
        if (_doc?.Game == null) return;
        var brush = RoomEdit.MakeTileBrush(_doc.Game.Data, spr, source);
        _canvas.SetLegacyBrush(brush);
        SetTool(EditorTool.Tile);
        _tileList.ClearSelected();
        _statusLeft.Text = $"瓦片笔刷：{GameData.Name(spr.Name)}（源 {brush.SourceX},{brush.SourceY} · {brush.Width}×{brush.Height}" +
                           (fromPalette ? " · 调色板" : "") + "，左键拖动绘制 / 右键拖动擦除）";
        _canvas.Invalidate();
        RebuildInspector();
    }

    /// <summary>把原生 tileset 的某格设为当前原生笔刷（调色板/原生来源共用）。</summary>
    private void ApplyNativeBrush(NativeTileBrush brush, bool fromPalette = false)
    {
        _canvas.SetNativeBrush(brush);
        SetTool(EditorTool.Tile);
        _tileList.ClearSelected();
        _statusLeft.Text = $"原生图块笔刷：id {brush.TileId} · flags {brush.Flags} · {brush.CellSize.W}×{brush.CellSize.H}" +
                           (fromPalette ? " · 调色板" : "") + "（左键拖动绘制 / 右键拖动擦除 / Alt+点击拾取）";
        _canvas.Invalidate();
        RebuildInspector();
    }

    /// <summary>
    /// 打开非模态「瓦片调色板」：把一张 tileset 精灵按单元格切网格，点格子即设为笔刷。
    /// 调色板开着也能回主窗口绘制，连续点不同格子可来回换笔刷。
    /// </summary>
    private void OpenTilePalette()
    {
        if (_doc?.Game == null) return;
        if (_palette != null && !_palette.IsDisposed)
        {
            _palette.Show();
            _palette.BringToFront();
            _palette.Activate();
            return;
        }
        var initial = _canvas.Brush?.ObjectDefinition as UndertaleSprite;
        var pal = TilePaletteDialog.Create(_doc.Game, _doc.Room, initial);
        pal.Picked = (spr, rect) => ApplyTileBrush(spr, rect, true);
        pal.PickedNative = brush => ApplyNativeBrush(brush, true);
        pal.FormClosed += (s, e) => { if (ReferenceEquals(_palette, pal)) _palette = null; };
        _palette = pal;
        pal.Show(this);
        var wa = Screen.FromControl(this).WorkingArea;
        int x = Math.Min(Math.Max(wa.Left + 8, Right - pal.Width - 24), wa.Right - pal.Width - 8);
        int y = Math.Max(wa.Top + 8, Top + 90);
        pal.Location = new Point(x, Math.Min(y, wa.Bottom - pal.Height - 8));
    }

    /// <summary>更换背景层的精灵。</summary>
    private void PickBackgroundSprite(UndertaleRoom.Layer layer)
    {
        if (_doc?.Game == null || layer.BackgroundData == null) return;
        var spr = AssetPickerDialog.Show(this, _doc.Game, "选择背景层精灵");
        if (spr == null) return;
        if (!_doc.SetBackgroundSprite(layer, spr)) return;
        _canvas.RefreshRoom();
        RebuildLayers();
        RebuildInspector();
        UpdateTitle();
        _statusLeft.Text = $"背景精灵：{GameData.Name(spr.Name)}";
    }

    // ------------------------------------------------------------- status bar

    private Panel BuildStatusBar()
    {
        var bar = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg1, Margin = new Padding(0) };
        _statusLeft.Text = "就绪 — 文件 → 打开 data.win";
        _statusLeft.Dock = DockStyle.Fill;
        _statusLeft.TextAlign = ContentAlignment.MiddleLeft;
        _statusLeft.Padding = new Padding(10, 0, 0, 0);
        _statusLeft.ForeColor = Theme.TextDim;
        _statusLeft.Font = Theme.UI(8.5f);
        _statusCoords.Dock = DockStyle.Right;
        _statusCoords.Width = 190;
        _statusCoords.TextAlign = ContentAlignment.MiddleRight;
        _statusCoords.ForeColor = Theme.TextDim;
        _statusCoords.Font = Theme.Mono(8.5f);
        _statusZoom.Dock = DockStyle.Right;
        _statusZoom.Width = 70;
        _statusZoom.TextAlign = ContentAlignment.MiddleRight;
        _statusZoom.ForeColor = Theme.TextDim;
        _statusZoom.Font = Theme.Mono(8.5f);
        _statusZoom.Padding = new Padding(0, 0, 10, 0);
        bar.Controls.Add(_statusLeft);
        bar.Controls.Add(_statusCoords);
        bar.Controls.Add(_statusZoom);
        return bar;
    }

    // ------------------------------------------------------------- documents

    private void WireDocument()
    {
        if (_doc == null) return;
        _doc.StateChanged += () => { UpdateTitle(); UpdateUndoState(); };
        _doc.RoomChanged += () =>
        {
            _canvas.RefreshRoom();
            RebuildLayers();
            RebuildInspector();
            RebuildTileList();
            UpdateStatus();
        };
        _doc.SelectionChanged += () => { RebuildInspector(); _canvas.Invalidate(); };
        _doc.RoomsChanged += () =>
        {
            RebuildRoomList();
            RebuildLayers();
            RebuildInspector();
            UpdateTitle();
            UpdateStatus();
        };
    }

    private void OpenDialog()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "打开 data.win",
            Filter = "GameMaker data.win|data.win;*.win|所有文件|*.*",
            CheckFileExists = true,
        };
        if (_doc != null)
        {
            try { dlg.InitialDirectory = Path.GetDirectoryName(_doc.FilePath); } catch { }
        }
        if (dlg.ShowDialog(this) == DialogResult.OK) OpenPath(dlg.FileName, false);
    }

    private void OpenPath(string path, bool force)
    {
        if (!File.Exists(path))
        {
            MessageBox.Show(this, "文件不存在：\n" + path, "打开失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        if (!force && _doc?.Dirty == true)
        {
            var r = MessageBox.Show(this, "当前文件有未保存的修改，仍要打开新文件吗？", "未保存的修改",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) return;
        }
        Cursor = Cursors.WaitCursor;
        _statusLeft.Text = "正在读取 " + path + " …";
        Application.DoEvents();
        try
        {
            if (_palette != null && !_palette.IsDisposed) _palette.Close();
            _palette = null;
            var game = new GameData(path);
            _doc?.Dispose();
            _doc = new EditorDocument(game, path);
            _battles = BattleProject.LoadOrNew(BattleProject.SidecarFor(path));
            _objects.Clear();
            _objects.AddRange(game.Data.GameObjects);
            _thumbs.Clear();
            // drop thumbnails of the previous document
            foreach (var b in _thumbs.Values) b?.Dispose();
            WireDocument();
            _canvas.Attach(_doc);
            _fileRole = FileRole.Classify(path, FindGameRoot());
            _warnedDeployed = false;
            RebuildRoomList();
            RebuildObjectList();
            RebuildLayers();
            RebuildInspector();
            RebuildTileList();
            if (_roomRows.Count > 0)
            {
                _syncingLists = true;
                _roomList.SelectedIndex = 0;
                _syncingLists = false;
                _doc.OpenRoom(_roomRows[0].Room);
                _canvas.FitToView();
            }
            UpdateTitle();
            UpdateUndoState();
            UpdateStatus();
            _statusLeft.Text = $"已加载 {Path.GetFileName(path)} — {_roomRows.Count} 个房间 / {_objects.Count} 个对象";
            if (_fileRole == FileKind.Baseline)
                _statusLeft.Text += $"  ·  {FileRole.BaselineBadge}（Ctrl+S 会被拦截，请用「另存为」或打开游戏文件）";
            else if (FileRole.IsLive(_fileRole))
                _statusLeft.Text += $"  ·  {FileRole.LiveBadge}";
            RecentStore.Record(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "读取 data.win 失败：\n" + ex.Message, "打开失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _statusLeft.Text = "打开失败";
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void RebuildRoomList()
    {
        if (_doc == null) return;
        string filter = _roomFilter.Text.Trim();
        _syncingLists = true;
        try
        {
            _roomRows.Clear();
            foreach (var room in _doc.Game.Rooms)
            {
                string name = GameData.Name(room.Name);
                if (filter.Length > 0 && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
                _roomRows.Add(new RoomRow { Room = room, Name = name, SizeText = $"{room.Width}×{room.Height}" });
            }
            _roomList.BeginUpdate();
            _roomList.Items.Clear();
            foreach (var r in _roomRows) _roomList.Items.Add(r);
            _roomList.EndUpdate();
            if (_doc.Room != null)
            {
                int idx = _roomRows.FindIndex(r => ReferenceEquals(r.Room, _doc.Room));
                if (idx >= 0) _roomList.SelectedIndex = idx;
            }
            _roomList.Refresh();
        }
        finally { _syncingLists = false; }
    }

    private void RebuildLayers()
    {
        _layerRows.Clear();
        if (_doc?.Room?.Layers != null)
        {
            foreach (var layer in _doc.Room.Layers)
            {
                string kind = layer.LayerType switch
                {
                    UndertaleRoom.LayerType.Instances => "实例",
                    UndertaleRoom.LayerType.Background => "背景",
                    UndertaleRoom.LayerType.Assets => "图块",
                    UndertaleRoom.LayerType.Tiles => "瓦片",
                    UndertaleRoom.LayerType.Effect => "效果",
                    _ => layer.LayerType.ToString(),
                };
                _layerRows.Add(new LayerRow
                {
                    Layer = layer,
                    Name = GameData.Name(layer.LayerName),
                    Detail = $"{kind} {layer.LayerDepth}",
                });
            }
        }
        _syncingLists = true;
        try
        {
            _layerList.BeginUpdate();
            _layerList.Items.Clear();
            foreach (var r in _layerRows) _layerList.Items.Add(r);
            _layerList.EndUpdate();
            if (_canvas.TargetLayer == null || !_layerRows.Any(r => ReferenceEquals(r.Layer, _canvas.TargetLayer)))
            {
                var firstInst = _layerRows.FirstOrDefault(r => r.Layer.InstancesData?.Instances != null);
                _canvas.TargetLayer = firstInst?.Layer ?? _layerRows.FirstOrDefault()?.Layer;
            }
            int idx = _layerRows.FindIndex(r => ReferenceEquals(r.Layer, _canvas.TargetLayer));
            if (idx >= 0) _layerList.SelectedIndex = idx;
            _layerList.Refresh();
        }
        finally { _syncingLists = false; }
    }

    private void RebuildObjectList()
    {
        if (_doc == null) return;
        string filter = _objFilter.Text.Trim();
        _syncingLists = true;
        try
        {
            _objRows.Clear();
            foreach (var obj in _objects)
            {
                string name = GameData.Name(obj.Name);
                string sprite = GameData.Name(obj.Sprite?.Name);
                if (filter.Length > 0 && !name.Contains(filter, StringComparison.OrdinalIgnoreCase) && !sprite.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
                _objRows.Add(new ObjectRow { Obj = obj, Name = name, Sprite = string.IsNullOrEmpty(sprite) ? "（无 sprite）" : sprite });
                if (_objRows.Count >= 800) break;
            }
            _objList.BeginUpdate();
            _objList.Items.Clear();
            foreach (var r in _objRows) _objList.Items.Add(r);
            _objList.EndUpdate();
            _objList.Refresh();
        }
        finally { _syncingLists = false; }
    }

    private void UpdateStatus()
    {
        if (_doc?.Room == null)
        {
            _statusLeft.Text = "就绪 — 文件 → 打开 data.win";
            return;
        }
        int instances = RoomEdit.AllInstances(_doc.Room).Count();
        int tiles = _doc.Room.Layers?.Sum(l => (l.AssetsData?.LegacyTiles?.Count ?? 0) + NativeTiles.CountNonZero(l)) ?? 0;
        _statusLeft.Text = $"{_doc.RoomName()} — {_doc.Room.Width}×{_doc.Room.Height} · {instances} 个实例 · {tiles} 个图块";
    }

    private void UpdateTitle()
    {
        if (_doc == null)
        {
            Text = "Neutraled 地图编辑器";
            return;
        }
        string name = Path.GetFileName(_doc.FilePath);
        string dir = Path.GetDirectoryName(_doc.FilePath) ?? "";
        string badge = _fileRole == FileKind.Other ? "" : $"  [{FileRole.Badge(_fileRole)}]";
        Text = $"{name}{(_doc.Dirty ? " *" : "")} — {dir}{badge}";
        UpdateUndoState();
    }

    private void UpdateUndoState()
    {
        if (_undoBtn != null) _undoBtn.Enabled = _doc?.CanUndo == true;
        if (_redoBtn != null) _redoBtn.Enabled = _doc?.CanRedo == true;
    }

    // -------------------------------------------------------------- inspector

    private void RebuildInspector()
    {
        ClearInspector();
        if (_doc?.Room == null)
        {
            AddHint("打开 data.win 后，这里显示房间与实例属性。\n\n快捷键：V 选择 · H 平移 · B 放置 · X 擦除 · T 瓦片 · R 矩形 · L 直线 · C 圆形 · E 椭圆 · K 油漆桶 · S 瓦片选择\nF 适应 · G 网格 · Delete 删除 · Ctrl+Z 撤销");
            FitInspectorHeight();
            return;
        }
        if (_doc.SelectedTile != null)
        {
            var tile = _doc.SelectedTile;
            AddSection("图块");
            AddReadonly("精灵", GameData.Name(tile.SpriteDefinition?.Name));
            var tileLayer = RoomEdit.LayerOfTile(_doc.Room, tile);
            AddReadonly("图层", tileLayer == null ? "" : GameData.Name(tileLayer.LayerName));
            AddNumberField("X", tile.X, v => ApplyTile(t => t.X = v));
            AddNumberField("Y", tile.Y, v => ApplyTile(t => t.Y = v));
            AddNumberField("宽度", (int)tile.Width, v => ApplyTile(t => t.Width = (uint)Math.Clamp(v, 1, 4096)));
            AddNumberField("高度", (int)tile.Height, v => ApplyTile(t => t.Height = (uint)Math.Clamp(v, 1, 4096)));
            AddNumberField("源 X", tile.SourceX, v => ApplyTile(t => t.SourceX = v));
            AddNumberField("源 Y", tile.SourceY, v => ApplyTile(t => t.SourceY = v));
            AddFloatField("缩放 X", tile.ScaleX, v => ApplyTile(t => t.ScaleX = v));
            AddFloatField("缩放 Y", tile.ScaleY, v => ApplyTile(t => t.ScaleY = v));
            AddColorField("颜色", tile.Color, v => ApplyTile(t => t.Color = v));
            AddReadonly("Tile ID", tile.InstanceID.ToString());
            AddSection("操作");
            var delTile = new Button { Text = "删除此图块 (Delete)", Height = 28 };
            Theme.StyleButton(delTile);
            delTile.Click += (s, e) => DeleteSelection();
            AddSpan(delTile);
            AddHint("Alt+点击画布可拾取图块；选中瓦片笔刷后左键拖动连续绘制、右键拖动连续擦除。\n矩形 (R)/直线 (L)/圆形 (C)/椭圆 (E) 拖出区域整片填充，右键为整片擦除；油漆桶 (K) 替换连通区域。\n「瓦片调色板…」可按单元格从整张 tileset 里点选笔刷（非模态，开着也能画）。");
            FitInspectorHeight();
            return;
        }
        if (_doc.SelectedNativeTile != null)
        {
            var cell = _doc.SelectedNativeTile;
            AddSection("原生图块 (GMS2 Tiles)");
            AddReadonly("图层", GameData.Name(cell.Layer.LayerName));
            AddReadonly("格子", $"[{cell.X}, {cell.Y}]  {cell.Rect.Width}×{cell.Rect.Height}");
            AddReadonly("Tile ID", cell.TileId.ToString());
            AddReadonly("Flags", $"{cell.Flags}（{(cell.Flags == 0 ? "无翻转" : "翻转/旋转位")}）");
            AddReadonly("Tileset", GameData.Name(cell.Layer.TilesData?.Background?.Name));
            AddReadonly("源矩形", NativeTiles.DescribeSource(cell.Layer, cell.Value));
            AddSection("操作");
            var delNative = new Button { Text = "删除此图块 (Delete)", Height = 28 };
            Theme.StyleButton(delNative);
            delNative.Click += (s, e) => DeleteSelection();
            AddSpan(delNative);
            AddHint("原生 Tiles 层没有独立 Tile 对象，选中的是网格里的一个格子；删除即把该格 id 置 0（可撤销）。\n用「瓦片选择 (S)」右键点击也可直接擦除单格。");
            FitInspectorHeight();
            return;
        }
        if (_doc.SelectedSprite != null && _doc.SelectedSpriteLayer != null)
        {
            var si = _doc.SelectedSprite;
            var siLayer = _doc.SelectedSpriteLayer;
            var spr = RoomRender.ResolveSprite(_doc.Game, si);
            bool nine = siLayer.AssetsData?.NineSlices?.Contains(si) == true;
            AddSection(nine ? "九宫格精灵实例" : "精灵实例 (Assets)");
            AddReadonly("精灵", GameData.Name(spr?.Name));
            AddReadonly("图层", GameData.Name(siLayer.LayerName));
            AddReadonly("帧", $"{si.WrappedFrameIndex} / {Math.Max(1, spr?.Textures?.Count ?? 1)}");
            AddNumberField("X", si.X, v => ApplySprite(s => s.X = v));
            AddNumberField("Y", si.Y, v => ApplySprite(s => s.Y = v));
            AddFloatField("缩放 X", si.ScaleX, v => ApplySprite(s => s.ScaleX = v));
            AddFloatField("缩放 Y", si.ScaleY, v => ApplySprite(s => s.ScaleY = v));
            AddFloatField("旋转", si.Rotation, v => ApplySprite(s => s.Rotation = v));
            AddFloatField("帧索引", si.FrameIndex, v => ApplySprite(s => s.FrameIndex = v));
            AddColorField("颜色", si.Color, v => ApplySprite(s => s.Color = v));
            AddSection("操作");
            var delSprite = new Button { Text = "删除此精灵实例 (Delete)", Height = 28 };
            Theme.StyleButton(delSprite);
            delSprite.Click += (s, e) => DeleteSelection();
            AddSpan(delSprite);
            AddHint("Assets 层精灵按 x/y/scale/rotation/colour 渲染；编辑立即重绘，可 Ctrl+Z 撤销。");
            FitInspectorHeight();
            return;
        }
        if (_doc.Selection.Count > 1)
        {
            AddSection($"已选 {_doc.Selection.Count} 个实例");
            var del = new Button { Text = "删除选中 (Delete)", Height = 28 };
            Theme.StyleButton(del);
            del.Click += (s, e) => DeleteSelection();
            AddSpan(del);
            AddSection("批量调整");
            var alignGrid = new TableLayoutPanel
            {
                ColumnCount = 4, RowCount = 2, AutoSize = true, Dock = DockStyle.Fill,
                Margin = new Padding(10, 4, 10, 4),
            };
            for (int c = 0; c < 4; c++) alignGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            void AlignBtn(string text, EditorDocument.AlignMode mode, string label)
            {
                var b = new Button { Text = text, Height = 26, Dock = DockStyle.Fill, Margin = new Padding(2) };
                Theme.StyleButton(b);
                b.Click += (s, e) => ApplyAlign(mode, label);
                alignGrid.Controls.Add(b);
            }
            AlignBtn("左对齐", EditorDocument.AlignMode.Left, "左对齐");
            AlignBtn("居中 X", EditorDocument.AlignMode.CenterX, "水平居中");
            AlignBtn("右对齐", EditorDocument.AlignMode.Right, "右对齐");
            AlignBtn("顶对齐", EditorDocument.AlignMode.Top, "顶对齐");
            AlignBtn("居中 Y", EditorDocument.AlignMode.CenterY, "垂直居中");
            AlignBtn("底对齐", EditorDocument.AlignMode.Bottom, "底对齐");
            AlignBtn("分布 X", EditorDocument.AlignMode.DistributeX, "水平分布");
            AlignBtn("分布 Y", EditorDocument.AlignMode.DistributeY, "垂直分布");
            AddSpan(alignGrid);
            AddHint("拖动可整体移动；方向键微移（按住 Shift 每次 8px）。\n对齐/分布按「当前图像帧 × 缩放」估算的包围盒计算（有旋转时按绕中心的旋转外接矩形近似）。");
            FitInspectorHeight();
            return;
        }
        if (_doc.Selection.Count == 1)
        {
            var inst = _doc.Selection[0];
            AddSection("实例");
            AddReadonly("对象", GameData.Name(inst.ObjectDefinition?.Name));
            AddReadonly("实例 ID", inst.InstanceID.ToString());
            AddSection("战斗");
            var instRoom = _doc.RoomName();
            var instBattle = _battles?.ForInstance(instRoom, inst.InstanceID);
            if (instBattle == null)
            {
                AddReadonly("状态", "（未绑定）");
                AddButtonRow("", "为此实体新建战斗…", EditSelectedBattle);
            }
            else
            {
                AddReadonly("状态", instBattle.Id + "（" + instBattle.Kind + (instBattle.Enabled ? "" : "，已禁用") + "）");
                AddReadonly("规模", instBattle.Turns.Count + " 回合 / " + instBattle.Enemies.Count + " 敌人");
                AddButtonRow("", "编辑战斗…", EditSelectedBattle);
                AddButtonRow("", "解除绑定", () =>
                {
                    if (_battles == null) return;
                    _battles.RemoveBattle(instRoom, inst.InstanceID);
                    SaveBattles(false);
                    RebuildInspector();
                });
            }
            AddSection("剧情");
            var instCutscene = _battles?.CutsceneForInstance(instRoom, inst.InstanceID);
            if (instCutscene == null)
            {
                AddReadonly("状态", "（未绑定）");
                AddButtonRow("", "为此实体新建剧情…", EditSelectedCutscene);
            }
            else
            {
                AddReadonly("状态", instCutscene.Id + "（" + instCutscene.Mode + (instCutscene.Enabled ? "" : "，已禁用") + "）");
                AddReadonly("规模", instCutscene.Steps.Count + " 个步骤");
                AddButtonRow("", "编辑剧情…", EditSelectedCutscene);
                AddButtonRow("", "解除绑定", () =>
                {
                    if (_battles == null) return;
                    _battles.RemoveCutscene(instRoom, inst.InstanceID);
                    SaveBattles(false);
                    RebuildInspector();
                });
            }
            AddSection("对话");
            var instDialogue = _battles?.DialogueForInstance(instRoom, inst.InstanceID);
            if (instDialogue == null)
            {
                AddReadonly("状态", "（未绑定）");
                AddButtonRow("", "为此实体新建纯对话…", EditSelectedDialogue);
            }
            else
            {
                AddReadonly("状态", instDialogue.Id + (instDialogue.Enabled ? "" : "（已禁用）"));
                AddReadonly("规模", instDialogue.Lines.Count + " 行文本");
                AddButtonRow("", "编辑对话…", EditSelectedDialogue);
                AddButtonRow("", "解除绑定", () =>
                {
                    if (_battles == null) return;
                    _battles.RemoveDialogue(instRoom, inst.InstanceID);
                    SaveBattles(false);
                    RebuildInspector();
                });
            }
            var layer = RoomEdit.LayerOf(_doc.Room, inst);
            AddReadonly("图层", layer == null ? "" : GameData.Name(layer.LayerName));
            AddNumberField("X", inst.X, v => ApplyInstance(inst, () => inst.X = v));
            AddNumberField("Y", inst.Y, v => ApplyInstance(inst, () => inst.Y = v));
            AddFloatField("缩放 X", inst.ScaleX, v => ApplyInstance(inst, () => inst.ScaleX = v));
            AddFloatField("缩放 Y", inst.ScaleY, v => ApplyInstance(inst, () => inst.ScaleY = v));
            AddFloatField("旋转", inst.Rotation, v => ApplyInstance(inst, () => inst.Rotation = v));
            AddNumberField("图像帧", inst.ImageIndex, v => ApplyInstance(inst, () => inst.ImageIndex = v));
            AddColorField("颜色", inst.Color, v => ApplyInstance(inst, () => inst.Color = v));
            AddSection("操作");
            var del = new Button { Text = "删除此实例 (Delete)", Height = 28 };
            Theme.StyleButton(del);
            del.Click += (s, e) => DeleteSelection();
            AddSpan(del);
            FitInspectorHeight();
            return;
        }

        var room = _doc.Room;
        AddSection("房间");
        AddReadonly("名称", GameData.Name(room.Name));
        AddNumberField("宽度", (int)room.Width, v => ApplyRoomSize(v, null));
        AddNumberField("高度", (int)room.Height, v => ApplyRoomSize(null, v));
        AddColorField("背景色", room.BackgroundColor, v => ApplyRoom(r => r.BackgroundColor = v));
        var cb = new CheckBox { Text = "绘制背景色", Checked = room.DrawBackgroundColor, ForeColor = Theme.Text, BackColor = Theme.Bg1, FlatStyle = FlatStyle.Flat };
        cb.CheckedChanged += (s, e) => ApplyRoom(r => r.DrawBackgroundColor = cb.Checked);
        AddSpan(cb);

        var targetLayer = _canvas.TargetLayer ?? room.Layers?.FirstOrDefault();
        if (targetLayer != null)
        {
            AddSection("目标图层");
            AddReadonly("名称", GameData.Name(targetLayer.LayerName));
            AddReadonly("类型", LayerKindText(targetLayer));
            AddNumberField("深度", targetLayer.LayerDepth, v => ApplyLayerDepth(targetLayer, v));
            AddFloatField("偏移 X", targetLayer.XOffset, v => ApplyLayerOffset(targetLayer, v, targetLayer.YOffset));
            AddFloatField("偏移 Y", targetLayer.YOffset, v => ApplyLayerOffset(targetLayer, targetLayer.XOffset, v));
            var layerVisible = new CheckBox
            {
                Text = "图层可见", Checked = targetLayer.IsVisible, ForeColor = Theme.Text,
                BackColor = Theme.Bg1, FlatStyle = FlatStyle.Flat,
            };
            layerVisible.CheckedChanged += (s, e) => ApplyLayerVisible(targetLayer, layerVisible.Checked);
            AddSpan(layerVisible);
            var bg = targetLayer.BackgroundData;
            if (bg != null)
            {
                AddSection("背景层");
                AddReadonly("精灵", bg.Sprite == null ? "（无）" : GameData.Name(bg.Sprite.Name));
                AddButtonRow("", "选择背景精灵…", () => PickBackgroundSprite(targetLayer));
                AddBackgroundToggle(targetLayer, "背景可见", bg.Visible, (b, v) => ApplyBackgroundProps(b, visible: v));
                AddBackgroundToggle(targetLayer, "水平平铺", bg.TiledHorizontally, (b, v) => ApplyBackgroundProps(b, tiledH: v));
                AddBackgroundToggle(targetLayer, "垂直平铺", bg.TiledVertically, (b, v) => ApplyBackgroundProps(b, tiledV: v));
                AddBackgroundToggle(targetLayer, "拉伸铺满", bg.Stretch, (b, v) => ApplyBackgroundProps(b, stretch: v));
                AddColorField("背景色", bg.Color, v => ApplyBackgroundProps(targetLayer, color: v));
            }
        }

        AddSection("统计");
        int instances = RoomEdit.AllInstances(room).Count();
        int tiles = room.Layers?.Sum(l => (l.AssetsData?.LegacyTiles?.Count ?? 0) + NativeTiles.CountNonZero(l)) ?? 0;
        AddReadonly("实例数", instances.ToString());
        AddReadonly("图块数", tiles.ToString());
        AddReadonly("图层数", (room.Layers?.Count ?? 0).ToString());
        AddHint("提示：在左侧图层列表点圆点可切换图层显示；点对象列表选择要放置的对象。\n瓦片选择 (S)：拖框选图块，拖动移动，Ctrl+C/V 复制粘贴，Delete 删除。");
        FitInspectorHeight();
    }

    /// <summary>内容高度写进 grid.Height，外部 _inspector 的 AutoScroll 才会出现滚动条。</summary>
    private void FitInspectorHeight()
    {
        _inspectorGrid.PerformLayout();
        int h = _inspectorGrid.PreferredSize.Height;
        if (h > 0) _inspectorGrid.Height = h;
    }

    private void ClearInspector()
    {
        _inspectorGrid.SuspendLayout();
        foreach (Control c in _inspectorGrid.Controls.Cast<Control>().ToList()) c.Dispose();
        _inspectorGrid.Controls.Clear();
        _inspectorGrid.RowStyles.Clear();
        _inspectorGrid.RowCount = 0;
        _inspectorGrid.ResumeLayout();
    }

    private int AddRow(Control c, int colspan = 1)
    {
        int row = _inspectorGrid.RowCount;
        _inspectorGrid.RowCount = row + 1;
        _inspectorGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _inspectorGrid.Controls.Add(c, 0, row);
        if (colspan > 1) _inspectorGrid.SetColumnSpan(c, colspan);
        return row;
    }

    private void AddField(string label, Control field)
    {
        int row = AddRow(new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Theme.TextDim,
            BackColor = Color.Transparent,
            Font = Theme.UI(8.5f),
            Margin = new Padding(10, 4, 4, 2),
        });
        _inspectorGrid.Controls.Add(field, 1, row);
        _inspectorGrid.SetColumnSpan(_inspectorGrid.GetControlFromPosition(0, row)!, 1);
        field.Dock = DockStyle.Fill;
        field.Margin = new Padding(0, 2, 10, 2);
        // re-place the label into column 0, field into column 1
        var lbl = _inspectorGrid.GetControlFromPosition(0, row);
        if (lbl != null && !ReferenceEquals(lbl, field))
        {
            _inspectorGrid.Controls.Remove(field);
            _inspectorGrid.Controls.Add(field, 1, row);
        }
    }

    private void AddSection(string text)
    {
        var lbl = new Label
        {
            Text = text.ToUpperInvariant(),
            AutoSize = false,
            Height = 24,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            ForeColor = Theme.Accent,
            BackColor = Color.Transparent,
            Font = SectionFont,
            Margin = new Padding(10, 10, 10, 2),
        };
        AddRow(lbl, 2);
    }

    private void AddHint(string text)
    {
        var lbl = new Label
        {
            Text = text,
            AutoSize = false,
            Height = 54,
            Dock = DockStyle.Fill,
            ForeColor = Theme.TextDim,
            BackColor = Color.Transparent,
            Font = Theme.UI(8.5f),
            Margin = new Padding(10, 6, 10, 6),
        };
        AddRow(lbl, 2);
    }

    private void AddSpan(Control c)
    {
        c.Dock = DockStyle.Fill;
        c.Margin = new Padding(10, 4, 10, 4);
        AddRow(c, 2);
    }

    private void AddReadonly(string label, string value)
    {
        var tb = new TextBox
        {
            Text = value,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Bg1,
            ForeColor = Theme.TextDim,
            Font = Theme.Mono(8.5f),
            TabStop = false,
        };
        AddField(label, tb);
    }

    private void AddNumberField(string label, int value, Action<int> commit)
    {
        var tb = new TextBox { Text = value.ToString(), AccessibleName = label };
        Theme.StyleTextBox(tb);
        tb.Leave += (s, e) => { if (int.TryParse(tb.Text.Trim(), out int v) && v != value) commit(v); else tb.Text = value.ToString(); };
        tb.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; tb.Parent?.Focus(); } };
        AddField(label, tb);
    }

    private void AddFloatField(string label, float value, Action<float> commit)
    {
        var tb = new TextBox { Text = value.ToString("0.###"), AccessibleName = label };
        Theme.StyleTextBox(tb);
        tb.Leave += (s, e) =>
        {
            if (float.TryParse(tb.Text.Trim(), out float v) && Math.Abs(v - value) > 0.0001f) commit(v);
            else tb.Text = value.ToString("0.###");
        };
        tb.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; tb.Parent?.Focus(); } };
        AddField(label, tb);
    }

    private void AddButtonRow(string label, string text, Action onClick)
    {
        var b = new Button { Text = text, Height = 26, Dock = DockStyle.Fill };
        Theme.StyleButton(b);
        b.Click += (s, e) => onClick();
        AddField(label, b);
    }

    private void AddColorField(string label, uint value, Action<uint> commit)
    {
        var tb = new TextBox { Text = $"#{value & 0xFFFFFF:X6}", AccessibleName = label };
        Theme.StyleTextBox(tb);
        tb.Leave += (s, e) =>
        {
            var txt = tb.Text.Trim().TrimStart('#');
            if (uint.TryParse(txt, System.Globalization.NumberStyles.HexNumber, null, out uint v))
            {
                uint full = 0xFF000000u | (v & 0xFFFFFFu);
                if (full != value) commit(full);
            }
            else tb.Text = $"#{value & 0xFFFFFF:X6}";
        };
        AddField(label, tb);
    }

    private void ApplyInstance(UndertaleRoom.GameObject inst, Action act)
    {
        if (_doc?.Room == null) return;
        _doc.PushUndo();
        act();
        _doc.MarkDirty(_doc.RoomName());
        _doc.InvalidateRender(_doc.RoomName());
        _canvas.RefreshRoom();
        UpdateTitle();
    }

    private void ApplyRoom(Action<UndertaleRoom> act)
    {
        if (_doc?.Room == null) return;
        _doc.PushUndo();
        act(_doc.Room);
        _doc.MarkDirty(_doc.RoomName());
        _doc.InvalidateRender(_doc.RoomName());
        _canvas.RefreshRoom();
        UpdateTitle();
    }

    /// <summary>多选实例的对齐/分布（包围盒估算在 EditorDocument 里，这里只负责刷新与状态栏）。</summary>
    private void ApplyAlign(EditorDocument.AlignMode mode, string label)
    {
        if (_doc?.Room == null) return;
        int moved = _doc.AlignSelection(mode);
        if (moved == 0)
        {
            _statusLeft.Text = "对齐/分布：实例已在目标位置，没有移动";
            RebuildInspector();
            return;
        }
        _canvas.RefreshRoom();
        RebuildInspector();
        UpdateTitle();
        _statusLeft.Text = $"已{label} {moved} 个实例";
    }

    /// <summary>
    /// 房间尺寸。以前走 ApplyRoom 直接改 room.Width/Height：撤销/脏标记有了，但左侧房间列表
    /// 的「320×240」、状态栏与属性面板不会跟着变，玩家会以为没改成功。改为统一走
    /// EditorDocument.SetRoomSize 并刷新这三处。
    /// </summary>
    private void ApplyRoomSize(int? width, int? height)
    {
        var room = _doc?.Room;
        if (_doc == null || room == null) return;
        uint w = width.HasValue ? (uint)Math.Clamp(width.Value, 16, 10000) : room.Width;
        uint h = height.HasValue ? (uint)Math.Clamp(height.Value, 16, 10000) : room.Height;
        if (!_doc.SetRoomSize(w, h))
        {
            // 没变（或非法输入）：把输入框恢复成真实值。
            // Leave 事件里不能立刻销毁正在失焦的文本框，所以延后重建属性面板。
            if (IsHandleCreated) BeginInvoke((Action)RebuildInspector);
            return;
        }
        _canvas.RefreshRoom();
        RebuildRoomList();
        UpdateTitle();
        UpdateStatus();
        if (IsHandleCreated) BeginInvoke((Action)RebuildInspector);
    }

    private void ApplySprite(Action<UndertaleRoom.SpriteInstance> act)
    {
        if (_doc?.Room == null || _doc.SelectedSprite == null) return;
        _doc.UpdateSelectedSprite(act);
        _canvas.RefreshRoom();
        UpdateTitle();
        UpdateStatus();
    }

    private void ApplyTile(Action<UndertaleRoom.Tile> act)
    {
        if (_doc?.Room == null || _doc.SelectedTile == null) return;
        _doc.PushUndo();
        act(_doc.SelectedTile);
        _doc.MarkDirty(_doc.RoomName());
        _canvas.RefreshRoom();
        RebuildTileList();
        UpdateTitle();
        UpdateStatus();
    }

    private static string LayerKindText(UndertaleRoom.Layer layer) => layer.LayerType switch
    {
        UndertaleRoom.LayerType.Instances => "实例",
        UndertaleRoom.LayerType.Background => "背景",
        UndertaleRoom.LayerType.Assets => "资源/图块",
        UndertaleRoom.LayerType.Tiles => "瓦片(GMS2)",
        UndertaleRoom.LayerType.Path => "路径",
        UndertaleRoom.LayerType.Path2 => "路径2",
        UndertaleRoom.LayerType.Effect => "效果",
        _ => layer.LayerType.ToString(),
    };

    private void ApplyLayerDepth(UndertaleRoom.Layer layer, int depth)
    {
        if (_doc?.SetLayerDepth(layer, depth) != true) return;
        AfterLayerPropChanged($"图层深度：{GameData.Name(layer.LayerName)} -> {depth}");
    }

    private void ApplyLayerOffset(UndertaleRoom.Layer layer, float x, float y)
    {
        if (_doc?.SetLayerOffset(layer, x, y) != true) return;
        AfterLayerPropChanged($"图层偏移：{GameData.Name(layer.LayerName)} ({x}, {y})");
    }

    private void ApplyLayerVisible(UndertaleRoom.Layer layer, bool visible)
    {
        if (_doc?.SetLayerVisible(layer, visible) != true) return;
        AfterLayerPropChanged($"{GameData.Name(layer.LayerName)}：{(visible ? "显示" : "隐藏")}");
    }

    private void ApplyBackgroundProps(UndertaleRoom.Layer layer, bool? visible = null, bool? tiledH = null,
        bool? tiledV = null, bool? stretch = null, uint? color = null)
    {
        if (_doc?.SetBackgroundProps(layer, visible, tiledH, tiledV, stretch, color) != true) return;
        AfterLayerPropChanged($"背景层属性：{GameData.Name(layer.LayerName)}");
    }

    private void AddBackgroundToggle(UndertaleRoom.Layer layer, string text, bool value,
        Action<UndertaleRoom.Layer, bool> apply)
    {
        var cb = new CheckBox
        {
            Text = text, Checked = value, ForeColor = Theme.Text,
            BackColor = Theme.Bg1, FlatStyle = FlatStyle.Flat,
        };
        cb.CheckedChanged += (s, e) => apply(layer, cb.Checked);
        AddSpan(cb);
    }

    private void AfterLayerPropChanged(string status)
    {
        _canvas.RefreshRoom();
        RebuildLayers();
        UpdateTitle();
        UpdateStatus();
        _statusLeft.Text = status;
    }

    // ---------------------------------------------------------------- actions

    private void DoUndo()
    {
        if (_doc?.Undo() == true)
        {
            _canvas.RefreshRoom();
            RebuildRoomList();   // 撤销可能改回房间尺寸/名字，左侧列表的「320×240」要跟着回来
            RebuildLayers();
            RebuildInspector();
            RebuildTileList();
            UpdateTitle();
            _statusLeft.Text = "已撤销";
        }
    }

    private void DoRedo()
    {
        if (_doc?.Redo() == true)
        {
            _canvas.RefreshRoom();
            RebuildRoomList();   // 重做同样可能改房间尺寸/名字
            RebuildLayers();
            RebuildInspector();
            RebuildTileList();
            UpdateTitle();
            _statusLeft.Text = "已重做";
        }
    }

    private void DeleteSelection()
    {
        if (_doc?.Room == null) return;
        if (_doc.TileSelectionCount > 0)
        {
            int removed = _doc.DeleteTileSelection();
            _canvas.RefreshRoom();
            RebuildLayers();
            RebuildInspector();
            RebuildTileList();
            UpdateTitle();
            UpdateStatus();
            _statusLeft.Text = removed > 0 ? $"已删除 {removed} 个图块" : "没有可删除的图块";
            return;
        }
        if (_doc.NativeSelectionCount > 0)
        {
            int removed = _doc.DeleteNativeTileSelection();
            _canvas.RefreshRoom();
            RebuildLayers();
            RebuildInspector();
            RebuildTileList();
            UpdateTitle();
            UpdateStatus();
            _statusLeft.Text = removed > 0 ? $"已删除 {removed} 个原生图块" : "没有可删除的原生图块";
            return;
        }
        if (_doc.SelectedNativeTile != null)
        {
            int removed = _doc.DeleteSelectedNativeTile();
            _canvas.RefreshRoom();
            RebuildLayers();
            RebuildInspector();
            UpdateTitle();
            UpdateStatus();
            _statusLeft.Text = removed > 0 ? "已删除原生图块" : "没有可删除的图块";
            return;
        }
        if (_doc.SelectedSprite != null)
        {
            bool removed = _doc.DeleteSelectedSprite();
            _canvas.RefreshRoom();
            RebuildLayers();
            RebuildInspector();
            UpdateTitle();
            UpdateStatus();
            _statusLeft.Text = removed ? "已删除精灵实例" : "没有可删除的精灵实例";
            return;
        }
        if (_doc.SelectedTile != null)
        {
            var tile = _doc.SelectedTile;
            _doc.PushUndo();
            RoomEdit.RemoveTile(_doc.Room, tile);
            _doc.SelectTile(null);
            _doc.MarkDirty(_doc.RoomName());
            _canvas.RefreshRoom();
            RebuildInspector();
            RebuildTileList();
            UpdateTitle();
            UpdateStatus();
            _statusLeft.Text = "已删除图块";
            return;
        }
        if (_doc.Selection.Count == 0) return;
        int n = _doc.DeleteSelectedInstances();
        _canvas.RefreshRoom();
        RebuildLayers();
        RebuildInspector();
        RebuildTileList();
        UpdateTitle();
        UpdateStatus();
        _statusLeft.Text = n > 0 ? $"已删除 {n} 个实例" : "没有可删除的实例";
    }

    private void SelectAllInstances()
    {
        if (_doc?.Room == null) return;
        if (_canvas.Tool == EditorTool.TileSelect)
        {
            var layer = TileEditLayer();
            if (layer == null) { _statusLeft.Text = "当前房间没有图块图层"; return; }
            if (NativeTiles.IsNative(layer))
            {
                int nn = _doc.SelectAllNativeTiles(layer);
                _canvas.Invalidate();
                _statusLeft.Text = $"已全选 {nn} 个原生图块";
                return;
            }
            int n = _doc.SelectAllTiles(layer);
            _canvas.Invalidate();
            _statusLeft.Text = $"已全选 {n} 个图块";
            return;
        }
        _doc.SelectMany(RoomEdit.AllInstances(_doc.Room).Select(t => t.Inst), false);
        _statusLeft.Text = $"已全选 {_doc.Selection.Count} 个实例";
    }

    /// <summary>瓦片选择/编辑用的图层：目标图层本身是瓦片层就用它，否则房间第一个瓦片层。</summary>
    private UndertaleRoom.Layer? TileEditLayer()
    {
        var target = _canvas.TargetLayer;
        if (target != null && (target.AssetsData?.LegacyTiles != null || NativeTiles.IsNative(target))) return target;
        return _doc?.Room != null
            ? RoomEdit.TilesLayer(_doc.Room) ?? _doc.Room.Layers.OrderBy(l => l.LayerDepth).FirstOrDefault(NativeTiles.IsNative)
            : null;
    }

    private void InvertSelection()
    {
        if (_doc?.Room == null) return;
        if (_canvas.Tool == EditorTool.TileSelect)
        {
            var layer = TileEditLayer();
            if (layer == null) { _statusLeft.Text = "当前房间没有图块图层"; return; }
            if (NativeTiles.IsNative(layer))
            {
                var refs = new List<NativeTileRef>();
                foreach (var c in NativeTiles.Cells(layer))
                {
                    if (NativeTiles.TileId(NativeTiles.Get(layer, c.X, c.Y)) == 0) continue;
                    var r = new NativeTileRef(layer, c.X, c.Y);
                    if (!_doc.IsNativeSelected(r)) refs.Add(r);
                }
                _doc.SetNativeSelection(refs, false);
                _canvas.Invalidate();
                _statusLeft.Text = $"已反选，当前选中 {_doc.NativeSelectionCount} 个原生图块";
                return;
            }
            var keep = _doc.TileSelection.Where(t => !RoomEdit.AllTilesInLayer(layer).Contains(t)).ToList();
            _doc.SetTileSelection(keep, false);
            _canvas.Invalidate();
            _statusLeft.Text = $"已反选，当前选中 {_doc.TileSelectionCount} 个图块";
            return;
        }
        _doc.InvertSelection();
        _statusLeft.Text = $"已反选，当前选中 {_doc.Selection.Count} 个实例";
    }

    private void CopySelection()
    {
        if (_doc?.Room == null) return;
        if (_doc.NativeSelectionCount > 0)
        {
            int nn = _doc.CopyNativeSelection();
            _statusLeft.Text = nn > 0 ? $"已复制 {nn} 个原生图块" : "没有选中原生图块可复制";
            return;
        }
        if (_doc.TileSelectionCount > 0)
        {
            int tn = _doc.CopyTileSelection();
            _statusLeft.Text = tn > 0 ? $"已复制 {tn} 个图块" : "没有选中图块可复制";
            return;
        }
        int n = _doc.CopySelection();
        _pasteSerial = 0;
        _statusLeft.Text = n > 0 ? $"已复制 {n} 个实例" : "没有选中实例可复制";
    }

    private void CutSelection()
    {
        if (_doc?.Room == null) return;
        if (_doc.NativeSelectionCount > 0)
        {
            int nn = _doc.CutNativeSelection();
            if (nn == 0) { _statusLeft.Text = "没有选中原生图块可剪切"; return; }
            _canvas.RefreshRoom();
            RebuildLayers();
            RebuildInspector();
            RebuildTileList();
            UpdateTitle();
            UpdateStatus();
            _statusLeft.Text = $"已剪切 {nn} 个原生图块";
            return;
        }
        if (_doc.TileSelectionCount > 0)
        {
            int tn = _doc.CutTileSelection();
            if (tn == 0) { _statusLeft.Text = "没有选中图块可剪切"; return; }
            _canvas.RefreshRoom();
            RebuildLayers();
            RebuildInspector();
            RebuildTileList();
            UpdateTitle();
            UpdateStatus();
            _statusLeft.Text = $"已剪切 {tn} 个图块";
            return;
        }
        int n = _doc.CutSelection();
        _pasteSerial = 0;
        if (n == 0) { _statusLeft.Text = "没有选中实例可剪切"; return; }
        _canvas.RefreshRoom();
        RebuildLayers();
        RebuildInspector();
        RebuildTileList();
        UpdateTitle();
        UpdateStatus();
        _statusLeft.Text = $"已剪切 {n} 个实例";
    }

    private void PasteClipboard()
    {
        if (_doc?.Room == null) return;
        if (_doc.NativeClipboardCount > 0)
        {
            var nlayer = TileEditLayer();
            if (nlayer == null || !NativeTiles.IsNative(nlayer))
            {
                _statusLeft.Text = "原生图块只能粘贴到原生 Tiles 图层（先选中一个原生图层）";
                return;
            }
            if (!NativeTileEdit.SnapCell(nlayer, _canvas.LastWorld, out var ncell))
            {
                _statusLeft.Text = "粘贴失败：光标不在该原生图层范围内";
                return;
            }
            int ncx = ncell.X, ncy = ncell.Y;
            int pn = _doc.PasteNativeTiles(nlayer, ncx, ncy);
            if (pn == 0) { _statusLeft.Text = "粘贴失败：原生图块剪贴板为空"; return; }
            _canvas.RefreshRoom();
            RebuildLayers();
            RebuildInspector();
            RebuildTileList();
            UpdateTitle();
            UpdateStatus();
            _statusLeft.Text = $"已粘贴 {pn} 个原生图块（{ncx}, {ncy}）";
            return;
        }
        if (_doc.TileClipboardCount > 0)
        {
            var layer = TileEditLayer();
            if (layer == null) { _statusLeft.Text = "粘贴失败：当前房间没有图块图层"; return; }
            var cell = _doc.TileClipboardCellSize;
            var anchor = RoomEdit.SnapToTileCell(cell, _canvas.LastWorld);
            var pasted = _doc.PasteTiles(layer, anchor.X, anchor.Y);
            if (pasted.Count == 0) { _statusLeft.Text = "粘贴失败：图块剪贴板为空"; return; }
            _canvas.RefreshRoom();
            RebuildLayers();
            RebuildInspector();
            RebuildTileList();
            UpdateTitle();
            UpdateStatus();
            _statusLeft.Text = $"已粘贴 {pasted.Count} 个图块（{anchor.X}, {anchor.Y}）";
            return;
        }
        if (_doc.ClipboardCount == 0) { _statusLeft.Text = "剪贴板为空（先 Ctrl+C 复制实例或图块）"; return; }
        int step = Math.Max(1, _canvas.GridSize);
        _pasteSerial++;
        var created = _doc.PasteClipboard(step * _pasteSerial, step * _pasteSerial);
        if (created.Count == 0) { _statusLeft.Text = "粘贴失败：当前房间没有实例图层"; return; }
        _canvas.RefreshRoom();
        RebuildLayers();
        RebuildInspector();
        UpdateTitle();
        UpdateStatus();
        _statusLeft.Text = $"已粘贴 {created.Count} 个实例（偏移 {step * _pasteSerial}px）";
    }

    private void DuplicateSelection()
    {
        if (_doc?.Room == null) return;
        if (_doc.NativeSelectionCount > 0)
        {
            var nlayer = _doc.NativeSelection[0].Layer;
            var first = _doc.NativeSelection[0];
            int cn = _doc.CopyNativeSelection();
            if (cn == 0) { _statusLeft.Text = "没有选中原生图块可再制"; return; }
            int dn = _doc.PasteNativeTiles(nlayer, first.X + 1, first.Y + 1);
            if (dn == 0) { _statusLeft.Text = "再制失败"; return; }
            _canvas.RefreshRoom();
            RebuildLayers();
            RebuildInspector();
            RebuildTileList();
            UpdateTitle();
            UpdateStatus();
            _statusLeft.Text = $"已再制 {dn} 个原生图块";
            return;
        }
        if (_doc.TileSelectionCount > 0)
        {
            var layer = TileEditLayer();
            if (layer == null) { _statusLeft.Text = "当前房间没有图块图层"; return; }
            var cell = _doc.TileClipboardCellSize;
            int n = _doc.CopyTileSelection();
            if (n == 0) { _statusLeft.Text = "没有选中图块可再制"; return; }
            // 再制偏移一个图块尺寸（首块尺寸），避免与原图块完全重叠。
            var first = _doc.TileSelection[0];
            var dup = _doc.PasteTiles(layer, first.X + (int)MathF.Round(cell.Width), first.Y + (int)MathF.Round(cell.Height));
            if (dup.Count == 0) { _statusLeft.Text = "再制失败"; return; }
            _canvas.RefreshRoom();
            RebuildLayers();
            RebuildInspector();
            RebuildTileList();
            UpdateTitle();
            UpdateStatus();
            _statusLeft.Text = $"已再制 {dup.Count} 个图块";
            return;
        }
        int step = Math.Max(1, _canvas.GridSize);
        var createdInst = _doc.DuplicateSelection(step, step);
        var created = createdInst;
        if (created.Count == 0) { _statusLeft.Text = "没有选中实例可再制"; return; }
        _canvas.RefreshRoom();
        RebuildLayers();
        RebuildInspector();
        UpdateTitle();
        UpdateStatus();
        _statusLeft.Text = $"已再制 {created.Count} 个实例（偏移 {step}px）";
    }

    private void Nudge(int dx, int dy)
    {
        if (_doc?.Room == null) return;
        if (_doc.TileSelectionCount > 0)
        {
            _doc.PushUndo();
            foreach (var t in _doc.TileSelection)
            {
                t.X += dx;
                t.Y += dy;
            }
            _doc.MarkDirty(_doc.RoomName());
            _doc.InvalidateRender(_doc.RoomName());
            _canvas.RefreshRoom();
            UpdateTitle();
            return;
        }
        if (_doc.Selection.Count == 0) return;
        _doc.PushUndo();
        foreach (var inst in _doc.Selection)
        {
            inst.X += dx;
            inst.Y += dy;
        }
        _doc.MarkDirty(_doc.RoomName());
        _doc.InvalidateRender(_doc.RoomName());
        _canvas.RefreshRoom();
        UpdateTitle();
    }

    private void ToggleLayerVisible(UndertaleRoom.Layer layer)
    {
        if (_doc?.Room == null) return;
        if (!_doc.SetLayerVisible(layer, !layer.IsVisible)) return;
        _canvas.RefreshRoom();
        RebuildLayers();
        RebuildInspector();
        _statusLeft.Text = $"{GameData.Name(layer.LayerName)}：{(layer.IsVisible ? "显示" : "隐藏")}";
    }

    // ------------------------------------------------------ room / layer edit UI

    /// <summary>Layer that placement tools draw into: canvas target, else first layer in the list.</summary>
    private UndertaleRoom.Layer? TargetLayer => _canvas.TargetLayer ?? (_layerRows.Count > 0 ? _layerRows[0].Layer : null);

    private ContextMenuStrip BuildRoomContextMenu()
    {
        var m = Theme.CreateMenu();
        m.Items.Add(Theme.Item("新建房间（复制当前）…", (s, e) => NewRoomDialog(true)));
        m.Items.Add(Theme.Item("新建空白房间…", (s, e) => NewRoomDialog(false)));
        m.Items.Add(Theme.Item("复制当前房间", (s, e) => DuplicateRoomQuick()));
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(Theme.Item("重命名房间…", (s, e) => RenameRoomDialog()));
        m.Items.Add(Theme.Item("删除房间", (s, e) => DeleteRoomAction()));
        return m;
    }

    private ContextMenuStrip BuildLayerContextMenu()
    {
        var m = Theme.CreateMenu();
        m.Items.Add(Theme.Item("新建实例层", (s, e) => AddLayerAction(false)));
        m.Items.Add(Theme.Item("新建瓦片层", (s, e) => AddLayerAction(true)));
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(Theme.Item("重命名图层…", (s, e) => RenameLayerDialog()));
        m.Items.Add(Theme.Item("上移图层", (s, e) => MoveLayerAction(-1)));
        m.Items.Add(Theme.Item("下移图层", (s, e) => MoveLayerAction(1)));
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(Theme.Item("删除图层", (s, e) => DeleteLayerAction()));
        return m;
    }

    private void NewRoomDialog(bool withContent)
    {
        if (_doc?.Room == null) return;
        string baseName = _doc.RoomName() + (withContent ? "_copy" : "_new");
        string? name = PromptDialog.Show(this, withContent ? "新建房间" : "新建空白房间",
            withContent ? "新房间名称（会复制当前房间的图层与内容）：" : "新房间名称（只复制图层结构，内容为空）：", baseName);
        if (name == null) return;
        if (name.Length == 0) name = baseName;
        var room = _doc.NewRoom(name, withContent);
        if (room == null) return;
        _canvas.FitToView();
        _statusLeft.Text = $"已新建{(withContent ? "" : "空白")}房间：{_doc.RoomName(room)}";
    }

    private void DuplicateRoomQuick()
    {
        if (_doc?.Room == null) return;
        var room = _doc.DuplicateRoom(null);
        if (room == null) return;
        _canvas.FitToView();
        _statusLeft.Text = $"已复制房间 → {_doc.RoomName(room)}";
    }

    private void RenameRoomDialog()
    {
        if (_doc?.Room == null) return;
        string old = _doc.RoomName();
        string? name = PromptDialog.Show(this, "重命名房间", "房间名称：", old);
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!_doc.RenameRoom(name!)) return;
        _statusLeft.Text = $"已重命名房间：{old} → {name}";
        UpdateStatus();
    }

    private void DeleteRoomAction()
    {
        if (_doc?.Room == null) return;
        string name = _doc.RoomName();
        if (_doc.Game.Rooms.Count() <= 1)
        {
            MessageBox.Show(this, "至少要保留一个房间。", "删除房间", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var r = MessageBox.Show(this,
            $"删除房间「{name}」？\n\n删除会同时从 RoomOrder 里移除它；这个操作不进撤销栈。\n保存后才会写回 data.win。",
            "删除房间", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (r != DialogResult.Yes) return;
        if (!_doc.DeleteRoom()) return;
        _statusLeft.Text = $"已删除房间：{name}";
    }

    private void AddLayerAction(bool tiles)
    {
        if (_doc?.Room == null) return;
        var target = TargetLayer;
        int depth = target != null ? target.LayerDepth - 1 : 0;
        var layer = _doc.AddLayer(tiles, depth);
        if (layer == null) return;
        _canvas.TargetLayer = layer;
        RebuildLayers();
        _canvas.RefreshRoom();
        _statusLeft.Text = $"已添加{(tiles ? "瓦片" : "实例")}层：{GameData.Name(layer.LayerName)}";
    }

    private void RenameLayerDialog()
    {
        var layer = TargetLayer;
        if (_doc?.Room == null || layer == null) return;
        string old = GameData.Name(layer.LayerName);
        string? name = PromptDialog.Show(this, "重命名图层", "图层名称：", old);
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!_doc.RenameLayer(layer, name!)) return;
        RebuildLayers();
        _statusLeft.Text = $"已重命名图层：{old} → {name}";
    }

    private void MoveLayerAction(int delta)
    {
        var layer = TargetLayer;
        if (_doc?.Room == null || layer == null) return;
        if (!_doc.MoveLayer(layer, delta))
        {
            _statusLeft.Text = delta < 0 ? "已经是最前面的图层" : "已经是最后面的图层";
            return;
        }
        RebuildLayers();
        _canvas.RefreshRoom();
        _statusLeft.Text = $"图层「{GameData.Name(layer.LayerName)}」已{(delta < 0 ? "上" : "下")}移";
    }

    private void DeleteLayerAction()
    {
        var layer = TargetLayer;
        if (_doc?.Room == null || layer == null) return;
        string name = GameData.Name(layer.LayerName);
        int content = NativeTiles.CountContent(layer);
        if (content > 0)
        {
            var r = MessageBox.Show(this,
                $"图层「{name}」里还有 {content} 个对象/图块，删除会连同内容一起删掉。仍要删除吗？",
                "删除图层", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) return;
        }
        if (!_doc.RemoveLayer(layer))
        {
            _statusLeft.Text = "至少要保留一个图层";
            return;
        }
        RebuildLayers();
        _canvas.RefreshRoom();
        _statusLeft.Text = $"已删除图层：{name}";
    }

    private void Save(bool saveAs)
    {
        if (_doc == null) return;
        string path = _doc.FilePath;
        if (saveAs)
        {
            using var dlg = new SaveFileDialog
            {
                Title = "另存为 data.win",
                Filter = "data.win|*.win|所有文件|*.*",
                FileName = Path.GetFileName(path),
            };
            try { dlg.InitialDirectory = Path.GetDirectoryName(path); } catch { }
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            path = dlg.FileName;
        }

        var root = FindGameRoot();
        // 原版基线只读：Ctrl+S / 另存为到 backup 都在写盘前拦下，给出可操作去处。
        if (FileRole.Classify(path, root) == FileKind.Baseline)
        {
            var r = MessageBox.Show(this,
                "这是原版基线（backup/），只读，不能原地保存。\n\n" +
                "点「是」另存为…（可存成 Neutraled mod 的 ref/data.win 或任意工作副本）。\n" +
                "要编辑游戏实际读取的文件，请用 文件 → 打开游戏章节 → 游戏文件（可保存）。\n\n" +
                "点「否」取消；原文件不会被改动。",
                FileRole.BaselineBadge, MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (r != DialogResult.Yes)
            {
                _statusLeft.Text = "保存已拦截：原版基线只读，原文件未改动";
                return;
            }
            using var dlg = new SaveFileDialog
            {
                Title = "另存为（原版基线只读）",
                Filter = "data.win|*.win|所有文件|*.*",
                FileName = Path.GetFileName(path),
            };
            try { dlg.InitialDirectory = Path.GetDirectoryName(path); } catch { }
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            path = dlg.FileName;
            if (FileRole.Classify(path, root) == FileKind.Baseline)
            {
                MessageBox.Show(this,
                    "目标仍在原版基线目录（backup/）里，已拒绝写入。请选择 backup 之外的位置。",
                    FileRole.BaselineBadge, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        var targetKind = FileRole.Classify(path, root);
        if (FileRole.IsLive(targetKind) && !_warnedDeployed)
        {
            var r = MessageBox.Show(this,
                "你正在覆盖 Neutraled 的部署产物。\n\nNeutraled 重新部署时会用原版备份 + mods 重建这个文件，这次修改会被覆盖。\n\n仍要保存吗？",
                "保存到部署产物", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) return;
            _warnedDeployed = true;
        }
        Cursor = Cursors.WaitCursor;
        _statusLeft.Text = "正在写回 data.win …";
        Application.DoEvents();
        try
        {
            var saved = _doc.Save(path, false, root);
            if (!saved.Ok)
            {
                MessageBox.Show(this, saved.Error ?? "保存被拒绝。", "保存已拦截",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _statusLeft.Text = "保存已拦截：" + (saved.Error ?? "");
                return;
            }
            var side = _battles?.Save(BattleProject.SidecarFor(path), false, root);
            if (side.HasValue && !side.Value.Ok)
                throw new InvalidOperationException(side.Value.Error ?? "战斗配置保存被拒绝");
            _fileRole = FileRole.Classify(path, root);
            _statusLeft.Text = $"已保存 {path}（战斗配置 {_battles?.File.Battles.Count ?? 0} 场 / {_battles?.File.Dialogues.Count ?? 0} 段对话 / {_battles?.File.Cutscenes.Count ?? 0} 段剧情 / {_battles?.File.Settings.Count ?? 0} 个设置项）";
            UpdateTitle();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "保存失败：\n" + ex.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _statusLeft.Text = "保存失败：" + ex.Message;
        }
        finally { Cursor = Cursors.Default; }
    }

    private void ShowResourceBrowser()
    {
        if (_doc == null)
        {
            MessageBox.Show(this, "请先打开一个 data.win（文件 → 打开 data.win，或文件 → 打开游戏章节）。", "资源浏览器", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var dlg = new ResourceBrowserDialog(_doc.Game);
        dlg.ShowDialog(this);
    }

    private void ShowHelp()
    {
        MessageBox.Show(this,
            "工具\n  V 选择 / 拖动实例\n  H 平移（也可中键拖动或按住空格）\n  B 放置：先在右侧对象列表选对象，再点画布\n  X 擦除：点谁删谁\n" +
            "  T 瓦片：右「瓦片笔刷」选图块后，左键拖动连续绘制、右键拖动连续擦除、Alt+点击拾取\n" +
          "  新房间/新瓦片层没有现成图块时，用「选择图块来源…」挑精灵并框选帧内区域当笔刷\n" +
          "  「瓦片调色板…」把整张 tileset 按单元格切网格：点格子即设为笔刷（窗口非模态，可边看边画）\n" +
          "  背景层属性里可用「选择背景精灵…」更换背景图\n" +
            "  R 矩形：拖出矩形区域整片填充；右键拖出为整片擦除\n  L 直线：拖出直线填充；右键拖出为整片擦除\n" +
            "  C 圆形：从圆心拖出半径（取两轴较大者）整片填充；右键为整片擦除\n" +
            "  E 椭圆：拖出包围盒两角整片填充；右键为整片擦除\n" +
            "  K 油漆桶：左键替换连通同图块区域，右键整片擦除（点空白处填满连通空白）\n\n" +
            "视图\n  滚轮 以光标为中心缩放\n  F 适应窗口 · Ctrl+0 实际大小 · G 网格 · F5 重新渲染\n  F4 资源浏览器（只读：房间/对象/精灵/背景/字体/音效/脚本代码/着色器/序列/时间线/纹理页/路径）\n\n" +
            "编辑\n  拖动 移动选中实例 · 方向键 微移 1px（Shift 8px）\n  框选 空白处拖出矩形 · Shift 加选 · Ctrl+A 全选 · Ctrl+I 反选\n  Delete 删除 · Ctrl+C/X/V 复制/剪切/粘贴 · Ctrl+D 再制\n  Ctrl+Z 撤销 · Ctrl+Y 重做\n" +
            "  S 瓦片选择：拖框选图块（Shift 加选/反选）· 拖动已选图块移动 · 右键删单块\n     Ctrl+A/C/X/V/D 对图块同样有效（粘贴落在光标处）\n\n" +
            "文件\n  Ctrl+O 打开 · Ctrl+S 保存 · Ctrl+Shift+S 另存为\n\n" +
            "战斗\n  选中地图上的一个实例 → 属性面板「战斗」→「为此实体新建战斗…」（或 Ctrl+B）\n" +
            "  encounter = 进战斗界面（敌人/每回合弹幕/回合上限/胜负效果）；overworld = 地图上直接躲弹幕\n" +
            "  弹幕 type 用目标章节原版 obj_dbulletcontroller 模板编号；跨章节最稳是 0（环形）与 1（上下边缘）\n" +
            "  菜单 战斗 → 生成战斗 mod… 把当前战斗配置导出成可部署的 Neutraled mod\n" +
            "  菜单 战斗 → mods 清单（mods.json）… 编辑整包清单（类似 Everest.yaml：id/名称/作者/版本/启停/依赖/加载顺序）\n\n" +
            "对话与 Mod 设置项\n  选中实例 → 属性面板「对话」→「为此实体新建纯对话…」（或 Ctrl+Shift+B）：多行文本 + 触发半径/flag + 结束 effects\n" +
            "  菜单 战斗 → Mod 设置项…：注册进游戏内 Neutraled「Mod 设置」面板的项（bool/choice/int）\n" +
            "  功能绑定 feature：battlesEnabled / overworldEnabled / dialoguesEnabled / cutscenesEnabled / barrageDamagePercent，切换后实时生效（存档持久化）\n\n" +
            "简单剧情\n  选中实例 → 属性面板「剧情」→「为此实体新建剧情…」（或 Ctrl+Shift+K）：mode=interact 按 Z 触发 / area 进入半径自动播放\n" +
            "  步骤：wait 等帧 / move 移动(帧数内插值到 X,Y) / face 转向 / action 换精灵与帧 / dialogue 播放对话 / flag 设清 flag / message 写日志\n" +
            "  actor=player 玩家，actor=trigger 本段剧情绑定的地图实体；结束后按「结束效果」设 flag/治疗/传送/提示\n" +
            "  控制台：cutscene list / cutscene start <id> / cutscene stop\n\n" +
            "左侧列表点图层圆点可显示/隐藏该图层。",
            "快捷键与用法", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // -------------------------------------------------------------- keyboard

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Theme.EnableDarkTitleBar(this);
        if (_doc == null)
        {
            string? def = FindDefaultDataWin();
            if (def != null)
            {
                OpenPath(def, false);
                if (_doc != null && _fileRole == FileKind.Baseline)
                    _statusLeft.Text = "最近文件是原版基线 · 只读 — Ctrl+S 会被拦截；要编辑 live 请用 文件 → 打开游戏章节 → 游戏文件（可保存）";
            }
            else _statusLeft.Text = "就绪 — 文件 → 打开 data.win（也可从菜单打开游戏章节）";
        }
    }

    /// <summary>启动目标：最近文件（可能是 baseline，打开后标只读）→ live chapter1；不再默认 backup。</summary>
    private static string? FindDefaultDataWin() =>
        StartupTarget.Resolve(FindGameRoot(), RecentStore.LastPath);

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        bool editingText = ActiveControl is TextBoxBase;
        if (_doc != null && !editingText)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.Z: DoUndo(); return true;
                case Keys.Control | Keys.Y: DoRedo(); return true;
                case Keys.Control | Keys.Shift | Keys.Z: DoRedo(); return true;
                case Keys.Control | Keys.S: Save(false); return true;
                case Keys.Control | Keys.Shift | Keys.S: Save(true); return true;
                case Keys.Control | Keys.O: OpenDialog(); return true;
                case Keys.Control | Keys.A: SelectAllInstances(); return true;
                case Keys.Control | Keys.I: InvertSelection(); return true;
                case Keys.Control | Keys.C: CopySelection(); return true;
                case Keys.Control | Keys.X: CutSelection(); return true;
                case Keys.Control | Keys.V: PasteClipboard(); return true;
                case Keys.Control | Keys.D: DuplicateSelection(); return true;
                case Keys.Control | Keys.B: EditSelectedBattle(); return true;
                case Keys.Control | Keys.Shift | Keys.B: EditSelectedDialogue(); return true;
                case Keys.Control | Keys.Shift | Keys.K: EditSelectedCutscene(); return true;
                case Keys.Control | Keys.D0: _canvas.SetZoom(1f); return true;
                case Keys.Delete: DeleteSelection(); return true;
                case Keys.Control | Keys.Delete: DeleteRoomAction(); return true;
                case Keys.F2: RenameRoomDialog(); return true;
                case Keys.F3: RenameLayerDialog(); return true;
                case Keys.Control | Keys.N: NewRoomDialog(true); return true;
                case Keys.Control | Keys.Shift | Keys.N: NewRoomDialog(false); return true;
                case Keys.Control | Keys.Shift | Keys.D: DuplicateRoomQuick(); return true;
                case Keys.Control | Keys.Shift | Keys.L: AddLayerAction(false); return true;
                case Keys.Control | Keys.Shift | Keys.T: AddLayerAction(true); return true;
                case Keys.Control | Keys.Shift | Keys.Up: MoveLayerAction(-1); return true;
                case Keys.Control | Keys.Shift | Keys.Down: MoveLayerAction(1); return true;
                case Keys.Escape: _doc.ClearSelection(); return true;
            }
        }
        if (ActiveControl == _canvas && _doc != null)
        {
            switch (keyData)
            {
                case Keys.V: SetTool(EditorTool.Select); return true;
                case Keys.H: SetTool(EditorTool.Pan); return true;
                case Keys.B: SetTool(EditorTool.Place); return true;
                case Keys.X: SetTool(EditorTool.Erase); return true;
                case Keys.T: SetTool(EditorTool.Tile); return true;
                case Keys.R: SetTool(EditorTool.TileRect); return true;
                case Keys.L: SetTool(EditorTool.TileLine); return true;
                case Keys.C: SetTool(EditorTool.TileCircle); return true;
                case Keys.E: SetTool(EditorTool.TileEllipse); return true;
                case Keys.K: SetTool(EditorTool.TileFill); return true;
                case Keys.S: SetTool(EditorTool.TileSelect); return true;
                case Keys.F: _canvas.FitToView(); return true;
                case Keys.G:
                    if (_gridItem != null)
                    {
                        _gridItem.Checked = !_gridItem.Checked;
                        _canvas.ShowGrid = _gridItem.Checked;
                        _canvas.Invalidate();
                    }
                    return true;
                case Keys.F5: _canvas.RefreshRoom(); return true;
                case Keys.Left: Nudge((ModifierKeys & Keys.Shift) != 0 ? -8 : -1, 0); return true;
                case Keys.Right: Nudge((ModifierKeys & Keys.Shift) != 0 ? 8 : 1, 0); return true;
                case Keys.Up: Nudge(0, (ModifierKeys & Keys.Shift) != 0 ? -8 : -1); return true;
                case Keys.Down: Nudge(0, (ModifierKeys & Keys.Shift) != 0 ? 8 : 1); return true;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space)
        {
            _canvas.SpaceHeld = true;
            if (ActiveControl == _canvas) Cursor = Cursors.Hand;
        }
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space) _canvas.SpaceHeld = false;
        base.OnKeyUp(e);
    }

    // ---------------------------------------------------------------- battle binding

    /// <summary>编辑（或新建）当前单选实例绑定的战斗。</summary>
    private void EditSelectedBattle()
    {
        if (_doc == null) return;
        if (_battles == null)
        {
            MessageBox.Show(this, "战斗配置尚未加载。", "战斗", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_doc.Selection.Count != 1)
        {
            MessageBox.Show(this, "请先在地图上选中一个实体（实例），再编辑它的战斗。", "战斗",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var inst = _doc.Selection[0];
        var objectName = GameData.Name(inst.ObjectDefinition?.Name);
        var roomName = _doc.RoomName();
        var existed = _battles.ForInstance(roomName, inst.InstanceID) != null;
        var battle = _battles.GetOrCreateForInstance(roomName, inst.InstanceID, objectName);
        battle.InstanceX = inst.X;
        battle.InstanceY = inst.Y;
        using (var dlg = new BattleEditDialog(_doc.Game, battle, roomName, inst.InstanceID, objectName))
        {
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _battles.Normalize();
                SaveBattles(false);
                _statusLeft.Text = "已保存战斗 " + battle.Id + "；生成 mod 请用「战斗 → 生成战斗 mod…」";
                RebuildInspector();
            }
            else if (!existed)
            {
                _battles.RemoveBattle(roomName, inst.InstanceID);
            }
        }
    }

    private void SaveBattles(bool notify)
    {
        if (_battles == null || _doc == null) return;
        var sidecar = BattleProject.SidecarFor(_doc.FilePath);
        var result = _battles.Save(sidecar, false, FindGameRoot());
        if (!result.Ok)
        {
            _statusLeft.Text = "战斗配置未保存：" + (result.Error ?? "原版基线只读");
            if (notify)
                MessageBox.Show(this, result.Error ?? "战斗配置未保存。", "原版基线 · 只读",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (notify) _statusLeft.Text = "已保存战斗配置 " + sidecar;
    }

    /// <summary>列出全部战斗；双击跳到绑定房间并进入编辑。</summary>
    private void ShowBattleList()
    {
        if (_battles == null) return;
        using var dlg = new Form
        {
            Text = "全部战斗",
            Size = new Size(560, 520),
            StartPosition = FormStartPosition.CenterParent,
            BackColor = Theme.Bg0,
            ForeColor = Theme.Text,
            Font = Theme.UI(9f),
        };
        var list = new ListBox { Dock = DockStyle.Fill, BackColor = Theme.Bg0, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, IntegralHeight = false };
        foreach (var b in _battles.File.Battles)
        {
            var where = string.IsNullOrWhiteSpace(b.Room) ? "未绑定" : b.Room + " #" + b.InstanceId;
            list.Items.Add(b.Id + "  [" + b.Kind + "]  " + where + "  ·  " + b.Turns.Count + " 回合 / " + b.Enemies.Count + " 敌人");
        }
        dlg.Controls.Add(list);
        var ok = new Button { Text = "编辑", Dock = DockStyle.Bottom, Height = 32 };
        Theme.StyleButton(ok);
        ok.Click += (s, e) => { dlg.DialogResult = DialogResult.OK; };
        dlg.Controls.Add(ok);
        dlg.AcceptButton = ok;
        if (dlg.ShowDialog(this) != DialogResult.OK || list.SelectedIndex < 0) return;
        var chosen = _battles.File.Battles[list.SelectedIndex];
        if (!string.IsNullOrWhiteSpace(chosen.Room)) OpenRoomByName(chosen.Room);
        var inst = _doc?.Room == null ? null : RoomEdit.AllInstances(_doc.Room)
            .Select(t => t.Inst)
            .FirstOrDefault(i => chosen.InstanceId != 0 && i.InstanceID == chosen.InstanceId);
        if (inst != null)
        {
            _doc!.ClearSelection();
            _doc.SelectOnly(inst);
            RebuildInspector();
        }
        EditSelectedBattle();
    }

    /// <summary>编辑（或新建）当前单选实例绑定的纯对话。</summary>
    private void EditSelectedDialogue()
    {
        if (_doc == null) return;
        if (_battles == null)
        {
            MessageBox.Show(this, "战斗/对话配置尚未加载。", "对话", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_doc.Selection.Count != 1)
        {
            MessageBox.Show(this, "请先在地图上选中一个实体（实例），再编辑它的对话。", "对话",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var inst = _doc.Selection[0];
        var objectName = GameData.Name(inst.ObjectDefinition?.Name);
        var roomName = _doc.RoomName();
        var existed = _battles.DialogueForInstance(roomName, inst.InstanceID) != null;
        var dialogue = _battles.GetOrCreateDialogueForInstance(roomName, inst.InstanceID, objectName);
        dialogue.InstanceX = inst.X;
        dialogue.InstanceY = inst.Y;
        using (var dlg = new DialogueEditDialog(dialogue, roomName, inst.InstanceID, objectName))
        {
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                dlg.SaveToDialogue();
                _battles.Normalize();
                SaveBattles(false);
                _statusLeft.Text = "已保存对话 " + dialogue.Id + "；生成 mod 请用「战斗 → 生成战斗 mod…」";
                RebuildInspector();
            }
            else if (!existed)
            {
                _battles.RemoveDialogue(roomName, inst.InstanceID);
            }
        }
    }

    /// <summary>列出全部纯对话；双击跳到绑定房间并进入编辑。</summary>
    private void ShowDialogueList()
    {
        if (_battles == null) return;
        using var dlg = new Form
        {
            Text = "全部对话",
            Size = new Size(560, 520),
            StartPosition = FormStartPosition.CenterParent,
            BackColor = Theme.Bg0,
            ForeColor = Theme.Text,
            Font = Theme.UI(9f),
        };
        var list = new ListBox { Dock = DockStyle.Fill, BackColor = Theme.Bg0, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, IntegralHeight = false };
        foreach (var d in _battles.File.Dialogues)
        {
            var where = string.IsNullOrWhiteSpace(d.Room) ? "未绑定" : d.Room + " #" + d.InstanceId;
            list.Items.Add(d.Id + "  " + (d.Enabled ? "" : "[禁用] ") + where + "  ·  " + d.Lines.Count + " 行");
        }
        dlg.Controls.Add(list);
        var ok = new Button { Text = "编辑", Dock = DockStyle.Bottom, Height = 32 };
        Theme.StyleButton(ok);
        ok.Click += (s, e) => { dlg.DialogResult = DialogResult.OK; };
        dlg.Controls.Add(ok);
        dlg.AcceptButton = ok;
        if (dlg.ShowDialog(this) != DialogResult.OK || list.SelectedIndex < 0) return;
        var chosen = _battles.File.Dialogues[list.SelectedIndex];
        if (!string.IsNullOrWhiteSpace(chosen.Room)) OpenRoomByName(chosen.Room);
        var inst = _doc?.Room == null ? null : RoomEdit.AllInstances(_doc.Room)
            .Select(t => t.Inst)
            .FirstOrDefault(i => chosen.InstanceId != 0 && i.InstanceID == chosen.InstanceId);
        if (inst != null)
        {
            _doc!.ClearSelection();
            _doc.SelectOnly(inst);
            RebuildInspector();
        }
        EditSelectedDialogue();
    }

    /// <summary>编辑（或新建）当前单选实例绑定的简单剧情。</summary>
    private void EditSelectedCutscene()
    {
        if (_doc == null) return;
        if (_battles == null)
        {
            MessageBox.Show(this, "战斗/对话/剧情配置尚未加载。", "剧情", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_doc.Selection.Count != 1)
        {
            MessageBox.Show(this, "请先在地图上选中一个实体（实例），再编辑它的剧情。", "剧情",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var inst = _doc.Selection[0];
        var objectName = GameData.Name(inst.ObjectDefinition?.Name);
        var roomName = _doc.RoomName();
        var existed = _battles.CutsceneForInstance(roomName, inst.InstanceID) != null;
        var cutscene = _battles.GetOrCreateCutsceneForInstance(roomName, inst.InstanceID, objectName);
        cutscene.InstanceX = inst.X;
        cutscene.InstanceY = inst.Y;
        using (var dlg = new CutsceneEditDialog(cutscene, roomName, inst.InstanceID, objectName))
        {
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                dlg.SaveToCutscene();
                _battles.Normalize();
                SaveBattles(false);
                _statusLeft.Text = "已保存剧情 " + cutscene.Id + "（" + cutscene.Steps.Count + " 步）；生成 mod 请用「战斗 → 生成战斗 mod…」";
                RebuildInspector();
            }
            else if (!existed)
            {
                _battles.RemoveCutscene(roomName, inst.InstanceID);
            }
        }
    }

    /// <summary>列出全部剧情；双击跳到绑定房间并进入编辑。</summary>
    private void ShowCutsceneList()
    {
        if (_battles == null) return;
        using var dlg = new Form
        {
            Text = "全部剧情",
            Size = new Size(620, 520),
            StartPosition = FormStartPosition.CenterParent,
            BackColor = Theme.Bg0,
            ForeColor = Theme.Text,
            Font = Theme.UI(9f),
        };
        var list = new ListBox { Dock = DockStyle.Fill, BackColor = Theme.Bg0, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, IntegralHeight = false };
        foreach (var c in _battles.File.Cutscenes)
        {
            var where = string.IsNullOrWhiteSpace(c.Room) ? "未绑定" : c.Room + " #" + c.InstanceId;
            list.Items.Add(c.Id + "  " + (c.Enabled ? "" : "[禁用] ") + "[" + c.Mode + "] " + where + "  ·  " + c.Steps.Count + " 步");
        }
        dlg.Controls.Add(list);
        var ok = new Button { Text = "编辑", Dock = DockStyle.Bottom, Height = 32 };
        Theme.StyleButton(ok);
        ok.Click += (s, e) => { dlg.DialogResult = DialogResult.OK; };
        dlg.Controls.Add(ok);
        dlg.AcceptButton = ok;
        if (dlg.ShowDialog(this) != DialogResult.OK || list.SelectedIndex < 0) return;
        var chosen = _battles.File.Cutscenes[list.SelectedIndex];
        if (!string.IsNullOrWhiteSpace(chosen.Room)) OpenRoomByName(chosen.Room);
        var inst = _doc?.Room == null ? null : RoomEdit.AllInstances(_doc.Room)
            .Select(t => t.Inst)
            .FirstOrDefault(i => chosen.InstanceId != 0 && i.InstanceID == chosen.InstanceId);
        if (inst != null)
        {
            _doc!.ClearSelection();
            _doc.SelectOnly(inst);
            RebuildInspector();
        }
        EditSelectedCutscene();
    }

    /// <summary>编辑 Mod 设置项（游戏内 Neutraled「Mod 设置」面板的项）。</summary>
    private void EditSettings()
    {
        if (_battles == null)
        {
            MessageBox.Show(this, "战斗/对话配置尚未加载。", "Mod 设置项", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var dlg = new SettingsEditDialog(_battles.File.Settings);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _battles.File.Settings = dlg.Result;
        _battles.Normalize();
        SaveBattles(false);
        _statusLeft.Text = "已保存 " + _battles.File.Settings.Count + " 个 Mod 设置项" +
            (_battles.File.Settings.Any(s => s.Feature != "none")
                ? "，切换后实时影响战斗/对话/弹幕伤害"
                : "");
        RebuildInspector();
    }

    /// <summary>把配置生成成可部署的 Neutraled mod（mods/ 目录下）。</summary>
    private void GenerateBattleMod()
    {
        if (_battles == null)
        {
            MessageBox.Show(this, "战斗配置尚未加载。", "生成战斗 mod", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_battles.File.Battles.Count == 0 && _battles.File.Dialogues.Count == 0 && _battles.File.Cutscenes.Count == 0)
        {
            MessageBox.Show(this, "还没有任何战斗、对话或剧情配置。先在地图上选实体并新建。", "生成战斗 mod",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var issues = _battles.Validate(_doc?.Game);
        var errors = issues.Where(i => i.IsError).ToList();
        if (errors.Count > 0)
        {
            MessageBox.Show(this, "战斗配置有错误，先修好：\n\n" + string.Join("\n", errors.Select(i => "· " + i.Message)),
                "生成战斗 mod", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var leaf = PromptLeaf(_battles.File.Mod.Leaf);
        if (leaf == null) return; // 取消生成
        _battles.File.Mod.Leaf = leaf;
        var modsDir = Path.Combine(GameRoot(), "Neutraled", "mods");
        try
        {
            SaveBattles(false);
            var r = BattleGenerator.Generate(_battles, modsDir, _doc?.Game);
            var warns = issues.Where(i => !i.IsError).ToList();
            var msg = "已生成 mod：\n" + r.ModDir + "\n\nGML " + r.GmlFiles + " 个 / 对象事件 " + r.ObjectFiles + " 个 / 数据 " + r.DataFiles + " 个"
                + "\n战斗 " + _battles.File.Battles.Count + " 场 / 对话 " + _battles.File.Dialogues.Count + " 段 / 剧情 "
                + _battles.File.Cutscenes.Count + " 段 / 设置项 " + _battles.File.Settings.Count + " 个";
            if (warns.Count > 0) msg += "\n\n警告：\n" + string.Join("\n", warns.Select(i => "· " + i.Message));
            _statusLeft.Text = "已生成战斗 mod " + r.ModDir;
            MessageBox.Show(this, msg, "生成战斗 mod", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "生成失败：" + ex.Message, "生成战斗 mod", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>生成前选部署叶子（root / chapter1..5）；取消返回 null。</summary>
    private string? PromptLeaf(string current)
    {
        using var dlg = new Form
        {
            Text = "生成战斗 mod — 选择部署叶子",
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(360, 132),
            BackColor = Theme.Bg1,
            ForeColor = Theme.Text,
            Font = Theme.UI(9f),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
        };
        var label = new Label
        {
            Text = "叶子名决定这个 mod 进哪个产物（mods/<id>/<author>/<leaf>/）。\n弹幕 type 必须按该章节填。",
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 50,
            Padding = new Padding(12, 8, 12, 0),
        };
        Theme.StyleLabel(label, Theme.TextDim);
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Left = 12,
            Top = 58,
            Width = 200,
            BackColor = Theme.Bg2,
            ForeColor = Theme.Text,
        };
        combo.Items.AddRange(new object[] { "root", "chapter1", "chapter2", "chapter3", "chapter4", "chapter5" });
        combo.SelectedItem = combo.Items.Contains(current) ? current : "chapter1";
        var ok = new Button { Text = "生成", DialogResult = DialogResult.OK, Left = 152, Top = 92, Width = 90, Height = 28 };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Left = 248, Top = 92, Width = 90, Height = 28 };
        Theme.StyleButton(ok, true);
        Theme.StyleButton(cancel);
        dlg.Controls.Add(label);
        dlg.Controls.Add(combo);
        dlg.Controls.Add(ok);
        dlg.Controls.Add(cancel);
        dlg.AcceptButton = ok;
        dlg.CancelButton = cancel;
        return dlg.ShowDialog(this) == DialogResult.OK ? combo.SelectedItem?.ToString() ?? "chapter1" : null;
    }

    private string GameRoot()
    {
        var found = FindGameRoot();
        if (found != null) return found;
        if (_doc != null)
        {
            var dir = Path.GetDirectoryName(_doc.FilePath);
            if (!string.IsNullOrWhiteSpace(dir)) return dir!;
        }
        return AppContext.BaseDirectory;
    }

    private void ShowModsManifest()
    {
        using var dlg = new ModsManifestDialog(Path.Combine(GameRoot(), "Neutraled", "mods.json"));
        if (_doc != null) dlg.ScanModsDir(Path.Combine(GameRoot(), "Neutraled", "mods"));
        dlg.ShowDialog(this);
    }

    private void OpenRoomByName(string name)
    {
        var row = _roomRows.FirstOrDefault(r => r.Name == name);
        if (row == null) return;
        _roomList.SelectedItem = row;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _doc?.Dispose();
            foreach (var b in _thumbs.Values) b?.Dispose();
            _thumbs.Clear();
        }
        base.Dispose(disposing);
    }
}
