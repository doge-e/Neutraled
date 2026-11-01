using System.Drawing;
using System.Windows.Forms;

namespace NtlMapEdit;

/// <summary>
/// 绑定在地图实体上的「纯对话」编辑器：实体 → obj_dialoguer → 多行文本 → 结束 effects。
/// 只编辑一个 <see cref="DialogueDef"/>；保存由调用方（EditorForm）写 sidecar。
/// </summary>
public sealed class DialogueEditDialog : Form
{
    private readonly DialogueDef _target;
    private readonly TextBox _id = new();
    private readonly TextBox _name = new();
    private readonly ComboBox _mode = new();
    private readonly CheckBox _enabled = new();
    private readonly TextBox _speaker = new();
    private readonly TextBox _lines = new();
    private readonly TextBox _flag = new();
    private readonly TextBox _radius = new();
    private readonly CheckBox _once = new();
    private readonly CheckBox _persistOnce = new();
    private readonly TextBox _setFlags = new();
    private readonly TextBox _clearFlags = new();
    private readonly TextBox _heal = new();
    private readonly TextBox _goto = new();
    private readonly TextBox _message = new();
    private readonly Label _issues = new();

    public DialogueEditDialog(DialogueDef dialogue, string room, uint instanceId, string objectName)
    {
        _target = dialogue;
        Text = $"对话编辑 —— {room} / {objectName} #{instanceId}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(760, 800);
        MinimumSize = new Size(680, 700);
        BackColor = Theme.Bg1;
        ForeColor = Theme.Text;
        Font = Theme.UI(9f);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(14, 12, 14, 10),
            BackColor = Theme.Bg1,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(root, "对话 ID", _id, dialogue.Id);
        AddRow(root, "名称", _name, dialogue.Name);
        AddRow(root, "启用", _enabled, null, dialogue.Enabled);
        AddRow(root, "说话人（可空）", _speaker, dialogue.Speaker);
        _mode.DropDownStyle = ComboBoxStyle.DropDownList;
        _mode.Items.AddRange(new object[] { "interact（靠近后按 Z/Enter 触发）", "area（走进触发半径自动播放）" });
        _mode.SelectedIndex = dialogue.Mode == "area" ? 1 : 0;
        _mode.BackColor = Theme.Bg2;
        _mode.ForeColor = Theme.Text;
        AddRow(root, "触发方式", _mode, null);

        _lines.Multiline = true;
        _lines.ScrollBars = ScrollBars.Vertical;
        _lines.Height = 170;
        _lines.Text = string.Join(Environment.NewLine, dialogue.Lines ?? new List<string>());
        _lines.Font = Theme.Mono(9f);
        Theme.StyleTextBox(_lines);
        var linesLabel = new Label
        {
            Text = "文本行（每行一句）\r\nZ 翻页，末行后自动结束",
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
            ForeColor = Theme.TextDim,
            Padding = new Padding(0, 6, 0, 0),
        };
        Theme.StyleLabel(linesLabel, Theme.TextDim);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 180));
        root.Controls.Add(linesLabel, 0, root.RowCount);
        root.Controls.Add(_lines, 1, root.RowCount);
        root.RowCount++;

        AddRow(root, "需要 flag（0=无）", _flag, dialogue.Trigger?.RequiredFlag.ToString() ?? "0");
        AddRow(root, "触发半径（像素）", _radius, (dialogue.Trigger?.Radius > 0 ? dialogue.Trigger!.Radius : 64).ToString());
        AddRow(root, "只触发一次（本次游戏）", _once, null, dialogue.Trigger?.Once ?? true);
        AddRow(root, "跨重启只触发一次", _persistOnce, null, dialogue.Trigger?.PersistOnce ?? false);

        var sep = new Label
        {
            Text = "── 结束效果（可选） ──",
            AutoSize = false,
            Height = 28,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Theme.Accent,
        };
        Theme.StyleLabel(sep, Theme.Accent, true);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.Controls.Add(sep, 0, root.RowCount);
        root.SetColumnSpan(sep, 2);
        root.RowCount++;

        AddRow(root, "设置 flag（逗号）", _setFlags, Join(dialogue.End?.SetFlags));
        AddRow(root, "清除 flag（逗号）", _clearFlags, Join(dialogue.End?.ClearFlags));
        AddRow(root, "治疗百分比", _heal, (dialogue.End?.HealPercent ?? 0).ToString());
        AddRow(root, "传送到房间", _goto, dialogue.End?.GotoRoom ?? "");
        AddRow(root, "结束提示文字", _message, dialogue.End?.Message ?? "");

        var hint = new Label
        {
            Text = "提示：触发方式 interact = 进入半径后按 Z/Enter 开始；area = 进入半径自动播放（由生成的 mod 运行时检测）。\r\n" +
                   "对话文本里不要出现 %（那是原版的分页符）。",
            AutoSize = false,
            Dock = DockStyle.Fill,
            Height = 56,
            ForeColor = Theme.TextDim,
        };
        Theme.StyleLabel(hint, Theme.TextDim);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.Controls.Add(hint, 0, root.RowCount);
        root.SetColumnSpan(hint, 2);
        root.RowCount++;

        _issues.AutoSize = false;
        _issues.Dock = DockStyle.Fill;
        _issues.Height = 30;
        Theme.StyleLabel(_issues, Theme.Red);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        root.Controls.Add(_issues, 0, root.RowCount);
        root.SetColumnSpan(_issues, 2);
        root.RowCount++;

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            Height = 34,
            Padding = new Padding(0, 4, 0, 0),
        };
        var save = new Button { Text = "保存", Width = 90, Height = 28, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "取消", Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
        Theme.StyleButton(save, true);
        Theme.StyleButton(cancel);
        save.Click += (_, _) => { if (ValidateInput()) DialogResult = DialogResult.OK; else DialogResult = DialogResult.None; };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.Controls.Add(buttons, 0, root.RowCount);
        root.SetColumnSpan(buttons, 2);
        root.RowCount++;

        Controls.Add(root);
        AcceptButton = save;
        CancelButton = cancel;
        Shown += (_, _) => { Theme.EnableDarkTitleBar(this); _id.Focus(); };
    }

    private static string Join(List<int>? list) => list == null || list.Count == 0 ? "" : string.Join(",", list);

    private static void AddRow(TableLayoutPanel root, string label, Control box, string? value, bool? check = null)
    {
        var lb = new Label { Text = label, AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        Theme.StyleLabel(lb, Theme.TextDim);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        if (check != null && box is CheckBox cb)
        {
            cb.Checked = check.Value;
            cb.Text = "";
            cb.BackColor = Theme.Bg1;
            cb.Dock = DockStyle.Left;
            cb.Width = 120;
            var holder = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg1 };
            holder.Controls.Add(cb);
            root.Controls.Add(lb, 0, root.RowCount);
            root.Controls.Add(holder, 1, root.RowCount);
        }
        else
        {
            if (box is ComboBox combo)
            {
                combo.Dock = DockStyle.Fill;
            }
            else
            {
                box.Text = value ?? "";
                if (box is TextBox tb) Theme.StyleTextBox(tb);
                box.Dock = DockStyle.Fill;
            }
            root.Controls.Add(lb, 0, root.RowCount);
            root.Controls.Add(box, 1, root.RowCount);
        }
        root.RowCount++;
    }

    private static int ParseInt(string s, int def, int min, int max)
    {
        if (!int.TryParse(s.Trim(), out int v)) v = def;
        return Math.Clamp(v, min, max);
    }

    private static List<int> ParseFlags(string s)
    {
        var list = new List<int>();
        foreach (var part in s.Split(new[] { ',', ';', ' ', '，' }, StringSplitOptions.RemoveEmptyEntries))
            if (int.TryParse(part.Trim(), out int v) && !list.Contains(v)) list.Add(v);
        return list;
    }

    private bool ValidateInput()
    {
        string id = _id.Text.Trim();
        if (id.Length == 0) { Fail("对话 ID 不能为空"); return false; }
        if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[A-Za-z0-9_.-]{1,64}$"))
        {
            Fail("对话 ID 只能用字母/数字/下划线/点/短横线（1-64 字符）");
            return false;
        }
        var lines = _lines.Lines.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (lines.Count == 0) { Fail("至少写一行文本"); return false; }
        if (lines.Any(l => l.Contains('%'))) { Fail("文本不能包含 %（原版分页符）"); return false; }
        Fail("");
        return true;
    }

    private void Fail(string text)
    {
        _issues.Text = text;
        _issues.ForeColor = text.Length == 0 ? Theme.TextDim : Theme.Red;
    }

    /// <summary>把对话框内容写回被编辑的 DialogueDef（OK 后由调用方保存 sidecar）。</summary>
    public void SaveToDialogue()
    {
        _target.Id = _id.Text.Trim();
        _target.Name = _name.Text.Trim().Length == 0 ? _target.Id : _name.Text.Trim();
        _target.Enabled = _enabled.Checked;
        _target.Speaker = _speaker.Text.Trim();
        _target.Lines = _lines.Lines.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        _target.Mode = _mode.SelectedIndex == 1 ? "area" : "interact";

        _target.Trigger ??= new TriggerDef();
        _target.Trigger.Room = _target.Room;
        _target.Trigger.RequiredFlag = ParseInt(_flag.Text, 0, 0, 100000);
        _target.Trigger.Radius = ParseInt(_radius.Text, 64, 0, 100000);
        _target.Trigger.Once = _once.Checked;
        _target.Trigger.PersistOnce = _persistOnce.Checked;
        if (_target.Trigger.X == 0 && _target.InstanceX > 0) _target.Trigger.X = (int)_target.InstanceX;
        if (_target.Trigger.Y == 0 && _target.InstanceY > 0) _target.Trigger.Y = (int)_target.InstanceY;

        _target.End ??= new EffectDef();
        _target.End.SetFlags = ParseFlags(_setFlags.Text);
        _target.End.ClearFlags = ParseFlags(_clearFlags.Text);
        _target.End.HealPercent = ParseInt(_heal.Text, 0, 0, 100);
        string gotoRoom = _goto.Text.Trim();
        _target.End.GotoRoom = gotoRoom.Length == 0 ? null : gotoRoom;
        string message = _message.Text.Trim();
        _target.End.Message = message.Length == 0 ? null : message;
    }
}

