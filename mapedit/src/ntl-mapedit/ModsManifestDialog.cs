using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NtlMapEdit;

/// <summary>
/// Whole-pack mod list editor (mods.json). 用户在 m35081 明确：mods.json 指整个 mod 集合的
/// 信息整合文件，类似 Everest 的 Everest.yaml（id / name / author / version / enabled /
/// 依赖与加载顺序），不是 builder 部署时生成的运行时清单。
/// </summary>
public sealed class ModsManifestDialog : Form
{
    /// <summary>一份清单文件：{ "version": 1, "mods": [ ... ] }。</summary>
    public sealed class ManifestFile
    {
        public int Version { get; set; } = 1;
        public List<ManifestMod> Mods { get; set; } = new();
    }

    /// <summary>单个 mod 的条目；字段与 Neutraled 的 mod.json 对齐并额外带依赖/顺序。</summary>
    public sealed class ManifestMod
    {
        public bool Enabled { get; set; } = true;
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Author { get; set; } = "";
        public string Version { get; set; } = "1.0.0";
        public string Chapter { get; set; } = "";
        /// <summary>部署叶子：生成的 mod 进哪个产物（root / chapter1..chapter5）。</summary>
        public string Leaf { get; set; } = "chapter1";
        public string Description { get; set; } = "";
        /// <summary>相对 Neutraled/ 的 mod 目录（扫描导入时填）。</summary>
        public string Path { get; set; } = "";
        public List<string> Dependencies { get; set; } = new();
        public List<string> LoadAfter { get; set; } = new();
        public List<string> LoadBefore { get; set; } = new();

        // 表格里的逗号文本框（不进 JSON，加载/保存时用 List 字段）。
        [JsonIgnore] public string DependenciesText => string.Join(", ", Dependencies);
        [JsonIgnore] public string LoadAfterText => string.Join(", ", LoadAfter);
        [JsonIgnore] public string LoadBeforeText => string.Join(", ", LoadBefore);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>部署叶子选项：生成器用 mod 目录的叶子决定目标产物。</summary>
    private static readonly string[] LeafChoices = { "root", "chapter1", "chapter2", "chapter3", "chapter4", "chapter5" };

    /// <summary>把任意 leaf 值归一成合法选项；空/非法时从 Path 末段推断，再退回 chapter1。</summary>
    public static string NormalizeLeaf(string? leaf, string? path = null)
    {
        string v = (leaf ?? "").Trim();
        if (IsValidLeaf(v)) return v.ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(path))
        {
            var last = path.Replace('\\', '/').TrimEnd('/').Split('/').LastOrDefault();
            if (IsValidLeaf(last)) return last!.ToLowerInvariant();
        }
        return "chapter1";
    }

    public static bool IsValidLeaf(string? leaf) =>
        !string.IsNullOrWhiteSpace(leaf) && LeafChoices.Contains(leaf!.Trim(), StringComparer.OrdinalIgnoreCase);

    private string _path;
    private readonly string? _defaultModsDir;
    private readonly BindingList<ManifestMod> _rows = new();
    private readonly DataGridView _grid = new();
    private readonly Label _info = new();
    private readonly Label _issues = new();

    public ModsManifestDialog(string path)
    {
        _path = path;
        _defaultModsDir = Path.Combine(Path.GetDirectoryName(path) ?? ".", "mods");

        Text = "mods.json — 整包 mod 清单";
        Size = new Size(1100, 620);
        MinimumSize = new Size(820, 420);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Bg1;
        ForeColor = Theme.Text;
        Font = Theme.UI(9f);
        KeyPreview = true;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Theme.Bg1 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        Controls.Add(root);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Bg1, Padding = new Padding(8, 6, 8, 4), WrapContents = false };
        bar.Controls.Add(MakeButton("打开…", OpenDialog));
        bar.Controls.Add(MakeButton("保存", () => Save(false)));
        bar.Controls.Add(MakeButton("另存为…", () => Save(true)));
        bar.Controls.Add(MakeButton("从 mods 目录导入…", ImportFromModsDir));
        bar.Controls.Add(MakeButton("添加条目", AddRow));
        bar.Controls.Add(MakeButton("删除条目", DeleteRow));
        bar.Controls.Add(MakeButton("上移", () => MoveRow(-1)));
        bar.Controls.Add(MakeButton("下移", () => MoveRow(1)));
        root.Controls.Add(bar, 0, 0);

