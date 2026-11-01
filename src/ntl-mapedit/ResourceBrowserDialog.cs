using System.Drawing;
using UndertaleModLib.Models;

namespace NtlMapEdit;

/// <summary>
/// Read-only browser over every resource family the loader parsed (rooms, objects, sprites,
/// backgrounds, fonts, sounds, scripts/code, shaders, sequences, timelines, texture pages, paths).
/// It never edits or saves anything: it exists so custom/rare resources are inspectable instead
/// of silently invisible, and so authors can copy exact values out. Data comes from
/// <see cref="ResourceCatalog"/> (same implementation the CLI self-check uses).
/// </summary>
internal sealed class ResourceBrowserDialog : Form
{
    private readonly GameData _gd;
    private readonly TreeView _tree = new();
    private readonly ListView _list = new();
    private readonly TextBox _detail = new();
    private readonly Label _summary = new();
    private readonly Dictionary<string, List<ResourceCatalog.Entry>> _cache = new();
    private List<ResourceCatalog.Entry> _rows = new();

    public ResourceBrowserDialog(GameData gd)
    {
        _gd = gd;
        Text = "资源浏览器（只读） — " + Path.GetFileName(gd.SourcePath);
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(980, 620);
        MinimumSize = new Size(700, 420);
        BackColor = Theme.Bg1;
        ForeColor = Theme.Text;
        Font = Theme.UI(9f);
        KeyPreview = true;

        _tree.Dock = DockStyle.Fill;
        _tree.BackColor = Theme.Bg0;
        _tree.ForeColor = Theme.Text;
        _tree.BorderStyle = BorderStyle.None;
        _tree.HideSelection = false;
        _tree.ItemHeight = 22;
        _tree.AfterSelect += (s, e) => LoadCategory();
        PopulateTree();

        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.VirtualMode = true;
        _list.FullRowSelect = true;
        _list.GridLines = false;
        _list.BorderStyle = BorderStyle.None;
        _list.BackColor = Theme.Bg0;
        _list.ForeColor = Theme.Text;
        _list.Columns.Add("名称", 320);
        _list.Columns.Add("概要", 560);
        _list.RetrieveVirtualItem += RetrieveItem;
        _list.SelectedIndexChanged += (s, e) => ShowDetail();
        _list.MultiSelect = false;

        _detail.Dock = DockStyle.Bottom;
        _detail.Height = 150;
        _detail.Multiline = true;
        _detail.ReadOnly = true;
        _detail.ScrollBars = ScrollBars.Vertical;
        _detail.BackColor = Theme.Bg0;
        _detail.ForeColor = Theme.TextDim;
        _detail.BorderStyle = BorderStyle.None;
        _detail.Font = Theme.UI(9f);
        _detail.Text = "选中左侧类别查看资源；本窗口只读，不写 data.win。";

        _summary.Dock = DockStyle.Bottom;
        _summary.Height = 28;
        _summary.TextAlign = ContentAlignment.MiddleLeft;
        _summary.Padding = new Padding(10, 0, 0, 0);
        Theme.StyleLabel(_summary, Theme.TextDim);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel1,
            SplitterDistance = 230,
            BackColor = Theme.Line,
        };
        split.Panel1.BackColor = Theme.Bg1;
        split.Panel2.BackColor = Theme.Bg1;
        split.Panel1.Controls.Add(_tree);
        split.Panel2.Controls.Add(_list);
        split.Panel2.Controls.Add(_detail);

        var buttons = new Panel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(10, 8, 10, 8), BackColor = Theme.Bg1 };
        var close = new Button { Text = "关闭", Dock = DockStyle.Right, Width = 96, DialogResult = DialogResult.Cancel };
        Theme.StyleButton(close, true);
        buttons.Controls.Add(close);

        Controls.Add(split);
        Controls.Add(buttons);
        Controls.Add(_summary);
        CancelButton = close;

        Shown += (s, e) => Theme.EnableDarkTitleBar(this);
    }

    private void PopulateTree()
    {
        foreach (var cat in ResourceCatalog.Categories)
        {
            var node = new TreeNode(cat.Label) { Tag = cat.Key };
            _tree.Nodes.Add(node);
        }
        if (_tree.Nodes.Count > 0) _tree.SelectedNode = _tree.Nodes[0];
    }

    private void LoadCategory()
    {
        if (_tree.SelectedNode?.Tag is not string key) return;
        UseWaitCursor = true;
        try
        {
            if (!_cache.TryGetValue(key, out var rows))
            {
                rows = ResourceCatalog.Enumerate(_gd.Data, key).ToList();
                _cache[key] = rows;
            }
            _rows = rows;
            _list.VirtualListSize = rows.Count;
            _list.Invalidate();
            _detail.Text = "（选中一行查看完整字段）";
            var cat = ResourceCatalog.Categories.FirstOrDefault(c => c.Key == key);
            _summary.Text = $"{cat?.Label ?? key}: {rows.Count} 项（只读，来自 {Path.GetFileName(_gd.SourcePath)}）";
        }
        catch (Exception ex)
        {
            _rows = new List<ResourceCatalog.Entry>();
            _list.VirtualListSize = 0;
            _detail.Text = "[错误] 读取该类资源失败: " + ex.Message;
            _summary.Text = "读取失败";
        }
        finally { UseWaitCursor = false; }
    }

    private void RetrieveItem(object? sender, RetrieveVirtualItemEventArgs e)
    {
        if (e.ItemIndex < 0 || e.ItemIndex >= _rows.Count)
        {
            e.Item = new ListViewItem(string.Empty);
            return;
        }
        var row = _rows[e.ItemIndex];
        e.Item = new ListViewItem(new[] { row.Name, row.Detail });
    }

    private void ShowDetail()
    {
        if (_list.SelectedIndices.Count == 0) return;
        int i = _list.SelectedIndices[0];
        if (i >= 0 && i < _rows.Count) _detail.Text = _rows[i].Full;
    }
}
