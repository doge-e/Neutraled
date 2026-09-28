using System.Text.RegularExpressions;

namespace Neutraled.Studio;

/// <summary>带语法高亮 / 行号 / 自动补全的代码编辑器（RichTextBox 派生）。</summary>
public sealed class CodeEditor : RichTextBox
{
    private readonly List<Syntax.Rule> _rules = Syntax.Build();
    private readonly System.Windows.Forms.Timer _hlTimer = new();
    private readonly RichTextBox _gutter = new();
    private ListBox? _completion;
    private List<string> _symbols = new();

    public event Action<string>? StatusChanged;

    public CodeEditor()
    {
        Font = new Font("Consolas", 11F);
        AcceptsTab = true;
        WordWrap = false;
        ScrollBars = RichTextBoxScrollBars.Both;
        HideSelection = false;
        BorderStyle = BorderStyle.None;

        _gutter.ReadOnly = true;
        _gutter.Width = 56;
        _gutter.Dock = DockStyle.Left;
        _gutter.Font = new Font("Consolas", 11F);
        _gutter.BackColor = Color.FromArgb(245, 245, 245);
        _gutter.ForeColor = Color.Gray;
        _gutter.BorderStyle = BorderStyle.None;
        _gutter.TabStop = false;
        _gutter.ScrollBars = RichTextBoxScrollBars.None;
        _gutter.Cursor = Cursors.Default;

        _hlTimer.Interval = 250;
        _hlTimer.Tick += (_, _) => { _hlTimer.Stop(); Highlight(); };
        TextChanged += (_, _) => { _hlTimer.Stop(); _hlTimer.Start(); UpdateGutter(); };
        VScroll += (_, _) => SyncGutter();
        SelectionChanged += (_, _) =>
        {
            var line = GetLineFromCharIndex(SelectionStart) + 1;
            var col = SelectionStart - GetFirstCharIndexFromLine(line - 1) + 1;
            StatusChanged?.Invoke(Localizer.T("行 {0}, 列 {1}  |  字符 {2}", line, col, TextLength));
        };
        KeyDown += OnKeyDown;
    }

    public RichTextBox Gutter => _gutter;

    /// <summary>当前补全列表的候选数（0 表示未弹出）。</summary>
    public int CompletionCount => _completion?.Items.Count ?? 0;

    /// <summary>当前补全候选项（供自检）。</summary>
    public List<string> CompletionItems =>
        _completion?.Items.Cast<object>().Select(o => o?.ToString() ?? "").ToList() ?? new List<string>();

    public void SetSymbols(IEnumerable<string> symbols)
    {
        _symbols = symbols.Distinct().OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void UpdateGutter()
    {
        var lineCount = Math.Max(1, Lines.Length);
        var sb = new System.Text.StringBuilder();
        for (int i = 1; i <= lineCount; i++) sb.Append(i).Append('\n');
        _gutter.Text = sb.ToString();
        SyncGutter();
    }

    private void SyncGutter()
    {
        try
        {
            var first = GetLineFromCharIndex(GetCharIndexFromPosition(new Point(1, 1)));
            var target = Math.Max(0, first);
            _gutter.SelectionStart = 0;
            var idx = 0;
            for (int i = 0; i < target && idx >= 0 && idx < _gutter.TextLength; i++)
                idx = _gutter.Text.IndexOf('\n', idx) + 1;
            _gutter.SelectionStart = Math.Max(0, idx);
            _gutter.ScrollToCaret();
        }
        catch { }
    }

    /// <summary>重新着色（保留选区与滚动位置）。</summary>
    public void Highlight()
    {
        if (TextLength == 0) return;
        if (TextLength > 200000) return;   // 超大文件跳过高亮，避免卡顿

        var selStart = SelectionStart;
        var selLen = SelectionLength;
        var scroll = GetCharIndexFromPosition(new Point(1, 1));

        SuspendLayout();
        try
        {
            SelectAll();
            SelectionColor = Color.FromArgb(30, 30, 30);
            SelectionFont = Font;

            var text = Text;
            foreach (var rule in _rules)
            {
                foreach (Match m in rule.Pattern.Matches(text))
                {
                    if (m.Length <= 0) continue;
                    Select(m.Index, m.Length);
                    SelectionColor = rule.Color;
                    if (rule.Bold) SelectionFont = new Font(Font, FontStyle.Bold);
                }
            }
        }
        catch { }
        finally
        {
            Select(selStart, selLen);
            try { SelectionStart = selStart; ScrollToCaret(); } catch { }
            ResumeLayout();
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl+Space / Ctrl+J 触发补全
        if (e.Control && (e.KeyCode == Keys.Space || e.KeyCode == Keys.J))
        {
            ShowCompletion();
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }
        if (_completion != null)
        {
            if (e.KeyCode == Keys.Escape) { HideCompletion(); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Tab)
            {
                AcceptCompletion();
                e.Handled = true; e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Up)
            {
                var i = _completion.SelectedIndex + (e.KeyCode == Keys.Down ? 1 : -1);
                if (i >= 0 && i < _completion.Items.Count) _completion.SelectedIndex = i;
                e.Handled = true; e.SuppressKeyPress = true;
            }
            return;
        }
        // Tab 缩进
        if (e.KeyCode == Keys.Tab && !e.Shift)
        {
            SelectedText = "    ";
            e.Handled = true; e.SuppressKeyPress = true;
        }
    }

    /// <summary>弹出补全列表（基于 API 注册表的符号表）。</summary>
    public void ShowCompletion()
    {
        if (_symbols.Count == 0) { StatusChanged?.Invoke(Localizer.T("补全不可用：符号表为空")); return; }
        HideCompletion();

        var caret = GetPositionFromCharIndex(SelectionStart);
        var wordStart = SelectionStart;
        var word = "";
        while (wordStart > 0)
        {
            var ch = Text[wordStart - 1];
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '.') { wordStart--; word = ch + word; }
            else break;
        }

        var matches = string.IsNullOrEmpty(word)
            ? _symbols.Take(400).ToList()
            : _symbols.Where(s => s.StartsWith(word, StringComparison.OrdinalIgnoreCase) ||
                                  s.Contains(word, StringComparison.OrdinalIgnoreCase)).Take(400).ToList();
        if (matches.Count == 0) return;

        _completion = new ListBox
        {
            Left = caret.X + 4,
            Top = caret.Y + Font.Height + 4,
            Width = 280,
            Height = Math.Min(220, matches.Count * 16 + 6),
            Font = new Font("Consolas", 10F)
        };
        _completion.Items.AddRange(matches.ToArray());
        _completion.SelectedIndex = 0;
        _completion.DoubleClick += (_, _) => AcceptCompletion();
        StatusChanged?.Invoke(Localizer.T("补全候选 {0} 项（前缀 \"{1}\"）", matches.Count, word));
        Parent?.Controls.Add(_completion);
        _completion.BringToFront();
    }

    public void AcceptCompletion()
    {
        if (_completion?.SelectedItem is not string pick) { HideCompletion(); return; }
        var wordStart = SelectionStart;
        while (wordStart > 0)
        {
            var ch = Text[wordStart - 1];
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '.') wordStart--;
            else break;
        }
        Select(wordStart, SelectionStart - wordStart);
        SelectedText = pick;
        HideCompletion();
    }

    public void HideCompletion()
    {
        if (_completion != null)
        {
            Parent?.Controls.Remove(_completion);
            _completion.Dispose();
            _completion = null;
        }
    }
}