        BuildGrid();
        root.Controls.Add(_grid, 0, 1);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Theme.Bg1, Padding = new Padding(8, 2, 8, 4) };
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        _issues.ForeColor = Theme.Orange;
        _issues.Dock = DockStyle.Fill;
        _info.ForeColor = Theme.TextDim;
        _info.Dock = DockStyle.Fill;
        footer.Controls.Add(_issues, 0, 0);
        footer.Controls.Add(_info, 0, 1);
        root.Controls.Add(footer, 0, 2);

        LoadFrom(_path);
    }

    private Button MakeButton(string text, Action onClick)
    {
        var b = new Button { Text = text, AutoSize = true, Height = 28, Margin = new Padding(0, 0, 6, 0) };
        Theme.StyleButton(b);
        b.Click += (s, e) => onClick();
        return b;
    }

    private void BuildGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.BackgroundColor = Theme.Bg0;
        _grid.GridColor = Theme.Line;
        _grid.BorderStyle = BorderStyle.None;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.AutoGenerateColumns = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = true;
        _grid.EditMode = DataGridViewEditMode.EditOnEnter;
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Bg2;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Text;
        _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.Bg3;
        _grid.DefaultCellStyle.BackColor = Theme.Bg1;
        _grid.DefaultCellStyle.ForeColor = Theme.Text;
        _grid.DefaultCellStyle.SelectionBackColor = Theme.Bg3;
        _grid.DefaultCellStyle.SelectionForeColor = Theme.Text;
        _grid.DefaultCellStyle.Font = Theme.UI(9f);
        _grid.DataSource = _rows;

        var enabled = new DataGridViewCheckBoxColumn
        {
            HeaderText = "启用",
            DataPropertyName = nameof(ManifestMod.Enabled),
            Name = nameof(ManifestMod.Enabled),
            Width = 44,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            ReadOnly = false,
            DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter },
        };
        _grid.Columns.Add(enabled);
        AddColumn("id", nameof(ManifestMod.Id), 170);
        AddColumn("名称", nameof(ManifestMod.Name), 160);
        AddColumn("作者", nameof(ManifestMod.Author), 100);
        AddColumn("版本", nameof(ManifestMod.Version), 70);
        AddColumn("章节", nameof(ManifestMod.Chapter), 80);
        var leaf = new DataGridViewComboBoxColumn
        {
            HeaderText = "部署叶子",
            DataPropertyName = nameof(ManifestMod.Leaf),
            Name = nameof(ManifestMod.Leaf),
            Width = 96,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            FlatStyle = FlatStyle.Flat,
        };
        leaf.Items.AddRange(LeafChoices);
        _grid.Columns.Add(leaf);
        AddColumn("路径", nameof(ManifestMod.Path), 180);
        AddColumn("依赖(逗号)", "DependenciesText", 130);
        AddColumn("加载在其后", "LoadAfterText", 130);
        AddColumn("加载在其前", "LoadBeforeText", 130);
        AddColumn("说明", nameof(ManifestMod.Description), 220);
        _grid.CellEndEdit += (s, e) => { CommitTextColumns(); ValidateAndShow(); };
        _grid.CurrentCellDirtyStateChanged += (s, e) =>
        {
            // 复选框/部署叶子下拉要立刻提交，否则保存时还是旧值。
            if (!_grid.IsCurrentCellDirty) return;
            if (_grid.CurrentCell is DataGridViewCheckBoxCell || _grid.CurrentCell is DataGridViewComboBoxCell)
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged += (s, e) => { if (e.ColumnIndex == 0) ValidateAndShow(); };
    }

    private void AddColumn(string header, string property, int width, bool check = false)
    {
        var col = new DataGridViewTextBoxColumn
        {
            HeaderText = header,
            DataPropertyName = property,
            Name = property,
            Width = width,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            ReadOnly = false,
        };
        if (check)
        {
            col.ReadOnly = true;
            col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        }
        _grid.Columns.Add(col);
    }

    /// <summary>列表字段在表格里用逗号文本编辑；提交时拆回 List。</summary>
    private void CommitTextColumns()
    {
        if (_grid.CurrentRow?.DataBoundItem is not ManifestMod row) return;
        foreach (var name in new[] { "DependenciesText", "LoadAfterText", "LoadBeforeText" })
        {
            int idx = _grid.Columns[name]?.Index ?? -1;
            if (idx < 0) continue;
            var text = _grid.Rows[_grid.CurrentRow.Index].Cells[idx].Value as string ?? "";
            var list = SplitList(text);
            if (name == "DependenciesText") row.Dependencies = list;
            else if (name == "LoadAfterText") row.LoadAfter = list;
            else row.LoadBefore = list;
        }
    }

    private static List<string> SplitList(string text) =>
        text.Split(new[] { ',', '，', ';', '；', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private void RefreshGrid()
    {
        _grid.DataSource = null;
        _grid.DataSource = _rows;
        ValidateAndShow();
    }

    private void AddRow()
    {
        _rows.Add(new ManifestMod { Id = "new.mod", Name = "New Mod", Author = "Neutraled" });
        RefreshGrid();
        if (_grid.Rows.Count > 0) _grid.CurrentCell = _grid.Rows[_grid.Rows.Count - 1].Cells[1];
    }

    private void DeleteRow()
    {
        var selected = _grid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.DataBoundItem).OfType<ManifestMod>().ToList();
        if (selected.Count == 0 && _grid.CurrentRow?.DataBoundItem is ManifestMod cur) selected.Add(cur);
        foreach (var row in selected) _rows.Remove(row);
        RefreshGrid();
    }

    private void MoveRow(int delta)
    {
        if (_grid.CurrentRow?.DataBoundItem is not ManifestMod row) return;
        int i = _rows.IndexOf(row);
        int j = i + delta;
        if (i < 0 || j < 0 || j >= _rows.Count) return;
        _rows.RemoveAt(i);
        _rows.Insert(j, row);
        RefreshGrid();
        _grid.CurrentCell = _grid.Rows[j].Cells[1];
    }

    // -------------------------------------------------------------- load / save

    private void OpenDialog()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "打开 mods.json",
            Filter = "mods.json|mods.json|JSON|*.json|所有文件|*.*",
            FileName = Path.GetFileName(_path),
        };
        try { dlg.InitialDirectory = Path.GetDirectoryName(_path); } catch { }
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        LoadFrom(dlg.FileName);
    }

    private void LoadFrom(string path)
    {
        _rows.Clear();
        _path = path;
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var file = JsonSerializer.Deserialize<ManifestFile>(json, JsonOptions) ?? new ManifestFile();
                foreach (var m in file.Mods ?? new List<ManifestMod>())
                {
                    m.Dependencies ??= new List<string>();
                    m.LoadAfter ??= new List<string>();
                    m.LoadBefore ??= new List<string>();
                    m.Leaf = NormalizeLeaf(m.Leaf, m.Path);
                    _rows.Add(m);
                }
                _info.Text = "已载入 " + path + "（" + _rows.Count + " 个 mod）";
            }
            else
            {
                _info.Text = "文件不存在，保存时会新建：" + path;
            }
        }
        catch (Exception ex)
        {
            _info.Text = "读取失败：" + ex.Message;
        }
        RefreshGrid();
    }

    private void Save(bool saveAs)
    {
        CommitTextColumns();
        string path = _path;
        if (saveAs)
        {
            using var dlg = new SaveFileDialog
            {
                Title = "保存 mods.json",
                Filter = "mods.json|mods.json|JSON|*.json|所有文件|*.*",
                FileName = Path.GetFileName(_path),
            };
            try { dlg.InitialDirectory = Path.GetDirectoryName(_path); } catch { }
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            path = dlg.FileName;
        }
        if (!_rows.Any(m => !string.IsNullOrWhiteSpace(m.Id)))
        {
            var r = MessageBox.Show(this, "清单是空的（没有任何 mod）。仍要保存吗？", "mods.json",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;
        }
        try
        {
            if (File.Exists(path)) File.Copy(path, path + ".bak", true);
            var file = new ManifestFile { Version = 1, Mods = _rows.ToList() };
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            File.WriteAllText(path, JsonSerializer.Serialize(file, JsonOptions));
            _path = path;
            _info.Text = "已保存 " + path + "（" + _rows.Count + " 个 mod）";
            ValidateAndShow();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "保存失败：" + ex.Message, "mods.json", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ------------------------------------------------------------- mods scan

    // ------------------------------------------------- 可复用的静态逻辑（CLI 测试用）

    /// <summary>扫描目录下的 mod.json，返回一份新清单（不依赖 GUI）。</summary>
    public static ManifestFile ScanMods(string modsDir)
    {
        var file = new ManifestFile();
        if (!Directory.Exists(modsDir)) return file;
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(modsDir, "mod.json", SearchOption.AllDirectories))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var root = doc.RootElement;
                string id = Str(root, "id");
                if (string.IsNullOrWhiteSpace(id) || !known.Add(id)) continue;
                string rel = RelativeTo(modsDir, Path.GetDirectoryName(path) ?? "");
                var entry = new ManifestMod
                {
                    Id = id,
                    Name = Str(root, "name"),
                    Author = Str(root, "author"),
                    Version = Str(root, "version"),
                    Enabled = Bool(root, "enabled", true),
                    Chapter = Str(root, "chapter"),
                    Leaf = NormalizeLeaf(Str(root, "leaf"), rel),
                    Description = Str(root, "description"),
                    Path = rel,
                };
                if (string.IsNullOrWhiteSpace(entry.Version)) entry.Version = "1.0.0";
                file.Mods.Add(entry);
            }
            catch
            {
                // 单个坏 mod.json 不阻塞整包导入。
            }
        }
        return file;
    }

    /// <summary>校验清单：返回错误（id 缺失/重复）。</summary>
    public static List<string> Validate(ManifestFile file)
    {
        var errors = new List<string>();
        var ids = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in file.Mods)
        {
            if (string.IsNullOrWhiteSpace(m.Id)) errors.Add("有条目缺少 id");
            else if (ids.ContainsKey(m.Id)) errors.Add("id 重复：" + m.Id);
            if (!string.IsNullOrWhiteSpace(m.Id)) ids[m.Id] = ids.TryGetValue(m.Id, out int c) ? c + 1 : 1;
            if (!IsValidLeaf(m.Leaf)) errors.Add("部署叶子非法：" + (string.IsNullOrWhiteSpace(m.Id) ? "?" : m.Id) + "（" + m.Leaf + "，应为 root/chapter1..chapter5）");
        }
        return errors;
    }

    /// <summary>保存清单（.bak 备份 + 建目录 + UTF-8，不依赖 GUI）。</summary>
    public static void SaveTo(ManifestFile file, string path)
    {
        var full = Path.GetFullPath(path);
        if (Directory.Exists(full)) throw new IOException("输出路径是目录，请指定文件名: " + full);
        if (File.Exists(full)) File.Copy(full, full + ".bak", true);
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(full, JsonSerializer.Serialize(file, JsonOptions));
    }

    /// <summary>读取清单（不存在返回空清单）。</summary>
    public static ManifestFile LoadFromFile(string path)
    {
        if (!File.Exists(path)) return new ManifestFile();
        var file = JsonSerializer.Deserialize<ManifestFile>(File.ReadAllText(path), JsonOptions) ?? new ManifestFile();
        file.Mods ??= new List<ManifestMod>();
        foreach (var m in file.Mods)
        {
            m.Dependencies ??= new List<string>();
            m.LoadAfter ??= new List<string>();
            m.LoadBefore ??= new List<string>();
            m.Leaf = NormalizeLeaf(m.Leaf, m.Path);
        }
        return file;
    }

    /// <summary>扫描 Neutraled/mods 下的 mod.json，把还没进清单的 mod 追加进来。</summary>
    public int ScanModsDir(string modsDir)
    {
        if (!Directory.Exists(modsDir)) return 0;
        var known = new HashSet<string>(_rows.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
        int added = 0;
        foreach (var file in Directory.EnumerateFiles(modsDir, "mod.json", SearchOption.AllDirectories))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;
                string id = Str(root, "id");
                if (string.IsNullOrWhiteSpace(id) || known.Contains(id)) continue;
                string rel = RelativeTo(modsDir, Path.GetDirectoryName(file) ?? "");
                var entry = new ManifestMod
                {
                    Id = id,
                    Name = Str(root, "name"),
                    Author = Str(root, "author"),
                    Version = Str(root, "version"),
                    Enabled = Bool(root, "enabled", true),
                    Chapter = Str(root, "chapter"),
                    Leaf = NormalizeLeaf(Str(root, "leaf"), rel),
                    Description = Str(root, "description"),
                    Path = rel,
                };
                if (string.IsNullOrWhiteSpace(entry.Version)) entry.Version = "1.0.0";
                _rows.Add(entry);
                known.Add(id);
                added++;
            }
            catch
            {
                // 单个坏 mod.json 不阻塞整包导入。
            }
        }
        if (added > 0) RefreshGrid();
        _info.Text = "从 " + modsDir + " 导入 " + added + " 个 mod（已在清单里的跳过）";
        return added;
    }

    private static string RelativeTo(string root, string dir)
    {
        try
        {
            var rel = Path.GetRelativePath(root, dir);
            return rel == "." ? "" : rel.Replace('\\', '/');
        }
        catch { return dir; }
    }

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";

    private static bool Bool(JsonElement e, string name, bool dflt) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) &&
        (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : dflt;

    private void ImportFromModsDir()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "选择 Neutraled/mods 目录（或含 mod.json 的目录）",
            UseDescriptionForTitle = true,
        };
        if (Directory.Exists(_defaultModsDir)) dlg.SelectedPath = _defaultModsDir;
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        ScanModsDir(dlg.SelectedPath);
    }

    // ------------------------------------------------------------ validation

    private void ValidateAndShow()
    {
        var errors = new List<string>();
        var warns = new List<string>();
        var ids = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in _rows)
        {
            if (string.IsNullOrWhiteSpace(m.Id)) errors.Add("有条目缺少 id");
            else if (ids.TryGetValue(m.Id, out int n)) errors.Add("id 重复：" + m.Id + "（" + n + " 次）");
            ids[m.Id] = ids.TryGetValue(m.Id, out int c) ? c + 1 : 1;
        }
        foreach (var m in _rows)
        {
            if (!IsValidLeaf(m.Leaf)) warns.Add((string.IsNullOrWhiteSpace(m.Id) ? "有条目" : m.Id) + " 的部署叶子无效：" + m.Leaf + "（可选 root/chapter1..chapter5）");
            foreach (var dep in m.Dependencies)
                if (dep.Length > 0 && !ids.ContainsKey(dep)) warns.Add(m.Id + " 依赖的 " + dep + " 不在清单里");
            foreach (var dep in m.LoadAfter)
                if (dep.Length > 0 && !ids.ContainsKey(dep)) warns.Add(m.Id + " 要加载在 " + dep + " 之后，但清单里没有它");
        }
        _issues.ForeColor = errors.Count > 0 ? Theme.Red : Theme.Orange;
        _issues.Text = errors.Count > 0
            ? "错误：" + string.Join("；", errors.Distinct().Take(4)) + (errors.Count > 4 ? " …" : "")
            : warns.Count > 0
                ? "警告：" + string.Join("；", warns.Distinct().Take(3)) + (warns.Count > 3 ? " …" : "")
                : "清单有效：" + _rows.Count + " 个 mod";
    }
}
