using System.Text.Json;

namespace Neutraled.Gui;

/// <summary>配置档（只读）弹窗 —— C4 P1-13 补齐的「GUI 只读入口」。
///
/// 数据来源是 builder 的 `--profile-list --json`（与 CLI / Web 同源，绝不自己在 GUI 里重写一套扫描规则）。
/// 为什么只读：套用配置档会批量改写每个 mod.json 的 enabled 与 config.json 的设置，
/// 那是「一次改很多文件」的动作，放在一个只是「看一眼」的弹窗里很容易误点。
/// 真要套用请用 CLI（--profile-use）或 Web 界面；这里的职责是让人在 GUI 里也能看到有哪些档、各开了几个 mod。
/// </summary>
public static class ProfilesDialog
{
    /// <summary>一行配置档（只保留展示需要的字段）。</summary>
    public sealed record Row(string Id, string Name, string Description, string Updated,
        int Enabled, string Chapters, string ModsRoot, bool Active);

    /// <summary>跑一次 --profile-list --json 并解析。返回 (rows, error)；error 非空表示拿不到数据。</summary>
    public static async Task<(Row[] Rows, string Error)> LoadAsync()
    {
        var gameRoot = Path.GetDirectoryName(Program.NeutraledRoot) ?? "";
        var lines = new List<string>();
        int code;
        try
        {
            code = await Program.RunBuilderAsync(
                $"--game \"{gameRoot}\" --profile-list --json",
                l => { lock (lines) lines.Add(l); });
        }
        catch (Exception ex) { return (Array.Empty<Row>(), ex.Message); }

        var text = string.Join("\n", lines);
        // 防御：万一将来 builder 又往 stdout 混了抬头/日志，这里截取最外层 JSON 对象再解析。
        int a = text.IndexOf('{'), b = text.LastIndexOf('}');
        if (code != 0 || a < 0 || b <= a)
            return (Array.Empty<Row>(), string.IsNullOrWhiteSpace(text) ? $"exit={code}" : text.Trim());

        try
        {
            using var doc = JsonDocument.Parse(text[a..(b + 1)]);
            var rows = new List<Row>();
            if (doc.RootElement.TryGetProperty("profiles", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in arr.EnumerateArray())
                {
                    string Str(string k) => p.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                    int Num(string k) => p.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;
                    var chapters = "";
                    if (p.TryGetProperty("chapters", out var ch) && ch.ValueKind == JsonValueKind.Array)
                        chapters = string.Join(", ", ch.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()));
                    var active = p.TryGetProperty("active", out var av) && av.ValueKind == JsonValueKind.True;
                    rows.Add(new Row(Str("id"), Str("name"), Str("description"), Str("updated"),
                        Num("enabledCount"), chapters, Str("modsRoot"), active));
                }
            }
            return (rows.ToArray(), "");
        }
        catch (Exception ex) { return (Array.Empty<Row>(), ex.Message); }
    }

    /// <summary>显示只读弹窗（调用方先 LoadAsync）。</summary>
    public static void Show(IWin32Window owner, Row[] rows, string error)
    {
        using var dlg = new Form
        {
            Text = Localizer.T("配置档（只读）"),
            Width = 880, Height = 480,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false, MaximizeBox = false
        };
        var hint = new Label
        {
            Text = Localizer.T("只读：这里只显示配置档内容。套用请用 CLI --profile-use 或 Web 界面（会修改各 mod 的启用态）。"),
            Left = 12, Top = 10, Width = 844, Height = 20, ForeColor = Color.DimGray
        };
        var list = new ListView
        {
            Left = 12, Top = 36, Width = 844, Height = 300,
            View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false
        };
        list.Columns.Add(Localizer.T("活动"), 44);
        list.Columns.Add(Localizer.T("ID"), 140);
        list.Columns.Add(Localizer.T("名称"), 170);
        list.Columns.Add(Localizer.T("启用 mod 数"), 90);
        list.Columns.Add(Localizer.T("章节"), 120);
        list.Columns.Add(Localizer.T("更新时间"), 150);
        list.Columns.Add(Localizer.T("mods 根"), 110);

        var detail = new TextBox
        {
            Left = 12, Top = 344, Width = 844, Height = 50,
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true
        };

        foreach (var r in rows)
        {
            var it = new ListViewItem(r.Active ? "*" : " ");
            it.SubItems.Add(r.Id);
            it.SubItems.Add(string.IsNullOrWhiteSpace(r.Name) ? "-" : r.Name);
            it.SubItems.Add(r.Enabled.ToString());
            it.SubItems.Add(string.IsNullOrWhiteSpace(r.Chapters) ? "-" : r.Chapters);
            it.SubItems.Add(string.IsNullOrWhiteSpace(r.Updated) ? "-" : r.Updated);
            it.SubItems.Add(string.IsNullOrWhiteSpace(r.ModsRoot) ? "-" : r.ModsRoot);
            it.Tag = r;
            list.Items.Add(it);
        }
        list.SelectedIndexChanged += (_, _) =>
        {
            if (list.SelectedItems.Count == 0) { detail.Text = ""; return; }
            var r = (Row)list.SelectedItems[0].Tag!;
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(r.Description)) parts.Add(Localizer.T("说明: ") + r.Description);
            parts.Add(Localizer.T("启用: ") + r.Enabled + "  " + Localizer.T("章节: ") + (string.IsNullOrWhiteSpace(r.Chapters) ? "-" : r.Chapters));
            detail.Text = string.Join(Environment.NewLine, parts);
        };
        if (list.Items.Count > 0) list.Items[0].Selected = true;

        var status = new Label
        {
            Text = error == ""
                ? Localizer.T("共 {0} 个配置档", rows.Length)
                : Localizer.T("加载配置档失败：{0}", error),
            Left = 12, Top = 400, Width = 844, Height = 20,
            ForeColor = error == "" ? Color.DimGray : Color.Firebrick
        };

        var close = new Button { Text = Localizer.T("关闭"), Left = 764, Top = 406, Width = 92, Height = 28, DialogResult = DialogResult.Cancel };
        dlg.Controls.AddRange(new Control[] { hint, list, detail, status, close });
        dlg.CancelButton = close;

        if (rows.Length == 0 && error == "")
            status.Text = Localizer.T("还没有配置档（用 --profile-new <id> 新建）。");

        dlg.ShowDialog(owner);
    }
}
