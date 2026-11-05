using System.Diagnostics;
using System.Text.Json;

namespace Neutraled.Gui;

public sealed class MainForm : Form
{
    private readonly ComboBox _chapterCombo = new();
    private readonly ComboBox _langCombo = new();
    private readonly ListView _modList = new();
    private readonly TextBox _logBox = new();
    private readonly ProgressBar _progress = new();
    private readonly Label _status = new();
    private readonly Label _lblChapter = new();
    private readonly Button _btnRefresh = new();
    private readonly Button _btnDeploy = new();
    private readonly Button _btnDeployLaunch = new();
    private readonly Button _btnRestore = new();
    private readonly Button _btnImport = new();
    private readonly Button _btnOnline = new();
    private readonly Button _btnUpdates = new();
    private readonly Button _btnAll = new();
    private readonly Button _btnExportLog = new();
    private readonly Label _deployState = new();
    private readonly Label _modDetail = new();
    private readonly Button _btnEnhance = new();
    private readonly Button _btnCache = new();
    private readonly Button _btnTools = new();
    private readonly Button _btnDiag = new();
    private readonly Button _btnInterop = new();
    private readonly Button _btnPerm = new();
    private readonly Button _btnKristal = new();
    private bool _busy;
    private bool _refreshingMods;
    /// <summary>写 mod.json 的 JSON 选项：**必须**放宽转义（CLAUDE.md 硬坑 15：GML 的 json_parse
    /// 吃不下 \uXXXX 转义，description 里一个中文被转义成 \u6E90 就会整份解析失败、mod 被静默跳过）。
    /// 默认 Encoder 会把非 ASCII 全部转义 —— 2026-10-31 真机复现：[live] mod.json 解析失败。</summary>
    private static readonly JsonSerializerOptions ModJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    /// <summary>「退出后自动还原」等待任务的取消源（C4 P0-3）。重新部署/手动还原时要能取消上一次的等待。</summary>
    private CancellationTokenSource? _autoRestoreCts;
    /// <summary>UX-31：部署加速档的二次确认状态（首次勾选弹一次，本次会话内确认过就不再弹）。</summary>
    private static bool _fastDeployConfirmed;
    private bool _suppressFastToggle;

    private sealed class ModRow
    {
        public string Path = "";
        public string Id = "";
        public string Name = "";
        public string Author = "";
        public string Chapter = "";
        public string Version = "1.0.0";
        public bool Enabled = true;
    }

    public MainForm()
    {
        Localizer.MigrateLegacy(Program.NeutraledRoot);   // C4 P1-5：旧 gui_lang.txt 并入 config.json.lang 后归档
        Localizer.Load(Program.NeutraledRoot);
        DeployOptions.Load(Program.NeutraledRoot);   // 部署加速档（Neutraled/gui_deploy.txt）

        Text = Localizer.T("Neutraled — DELTARUNE Mod 管理器");
        // GUI-M01：900x620 时底部「缓存管理/工具箱」与顶部「权限/Kristal」必然被裁（控件最右到 x≈1128）。
        // 最小尺寸按实际内容宽度收紧到 1190x700（AutoScroll 仍保留，DPI/字体变大时还能滚动触达）。
        Width = 1190; Height = 740; MinimumSize = new Size(1190, 700);
        StartPosition = FormStartPosition.CenterScreen;

        // GUI-M01：窗口缩到 MinimumSize(900x620) 时顶部/底部按钮会被右边裁掉。
        // 两个面板改成可横向滚动（内容最小宽度 = 实际最右控件），并略加高避免滚动条压住按钮；
        // 这样小窗口下所有按钮都能通过滚动触达，不需要重排整套布局。
        var topPanel = new Panel { Dock = DockStyle.Top, Height = 64, Padding = new Padding(8), AutoScroll = true, AutoScrollMinSize = new Size(1030, 0) };
        _lblChapter.Text = Localizer.T("章节:"); _lblChapter.Left = 8; _lblChapter.Top = 14; _lblChapter.Width = 50; _lblChapter.AutoSize = false;
        _chapterCombo.Left = 60; _chapterCombo.Top = 10; _chapterCombo.Width = 150;
        _chapterCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        // C4 P0-2：默认目标改为 root（以前默认 chapter4，裸点【部署】会莫名部署第 4 章，见 A3 F01）。
        _chapterCombo.Items.AddRange(new object[] { "root", "chapter1", "chapter2", "chapter3", "chapter4", "chapter5", "all" });   // ntl:chapter4-ok 显式章节列表
        _chapterCombo.SelectedIndex = 0;
        _btnRefresh.Text = Localizer.T("刷新"); _btnRefresh.Left = 222; _btnRefresh.Top = 9; _btnRefresh.Width = 70;
        _btnRefresh.Click += async (_, _) => await RefreshModsAsync();

        _langCombo.Left = 306; _langCombo.Top = 10; _langCombo.Width = 100;
        _langCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _langCombo.Items.AddRange(new object[] { "中文", "English" });
        _langCombo.SelectedIndex = Localizer.Current == "en" ? 1 : 0;
        _langCombo.SelectedIndexChanged += (_, _) =>
        {
            Localizer.Current = _langCombo.SelectedIndex == 1 ? "en" : "zh";
            SaveConfigLang(Localizer.Current);   // config.json.lang = 语言唯一真源（游戏内语言与 GUI 用同一个开关）
            ApplyLocalizedTexts();
        };
        _chapterCombo.SelectedIndexChanged += (_, _) => UpdateDeployState();

        _deployState.Left = 416; _deployState.Top = 14; _deployState.Width = 200; _deployState.AutoSize = false;
        _deployState.ForeColor = Color.DimGray;

        _btnEnhance.Text = Localizer.T("增强选项"); _btnEnhance.Left = 624; _btnEnhance.Top = 9; _btnEnhance.Width = 110;
        _btnDiag.Text = Localizer.T("诊断"); _btnDiag.Left = 742; _btnDiag.Top = 9; _btnDiag.Width = 70;
        _btnInterop.Text = Localizer.T("联动"); _btnInterop.Left = 818; _btnInterop.Top = 9; _btnInterop.Width = 60;
        _btnInterop.Click += async (_, _) => await ShowInteropDialogAsync();
        topPanel.Controls.Add(_btnInterop);
        _btnPerm.Text = Localizer.T("权限"); _btnPerm.Left = 884; _btnPerm.Top = 9; _btnPerm.Width = 60;
        _btnPerm.Click += (_, _) => ShowPermissionsDialog();
        topPanel.Controls.Add(_btnPerm);
        // Kristal 是专有名词，中英一致（保持硬编码）
        _btnKristal.Text = "Kristal"; _btnKristal.Left = 950; _btnKristal.Top = 9; _btnKristal.Width = 70;
        _btnKristal.Click += (_, _) => ShowKristalDialog();   // UX-12：方法内部改为异步并按退出码给结论
        topPanel.Controls.Add(_btnKristal);
        _btnDiag.Click += (_, _) => ShowDiagnosticsDialog();
        topPanel.Controls.Add(_btnDiag);
        _btnEnhance.Click += (_, _) => ShowEnhanceDialog();

        topPanel.Controls.AddRange(new Control[] { _lblChapter, _chapterCombo, _btnRefresh, _langCombo, _deployState, _btnEnhance });

        _modList.Dock = DockStyle.Fill;
        _modList.View = View.Details;
        _modList.CheckBoxes = true;
        _modList.FullRowSelect = true;
        _modList.GridLines = true;
        _modList.Columns.Add("Mod", 300);
        _modList.Columns.Add("ID", 220);
        _modList.Columns.Add(Localizer.T("作者"), 130);
        _modList.Columns.Add(Localizer.T("章节"), 90);
        _modList.Columns.Add(Localizer.T("状态"), 80);
        // GUI-I05：右键菜单 / 双击切换启用 / 列头排序（旧实现只能靠勾选框，列头点了没反应）。
        var modMenu = new ContextMenuStrip();
        modMenu.Items.Add(Localizer.T("启用 / 禁用"), null, async (_, _) => await ToggleSelectedModsAsync());
        modMenu.Items.Add(Localizer.T("打开所在文件夹"), null, (_, _) => OpenSelectedModFolder());
        modMenu.Items.Add(Localizer.T("查看 mod.json"), null, (_, _) => ShowSelectedModJson());
        _modList.ContextMenuStrip = modMenu;
        _modList.DoubleClick += async (_, _) => await ToggleSelectedModsAsync();
        _modList.ColumnClick += (_, e) => SortModsByColumn(e.Column);
        _modList.ItemChecked += (_, e) =>
        {
            // 勾选/取消勾选立刻写回该 mod.json 的 enabled（以前的写法只在点「部署/部署并启动」时才
            // 由 ApplyCheckedState 落盘 → 用户勾完直接用 Steam、启动 bat 或游戏内「重新部署并重启」
            // 启动时，勾选根本没有写进文件，builder 扫描（只收 enabled != false）直接跳过该 mod，
            // 表现就是"明明启用了却没生效"）。
            if (_busy || _refreshingMods || e.Item.Tag is not ModRow row) return;
            if (row.Enabled == e.Item.Checked) return;
            if (SaveModEnabled(row, e.Item.Checked))
            {
                e.Item.SubItems[4].Text = row.Enabled ? Localizer.T("启用") : Localizer.T("禁用");
                _status.Text = Localizer.T("启用状态已保存；点【部署】或【部署并启动】后生效");
            }
            NotifyConfigChanged();   // 配置变更时提示"重启游戏后生效"
        };

        var btnPanel = new Panel { Dock = DockStyle.Bottom, Height = 66, Padding = new Padding(8), AutoScroll = true, AutoScrollMinSize = new Size(1150, 0) };
        void Style(Button b, string text, int left, int width) { b.Text = text; b.Left = left; b.Top = 8; b.Width = width; b.Height = 30; }
        Style(_btnDeploy, Localizer.T("部署"), 8, 90);
        Style(_btnDeployLaunch, Localizer.T("部署并启动"), 106, 120);
        Style(_btnRestore, Localizer.T("恢复原版"), 234, 110);
        Style(_btnImport, Localizer.T("导入旧 mod"), 352, 120);
        Style(_btnOnline, Localizer.T("在线获取"), 480, 100);
        Style(_btnUpdates, Localizer.T("检查更新"), 588, 100);
        Style(_btnAll, Localizer.T("部署全部"), 696, 100);
        Style(_btnExportLog, Localizer.T("导出日志"), 804, 100);
        Style(_btnCache, Localizer.T("缓存管理"), 912, 100);
        Style(_btnTools, Localizer.T("工具箱"), 1018, 110);
        _btnTools.Click += (_, _) => ShowToolboxDialog();
        btnPanel.Controls.Add(_btnTools);
        _btnCache.Click += async (_, _) => await ShowCacheDialogAsync();
        _btnAll.Click += async (_, _) => await DeployAllAsync();
        _btnExportLog.Click += (_, _) => ExportLog();
        _btnDeploy.Click += async (_, _) => await DeployAsync(false);
        _btnDeployLaunch.Click += async (_, _) => await DeployAsync(true);
        _btnRestore.Click += async (_, _) => await RestoreAsync();
        _btnImport.Click += async (_, _) => await ImportAsync();
        _btnOnline.Click += async (_, _) => await OnlineAsync();
        _btnUpdates.Click += async (_, _) => await CheckUpdatesAsync();
        btnPanel.Controls.AddRange(new Control[] { _btnDeploy, _btnDeployLaunch, _btnRestore, _btnImport, _btnOnline, _btnUpdates, _btnAll, _btnExportLog, _btnCache });

        var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 220 };
        _progress.Dock = DockStyle.Top; _progress.Height = 16; _progress.Style = ProgressBarStyle.Continuous;
        _status.Dock = DockStyle.Top; _status.Height = 20;
        _logBox.Multiline = true; _logBox.ScrollBars = ScrollBars.Vertical; _logBox.ReadOnly = true;
        _logBox.Dock = DockStyle.Fill; _logBox.Font = new Font("Consolas", 9);
        bottomPanel.Controls.Add(_logBox); bottomPanel.Controls.Add(_status); bottomPanel.Controls.Add(_progress);

        var detailPanel = new Panel { Dock = DockStyle.Bottom, Height = 96, Padding = new Padding(10, 6, 10, 6) };
        _modDetail.Dock = DockStyle.Fill;
        _modDetail.Font = new Font("Consolas", 9);
        _modDetail.ForeColor = Color.FromArgb(40, 40, 40);
        detailPanel.Controls.Add(_modDetail);
        _modList.SelectedIndexChanged += (_, _) => ShowModDetail();

        Controls.Add(_modList);
        Controls.Add(detailPanel);
        Controls.Add(bottomPanel);
        Controls.Add(btnPanel);
        Controls.Add(topPanel);

