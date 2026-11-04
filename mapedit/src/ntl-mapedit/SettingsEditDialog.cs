using System.Drawing;
using System.Windows.Forms;

namespace NtlMapEdit;

/// <summary>
/// 编辑生成 mod 的「Mod 设置项」：这些项会注册进游戏内 Neutraled 的「Mod 设置」面板
/// （ntl_menu_add），玩家按 Z 切换后通过 feature 实时影响战斗/对话/弹幕伤害。
/// </summary>
public sealed class SettingsEditDialog : Form
{
    private const string FeatureNone = "none";
    private const string FeatureBattles = "battlesEnabled";
    private const string FeatureOverworld = "overworldEnabled";
    private const string FeatureDialogues = "dialoguesEnabled";
    private const string FeatureCutscenes = "cutscenesEnabled";
    private const string FeatureBarrage = "barrageDamagePercent";

    private static readonly string[] Features = { FeatureNone, FeatureBattles, FeatureOverworld, FeatureDialogues, FeatureCutscenes, FeatureBarrage };
    private static readonly string[] Types = { "bool", "choice", "int" };

    private readonly List<SettingDef> _original;
    private readonly DataGridView _grid = new();
    private readonly Label _issues = new();

    public List<SettingDef> Result { get; private set; } = new();

    public SettingsEditDialog(List<SettingDef>? settings)
    {
        _original = settings ?? new List<SettingDef>();
        Text = "Mod 设置项（游戏内「Mod 设置」面板）";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1080, 600);
        MinimumSize = new Size(900, 480);
        BackColor = Theme.Bg1;
        ForeColor = Theme.Text;
        Font = Theme.UI(9f);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;

        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 46,
            Padding = new Padding(10, 8, 0, 0),
            BackColor = Theme.Bg1,
        };
        var add = new Button { Text = "添加设置项", Width = 112, Height = 28 };
        var del = new Button { Text = "删除选中", Width = 96, Height = 28 };
        var up = new Button { Text = "上移", Width = 72, Height = 28 };
        var down = new Button { Text = "下移", Width = 72, Height = 28 };
        foreach (var b in new[] { add, del, up, down }) { Theme.StyleButton(b); top.Controls.Add(b); }
        add.Click += (_, _) => { _grid.Rows.Add(); if (_grid.Rows.Count > 0) _grid.CurrentCell = _grid.Rows[^1].Cells[0]; };
        del.Click += (_, _) => { if (_grid.CurrentRow != null) _grid.Rows.Remove(_grid.CurrentRow); };
        up.Click += (_, _) => MoveRow(-1);
        down.Click += (_, _) => MoveRow(1);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Theme.Bg1 };
        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Right,
            Width = 240,
            Padding = new Padding(0, 12, 10, 0),
        };
        var save = new Button { Text = "保存", Width = 90, Height = 30 };
        var cancel = new Button { Text = "取消", Width = 90, Height = 30, DialogResult = DialogResult.Cancel };
        Theme.StyleButton(save, true);
        Theme.StyleButton(cancel);
        save.Click += (_, _) => { if (Commit()) { DialogResult = DialogResult.OK; } };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        bottom.Controls.Add(buttons);

        _issues.Dock = DockStyle.Fill;
        _issues.TextAlign = ContentAlignment.MiddleLeft;
        _issues.Padding = new Padding(12, 0, 0, 0);
        Theme.StyleLabel(_issues, Theme.TextDim);
        bottom.Controls.Add(_issues);

        BuildGrid();

        Controls.Add(_grid);
        Controls.Add(top);
        Controls.Add(bottom);
        AcceptButton = save;
        CancelButton = cancel;
        Shown += (_, _) => { Theme.EnableDarkTitleBar(this); _grid.Focus(); };
    }

    private void BuildGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.BackgroundColor = Theme.Bg2;
        _grid.GridColor = Theme.Line;
        _grid.BorderStyle = BorderStyle.None;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.EditMode = DataGridViewEditMode.EditOnEnter;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Bg3;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Text;
        _grid.ColumnHeadersDefaultCellStyle.Font = Theme.UI(8.5f, FontStyle.Bold);
        _grid.DefaultCellStyle.BackColor = Theme.Bg2;
        _grid.DefaultCellStyle.ForeColor = Theme.Text;
        _grid.DefaultCellStyle.SelectionBackColor = Theme.AccentDim;
        _grid.DefaultCellStyle.SelectionForeColor = Theme.Text;
        _grid.EnableHeadersVisualStyles = false;
        _grid.Font = Theme.UI(9f);
        _grid.Columns.Add(TextCol("id", "id", 130));
        _grid.Columns.Add(TextCol("label", "显示名", 110));
        var type = new DataGridViewComboBoxColumn { HeaderText = "类型", Name = "type", Width = 76, FlatStyle = FlatStyle.Flat };
        type.Items.AddRange(Types);
        _grid.Columns.Add(type);
        _grid.Columns.Add(TextCol("value", "默认值", 84));
        var feature = new DataGridViewComboBoxColumn { HeaderText = "功能绑定", Name = "feature", Width = 150, FlatStyle = FlatStyle.Flat };
        feature.Items.AddRange(Features);
        _grid.Columns.Add(feature);
        _grid.Columns.Add(TextCol("options", "choice 选项（label=value,…）", 200));
        _grid.Columns.Add(TextCol("min", "min", 52));
        _grid.Columns.Add(TextCol("max", "max", 52));
        _grid.Columns.Add(TextCol("step", "step", 52));
        _grid.Columns.Add(TextCol("description", "说明（面板里显示）", 220));

        foreach (var s in _original) AddRow(s);
        if (_grid.Rows.Count == 0) AddRow(new SettingDef());
    }

    private static DataGridViewTextBoxColumn TextCol(string name, string header, int width) => new()
    {
        Name = name,
        HeaderText = header,
        Width = width,
        SortMode = DataGridViewColumnSortMode.NotSortable,
    };

    private void AddRow(SettingDef s)
    {
        int i = _grid.Rows.Add();
        var row = _grid.Rows[i];
        row.Cells["id"].Value = s.Id ?? "";
        row.Cells["label"].Value = s.Label ?? "";
        row.Cells["type"].Value = Types.Contains(s.Type) ? s.Type : "bool";
        row.Cells["value"].Value = s.Value ?? "";
        row.Cells["feature"].Value = Features.Contains(s.Feature) ? s.Feature : FeatureNone;
        row.Cells["options"].Value = s.Options == null || s.Options.Count == 0
            ? ""
            : string.Join(",", s.Options.Select(o => o.Label + "=" + o.Value));
        row.Cells["min"].Value = s.Min.ToString();
        row.Cells["max"].Value = s.Max.ToString();
        row.Cells["step"].Value = s.Step.ToString();
        row.Cells["description"].Value = s.Description ?? "";
    }

    private void MoveRow(int delta)
    {
        if (_grid.CurrentRow == null) return;
        int from = _grid.CurrentRow.Index;
        int to = from + delta;
        if (to < 0 || to >= _grid.Rows.Count) return;
        var values = _grid.Rows[from].Cells.Cast<DataGridViewCell>().Select(c => c.Value).ToArray();
        _grid.Rows.RemoveAt(from);
        _grid.Rows.Insert(to, 1);
        for (int i = 0; i < values.Length; i++) _grid.Rows[to].Cells[i].Value = values[i];
        _grid.CurrentCell = _grid.Rows[to].Cells[0];
    }

    private static string Cell(DataGridViewRow row, string name) => row.Cells[name].Value?.ToString()?.Trim() ?? "";

    private static int IntCell(DataGridViewRow row, string name, int def)
    {
        return int.TryParse(Cell(row, name), out int v) ? v : def;
    }

    private bool Commit()
    {
        var list = new List<SettingDef>();
        var issues = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _grid.EndEdit();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow) continue;
            string id = Cell(row, "id");
            if (id.Length == 0) continue;
            if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[A-Za-z0-9_.-]{1,48}$"))
            {
                issues.Add($"id 只能用字母/数字/下划线/点/短横线：{id}");
                continue;
            }
            if (!seen.Add(id)) { issues.Add($"id 重复：{id}"); continue; }
            string type = Cell(row, "type");
            if (type != "bool" && type != "choice" && type != "int") type = "bool";
            var def = new SettingDef
            {
                Id = id,
                Label = Cell(row, "label") is { Length: > 0 } label ? label : id,
                Type = type,
                Value = Cell(row, "value"),
                Feature = Cell(row, "feature"),
                Description = Cell(row, "description"),
                Min = IntCell(row, "min", 0),
                Max = IntCell(row, "max", 100),
                Step = IntCell(row, "step", 10),
            };
            if (!Features.Contains(def.Feature)) def.Feature = FeatureNone;
            if (type == "choice")
            {
                foreach (var part in Cell(row, "options").Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int eq = part.IndexOf('=');
                    if (eq <= 0 || eq >= part.Length - 1) continue;
                    def.Options.Add(new SettingOption { Label = part[..eq].Trim(), Value = part[(eq + 1)..].Trim() });
                }
                if (def.Options.Count < 2) issues.Add($"{id}：choice 类型至少需要 2 个选项（写成 普通=normal,困难=hard）");
                if (def.Options.Count > 0 && !def.Options.Any(o => o.Value == def.Value)) def.Value = def.Options[0].Value;
            }
            else if (type == "bool")
            {
                def.Value = def.Value is "0" or "false" or "off" ? "0" : "1";
            }
            else
            {
                if (!int.TryParse(def.Value, out int iv)) iv = def.Min;
                def.Value = Math.Clamp(iv, def.Min, def.Max).ToString();
            }
            list.Add(def);
        }

        var duplicatedFeatures = list.Where(s => s.Feature != FeatureNone).GroupBy(s => s.Feature)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicatedFeatures.Count > 0) issues.Add("同一功能被多个设置项绑定（运行时取第一个）：" + string.Join(",", duplicatedFeatures));

        if (issues.Count > 0)
        {
            _issues.Text = string.Join("；", issues);
            _issues.ForeColor = Theme.Red;
            return false;
        }
        Result = list;
        _issues.Text = $"有效：{list.Count} 个设置项";
        _issues.ForeColor = Theme.Green;
        return true;
    }
}
