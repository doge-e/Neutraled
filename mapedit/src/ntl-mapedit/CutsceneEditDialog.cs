using System.Drawing;
using System.Windows.Forms;

namespace NtlMapEdit;

/// <summary>
/// 绑定在地图实体上的「简单剧情」编辑器：进入区域/按 Z 触发，然后按步骤让人物移动、转向、
/// 换动作、播对话、设 flag，最后执行结束 effects。只编辑一个 <see cref="CutsceneDef"/>，
/// 保存由调用方（EditorForm）写 sidecar。
/// </summary>
public sealed class CutsceneEditDialog : Form
{
    private static readonly string[] Kinds =
    {
        "wait", "move", "face", "action", "dialogue", "flag", "message", "sound", "shake", "waitinput",
        "event", "script",
    };
    private static readonly string[] Actors = { "player", "trigger" };
    private static readonly string[] Dirs = { "down", "right", "up", "left" };

    private readonly CutsceneDef _target;
    private readonly string _room;
    private readonly TextBox _id = new();
    private readonly TextBox _name = new();
    private readonly ComboBox _mode = new();
    private readonly CheckBox _enabled = new();
    private readonly TextBox _flag = new();
    private readonly TextBox _radius = new();
    private readonly CheckBox _once = new();
    private readonly CheckBox _persistOnce = new();
    private readonly DataGridView _grid = new();
    private readonly TextBox _endSetFlags = new();
    private readonly TextBox _endClearFlags = new();
    private readonly TextBox _endHeal = new();
    private readonly TextBox _endGoto = new();
    private readonly TextBox _endMessage = new();
    private readonly Label _issues = new();

    /// <summary>「插入原版代码」可选的事件清单（由 EditorForm 传入当前房间/对象的原版事件）。</summary>
    private readonly IReadOnlyList<VanillaEventInfo> _vanillaEvents;

    public CutsceneEditDialog(CutsceneDef cutscene, string room, uint instanceId, string objectName,
        IReadOnlyList<VanillaEventInfo>? vanillaEvents = null)
    {
        _target = cutscene;
        _room = room;
        _vanillaEvents = vanillaEvents ?? Array.Empty<VanillaEventInfo>();
        Text = $"剧情编辑 —— {room} / {objectName} #{instanceId}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1720, 800);
        MinimumSize = new Size(1500, 700);
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

        // ---------------- 头部：基本 + 触发
        var head = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 0, BackColor = Theme.Bg1 };
        head.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        head.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        AddPair(head, "剧情 ID", _id, "名称", _name);
        _id.Text = cutscene.Id;
        _name.Text = cutscene.Name;
        _mode.DropDownStyle = ComboBoxStyle.DropDownList;
        _mode.Items.AddRange(new object[] { "interact（靠近按 Z 触发）", "area（进入触发半径自动播放）" });
        _mode.SelectedIndex = cutscene.Mode == "area" ? 1 : 0;
        _mode.BackColor = Theme.Bg2;
        _mode.ForeColor = Theme.Text;
        AddPair(head, "触发方式", _mode, "启用", _enabled);
        _enabled.Checked = cutscene.Enabled;
        _flag.Text = (cutscene.Trigger?.RequiredFlag ?? 0).ToString();
        _radius.Text = (cutscene.Trigger?.Radius > 0 ? cutscene.Trigger.Radius : 64).ToString();
        _once.Checked = cutscene.Trigger?.Once ?? true;
        _persistOnce.Checked = cutscene.Trigger?.PersistOnce ?? false;
        AddPair(head, "需要 flag（0=无）", _flag, "触发半径（像素）", _radius);
        AddPair(head, "只触发一次", _once, "跨重启只触发一次", _persistOnce);

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
        root.Controls.Add(head, 0, root.RowCount);
        root.RowCount++;

