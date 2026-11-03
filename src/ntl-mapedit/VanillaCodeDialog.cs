using System.Text;
using System.Windows.Forms;
using UndertaleModLib.Models;

namespace NtlMapEdit;

/// <summary>
/// 只读查看器：显示某个原版对象的事件清单、解析出的对白行（含条件/三态）与反编译代码。
/// 也用于显示实例的创建代码 / 预创建代码（通过静态 ShowCode）。
/// </summary>
public sealed class VanillaCodeDialog : Form
{
    private readonly VanillaCode _vc;
    private readonly UndertaleGameObject _obj;
    private readonly int _roomIndex;
    private readonly float _x;
    private readonly float _y;
    private readonly UndertaleCode? _instanceCode;
    private readonly UndertaleCode? _preCreateCode;

    private VanillaObjectInfo _info;
    private readonly ListBox _eventList = new();
    private readonly ListBox _lineList = new();
    private readonly TextBox _codeBox = new();
    private readonly Label _summary = new();
    private readonly Label _eventHint = new();
    private bool _busy;

    public VanillaCodeDialog(Form owner, VanillaCode vc, UndertaleGameObject obj, int roomIndex, float x, float y,
        VanillaObjectInfo info, UndertaleCode? instanceCode, UndertaleCode? preCreateCode)
    {
        _vc = vc;
        _obj = obj;
        _roomIndex = roomIndex;
        _x = x;
        _y = y;
        _info = info;
        _instanceCode = instanceCode;
        _preCreateCode = preCreateCode;

        Text = "原版事件 / 对话（只读） — " + GameData.Name(obj.Name);
        Size = new Size(1120, 720);
        MinimumSize = new Size(860, 560);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Bg0;
        ForeColor = Theme.Text;
        Font = Theme.UI(9f);
        Theme.EnableDarkTitleBar(this);

        _summary.Dock = DockStyle.Top;
        _summary.Height = 62;
        _summary.Padding = new Padding(10, 6, 10, 4);
        _summary.ForeColor = Theme.Text;
        _summary.Text = Summarize();

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 6,
            BackColor = Theme.Bg0,
            // 顺序很重要：WinForms 的 PanelNMinSize setter 会立刻校验 SplitterDistance，
            // 在默认 150 宽上设 300/320 会抛 "SplitterDistance must be between Panel1MinSize and Width - Panel2MinSize"。
            Size = new Size(1060, 540),
            SplitterDistance = 520,
            Panel1MinSize = 260,
            Panel2MinSize = 280,
        };
        var left = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg0, Padding = new Padding(8, 0, 4, 0) };
        var right = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg0, Padding = new Padding(4, 0, 8, 0) };

        var evLabel = new Label { Text = "事件（点击查看反编译代码与对白行）", Dock = DockStyle.Top, Height = 22, ForeColor = Theme.TextDim };
        StyleList(_eventList);
        _eventList.Dock = DockStyle.Top;
        _eventList.Height = 190;
        _eventList.SelectedIndexChanged += (s, e) => ShowSelectedEvent();

        _eventHint.Dock = DockStyle.Top;
        _eventHint.Height = 60;
        _eventHint.Padding = new Padding(2, 4, 2, 2);
        _eventHint.ForeColor = Theme.TextDim;
        _eventHint.Text = "提示：默认只扫描可能藏对白的事件（创建/用户事件/闹钟/按键/碰撞/预创建）；\n点下方「扫描全部事件」可强制解析每帧/绘制事件（较慢）。";

        var scanAll = new Button { Text = "扫描全部事件（较慢）", Dock = DockStyle.Top, Height = 30 };
        Theme.StyleButton(scanAll);
        scanAll.Click += (s, e) => ScanAll();

        left.Controls.Add(_eventHint);
        left.Controls.Add(scanAll);
        left.Controls.Add(_eventList);
        left.Controls.Add(evLabel);

        var lineLabel = new Label { Text = "对白行（按当前房间/实例坐标做条件判定）", Dock = DockStyle.Top, Height = 22, ForeColor = Theme.TextDim };
        StyleList(_lineList);
        _lineList.Dock = DockStyle.Fill;
        _lineList.SelectedIndexChanged += (s, e) => ShowSelectedLineCode();
        left.Controls.Add(_lineList);

        var codeLabel = new Label { Text = "反编译代码（只读）", Dock = DockStyle.Top, Height = 22, ForeColor = Theme.TextDim };
        _codeBox.Dock = DockStyle.Fill;
        _codeBox.Multiline = true;
        _codeBox.ReadOnly = true;
        _codeBox.ScrollBars = ScrollBars.Both;
        _codeBox.WordWrap = false;
        _codeBox.Font = Theme.Mono(8.5f);
        _codeBox.BackColor = Theme.Bg1;
        _codeBox.ForeColor = Theme.Text;
        _codeBox.BorderStyle = BorderStyle.FixedSingle;
        right.Controls.Add(_codeBox);
        right.Controls.Add(codeLabel);

        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(right);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 42,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8, 6, 8, 6),
            BackColor = Theme.Bg1,
        };
        var close = new Button { Text = "关闭 (Esc)", Width = 110, Height = 28 };
        Theme.StyleButton(close);
        close.Click += (s, e) => Close();
        var copyCode = new Button { Text = "复制代码", Width = 100, Height = 28 };
        Theme.StyleButton(copyCode);
        copyCode.Click += (s, e) => CopyText(_codeBox.Text, "代码");
        var copyLines = new Button { Text = "复制对白摘要", Width = 120, Height = 28 };
        Theme.StyleButton(copyLines);
        copyLines.Click += (s, e) => CopyText(BuildLineDigest(), "对白摘要");
        var copySummary = new Button { Text = "复制事件摘要", Width = 120, Height = 28 };
        Theme.StyleButton(copySummary);
        copySummary.Click += (s, e) => CopyText(Summarize() + Environment.NewLine + Environment.NewLine + BuildLineDigest(), "事件摘要");
        buttons.Controls.Add(close);
        buttons.Controls.Add(copyCode);
        buttons.Controls.Add(copyLines);
        buttons.Controls.Add(copySummary);

        Controls.Add(split);
        Controls.Add(_summary);
        Controls.Add(buttons);
        KeyPreview = true;
        KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        Shown += (s, e) =>
        {
            if (_eventList.Items.Count > 0) _eventList.SelectedIndex = 0;
        };
        RefreshEvents();
    }

    private static void StyleList(ListBox lb)
    {
        lb.BackColor = Theme.Bg1;
        lb.ForeColor = Theme.Text;
        lb.BorderStyle = BorderStyle.FixedSingle;
        lb.Font = Theme.Mono(8.5f);
        lb.IntegralHeight = false;
        lb.HorizontalScrollbar = true;
    }

    private string Summarize()
    {
        var all = _info.Events.SelectMany(e => e.Lines).ToList();
        int reachable = all.Count(l => l.Truth != TriTruth.False);
        int mscTotal = all.Count(l => l.MscNo > 0);
        int argTotal = all.Count(l => l.RequiresArgs);
        int unknownTotal = all.Count(l => l.Truth == TriTruth.Unknown);
        bool truncated = _info.Events.Any(e => e.Truncated);
        string lang = _vc.LangStatus;
        string inter;
        if (_info.InteractionEvent == null) inter = "未发现 Other_10";
        else if (_info.InteractionEvent.Inherited) inter = "交互事件 " + _info.InteractionEvent.Name + "（继承自 " + _info.InteractionEvent.OwnerObject + "）";
        else inter = "交互事件 " + _info.InteractionEvent.Name;
        string inherited = _info.InheritedDialogueLineCount > 0 ? "；其中继承 " + _info.InheritedDialogueLineCount + " 条" : "";
        string parents = _info.ParentChain.Count > 0 ? "    父对象: " + string.Join(" → ", _info.ParentChain) : "";
        string trunc = truncated ? "  ⚠ 超过 " + VanillaCode.MaxLines + " 条已截断" : "";
        bool hasCoords = !float.IsNaN(_x) && !float.IsNaN(_y);
        string reachText = hasCoords
            ? $"当前房间与坐标下可达 {reachable} 条"
            : $"坐标未知：确定可达 {all.Count(l => l.Truth == TriTruth.True)} 条、待定 ? {unknownTotal} 条";
        string coordNote = hasCoords ? "" : "（该对象没有实例坐标；带 x/y 的条件显示为 ?）";
        return $"对象: {_info.ObjectName}    {inter}{parents}" + Environment.NewLine +
               $"对白事件 {_info.DialogueEventCount} 个 / 对白行 {_info.DialogueLineCount} 条（可阅读 {_info.DisplayableLineCount} 条；运行期文本/控制码 {_info.RuntimeTextLineCount} 条；{reachText}{inherited}）{trunc}" + coordNote + Environment.NewLine +
               $"msc 旧式文本 {mscTotal} 条 · 含参数替换 {argTotal} 条 · 未知条件 {unknownTotal} 条" + Environment.NewLine +
               lang;
    }

    private void RefreshEvents()
    {
        _eventList.BeginUpdate();
        _eventList.Items.Clear();
        foreach (var ev in _info.Events)
        {
            int reach = ev.Lines.Count(l => l.Truth != TriTruth.False);
            string inh = ev.Inherited ? "（继承自 " + ev.OwnerObject + "）" : "";
            _eventList.Items.Add(new EventRow(ev, $"{ev.Name}  {ev.Friendly}{inh}  ·  {ev.Lines.Count} 行（可达 {reach}）"));
        }
        if (_instanceCode != null)
            _eventList.Items.Add(new EventRow(null, "实例创建代码  ·  只读"));
        if (_preCreateCode != null)
            _eventList.Items.Add(new EventRow(null, "实例预创建代码  ·  只读"));
        _eventList.EndUpdate();
        _summary.Text = Summarize();
    }

    private sealed class EventRow
    {
        public readonly VanillaEventInfo? Ev;
        public readonly string Label;
        public EventRow(VanillaEventInfo? ev, string label) { Ev = ev; Label = label; }
        public override string ToString() => Label;
    }

    private VanillaEventInfo? SelectedEvent => (_eventList.SelectedItem as EventRow)?.Ev;

    private void ShowSelectedEvent()
    {
        var row = _eventList.SelectedItem as EventRow;
        _lineList.BeginUpdate();
        _lineList.Items.Clear();
        if (row == null) { _lineList.EndUpdate(); _codeBox.Text = ""; return; }
        if (row.Ev == null)
        {
            bool pre = _eventList.SelectedIndex == _eventList.Items.Count - 1 && _preCreateCode != null;
            var code = pre ? _preCreateCode : _instanceCode;
            _codeBox.Text = _vc.Decompile(code) ?? "（该代码条目没有反编译结果）";
            _lineList.Items.Add(new LineRow("（这是实例创建代码，不含对话解析；对话请选择对象事件）"));
            _lineList.EndUpdate();
            return;
        }
        foreach (var l in row.Ev.Lines)
        {
            _lineList.Items.Add(new LineRow(FormatLine(l), l));
        }
        if (row.Ev.Lines.Count == 0 && row.Ev.OtherKeys.Count > 0)
            foreach (var k in row.Ev.OtherKeys) _lineList.Items.Add(new LineRow("其它 key: " + k));
        _lineList.EndUpdate();
        _codeBox.Text = _vc.Decompile(row.Ev.Code) ?? "（没有反编译结果）";
        if (_lineList.Items.Count > 0) _lineList.SelectedIndex = 0;
    }

    private static string TruthMark(TriTruth t) => t switch
    {
        TriTruth.True => "✓ 会显示",
        TriTruth.False => "✗ 不显示",
        _ => "? 条件未知",
    };

    private static string FormatLine(VanillaDialogueLine l)
    {
        var sb = new StringBuilder();
        sb.Append(TruthMark(l.Truth)).Append("  ");
        if (l.MscNo > 0) sb.Append("[msc ").Append(l.MscNo).Append("]  ");
        else if (l.MsgIndex >= 0) sb.Append("msg[").Append(l.MsgIndex).Append("]  ");
        if (l.RequiresArgs) sb.Append("[参数]  ");
        if (l.RuntimeText) sb.Append("[运行期文本/控制码]  ");
        sb.Append(l.DisplayText.Replace(" / ", " ⏎ "));
        if (l.RequiresArgs) sb.Append("  （文本含参数替换，实际显示会带变量）");
        if (!string.IsNullOrEmpty(l.Condition)) sb.Append("   条件: ").Append(l.Condition);
        if (!string.IsNullOrEmpty(l.Key)) sb.Append("   key: ").Append(l.Key);
        return sb.ToString();
    }

    private sealed class LineRow
    {
        public readonly string Label;
        public readonly VanillaDialogueLine? Line;
        public LineRow(string label, VanillaDialogueLine? line = null) { Label = label; Line = line; }
        public override string ToString() => Label;
    }

    private void ShowSelectedLineCode()
    {
        if (_lineList.SelectedItem is LineRow { Line: not null } row && SelectedEvent != null)
        {
            // 代码框保持整个事件的代码；这里只把选中的那一行滚动到可读位置附近
            string needle = row.Line!.Key;
            if (string.IsNullOrEmpty(needle)) return;
            int idx = _codeBox.Text.IndexOf(needle, StringComparison.Ordinal);
            if (idx >= 0)
            {
                _codeBox.Focus();
                _codeBox.Select(idx, Math.Min(needle.Length, 200));
                _codeBox.ScrollToCaret();
                ActiveControl = _lineList;
            }
        }
    }

    private void ScanAll()
    {
        if (_busy) return;
        _busy = true;
        Cursor = Cursors.WaitCursor;
        try
        {
            _info = _vc.ScanObject(_obj, _roomIndex, _x, _y, quick: false);
            RefreshEvents();
            if (_eventList.Items.Count > 0) _eventList.SelectedIndex = 0;
        }
        finally
        {
            Cursor = Cursors.Default;
            _busy = false;
        }
    }

    private string BuildLineDigest()
    {
        var sb = new StringBuilder();
        foreach (var ev in _info.Events)
        {
            sb.AppendLine("== " + ev.Name + "（" + ev.Friendly + "）==");
            foreach (var l in ev.Lines) sb.AppendLine("  " + FormatLine(l));
        }
        if (sb.Length == 0) sb.AppendLine("（没有解析到对白行）");
        return sb.ToString();
    }

    private void CopyText(string text, string what)
    {
        if (string.IsNullOrEmpty(text)) return;
        try { Clipboard.SetText(text); } catch { }
        _summary.Text = Summarize();
    }

    /// <summary>只读代码查看器（实例创建代码 / 预创建代码 / 房间创建代码）。</summary>
    public static void ShowCode(Form owner, string title, string? code)
    {
        using var dlg = new Form
        {
            Text = title,
            Size = new Size(900, 640),
            MinimumSize = new Size(600, 400),
            StartPosition = FormStartPosition.CenterParent,
            BackColor = Theme.Bg0,
            ForeColor = Theme.Text,
            Font = Theme.UI(9f),
        };
        Theme.EnableDarkTitleBar(dlg);
        var box = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = Theme.Mono(8.5f),
            BackColor = Theme.Bg1,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Text = string.IsNullOrEmpty(code) ? "（这段代码是空的）" : code,
        };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8, 6, 8, 6),
            BackColor = Theme.Bg1,
        };
        var close = new Button { Text = "关闭 (Esc)", Width = 110, Height = 28 };
        Theme.StyleButton(close);
        close.Click += (s, e) => dlg.Close();
        var copy = new Button { Text = "复制代码", Width = 100, Height = 28 };
        Theme.StyleButton(copy);
        copy.Click += (s, e) => { try { Clipboard.SetText(box.Text); } catch { } };
        buttons.Controls.Add(close);
        buttons.Controls.Add(copy);
        dlg.Controls.Add(box);
        dlg.Controls.Add(buttons);
        dlg.KeyPreview = true;
        dlg.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) dlg.Close(); };
        dlg.ShowDialog(owner);
    }
}
