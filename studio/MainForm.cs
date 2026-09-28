using System.Text.Json.Nodes;

namespace Neutraled.Studio;

public sealed class MainForm : Form
{
    private readonly TreeView _tree = new();
    private readonly CodeEditor _editor = new();
    private readonly RichTextBox _log = new();
    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _statusText = new();
    private readonly System.Windows.Forms.Timer _logTimer = new();
    private readonly ToolStripComboBox _langCombo = new();
    private readonly List<(ToolStripButton Btn, string Key)> _localizedButtons = new();
    private readonly TreeNode _nodeLive = new();
    private readonly TreeNode _nodeMods = new();
    private readonly TreeNode _nodeDocs = new();
    private string? _currentFile;
    private bool _dirty;

    public MainForm()
    {
        Localizer.Load(Program.NeutraledRoot);   // 与 ntl-gui 共用同一个 Neutraled/gui_lang.txt

        Text = Localizer.T("Neutraled Studio — DELTARUNE Mod 编辑器");
        Width = 1280; Height = 800; MinimumSize = new Size(900, 600);
        StartPosition = FormStartPosition.CenterScreen;

        // ---------- 工具栏 ----------
        var toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(4) };
        void Btn(string text, Action act)
        {
            var b = new ToolStripButton(Localizer.T(text));
            b.Click += (_, _) => act();
            toolbar.Items.Add(b);
            _localizedButtons.Add((b, text));   // 记词条原文，切换语言时由 ApplyLocalizedTexts 重刷
        }
        Btn("新建", NewFile);
        Btn("打开", OpenFileDialogPick);
        Btn("保存", () => SaveFile(false));
        toolbar.Items.Add(new ToolStripSeparator());
        Btn("部署当前章节", () => _ = DeployAsync());
        Btn("启动游戏", Program.LaunchGame);
        Btn("热重载 live", HotReload);
        toolbar.Items.Add(new ToolStripSeparator());
        Btn("补全 (Ctrl+Space)", () => { _editor.Focus(); _editor.ShowCompletion(); Log(Localizer.T("已触发补全列表")); });
        Btn("刷新文件树", RefreshTree);
        Btn("打开 mod 目录", () => OpenInExplorer(Program.ModsRoot));
        Btn("打开 live 目录", () => OpenInExplorer(Program.LiveRoot));

        // 语言切换（工具栏右端；两个项的文字固定，不翻译）
        _langCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _langCombo.AutoSize = false;   // 否则 ToolStrip 会按内容自动撑宽（约 121px）
        _langCombo.Width = 80;
        _langCombo.Alignment = ToolStripItemAlignment.Right;
        _langCombo.Items.AddRange(new object[] { "中文", "English" });
        _langCombo.SelectedIndex = Localizer.Current == "en" ? 1 : 0;
        _langCombo.SelectedIndexChanged += (_, _) =>
        {
            Localizer.Current = _langCombo.SelectedIndex == 1 ? "en" : "zh";
            Localizer.Save(Program.NeutraledRoot);
            ApplyLocalizedTexts();
        };
        toolbar.Items.Add(_langCombo);

        // ---------- 文件树 ----------
        _tree.Dock = DockStyle.Left;
        _tree.Width = 240;
        _tree.HideSelection = false;
        _tree.AfterSelect += (_, _) => { if (_tree.SelectedNode?.Tag is string p && File.Exists(p)) OpenPath(p); };

        // ---------- 编辑器 ----------
        var editorHost = new Panel { Dock = DockStyle.Fill };
        editorHost.Controls.Add(_editor);
        editorHost.Controls.Add(_editor.Gutter);
        _editor.Dock = DockStyle.Fill;
        _editor.StatusChanged += s => _statusText.Text = s;

        // ---------- 日志 ----------
        _log.Dock = DockStyle.Bottom;
        _log.Height = 200;
        _log.ReadOnly = true;
        _log.Font = new Font("Consolas", 9.5F);
        _log.BackColor = Color.FromArgb(30, 30, 30);
        _log.ForeColor = Color.Gainsboro;
        _log.BorderStyle = BorderStyle.FixedSingle;

        _status.Items.Add(_statusText);
        _statusText.Text = Localizer.T("就绪");