        AllowDrop = true;
        DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy;
        };
        DragDrop += async (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
            await ImportPathsAsync(files);
        };

        Load += async (_, _) =>
        {
            ApplyLocalizedTexts();
            Log(Localizer.T("游戏根: {0}", Program.GameRoot));
            Log(Localizer.T("部署加速档: {0}（工具箱里可改）", DeployOptions.FastDeploy ? "on" : "off"));
            if (!File.Exists(Path.Combine(Program.GameRoot, "DELTARUNE.exe")))
                Log(Localizer.T("[警告] 未检测到 DELTARUNE.exe —— 请把整个 Neutraled 文件夹放到游戏根目录下运行"));

            // ★ 打开管理器就先修一次"存档联接悬空"：玩家删掉 Neutraled/saves/<名>/ 之后联接会指向空气，
            //   而游戏在**悬空状态下启动**会让 GM 的文件子系统整个失效（连绝对路径写都失败且不报错），
            //   游戏内自愈那时已经不可能 —— 只能赶在游戏进程起来之前修（见 builder 的 RepairSaveLinks 注释）。
            _ = Task.Run(async () =>
            {
                try { await Program.RunBuilderAsync("--repair-saves", s => Log("  " + s.TrimEnd())); }
                catch { }
            });

            // ★ 顺手确认"守候进程"在跑：Kristal 这类外部章节的启动请求（launch-request.json）
            //   只有守候进程会消费 —— 游戏内没有任何"启动进程"的内置函数，所以守候没跑时
            //   玩家选中外部章节只会看到提示。幂等：已在跑时 --ensure-watcher 什么都不做，
            //   一个外部章节都没配置时它也不会起（见 builder 的 WatchAutostart.EnsureWatcherIfNeeded）。
            _ = Task.Run(async () =>
            {
                try { await Program.RunBuilderAsync("--ensure-watcher", s => Log("  " + s.TrimEnd())); }
                catch { }
            });

            await RefreshModsAsync();
            CheckDeploySessionBanner();
            RefreshAlerts();   // C4 P1-12：首次运行只建基线，不把历史错误当新告警
        };
    }

    /// <summary>按当前语言刷新所有常驻控件的文案（启动时与切换语言后调用，切换后立即生效）。
    /// 工具箱/增强选项等对话框是每次打开时新建的（BuildToolboxDialog / ShowEnhanceDialog 在构造期调 T()），
    /// 天然跟随当前语言，不需要在这里持有它们的控件。</summary>
    private void ApplyLocalizedTexts()
    {
        Text = Localizer.T("Neutraled — DELTARUNE Mod 管理器");
        _lblChapter.Text = Localizer.T("章节:");
        _btnRefresh.Text = Localizer.T("刷新");
        _btnDeploy.Text = Localizer.T("部署");
        _btnDeployLaunch.Text = Localizer.T("部署并启动");
        _btnRestore.Text = Localizer.T("恢复原版");
        _btnImport.Text = Localizer.T("导入旧 mod");
        _btnOnline.Text = Localizer.T("在线获取");
        _btnUpdates.Text = Localizer.T("检查更新");
        _btnAll.Text = Localizer.T("部署全部");
        _btnExportLog.Text = Localizer.T("导出日志");
        _btnCache.Text = Localizer.T("缓存管理");
        _btnTools.Text = Localizer.T("工具箱");
        _btnEnhance.Text = Localizer.T("增强选项");
        _btnDiag.Text = Localizer.T("诊断");
        _btnInterop.Text = Localizer.T("联动");
        _btnPerm.Text = Localizer.T("权限");
        _btnKristal.Text = "Kristal";   // 专有名词，中英一致
        _status.Text = Localizer.T("就绪");
        // 列头 0/1（Mod / ID）是中英一致的通用行话，保持硬编码；只刷新有译文的列。
        if (_modList.Columns.Count >= 5)
        {
            _modList.Columns[2].Text = Localizer.T("作者");
            _modList.Columns[3].Text = Localizer.T("章节");
            _modList.Columns[4].Text = Localizer.T("状态");
        }
        foreach (ListViewItem item in _modList.Items)
        {
            if (item.Tag is ModRow row && item.SubItems.Count >= 5)
                item.SubItems[4].Text = row.Enabled ? Localizer.T("启用") : Localizer.T("禁用");
        }
    }

    /// <summary>把界面语言同步写进 Neutraled/config.json 的 lang（游戏侧读同一个键）。
    /// 经 MergeConfig 就地合并：只动 lang，文件里其它键、键序与中文注释照旧保留；失败只记日志，不影响界面。</summary>
    private void SaveConfigLang(string lang)
    {
        try { MergeConfig(Path.Combine(Program.NeutraledRoot, "config.json"), o => o["lang"] = lang); }
        catch (Exception ex) { Log(Localizer.T("[警告] 写入 config.json 失败: {0}", ex.Message)); }
    }

    /// <summary>config.json 的就地合并写盘 —— 语言切换与增强选项共用这一条路径，避免各写一份。
    /// 文件存在且能解析成对象时直接在现有对象上跑 mutate：只覆盖 mutate 赋过的键，其余键、键序、中文注释
    /// 原样留在重新写出的文件里；文件不存在时以空对象新建（只含这次要写的键）。
    /// 序列化沿用本仓库既有写法（WriteIndented 2 空格 + UnsafeRelaxedJsonEscaping：中文原样 UTF-8，
    /// 不转成 \uXXXX —— 游戏侧 GML 的 json_parse 吃不下转义序列）。</summary>
    /// <param name="overwriteOnBroken">文件在、但内容不是合法 JSON 时：true = 与旧 ShowEnhanceDialog 一致，
    /// 丢弃坏内容、只写这次要写的键；false = 与旧 SaveConfigLang 一致，把异常抛给调用方
    /// （调用方记日志、不写盘，坏文件保持原样）。</param>
    private static void MergeConfig(string path, Action<System.Text.Json.Nodes.JsonObject> mutate, bool overwriteOnBroken = false)
    {
        var obj = new System.Text.Json.Nodes.JsonObject();
        if (File.Exists(path))
        {
            try
            {
                obj = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path), null,
                    new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip })
                    as System.Text.Json.Nodes.JsonObject ?? new System.Text.Json.Nodes.JsonObject();
            }
            catch (Exception) when (overwriteOnBroken) { obj = new System.Text.Json.Nodes.JsonObject(); }
        }
        mutate(obj);
        // GUI-M03 / 裁决 u：写盘统一走 builder 的 ConfigFile（同一个序列化器 + 无 BOM + 镜像存档区），
        // 不再自己 File.WriteAllText —— 否则游戏 root 阶段（章节选择器）读到的存档区镜像会一直是旧配置。
        // 解析仍留在本方法（要支持「坏文件时丢弃 or 抛给调用方」两种既有语义，ConfigFile.Load 不做这种区分）。
        Neutraled.Builder.ConfigFile.Save(Program.GameRoot, obj);
        // 一致性校验：ConfigFile.Save 的镜像是 best-effort，这里再核一次；不一致就补写一份（同一份文本）。
        try
        {
            var mirror = Neutraled.Builder.Paths.SaveMirrorConfigPath();
            if (mirror != null)
            {
                var text = File.ReadAllText(path);
                if (!File.Exists(mirror) || !string.Equals(File.ReadAllText(mirror), text, StringComparison.Ordinal))
                {
                    var mdir = Path.GetDirectoryName(mirror);
                    if (!string.IsNullOrEmpty(mdir) && !Directory.Exists(mdir)) Directory.CreateDirectory(mdir);
                    File.WriteAllText(mirror, text);
                    System.Diagnostics.Debug.WriteLine(Localizer.T("[配置] 检测到存档区镜像不一致，已按 {0} 重新镜像", path));
                }
            }
        }
        catch { /* 校验失败不影响主流程 */ }
    }

    private void Log(string s)
    {
        if (InvokeRequired) { BeginInvoke(() => Log(s)); return; }
        _logBox.AppendText(s + Environment.NewLine);
    }

    /// <summary>增量告警（C4 P1-12）：读游戏日志自上次以来新增的 ERROR 行；偏移记在 Neutraled/gui-alerts.json。
    /// 首次运行没有状态文件 ⇒ 只建基线（静默），所以老错误不会每次启动都弹出来。</summary>
    private void RefreshAlerts()
    {
        try
        {
            var alerts = UiLogic.NewAlerts(UiLogic.DefaultGameLogPath(), UiLogic.AlertsStatePath(Program.NeutraledRoot));
            if (alerts.Count == 0) return;
            Log(Localizer.T("[告警] 检测到 {0} 条新的游戏日志错误（详见日志）", alerts.Count));
            foreach (var line in alerts.Take(5)) Log("  " + line);
            _status.Text = Localizer.T("有 {0} 条新的游戏日志错误", alerts.Count);
        }
        catch { }
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _busy = busy;
        foreach (var b in new[] { _btnDeploy, _btnDeployLaunch, _btnRestore, _btnImport, _btnOnline, _btnUpdates, _btnRefresh })
            b.Enabled = !busy;
        _progress.Style = busy ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
        if (status != null) _status.Text = status;
        Application.DoEvents();
    }

    /// <summary>刷新 mod 列表（C4 P1-13：数据源 = ntl-gui.exe --mod-list --json，与部署共用同一套叶子
    /// 扫描规则 ModScanner；GUI 不再自己写第三套扫描，避免"GUI 里看得到、部署却扫不到"）。</summary>
    private async Task RefreshModsAsync()
    {
        // 刷新时会把每行 Checked 设成 disk 上的值，ItemChecked 会跟着触发；
        // 用 _refreshingMods 挡住「刷新→回写」的假变更（否则每次刷新都可能重写一遍 mod.json）。
        _refreshingMods = true;
        try { await RefreshModsCoreAsync(); }
        finally { _refreshingMods = false; }
    }

    private async Task RefreshModsCoreAsync()
    {
        _modList.Items.Clear();
        var (installed, error) = await ModInstallBridge.LoadAsync(Program.NeutraledRoot);
        if (error.Length > 0)
        {
            Log(Localizer.T("[错误] 读取 mod 列表失败（--mod-list --json）: {0}", error));
            _status.Text = Localizer.T("mod 列表读取失败");
            return;
        }

        int count = 0;
        foreach (var m in installed)
        {
            var row = new ModRow
            {
                Path = m.Path,
                Id = m.Id,
                Name = m.Name,
                Author = m.Author,
                Chapter = m.Chapter,
                Version = m.Version,
                Enabled = m.Enabled
            };
            var item = new ListViewItem(new[] { row.Name, row.Id, row.Author, row.Chapter, row.Enabled ? Localizer.T("启用") : Localizer.T("禁用") })
            { Tag = row, Checked = row.Enabled };
            _modList.Items.Add(item);
            count++;
        }
        Log(Localizer.T("已加载 {0} 个 mod（与部署同源，目录: {1}）", count, Program.ModsRoot));
        _status.Text = Localizer.T("共 {0} 个 mod", count);
        UpdateDeployState();
    }

    private int ApplyCheckedState()
    {
        int changed = 0;
        foreach (ListViewItem item in _modList.Items)
        {
            if (item.Tag is not ModRow row || row.Enabled == item.Checked) continue;
            try
            {
                var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(row.Path), null,
                    new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip })!.AsObject();
                node["enabled"] = item.Checked;
                File.WriteAllText(row.Path, node.ToJsonString(ModJsonOptions));
                row.Enabled = item.Checked;
                changed++;
                Log(Localizer.T("  启用状态: {0} -> {1}", row.Name, item.Checked ? "on" : "off"));
            }
            catch (Exception ex) { Log(Localizer.T("[警告] 写入失败 {0}: {1}", row.Path, ex.Message)); }
        }
        return changed;
    }

    /// <summary>单个 mod 的启用状态立即落盘（GUI 勾选/取消勾选时调用）。
    /// builder 侧 Mods.ScanMods 用的是 ModEntry.Enabled（缺省 true，JSON 的 enabled=false 才关闭），
    /// 所以勾选必须立刻写回 mod.json，不能拖到「部署」时才写 —— 用户很可能勾完直接用 Steam /
    /// 启动 bat / 游戏内「重新部署并重启」，那些路径都不会经过 ApplyCheckedState。</summary>
    private bool SaveModEnabled(ModRow row, bool enabled)
    {
        try
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(row.Path), null,
                new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip })!.AsObject();
            node["enabled"] = enabled;
            File.WriteAllText(row.Path, node.ToJsonString(ModJsonOptions));
            row.Enabled = enabled;
            Log(Localizer.T("  启用状态: {0} -> {1}", row.Name, enabled ? "on" : "off"));
            return true;
        }
        catch (Exception ex)
        {
            Log(Localizer.T("[警告] 写入失败 {0}: {1}", row.Path, ex.Message));
            return false;
        }
    }

    /// <summary>增强选项落盘：只写**规范键**（C4 P1-8：auto_skip / auto_chapter / skip_intro / debug_live /
    /// restore_on_exit + 两个高级键 auto_skip_delay / intro_delay，另加 schema_version=2）。
    /// 旧键（auto_skip_selector / skip_legend / skip_logo / auto_skip_intro）不再写入 —— 游戏侧
    /// ntl_config_load 会把规范键展开成旧键，向下兼容读；其它键（lang / base_mod / cache_max_mb…）由
    /// MergeConfig 原样保住；坏 JSON 时整份覆写（不弹错、不崩）。</summary>
    private static void SaveEnhanceConfig(string path, bool skipSelector, int autoChapter, bool skipIntro,
        int introDelay, bool debugLive, bool restoreOnExit)
    {
        MergeConfig(path, obj =>
        {
            obj["_comment"] = "Neutraled 增强开关（全部可选，默认关闭；改完重启游戏生效）";
            obj["schema_version"] = 2;
            obj["auto_skip"] = skipSelector;
            obj["auto_chapter"] = autoChapter;
            // UX-05：不再写 GUI 没有界面的键（auto_chapter_id 由游戏侧 ntl_autoskip 读取、外部/时间线章节用；
            // auto_skip_delay 是高级键）。旧实现每次「保存增强选项」都把玩家手写的值覆盖成 ""/90。
            obj["skip_intro"] = skipIntro;
            obj["intro_delay"] = introDelay;
            obj["debug_live"] = debugLive;
            // C4 P0-3：默认 false —— 以前 GUI 无条件在游戏退出后还原（A3 F09），想留着部署结果的玩家被强制还原。
            obj["restore_on_exit"] = restoreOnExit;
        }, overwriteOnBroken: true);
    }

    /// <summary>读取 config.json 里的 restore_on_exit（缺失/坏文件一律按 false = 不自动还原）。</summary>
    private static bool ReadRestoreOnExit()
    {
        try
        {
            var p = Path.Combine(Program.NeutraledRoot, "config.json");
            if (!File.Exists(p)) return false;
            using var doc = JsonDocument.Parse(File.ReadAllText(p),
                new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            return doc.RootElement.TryGetProperty("restore_on_exit", out var v) && v.ValueKind == JsonValueKind.True;
        }
        catch { return false; }
    }

    /// <summary>增强选项对话框（读写 Neutraled/config.json；全部可选，默认关闭）。</summary>
    private void ShowEnhanceDialog()
    {
        var cfgPath = Path.Combine(Program.NeutraledRoot, "config.json");
        bool skipSelector = false, skipIntro = false, debugLive = false, restoreOnExit = false;
        int autoChapter = 0, introDelay = 45;

        try
        {
            if (File.Exists(cfgPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(cfgPath),
                    new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                var r = doc.RootElement;
                bool B(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
                int I(string n) => r.TryGetProperty(n, out var v) && v.TryGetInt32(out var i) ? i : 0;
                // C4 P1-8：优先读规范键；旧键（auto_skip_selector / skip_legend / skip_logo / auto_skip_intro）只读兜底，
                // 免得玩家升级前写下的开关看起来被悄悄重置。
                skipSelector = B("auto_skip") || B("auto_skip_selector");
                skipIntro = B("skip_intro") || B("auto_skip_intro") || B("skip_legend") || B("skip_logo");
                debugLive = B("debug_live");
                restoreOnExit = B("restore_on_exit");
                autoChapter = I("auto_chapter");
                introDelay = I("intro_delay");
                if (introDelay <= 0) introDelay = 45;
            }
        }
        catch (Exception ex) { Log(Localizer.T("[警告] 读取 config.json 失败: {0}", ex.Message)); }

        using var dlg = new Form
        {
            Text = Localizer.T("Neutraled 增强选项（全部可选）"),
            Width = 460, Height = 424, StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false
        };
        var cbSelector = new CheckBox { Text = Localizer.T("跳过章节选择器（直接进入指定章节）"), Left = 16, Top = 16, Width = 410, Checked = skipSelector };
        var lblCh = new Label { Text = Localizer.T("自动进入章节:"), Left = 36, Top = 46, Width = 100 };
        var numCh = new NumericUpDown { Left = 140, Top = 43, Width = 70, Minimum = 0, Maximum = 20, Value = autoChapter };
        // C4 P1-8：原来「跳过传说」「跳过报幕」两个勾选在游戏侧是同一个开关（skip_intro），合成一个更直白；
        // 两个旧键仍会被读取（OR），不会因为合并而丢失玩家原来的选择。
        var cbIntro = new CheckBox { Text = Localizer.T("跳过章节内开场演出（“传说”+“DELTARUNE”报幕）"), Left = 16, Top = 82, Width = 410, Checked = skipIntro };
        var lblDelay = new Label { Text = Localizer.T("跳过前等待帧数:"), Left = 36, Top = 112, Width = 120 };
        var numDelay = new NumericUpDown { Left = 160, Top = 109, Width = 70, Minimum = 0, Maximum = 600, Value = introDelay };
        var cbDebug = new CheckBox { Text = Localizer.T("live 脚本调试日志（debug_on）"), Left = 16, Top = 142, Width = 410, Checked = debugLive };
        var cbRestore = new CheckBox
        {
            Text = Localizer.T("退出游戏后自动还原原版（只还原上次部署会话；默认关）"),
            Left = 16, Top = 172, Width = 420, Checked = restoreOnExit
        };
        var lblNote = new Label
        {
            Text = Localizer.T("提示：修改后需重启游戏生效；全部默认关闭，不影响原版体验。"),
            Left = 16, Top = 210, Width = 410, Height = 40, ForeColor = Color.DimGray
        };
        var btnSave = new Button { Text = Localizer.T("保存"), Left = 230, Top = 280, Width = 95, DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = Localizer.T("关闭"), Left = 335, Top = 280, Width = 95, DialogResult = DialogResult.Cancel };
        dlg.Controls.AddRange(new Control[] { cbSelector, lblCh, numCh, cbIntro, lblDelay, numDelay, cbDebug, cbRestore, lblNote, btnSave, btnCancel });
        dlg.AcceptButton = btnSave; dlg.CancelButton = btnCancel;
        // 行数变少：表单高度跟着收，避免底部留一大片空白
        dlg.Height = 384;

        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            SaveEnhanceConfig(cfgPath, cbSelector.Checked, (int)numCh.Value, cbIntro.Checked,
                (int)numDelay.Value, cbDebug.Checked, cbRestore.Checked);
            Log(Localizer.T("增强选项已保存: ") + cfgPath);
            _status.Text = Localizer.T("增强选项已保存（重启游戏生效）");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Neutraled"); }
    }

    /// <summary>选中 mod 时显示详情（路径 / 版本 / 包含的资源类型）。</summary>
    private void ShowModDetail()
    {
        if (_modList.SelectedItems.Count == 0) { _modDetail.Text = ""; return; }
        if (_modList.SelectedItems[0].Tag is not ModRow row) return;

        var dir = Path.GetDirectoryName(row.Path) ?? "";
        var sb = new System.Text.StringBuilder();
        var byLabel = Localizer.T("作者 {0}", row.Author);
        sb.AppendLine($"{row.Name}  v{row.Version}   {byLabel}   [{row.Chapter}]");
        sb.AppendLine($"ID:   {row.Id}");   // ID 是缩写，中英相同
        sb.AppendLine(Localizer.T("路径: {0}", dir));

        var parts = new List<string>();
        foreach (var (sub, label) in new[]
        {
            ("gml", Localizer.T("脚本")), ("patches", Localizer.T("补丁")),
            ("sprites", Localizer.T("精灵")), ("sounds", Localizer.T("声音")),
            ("fonts", Localizer.T("字体")), ("files", Localizer.T("外部文件"))
        })
            if (Directory.Exists(Path.Combine(dir, sub))) parts.Add(label);
        if (File.Exists(Path.Combine(dir, "ref", "data.win"))) parts.Add(Localizer.T("资源基底(data.win)"));
        sb.Append(Localizer.T("包含: ") + (parts.Count > 0 ? string.Join(" / ", parts) : Localizer.T("(仅 mod.json)")));
        _modDetail.Text = sb.ToString();
    }

    /// <summary>显示所选章节的部署状态。判定交给 builder --deploy-state（**按内容哈希**，
    /// 不再用文件长度：整包重写后只差几百字节也会被误判成「与原版一致」，见 A3 审计 F08）。
    /// 连续切换章节时用序号丢弃过期结果，避免慢查询把新章节的标签覆盖掉。</summary>
    private int _deployStateSeq;

    /// <summary>下拉框当前选中的部署目标（下拉框是 DropDownList，理论上恒有值；兜底取 root）。</summary>
    private string SelectedChapter() => _chapterCombo.SelectedItem?.ToString() ?? "root";

    /// <summary>需要「具体某一章」的功能：下拉选 all 时**不能**静默当成第 4 章（A3 F01），改成提示。
    /// 只有真正依赖单章上下文的功能才用它（导入/导出/冲突检查/差异层/在线下载）。</summary>
    private bool RequireSingleChapter(string chapter, string feature)
    {
        // C4 P1-12：判定统一在 UiLogic（headless plan 用同一套规则），不静默把 all 当第 4 章。
        if (UiLogic.ChapterAllowed(chapter, UiLogic.ActionKind.Other).Length > 0) return true;
        MessageBox.Show(Localizer.T("{0}需要选择具体章节（不能选 all）。", feature), "Neutraled");
        return false;
    }

    /// <summary>GUI-L02：选 all 时的聚合部署状态（root + chapter1..5 共 6 个槽位）。
    /// 数据来自 builder 的 --deploy-state --chapter all --json（与单章同一判定：按内容哈希）。</summary>
    private async Task UpdateDeployStateAllAsync()
    {
        var seq = ++_deployStateSeq;
        try
        {
            _deployState.ForeColor = Color.DimGray;
            _deployState.Text = Localizer.T("全部章节: 查询中…");
            var sb = new System.Text.StringBuilder();
            var rc = await Program.RunBuilderAsync("--deploy-state --chapter all --json", l => sb.AppendLine(l));
            if (seq != _deployStateSeq) return;
            if (rc != 0) { _deployState.Text = Localizer.T("全部章节: 状态未知"); _deployState.ForeColor = Color.DarkOrange; return; }
            string? json = null;
            foreach (var line in sb.ToString().Split('\n'))
            {
                var t = line.Trim();
                if (t.StartsWith("{") && t.EndsWith("}")) json = t;
            }
            if (json == null) { _deployState.Text = Localizer.T("全部章节: 状态未知"); _deployState.ForeColor = Color.DarkOrange; return; }
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("slots", out var slots)) { _deployState.Text = Localizer.T("全部章节: 状态未知"); return; }
            int total = 0, deployed = 0, baseline = 0, missing = 0;
            foreach (var slot in slots.EnumerateArray())
            {
                total++;
                switch (slot.TryGetProperty("state", out var st) ? st.GetString() : null)
                {
                    case "deployed": deployed++; break;
                    case "baseline": baseline++; break;
                    case "missing": missing++; break;
                }
            }
            _deployState.Text = missing > 0
                ? Localizer.T("全部 {0} 个目标: 已部署 {1}，原版 {2}，缺失 {3}", total, deployed, baseline, missing)
                : Localizer.T("全部 {0} 个目标: 已部署 {1}，原版 {2}", total, deployed, baseline);
            _deployState.ForeColor = missing > 0 ? Color.Firebrick : (deployed == total ? Color.SeaGreen : Color.DarkOrange);
            _deployState.Tag = "";
        }
        catch
        {
            if (seq == _deployStateSeq) { _deployState.Text = Localizer.T("全部章节: 状态未知"); _deployState.ForeColor = Color.DarkOrange; }
        }
    }

    private async void UpdateDeployState()
    {
        var chapter = SelectedChapter();
        if (chapter == "all") { await UpdateDeployStateAllAsync(); return; }
        var seq = ++_deployStateSeq;
        try
        {
            _deployState.ForeColor = Color.DimGray;
            _deployState.Text = Localizer.T("查询部署状态…");

            var sb = new System.Text.StringBuilder();
            var rc = await Program.RunBuilderAsync(
                "--deploy-state --chapter " + chapter + " --json",
                l => sb.AppendLine(l));
            if (seq != _deployStateSeq) return;   // 已切到别的章节，丢弃过期结果

            if (rc != 0) { _deployState.Text = chapter + ": " + Localizer.T("状态未知"); _deployState.ForeColor = Color.DarkOrange; return; }

            // builder 在 JSON 之前还会打印「游戏根: …」；只取最后一行以 { 开头的 JSON。
            string? json = null;
            foreach (var line in sb.ToString().Split('\n'))
            {
                var t = line.Trim();
                if (t.StartsWith("{") && t.EndsWith("}")) json = t;
            }
            if (json == null) { _deployState.Text = chapter + ": " + Localizer.T("状态未知"); _deployState.ForeColor = Color.DarkOrange; return; }

            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var slot = doc.RootElement.GetProperty("slots")[0];
            var state = slot.GetProperty("state").GetString() ?? "unknown";
            var reason = slot.TryGetProperty("reason", out var rEl) ? rEl.GetString() ?? "" : "";
            var color = state switch
            {
                "baseline" => Color.DimGray,
                "deployed" => Color.SeaGreen,
                "missing" => Color.Firebrick,
                _ => Color.DarkOrange,
            };
            var label = state switch
            {
                "baseline" => Localizer.T("未部署（原版）"),
                "deployed" => Localizer.T("已部署"),
                "missing" => Localizer.T("（缺失）"),
                _ => Localizer.T("状态未知"),
            };
            _deployState.Text = chapter + ": " + label;
            _deployState.ForeColor = color;
            // 悬停看原因：未知/异常状态时最有用（例如「基线未登记」）
            _deployState.Tag = reason;
            var tip = new ToolTip();
            tip.SetToolTip(_deployState, reason);
        }
        catch
        {
            if (seq == _deployStateSeq) { _deployState.Text = chapter + ": " + Localizer.T("状态未知"); _deployState.ForeColor = Color.DarkOrange; }
        }
    }

    /// <summary>跑 builder，并把拼好的完整命令行打进日志（便于核对参数确实带上了）。</summary>
    private async Task<int> RunBuilderCmdAsync(string args)
    {
        Log("> ntl-gui.exe " + args);
        return await Program.RunBuilderAsync(args, Log);
    }

    /// <summary>
    /// 部署/启动前的更新检测（只读跑 --update-check）。
    /// 退出码 2 = 检测到游戏更新 / 新章节 / 整包基底漂移 ⇒ 只提示，由用户在弹窗里决定；
    /// **绝不自动改本体**：只有用户点「采纳新基线并继续」时才显式跑 --adopt-current --yes。
    /// 详见 docs/UPDATE.md。
    /// </summary>
    private async Task<bool> ConfirmUpdatesAsync(string action)
    {
        var report = new System.Text.StringBuilder();
        var rc = await Program.RunBuilderAsync("--update-check", l => { report.AppendLine(l); Log(l); });

        if (rc == 0) return true;   // 没检测到更新：直接放行

        if (rc != 2)
        {
            // rc == 3（检测本身失败）/ rc == -1（找不到 builder）：交给用户决定是否继续
            return MessageBox.Show(this,
                Localizer.T("更新检测失败（退出码 {0}），详见日志。\n\n仍要继续{1}吗？", rc, action),
                "Neutraled", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK;
        }

        bool adopt;
        using (var dlg = new Form
        {
            Text = Localizer.T("检测到游戏更新"),
            Width = 760, Height = 500, StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.Sizable, MinimizeBox = false, MaximizeBox = false
        })
        {
            var box = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
                Dock = DockStyle.Fill, Font = new Font("Consolas", 9), WordWrap = false,
                Text = report.ToString()
            };
            var bar = new Panel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(8) };
            var btnAdopt = new Button { Text = Localizer.T("采纳新基线并继续"), Left = 8, Top = 9, Width = 160, Height = 28, DialogResult = DialogResult.Yes };
            var btnGo = new Button { Text = Localizer.T("直接继续"), Left = 176, Top = 9, Width = 100, Height = 28, DialogResult = DialogResult.No };
            var btnCancel = new Button { Text = Localizer.T("取消"), Left = 284, Top = 9, Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
            bar.Controls.AddRange(new Control[] { btnAdopt, btnGo, btnCancel });

            var info = new Label
            {
                Dock = DockStyle.Top, Height = 118, Padding = new Padding(10),
                Text = Localizer.T("检测到游戏更新 / 新章节 / 整包基底漂移（详见下方报告）。\n\n"
                    + "① 采纳新基线并继续：把当前原版采纳为新基线，旧备份移进 backup/history/<buildid>/（不删）；"
                    + "新章节同时登记为已确认，然后继续本次{0}。\n"
                    + "② 直接继续：不做采纳，本次{0}可能被检测拦下（拦下时不会改动游戏目录，日志里有处置步骤）。", action)
            };

            dlg.Controls.Add(box);
            dlg.Controls.Add(bar);
            dlg.Controls.Add(info);
            dlg.AcceptButton = btnAdopt;
            dlg.CancelButton = btnCancel;

            var dr = dlg.ShowDialog(this);
            if (dr == DialogResult.Cancel) return false;
            adopt = dr == DialogResult.Yes;
        }

        if (adopt)
        {
            Log("> ntl-gui.exe --adopt-current --yes");
            var arc = await Program.RunBuilderAsync("--adopt-current --yes", Log);
            if (arc != 0)
            {
                MessageBox.Show(this,
                    Localizer.T("采纳新基线失败（退出码 {0}），本次已取消，游戏目录没有改动。详见日志。", arc),
                    "Neutraled", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }
        return true;
    }

    private async Task DeployAllAsync()
    {
        if (!await ConfirmUpdatesAsync(Localizer.T("部署全部章节"))) return;
        SetBusy(true, Localizer.T("部署全部章节中..."));
        ApplyCheckedState();
        var rc = await RunBuilderCmdAsync(DeployOptions.AppendFastDeploy(UiLogic.BuildArgs(UiLogic.ActionKind.Deploy, "all")));
        SetBusy(false, rc == 0 ? Localizer.T("部署完成") : Localizer.T("部署失败 (exit={0})", rc));
        UpdateDeployState();
        if (rc == 0) RefreshAlerts();
    }

    private void ExportLog()
    {
        using var sfd = new SaveFileDialog { Filter = Localizer.T("日志 (*.txt)|*.txt"), FileName = "neutraled-log.txt" };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(sfd.FileName, _logBox.Text);
            Log(Localizer.T("日志已导出: {0}", sfd.FileName));
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Neutraled"); }
    }

    /// <summary>拖放文件/目录 → 导入为 mod。</summary>
    private async Task ImportPathsAsync(string[] paths)
    {
        var input = paths[0];
        if (paths.Length > 1) Log(Localizer.T("（拖入了 {0} 项，仅处理第一个）", paths.Length));
        var defName = Directory.Exists(input)
            ? Path.GetFileName(input.TrimEnd('\\', '/'))
            : Path.GetFileNameWithoutExtension(input);

        using var nameDlg = new Form { Text = Localizer.T("导入旧 mod"), Width = 460, Height = 200, StartPosition = FormStartPosition.CenterParent };
        var lblN = new Label { Text = Localizer.T("mod 名称"), Left = 12, Top = 14, Width = 120 };
        var tbName = new TextBox { Left = 12, Top = 34, Width = 410, Text = defName };
        var lblA = new Label { Text = Localizer.T("作者"), Left = 12, Top = 68, Width = 120 };
        var tbAuthor = new TextBox { Left = 12, Top = 88, Width = 410, Text = "unknown" };
        var ok = new Button { Text = Localizer.T("确定"), Left = 330, Top = 122, Width = 90, DialogResult = DialogResult.OK };
        nameDlg.Controls.AddRange(new Control[] { lblN, tbName, lblA, tbAuthor, ok });
        nameDlg.AcceptButton = ok;
        if (nameDlg.ShowDialog(this) != DialogResult.OK) return;

        await RunImportAsync(input, tbName.Text, tbAuthor.Text);
    }

    private async Task RunImportAsync(string input, string modName, string author)
    {
        var chapter = SelectedChapter();
        if (!RequireSingleChapter(chapter, Localizer.T("该功能"))) return;
        SetBusy(true, Localizer.T("转换中..."));
        Log($"===== {input} -> {modName} =====");
        // 用 --import-mod（自动识别 6 种包形态：Modding.xml / Deltamod / mod_config.json /
        // 裸 xdelta / 裸 data.win / Kristal / 原生 mod）—— 以前写死 --import-delta，只能吃单项 xdelta。
        var rc = await Program.RunBuilderAsync(
            UiLogic.BuildArgs(UiLogic.ActionKind.ImportMod, chapter, input, modName, author), Log);
        SetBusy(false, rc == 0 ? Localizer.T("转换完成") : Localizer.T("转换失败（详见日志）"));
        if (rc == 0) await RefreshModsAsync();
    }

    private async Task DeployAsync(bool launch)
    {
        CancelAutoRestore();   // 重新部署/启动时取消上一次「退出后自动还原」的等待，避免还原刚部署好的产物
        var chapter = SelectedChapter();
        if (!await ConfirmUpdatesAsync(launch ? Localizer.T("启动") : Localizer.T("部署"))) return;
        SetBusy(true, launch ? Localizer.T("准备启动（查缓存）...") : Localizer.T("部署中..."));
        Log($"===== {chapter} =====");
        ApplyCheckedState();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        int rc;
        if (launch)
        {
            // 走缓存路径：命中则硬链接复用（秒开），未命中则部署 + 存缓存，最后由 Steam 拉起
            rc = await RunBuilderCmdAsync(DeployOptions.AppendFastDeploy(UiLogic.BuildArgs(UiLogic.ActionKind.Launch, chapter)));
            // 起后台守候：游戏里进 Kristal 章节 → 游戏写请求并退出 → 守候进程拉起 Kristal
            if (rc == 0) Program.StartExternalWatcher();
            sw.Stop();
            var el = $"{sw.Elapsed.TotalSeconds:F1}s";
            SetBusy(false, rc == 0 ? $"{Localizer.T("已启动游戏（通过 Steam）")} ({el})" : Localizer.T("启动失败 (exit={0})", rc));
            if (rc == 0) WatchAndRestore(chapter);
            if (rc == 0) RefreshAlerts();
            // 记录当前生效的配置签名，用于"配置已变更"提示
            _launchedSignature = await QuerySignatureAsync(chapter);
        }
        else
        {
            rc = await RunBuilderCmdAsync(DeployOptions.AppendFastDeploy(UiLogic.BuildArgs(UiLogic.ActionKind.Deploy, chapter)));
            sw.Stop();
            var elapsed = $"{sw.Elapsed.TotalSeconds:F1}s";
            SetBusy(false, rc == 0 ? $"{Localizer.T("部署完成")} ({elapsed})" : Localizer.T("部署失败 (exit={0})", rc));
            if (rc == 0) RefreshAlerts();
        }
    }

    /// <summary>缓存管理对话框：查看/清空部署缓存、设置上限。</summary>
    private async Task ShowCacheDialogAsync()
    {
        var dlg = new Form
        {
            Text = Localizer.T("部署缓存管理"),
            Width = 760,
            Height = 480,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.Sizable
        };
        var box = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9),
            WordWrap = false
        };
        var panel = new Panel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(8) };
        var btnClear = new Button { Text = Localizer.T("清空缓存"), Left = 8, Top = 8, Width = 100, Height = 28 };
        var btnRefresh = new Button { Text = Localizer.T("刷新"), Left = 116, Top = 8, Width = 80, Height = 28 };
        var lblMax = new Label { Text = Localizer.T("上限(MB)"), Left = 210, Top = 14, Width = 70, AutoSize = true };
        var txtMax = new TextBox { Left = 286, Top = 10, Width = 80, Text = "4096" };
        var btnSetMax = new Button { Text = Localizer.T("设置"), Left = 374, Top = 8, Width = 70, Height = 28 };
        panel.Controls.AddRange(new Control[] { btnClear, btnRefresh, lblMax, txtMax, btnSetMax });

        async Task ReloadAsync()
        {
            var sb = new System.Text.StringBuilder();
            await Program.RunBuilderAsync("--cache-list", s => sb.AppendLine(s));
            box.Text = sb.ToString();
        }
        btnRefresh.Click += async (_, _) => await ReloadAsync();
        btnClear.Click += async (_, _) =>
        {
            if (MessageBox.Show(Localizer.T("确定清空所有部署缓存？"), Localizer.T("确认"),
                MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            await Program.RunBuilderAsync("--cache-clear", Log);
            await ReloadAsync();
        };
        btnSetMax.Click += async (_, _) =>
        {
            // GUI-M07 / 裁决 s：只写 config.cache_max_mb，绝不部署、不启动游戏。
            // 旧实现走 --launch <章节> --cache-max（设完上限继续部署并由 Steam 拉起游戏）。
            if (!int.TryParse((txtMax.Text ?? "").Trim(), out var mb) || mb <= 0 || mb > 1024 * 1024)
            {
                MessageBox.Show(this, Localizer.T("上限必须是正整数（MB），例如 4096。"), "Neutraled",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var rc = await Program.RunBuilderAsync($"--cache-max-set {mb}", Log);
            if (rc != 0) Log(Localizer.T("[缓存] 设置上限失败（退出码 {0}）", rc));
            await ReloadAsync();
        };

        dlg.Controls.Add(box);
        dlg.Controls.Add(panel);
        await ReloadAsync();
        dlg.ShowDialog(this);
    }

    /// <summary>当前配置的缓存签名（用于检测"配置已变更"）。</summary>
    private string _launchedSignature = "";

    private async Task<string> QuerySignatureAsync(string chapter)
    {
        try
        {
            var outp = new System.Text.StringBuilder();
            await Program.RunBuilderAsync($"--cache-applied --chapter {chapter}", s => outp.AppendLine(s));
            var txt = outp.ToString();
            var i = txt.IndexOf("\"signature\":\"", StringComparison.Ordinal);
            if (i < 0) return "";
            var j = txt.IndexOf('"', i + 13);
            return j > i ? txt.Substring(i + 13, j - i - 13) : "";
        }
        catch { return ""; }
    }

    /// <summary>配置变更时提示（在勾选变化时调用）。</summary>
    private async void NotifyConfigChanged()
    {
        if (string.IsNullOrEmpty(_launchedSignature)) return;
        var chapter = SelectedChapter();
        var now = await QuerySignatureAsync(chapter);
        if (!string.IsNullOrEmpty(now) && now != _launchedSignature)
            _status.Text = Localizer.T("配置已变更，重启游戏后生效");
    }

    /// <summary>取消上一次「退出后自动还原」的等待（重新部署/手动还原时调用）。</summary>
    private void CancelAutoRestore()
    {
        try { _autoRestoreCts?.Cancel(); } catch { }
        _autoRestoreCts = null;
    }

    /// <summary>在 UI 线程上写日志（后台任务里用）。</summary>
    private void LogLine(string msg)
    {
        if (InvokeRequired)
        {
            try { BeginInvoke(() => Log(msg)); } catch { }
        }
        else Log(msg);
    }

    /// <summary>退出游戏后按**部署会话**还原（C4 P0-3）。
    /// 旧实现（A3 F09）：无开关、无条件等候、只还原下拉框那一个章节、all 直接不还原。
    /// 现在：只有 config.json 的 restore_on_exit=true 才等；游戏退出后跑 --restore-session，
    /// 会话由 --launch 在启动前写下（root + 本次章节，或 all 的全部章节）；可取消。</summary>
    private void WatchAndRestore(string chapter)
    {
        if (!ReadRestoreOnExit())
        {
            _status.Text = Localizer.T("已启动游戏（退出后不自动还原；可在增强选项开启）");
            return;
        }
        CancelAutoRestore();
        var cts = new CancellationTokenSource();
        _autoRestoreCts = cts;
        var token = cts.Token;
        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(20000, token);
                for (int i = 0; i < 3600 && !token.IsCancellationRequested; i++)   // 最多等 3 小时
                {
                    if (Process.GetProcessesByName("DELTARUNE").Length == 0) break;
                    await Task.Delay(3000, token);
                }
                await Task.Delay(2000, token);
                LogLine(Localizer.T("游戏退出，正在按部署会话还原原版..."));
                var rc = await Program.RunBuilderAsync("--restore-session", Log);
                var msg = rc == 0 ? Localizer.T("已自动恢复原版") : Localizer.T("自动还原失败（详见日志）");
                LogLine(msg);
                if (InvokeRequired) BeginInvoke(() => _status.Text = msg);
                else _status.Text = msg;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { LogLine("[auto-restore] " + ex.Message); }
            finally { if (ReferenceEquals(_autoRestoreCts, cts)) _autoRestoreCts = null; }
        });
    }

    /// <summary>启动时检测上次 --launch 留下的部署会话（说明游戏文件仍是产物、没还原）。</summary>
    private void CheckDeploySessionBanner()
    {
        try
        {
            var p = Path.Combine(Program.NeutraledRoot, "deploy-session.json");
            if (!File.Exists(p)) return;
            var summary = ReadSessionSummary(p);
            Log(Localizer.T("[提示] 检测到上次部署会话：{0}（游戏文件仍是 mod 产物；点【恢复原版】或运行 --restore-session 还原）", summary));
            _status.Text = Localizer.T("上次部署未还原：{0}", summary);
        }
        catch (Exception ex) { Log("[session] " + ex.Message); }
    }

    /// <summary>读部署会话的 Chapters（builder 用 PascalCase 序列化）拼成摘要文本。</summary>
    private static string ReadSessionSummary(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path),
                new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (doc.RootElement.TryGetProperty("Chapters", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                var list = new System.Collections.Generic.List<string>();
                foreach (var it in arr.EnumerateArray())
                    if (it.ValueKind == JsonValueKind.String) list.Add(it.GetString() ?? "");
                list.RemoveAll(s => s.Length == 0);
                if (list.Count > 0) return string.Join("/", list);
            }
        }
        catch { }
        return Localizer.T("未知");
    }

    private async Task RestoreAsync()
    {
        CancelAutoRestore();
        var sessionPath = Path.Combine(Program.NeutraledRoot, "deploy-session.json");
        if (File.Exists(sessionPath))
        {
            // 有部署会话时优先按会话还原（可能包含 root + 本次章节；旧实现只能勉强还原一章还会漏 root）
            var summary = ReadSessionSummary(sessionPath);
            if (MessageBox.Show(Localizer.T("检测到上次部署会话（{0}），要按会话还原这些目标吗？", summary),
                    "Neutraled", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            SetBusy(true, Localizer.T("恢复中..."));
            var rcSession = await Program.RunBuilderAsync("--restore-session", Log);
            SetBusy(false, rcSession == 0 ? Localizer.T("已恢复原版") : Localizer.T("恢复失败"));
            UpdateDeployState();
            return;
        }
        var chapter = SelectedChapter();
        if (chapter == "all")
        {
            // UX-27：builder 的 --restore-chapter 本来就支持 all（逐槽还原 root + chapter1..5），
            // 旧 GUI 直接拒绝并让玩家逐章点，纯属没有接线。
            if (MessageBox.Show(Localizer.T("把全部目标（root + chapter1..5）恢复到原版？"), "Neutraled",
                    MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            SetBusy(true, Localizer.T("恢复中..."));
            var rcAll = await Program.RunBuilderAsync("--restore-chapter all", Log);
            SetBusy(false, rcAll == 0 ? Localizer.T("已恢复原版") : Localizer.T("恢复失败"));
            UpdateDeployState();
            return;
        }
        if (MessageBox.Show(Localizer.T("把 {0} 恢复到原版？", chapter), "Neutraled", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
        SetBusy(true, Localizer.T("恢复中..."));
        var rc = await Program.RunBuilderAsync($"--restore-chapter {chapter}", Log);
        SetBusy(false, rc == 0 ? Localizer.T("已恢复原版") : Localizer.T("恢复失败"));
        UpdateDeployState();
    }

    private async Task ImportAsync()
    {
        string input;
        using (var pick = new Form { Text = Localizer.T("导入旧 mod"), Width = 460, Height = 170, StartPosition = FormStartPosition.CenterParent })
        {
            var lbl = new Label { Text = Localizer.T("选择要转换的旧 mod（文件或整个补丁目录）"), Left = 12, Top = 14, Width = 420 };
            var bFile = new Button { Text = Localizer.T("选择文件"), Left = 12, Top = 46, Width = 130, Height = 32, DialogResult = DialogResult.Yes };
            var bDir = new Button { Text = Localizer.T("选择目录"), Left = 152, Top = 46, Width = 130, Height = 32, DialogResult = DialogResult.No };
            var bCancel = new Button { Text = Localizer.T("关闭"), Left = 340, Top = 46, Width = 90, Height = 32, DialogResult = DialogResult.Cancel };
            pick.Controls.AddRange(new Control[] { lbl, bFile, bDir, bCancel });
            pick.AcceptButton = bFile; pick.CancelButton = bCancel;
            var res = pick.ShowDialog(this);
            if (res == DialogResult.Yes)
            {
                using var ofd = new OpenFileDialog
                {
                    Filter = Localizer.T("旧 mod (*.zip;*.xdelta;*.win)|*.zip;*.xdelta;*.win|所有文件|*.*"),
                    Title = Localizer.T("选择要转换的旧 mod")
                };
                if (ofd.ShowDialog(this) != DialogResult.OK) return;
                input = ofd.FileName;
            }
            else if (res == DialogResult.No)
            {
                using var fbd = new FolderBrowserDialog { Description = Localizer.T("选择补丁目录（含 chapterN.xdelta）") };
                if (fbd.ShowDialog(this) != DialogResult.OK) return;
                input = fbd.SelectedPath;
            }
            else return;
        }

        var defName = Directory.Exists(input) ? Path.GetFileName(input.TrimEnd('\\', '/')) : Path.GetFileNameWithoutExtension(input);
        using var nameDlg = new Form { Text = Localizer.T("mod 名称"), Width = 460, Height = 200, StartPosition = FormStartPosition.CenterParent };
        var lblN = new Label { Text = Localizer.T("mod 名称"), Left = 12, Top = 14, Width = 120 };
        var tbName = new TextBox { Left = 12, Top = 34, Width = 410, Text = defName };
        var lblA = new Label { Text = Localizer.T("作者"), Left = 12, Top = 68, Width = 120 };
        var tbAuthor = new TextBox { Left = 12, Top = 88, Width = 410, Text = "unknown" };
        var ok2 = new Button { Text = Localizer.T("确定"), Left = 330, Top = 122, Width = 90, DialogResult = DialogResult.OK };
        nameDlg.Controls.AddRange(new Control[] { lblN, tbName, lblA, tbAuthor, ok2 });
        nameDlg.AcceptButton = ok2;
        if (nameDlg.ShowDialog(this) != DialogResult.OK) return;

        await RunImportAsync(input, tbName.Text, tbAuthor.Text);
    }

    // ==================== 工具箱 ====================
    private void ShowToolboxDialog()
    {
        using var dlg = BuildToolboxDialog();
        dlg.ShowDialog(this);
    }

    /// <summary>构造工具箱对话框（不显示）——拆出来是为了能结构自检（--check-toolbox-layout）。</summary>
    internal Form BuildToolboxDialog()
    {
        var dlg = new Form { Text = Localizer.T("工具箱"), Width = 640, Height = 520, StartPosition = FormStartPosition.CenterParent };
        int y = 12;
        Button Add(string text, string hint)
        {
            var b = new Button { Text = text, Left = 12, Top = y, Width = 600, Height = 40, TextAlign = ContentAlignment.MiddleLeft };
            var h = new Label { Text = hint, Left = 24, Top = y + 42, Width = 580, Height = 16, ForeColor = Color.DimGray };
            dlg.Controls.Add(b); dlg.Controls.Add(h);
            // GUI-M08：说明文案的行数随语言/字体变化（英文常折成两行，16px 会裁掉下半行）。
            // 用与 --check-toolbox-layout 相同的度量方式撑高，并按实际高度推进 y，避免文字压到下一条按钮上。
            h.Height = Math.Max(16, TextRenderer.MeasureText(h.Text, h.Font,
                new Size(h.Width, int.MaxValue), TextFormatFlags.WordBreak).Height + 4);
            y += 42 + h.Height + 8;
            return b;
        }
        // v1.1.0：地图编辑器和 Studio 都并进本 exe 了 —— 这里是最容易找到的入口（用户只需要打开这一个 exe）。
        Add(Localizer.T("地图 / 战斗 / 剧情编辑器"), Localizer.T("直接编辑原版 data：房间、对话、战斗、剧情；保存即生成 ntl mod（不写原版 data）"))
            .Click += (_, _) => LaunchTool("mapedit");
        Add(Localizer.T("Studio 脚本编辑器"), Localizer.T("编辑 Lua / NTL live 脚本，带热重载、日志与部署"))
            .Click += (_, _) => LaunchTool("studio");
        Add(Localizer.T("Kristal 宿主合并"), Localizer.T("项目 + 插件 → 一个 mod（插件型 mod 缺宿主时用）"))
            .Click += async (_, _) => await KristalMergeAsync(dlg);
        Add(Localizer.T("制作 B 面存档"), Localizer.T("任意章节直接开 B 面，不用从第二章重打"))
            .Click += async (_, _) => await MakeBSideAsync(dlg);
        Add(Localizer.T("导入 B 面存档"), Localizer.T("把一份真实 B 面存档导入并直接加载（名字 ≤12 个字母写进存档第 1 行）"))
            .Click += async (_, _) => await ImportBSideAsync(dlg);
        Add(Localizer.T("导出资源包"), Localizer.T("data.win → 精灵/声音/字体，可叠加到任意基底"))
            .Click += async (_, _) => await ExportPacksAsync(dlg);
        Add(Localizer.T("检查 mod 冲突"), Localizer.T("只查不部署（退出码 2 = 有冲突），写 conflicts.json"))
            .Click += async (_, _) => await ConflictsCheckAsync(dlg);
        Add(Localizer.T("源码级差异层"), Localizer.T("整包 mod → 可叠加 patch 层（反编译真实改动，绕过索引问题）"))
            .Click += async (_, _) => await LayerFromBaseAsync(dlg);
        // C4 P1-13：配置档原来只有 CLI / Web 有入口，GUI 里完全找不到（A3 审计「文档说有 GUI 入口」不符）。
        // 这里给一个**只读**入口：数据与 CLI 同源（--profile-list --json），避免 GUI 自写一套扫描规则。
        Add(Localizer.T("配置档（只读）"), Localizer.T("看有哪些配置档、各开了几个 mod。只读：套用请用 CLI --profile-use 或 Web 界面"))
            .Click += async (_, _) =>
            {
                var (rows, err) = await ProfilesDialog.LoadAsync();
                ProfilesDialog.Show(dlg, rows, err);
            };
        // ---- 守候进程（Kristal 等"外部章节"的接管者）----
        //   游戏内没有启动进程的内置函数：选中外部章节时游戏只能写一个 launch-request.json，
        //   等外部进程去把它拉起来（见 builder/WatchAutostart.cs）。守候没在跑 → 只能给提示。
        Add(Localizer.T("守候进程：状态 / 立即启动"), Localizer.T("看守候是否在跑、自启装了没有；没在跑就立刻起一个（选中 Kristal 章节需要它）"))
            .Click += async (_, _) =>
            {
                await Program.RunBuilderAsync("--watch-autostart status", Log);
                await Program.RunBuilderAsync("--ensure-watcher", Log);
            };
        Add(Localizer.T("守候进程自启：开启"), Localizer.T("登录 / 解锁 / 每 1 分钟兜底自动拉起守候（计划任务 + 开机启动项，都不需要管理员权限）"))
            .Click += async (_, _) => await Program.RunBuilderAsync("--watch-autostart on", Log);
        Add(Localizer.T("守候进程自启：关闭"), Localizer.T("移除计划任务与开机启动项（不会杀掉当前正在跑的守候）"))
            .Click += async (_, _) => await Program.RunBuilderAsync("--watch-autostart off", Log);

        // ---- 部署加速档（可选；代价就写在开关下面，不藏）----
        var cbFast = new CheckBox
        {
            Text = Localizer.T("部署加速档（跳过输入重定向）"),
            Left = 12, Top = y + 6, Width = 600, Height = 22,
            Checked = DeployOptions.FastDeploy
        };
        var lblFastCost = new Label
        {
            Text = Localizer.T("开启后部署会附加 --fast-deploy：跳过「输入函数重定向」，部署更快。") + Environment.NewLine +
                   Localizer.T("代价：章节内「控制台输入屏蔽」失效 —— 控制台仍能打开，但游戏自身的 keyboard_check_direct 仍会读到按键。") + Environment.NewLine +
                   Localizer.T("chapter5 实测约省 20 秒（命中输入扫描缓存后约省 1 秒）。默认关闭，随时可改回。") + Environment.NewLine +
                   Localizer.T("缓存签名已包含这个开关（Cache.cs 的 fast 位）：切换后下一次部署会自动重做，不需要清缓存。"),
            Left = 30, Top = y + 30, Width = 586, AutoSize = false, ForeColor = Color.DimGray
        };
        cbFast.CheckedChanged += async (_, _) =>
        {
            if (_suppressFastToggle) return;
            if (cbFast.Checked && !_fastDeployConfirmed)
            {
                // UX-31：代价写清楚，首次勾选必须显式确认；取消则不改变设置。
                var ok = MessageBox.Show(dlg,
                    Localizer.T("部署加速档会跳过「输入函数重定向」：部署更快，但章节内『控制台输入屏蔽』失效 —— 控制台仍能打开，游戏自身的 keyboard_check_direct 仍会读到按键。\n\n确定开启吗？"),
                    Localizer.T("Neutraled — 确认开启部署加速档"),
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK;
                if (!ok)
                {
                    _suppressFastToggle = true;
                    cbFast.Checked = false;
                    _suppressFastToggle = false;
                    return;
                }
                _fastDeployConfirmed = true;
            }
            DeployOptions.FastDeploy = cbFast.Checked;
            DeployOptions.Save(Program.NeutraledRoot);
            Log(cbFast.Checked
                ? Localizer.T("部署加速档：已开启（之后部署会附加 --fast-deploy，控制台输入屏蔽失效）")
                : Localizer.T("部署加速档：已关闭（之后部署会做完整的输入函数重定向）"));
            // C4 P1-10：缓存签名已包含 fast 位（builder/Cache.cs 的 SignatureRaw），切换挡位后下一次部署
            // 自然 miss 并重建 —— 旧版本「自动清空缓存 / 弹窗让你手清」的补丁分支已删除（它会误伤其它章节缓存）。
            Log(Localizer.T("部署加速档：已保存；下一次部署会按新挡位重建（缓存签名包含该开关，无需手动清缓存）"));
        };
        dlg.Controls.AddRange(new Control[] { cbFast, lblFastCost });

        // 代价说明按实际字体度量高度（中英文行数不同），别让文案被裁掉；关闭按钮和对话框跟着长。
        lblFastCost.Height = TextRenderer.MeasureText(lblFastCost.Text, lblFastCost.Font,
            new Size(lblFastCost.Width, int.MaxValue), TextFormatFlags.WordBreak).Height + 4;

        var closeTop = lblFastCost.Bottom + 14;
        var close = new Button { Text = Localizer.T("关闭"), Left = 524, Top = closeTop, Width = 92, Height = 30, DialogResult = DialogResult.Cancel };
        dlg.Controls.Add(close); dlg.CancelButton = close;
        dlg.Height = closeTop + close.Height + 58;   // 标题栏 + 边框 + 底部留白
        return dlg;
    }

    private async Task KristalMergeAsync(Form owner)
    {
        using var fbd = new FolderBrowserDialog { Description = Localizer.T("选择 Kristal 宿主项目目录（含 mod.json）") };
        if (fbd.ShowDialog(owner) != DialogResult.OK) return;
        var host = fbd.SelectedPath;

        using var dlg = new Form { Text = Localizer.T("Kristal 宿主合并"), Width = 680, Height = 440, StartPosition = FormStartPosition.CenterParent };
        dlg.Controls.Add(new Label { Text = Localizer.T("宿主: ") + host, Left = 12, Top = 12, Width = 640 });
        var list = new ListBox { Left = 12, Top = 40, Width = 640, Height = 250 };
        dlg.Controls.Add(list);
        var add = new Button { Text = Localizer.T("添加插件目录..."), Left = 12, Top = 300, Width = 150 };
        var del = new Button { Text = Localizer.T("移除选中"), Left = 170, Top = 300, Width = 100 };
        var ok = new Button { Text = Localizer.T("开始合并转换"), Left = 490, Top = 300, Width = 162, DialogResult = DialogResult.OK };
        dlg.Controls.AddRange(new Control[] { add, del, ok });
        add.Click += (_, _) =>
        {
            using var fb = new FolderBrowserDialog { Description = Localizer.T("选择插件 mod 目录（含 scripts/ 或 assets/）") };
            if (fb.ShowDialog(dlg) == DialogResult.OK && !list.Items.Contains(fb.SelectedPath)) list.Items.Add(fb.SelectedPath);
        };
        del.Click += (_, _) => { if (list.SelectedIndex >= 0) list.Items.RemoveAt(list.SelectedIndex); };
        dlg.AcceptButton = ok;
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (list.Items.Count == 0) { MessageBox.Show(this, Localizer.T("至少添加一个插件目录"), "Neutraled"); return; }

        var args = new System.Text.StringBuilder();
        args.Append("--kristal-merge \"").Append(host).Append('"');
        foreach (var it in list.Items) args.Append(" --with \"").Append(it).Append('"');
        SetBusy(true, Localizer.T("合并转换中..."));
        var rc = await Program.RunBuilderAsync(args.ToString(), Log);
        SetBusy(false, rc == 0 ? Localizer.T("完成") : Localizer.T("失败（详见日志）"));
        if (rc == 0) await RefreshModsAsync();
    }

    private async Task MakeBSideAsync(Form owner)
    {
        using var dlg = new Form { Text = Localizer.T("制作 B 面存档"), Width = 470, Height = 250, StartPosition = FormStartPosition.CenterParent };
        var lbl = new Label { Text = Localizer.T("章节（1-5）:"), Left = 12, Top = 18, Width = 120 };
        var num = new NumericUpDown { Left = 140, Top = 14, Width = 60, Minimum = 1, Maximum = 5, Value = 2 };
        var lbl2 = new Label { Text = Localizer.T("槽位（0-2）:"), Left = 12, Top = 58, Width = 120 };
        var slot = new NumericUpDown { Left = 140, Top = 54, Width = 60, Minimum = 0, Maximum = 2, Value = 0 };
        var all = new CheckBox { Text = Localizer.T("全部 3 个槽位"), Left = 140, Top = 92, Width = 180 };
        var ok = new Button { Text = Localizer.T("确定"), Left = 250, Top = 150, Width = 90, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = Localizer.T("取消"), Left = 348, Top = 150, Width = 90, DialogResult = DialogResult.Cancel };
        dlg.Controls.AddRange(new Control[] { lbl, num, lbl2, slot, all, ok, cancel });
        dlg.AcceptButton = ok; dlg.CancelButton = cancel;
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var args = "--make-bside --chapter chapter" + (int)num.Value + " --slot " + (int)slot.Value + (all.Checked ? " --all-slots" : "");
        var rc = await Program.RunBuilderAsync(args, Log);
        MessageBox.Show(this, rc == 0 ? Localizer.T("B 面存档已创建：进游戏选该章节即可直接开 B 面") : Localizer.T("失败，详见日志"), "Neutraled");
    }

    /// <summary>导入 B 面存档：选文件 → 识别章节/槽位 → 输入名字（≤12 字母）→ 直接写进存档并标记 SideB。
    ///
    /// 两条游戏规则决定了这个界面长这样：
    ///   ① 存档文件**第 1 行就是角色名**（scr_saveprocess 的第一个字段）⇒ 必须输入名字，
    ///      否则游戏里的存档槽显示不出名字（用户反馈：玩家需要输入名字，≤12 个字母）；
    ///   ② 「按实际情况改模板文件名」= 源文件叫什么不重要，模板一律按**实际章节**存成
    ///      Neutraled/bside/chapterN.sav（你自己导入的；随包默认模板在 bside/templates/chapterN.sav，
    ///      --make-bside 优先用自己的、其次用随包的），再把存档写成 filech&lt;N&gt;_&lt;slot&gt;（builder/BSide.cs 统一处理）。
    /// </summary>
    private async Task ImportBSideAsync(Form owner)
    {
        using var ofd = new OpenFileDialog
        {
            Filter = Localizer.T("存档文件|filech*;*.sav|所有文件|*.*"),
            Title = Localizer.T("选择要导入的真实 B 面存档")
        };
        if (ofd.ShowDialog(owner) != DialogResult.OK) return;

        // 先按文件名猜章节/槽位（filech2 → 第 2 章槽位 0），猜不出就默认第 2 章（B 面通常从第二章开始）
        int ch = 2, slot = 0;
        var m = System.Text.RegularExpressions.Regex.Match(
            Path.GetFileNameWithoutExtension(ofd.FileName),
            @"^(?:file)?ch(?:apter)?[_\-\s]*(\d{1,2})(?:[_\-\s]+(\d{1,2}))?$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m.Success)
        {
            ch = int.Parse(m.Groups[1].Value);
            if (m.Groups[2].Success) slot = int.Parse(m.Groups[2].Value);
        }

        using var dlg = new Form { Text = Localizer.T("导入 B 面存档"), Width = 600, Height = 320, StartPosition = FormStartPosition.CenterParent };
        dlg.Controls.Add(new Label { Text = Localizer.T("存档: ") + ofd.FileName, Left = 12, Top = 12, Width = 560, AutoEllipsis = true });
        var lblCh = new Label { Text = Localizer.T("章节（1-5）:"), Left = 12, Top = 52, Width = 120 };
        var numCh = new NumericUpDown { Left = 140, Top = 48, Width = 60, Minimum = 1, Maximum = 5, Value = Math.Clamp(ch, 1, 5) };
        var lblSlot = new Label { Text = Localizer.T("槽位（0-2）:"), Left = 12, Top = 88, Width = 120 };
        var numSlot = new NumericUpDown { Left = 140, Top = 84, Width = 60, Minimum = 0, Maximum = 2, Value = Math.Clamp(slot, 0, 2) };
        var lblName = new Label { Text = Localizer.T("名字（≤12 字母）:"), Left = 240, Top = 52, Width = 130 };
        var tbName = new TextBox { Left = 380, Top = 48, Width = 180, MaxLength = 12 };
        var lblHint = new Label
        {
            Text = Localizer.T("存档第 1 行就是角色名：填玩家名字（最多 12 个字母，会自动转大写）"),
            Left = 12, Top = 122, Width = 560, ForeColor = Color.DimGray
        };
        var apply = new CheckBox
        {
            Text = Localizer.T("直接写进存档并标记 B 面（取消勾选 = 只导入模板）"),
            Left = 12, Top = 150, Width = 500, Checked = true
        };
        var ok = new Button { Text = Localizer.T("导入"), Left = 380, Top = 210, Width = 90, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = Localizer.T("取消"), Left = 478, Top = 210, Width = 90, DialogResult = DialogResult.Cancel };
        dlg.Controls.AddRange(new Control[] { lblCh, numCh, lblSlot, numSlot, lblName, tbName, lblHint, apply, ok, cancel });
        dlg.AcceptButton = ok; dlg.CancelButton = cancel;
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var name = tbName.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show(this, Localizer.T("请先输入名字：存档第 1 行就是角色名（最多 12 个字母）"), "Neutraled");
            return;
        }

        var args = "--import-bside \"" + ofd.FileName + "\" --chapter chapter" + (int)numCh.Value +
                   " --slot " + (int)numSlot.Value + " --save-name \"" + name + "\"" +
                   (apply.Checked ? "" : " --template-only");
        SetBusy(true, Localizer.T("导入 B 面存档中..."));
        var rc = await Program.RunBuilderAsync(args, Log);
        SetBusy(false, rc == 0 ? Localizer.T("完成") : Localizer.T("失败（详见日志）"));
        MessageBox.Show(this, rc == 0
            ? (apply.Checked
                ? Localizer.T("B 面存档已导入并写入槽位：进游戏选该章节即可直接开 B 面")
                : Localizer.T("B 面模板已导入（只导入模板，没有写进存档）"))
            : Localizer.T("失败，详见日志"), "Neutraled");
    }

    private async Task ExportPacksAsync(Form owner)
    {
        using var ofd = new OpenFileDialog { Filter = Localizer.T("data.win|*.win|所有文件|*.*"), Title = Localizer.T("选择要导出资源的 data.win") };
        if (ofd.ShowDialog(owner) != DialogResult.OK) return;
        var chapter = SelectedChapter();
        if (!RequireSingleChapter(chapter, Localizer.T("该功能"))) return;
        SetBusy(true, Localizer.T("导出资源包中（大文件可能几分钟）..."));
        var rc = await Program.RunBuilderAsync(UiLogic.BuildArgs(UiLogic.ActionKind.Packs, chapter, outPath: ofd.FileName), Log);
        SetBusy(false, rc == 0 ? Localizer.T("导出完成") : Localizer.T("失败（详见日志）"));
    }

    private async Task ConflictsCheckAsync(Form owner)
    {
        var chapter = SelectedChapter();
        if (!RequireSingleChapter(chapter, Localizer.T("该功能"))) return;
        var rc = await Program.RunBuilderAsync(UiLogic.BuildArgs(UiLogic.ActionKind.Conflicts, chapter), Log);
        var conflictsPath = Path.Combine(Program.NeutraledRoot, "conflicts.json");
        var ackPath = Path.Combine(Program.NeutraledRoot, "gui-conflicts-ack.txt");
        if (rc == 0)
        {
            UiLogic.AckConflicts(conflictsPath, ackPath);
            MessageBox.Show(this, Localizer.T("未发现冲突"), "Neutraled");
        }
        else if (UiLogic.ConflictsIsNew(conflictsPath, ackPath))
        {
            // 内容哈希变了才算新冲突；ack 之后同样的报告不再重复弹窗
            UiLogic.AckConflicts(conflictsPath, ackPath);
            MessageBox.Show(this, Localizer.T("发现冲突：详见日志与 conflicts.json"), "Neutraled");
        }
        else
        {
            Log(Localizer.T("冲突未变（上次已确认，本次不重复提示）"));
        }
    }

    private async Task LayerFromBaseAsync(Form owner)
    {
        using var ofd = new OpenFileDialog { Filter = Localizer.T("data.win|*.win|所有文件|*.*"), Title = Localizer.T("选择整包 mod 的 ref/data.win") };
        if (ofd.ShowDialog(owner) != DialogResult.OK) return;
        var chapter = SelectedChapter();
        if (!RequireSingleChapter(chapter, Localizer.T("该功能"))) return;
        var name = Path.GetFileName(Path.GetDirectoryName(ofd.FileName) ?? "layer");
        SetBusy(true, Localizer.T("反编译提取差异层中..."));
        var rc = await Program.RunBuilderAsync(UiLogic.BuildArgs(UiLogic.ActionKind.LayerFromBase, chapter, name: name, author: "unknown", winPath: ofd.FileName), Log);
        SetBusy(false, rc == 0 ? Localizer.T("完成") : Localizer.T("失败（详见日志）"));
        if (rc == 0) await RefreshModsAsync();
    }

    private async Task OnlineAsync()
    {
        using var q = new Form { Text = Localizer.T("在线获取（GameBanana）"), Width = 640, Height = 480, StartPosition = FormStartPosition.CenterParent };
        var tb = new TextBox { Left = 12, Top = 14, Width = 430 };
        var btnSearch = new Button { Text = Localizer.T("搜索"), Left = 452, Top = 12, Width = 90 };
        var list = new ListBox { Left = 12, Top = 52, Width = 600, Height = 270, Font = new Font("Consolas", 9) };
        var btnFetch = new Button { Text = Localizer.T("下载并转换"), Left = 12, Top = 334, Width = 140, Enabled = false };
        var btnClose = new Button { Text = Localizer.T("关闭"), Left = 520, Top = 334, Width = 92 };
        q.Controls.AddRange(new Control[] { tb, btnSearch, list, btnFetch, btnClose });
        btnClose.Click += (_, _) => q.Close();

        var results = new List<int>();
        btnSearch.Click += async (_, _) =>
        {
            var query = tb.Text.Trim();
            if (query == "") return;
            list.Items.Clear(); results.Clear();
            var lines = new List<string>();
            await Program.RunBuilderAsync($"--search \"{query}\"", l => lines.Add(l));
            foreach (var l in lines)
            {
                var t = l.Trim();
                if (!t.StartsWith("[")) continue;
                var rb = t.IndexOf(']'); var idb = t.IndexOf("id=");
                if (rb < 0 || idb < 0) continue;
                var idStr = t.Substring(idb + 3).Split(',')[0].Trim();
                if (!int.TryParse(idStr, out var id)) continue;
                results.Add(id);
                list.Items.Add(t);
            }
            btnFetch.Enabled = results.Count > 0;
        };
        btnFetch.Click += async (_, _) =>
        {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= results.Count) return;
            var chapter = SelectedChapter();
            if (!RequireSingleChapter(chapter, Localizer.T("该功能"))) return;
            btnFetch.Enabled = false;
            var rc = await Program.RunBuilderAsync(UiLogic.BuildArgs(UiLogic.ActionKind.Fetch, chapter, results[list.SelectedIndex].ToString()), Log);
            btnFetch.Text = rc == 0 ? Localizer.T("完成 ✓") : Localizer.T("失败");
            if (rc == 0) await RefreshModsAsync();
        };
        q.ShowDialog(this);
    }

    /// <summary>检查已安装 mod 的更新（读取 mod.json 里的 gb_id，查 GameBanana Updates）。</summary>
    private async Task CheckUpdatesAsync()
    {
        SetBusy(true, Localizer.T("检查更新"));
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Neutraled/0.1");
            int checkedCount = 0, updateCount = 0;
            var updates = new List<(string gb, string name, string cur, string ver)>();

            foreach (ListViewItem item in _modList.Items)
            {
                if (item.Tag is not ModRow row) continue;
                string gbId = "", curVer = "";
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(row.Path),
                        new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                    foreach (var p in doc.RootElement.EnumerateObject())
                    {
                        if (p.Name == "gb_id" || p.Name == "gamebanana_id") gbId = p.Value.ToString();
                        if (p.Name == "version") curVer = p.Value.ToString();
                    }
                }
                catch { }
                if (string.IsNullOrEmpty(gbId)) continue;

                checkedCount++;
                try
                {
                    var json = await http.GetStringAsync($"https://gamebanana.com/apiv11/Mod/{gbId}/Updates?_nPerpage=1");
                    using var udoc = JsonDocument.Parse(json);
                    if (!udoc.RootElement.TryGetProperty("_aRecords", out var recs) || recs.GetArrayLength() == 0) continue;
                    var latest = recs[0];
                    var ver = latest.TryGetProperty("_sVersion", out var v) ? v.ToString() : "";
                    var title = latest.TryGetProperty("_sTitle", out var ti) ? ti.ToString() : "";
                    if (ver != "" && ver.TrimStart('v') != curVer.TrimStart('v'))
                    {
                        updateCount++;
                        updates.Add((gbId, row.Name, curVer, ver));
                        Log($"★ {row.Name}: {curVer} -> {ver}  ({title})");
                    }
                    else Log(Localizer.T("   {0}: 已是最新 ({1})", row.Name, curVer));
                }
                catch (Exception ex) { Log(Localizer.T("   {0}: 检查失败 ({1})", row.Name, ex.Message)); }
            }
            SetBusy(false, updateCount > 0 ? Localizer.T("发现 {0} 个更新", updateCount) : Localizer.T("全部为最新"));
            if (checkedCount == 0) Log(Localizer.T("（没有可用于检查更新的 mod：需要 mod.json 里带 gb_id）"));
            // UX-17：只读检查完不再「没有任何入口」——给一个明确的下一步（打开下载页，绝不自动下载/覆盖）。
            if (updates.Count > 0)
            {
                var preview = string.Join(Environment.NewLine,
                    updates.Take(5).Select(u => "  " + u.name + ": " + u.cur + " -> " + u.ver));
                if (updates.Count > 5) preview += Environment.NewLine + Localizer.T("  …（还有 {0} 个）", updates.Count - 5);
                var ask = MessageBox.Show(this,
                    Localizer.T("发现 {0} 个更新：\n\n{1}\n\n要打开 GameBanana 下载页吗？（只打开网页，不会自动下载或覆盖）", updates.Count, preview),
                    Localizer.T("Neutraled — 发现更新"), MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (ask == DialogResult.Yes)
                {
                    try { Process.Start(new ProcessStartInfo { FileName = "https://gamebanana.com/mods/" + updates[0].gb, UseShellExecute = true }); }
                    catch (Exception ex) { Log(Localizer.T("[检查更新] 打开下载页失败: {0}", ex.Message)); }
                }
            }
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            SetBusy(false, Localizer.T("检查失败"));
            Log(Localizer.T("[检查更新] {0}", ex.Message));
        }
    }

    // ==================== GUI 三件套（D3）====================

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try
        {
            var marker = Path.Combine(Program.NeutraledRoot, "installed.json");
            if (!File.Exists(marker)) { ShowFirstRunWizard(); return; }
            if (CheckCrashLastSession(out var ci)) { ShowCrashRecovery(ci); return; }
            var conflicts = Path.Combine(Program.NeutraledRoot, "conflicts.json");
            if (File.Exists(conflicts))
            {
                var doc = JsonDocument.Parse(File.ReadAllText(conflicts));
                var errs = doc.RootElement.GetProperty("summary").GetProperty("errors").GetInt32();
                var warns = doc.RootElement.GetProperty("summary").GetProperty("warnings").GetInt32();
                if (errs > 0 || warns > 0)
                {
                    var r = MessageBox.Show(this,
                        Localizer.T("检测到 mod 冲突：\n\n  严重冲突 {0} 个\n  覆盖警告 {1} 个\n\n是否查看详情？", errs, warns),
                        Localizer.T("Neutraled — mod 冲突提醒"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (r == DialogResult.Yes) ShowDiagnosticsDialog();
                }
            }
        }
        catch { }
    }

    private void ShowFirstRunWizard()
    {
        MessageBox.Show(this,
            Localizer.T("欢迎使用 Neutraled！\n\n" +
                        "1. 确认游戏目录（自动检测）\n" +
                        "2. 备份原版存档（BASELINE 快照，永不删除）\n" +
                        "3. 选择要启用的 mod\n" +
                        "4. 部署并启动游戏\n\n" +
                        "之后每次启动只需点「部署并启动」。\n" +
                        "游戏内按 F2 可以打开控制台。"),
            Localizer.T("Neutraled — 首次运行向导"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        try
        {
            var exe = FindBuilderExe();
            if (exe != null) { RunBuilder(exe, "--install"); Log(Localizer.T("已建立原版存档备份（BASELINE）")); }
        }
        catch (Exception ex) { Log(Localizer.T("首启初始化失败: {0}", ex.Message)); }
        _ = RefreshModsAsync();   // 首启向导是同步方法：列表刷新放后台，不阻塞对话框关闭
    }

    private bool CheckCrashLastSession(out string info)
    {
        info = "";
        try
        {
            var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DELTARUNE", "Neutraled", "dr-api.log");
            if (!File.Exists(logPath)) return false;
            var text = File.ReadAllText(logPath);
            if (!text.Contains("ERROR in") && !text.Contains("Code Error") && !text.Contains("异常已被隔离")) return false;
            foreach (var line in text.Split((char)10))
                if (line.Contains("ERROR in") || line.Contains("Code Error")) { info = line.Trim(); break; }
            if (info == "") info = Localizer.T("日志里发现 mod 异常记录");
            return true;
        }
        catch { return false; }
    }

    private void ShowCrashRecovery(string info)
    {
        var r = MessageBox.Show(this,
            Localizer.T("检测到上次游戏出现异常。\n\n{0}\n\n可以尝试：\n  • 恢复存档到上一次快照（推荐）\n  • 禁用所有 mod 后重新部署\n  • 先看诊断详情\n\n要恢复存档吗？", info),
            Localizer.T("Neutraled — 异常恢复"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
        if (r == DialogResult.Yes)
        {
            try { var exe = FindBuilderExe(); if (exe != null) { RunBuilder(exe, "--restore-save"); Log(Localizer.T("已恢复存档")); } }
            catch (Exception ex) { Log(Localizer.T("恢复失败: {0}", ex.Message)); }
        }
        else if (r == DialogResult.Cancel) ShowDiagnosticsDialog();
    }

    private void ShowDiagnosticsDialog()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(Localizer.T("========== mod 冲突分析 =========="));
        try
        {
            var p = Path.Combine(Program.NeutraledRoot, "conflicts.json");
            if (File.Exists(p))
            {
                var doc = JsonDocument.Parse(File.ReadAllText(p));
                var s = doc.RootElement.GetProperty("summary");
                sb.AppendLine(Localizer.T("  严重冲突: {0}", s.GetProperty("errors").GetInt32()));
                sb.AppendLine(Localizer.T("  覆盖警告: {0}", s.GetProperty("warnings").GetInt32()));
                sb.AppendLine(Localizer.T("  可共存  : {0}", s.GetProperty("ok").GetInt32()));
                sb.AppendLine();
                foreach (var f in doc.RootElement.GetProperty("findings").EnumerateArray())
                {
                    var lvl = f.GetProperty("level").GetString();
                    var tag = lvl == "error" ? Localizer.T("[冲突]") : lvl == "warn" ? Localizer.T("[覆盖]") : Localizer.T("[共存]");
                    sb.AppendLine("  " + tag + "  " + f.GetProperty("target").GetString());
                    sb.AppendLine("      " + (f.GetProperty("detail").GetString() ?? ""));
                    sb.AppendLine();
                }
            }
            else sb.AppendLine(Localizer.T("  （还没有冲突报告，先部署一次）"));
        }
        catch (Exception ex) { sb.AppendLine(Localizer.T("  读取失败: {0}", ex.Message)); }

        sb.AppendLine();
        sb.AppendLine(Localizer.T("========== 存档快照 =========="));
        try
        {
            var bak = Path.Combine(Program.NeutraledRoot, "save-backups");
            if (Directory.Exists(bak))
            {
                foreach (var f in Directory.GetFiles(bak, "*.zip").OrderByDescending(f => f).Take(8))
                {
                    var fi = new FileInfo(f);
                    sb.AppendLine("  " + fi.Name.PadRight(28) + (fi.Length / 1024).ToString().PadLeft(7) + " KB  " + fi.LastWriteTime.ToString("MM-dd HH:mm"));
                }
            }
            else sb.AppendLine(Localizer.T("  （还没有快照）"));
        }
        catch { }

        sb.AppendLine();
        sb.AppendLine(Localizer.T("========== 最近日志 =========="));
        try
        {
            var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DELTARUNE", "Neutraled", "dr-api.log");
            if (File.Exists(logPath))
            {
                var lines = File.ReadAllLines(logPath);
                foreach (var l in lines.Skip(Math.Max(0, lines.Length - 20))) sb.AppendLine("  " + l);
            }
            else sb.AppendLine(Localizer.T("  （无日志）"));
        }
        catch { }

        // GUI-L04：诊断也走通用只读对话框（带 复制全部 / 另存为）。
        ShowTextDialog(Localizer.T("Neutraled — 诊断"), sb.ToString());
    }

    // ==================== GUI-I05：mod 列表交互（右键 / 双击 / 排序）====================
    private int _modSortColumn = -1;
    private bool _modSortAsc = true;

    private async Task ToggleSelectedModsAsync()
    {
        if (_busy || _modList.SelectedItems.Count == 0) return;
        foreach (ListViewItem item in _modList.SelectedItems)
        {
            if (item.Tag is not ModRow row) continue;
            var want = !row.Enabled;
            if (!SaveModEnabled(row, want)) continue;
            item.Checked = want;
            if (item.SubItems.Count >= 5) item.SubItems[4].Text = row.Enabled ? Localizer.T("启用") : Localizer.T("禁用");
        }
        NotifyConfigChanged();
        await Task.CompletedTask;
    }

    private void OpenSelectedModFolder()
    {
        var first = _modList.SelectedItems.Cast<ListViewItem>().FirstOrDefault();
        if (first?.Tag is not ModRow row || !File.Exists(row.Path)) return;
        try { Process.Start("explorer.exe", "/select,\"" + row.Path + "\""); }
        catch (Exception ex) { Log(Localizer.T("[警告] 打开文件夹失败: {0}", ex.Message)); }
    }

    private void ShowSelectedModJson()
    {
        var first = _modList.SelectedItems.Cast<ListViewItem>().FirstOrDefault();
        if (first?.Tag is not ModRow row) return;
        try { ShowTextDialog(Localizer.T("mod.json — {0}", row.Name), File.ReadAllText(row.Path)); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Neutraled"); }
    }

    private sealed class ModListComparer : System.Collections.IComparer
    {
        private readonly int _col;
        private readonly bool _asc;
        public ModListComparer(int col, bool asc) { _col = col; _asc = asc; }
        public int Compare(object? x, object? y)
        {
            var a = ((ListViewItem)x!).SubItems[_col].Text;
            var b = ((ListViewItem)y!).SubItems[_col].Text;
            return (_asc ? 1 : -1) * string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void SortModsByColumn(int column)
    {
        if (column < 0 || column >= _modList.Columns.Count) return;
        if (_modSortColumn == column) _modSortAsc = !_modSortAsc;
        else { _modSortColumn = column; _modSortAsc = true; }
        _modList.ListViewItemSorter = new ModListComparer(column, _modSortAsc);
        _modList.Sort();
    }

    private string? FindBuilderExe()
    {
        // v1.1.0：builder 并进 ntl-gui.exe 了，"builder 可执行文件"就是当前这个 exe（用命令行参数区分行为）。
        var p = Program.SelfExe;
        return File.Exists(p) ? p : null;
    }

    /// <summary>打开合并进本 exe 的子工具（mapedit = 地图/战斗/剧情编辑器，studio = 脚本编辑器）。
    /// 起一个自己的子进程（保留各自的窗口/生命周期），而不是进程内直接调 Main（否则主界面会被子工具的
    /// Application.Run 接管，关掉编辑器就连管理器一起退出）。</summary>
    private void LaunchTool(string sub)
    {
        try
        {
            Log("> ntl-gui.exe " + sub);
            Process.Start(new ProcessStartInfo
            {
                FileName = Program.SelfExe,
                Arguments = sub,
                UseShellExecute = false,
                WorkingDirectory = Program.NeutraledRoot
            });
        }
        catch (Exception ex) { Log(Localizer.T("启动 builder 失败") + ": " + ex.Message); }
    }

    /// <summary>运行 builder 命令（诊断/恢复/安装用）</summary>
    private void RunBuilder(string exe, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8
            };
            using var p = Process.Start(psi);
            if (p == null) { Log(Localizer.T("启动 builder 失败")); return; }
            var outp = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            foreach (var line in outp.Split((char)10))
                if (line.Trim().Length > 0) Log("  " + line.TrimEnd());
        }
        catch (Exception ex) { Log(Localizer.T("执行失败: {0}", ex.Message)); }
    }

    // ==================== 新功能整合 ====================

    /// <summary>mod 联动关系对话框（谁导出了什么、谁依赖谁）</summary>
    private async Task ShowInteropDialogAsync()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(Localizer.T("========== mod 联动关系 =========="));
        sb.AppendLine();

        try
        {
            var modsRoot = Path.Combine(Program.NeutraledRoot, "mods");
            var (installed, loadError) = await ModInstallBridge.LoadAsync(Program.NeutraledRoot);
            if (loadError.Length > 0) sb.AppendLine(Localizer.T("读取失败: {0}", loadError));
            sb.AppendLine(Localizer.T("已安装 mod: {0} 个", installed.Count));
            sb.AppendLine();

            foreach (var m in installed.OrderBy(x => x.Name))
            {
                sb.AppendLine($"  {m.Name}  v{m.Version}  ({m.Author})");
                sb.AppendLine($"      {m.Dir}");
            }
            sb.AppendLine();
            sb.AppendLine(Localizer.T("提示: 游戏内按 F2 输入 mods 可以看到运行时的导出/依赖情况"));
        }
        catch (Exception ex) { sb.AppendLine(Localizer.T("读取失败: {0}", ex.Message)); }

        ShowTextDialog(Localizer.T("Neutraled — mod 联动"), sb.ToString());
    }

    /// <summary>权限报告对话框</summary>
    private void ShowPermissionsDialog()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(Localizer.T("========== mod 权限报告 =========="));
        sb.AppendLine();
        try
        {
            var p = Path.Combine(Program.NeutraledRoot, "permissions.json");
            if (File.Exists(p))
            {
                var doc = JsonDocument.Parse(File.ReadAllText(p));
                int n = 0;
                foreach (var m in doc.RootElement.EnumerateArray())
                {
                    n++;
                    var name = m.GetProperty("name").GetString();
                    var und = m.GetProperty("undeclared");
                    var decl = m.GetProperty("declared");
                    sb.AppendLine($"  {name}");
                    if (decl.GetArrayLength() > 0)
                    {
                        sb.Append(Localizer.T("      已声明: "));
                        foreach (var d in decl.EnumerateArray()) sb.Append(d.GetString() + " ");
                        sb.AppendLine();
                    }
                    if (und.GetArrayLength() > 0)
                    {
                        sb.Append(Localizer.T("      ⚠️ 未声明: "));
                        foreach (var d in und.EnumerateArray()) sb.Append(d.GetString() + " ");
                        sb.AppendLine();
                    }
                }
                if (n == 0) sb.AppendLine(Localizer.T("  （所有 mod 都没有敏感操作）"));
            }
            else sb.AppendLine(Localizer.T("  （还没有报告，先部署一次）"));
        }
        catch (Exception ex) { sb.AppendLine(Localizer.T("读取失败: {0}", ex.Message)); }
        sb.AppendLine();
        sb.AppendLine(Localizer.T("权限级别: safe（安全） / risky（注意） / danger（危险）"));

        ShowTextDialog(Localizer.T("Neutraled — mod 权限"), sb.ToString());
    }

    /// <summary>Kristal 验证对话框</summary>
    private async void ShowKristalDialog()
    {
        var dlg = new FolderBrowserDialog { Description = Localizer.T("选择 Kristal mod 目录（含 *.lua）") };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        Log(Localizer.T("========== Kristal 批量验证 =========="));
        SetBusy(true, Localizer.T("验证 Kristal 工程…"));
        // UX-12：以前不看退出码就 Log「验证完成」，坏工程/坏路径也显示成功。
        var rc = await Program.RunBuilderAsync("--validate-kristal \"" + dlg.SelectedPath + "\"", Log);
        SetBusy(false, rc == 0 ? Localizer.T("Kristal 验证完成") : Localizer.T("Kristal 验证失败（退出码 {0}）", rc));
        if (rc == 0) Log(Localizer.T("验证完成，报告在 Neutraled/kristal-validation/REPORT.md"));
        else Log(Localizer.T("[错误] Kristal 验证失败（退出码 {0}），详见日志", rc));
    }

    /// <summary>通用只读文本对话框（诊断/联动/权限/mod.json 共用）。
    /// GUI-L04：加「复制全部 / 另存为」，可选「刷新」（传入 reload 才会出现该按钮）。</summary>
    private void ShowTextDialog(string title, string content, Func<string>? reload = null)
    {
        var form = new Form { Text = title, Width = 860, Height = 640, StartPosition = FormStartPosition.CenterParent };
        var box = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
            Dock = DockStyle.Fill, Font = new Font("Consolas", 9),
            WordWrap = false, Text = content
        };
        var bar = new Panel { Dock = DockStyle.Top, Height = 42 };
        var btnCopy = new Button { Text = Localizer.T("复制全部"), Left = 8, Top = 7, Width = 100, Height = 28 };
        btnCopy.Click += (_, _) => { try { Clipboard.SetText(box.Text); } catch { } };
        var btnSave = new Button { Text = Localizer.T("另存为…"), Left = 116, Top = 7, Width = 100, Height = 28 };
        btnSave.Click += (_, _) =>
        {
            using var sfd = new SaveFileDialog { Filter = Localizer.T("文本 (*.txt)|*.txt|所有文件|*.*"), FileName = "neutraled.txt" };
            if (sfd.ShowDialog(form) != DialogResult.OK) return;
            try { File.WriteAllText(sfd.FileName, box.Text); }
            catch (Exception ex) { MessageBox.Show(form, ex.Message, "Neutraled"); }
        };
        bar.Controls.AddRange(new Control[] { btnCopy, btnSave });
        if (reload != null)
        {
            var btnReload = new Button { Text = Localizer.T("刷新"), Left = 224, Top = 7, Width = 90, Height = 28 };
            btnReload.Click += (_, _) =>
            {
                try { box.Text = reload(); }
                catch (Exception ex) { box.Text += Environment.NewLine + ex.Message; }
            };
            bar.Controls.Add(btnReload);
        }
        var close = new Button
        {
            Text = Localizer.T("关闭"), Left = form.ClientSize.Width - 108, Top = 7,
            Width = 90, Height = 28, DialogResult = DialogResult.Cancel,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        bar.Controls.Add(close);
        form.CancelButton = close;
        form.Controls.Add(box);
        form.Controls.Add(bar);
        form.ShowDialog(this);
    }
}
