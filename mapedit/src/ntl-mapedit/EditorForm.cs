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
    private readonly Label _statusTool = new();
    /// <summary>状态栏常驻区：当前房间 / 尺寸 / 实例数。操作提示写在 _statusLeft，不会把这条覆盖掉。</summary>
    private readonly Label _statusRoom = new();
    /// <summary>状态栏左栏的完整文本（含 sidecar / mod / data.win 绝对路径）：窄窗口下会截断，悬停可见。</summary>
    private readonly ToolTip _statusTip = new() { AutoPopDelay = 30000, InitialDelay = 350, ReshowDelay = 100 };
    private readonly Button _pickTileSourceBtn = new();
    private readonly Button _paletteBtn = new();
    private TilePaletteDialog? _palette;
    private ContextMenuStrip? _canvasMenu;
    /// <summary>与当前 data.win 同目录的战斗配置（&lt;win&gt;.ntlbattle.json）。</summary>
    private BattleProject? _battles;
    /// <summary>算地图/房间差分的基线 data.win：原版基线用原文件本身；其它目标用打开时的快照（多次保存都相对原版累计差分）。</summary>
    private string? _diffBaseWin;
    /// <summary>最近一次算出的房间差分（保存/生成时复用，避免重复读 data.win）。</summary>
    private RoomDiffResult? _lastRoomDiff;
    /// <summary>差分基线（打开时的原版 data.win）加载出来的游戏数据：生成前校验「新增房间」要拿它比对——</summary>
    /// <remarks>不能拿当前编辑中的数据比：新增房间本来就存在于内存里，比出来永远是「已有同名房间」= 永远生成失败。</remarks>
    private GameData? _baselineGame;
    /// <summary>最近一次自动生成 mod 失败的原因（校验错误明细等）；导出/生成时用来给用户看真原因。</summary>
    private string? _lastGenerateError;
    /// <summary>当前 data.win 的原版代码扫描器（懒反编译，只读）。</summary>
    private VanillaCode? _vanilla;
    private readonly Dictionary<string, VanillaObjectInfo> _vanillaCache = new();
    /// <summary>无参数/双击启动时的来源说明（显示在状态栏）。</summary>
    private string _startupNote = "";

    private readonly List<UndertaleGameObject> _objects = new();
    private readonly Dictionary<UndertaleGameObject, Bitmap?> _thumbs = new();
    private readonly List<RoomRow> _roomRows = new();
    /// <summary>上次真正打开的房间里对应的 _roomRows 下标（用于跳过流程分组标题行）。</summary>
    private int _lastRoomIndex = -1;
    private readonly List<LayerRow> _layerRows = new();
    private int _pasteSerial;
    private readonly List<ObjectRow> _objRows = new();
    private readonly List<TileRow> _tileRows = new();

    private readonly List<Button> _toolButtons = new();
    private ToolStripMenuItem? _gridItem;
    private ToolStripMenuItem? _snapItem;
    private ToolStripMenuItem? _helperItem;
    private ToolStripMenuItem? _flowItem;
    private ToolStripMenuItem? _autoGenItem;
    /// <summary>启动/行为设置（房间流程排序、保存时自动生成 mod…）。</summary>
    private readonly StartupSettings _settings = StartupSettings.Load();
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
        /// <summary>流程排序时的分组标题行（不是房间；选中它不切房间）。</summary>
        public bool IsHeader;
        /// <summary>支线缩进层级（0=主线，1=支线）。</summary>
        public int Depth;
        /// <summary>流程序号（主线 1..N；支线跟随前置主线）。</summary>
        public int Order;
        /// <summary>"支线" / "分组" / "已摘除" 等角标。</summary>
        public string Tag = "";
        /// <summary>已从 RoomOrder（玩家流程顺序）摘除（基线编辑时表达「删除房间」，保存进 mod 的 removeFromOrder）。</summary>
        public bool RemovedFromOrder;
        public override string ToString() => Name;
    }

    private sealed class LayerRow
    {
        public UndertaleRoom.Layer Layer = null!;
        public string Name = "";
        public string Detail = "";
        public override string ToString() => Name;
    }

    private sealed class ObjectRow
    {
        public UndertaleGameObject Obj = null!;
        public string Name = "";
        public string Sprite = "";
        public override string ToString() => Name;
    }

    private sealed class TileRow
    {
        public UndertaleRoom.Tile Tile = null!;
        public string Name = "";
        public string Detail = "";
        public Bitmap? Thumb;
        public override string ToString() => Name;
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
        _canvas.ZoomChanged += () =>
        {
            if (_zoomLabel != null) _zoomLabel.Text = $"{_canvas.Zoom * 100:0}%";
            _statusZoom.Text = $"{_canvas.Zoom * 100:0}%";
        };
        _canvas.Edited += () => { RebuildLayers(); RebuildInspector(); UpdateTitle(); UpdateUndoState(); };
        _canvas.Hint += msg => _statusLeft.Text = msg;
        _canvas.ContextRequested += (inst, screen) =>
        {
            _canvasMenu ??= BuildCanvasContextMenu();
            _canvasMenu.Show(_canvas, _canvas.PointToClient(screen));
        };

        WireFileDrop(this);
        FormClosing += OnEditorFormClosing;

        UpdateToolStatus();
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
        file.DropDownItems.Add(BuildDefaultChapterMenu());
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
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add(Theme.Item("编辑本房间原版对话（文本覆盖）…", (s, e) => EditVanillaDialogue(), Keys.Control | Keys.Shift | Keys.E));

        var room = TopMenu("房间(&R)");
        room.DropDownItems.Add(Theme.Item("新建房间（复制当前）…", (s, e) => NewRoomDialog(true), Keys.Control | Keys.N));
        room.DropDownItems.Add(Theme.Item("新建空白房间…", (s, e) => NewRoomDialog(false), Keys.Control | Keys.Shift | Keys.N));
        room.DropDownItems.Add(Theme.Item("复制当前房间", (s, e) => DuplicateRoomQuick(), Keys.Control | Keys.Shift | Keys.D));
        room.DropDownItems.Add(new ToolStripSeparator());
        room.DropDownItems.Add(Theme.Item("重命名房间…", (s, e) => RenameRoomDialog(), Keys.F2));
        room.DropDownItems.Add(Theme.Item("删除房间", (s, e) => DeleteRoomAction(), Keys.Control | Keys.Delete));
        room.DropDownItems.Add(Theme.Item("恢复房间到流程顺序", (s, e) => RestoreRoomOrderAction()));
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
        _gridItem = Theme.Item("显示网格", (s, e) => { if (_gridItem != null) _canvas.ShowGrid = _gridItem.Checked; UpdateToolStatus(); });
        _gridItem.Checked = true;
        view.DropDownItems.Add(_gridItem);
        _snapItem = Theme.Item("吸附网格", (s, e) => { if (_snapItem != null) _canvas.SnapGrid = _snapItem.Checked; UpdateToolStatus(); });
        view.DropDownItems.Add(_snapItem);
        _helperItem = Theme.Item("显示辅助对象", (s, e) => { if (_helperItem != null) _canvas.ShowHelpers = _helperItem.Checked; UpdateToolStatus(); });
        _helperItem.Checked = true;
        view.DropDownItems.Add(_helperItem);
        view.DropDownItems.Add(new ToolStripSeparator());
        var rerenderItem = Theme.Item("重新渲染", (s, e) => _canvas.RefreshRoom());
        rerenderItem.ShortcutKeyDisplayString = "F5";
        view.DropDownItems.Add(rerenderItem);
        view.DropDownItems.Add(new ToolStripSeparator());
        _flowItem = Theme.Item("房间按剧情流程排序", (s, e) => ToggleFlowSort());
        _flowItem.Checked = _settings.SortRoomsByFlow;
        view.DropDownItems.Add(_flowItem);
        view.DropDownItems.Add(new ToolStripSeparator());
        view.DropDownItems.Add(Theme.Item("资源浏览器（只读）…", (s, e) => ShowResourceBrowser(), Keys.F4));

        var battleMenu = TopMenu("战斗(&B)");
        battleMenu.DropDownItems.Add(Theme.Item("编辑选中实体的战斗…", (s, e) => EditSelectedBattle(), Keys.Control | Keys.B));
        battleMenu.DropDownItems.Add(Theme.Item("编辑选中实体的对话…", (s, e) => EditSelectedDialogue(), Keys.Control | Keys.Shift | Keys.B));
        battleMenu.DropDownItems.Add(Theme.Item("编辑选中实体的剧情…", (s, e) => EditSelectedCutscene(), Keys.Control | Keys.Shift | Keys.K));
        battleMenu.DropDownItems.Add(Theme.Item("编辑本房间原版对话…", (s, e) => EditVanillaDialogue(), Keys.Control | Keys.Shift | Keys.E));
        battleMenu.DropDownItems.Add(new ToolStripSeparator());
        battleMenu.DropDownItems.Add(Theme.Item("全部战斗…", (s, e) => ShowBattleList()));
        battleMenu.DropDownItems.Add(Theme.Item("全部对话…", (s, e) => ShowDialogueList()));
        battleMenu.DropDownItems.Add(Theme.Item("全部剧情…", (s, e) => ShowCutsceneList()));
        battleMenu.DropDownItems.Add(new ToolStripSeparator());
        battleMenu.DropDownItems.Add(Theme.Item("Mod 设置项…", (s, e) => EditSettings()));
        battleMenu.DropDownItems.Add(new ToolStripSeparator());
        _autoGenItem = Theme.Item("保存时自动生成 Mod", (s, e) => ToggleAutoGenerate());
        _autoGenItem.Checked = _settings.AutoGenerateMod;
        battleMenu.DropDownItems.Add(_autoGenItem);
        battleMenu.DropDownItems.Add(Theme.Item("立即重新生成 Mod…", (s, e) => GenerateBattleMod()));
        battleMenu.DropDownItems.Add(Theme.Item("mods 清单（mods.json）…", (s, e) => ShowModsManifest()));
        battleMenu.DropDownItems.Add(new ToolStripSeparator());
        battleMenu.DropDownItems.Add(Theme.Item("保存战斗配置（仅写 sidecar）", (s, e) => SaveBattles(true)));
        battleMenu.DropDownItems.Add(Theme.Item("导出 .ntlmod…", (s, e) => ExportNtlmod()));
        battleMenu.DropDownItems.Add(Theme.Item("安装 .ntlmod 到 mods/…", (s, e) => InstallNtlmodFromFile()));

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

    private ToolStripMenuItem BuildDefaultChapterMenu()
    {
        var root = Theme.Item("默认启动章节（双击 exe 时打开）");
        var auto = Theme.Item("自动（最近打开，其次 chapter1）");
        auto.Click += (s, e) => SetDefaultChapter("auto");
        root.DropDownItems.Add(auto);
        for (int ch = 1; ch <= 5; ch++)
        {
            int n = ch;
            var item = Theme.Item("Chapter " + n);
            item.Click += (s, e) => SetDefaultChapter("chapter" + n);
            root.DropDownItems.Add(item);
        }
        root.DropDownItems.Add(new ToolStripSeparator());
        var note = Theme.Item("说明: 双击 exe / 无参数启动时打开该章节；也可用 ntl-mapedit default-chapter 设置");
        note.Enabled = true;
        root.DropDownItems.Add(note);
        root.DropDownOpening += (s, e) =>
        {
            var st = StartupSettings.Load();
            for (int i = 0; i < root.DropDownItems.Count; i++)
            {
                if (root.DropDownItems[i] is ToolStripMenuItem mi && mi != note)
                {
                    string want = i == 0 ? "auto" : "chapter" + i;
                    mi.Checked = st.DefaultChapter == want;
                }
            }
        };
        return root;
    }

    private void SetDefaultChapter(string chapter)
    {
        try
        {
            var st = StartupSettings.Load();
            st.DefaultChapter = StartupSettings.NormalizeChapter(chapter);
            st.Save();
            _statusLeft.Text = "默认启动章节已设为 " + StartupSettings.Describe(st.DefaultChapter) + "（下次双击 / 无参数启动生效；设置文件 " + StartupSettings.DefaultFile + "）";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "保存默认启动章节失败：\n" + ex.Message, "设置失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
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
        var toolTips = new ToolTip { AutoPopDelay = 15000, InitialDelay = 400 };
        for (int i = 0; i < tools.Length; i++)
        {
            // 工具变多后按钮要稍窄一点，否则 1520 宽下右半会被 FlowLayoutPanel 裁掉
            int w = i == tools.Length - 1 ? 96 : 78;
            var btn = new Button { Text = tools[i], Width = w, Height = 30, Margin = new Padding(0, 0, 4, 0) };
            Theme.StyleButton(btn);
            var tool = toolIds[i];
            // 右键擦除 / Alt 拾取笔刷只写在文档里，新人不一定发现（QA 体验 #9）→ 每个工具都给一条悬停说明。
            toolTips.SetToolTip(btn, ToolHint(tool));
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
        UpdateToolStatus();
    }

    private static string ToolDisplayName(EditorTool tool) => tool switch
    {
        EditorTool.Select => "选择 (V)",
        EditorTool.Pan => "平移 (H)",
        EditorTool.Place => "放置 (B)",
        EditorTool.Erase => "擦除 (X)",
        EditorTool.Tile => "瓦片 (T)",
        EditorTool.TileRect => "矩形 (R)",
        EditorTool.TileLine => "直线 (L)",
        EditorTool.TileCircle => "圆形 (C)",
        EditorTool.TileEllipse => "椭圆 (E)",
        EditorTool.TileFill => "油漆桶 (K)",
        EditorTool.TileSelect => "瓦片选择 (S)",
        _ => tool.ToString(),
    };

    /// <summary>工具按钮的悬停说明（快捷键 + 鼠标约定）。</summary>
    private static string ToolHint(EditorTool tool) => tool switch
    {
        EditorTool.Select => "选择 (V)：左键选中/拖动实例，框选多选，右键弹出快捷菜单（战斗/对话/剧情/文本覆盖）",
        EditorTool.Pan => "平移 (H)：左键拖动视图（滚轮缩放，也可用空格临时平移）",
        EditorTool.Place => "放置 (B)：左键在画布上放置选中的对象",
        EditorTool.Erase => "擦除 (X)：左键删除点中的实例",
        EditorTool.Tile => "瓦片 (T)：左键绘制 / 右键擦除 / Alt+左键拾取笔刷（先在本工具下选择图块来源）",
        EditorTool.TileRect => "矩形填瓦片 (R)：按住左键拖出矩形，右键擦除同一矩形范围",
        EditorTool.TileLine => "直线填瓦片 (L)：按住左键拖出直线，右键擦除",
        EditorTool.TileCircle => "圆形填瓦片 (C)：按住左键拖出圆形，右键擦除",
        EditorTool.TileEllipse => "椭圆填瓦片 (E)：按住左键拖出椭圆，右键擦除",
        EditorTool.TileFill => "油漆桶 (K)：左键把连通区域填成当前图块，右键擦除",
        EditorTool.TileSelect => "瓦片选择 (S)：左键框选一块瓦片区域当笔刷，右键擦除",
        _ => ToolDisplayName(tool),
    };

    /// <summary>状态栏右侧显示当前工具与显示开关（矩阵 MG-020/024/053..055 的可读状态来源）。</summary>
    private void UpdateToolStatus()
    {
        _statusTool.Text = $"工具：{ToolDisplayName(_canvas.Tool)} ｜ 网格 {(_canvas.ShowGrid ? "开" : "关")} ｜ 吸附 {(_canvas.SnapGrid ? "开" : "关")} ｜ 辅助 {(_canvas.ShowHelpers ? "开" : "关")}";
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
            if (_roomRows[i].IsHeader)
            {
                // 分组标题只是视觉分隔：选中它不切房间，直接回退到上一个真实房间。
                _syncingLists = true;
                _roomList.SelectedIndex = (_lastRoomIndex >= 0 && _lastRoomIndex < _roomRows.Count) ? _lastRoomIndex : -1;
                _syncingLists = false;
                return;
            }
            _lastRoomIndex = i;
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
        if (row.IsHeader)
        {
            // 分组标题：更深一点的底 + 粗体小号字，不可选中。
            using var hbg = new SolidBrush(Theme.Bg2);
            e.Graphics.FillRectangle(hbg, e.Bounds);
            TextRenderer.DrawText(e.Graphics, row.Name, SectionFont,
                new Rectangle(e.Bounds.X + 10, e.Bounds.Y, e.Bounds.Width - 20, e.Bounds.Height),
                Theme.TextDim, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            return;
        }
        using var bg = new SolidBrush(sel ? Theme.Bg3 : Theme.Bg1);
        e.Graphics.FillRectangle(bg, e.Bounds);
        if (sel)
        {
            using var accent = new SolidBrush(Theme.Accent);
            e.Graphics.FillRectangle(accent, e.Bounds.X, e.Bounds.Y + 2, 3, e.Bounds.Height - 4);
        }
        int textX = e.Bounds.X + 10 + row.Depth * 14;
        string prefix = row.Order > 0 ? row.Order.ToString("00") + "  " : "";
        // 右侧要给「支线」角标 + 尺寸留位，否则长房间名会压到角标上（实测 room_flowershop）。
        int rightReserve = row.Tag.Length > 0 ? 116 : 72;
        int nameWidth = Math.Max(20, e.Bounds.Right - rightReserve - textX);
        // 已摘除的房间在流程里已经不存在（门链会跳过），用灰字让它在列表里明显"退到后面"。
        Color nameColor = row.RemovedFromOrder ? Theme.TextDim : Theme.Text;
        TextRenderer.DrawText(e.Graphics, prefix + row.Name, RowFont,
            new Rectangle(textX, e.Bounds.Y, nameWidth, e.Bounds.Height),
            nameColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (row.Tag.Length > 0)
            TextRenderer.DrawText(e.Graphics, row.Tag, RowFontSmall,
                new Rectangle(e.Bounds.Right - 112, e.Bounds.Y, 42, e.Bounds.Height),
                row.RemovedFromOrder ? Theme.Red : Theme.Orange,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
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
        // 三个「为此实体新建…」按钮原来排在属性网格末尾，属性一多就被挤出 AutoScroll 视口（要滚动才能看见，
        // UIA 也会按滚动外坐标点到别处）。改成标题行右侧的常驻快捷按钮，选中实体后随时可点。
        var insTop = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0), BackColor = Theme.Bg1,
        };
        insTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        insTop.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        insTop.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var insQuick = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, AutoSize = false, BackColor = Theme.Bg1, Margin = new Padding(0),
            Padding = new Padding(0, 2, 6, 2),
        };
        foreach (var (label, tip, action) in new (string, string, Action)[]
        {
            ("战斗", "为选中实体新建战斗（也可在画布右键 → 编辑此实体的战斗…）", EditSelectedBattle),
            ("对话", "为选中实体新建纯对话（也可在画布右键 → 编辑此实体的对话…）", EditSelectedDialogue),
            ("剧情", "为选中实体新建剧情（也可在画布右键 → 编辑此实体的剧情…）", EditSelectedCutscene),
        })
        {
            var btn = new Button { Text = label, Width = 62, Height = 22, Margin = new Padding(2, 0, 2, 0) };
            Theme.StyleButton(btn);
            btn.Click += (s, e) => action();
            var tipRef = new ToolTip();
            tipRef.SetToolTip(btn, tip);
            insQuick.Controls.Add(btn);
        }
        insTop.Controls.Add(insHeader, 0, 0);
        insTop.Controls.Add(insQuick, 1, 0);
        panel.Controls.Add(insTop, 0, 0);

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
            _statusLeft.Text = $"放置对象：{_objRows[i].Name}（B 键回到放置工具）" + DescribeObjectVanilla(_objRows[i].Obj);
            _canvas.Invalidate();
        };
        var objMenu = new ContextMenuStrip { BackColor = Theme.Bg1, ForeColor = Theme.Text };
        var objVanillaItem = new ToolStripMenuItem("查看原版交互 / 事件…");
        objVanillaItem.Click += (s, e) => ShowObjectVanillaViewer(SelectedObjectRow()?.Obj);
        objMenu.Items.Add(objVanillaItem);
        _objList.ContextMenuStrip = objMenu;
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
        _statusZoom.Text = "100%";
        _statusZoom.TextAlign = ContentAlignment.MiddleRight;
        _statusZoom.ForeColor = Theme.TextDim;
        _statusZoom.Font = Theme.Mono(8.5f);
        _statusZoom.Padding = new Padding(0, 0, 10, 0);
        _statusTool.Dock = DockStyle.Right;
        _statusTool.Width = 330;
        _statusTool.TextAlign = ContentAlignment.MiddleRight;
        _statusTool.ForeColor = Theme.TextDim;
        _statusTool.Font = Theme.UI(8.5f);
        // 常驻「当前房间」条：原来房间信息写在 _statusLeft，任何操作提示（拖拽/放置/保存）都会把它冲掉，
        // 自动化与用户都无法据此判断当前在编辑哪个房间 → 拆成独立面板常驻显示。
        _statusRoom.Dock = DockStyle.Left;
        _statusRoom.Width = 330;
        _statusRoom.Text = "未打开文件";
        _statusRoom.TextAlign = ContentAlignment.MiddleLeft;
        _statusRoom.Padding = new Padding(10, 0, 0, 0);
        _statusRoom.ForeColor = Theme.Text;
        _statusRoom.Font = Theme.Mono(8.5f);
        bar.Controls.Add(_statusLeft);
        bar.Controls.Add(_statusRoom);
        bar.Controls.Add(_statusTool);
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

    /// <summary>整窗接受把 data.win / *.win 拖进来直接打开（矩阵 MG-006）。</summary>
    private void WireFileDrop(Control root)
    {
        root.AllowDrop = true;
        root.DragEnter += OnFileDragEnter;
        root.DragDrop += OnFileDragDrop;
        foreach (Control child in root.Controls) WireFileDrop(child);
    }

    private static bool IsDroppedWin(DragEventArgs e)
    {
        var data = e.Data;
        if (data is null || !data.GetDataPresent(DataFormats.FileDrop)) return false;
        if (data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return false;
        string first = files[0];
        return first.EndsWith(".win", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileName(first), "data.win", StringComparison.OrdinalIgnoreCase);
    }

    private void OnFileDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = IsDroppedWin(e) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnFileDragDrop(object? sender, DragEventArgs e)
    {
        if (!IsDroppedWin(e)) return;
        var data = e.Data;
        if (data is null) return;
        if (data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
        OpenPath(files[0], false);
        Activate();
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
            // 原版基线：sidecar 写到 Neutraled/editor/（不碰 backup），因此「打开原版 data 就能直接编辑对话/战斗/剧情」。
            var gameRootForSidecar = FindGameRoot();
            string sidecarPath = BattleProject.SidecarForEdit(path, gameRootForSidecar);
            bool sidecarFresh = !File.Exists(sidecarPath);
            _battles = BattleProject.LoadOrNew(sidecarPath);
            if (sidecarFresh) _battles.File.Mod.Leaf = BattleProject.ChapterLeaf(path);
            _vanilla = new VanillaCode(game.Data, path);
            _vanillaCache.Clear();
            _startupNote = "";
            _objects.Clear();
            _objects.AddRange(game.Data.GameObjects);
            _thumbs.Clear();
            // drop thumbnails of the previous document
            foreach (var b in _thumbs.Values) b?.Dispose();
            WireDocument();
            _canvas.Attach(_doc);
            _fileRole = FileRole.Classify(path, FindGameRoot());
            _warnedDeployed = false;
            // 原版 data 直接编辑：sidecar 里的地图/房间差分回灌到内存（继续编辑可见）；
            // 差分基线：原版基线用原文件本身，其它目标用打开时的快照。
            _lastRoomDiff = null;
            _baselineGame = null;
            _diffBaseWin = CaptureDiffBase(path, gameRootForSidecar);
            RestoreRoomPatchState(path, gameRootForSidecar);
            RebuildRoomList();
            RebuildObjectList();
            RebuildLayers();
            RebuildInspector();
            RebuildTileList();
            int firstRoom = _roomRows.FindIndex(r => !r.IsHeader);
            if (firstRoom >= 0)
            {
                _lastRoomIndex = firstRoom;
                _syncingLists = true;
                _roomList.SelectedIndex = firstRoom;
                _syncingLists = false;
                _doc.OpenRoom(_roomRows[firstRoom].Room);
                _canvas.FitToView();
            }
            UpdateTitle();
            UpdateUndoState();
            UpdateStatus();
            string loaded = $"已加载 {Path.GetFileName(path)} — {_roomRows.Count} 个房间 / {_objects.Count} 个对象";
            if (_startupNote.Length > 0) loaded += "  ·  " + _startupNote;
            if (_fileRole == FileKind.Baseline)
                loaded += "  ·  " + FileRole.BaselineBadge + "（data.win 不改；改动存 Neutraled/editor 并生成 mod）";
            else if (FileRole.IsLive(_fileRole))
                loaded += "  ·  " + FileRole.LiveBadge;
            // 单行放不下的细节（data.win 绝对路径）挂 ToolTip（QA 体验 #7）。
            SetStatusLine(loaded, "data.win: " + path + "\n" + loaded);
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
            bool searching = filter.Length > 0;
            var rooms = _doc.Game.Rooms.ToList();
            var entries = _settings.SortRoomsByFlow
                ? RoomFlow.Build(rooms)
                : RoomFlow.BuildDataOrder(rooms);
            foreach (var ent in entries)
            {
                if (ent.IsHeader)
                {
                    if (searching) continue; // 搜索时平铺，不显示分组标题
                    _roomRows.Add(new RoomRow { IsHeader = true, Name = ent.Name, SizeText = "", Tag = ent.Tag });
                    continue;
                }
                if (searching && !ent.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
                var room = ent.Room!;
                bool removed = _doc.IsRemovedFromOrder(room);
                _roomRows.Add(new RoomRow
                {
                    Room = room,
                    Name = ent.Name,
                    SizeText = $"{room.Width}×{room.Height}",
                    Depth = searching ? 0 : ent.Depth,
                    Order = searching ? 0 : ent.Order,
                    // 「已摘除」优先于流程角标：让用户一眼看出这间房不会出现在门链里。
                    Tag = searching ? "" : (removed ? "已摘除" : ent.Tag),
                    RemovedFromOrder = removed,
                });
            }
            _roomList.BeginUpdate();
            _roomList.Items.Clear();
            foreach (var r in _roomRows) _roomList.Items.Add(r);
            _roomList.EndUpdate();
            int idx = -1;
            if (_doc.Room != null) idx = _roomRows.FindIndex(r => ReferenceEquals(r.Room, _doc.Room));
            if (idx < 0) idx = _roomRows.FindIndex(r => !r.IsHeader);
            _lastRoomIndex = idx;
            _roomList.SelectedIndex = idx;
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
                string meta = (string.IsNullOrEmpty(sprite) ? "（无 sprite）" : sprite)
                    + " · 深度 " + obj.Depth
                    + (obj.Visible ? "" : " · 不可见")
                    + (obj.Solid ? " · solid" : "")
                    + (obj.Persistent ? " · persistent" : "");
                _objRows.Add(new ObjectRow { Obj = obj, Name = name, Sprite = meta });
                if (_objRows.Count >= 4000) break;   // 只是防御性上限；ch1..ch5 的对象总数都远低于此
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
            _statusRoom.Text = "未打开文件";
            _statusLeft.Text = "就绪 — 文件 → 打开 data.win";
            return;
        }
        int instances = RoomEdit.AllInstances(_doc.Room).Count();
        int tiles = _doc.Room.Layers?.Sum(l => (l.AssetsData?.LegacyTiles?.Count ?? 0) + NativeTiles.CountNonZero(l)) ?? 0;
        _statusRoom.Text = $"{_doc.RoomName()} · {_doc.Room.Width}×{_doc.Room.Height} · {instances} 实例 · {tiles} 图块" + (_doc.Dirty ? "  * 未保存" : "");
        // 启动说明/回灌提示只在打开流程写一次（OpenPath 的「已加载 … · 启动: …」、OnShown 的基线说明）。
        // 这里再写一次的话，保存后的「已保存编辑配置 …」会被启动说明冲掉（QA 体验：操作结果看不到）。
    }

    /// <summary>写状态栏左栏。窗口不宽时单行必然被截断，完整文本（含 sidecar / mod / data.win 绝对路径）挂 ToolTip。</summary>
    private void SetStatusLine(string text, string? full = null)
    {
        _statusLeft.Text = text;
        _statusTip.SetToolTip(_statusLeft, string.IsNullOrEmpty(full) ? text : full);
    }

    /// <summary>生成 mod 的状态栏后缀：只写 mod 目录名，完整路径挂 ToolTip（单行放不下必然截断）。</summary>
    private string ModStatusSuffix(GenerateResult gen)
    {
        _statusTip.SetToolTip(_statusLeft, "mod: " + gen.ModDir);
        return "；已生成 mod: " + Path.GetFileName(gen.ModDir.TrimEnd(Path.DirectorySeparatorChar));
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
            AddVanillaSection(inst);
            AddSection("Mod 战斗（可编辑）");
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
            AddSection("Mod 剧情（可编辑）");
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
            AddSection("Mod 对话（可编辑）");
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

        AddSection("原版房间（只读）");
        if (_vanilla == null)
        {
            AddReadonly("状态", "（未加载）");
        }
        else
        {
            int pathLayers = room.Layers?.Count(l => l.LayerType is UndertaleRoom.LayerType.Path or UndertaleRoom.LayerType.Path2) ?? 0;
            int effectLayers = room.Layers?.Count(l => l.LayerType == UndertaleRoom.LayerType.Effect) ?? 0;
            AddReadonly("原版属性", "speed=" + room.Speed + " · persistent=" + (room.Persistent ? "是" : "否") + " · flags=" + room.Flags
                + " · views=" + (room.Views?.Count ?? 0) + " · 旧版背景=" + (room.Backgrounds?.Count ?? 0)
                + " · 创建顺序=" + (room.InstanceCreationOrderIDs?.InstanceIDs?.Count ?? 0)
                + " · 路径层=" + pathLayers + " · 效果层=" + effectLayers);
            var roomCode = room.CreationCodeId;
            AddReadonly("房间创建代码", roomCode == null ? "（无）" : "有（点下方按钮反编译查看）");
            if (roomCode != null)
            {
                var codeRef = roomCode;
                AddButtonRow("", "查看房间创建代码…", () => VanillaCodeDialog.ShowCode(this, "房间创建代码（只读） — " + _doc.RoomName(), _vanilla.Decompile(codeRef) ?? "（反编译不可用）"));
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

    private string DescribeObjectVanilla(UndertaleGameObject obj)
    {
        if (_vanilla == null || _doc?.Room == null) return "";
        var hit = RoomEdit.AllInstances(_doc.Room).FirstOrDefault(x => ReferenceEquals(x.Inst.ObjectDefinition, obj));
        var inst = hit.Inst;
        float instX = inst?.X ?? float.NaN, instY = inst?.Y ?? float.NaN;
        string key = "obj:" + GameData.Name(obj.Name) + "@" + _doc.RoomName() + "@" + instX + "," + instY;
        if (!_vanillaCache.TryGetValue(key, out var info))
        {
            try
            {
                int ri = _vanilla.RoomIndex(_doc.Room);
                info = _vanilla.ScanObject(obj, ri, instX, instY);
            }
            catch
            {
                return "  ·  原版：扫描失败";
            }
            _vanillaCache[key] = info;
        }
        int reach = info.Events.SelectMany(e => e.Lines).Count(l => l.Truth != TriTruth.False);
        var all = info.Events.SelectMany(e => e.Lines).ToList();
        int mscCount = all.Count(l => l.MscNo > 0);
        int argCount = all.Count(l => l.RequiresArgs);
        bool truncated = info.Events.Any(e => e.Truncated);
        string inter;
        if (info.InteractionEvent == null) inter = "无 Other_10";
        else if (info.InteractionEvent.Inherited) inter = "交互 " + info.InteractionEvent.Name + "（继承自 " + info.InteractionEvent.OwnerObject + "）";
        else inter = "交互 " + info.InteractionEvent.Name;
        string inh = info.InheritedDialogueLineCount > 0 ? "，其中继承 " + info.InheritedDialogueLineCount + " 行" : "";
        string extra = (mscCount > 0 ? "，msc " + mscCount + " 行" : "") + (argCount > 0 ? "，带参数 " + argCount + " 行" : "")
                     + (truncated ? "，已截断" : "");
        string props = " · 对象: 深度 " + obj.Depth + " · visible " + (obj.Visible ? "是" : "否") + " · solid " + (obj.Solid ? "是" : "否")
                     + " · persistent " + (obj.Persistent ? "是" : "否") + (obj.ParentId != null ? " · 父 " + GameData.Name(obj.ParentId.Name) : "");
        return $"  ·  原版：{inter}，对白 {info.DialogueLineCount} 行（可阅读 {info.DisplayableLineCount} 行；当前房间/坐标可达 {reach}{inh}{extra}）{props}";
    }

    private ObjectRow? SelectedObjectRow()
    {
        int i = _objList.SelectedIndex;
        return i >= 0 && i < _objRows.Count ? _objRows[i] : null;
    }

    private void ShowObjectVanillaViewer(UndertaleGameObject? obj)
    {
        if (obj == null || _vanilla == null || _doc?.Room == null) return;
        string objName = GameData.Name(obj.Name);
        string key = "objview:" + objName + "@" + _doc.RoomName();
        if (!_vanillaCache.TryGetValue(key, out var info))
        {
            try { info = _vanilla.ScanObject(obj, _vanilla.RoomIndex(_doc.Room), float.NaN, float.NaN); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "扫描该对象的原版事件失败：\n" + ex.Message, "扫描失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            _vanillaCache[key] = info;
        }
        try
        {
            using var dlg = new VanillaCodeDialog(this, _vanilla, obj, _vanilla.RoomIndex(_doc.Room), float.NaN, float.NaN, info, null, null);
            dlg.ShowDialog(this);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "打开原版事件查看器失败：\n" + ex.Message, "查看失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private VanillaObjectInfo? GetVanillaInfo(UndertaleRoom.GameObject inst)
    {
        if (_vanilla == null || _doc?.Room == null || inst.ObjectDefinition == null) return null;
        // 坐标进 key：可达对白行是按当前坐标判定条件算的，实例一移动缓存就得失效。
        string key = _doc.RoomName() + "#" + inst.InstanceID + "@" + inst.X + "," + inst.Y;
        if (_vanillaCache.TryGetValue(key, out var cached)) return cached;
        try
        {
            var info = _vanilla.ScanObject(inst.ObjectDefinition, _vanilla.RoomIndex(_doc.Room), inst.X, inst.Y);
            _vanillaCache[key] = info;
            return info;
        }
        catch
        {
            _vanillaCache[key] = new VanillaObjectInfo { ObjectName = GameData.Name(inst.ObjectDefinition.Name), Note = "扫描失败" };
            return _vanillaCache[key];
        }
    }

    private void AddVanillaSection(UndertaleRoom.GameObject inst)
    {
        AddSection("原版交互（只读）");
        if (_vanilla == null)
        {
            AddReadonly("状态", "（未加载）");
            return;
        }
        var info = GetVanillaInfo(inst);
        if (info == null)
        {
            AddReadonly("状态", "（该实例没有对象定义）");
            return;
        }
        var inter = info.InteractionEvent;
        string interText;
        if (inter == null) interText = "（未发现 Other_10）";
        else if (inter.Inherited) interText = inter.Friendly + " · 继承自 " + inter.OwnerObject;
        else interText = inter.Friendly;
        AddReadonly("交互事件", interText);
        if (info.ParentChain.Count > 0) AddReadonly("父对象", string.Join(" → ", info.ParentChain));
        var allLines = info.Events.SelectMany(e => e.Lines).ToList();
        int reachable = allLines.Count(l => l.Truth != TriTruth.False);
        int mscTotal = allLines.Count(l => l.MscNo > 0);
        int argTotal = allLines.Count(l => l.RequiresArgs);
        int unknownTotal = allLines.Count(l => l.Truth == TriTruth.Unknown);
        bool truncated = info.Events.Any(e => e.Truncated);
        string inhNote = info.InheritedDialogueLineCount > 0 ? "；其中继承 " + info.InheritedDialogueLineCount + " 行" : "";
        AddReadonly("对白行", info.DialogueLineCount + " 行（可阅读 " + info.DisplayableLineCount + " 行；运行期文本/控制码 " + info.RuntimeTextLineCount + " 行；当前房间/坐标可达 " + reachable + " 行" + inhNote + "）"
            + (truncated ? "  ⚠ 超过 " + VanillaCode.MaxLines + " 行已截断" : ""));
        AddReadonly("文本统计", "msc 旧式文本 " + mscTotal + " 行 · 含参数替换 " + argTotal + " 行 · 未知条件 " + unknownTotal + " 行");
        AddReadonly("文本来源", _vanilla.LangPath == null
            ? "未找到 lang 文件（只显示 key）：" + _vanilla.LangStatus
            : "lang: " + _vanilla.LangPath + "（" + _vanilla.LangEntryCount + " 条）");
        if (!string.IsNullOrEmpty(info.Note)) AddHint(info.Note);
        var previews = allLines
            .Where(l => l.Truth != TriTruth.False && !l.RuntimeText && !string.IsNullOrWhiteSpace(l.DisplayText))
            .Take(3).Select(l => "· " + (l.MscNo > 0 ? "[msc " + l.MscNo + "] " : "") + (l.RequiresArgs ? "[参数] " : "") + ShortOneLine(l.DisplayText, 46)).ToList();
        if (previews.Count > 0) AddHint("当前条件下的对白预览（点下方按钮看全部）：\n" + string.Join("\n", previews));
        else if (info.DialogueLineCount == 0) AddHint("没有解析到对白：交互事件可能只有非文本逻辑，或文本走未识别的函数（见下方「原版事件…」里的完整反编译代码）。");
        AddButtonRow3("查看原版对话…", () => ShowVanillaViewer(inst, info),
                     "原版事件…", () => ShowVanillaViewer(inst, info),
                     "创建代码…", () => ShowInstanceCode(inst));
    }

    private void ShowVanillaViewer(UndertaleRoom.GameObject inst, VanillaObjectInfo info)
    {
        if (_vanilla == null || _doc?.Room == null || inst.ObjectDefinition == null) return;
        try
        {
            using var dlg = new VanillaCodeDialog(this, _vanilla, inst.ObjectDefinition, _vanilla.RoomIndex(_doc.Room), inst.X, inst.Y, info, inst.CreationCode, inst.PreCreateCode);
            dlg.ShowDialog(this);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "打开原版事件查看器失败：\n" + ex.Message, "查看失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowInstanceCode(UndertaleRoom.GameObject inst)
    {
        if (_vanilla == null) return;
        string objName = GameData.Name(inst.ObjectDefinition?.Name);
        if (inst.CreationCode != null)
        {
            VanillaCodeDialog.ShowCode(this, "实例创建代码（只读） — " + objName, _vanilla.Decompile(inst.CreationCode) ?? "（反编译不可用）");
            return;
        }
        if (inst.PreCreateCode != null)
        {
            VanillaCodeDialog.ShowCode(this, "实例预创建代码（只读） — " + objName, _vanilla.Decompile(inst.PreCreateCode) ?? "（反编译不可用）");
            return;
        }
        if (_doc?.Room?.CreationCodeId != null)
        {
            var rc = _doc.Room.CreationCodeId;
            if (rc != null)
            {
                VanillaCodeDialog.ShowCode(this, "房间创建代码（只读） — " + _doc.RoomName(), _vanilla.Decompile(rc) ?? "（反编译不可用）");
                return;
            }
        }
        MessageBox.Show(this, "此实例没有创建代码或预创建代码，房间也没有创建代码。", "创建代码", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static string ShortOneLine(string s, int max)
    {
        s = (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        return s.Length <= max ? s : s.Substring(0, max) + "…";
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

    /// <summary>把三个按钮放进同一行（属性面板窄，三行按钮会把后两个挤到 AutoScroll 之下）。</summary>
    private void AddButtonRow3(string t1, Action a1, string t2, Action a2, string t3, Action a3)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Theme.Bg1,
            Margin = new Padding(0),
            Height = 30,
        };
        for (int i = 0; i < 3; i++) row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));
        var buttons = new[]
        {
            new Button { Text = t1, Dock = DockStyle.Fill, Height = 26, Margin = new Padding(0, 0, 3, 0) },
            new Button { Text = t2, Dock = DockStyle.Fill, Height = 26, Margin = new Padding(3, 0, 3, 0) },
            new Button { Text = t3, Dock = DockStyle.Fill, Height = 26, Margin = new Padding(3, 0, 0, 0) },
        };
        var actions = new Action[] { a1, a2, a3 };
        for (int i = 0; i < 3; i++)
        {
            Theme.StyleButton(buttons[i]);
            int idx = i;
            buttons[i].Click += (s, e) => actions[idx]();
            row.Controls.Add(buttons[i], i, 0);
        }
        AddSpan(row);
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

    /// <summary>选择工具下右键画布：直接进入战斗/对话/剧情编辑（不用先去菜单栏）。</summary>
    private ContextMenuStrip BuildCanvasContextMenu()
    {
        var menu = Theme.CreateMenu();
        var battle = Theme.Item("编辑此实体的战斗…", (s, e) => EditSelectedBattle());
        var dialogue = Theme.Item("编辑此实体的对话…", (s, e) => EditSelectedDialogue());
        var cutscene = Theme.Item("编辑此实体的剧情…", (s, e) => EditSelectedCutscene());
        var vanillaText = Theme.Item("编辑本房间原版对话（文本覆盖）…", (s, e) => EditVanillaDialogue());
        var copy = Theme.Item("复制", (s, e) => CopySelection());
        var paste = Theme.Item("粘贴", (s, e) => PasteClipboard());
        var del = Theme.Item("删除", (s, e) => DeleteSelection());
        menu.Items.AddRange(new ToolStripItem[]
        {
            battle, dialogue, cutscene,
            new ToolStripSeparator(),
            vanillaText,
            new ToolStripSeparator(),
            copy, paste, del,
        });
        menu.Opening += (s, e) =>
        {
            bool hasSelection = _doc != null && _doc.Selection.Count > 0;
            battle.Enabled = hasSelection;
            dialogue.Enabled = hasSelection;
            cutscene.Enabled = hasSelection;
            vanillaText.Enabled = _doc?.Room != null;
            copy.Enabled = hasSelection;
            paste.Enabled = _doc?.Room != null;
            del.Enabled = hasSelection;
        };
        return menu;
    }

    private ContextMenuStrip BuildRoomContextMenu()
    {
        var m = Theme.CreateMenu();
        m.Items.Add(Theme.Item("新建房间（复制当前）…", (s, e) => NewRoomDialog(true)));
        m.Items.Add(Theme.Item("新建空白房间…", (s, e) => NewRoomDialog(false)));
        m.Items.Add(Theme.Item("复制当前房间", (s, e) => DuplicateRoomQuick()));
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(Theme.Item("重命名房间…", (s, e) => RenameRoomDialog()));
        m.Items.Add(Theme.Item("删除房间", (s, e) => DeleteRoomAction()));
        m.Items.Add(Theme.Item("恢复房间到流程顺序", (s, e) => RestoreRoomOrderAction()));
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

    /// <summary>房间名必须是合法 GML 资源名（字母/数字/下划线，不以数字开头），且不与现有房间重名。</summary>
    private static bool IsValidRoomIdentifier(string name)
    {
        if (name.Length == 0) return false;
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_';
            if (!ok) return false;
            if (i == 0 && c >= '0' && c <= '9') return false;
        }
        return true;
    }

    private bool RoomNameExists(string name, UndertaleRoom? except = null)
    {
        var rooms = _doc?.Game?.Rooms;
        if (rooms == null) return false;
        foreach (var r in rooms)
        {
            if (except != null && ReferenceEquals(r, except)) continue;
            if (string.Equals(GameData.Name(r.Name), name, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>矩阵 MG-105：非法/重复/空房间名要弹提示且不写入。</summary>
    private bool ValidateRoomName(string name, UndertaleRoom? except, out string error)
    {
        name = name.Trim();
        if (name.Length == 0)
        {
            error = "房间名不能为空。";
            return false;
        }
        if (!IsValidRoomIdentifier(name))
        {
            error = "房间名不合法：只能用字母、数字和下划线，且不能以数字开头（GML 资源名规则）。";
            return false;
        }
        if (RoomNameExists(name, except))
        {
            error = "已有同名房间：" + name + "。请换一个名字。";
            return false;
        }
        error = "";
        return true;
    }

    /// <summary>
    /// 引擎特殊房间（ROOM_INITIALIZE / ROOM_TEST…）：原版靠固定名字找它们，改名/删除会让游戏启动或流程崩。
    /// 只是提醒，不硬拦——改完马上改回去也可能是有意操作。
    /// </summary>
    internal static bool IsEngineRoom(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        name = name.Trim();
        // ★ 必须区分大小写：原版普通房间全是 room_xxx（小写），只有引擎房是 ROOM_ 大写。
        // 用 OrdinalIgnoreCase 会把 room_krisroom 之类全部误判成引擎房（QA 全量测试抓到）。
        return name.StartsWith("ROOM_", StringComparison.Ordinal);
    }

    private void NewRoomDialog(bool withContent)
    {
        if (_doc?.Room == null) return;
        string baseName = _doc.RoomName() + (withContent ? "_copy" : "_new");
        if (RoomNameExists(baseName))
        {
            // 默认名也可能已被占用（连续新建）：先找个空的默认名。
            int n = 2;
            while (RoomNameExists(baseName + "_" + n)) n++;
            baseName = baseName + "_" + n;
        }
        string? name = PromptDialog.Show(this, withContent ? "新建房间" : "新建空白房间",
            withContent ? "新房间名称（会复制当前房间的图层与内容）：" : "新房间名称（只复制图层结构，内容为空）：", baseName);
        if (name == null) return;
        if (!ValidateRoomName(name, null, out string error))
        {
            MessageBox.Show(this, error, "房间名不合法", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        name = name.Trim();
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
        if (name == null) return;
        name = name.Trim();
        if (string.Equals(name, old, StringComparison.Ordinal)) return;
        if (!ValidateRoomName(name, _doc.Room, out string error))
        {
            MessageBox.Show(this, error, "房间名不合法", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (IsEngineRoom(old) || IsEngineRoom(name))
        {
            var warn = MessageBox.Show(this,
                $"「{old}」是引擎特殊房间（章节初始化/测试用），原版靠固定名字找它。\n改名后原版流程可能找不到它，游戏可能无法启动。\n\n仍然要改名吗？",
                "重命名房间", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (warn != DialogResult.Yes) return;
        }
        if (!_doc.RenameRoom(name)) return;
        // 战斗/对话/剧情/文本覆盖里的房间名也要一起改：否则 Validate 报「data.win 里没有房间 <旧名>」，
        // 保存看起来成功、mod 却永远生成不了（QA full10 S15 实测）。
        int refs = _battles?.File.RenameRoomReferences(old, name) ?? 0;
        string refNote = refs > 0 ? $"；同步 {refs} 处触发/绑定房间名" : "";
        UpdateStatus();
        _statusLeft.Text = IsBaselineEdit()
            ? $"已重命名房间：{old} → {name}（保存后写入 mod 的 roomPatches.rename；原版 data.win 不动；不可撤销）{refNote}"
            : $"已重命名房间：{old} → {name}（不可撤销，Ctrl+Z 撤不回来）{refNote}";
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
        string engineWarn = IsEngineRoom(name)
            ? (IsBaselineEdit()
                ? "\n\n⚠ 这是引擎特殊房间（章节初始化/测试用），从流程顺序摘除后游戏可能无法启动。"
                : "\n\n⚠ 这是引擎特殊房间（章节初始化/测试用），删除后原版流程可能找不到它，游戏可能无法启动。")
            : "";

        // ★ 基线编辑一律不物理删原版房间：房间编号 = ROOM 表下标，删中间房间会让其后编号全体 -1，
        //   而原版字节码里的房间常量是裸 int、不会被重写（会静默跳错房间）。
        //   所以「删除」= 从 RoomOrder 摘除，保存时写进 mod 的 removeFromOrder（可逆、data.win 不动）。
        if (IsBaselineEdit())
        {
            if (_doc.IsRemovedFromOrder(_doc.Room))
            {
                MessageBox.Show(this, $"房间「{name}」已经不在流程顺序里了。\n\n用「恢复房间到流程顺序」把它放回去。",
                    "删除房间", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var rb = MessageBox.Show(this,
                $"从流程顺序摘除房间「{name}」？\n\n" +
                "原版房间编号 = ROOM 表下标，物理删除会让其后所有房间编号 -1（字节码里的房间常量不会被重写，会静默跳错房间）。\n" +
                "所以这里只把它从 RoomOrder 里摘除：房间资源与编号保留，门链 room_goto_next 会跳过它。\n\n" +
                "改动保存时写进 mod 的 removeFromOrder（原版 data.win 不动），可用「恢复房间到流程顺序」撤销。" + engineWarn,
                "删除房间（从流程顺序摘除）", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (rb != DialogResult.Yes) return;
            if (!_doc.SetRemovedFromOrder(_doc.Room, true)) return;
            UpdateStatus();
            _statusLeft.Text = $"已从流程顺序摘除房间：{name}（保存后写入 mod 的 removeFromOrder；原版 data.win 不动）";
            return;
        }

        var r = MessageBox.Show(this,
            $"删除房间「{name}」？\n\n删除会同时从 RoomOrder 里移除它；这个操作不进撤销栈。\n保存后才会写回 data.win。" + engineWarn,
            "删除房间", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (r != DialogResult.Yes) return;
        if (!_doc.DeleteRoom()) return;
        _statusLeft.Text = $"已删除房间：{name}";
    }

    /// <summary>把当前房间放回流程顺序（撤销基线编辑里的「删除房间」）。</summary>
    private void RestoreRoomOrderAction()
    {
        if (_doc?.Room == null) return;
        string name = _doc.RoomName();
        if (!_doc.IsRemovedFromOrder(_doc.Room))
        {
            _statusLeft.Text = $"房间「{name}」本来就在流程顺序里。";
            return;
        }
        if (!_doc.SetRemovedFromOrder(_doc.Room, false)) return;
        UpdateStatus();
        _statusLeft.Text = $"已恢复房间到流程顺序：{name}（保存后不再写入 removeFromOrder）";
    }

    /// <summary>当前是不是「直接编辑原版基线」会话（不写 data.win，改动蒸馏成 mod 的 roomPatches）。
    /// 与 AutoGenerateModNow/ApplyRoomDiff 的口径一致：有 diff 基线 = 基线编辑。</summary>
    private bool IsBaselineEdit() => !string.IsNullOrEmpty(_diffBaseWin);

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

    /// <summary>有未保存修改时拦截关闭：保存/不保存/取消（矩阵 MG-143 / MG-144）。</summary>
    private void OnEditorFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_doc?.Dirty != true) return;
        if (e.CloseReason == CloseReason.WindowsShutDown || e.CloseReason == CloseReason.TaskManagerClosing) return;
        var r = MessageBox.Show(this,
            "当前文件有未保存的修改。\n\n退出前要保存吗？",
            "未保存的修改", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
        if (r == DialogResult.Cancel)
        {
            e.Cancel = true;
            return;
        }
        if (r == DialogResult.No) return;
        Save(false);
        if (_doc?.Dirty == true) e.Cancel = true;
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
        var targetKind = FileRole.Classify(path, root);
        if (targetKind == FileKind.Baseline && saveAs)
        {
            MessageBox.Show(this,
                "目标仍在原版基线目录（backup/）里，已拒绝写入。请选择 backup 之外的位置。",
                FileRole.BaselineBadge, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // 所有保存都产出 Neutraled mod：原版基线 data.win 只读，地图/房间改动做差分写进 sidecar 的
        // roomPatches / rooms 段，再生成 mod；其它目标是可写的中间量，地图改动直接写回 data.win（不做差分，避免叠加）。
        bool writeData = true;
        if (targetKind == FileKind.Baseline) writeData = false;

        if (writeData && FileRole.IsLive(targetKind) && !_warnedDeployed)
        {
            var r = MessageBox.Show(this,
                "你正在覆盖 Neutraled 的部署产物。\n\nNeutraled 重新部署时会用原版备份 + mods 重建这个文件，这次修改会被覆盖。\n\n仍要保存吗？",
                "保存到部署产物", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) return;
            _warnedDeployed = true;
        }
        Cursor = Cursors.WaitCursor;
        _statusLeft.Text = writeData ? "正在写回 data.win …" : "正在写回编辑配置 …";
        Application.DoEvents();
        try
        {
            if (writeData)
            {
                var saved = _doc.Save(path, false, root);
                if (!saved.Ok)
                {
                    MessageBox.Show(this, saved.Error ?? "保存被拒绝。", "保存已拦截",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _statusLeft.Text = "保存已拦截：" + (saved.Error ?? "");
                    return;
                }
            }
            // 地图/房间改动一律变成 roomPatches / rooms 差分写进 sidecar（data.win 只是中间量）。
            var diff = (_doc.Dirty || _lastRoomDiff == null) ? ApplyRoomDiff(false) : _lastRoomDiff;
            // sidecar：原版基线写 Neutraled/editor/baseline_<leaf>.ntlbattle.json（不碰 backup），其余写在 data.win 旁边。
            string sidecarPath = BattleProject.SidecarForEdit(path, root);
            var side = _battles?.Save(sidecarPath, true, root);
            if (side.HasValue && !side.Value.Ok)
                throw new InvalidOperationException(side.Value.Error ?? "编辑配置保存被拒绝");
            // 原版基线不写 data.win（writeData=false），Dirty 不会由 EditorDocument.Save 清掉；
            // 房间差分与编辑配置此时都已落到 sidecar，显式清掉「* 未保存」，否则用户会以为没保存。
            if (!writeData) _doc.MarkSaved();
            _fileRole = FileRole.Classify(path, root);
            string stats = $"战斗 {_battles?.File.Battles.Count ?? 0} 场 / 对话 {_battles?.File.Dialogues.Count ?? 0} 段 / 剧情 {_battles?.File.Cutscenes.Count ?? 0} 段 / 设置 {_battles?.File.Settings.Count ?? 0} 项 / 文本改写 {_battles?.File.TextOverrides.Count ?? 0} 条";
            if (diff != null && diff.ChangeCount > 0) stats += $" / 地图改动 {diff.ChangeCount} 处（{diff.Patches.Count} 个房间）";
            // 绝对路径会让单行超长被截断（QA 体验 #7）→ 主文本只放文件名，完整路径挂 ToolTip。
            string status = writeData
                ? $"已保存 {Path.GetFileName(path)}（{stats}）"
                : $"已保存编辑配置（原版 data.win 未改动；{stats}）· {Path.GetFileName(sidecarPath)}";
            string statusFull = (writeData ? "data.win: " + path + "\n" : "") + "sidecar: " + sidecarPath;
            if (diff != null && diff.ChangeCount > 0 && _battles != null && _settings.AutoGenerateMod)
                status += "；地图改动已写入 mod 的 roomPatches";
            SetStatusLine(status, statusFull);
            UpdateTitle();
            // 常驻房间面板（"当前房间 · 尺寸 · 实例 · 图块 · * 未保存"）由 UpdateStatus() 重算；
            // 保存成功后不刷新，面板会一直挂着已经失效的「* 未保存」（QA S10.20 实测）。
            UpdateStatus();
            if (_settings.AutoGenerateMod)
            {
                var gen = AutoGenerateModNow(false);
                if (gen != null)
                {
                    _statusTip.SetToolTip(_statusLeft, statusFull + "\nmod: " + gen.ModDir);
                    SetStatusLine(status + $"；已生成 mod: {Path.GetFileName(gen.ModDir.TrimEnd(Path.DirectorySeparatorChar))}",
                        statusFull + "\nmod: " + gen.ModDir);
                }
                // gen == null：AutoGenerateModNow 已经写好「配置有 N 个错误，未生成 mod（详见 …）」+ 错误明细 ToolTip，别覆盖
            }
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
            "编辑\n  选择工具下右键画布：编辑此实体的战斗/对话/剧情、编辑本房间原版对话、复制/粘贴/删除\n  拖动 移动选中实例 · 方向键 微移 1px（Shift 8px）\n  框选 空白处拖出矩形 · Shift 加选 · Ctrl+A 全选 · Ctrl+I 反选\n  Delete 删除 · Ctrl+C/X/V 复制/剪切/粘贴 · Ctrl+D 再制\n  Ctrl+Z 撤销 · Ctrl+Y 重做\n" +
            "  S 瓦片选择：拖框选图块（Shift 加选/反选）· 拖动已选图块移动 · 右键删单块\n     Ctrl+A/C/X/V/D 对图块同样有效（粘贴落在光标处）\n\n" +
            "文件\n  Ctrl+O 打开 · Ctrl+S 保存（保存即自动生成 mod）· Ctrl+Shift+S 另存为\n" +
            "  原版基线（backup/）也能直接编辑：data.win 不改，对话/战斗/剧情/文本改写存到 Neutraled/editor/ 并生成 mod\n\n" +
            "原版内容直接编辑\n  打开原版 data.win（文件 → 打开游戏章节 → 原版基线）即可编辑，不需要先复制\n" +
            "  对话：编辑菜单/战斗菜单 → 编辑本房间原版对话…（Ctrl+Shift+E，或右键画布）按 lang key 覆盖原版文本，保存后由生成的 mod 调 ntl_set_lang_string 生效\n" +
            "  战斗：选中原版实体 → Ctrl+B 新建/编辑战斗（敌人/回合弹幕/胜负效果）\n" +
            "  剧情：选中实体 → Ctrl+Shift+K；步骤=wait/move/face/action/dialogue/flag/message/sound/shake/waitinput\n" +
            "  非简单剧情用代码引用：步骤类型 event / script —— event 在最近的该对象实例上执行原版事件，script 全局执行原版脚本\n" +
            "    （剧情对话框工具条「插入原版代码…」可直接从当前房间的原版事件里挑）\n\n" +
            "战斗\n  encounter = 进战斗界面；overworld = 地图上直接躲弹幕\n" +
            "  弹幕 type 用目标章节原版 obj_dbulletcontroller 模板编号；跨章节最稳是 0（环形）与 1（上下边缘）\n" +
            "  「保存时自动生成 Mod」默认开：Ctrl+S 存盘后立即刷新 mods/<id>/<author>/<leaf>\n" +
            "  菜单 战斗 → 立即重新生成 Mod… / 保存战斗配置（仅写 sidecar）/ mods 清单（mods.json）…\n\n" +
            "对话与 Mod 设置项\n  选中实例 → 属性面板「对话」→「为此实体新建纯对话…」（或 Ctrl+Shift+B）：多行文本 + 触发半径/flag + 结束 effects\n" +
            "  菜单 战斗 → Mod 设置项…：注册进游戏内 Neutraled「Mod 设置」面板的项（bool/choice/int）\n" +
            "  功能绑定 feature：battlesEnabled / overworldEnabled / dialoguesEnabled / cutscenesEnabled / barrageDamagePercent，切换后实时生效（存档持久化）\n\n" +
            "房间列表\n  默认按剧情线性流程排序：主线 01..N，支线缩进跟在最近的前置主线后，测试/调试/占位/旧版集中置底\n" +
            "  视图 → 房间按剧情流程排序 可切回 data.win 原始顺序（记在启动设置里）；筛选框搜索时平铺、不缩进\n\n" +
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
            var target = StartupSettings.ResolveTarget(FindGameRoot(), RecentStore.LastPath);
            if (target.Path != null && File.Exists(target.Path))
            {
                OpenPath(target.Path, false);
                if (_doc != null)
                {
                    _startupNote = target.FromDefault ? "默认启动: " + target.Source : "启动: " + target.Source;
                    if (_fileRole == FileKind.Baseline)
                        // 无参数/双击启动时也要让用户看到「确实加载了」（QA 体验 #6）：带上房间/对象数与文件名。
                        SetStatusLine($"原版基线 — data.win 只读；已加载 {_roomRows.Count} 个房间 / {_objects.Count} 个对象；改动存 Neutraled/editor 并生成 mod",
                            "data.win: " + target.Path + "（只读，保存只写 Neutraled/editor 并生成 mod）");
                    else UpdateStatus();
                }
            }
            else
            {
                _statusLeft.Text = "就绪 — 文件 → 打开 data.win（也可从菜单打开游戏章节；默认启动章节见 文件 菜单）";
            }
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        bool editingText = ActiveControl is TextBoxBase;
        // 焦点在筛选框/属性输入框时，文本框会吃掉 Ctrl+S 等组合键 —— 但「保存即生成 mod」这类全局动作
        // 必须仍然生效（否则用户输完筛选条件按 Ctrl+S 会静默什么都不发生）。
        if (editingText)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.S: Save(false); return true;
                case Keys.Control | Keys.Shift | Keys.S: Save(true); return true;
                case Keys.Control | Keys.O: OpenDialog(); return true;
                case Keys.Control | Keys.B: EditSelectedBattle(); return true;
                case Keys.Control | Keys.Shift | Keys.B: EditSelectedDialogue(); return true;
                case Keys.Control | Keys.Shift | Keys.K: EditSelectedCutscene(); return true;
                case Keys.Control | Keys.Shift | Keys.E: EditVanillaDialogue(); return true;
            }
        }
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
                case Keys.Control | Keys.Shift | Keys.E: EditVanillaDialogue(); return true;
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
                case Keys.Escape: _doc.ClearSelection(); UpdateStatus(); return true;
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
                        UpdateToolStatus();
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
                _statusLeft.Text = "已保存战斗 " + battle.Id;
                if (_settings.AutoGenerateMod)
                {
                    var gen = AutoGenerateModNow(false);
                    if (gen != null) _statusLeft.Text += ModStatusSuffix(gen);
                }
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
        var root = FindGameRoot();
        // 原版基线的编辑数据写到 Neutraled/editor/（不碰 backup），所以 sidecar 永远允许写；
        // 真正被保护的只有 data.win 本身（EditorDocument.Save 的基线闸门）。
        var sidecar = BattleProject.SidecarForEdit(_doc.FilePath, root);
        var result = _battles.Save(sidecar, true, root);
        if (!result.Ok)
        {
            _statusLeft.Text = "编辑配置未保存：" + (result.Error ?? "写入被拒绝");
            if (notify)
                MessageBox.Show(this, result.Error ?? "编辑配置未保存。", "保存编辑配置",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (notify) SetStatusLine("已保存编辑配置 · " + Path.GetFileName(sidecar), "sidecar: " + sidecar);
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
                _statusLeft.Text = "已保存对话 " + dialogue.Id;
                if (_settings.AutoGenerateMod)
                {
                    var gen = AutoGenerateModNow(false);
                    if (gen != null) _statusLeft.Text += ModStatusSuffix(gen);
                }
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
        using (var dlg = new CutsceneEditDialog(cutscene, roomName, inst.InstanceID, objectName, CurrentRoomVanillaEvents()))
        {
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                dlg.SaveToCutscene();
                _battles.Normalize();
                SaveBattles(false);
                _statusLeft.Text = "已保存剧情 " + cutscene.Id + "（" + cutscene.Steps.Count + " 步）";
                if (_settings.AutoGenerateMod)
                {
                    var gen = AutoGenerateModNow(false);
                    if (gen != null) _statusLeft.Text += ModStatusSuffix(gen);
                }
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

    /// <summary>把当前内容打包成 .ntlmod（Neutraled 的 mod 安装包 = 叶子目录的 zip，丢进 mods/ 或 --mod-install 安装）。</summary>
    private void ExportNtlmod()
    {
        if (_doc == null || _battles == null)
        {
            MessageBox.Show(this, "请先打开一个 data.win。", "导出 .ntlmod", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var gen = AutoGenerateModNow(false);
        if (gen == null)
        {
            // 生成失败最常见的原因是配置校验错误（例如改了房间名但触发还指向旧名）——
            // 不能一律说「没有内容」，否则用户看不出真原因（QA full10 S15.24 实测）。
            if (!string.IsNullOrEmpty(_lastGenerateError))
                MessageBox.Show(this, "导出失败：\n\n" + _lastGenerateError, "导出 .ntlmod",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else
                MessageBox.Show(this, "还没有可导出的内容（战斗 / 对话 / 剧情 / 文本改写 / 地图改动都为空）。", "导出 .ntlmod",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        string leaf = string.IsNullOrWhiteSpace(_battles.File.Mod.Leaf) ? BattleProject.ChapterLeaf(_doc.FilePath) : _battles.File.Mod.Leaf;
        string modId = string.IsNullOrWhiteSpace(_battles.File.Mod.Id) ? "battle.demo" : _battles.File.Mod.Id;
        string dir = Path.Combine(GameRoot(), "Neutraled", "editor", "export");
        string dst = Path.Combine(dir, modId + "-" + leaf + ".ntlmod");
        try
        {
            Directory.CreateDirectory(dir);
            if (File.Exists(dst)) File.Delete(dst);
            System.IO.Compression.ZipFile.CreateFromDirectory(gen.ModDir, dst,
                System.IO.Compression.CompressionLevel.Optimal, false);
            long kb = new FileInfo(dst).Length / 1024;
            string text = "已导出 mod 包：\n" + dst + "\n\n安装：把 .ntlmod 放进 Neutraled/mods/，或用 ntl-builder --mod-install。";
            // 状态栏先落「已导出」：后面还有安装询问框，脚本/用户可能先读状态栏（QA full10 S14.3 卡在这）。
            SetStatusLine($"已导出 .ntlmod：{Path.GetFileName(dst)}（{kb} KB）",
            "mod 包: " + dst + "\n安装：把 .ntlmod 放进 Neutraled/mods/，或用 ntl-builder --mod-install");
            var install = MessageBox.Show(this, text + "\n\n现在同时安装到 Neutraled/mods/ 吗？（会替换同名 id 的旧安装）",
                "导出 .ntlmod", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (install == DialogResult.Yes)
            {
                var res = NtlmodInstaller.Install(GameRoot(), dst, leaf);
                SetStatusLine(res.Ok ? "已导出并安装 .ntlmod：" + Path.GetFileName(dst) : "已导出 .ntlmod（安装失败：" + res.Message + "）",
                    res.Ok ? "安装位置: " + res.Target : "安装失败: " + res.Message);
                MessageBox.Show(this, res.Message, "安装 .ntlmod",
                    MessageBoxButtons.OK, res.Ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
                return;
            }
            MessageBox.Show(this, text, "导出 .ntlmod", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "导出失败：\n" + ex.Message, "导出 .ntlmod", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _statusLeft.Text = "导出 .ntlmod 失败：" + ex.Message;
        }
    }

    /// <summary>选一个 .ntlmod 装进 Neutraled/mods/（编辑器自己完成安装，不依赖命令行）。</summary>
    private void InstallNtlmodFromFile()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "安装 .ntlmod",
            Filter = "Neutraled mod 包 (*.ntlmod)|*.ntlmod|所有文件 (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var res = NtlmodInstaller.Install(GameRoot(), dlg.FileName);
        MessageBox.Show(this, res.Message, "安装 .ntlmod",
            MessageBoxButtons.OK, res.Ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        SetStatusLine(res.Ok ? "已安装 .ntlmod：" + Path.GetFileName(dlg.FileName) : "安装失败：" + res.Message,
            res.Ok ? "安装位置: " + res.Target : "安装失败: " + res.Message);
    }

    /// <summary>把配置生成成可部署的 Neutraled mod（mods/ 目录下）；保存文档时也会自动调用。</summary>
    private void GenerateBattleMod()
    {
        if (_battles == null)
        {
            MessageBox.Show(this, "编辑配置尚未加载。", "生成 Mod", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!HasAnyContent(_battles))
        {
            MessageBox.Show(this, "还没有任何战斗、对话、剧情或文本改写。先在地图上选实体并新建，或用「编辑本房间原版对话…」。", "生成 Mod",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var leaf = PromptLeaf(_battles.File.Mod.Leaf);
        if (leaf == null) return; // 取消生成
        _battles.File.Mod.Leaf = leaf;
        try
        {
            SaveBattles(false);
            var r = AutoGenerateModNow(true);
            if (r != null) _statusLeft.Text = "已生成 Mod " + r.ModDir;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "生成失败：" + ex.Message, "生成 Mod", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _statusLeft.Text = "生成失败：" + ex.Message;
        }
    }

    private static bool HasAnyContent(BattleProject battles) =>
        battles.File.Battles.Count > 0 || battles.File.Dialogues.Count > 0
        || battles.File.Cutscenes.Count > 0 || battles.File.TextOverrides.Count > 0
        || battles.File.RoomPatches.Count > 0 || battles.File.NewRooms.Count > 0;

    /// <summary>
    /// 地图/房间差分的基线 data.win：只有原版基线才做差分（原文件当基线，改动以 roomPatches 进 mod）。
    /// 其它目标（游戏文件/部署产物）保存时直接把改动写回 data.win，不做差分——否则同一份改动会
    /// 「写进 data.win + 再进 mod 的 roomPatches」，部署后叠加两次（实例重复创建）。
    /// </summary>
    private string? CaptureDiffBase(string path, string? root)
    {
        try
        {
            if (FileRole.Classify(path, root) == FileKind.Baseline) return path;
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 打开文件时回灌 sidecar 里的地图/房间差分。原版基线：改到内存里，保存时再按「原版 vs 内存」重算；
    /// 其它目标：以打开时文件为基线，若 sidecar 已有旧差分则不动它（数据.win 照常写回，避免差分叠加）。
    /// </summary>
    private void RestoreRoomPatchState(string path, string? root)
    {
        if (_doc == null || _battles == null || _diffBaseWin == null) return;
        bool isBaseline = FileRole.Classify(path, root) == FileKind.Baseline;
        if (!isBaseline)
        {
            if (_battles.File.RoomPatches.Count > 0 || _battles.File.NewRooms.Count > 0)
                _startupNote = "sidecar 里有地图差分，但本目标保存时直接把改动写回 data.win（地图改动只在编辑原版 data 时进 mod）";
            return;
        }
        if (_battles.File.RoomPatches.Count == 0 && _battles.File.NewRooms.Count == 0) return;
        try
        {
            var applied = RoomPatchApply.Apply(_doc.Game.Data, _battles.File.RoomPatches, _battles.File.NewRooms);
            if (applied.RoomsPatched > 0 || applied.RoomsAdded > 0)
                _startupNote = $"已回灌 {applied.Describe()}";
            if (applied.Warnings.Count > 0)
                _startupNote = $"回灌地图改动有 {applied.Warnings.Count} 条警告（{applied.Warnings[0]}）";
        }
        catch (Exception ex)
        {
            _startupNote = "回灌地图改动失败：" + ex.Message;
        }
    }

    /// <summary>
    /// 用「打开时的基线 data.win」与当前内存房间对比，得到 roomPatches / rooms 差分并写进编辑配置。
    /// 保存与生成 mod 前都会调用（这样 data.win 只是中间量，地图改动也能进 mod）。
    /// </summary>
    private RoomDiffResult? ApplyRoomDiff(bool notify)
    {
        if (_doc == null || _battles == null) return null;
        if (string.IsNullOrEmpty(_diffBaseWin) || !File.Exists(_diffBaseWin))
        {
            if (notify) _statusLeft.Text += "；没有房间差分基线，地图改动不会进 mod";
            return null;
        }
        try
        {
            var baseGame = new GameData(_diffBaseWin);
            _baselineGame = baseGame;
            var diff = RoomDiff.Build(baseGame.Data, _doc.Game.Data);
            _battles.File.RoomPatches = diff.Patches;
            _battles.File.NewRooms = diff.NewRooms;
            _lastRoomDiff = diff;
            if (notify)
            {
                _statusLeft.Text = diff.ChangeCount > 0
                    ? $"房间差分：{diff.Patches.Count} 个房间 / {diff.ChangeCount} 处改动 / 新增房间 {diff.NewRooms.Count} 个"
                    : "房间差分：没有地图改动";
                if (diff.Warnings.Count > 0) _statusLeft.Text += $"；警告 {diff.Warnings.Count} 条";
            }
            return diff;
        }
        catch (Exception ex)
        {
            _statusLeft.Text += "；计算房间差分失败：" + ex.Message;
            return null;
        }
    }

    /// <summary>
    /// 按当前 sidecar 重新生成 Neutraled mod（不弹叶子选择，用 Mod.Leaf；未设置时按打开的章节推断）。
    /// notify=false 时只写状态栏，不打断保存流程。
    /// </summary>
    private GenerateResult? AutoGenerateModNow(bool notify)
    {
        if (_battles == null || _doc == null) return null;
        _lastGenerateError = null;
        if (!HasAnyContent(_battles))
        {
            _lastGenerateError = "还没有任何战斗 / 对话 / 剧情 / 文本改写 / 地图改动。";
            if (notify)
                MessageBox.Show(this, "还没有任何战斗、对话、剧情或文本改写配置。", "生成 Mod",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            else _statusLeft.Text += "；没有可生成的配置，已跳过";
            return null;
        }
        if (string.IsNullOrWhiteSpace(_battles.File.Mod.Leaf))
            _battles.File.Mod.Leaf = BattleProject.ChapterLeaf(_doc.FilePath);
        // 生成前把最新的地图/房间差分写进配置（保存文档、单独点「立即重新生成」时都能带上）。
        if (_doc.Dirty || _lastRoomDiff == null) ApplyRoomDiff(false);
        // 非原版基线：地图改动直接写回 data.win，不该再进 mod（否则部署后实例重复）；清掉可能残留的差分。
        if (string.IsNullOrEmpty(_diffBaseWin))
        {
            _battles.File.RoomPatches.Clear();
            _battles.File.NewRooms.Clear();
        }
        // 基线（原版 data.win）用于「新增房间是否真的不存在」这类判断；没有基线时退回当前数据。
        var issues = _battles.Validate(_doc.Game, _baselineGame);
        var errors = issues.Where(i => i.IsError).ToList();
        if (errors.Count > 0)
        {
            string detail = string.Join("\n", errors.Select(i => "· " + i.Message));
            _lastGenerateError = "配置有 " + errors.Count + " 个错误，未生成 mod：\n\n" + detail;
            if (notify)
                MessageBox.Show(this, "配置有错误，先修好：\n\n" + detail, "生成 Mod",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else
            {
                // 错误明细不适合塞进单行状态栏（会被截断）→ 主文本指路，明细挂 ToolTip（QA 体验 #3）。
                _statusLeft.Text += "；配置有 " + errors.Count + " 个错误，未生成 mod（详见 战斗 → 立即重新生成 Mod…；悬停看明细）";
                _statusTip.SetToolTip(_statusLeft, "配置错误：\n" + detail);
            }
            return null;
        }
        var r = BattleGenerator.Generate(_battles, Path.Combine(GameRoot(), "Neutraled", "mods"), _doc.Game);
        if (notify)
        {
            var warns = issues.Where(i => !i.IsError).ToList();
            var msg = "已生成 mod：\n" + r.ModDir + "\n\nGML " + r.GmlFiles + " 个 / 对象事件 " + r.ObjectFiles + " 个 / 数据 " + r.DataFiles + " 个"
                + "\n战斗 " + _battles.File.Battles.Count + " 场 / 对话 " + _battles.File.Dialogues.Count + " 段 / 剧情 "
                + _battles.File.Cutscenes.Count + " 段 / 设置项 " + _battles.File.Settings.Count + " 项 / 文本改写 "
                + _battles.File.TextOverrides.Count + " 条 / 地图改动 " + (_lastRoomDiff?.ChangeCount ?? 0) + " 处（"
                + _battles.File.RoomPatches.Count + " 个房间 / 新增 " + _battles.File.NewRooms.Count + " 个）";
            if (warns.Count > 0) msg += "\n\n警告：\n" + string.Join("\n", warns.Select(i => "· " + i.Message));
            MessageBox.Show(this, msg, "生成 Mod", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        return r;
    }

    /// <summary>当前房间内所有实例对象的原版事件（供剧情步骤「插入原版代码…」/ 对话文本编辑用）。</summary>
    private List<VanillaEventInfo> CurrentRoomVanillaEvents()
    {
        var list = new List<VanillaEventInfo>();
        if (_vanilla == null || _doc?.Room == null) return list;
        int roomIndex = _vanilla.RoomIndex(_doc.Room);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in RoomEdit.AllInstances(_doc.Room))
        {
            var obj = t.Inst.ObjectDefinition;
            if (obj == null) continue;
            foreach (var ev in _vanilla.EnumerateEvents(obj))
            {
                if (ev.Code == null) continue;
                if (!seen.Add(ev.OwnerObject + "|" + ev.Name)) continue;
                list.Add(ev);
            }
        }
        return list;
    }

    /// <summary>编辑当前房间原版对话文本（按 lang key 覆盖，不改 data.win；保存后写进 sidecar 并生成 mod）。</summary>
    private void EditVanillaDialogue()
    {
        if (_doc == null) return;
        if (_battles == null || _vanilla == null)
        {
            MessageBox.Show(this, "编辑配置或原版扫描器尚未加载。", "原版对话", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_doc.Room == null)
        {
            MessageBox.Show(this, "请先在左侧打开一个房间。", "原版对话", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var dlg = new VanillaDialogueEditDialog(_vanilla, _doc.Room, _battles);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _battles.Normalize();
        SaveBattles(false);
        string room = _doc.RoomName();
        int mine = _battles.File.TextOverrides.Count(t => t.Room == room);
        _statusLeft.Text = $"已保存原版对话文本改写（本房间 {mine} 条 / 全部 {_battles.File.TextOverrides.Count} 条）";
        if (_settings.AutoGenerateMod)
        {
            var gen = AutoGenerateModNow(false);
            if (gen != null) _statusLeft.Text += ModStatusSuffix(gen);
        }
        RebuildInspector();
    }

    private void ToggleFlowSort()
    {
        _settings.SortRoomsByFlow = !_settings.SortRoomsByFlow;
        if (_flowItem != null) _flowItem.Checked = _settings.SortRoomsByFlow;
        try { _settings.Save(); } catch { }
        RebuildRoomList();
        _statusLeft.Text = _settings.SortRoomsByFlow
            ? "房间列表：按剧情流程排序（支线缩进跟在主线后，特殊/测试/旧版置底）"
            : "房间列表：按 data.win 原始顺序";
    }

    private void ToggleAutoGenerate()
    {
        _settings.AutoGenerateMod = !_settings.AutoGenerateMod;
        if (_autoGenItem != null) _autoGenItem.Checked = _settings.AutoGenerateMod;
        try { _settings.Save(); } catch { }
        _statusLeft.Text = _settings.AutoGenerateMod
            ? "保存时自动生成 Mod：开（保存文档即刷新 Neutraled mod）"
            : "保存时自动生成 Mod：关（用 战斗 → 立即重新生成 Mod…）";
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