        Controls.Add(editorHost);
        Controls.Add(_log);
        Controls.Add(_tree);
        Controls.Add(toolbar);
        Controls.Add(_status);

        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.S) { SaveFile(false); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.O) { OpenFileDialogPick(); e.Handled = true; }
            else if (e.KeyCode == Keys.F5) { _ = DeployAsync(); e.Handled = true; }
            else if (e.KeyCode == Keys.F6) { HotReload(); e.Handled = true; }
        };

        Load += (_, _) =>
        {
            ApplyLocalizedTexts();
            Log(Localizer.T("游戏根: {0}", Program.GameRoot));
            RefreshTree();
            LoadSymbols();
            if (Program.SelfTest) RunSelfTest();
            _logTimer.Interval = 2000;
            _logTimer.Tick += (_, _) => RefreshGameLog();
            _logTimer.Start();
        };
        FormClosing += (_, e) =>
        {
            if (_dirty && MessageBox.Show(Localizer.T("当前文件未保存，仍要退出？"), "Neutraled Studio",
                    MessageBoxButtons.YesNo) != DialogResult.Yes) e.Cancel = true;
        };
    }

    /// <summary>按当前语言重刷常驻界面文字：窗体标题 / 工具栏按钮 / 文件树节点 / 状态栏。</summary>
    private void ApplyLocalizedTexts()
    {
        RefreshTitle();
        foreach (var (b, key) in _localizedButtons) b.Text = Localizer.T(key);
        _nodeLive.Text = Localizer.T("live（运行时脚本）");
        _nodeMods.Text = Localizer.T("mods（编译期 mod）");
        _nodeDocs.Text = Localizer.T("docs（文档）");
        _statusText.Text = Localizer.T("就绪");
    }

    /// <summary>按当前状态渲染窗体标题（口径与 NewFile / OpenPath / SaveFile 一致）。</summary>
    private void RefreshTitle()
    {
        Text = _currentFile != null ? Localizer.T("Neutraled Studio — {0}", Path.GetFileName(_currentFile))
            : _dirty ? Localizer.T("Neutraled Studio — {0}", Localizer.T("未命名"))
            : Localizer.T("Neutraled Studio — DELTARUNE Mod 编辑器");
    }

    /// <summary>自检：验证补全 / 高亮 / 符号表（无需人工交互）。</summary>
    private void RunSelfTest()
    {
        try
        {
            Log(Localizer.T("===== 自检开始 ====="));
            _editor.Text = "snd_play(1, 1, 1);\n// comment\nlet x = 42;\nexample.hello();";
            _editor.Highlight();
            Log(Localizer.T("高亮: OK（4 行脚本）"));

            _editor.Select(3, 0);
            _editor.ShowCompletion();
            var n = _editor.CompletionCount;
            var sample = string.Join(", ", _editor.CompletionItems.Take(6));
            Log(Localizer.T("补全: 候选 {0} 项  样例: {1}", n, sample));
            _editor.AcceptCompletion();
            Log(Localizer.T("接受补全后首行: {0}", _editor.Lines.FirstOrDefault() ?? ""));

            Log(Localizer.T("===== 自检结束 ====="));
        }
        catch (Exception ex) { Log(Localizer.T("[自检错误] {0}", ex.Message)); }
    }

    // ---------- 日志 ----------
    private void Log(string s)
    {
        if (InvokeRequired) { BeginInvoke(() => Log(s)); return; }
        _log.AppendText(s + Environment.NewLine);
    }

    private void RefreshGameLog()
    {
        var tail = Program.ReadGameLog(60);
        if (string.IsNullOrEmpty(tail)) return;
        // 只在变化时追加
        var lastLine = tail.Split('\n').LastOrDefault()?.Trim() ?? "";
        if (lastLine.Length > 0 && !_log.Text.EndsWith(lastLine))
        {
            var lines = tail.Split('\n');
            var add = lines.TakeLast(3).Where(l => l.Trim().Length > 0);
            foreach (var l in add) _log.AppendText(Localizer.T("[游戏] {0}", l.Trim()) + Environment.NewLine);
        }
    }

    // ---------- 文件树 ----------
    private void RefreshTree()
    {
        _tree.BeginUpdate();
        _tree.Nodes.Clear();
        var root = new TreeNode("Neutraled") { Tag = Program.NeutraledRoot };
        AddDir(root, Program.LiveRoot, Localizer.T("live（运行时脚本）"), _nodeLive);
        AddDir(root, Program.ModsRoot, Localizer.T("mods（编译期 mod）"), _nodeMods);
        var docs = Path.Combine(Program.NeutraledRoot, "docs");
        if (Directory.Exists(docs)) AddDir(root, docs, Localizer.T("docs（文档）"), _nodeDocs);
        root.Expand();
        _tree.Nodes.Add(root);
        _tree.EndUpdate();
        Log(Localizer.T("文件树已刷新（live: {0}）", Program.LiveRoot));
    }

    private void AddDir(TreeNode parent, string dir, string label, TreeNode? reuse = null)
    {
        if (!Directory.Exists(dir)) return;
        var node = reuse ?? new TreeNode();
        node.Text = label;
        node.Tag = dir;
        node.Nodes.Clear();
        foreach (var sub in Directory.GetDirectories(dir).OrderBy(x => x))
            AddDir(node, sub, Path.GetFileName(sub));
        foreach (var f in Directory.GetFiles(dir, "*.ntl").Concat(Directory.GetFiles(dir, "*.gml"))
                     .Concat(Directory.GetFiles(dir, "*.json")).OrderBy(x => x))
            node.Nodes.Add(new TreeNode(Path.GetFileName(f)) { Tag = f });
        parent.Nodes.Add(node);
    }

    // ---------- 文件操作 ----------
    private void NewFile()
    {
        _editor.Text = Localizer.T("// 新的 NTL Script\n// 保存到 Neutraled/live/<ModName>/*.ntl 即可被运行时加载\n\nlog(\"hello from NTL Script\");\n");
        _currentFile = null;
        _dirty = true;
        RefreshTitle();
    }

    private void OpenFileDialogPick()
    {
        using var ofd = new OpenFileDialog
        {
            Filter = Localizer.T("NTL/GML 脚本|*.ntl;*.gml|GML|*.gml|JSON|*.json|所有文件|*.*"),
            InitialDirectory = Directory.Exists(Program.LiveRoot) ? Program.LiveRoot : Program.NeutraledRoot
        };
        if (ofd.ShowDialog(this) == DialogResult.OK) OpenPath(ofd.FileName);
    }

    private void OpenPath(string path)
    {
        try
        {
            _editor.Text = File.ReadAllText(path);
            _currentFile = path;
            _dirty = false;
            RefreshTitle();
            _editor.Highlight();
            Log(Localizer.T("已打开: {0}", path));
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Neutraled Studio"); }
    }

    private bool SaveFile(bool silent)
    {
        if (_currentFile == null)
        {
            using var sfd = new SaveFileDialog
            {
                Filter = Localizer.T("NTL/GML 脚本|*.ntl;*.gml|GML|*.gml|JSON|*.json|所有文件|*.*"),
                InitialDirectory = Directory.Exists(Program.LiveRoot) ? Program.LiveRoot : Program.NeutraledRoot,
                FileName = "script.ntl"
            };
            if (sfd.ShowDialog(this) != DialogResult.OK) return false;
            _currentFile = sfd.FileName;
        }
        try
        {
            File.WriteAllText(_currentFile, _editor.Text);
            _dirty = false;
            if (!silent) Log(Localizer.T("已保存: {0}", _currentFile));
            RefreshTitle();
            return true;
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Neutraled Studio"); return false; }
    }

    // ---------- 动作 ----------
    private async Task DeployAsync()
    {
        if (_dirty) SaveFile(true);
        Log(Localizer.T("===== 部署中（先关闭游戏）====="));
        var rc = await Program.RunBuilderAsync("--deploy --chapter chapter4", Log);
        Log(rc == 0 ? Localizer.T("部署完成 ✓") : Localizer.T("部署失败 (exit={0})", rc));
    }

    private void HotReload()
    {
        if (_dirty && !SaveFile(true)) return;
        Program.RequestHotReload();
        Log(Localizer.T("已请求热重载（游戏内下帧生效；若未运行请先启动游戏）"));
    }

    private void OpenInExplorer(string path)
    {
        try { if (Directory.Exists(path)) System.Diagnostics.Process.Start("explorer.exe", path); }
        catch { }
    }

    // ---------- 符号表（自动补全） ----------
    private void LoadSymbols()
    {
        var list = new List<string>(Syntax.Builtins);
        try
        {
            if (File.Exists(Program.ApiRegistry))
            {
                var j = JsonNode.Parse(File.ReadAllText(Program.ApiRegistry));
                if (j?["original"]?["functions"] is JsonArray fns)
                    foreach (var f in fns) { var s = f?.ToString(); if (!string.IsNullOrEmpty(s)) list.Add(s); }
                if (j?["original"]?["objects"] is JsonArray objs)
                    foreach (var o in objs) { var s = o?.ToString(); if (!string.IsNullOrEmpty(s)) list.Add(s); }
                if (j?["mods"] is JsonArray mods)
                {
                    foreach (var m in mods)
                    {
                        var ns = m?["ns"]?.ToString();
                        if (string.IsNullOrEmpty(ns)) continue;
                        if (m?["functions"] is JsonArray mf)
                            foreach (var fn in mf) { var n = fn?["Name"]?.ToString(); if (!string.IsNullOrEmpty(n)) list.Add(ns + "." + n); }
                        if (m?["constants"] is JsonArray mc)
                            foreach (var c in mc) { var n = c?["Name"]?.ToString(); if (!string.IsNullOrEmpty(n)) list.Add(ns + "." + n); }
                    }
                }
            }
            _editor.SetSymbols(list);
            Log(Localizer.T("符号表已加载: {0} 项（Ctrl+Space 触发补全）", list.Distinct().Count()));
        }
        catch (Exception ex) { Log(Localizer.T("[警告] 符号表加载失败: {0}", ex.Message)); }
    }
}
