using System.Drawing;

namespace NtlMapEdit;

/// <summary>Small dark-themed single-line text prompt (replacement for the missing InputBox).</summary>
internal sealed class PromptDialog : Form
{
    private readonly TextBox _box;

    private PromptDialog(string title, string label, string initial)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(400, 122);
        BackColor = Theme.Bg1;
        ForeColor = Theme.Text;
        Font = Theme.UI(9f);

        var lbl = new Label { Text = label, Left = 14, Top = 12, Width = 372, Height = 18, ForeColor = Theme.TextDim };
        Theme.StyleLabel(lbl, Theme.TextDim);
        _box = new TextBox { Left = 14, Top = 36, Width = 372, Text = initial };
        Theme.StyleTextBox(_box);
        var ok = new Button { Text = "确定", Left = 226, Top = 78, Width = 78, Height = 28, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "取消", Left = 308, Top = 78, Width = 78, Height = 28, DialogResult = DialogResult.Cancel };
        Theme.StyleButton(ok, true);
        Theme.StyleButton(cancel);
        Controls.AddRange(new Control[] { lbl, _box, ok, cancel });
        AcceptButton = ok;
        CancelButton = cancel;
        Shown += (s, e) => { _box.Focus(); _box.SelectAll(); };
    }

    /// <summary>Returns the trimmed text, or null when cancelled.</summary>
    public static string? Show(IWin32Window owner, string title, string label, string initial = "")
    {
        using var dlg = new PromptDialog(title, label, initial);
        return dlg.ShowDialog(owner) == DialogResult.OK ? dlg._box.Text.Trim() : null;
    }
}