        // ---------------- 步骤工具条
        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Bg1, Padding = new Padding(0, 4, 0, 4) };
        var add = new Button { Text = "添加步骤", Width = 96, Height = 26 };
        var del = new Button { Text = "删除选中", Width = 88, Height = 26 };
        var up = new Button { Text = "上移", Width = 68, Height = 26 };
        var down = new Button { Text = "下移", Width = 68, Height = 26 };
        var vanilla = new Button { Text = "插入原版代码…", Width = 128, Height = 26 };
        foreach (var b in new[] { add, del, up, down, vanilla }) { Theme.StyleButton(b); tools.Controls.Add(b); }
        vanilla.Enabled = _vanillaEvents.Count > 0;
        vanilla.Click += (_, _) => InsertVanillaCode();
        add.Click += (_, _) => AddRow(new CutsceneStep { Kind = "wait" });
        del.Click += (_, _) => { if (_grid.CurrentRow != null) _grid.Rows.Remove(_grid.CurrentRow); };
        up.Click += (_, _) => MoveRow(-1);
        down.Click += (_, _) => MoveRow(1);
        var tip = new Label
        {
            Text = "  步骤按顺序播放；actor: player=玩家，trigger=本段剧情绑定的实体；对话行用 | 分隔；sound 填音效名、shake 用「帧数=持续 / X=强度」、waitinput 等玩家按 Z/Enter；"
                   + "event/script = 执行原版事件/脚本（「插入原版代码…」按对象事件挑，帧数=执行后等待）",
            AutoSize = false,
            Height = 26,
            Width = 900,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Theme.TextDim,
        };
        Theme.StyleLabel(tip, Theme.TextDim);
        tools.Controls.Add(tip);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        root.Controls.Add(tools, 0, root.RowCount);
        root.RowCount++;

        // ---------------- 步骤表格
        BuildGrid();
        foreach (var st in cutscene.Steps) AddRow(st);
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(_grid, 0, root.RowCount);
        root.RowCount++;

        // ---------------- 结束效果
        var endTitle = new Label
        {
            Text = "── 结束效果（可选） ──",
            AutoSize = false,
            Height = 24,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        Theme.StyleLabel(endTitle, Theme.Accent, true);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        root.Controls.Add(endTitle, 0, root.RowCount);
        root.RowCount++;

        var end = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 0, BackColor = Theme.Bg1 };
        end.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        end.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddSimpleRow(end, "设置 flag（逗号）", _endSetFlags, Join(cutscene.End?.SetFlags));
        AddSimpleRow(end, "清除 flag（逗号）", _endClearFlags, Join(cutscene.End?.ClearFlags));
        AddSimpleRow(end, "治疗百分比", _endHeal, (cutscene.End?.HealPercent ?? 0).ToString());
        AddSimpleRow(end, "传送到房间", _endGoto, cutscene.End?.GotoRoom ?? "");
        AddSimpleRow(end, "结束提示文字", _endMessage, cutscene.End?.Message ?? "");
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 142));
        root.Controls.Add(end, 0, root.RowCount);
        root.RowCount++;

        _issues.AutoSize = false;
        _issues.Dock = DockStyle.Fill;
        _issues.Height = 28;
        Theme.StyleLabel(_issues, Theme.Red);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.Controls.Add(_issues, 0, root.RowCount);
        root.RowCount++;

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            Height = 34,
            Padding = new Padding(0, 4, 0, 0),
        };
        var save = new Button { Text = "保存", Width = 90, Height = 28 };
        var cancel = new Button { Text = "取消", Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
        Theme.StyleButton(save, true);
        Theme.StyleButton(cancel);
        save.Click += (_, _) => { if (ValidateInput()) DialogResult = DialogResult.OK; else DialogResult = DialogResult.None; };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.Controls.Add(buttons, 0, root.RowCount);
        root.RowCount++;
        root.SetColumnSpan(buttons, 1);

        Controls.Add(root);
        AcceptButton = save;
        CancelButton = cancel;
        Shown += (_, _) => { Theme.EnableDarkTitleBar(this); _id.Focus(); };
    }

    // ---------------------------------------------------------------- 布局工具

    private static void AddPair(TableLayoutPanel t, string l1, Control c1, string l2, Control c2)
    {
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
        int row = t.RowCount;
        t.Controls.Add(LabelOf(l1), 0, row);
        Prepare(c1);
        t.Controls.Add(c1, 1, row);
        t.Controls.Add(LabelOf(l2), 2, row);
        if (c2 is CheckBox cb)
        {
            cb.Text = "";
            cb.BackColor = Theme.Bg1;
            cb.Dock = DockStyle.Left;
            cb.Width = 120;
            var holder = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg1 };
            holder.Controls.Add(cb);
            t.Controls.Add(holder, 3, row);
        }
        else
        {
            if (c2 is Label) c2.Visible = false;
            else Prepare(c2);
            t.Controls.Add(c2, 3, row);
        }
        t.RowCount = row + 1;
    }

    private static void AddSimpleRow(TableLayoutPanel t, string label, TextBox box, string value)
    {
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        int row = t.RowCount;
        t.Controls.Add(LabelOf(label), 0, row);
        Prepare(box);
        box.Text = value;
        t.Controls.Add(box, 1, row);
        t.RowCount = row + 1;
    }

    private static Label LabelOf(string text)
    {
        var lb = new Label { Text = text, AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        Theme.StyleLabel(lb, Theme.TextDim);
        return lb;
    }

    private static void Prepare(Control c)
    {
        if (c is TextBox tb) Theme.StyleTextBox(tb);
        if (c is ComboBox cb) { cb.BackColor = Theme.Bg2; cb.ForeColor = Theme.Text; }
        c.Dock = DockStyle.Fill;
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
        _grid.Columns.Add(ComboCol("kind", "步骤", 92, Kinds));
        _grid.Columns.Add(ComboCol("actor", "角色", 74, Actors));
        _grid.Columns.Add(TextCol("frames", "帧数", 56));
        _grid.Columns.Add(TextCol("x", "X", 58));
        _grid.Columns.Add(TextCol("y", "Y", 58));
        _grid.Columns.Add(ComboCol("direction", "朝向", 68, Dirs));
        _grid.Columns.Add(TextCol("sprite", "精灵（action）", 130));
        _grid.Columns.Add(TextCol("imageIndex", "帧", 52));
        _grid.Columns.Add(TextCol("imageSpeed", "动画速度", 72));
        _grid.Columns.Add(TextCol("sound", "音效（sound）", 130));
        _grid.Columns.Add(TextCol("gain", "增益（gain）", 60));
        _grid.Columns.Add(TextCol("text", "文本（对话用 | 分隔；message 也写这里）", 300));
        _grid.Columns.Add(TextCol("setFlags", "设置 flag", 90));
        _grid.Columns.Add(TextCol("clearFlags", "清除 flag", 90));
        _grid.Columns.Add(TextCol("evObject", "对象（event）", 150));
        _grid.Columns.Add(TextCol("script", "原版脚本 / 事件", 230));
        _grid.Columns.Add(TextCol("note", "说明", 150));
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            // kind 是下拉列：改了就立刻提交，好让 ApplyKindRules 立即生效。
            if (_grid.IsCurrentCellDirty && _grid.CurrentCell?.OwningColumn?.Name == "kind")
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && _grid.Columns[e.ColumnIndex].Name == "kind")
                ApplyKindRules(e.RowIndex);
        };
    }

    private static DataGridViewTextBoxColumn TextCol(string name, string header, int width) => new()
    {
        Name = name,
        HeaderText = header,
        Width = width,
        SortMode = DataGridViewColumnSortMode.NotSortable,
    };

    private static DataGridViewComboBoxColumn ComboCol(string name, string header, int width, string[] items)
    {
        var col = new DataGridViewComboBoxColumn { Name = name, HeaderText = header, Width = width, FlatStyle = FlatStyle.Flat };
        col.Items.AddRange(items);
        return col;
    }

    private void AddRow(CutsceneStep st)
    {
        var row = _grid.Rows[_grid.Rows.Add()];
        FillRow(row, st);
    }

    private void InsertRow(int index, CutsceneStep st)
    {
        var row = new DataGridViewRow();
        row.CreateCells(_grid);
        if (index < 0 || index > _grid.Rows.Count) index = _grid.Rows.Count;
        _grid.Rows.Insert(index, row);
        FillRow(row, st);
    }

    private void FillRow(DataGridViewRow row, CutsceneStep st)
    {
        int i = row.Index;
        row.Cells["kind"].Value = Kinds.Contains(st.Kind) ? st.Kind : "wait";
        row.Cells["actor"].Value = Actors.Contains(st.Actor) ? st.Actor : "player";
        row.Cells["frames"].Value = st.Frames.ToString();
        row.Cells["x"].Value = st.X.ToString("0.##");
        row.Cells["y"].Value = st.Y.ToString("0.##");
        row.Cells["direction"].Value = Dirs.Contains(st.Direction) ? st.Direction : "right";
        row.Cells["sprite"].Value = st.Sprite;
        row.Cells["imageIndex"].Value = st.ImageIndex.ToString();
        row.Cells["imageSpeed"].Value = st.ImageSpeed.ToString("0.##");
        row.Cells["sound"].Value = st.Sound;
        row.Cells["gain"].Value = st.Gain.ToString("0.##");
        row.Cells["text"].Value = st.Kind == "dialogue" ? string.Join(" | ", st.Lines) : st.Message;
        row.Cells["setFlags"].Value = Join(st.SetFlags);
        row.Cells["clearFlags"].Value = Join(st.ClearFlags);
        row.Cells["evObject"].Value = st.EvObject;
        row.Cells["script"].Value = st.Script;
        row.Cells["note"].Value = st.Note;
        row.Tag = st.EvType >= 0 ? (st.EvType, st.EvSub) : null;
        ApplyKindRules(i);
    }

    /// <summary>从当前房间/对象的原版事件里挑一个，插入一条 event 步骤。</summary>
    private void InsertVanillaCode()
    {
        if (_vanillaEvents.Count == 0) return;
        using var dlg = new Form
        {
            Text = "插入原版代码 —— 选择要执行的原版事件",
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(860, 560),
            MinimumSize = new Size(640, 400),
            BackColor = Theme.Bg1,
            ForeColor = Theme.Text,
            Font = Theme.UI(9f),
            FormBorderStyle = FormBorderStyle.Sizable,
            MaximizeBox = false,
            MinimizeBox = false,
        };
        var list = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Bg2,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.None,
            IntegralHeight = false,
            ItemHeight = 22,
        };
        foreach (var ev in _vanillaEvents)
        {
            var tag = ev.IsInteraction ? "（交互事件）" : ev.Inherited ? "（继承）" : "";
            list.Items.Add($"{ev.OwnerObject} · {ev.Friendly}{tag}    {ev.CodeName}");
        }
        if (list.Items.Count > 0) list.SelectedIndex = 0;
        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = Theme.Bg1,
            Padding = new Padding(0, 8, 10, 0),
        };
        var cancel = new Button { Text = "取消", Width = 78, Height = 26, DialogResult = DialogResult.Cancel };
        var ok = new Button { Text = "插入", Width = 78, Height = 26, DialogResult = DialogResult.OK };
        Theme.StyleButton(ok, true);
        Theme.StyleButton(cancel);
        bottom.Controls.Add(cancel);
        bottom.Controls.Add(ok);
        var hint = new Label
        {
            Dock = DockStyle.Top,
            Height = 26,
            Text = "  运行时会在最近的该对象实例上执行这条原版事件代码（剧情里也能触发原版对话/演出）。",
            ForeColor = Theme.TextDim,
        };
        Theme.StyleLabel(hint, Theme.TextDim);
        dlg.Controls.Add(list);
        dlg.Controls.Add(bottom);
        dlg.Controls.Add(hint);
        dlg.AcceptButton = ok;
        dlg.CancelButton = cancel;
        list.DoubleClick += (_, _) => { if (list.SelectedIndex >= 0) dlg.DialogResult = DialogResult.OK; };
        if (dlg.ShowDialog(this) != DialogResult.OK || list.SelectedIndex < 0) return;

        var picked = _vanillaEvents[list.SelectedIndex];
        int at = _grid.CurrentRow != null ? _grid.CurrentRow.Index + 1 : _grid.Rows.Count;
        InsertRow(at, new CutsceneStep
        {
            Kind = "event",
            EvObject = picked.OwnerObject,
            Script = picked.CodeName,
            Note = picked.Friendly,
            EvType = picked.EventType,
            EvSub = picked.EventSub,
            Frames = 0,
        });
        _grid.CurrentCell = _grid.Rows[at].Cells["script"];
    }

    /// <summary>按步骤 kind 决定哪些列可编辑（其余列只读并置灰，避免填了不生效的值）。</summary>
    private void ApplyKindRules(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _grid.Rows.Count) return;
        var row = _grid.Rows[rowIndex];
        string kind = (row.Cells["kind"].Value ?? "wait").ToString() ?? "wait";
        string[] editable = kind switch
        {
            "wait" => new[] { "frames" },
            "move" => new[] { "actor", "frames", "x", "y" },
            "face" => new[] { "actor", "direction" },
            "action" => new[] { "actor", "sprite", "imageIndex", "imageSpeed" },
            "dialogue" => new[] { "actor", "text" },
            "flag" => new[] { "setFlags", "clearFlags" },
            "message" => new[] { "text" },
            "sound" => new[] { "sound", "gain" },
            "shake" => new[] { "frames", "x" },
            "event" => new[] { "evObject", "script", "frames", "note" },
            "script" => new[] { "script", "frames", "note" },
            _ => Array.Empty<string>(), // waitinput：等玩家按键，不需要参数
        };
        foreach (DataGridViewColumn col in _grid.Columns)
        {
            if (col.Name == "kind") continue;
            bool on = editable.Contains(col.Name);
            var cell = row.Cells[col.Index];
            cell.ReadOnly = !on;
            cell.Style.BackColor = on ? Theme.Bg2 : Theme.Bg1;
            cell.Style.ForeColor = on ? Theme.Text : Theme.TextDim;
        }
    }

    private void MoveRow(int delta)
    {
        if (_grid.CurrentRow == null) return;
        int i = _grid.CurrentRow.Index;
        int j = i + delta;
        if (j < 0 || j >= _grid.Rows.Count) return;
        var row = _grid.Rows[i];
        _grid.Rows.RemoveAt(i);
        _grid.Rows.Insert(j, row);
        _grid.CurrentCell = _grid.Rows[j].Cells["kind"];
    }

    // ---------------------------------------------------------------- 读写

    private static string Join(List<int>? list) => list == null || list.Count == 0 ? "" : string.Join(",", list);

    private static int ParseInt(string s, int def, int min, int max)
    {
        if (!int.TryParse(s.Trim(), out int v)) v = def;
        return Math.Clamp(v, min, max);
    }

    private static double ParseDouble(string s, double def)
    {
        if (!double.TryParse(s.Trim(), out double v)) v = def;
        return v;
    }

    private static List<int> ParseFlags(string s)
    {
        var list = new List<int>();
        foreach (var part in s.Split(new[] { ',', ';', ' ', '，' }, StringSplitOptions.RemoveEmptyEntries))
            if (int.TryParse(part.Trim(), out int v) && !list.Contains(v)) list.Add(v);
        return list;
    }

    private string Cell(DataGridViewRow row, string col)
    {
        var v = row.Cells[col].Value;
        return v == null ? "" : v.ToString() ?? "";
    }

    private bool ValidateInput()
    {
        string id = _id.Text.Trim();
        if (id.Length == 0) { Fail("剧情 ID 不能为空"); return false; }
        if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[A-Za-z0-9_.-]{1,64}$"))
        {
            Fail("剧情 ID 只能用字母/数字/下划线/点/短横线（1-64 字符）");
            return false;
        }
        if (_grid.Rows.Count == 0) { Fail("至少需要 1 个剧情步骤"); return false; }
        int rowNo = 0;
        foreach (DataGridViewRow row in _grid.Rows)
        {
            rowNo++;
            var kind = Cell(row, "kind");
            var text = Cell(row, "text");
            if (kind == "dialogue" && text.Replace("|", "").Trim().Length == 0)
            {
                FailAt(row, "text", rowNo, "对话步骤至少要写一行文本（多行用 | 分隔）");
                return false;
            }
            if (kind == "sound" && Cell(row, "sound").Trim().Length == 0)
            {
                FailAt(row, "sound", rowNo, "sound 步骤需要在「音效」列填资源名（例如 snd_heal_c）");
                return false;
            }
            if ((kind == "event" || kind == "script") && Cell(row, "script").Trim().Length == 0)
            {
                FailAt(row, "script", rowNo, "event/script 步骤需要填「原版脚本 / 事件」（可用工具条的「插入原版代码…」挑选）");
                return false;
            }
            if (text.Contains('%')) { FailAt(row, "text", rowNo, "文本不能包含 %（原版对话分页符）"); return false; }
        }
        Fail("");
        return true;
    }

    private void Fail(string text)
    {
        _issues.Text = text;
        _issues.ForeColor = text.Length == 0 ? Theme.TextDim : Theme.Red;
    }

    /// <summary>报错并定位到具体步骤行 + 可编辑单元格（保存失败时不用自己找是哪一行）。</summary>
    private void FailAt(DataGridViewRow row, string col, int rowNo, string text)
    {
        Fail("第 " + rowNo + " 行：" + text);
        try
        {
            _grid.ClearSelection();
            row.Selected = true;
            _grid.CurrentCell = row.Cells[col];
            _grid.FirstDisplayedScrollingRowIndex = Math.Max(0, row.Index);
            _grid.Focus();
            _grid.BeginEdit(true);
        }
        catch { /* 定位失败不影响校验结果 */ }
    }

    /// <summary>把对话框内容写回被编辑的 CutsceneDef（OK 后由调用方保存 sidecar）。</summary>
    public void SaveToCutscene()
    {
        _target.Id = _id.Text.Trim();
        _target.Name = _name.Text.Trim().Length == 0 ? _target.Id : _name.Text.Trim();
        _target.Enabled = _enabled.Checked;
        _target.Mode = _mode.SelectedIndex == 1 ? "area" : "interact";

        _target.Trigger ??= new TriggerDef();
        _target.Trigger.Room = _room;
        _target.Trigger.RequiredFlag = ParseInt(_flag.Text, 0, 0, 100000);
        _target.Trigger.Radius = ParseInt(_radius.Text, 64, 0, 100000);
        _target.Trigger.Once = _once.Checked;
        _target.Trigger.PersistOnce = _persistOnce.Checked;
        if (_target.Trigger.X == 0 && _target.InstanceX > 0) _target.Trigger.X = _target.InstanceX;
        if (_target.Trigger.Y == 0 && _target.InstanceY > 0) _target.Trigger.Y = _target.InstanceY;

        _target.Steps = new List<CutsceneStep>();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            var kind = Cell(row, "kind");
            if (!Kinds.Contains(kind)) kind = "wait";
            var st = new CutsceneStep
            {
                Kind = kind,
                Actor = Actors.Contains(Cell(row, "actor")) ? Cell(row, "actor") : "player",
                Frames = ParseInt(Cell(row, "frames"), 30, 0, 100000),
                X = ParseDouble(Cell(row, "x"), 0),
                Y = ParseDouble(Cell(row, "y"), 0),
                Direction = Dirs.Contains(Cell(row, "direction")) ? Cell(row, "direction") : "right",
                Sprite = Cell(row, "sprite").Trim(),
                ImageIndex = ParseInt(Cell(row, "imageIndex"), 0, 0, 100000),
                ImageSpeed = ParseDouble(Cell(row, "imageSpeed"), -1),
                Sound = Cell(row, "sound").Trim(),
                Gain = ParseDouble(Cell(row, "gain"), 1),
                SetFlags = ParseFlags(Cell(row, "setFlags")),
                ClearFlags = ParseFlags(Cell(row, "clearFlags")),
                EvObject = Cell(row, "evObject").Trim(),
                Script = Cell(row, "script").Trim(),
                Note = Cell(row, "note").Trim(),
            };
            if (row.Tag is ValueTuple<int, int> evPair)
            {
                st.EvType = evPair.Item1;
                st.EvSub = evPair.Item2;
            }
            else if (kind != "event" && kind != "script")
            {
                st.EvObject = "";
                st.Script = "";
                st.Note = "";
                st.EvType = -1;
                st.EvSub = -1;
            }
            var text = Cell(row, "text").Trim();
            if (kind == "dialogue")
            {
                st.Lines = text.Split(new[] { '|', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                if (st.Lines.Count == 0) st.Lines.Add("");
            }
            else st.Message = text;
            _target.Steps.Add(st);
        }

        _target.End ??= new EffectDef();
        _target.End.SetFlags = ParseFlags(_endSetFlags.Text);
        _target.End.ClearFlags = ParseFlags(_endClearFlags.Text);
        _target.End.HealPercent = ParseInt(_endHeal.Text, 0, 0, 100);
        string gotoRoom = _endGoto.Text.Trim();
        _target.End.GotoRoom = gotoRoom.Length == 0 ? null : gotoRoom;
        string message = _endMessage.Text.Trim();
        _target.End.Message = message.Length == 0 ? null : message;
    }
}
