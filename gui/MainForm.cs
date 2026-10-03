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

    private sealed class ModRow
    {
        public string Path = "";
        public string Id = "";
        public string Name = "";
        public string Author = "";
        public string Chapter = "";
        public string Version = "1.0.1";
        public bool Enabled = true;
    }

    public MainForm()
    {
        Localizer.Load(Program.NeutraledRoot);
        DeployOptions.Load(Program.NeutraledRoot);   // 部署加速档（Neutraled/gui_deploy.txt）

        Text = Localizer.T("Neutraled — DELTARUNE Mod 管理器");
        Width = 1190; Height = 740; MinimumSize = new Size(900, 620);
        StartPosition = FormStartPosition.CenterScreen;

        var topPanel = new Panel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8) };
        _lblChapter.Text = Localizer.T("章节:"); _lblChapter.Left = 8; _lblChapter.Top = 14; _lblChapter.Width = 50; _lblChapter.AutoSize = false;
        _chapterCombo.Left = 60; _chapterCombo.Top = 10; _chapterCombo.Width = 150;
        _chapterCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _chapterCombo.Items.AddRange(new object[] { "chapter1", "chapter2", "chapter3", "chapter4", "chapter5", "root", "all" });
        _chapterCombo.SelectedIndex = 3;
        _btnRefresh.Text = Localizer.T("刷新"); _btnRefresh.Left = 222; _btnRefresh.Top = 9; _btnRefresh.Width = 70;
        _btnRefresh.Click += (_, _) => RefreshMods();

        _langCombo.Left = 306; _langCombo.Top = 10; _langCombo.Width = 100;
        _langCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _langCombo.Items.AddRange(new object[] { "中文", "English" });
        _langCombo.SelectedIndex = Localizer.Current == "en" ? 1 : 0;
        _langCombo.SelectedIndexChanged += (_, _) =>
        {
            Localizer.Current = _langCombo.SelectedIndex == 1 ? "en" : "zh";
            Localizer.Save(Program.NeutraledRoot);
            SaveConfigLang(Localizer.Current);   // 同步 config.json 的 lang：游戏内语言与 GUI 用同一个开关
            ApplyLocalizedTexts();
        };
        _chapterCombo.SelectedIndexChanged += (_, _) => UpdateDeployState();

        _deployState.Left = 416; _deployState.Top = 14; _deployState.Width = 200; _deployState.AutoSize = false;
        _deployState.ForeColor = Color.DimGray;

        _btnEnhance.Text = Localizer.T("增强选项"); _btnEnhance.Left = 624; _btnEnhance.Top = 9; _btnEnhance.Width = 110;
        _btnDiag.Text = Localizer.T("诊断"); _btnDiag.Left = 742; _btnDiag.Top = 9; _btnDiag.Width = 70;
        _btnInterop.Text = Localizer.T("联动"); _btnInterop.Left = 818; _btnInterop.Top = 9; _btnInterop.Width = 60;
        _btnInterop.Click += (_, _) => ShowInteropDialog();
        topPanel.Controls.Add(_btnInterop);
        _btnPerm.Text = Localizer.T("权限"); _btnPerm.Left = 884; _btnPerm.Top = 9; _btnPerm.Width = 60;
        _btnPerm.Click += (_, _) => ShowPermissionsDialog();
        topPanel.Controls.Add(_btnPerm);
        // Kristal 是专有名词，中英一致（保持硬编码）
        _btnKristal.Text = "Kristal"; _btnKristal.Left = 950; _btnKristal.Top = 9; _btnKristal.Width = 70;
        _btnKristal.Click += (_, _) => ShowKristalDialog();
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
        _modList.ItemChecked += (_, e) =>
        {
            if (!_busy && e.Item.Tag is ModRow row) row.Enabled = e.Item.Checked;
            NotifyConfigChanged();   // 配置变更时提示"重启游戏后生效"
        };

        var btnPanel = new Panel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(8) };
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

        Load += (_, _) =>
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

            RefreshMods();
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
        File.WriteAllText(path, obj.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }));
    }

    private void Log(string s)
    {
        if (InvokeRequired) { BeginInvoke(() => Log(s)); return; }
        _logBox.AppendText(s + Environment.NewLine);
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

    private void RefreshMods()
    {
        _modList.Items.Clear();
        var modsRoot = Program.ModsRoot;
        if (!Directory.Exists(modsRoot)) { Log(Localizer.T("[错误] mods 目录不存在: {0}", modsRoot)); return; }

        var candidates = new List<(string dir, string chapter, string modName, string author)>();
        foreach (var modDir in Directory.GetDirectories(modsRoot))
        {
            var modName = Path.GetFileName(modDir);
            if (IsChapterFolder(modName))
            {
                foreach (var sub in Directory.GetDirectories(modDir))
                    candidates.Add((sub, modName, Path.GetFileName(sub), ""));
                continue;
            }
            bool matchedNew = false;
            foreach (var authorDir in Directory.GetDirectories(modDir))
            {
                var author = Path.GetFileName(authorDir);
                foreach (var chDir in Directory.GetDirectories(authorDir))
                {
                    var chName = Path.GetFileName(chDir);
                    if (!IsChapterFolder(chName)) continue;
                    candidates.Add((chDir, chName, modName, author));
                    matchedNew = true;
                }
            }
            if (!matchedNew && File.Exists(Path.Combine(modDir, "mod.json")))
                candidates.Add((modDir, "", modName, ""));
        }

        int count = 0;
        foreach (var (dir, dch, modName, author) in candidates)
        {
            var mj = Path.Combine(dir, "mod.json");
            if (!File.Exists(mj)) continue;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(mj),
                    new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                var root = doc.RootElement;
                var row = new ModRow
                {
                    Path = mj,
                    Id = GetStr(root, "id"),
                    Name = GetStr(root, "name"),
                    Author = GetStr(root, "author"),
                    Chapter = dch != "" ? dch : GetStr(root, "chapter"),
                    Version = GetStr(root, "version"),
                    Enabled = !root.TryGetProperty("enabled", out var en) || en.ValueKind != JsonValueKind.False
                };
                if (string.IsNullOrEmpty(row.Name)) row.Name = modName;
                if (string.IsNullOrEmpty(row.Author)) row.Author = author;
                var item = new ListViewItem(new[] { row.Name, row.Id, row.Author, row.Chapter, row.Enabled ? Localizer.T("启用") : Localizer.T("禁用") })
                { Tag = row, Checked = row.Enabled };
                _modList.Items.Add(item);
                count++;
            }
            catch (Exception ex) { Log(Localizer.T("[警告] 解析失败 {0}: {1}", mj, ex.Message)); }
        }
        Log(Localizer.T("已加载 {0} 个 mod（目录: {1}）", count, modsRoot));
        _status.Text = Localizer.T("共 {0} 个 mod", count);
        UpdateDeployState();
    }

    private static bool IsChapterFolder(string n) =>
        n.Equals("root", StringComparison.OrdinalIgnoreCase) ||
        (n.StartsWith("chapter", StringComparison.OrdinalIgnoreCase) && n.Length > 7);

    private static string GetStr(JsonElement e, string p) =>
        e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

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
                File.WriteAllText(row.Path, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                row.Enabled = item.Checked;
                changed++;
                Log(Localizer.T("  启用状态: {0} -> {1}", row.Name, item.Checked ? "on" : "off"));
            }
            catch (Exception ex) { Log(Localizer.T("[警告] 写入失败 {0}: {1}", row.Path, ex.Message)); }
        }
        return changed;
    }

    /// <summary>增强选项落盘：9 个增强键就地合并进 config.json（对话框按钮与 headless 自检共用这一条路径）。
    /// 只覆盖这 9 个键，文件里 lang / base_mod / cache_max_mb 等其它键由 MergeConfig 原样保住；
    /// 坏 JSON 时按旧 ShowEnhanceDialog 的行为整份覆写（不弹错、不崩）。</summary>
    private static void SaveEnhanceConfig(string path, bool skipSelector, int autoChapter, bool skipLegend,
        bool skipLogo, int introDelay, bool debugLive)
    {
        MergeConfig(path, obj =>
        {
            obj["_comment"] = "Neutraled 增强开关（全部可选，默认关闭；改完重启游戏生效）";
            obj["auto_skip_selector"] = skipSelector;
            obj["auto_chapter"] = autoChapter;
            obj["auto_chapter_id"] = "";
            obj["auto_skip_delay"] = 90;
            obj["skip_legend"] = skipLegend;
            obj["skip_logo"] = skipLogo;
            obj["intro_delay"] = introDelay;
            obj["debug_live"] = debugLive;
        }, overwriteOnBroken: true);
    }

    /// <summary>增强选项对话框（读写 Neutraled/config.json；全部可选，默认关闭）。</summary>
    private void ShowEnhanceDialog()
    {
        var cfgPath = Path.Combine(Program.NeutraledRoot, "config.json");
        bool skipSelector = false, skipLegend = false, skipLogo = false, debugLive = false;
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
                skipSelector = B("auto_skip_selector");
                skipLegend = B("skip_legend");
                skipLogo = B("skip_logo");
                debugLive = B("debug_live");
                autoChapter = I("auto_chapter");
                introDelay = I("intro_delay");
                if (introDelay <= 0) introDelay = 45;
            }
        }
        catch (Exception ex) { Log(Localizer.T("[警告] 读取 config.json 失败: {0}", ex.Message)); }

        using var dlg = new Form
        {
            Text = Localizer.T("Neutraled 增强选项（全部可选）"),
            Width = 460, Height = 380, StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false
        };
        var cbSelector = new CheckBox { Text = Localizer.T("跳过章节选择器（直接进入指定章节）"), Left = 16, Top = 16, Width = 410, Checked = skipSelector };
        var lblCh = new Label { Text = Localizer.T("自动进入章节:"), Left = 36, Top = 46, Width = 100 };
        var numCh = new NumericUpDown { Left = 140, Top = 43, Width = 70, Minimum = 0, Maximum = 20, Value = autoChapter };
        var cbLegend = new CheckBox { Text = Localizer.T("跳过章节内“传说”画面"), Left = 16, Top = 82, Width = 410, Checked = skipLegend };
        var cbLogo = new CheckBox { Text = Localizer.T("跳过“DELTARUNE”报幕"), Left = 16, Top = 110, Width = 410, Checked = skipLogo };
        var lblDelay = new Label { Text = Localizer.T("跳过前等待帧数:"), Left = 36, Top = 142, Width = 120 };
        var numDelay = new NumericUpDown { Left = 160, Top = 139, Width = 70, Minimum = 0, Maximum = 600, Value = introDelay };
        var cbDebug = new CheckBox { Text = Localizer.T("live 脚本调试日志（debug_on）"), Left = 16, Top = 172, Width = 410, Checked = debugLive };
        var lblNote = new Label
        {
            Text = Localizer.T("提示：修改后需重启游戏生效；全部默认关闭，不影响原版体验。"),
            Left = 16, Top = 210, Width = 410, Height = 40, ForeColor = Color.DimGray
        };
        var btnSave = new Button { Text = Localizer.T("保存"), Left = 230, Top = 280, Width = 95, DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = Localizer.T("关闭"), Left = 335, Top = 280, Width = 95, DialogResult = DialogResult.Cancel };
        dlg.Controls.AddRange(new Control[] { cbSelector, lblCh, numCh, cbLegend, cbLogo, lblDelay, numDelay, cbDebug, lblNote, btnSave, btnCancel });
        dlg.AcceptButton = btnSave; dlg.CancelButton = btnCancel;

        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            SaveEnhanceConfig(cfgPath, cbSelector.Checked, (int)numCh.Value, cbLegend.Checked,
                cbLogo.Checked, (int)numDelay.Value, cbDebug.Checked);
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

    /// <summary>显示所选章节的部署状态（与原版备份对比）。</summary>
    private void UpdateDeployState()
    {
        try
        {
            var chapter = _chapterCombo.SelectedItem?.ToString() ?? "chapter4";
            if (chapter == "all") { _deployState.Text = ""; return; }
            var gameRoot = Program.GameRoot;

            string win, bak;
            if (chapter == "root")
            {
                win = Path.Combine(gameRoot, "data.win");
                bak = Path.Combine(gameRoot, "backup", "data.win");
                if (!File.Exists(bak)) bak = Path.Combine(gameRoot, "backup", "root", "data.win");
            }
            else
            {
                win = Path.Combine(gameRoot, chapter + "_windows", "data.win");
                bak = Path.Combine(gameRoot, "backup", chapter + "_windows", "data.win");
                if (!File.Exists(bak)) bak = Path.Combine(gameRoot, "backup", chapter, "data.win");
            }

            if (!File.Exists(win)) { _deployState.Text = Localizer.T("{0}: （缺失）", chapter); _deployState.ForeColor = Color.Firebrick; return; }

            var same = File.Exists(bak) && new FileInfo(win).Length == new FileInfo(bak).Length;
            _deployState.Text = chapter + ": " + (same ? Localizer.T("未部署（原版）") : Localizer.T("已部署"));
            _deployState.ForeColor = same ? Color.DimGray : Color.SeaGreen;
        }
        catch { _deployState.Text = ""; }
    }

    /// <summary>跑 builder，并把拼好的完整命令行打进日志（便于核对参数确实带上了）。</summary>
    private async Task<int> RunBuilderCmdAsync(string args)
    {
        Log("> ntl-builder.exe " + args);
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
            Log("> ntl-builder.exe --adopt-current --yes");
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
        var rc = await RunBuilderCmdAsync(DeployOptions.AppendFastDeploy("--deploy --chapter all"));
        SetBusy(false, rc == 0 ? Localizer.T("部署完成") : Localizer.T("部署失败 (exit={0})", rc));
        UpdateDeployState();
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
        var chapter = _chapterCombo.SelectedItem?.ToString() ?? "chapter4";
        if (chapter == "all") chapter = "chapter4";
        SetBusy(true, Localizer.T("转换中..."));
        Log($"===== {input} -> {modName} =====");
        // 用 --import-mod（自动识别 6 种包形态：Modding.xml / Deltamod / mod_config.json /
        // 裸 xdelta / 裸 data.win / Kristal / 原生 mod）—— 以前写死 --import-delta，只能吃单项 xdelta。
        var rc = await Program.RunBuilderAsync(
            $"--import-mod \"{input}\" --chapter {chapter} --name \"{modName}\" --author \"{author}\"", Log);
        SetBusy(false, rc == 0 ? Localizer.T("转换完成") : Localizer.T("转换失败（详见日志）"));
        if (rc == 0) RefreshMods();
    }

    private async Task DeployAsync(bool launch)
    {
        var chapter = _chapterCombo.SelectedItem?.ToString() ?? "chapter4";
        if (!await ConfirmUpdatesAsync(launch ? Localizer.T("启动") : Localizer.T("部署"))) return;
        SetBusy(true, launch ? Localizer.T("准备启动（查缓存）...") : Localizer.T("部署中..."));
        Log($"===== {chapter} =====");
        ApplyCheckedState();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        int rc;
        if (launch)
        {
            // 走缓存路径：命中则硬链接复用（秒开），未命中则部署 + 存缓存，最后由 Steam 拉起
            rc = await RunBuilderCmdAsync(DeployOptions.AppendFastDeploy($"--launch {chapter}"));
            // 起后台守候：游戏里进 Kristal 章节 → 游戏写请求并退出 → 守候进程拉起 Kristal
            if (rc == 0) Program.StartExternalWatcher();
            sw.Stop();
            var el = $"{sw.Elapsed.TotalSeconds:F1}s";
            SetBusy(false, rc == 0 ? $"{Localizer.T("已启动游戏（通过 Steam）")} ({el})" : Localizer.T("启动失败 (exit={0})", rc));
            if (rc == 0) WatchAndRestore(chapter);
            // 记录当前生效的配置签名，用于"配置已变更"提示
            _launchedSignature = await QuerySignatureAsync(chapter);
        }
        else
        {
            rc = await RunBuilderCmdAsync(DeployOptions.AppendFastDeploy($"--deploy --chapter {chapter}"));
            sw.Stop();
            var elapsed = $"{sw.Elapsed.TotalSeconds:F1}s";
            SetBusy(false, rc == 0 ? $"{Localizer.T("部署完成")} ({elapsed})" : Localizer.T("部署失败 (exit={0})", rc));
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
            var chapter = _chapterCombo.SelectedItem?.ToString() ?? "chapter4";
            await Program.RunBuilderAsync($"--launch {chapter} --cache-max {txtMax.Text}", Log);
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
        var chapter = _chapterCombo.SelectedItem?.ToString() ?? "chapter4";
        var now = await QuerySignatureAsync(chapter);
        if (!string.IsNullOrEmpty(now) && now != _launchedSignature)
            _status.Text = Localizer.T("配置已变更，重启游戏后生效");
    }

    /// <summary>游戏退出后自动恢复原版（单章部署；all 不恢复）。</summary>
    private void WatchAndRestore(string chapter)
    {
        if (chapter == "all") return;
        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(20000);
                for (int i = 0; i < 3600; i++)   // 最多等 3 小时
                {
                    if (Process.GetProcessesByName("DELTARUNE").Length == 0) break;
                    await Task.Delay(3000);
                }
                await Task.Delay(2000);
                if (InvokeRequired) BeginInvoke(() => Log(Localizer.T("游戏退出，正在恢复原版...")));
                else Log(Localizer.T("游戏退出，正在恢复原版..."));
                await Program.RunBuilderAsync($"--restore-chapter {chapter}", Log);
                if (InvokeRequired) BeginInvoke(() => _status.Text = Localizer.T("已自动恢复原版"));
            }
            catch (Exception ex) { Log("[auto-restore] " + ex.Message); }
        });
    }

    private async Task RestoreAsync()
    {
        var chapter = _chapterCombo.SelectedItem?.ToString() ?? "chapter4";
        if (chapter == "all")
        {
            MessageBox.Show(Localizer.T("all 不支持一键恢复，请分别选择单章恢复。"), "Neutraled");
            return;
        }
        if (MessageBox.Show(Localizer.T("把 {0} 恢复到原版？", chapter), "Neutraled", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
        SetBusy(true, Localizer.T("恢复中..."));
        var rc = await Program.RunBuilderAsync($"--restore-chapter {chapter}", Log);
        SetBusy(false, rc == 0 ? Localizer.T("已恢复原版") : Localizer.T("恢复失败"));
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
            y += 66;
            return b;
        }
        Add(Localizer.T("Kristal 宿主合并"), Localizer.T("项目 + 插件 → 一个 mod（插件型 mod 缺宿主时用）"))
            .Click += async (_, _) => await KristalMergeAsync(dlg);
        Add(Localizer.T("制作 B 面存档"), Localizer.T("任意章节直接开 B 面，不用从第二章重打"))
            .Click += async (_, _) => await MakeBSideAsync(dlg);
        Add(Localizer.T("导出资源包"), Localizer.T("data.win → 精灵/声音/字体，可叠加到任意基底"))
            .Click += async (_, _) => await ExportPacksAsync(dlg);
        Add(Localizer.T("检查 mod 冲突"), Localizer.T("只查不部署（退出码 2 = 有冲突），写 conflicts.json"))
            .Click += async (_, _) => await ConflictsCheckAsync(dlg);
        Add(Localizer.T("源码级差异层"), Localizer.T("整包 mod → 可叠加 patch 层（反编译真实改动，绕过索引问题）"))
            .Click += async (_, _) => await LayerFromBaseAsync(dlg);
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
                   Localizer.T("注意：部署缓存签名不含这个开关，切换后会提示清空缓存，否则可能直接复用上一次的产物。"),
            Left = 30, Top = y + 30, Width = 586, AutoSize = false, ForeColor = Color.DimGray
        };
        cbFast.CheckedChanged += async (_, _) =>
        {
            DeployOptions.FastDeploy = cbFast.Checked;
            DeployOptions.Save(Program.NeutraledRoot);
            Log(cbFast.Checked
                ? Localizer.T("部署加速档：已开启（之后部署会附加 --fast-deploy，控制台输入屏蔽失效）")
                : Localizer.T("部署加速档：已关闭（之后部署会做完整的输入函数重定向）"));
            // 实测：部署缓存的签名（builder/Cache.cs 的 Signature）只算 api 指纹/游戏版本/章节/mod 列表，
            // 不含 --fast-deploy。不清缓存的话下一次「部署并启动」可能直接复用上一次的产物，
            // 开关看起来"没生效"（关掉开关时更糟：输入屏蔽仍然是坏的）。
            if (MessageBox.Show(dlg,
                    Localizer.T("部署缓存的签名不包含这个开关，旧缓存会在下次部署时被直接复用。\n\n现在清空部署缓存，确保新设置下次真正生效？"),
                    Localizer.T("部署加速档"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                await Program.RunBuilderAsync("--cache-clear", Log);
                Log(Localizer.T("部署缓存已清空，下次部署会按当前设置重建。"));
            }
            else
                Log(Localizer.T("[警告] 未清空缓存：若下次部署命中了旧缓存，这个开关可能看起来不生效（缓存管理里可随时清空）。"));
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
        if (rc == 0) RefreshMods();
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

    private async Task ExportPacksAsync(Form owner)
    {
        using var ofd = new OpenFileDialog { Filter = Localizer.T("data.win|*.win|所有文件|*.*"), Title = Localizer.T("选择要导出资源的 data.win") };
        if (ofd.ShowDialog(owner) != DialogResult.OK) return;
        var chapter = _chapterCombo.SelectedItem?.ToString() ?? "chapter4";
        if (chapter == "all") chapter = "chapter4";
        SetBusy(true, Localizer.T("导出资源包中（大文件可能几分钟）..."));
        var rc = await Program.RunBuilderAsync("--export-packs \"" + ofd.FileName + "\" --chapter " + chapter, Log);
        SetBusy(false, rc == 0 ? Localizer.T("导出完成") : Localizer.T("失败（详见日志）"));
    }

    private async Task ConflictsCheckAsync(Form owner)
    {
        var chapter = _chapterCombo.SelectedItem?.ToString() ?? "chapter4";
        if (chapter == "all") chapter = "chapter4";
        var rc = await Program.RunBuilderAsync("--conflicts --chapter " + chapter, Log);
        MessageBox.Show(this, rc == 0 ? Localizer.T("未发现冲突") : Localizer.T("发现冲突：详见日志与 conflicts.json"), "Neutraled");
    }

    private async Task LayerFromBaseAsync(Form owner)
    {
        using var ofd = new OpenFileDialog { Filter = Localizer.T("data.win|*.win|所有文件|*.*"), Title = Localizer.T("选择整包 mod 的 ref/data.win") };
        if (ofd.ShowDialog(owner) != DialogResult.OK) return;
        var chapter = _chapterCombo.SelectedItem?.ToString() ?? "chapter4";
        if (chapter == "all") chapter = "chapter4";
        var name = Path.GetFileName(Path.GetDirectoryName(ofd.FileName) ?? "layer");
        SetBusy(true, Localizer.T("反编译提取差异层中..."));
        var rc = await Program.RunBuilderAsync("--layer-from-base \"" + ofd.FileName + "\" --chapter " + chapter + " --name \"" + name + "\" --author unknown", Log);
        SetBusy(false, rc == 0 ? Localizer.T("完成") : Localizer.T("失败（详见日志）"));
        if (rc == 0) RefreshMods();
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
            var chapter = _chapterCombo.SelectedItem?.ToString() ?? "chapter4";
            if (chapter == "all") chapter = "chapter4";
            btnFetch.Enabled = false;
            var rc = await Program.RunBuilderAsync($"--fetch {results[list.SelectedIndex]} --chapter {chapter}", Log);
            btnFetch.Text = rc == 0 ? Localizer.T("完成 ✓") : Localizer.T("失败");
            if (rc == 0) RefreshMods();
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
                        Log($"★ {row.Name}: {curVer} -> {ver}  ({title})");
                    }
                    else Log(Localizer.T("   {0}: 已是最新 ({1})", row.Name, curVer));
                }
                catch (Exception ex) { Log(Localizer.T("   {0}: 检查失败 ({1})", row.Name, ex.Message)); }
            }
            SetBusy(false, updateCount > 0 ? Localizer.T("发现 {0} 个更新", updateCount) : Localizer.T("全部为最新"));
            if (checkedCount == 0) Log(Localizer.T("（没有可用于检查更新的 mod：需要 mod.json 里带 gb_id）"));
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
        RefreshMods();
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

        var form = new Form { Text = Localizer.T("Neutraled — 诊断"), Width = 840, Height = 640, StartPosition = FormStartPosition.CenterParent };
        form.Controls.Add(new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, Font = new Font("Consolas", 9), WordWrap = false, Text = sb.ToString() });
        form.ShowDialog(this);
    }

    private string? FindBuilderExe()
    {
        var p = Path.Combine(Program.NeutraledRoot, "builder", "bin", "Release", "net9.0", "ntl-builder.exe");
        return File.Exists(p) ? p : null;
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

    // ==================== 本轮新功能整合 ====================

    /// <summary>mod 联动关系对话框（谁导出了什么、谁依赖谁）</summary>
    private void ShowInteropDialog()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(Localizer.T("========== mod 联动关系 =========="));
        sb.AppendLine();

        try
        {
            var modsRoot = Path.Combine(Program.NeutraledRoot, "mods");
            var installed = ModInstallBridge.List(Program.NeutraledRoot);
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
    private void ShowKristalDialog()
    {
        var dlg = new FolderBrowserDialog { Description = Localizer.T("选择 Kristal mod 目录（含 *.lua）") };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var exe = FindBuilderExe();
        if (exe == null) { Log(Localizer.T("[错误] 找不到 ntl-builder.exe")); return; }

        Log(Localizer.T("========== Kristal 批量验证 =========="));
        RunBuilder(exe, "--validate-kristal \"" + dlg.SelectedPath + "\"");
        Log(Localizer.T("验证完成，报告在 Neutraled/kristal-validation/REPORT.md"));
    }

    /// <summary>通用文本对话框</summary>
    private void ShowTextDialog(string title, string content)
    {
        var form = new Form { Text = title, Width = 860, Height = 640, StartPosition = FormStartPosition.CenterParent };
        form.Controls.Add(new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
            Dock = DockStyle.Fill, Font = new Font("Consolas", 9),
            WordWrap = false, Text = content
        });
        form.ShowDialog(this);
    }
}
