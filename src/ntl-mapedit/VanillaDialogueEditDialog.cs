using System.Drawing;
using System.Windows.Forms;
using UndertaleModLib.Models;

namespace NtlMapEdit;

/// <summary>
/// 打开原版 data 时直接改写原版对话文本：列出当前房间里所有「能定位到 lang key」的原版对话行，
/// 只改右侧「覆盖文本」列；保存进 sidecar 的 TextOverrides，生成 mod 时由 ntl_set_lang_string 覆盖，
/// 不改 data.win、不需要重打包。
/// </summary>
public sealed class VanillaDialogueEditDialog : Form
{
    private sealed class Row
    {
        public string ObjectName = "";
        public string EventName = "";
        public string Condition = "";
        public string Key = "";
        public string Original = "";
        public string Override = "";
    }

    private readonly BattleProject _battles;
    private readonly string _roomName;
    private readonly List<Row> _rows = new();
    private readonly DataGridView _grid = new();
    private readonly TextBox _filter = new();
    private readonly TextBox _quickText = new();
    private readonly Label _issues = new();
    private readonly Label _summary = new();

    public VanillaDialogueEditDialog(VanillaCode vanilla, UndertaleRoom room, BattleProject battles)
    {
        _battles = battles;
        _roomName = GameData.Name(room.Name);
        Text = "编辑原版对话文本 —— " + _roomName;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1560, 820);
        MinimumSize = new Size(1200, 640);
        BackColor = Theme.Bg1;
        ForeColor = Theme.Text;
        Font = Theme.UI(9f);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 0,
            Padding = new Padding(14, 10, 14, 10),
            BackColor = Theme.Bg1,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var head = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 0, BackColor = Theme.Bg1 };
        head.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var filterRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Bg1 };
        filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var fl = new Label { Text = "筛选", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        Theme.StyleLabel(fl, Theme.TextDim);
        _filter.Dock = DockStyle.Fill;
        _filter.PlaceholderText = "按对象 / key / 原文过滤…";
        Theme.StyleTextBox(_filter);
        _filter.TextChanged += (_, _) => RebuildGrid();
        filterRow.Controls.Add(fl, 0, 0);
        filterRow.Controls.Add(_filter, 1, 0);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.Controls.Add(filterRow, 0, root.RowCount);
        root.RowCount++;

        BuildGrid();
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(_grid, 0, root.RowCount);
        root.RowCount++;

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Height = 46,
            BackColor = Theme.Bg1,
            Padding = new Padding(0, 8, 8, 0),
        };
        var quickLabel = new Label { Text = "选中行改写为", AutoSize = true, Margin = new Padding(4, 8, 6, 0) };
        Theme.StyleLabel(quickLabel, Theme.TextDim);
        _quickText.Width = 300;
        _quickText.Height = 26;
        _quickText.PlaceholderText = "输入新文本（留空=不改），再点「应用到选中行」";
        Theme.StyleTextBox(_quickText);
        var applySel = new Button { Text = "应用到选中行", Width = 108, Height = 28 };
        Theme.StyleButton(applySel);
        applySel.Click += (_, _) =>
        {
            var text = _quickText.Text ?? "";
            var rows = _grid.SelectedRows.Cast<DataGridViewRow>().ToList();
            if (rows.Count == 0 && _grid.Rows.Count > 0) rows.Add(_grid.Rows[0]);
            foreach (var r in rows)
            {
                if (r.Tag is Row row) row.Override = text;
                r.Cells["override"].Value = text;
            }
            _issues.Text = rows.Count > 0
                ? string.Format("  已把 {0} 行改写为「{1}」（保存覆盖后才写入配置）", rows.Count, text)
                : "  没有可应用的行";
            _issues.ForeColor = Theme.TextDim;
        };
        var save = new Button { Text = "保存覆盖", Width = 100, Height = 28 };
        var clearSel = new Button { Text = "清除选中", Width = 92, Height = 28 };
        var close = new Button { Text = "取消", Width = 78, Height = 28, DialogResult = DialogResult.Cancel };
        Theme.StyleButton(save, true);
        Theme.StyleButton(clearSel);
        Theme.StyleButton(close);
        bottom.Controls.Add(quickLabel);
        bottom.Controls.Add(_quickText);
        bottom.Controls.Add(applySel);
        bottom.Controls.Add(save);
        bottom.Controls.Add(clearSel);
        _issues.AutoSize = false;
        _issues.Width = 420;
        _issues.Height = 28;
        _issues.TextAlign = ContentAlignment.MiddleLeft;
        _issues.ForeColor = Theme.TextDim;
        bottom.Controls.Add(_issues);
        _summary.AutoSize = false;
        _summary.Width = 520;
        _summary.Height = 28;
        _summary.TextAlign = ContentAlignment.MiddleLeft;
        _summary.ForeColor = Theme.TextDim;
        bottom.Controls.Add(_summary);
        bottom.Controls.Add(close);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.Controls.Add(bottom, 0, root.RowCount);
        root.RowCount++;

        Controls.Add(root);
        AcceptButton = save;
        CancelButton = close;
        save.Click += (_, _) => { if (Commit()) DialogResult = DialogResult.OK; };
        clearSel.Click += (_, _) =>
        {
            foreach (DataGridViewRow r in _grid.SelectedRows)
            {
                if (r.Tag is Row row) r.Cells["override"].Value = "";
            }
        };

        Scan(vanilla, room);
        RebuildGrid();
    }

    private void Scan(VanillaCode vanilla, UndertaleRoom room)
    {
        var idx = vanilla.RoomIndex(room);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, inst) in RoomEdit.AllInstances(room))
        {
            var obj = inst.ObjectDefinition;
            if (obj == null) continue;
            var info = vanilla.ScanObject(obj, idx, inst.X, inst.Y, quick: true);
            foreach (var ev in info.Events)
            {
                foreach (var line in ev.Lines)
                {
                    var key = (line.Key ?? "").Trim();
                    if (key.Length == 0) continue;
                    if (!seen.Add(key)) continue;
                    var existing = _battles.TextOverrideFor(key);
                    _rows.Add(new Row
                    {
                        ObjectName = info.ObjectName,
                        EventName = ev.Friendly,
                        Condition = line.Condition ?? "",
                        Key = key,
                        Original = line.Text ?? "",
                        Override = existing?.Text ?? "",
                    });
                }
            }
            if (_rows.Count > 5000) break;
        }
        _summary.Text = _rows.Count > 0
            ? string.Format("  共 {0} 条可改写的原版对话（按 lang key 定位；只在有 key 的行出现）", _rows.Count)
            : "  当前房间没有扫描到可改写的原版对话行（有些文本是运行期拼的，没有 lang key）";
    }

    private void BuildGrid()
    {
        _grid.SelectionChanged += (_, _) =>
        {
            if (_grid.SelectedRows.Count > 0 && _grid.SelectedRows[0].Tag is Row r)
            {
                _quickText.Text = r.Override;
            }
        };
        _grid.Dock = DockStyle.Fill;
        _grid.BackgroundColor = Theme.Bg1;
        _grid.BorderStyle = BorderStyle.None;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
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
        _grid.Columns.Add(TextCol("objectName", "对象", 170, true));
        _grid.Columns.Add(TextCol("eventName", "事件", 120, true));
        _grid.Columns.Add(TextCol("condition", "条件", 180, true));
        _grid.Columns.Add(TextCol("key", "lang key", 320, true));
        _grid.Columns.Add(TextCol("original", "原版文本", 330, true));
        _grid.Columns.Add(TextCol("override", "覆盖文本（可编辑；清空=不改）", 330, false));
        _grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (_grid.Columns[e.ColumnIndex].Name != "override") return;
            if (_grid.Rows[e.RowIndex].Tag is Row r && !string.IsNullOrEmpty(r.Override))
                e.CellStyle.ForeColor = Theme.Green;
        };
    }

    private static DataGridViewTextBoxColumn TextCol(string name, string header, int width, bool readOnly) => new()
    {
        Name = name,
        HeaderText = header,
        Width = width,
        ReadOnly = readOnly,
        SortMode = DataGridViewColumnSortMode.NotSortable,
    };

    private string Filter => (_filter.Text ?? "").Trim();

    private void RebuildGrid()
    {
        _grid.Rows.Clear();
        var f = Filter;
        foreach (var r in _rows)
        {
            if (f.Length > 0
                && r.ObjectName.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0
                && r.EventName.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0
                && r.Key.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0
                && r.Original.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0
                && r.Override.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0) continue;
            int i = _grid.Rows.Add(r.ObjectName, r.EventName, r.Condition, r.Key, r.Original, r.Override);
            _grid.Rows[i].Tag = r;
        }
    }

    /// <summary>把表格里的「覆盖文本」写回 battles.File.TextOverrides（按 key 去重/删除）。</summary>
    public bool Commit()
    {
        var byKey = new Dictionary<string, Row>(StringComparer.Ordinal);
        foreach (var r in _rows) if (r.Key.Length > 0) byKey[r.Key] = r;
        // 先把本次可见的编辑结果收集到行对象（过滤时没显示的行保持原值）。
        foreach (DataGridViewRow gr in _grid.Rows)
        {
            if (gr.Tag is not Row row) continue;
            var v = gr.Cells["override"].Value;
            row.Override = v == null ? "" : v.ToString() ?? "";
        }
        var bad = _rows.FirstOrDefault(r => r.Override.Contains('%'));
        if (bad != null)
        {
            _issues.Text = "覆盖文本不能包含 %（原版对话分页符）: " + bad.Key;
            return false;
        }
        int changed = 0;
        _battles.File.TextOverrides ??= new List<TextOverrideDef>();
        // 删掉本房间所有「不再覆盖/覆盖清空」的旧条目，再写入新条目。
        var keep = new List<TextOverrideDef>();
        foreach (var t in _battles.File.TextOverrides)
        {
            if (!string.Equals(t.Room, _roomName, StringComparison.Ordinal)) { keep.Add(t); continue; }
            if (byKey.TryGetValue(t.Key, out var r))
            {
                if (string.IsNullOrWhiteSpace(r.Override)) { changed++; continue; }
            }
            keep.Add(t);
        }
        foreach (var r in _rows)
        {
            var text = (r.Override ?? "").Trim();
            if (text.Length == 0) continue;
            if (string.Equals(text, (r.Original ?? "").Trim(), StringComparison.Ordinal)) continue;
            var existing = keep.FirstOrDefault(t => string.Equals(t.Key, r.Key, StringComparison.Ordinal));
            if (existing == null)
            {
                keep.Add(new TextOverrideDef
                {
                    Key = r.Key,
                    Text = text,
                    Room = _roomName,
                    ObjectName = r.ObjectName,
                    EventName = r.EventName,
                    Original = r.Original,
                });
            }
            else
            {
                existing.Text = text;
                existing.Room = _roomName;
                existing.ObjectName = r.ObjectName;
                existing.EventName = r.EventName;
                existing.Original = r.Original;
            }
            changed++;
        }
        _battles.File.TextOverrides = keep;
        _issues.Text = "";
        _summary.Text = changed > 0
            ? string.Format("  已更新 {0} 条覆盖（保存文档时会写 sidecar 并按需生成 mod）", changed)
            : "  没有改动";
        return true;
    }
}
