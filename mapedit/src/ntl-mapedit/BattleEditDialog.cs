using System.Text.Json;
using UndertaleModLib.Models;

namespace NtlMapEdit;

/// <summary>
/// 一场战斗的编辑窗口。战斗绑定在地图上的一个实体（房间 + 实例 ID），
/// 由地图编辑器在选中实体时打开——触发条件（房间/坐标）从实体本身取，不需要手填。
/// </summary>
public sealed class BattleEditDialog : Form
{
    private readonly GameData _gd;
    private readonly BattleDef _b;
    private readonly string _room;
    private readonly uint _instanceId;
    private readonly string _objectName;

    private readonly TextBox _id = new();
    private readonly TextBox _name = new();
    private readonly ComboBox _kind = new();
    private readonly ComboBox _triggerMode = new();
    private readonly CheckBox _enabled = new();
    private readonly TextBox _startMessage = new();
    private readonly TextBox _roundLimit = new();
    private readonly CheckBox _loopLast = new();
    private readonly TextBox _tension = new();
    private readonly TextBox _preDelay = new();

    private readonly DataGridView _enemyGrid = new();
    private readonly DataGridView _barrageGrid = new();
    private readonly ListBox _turnList = new();
    private readonly TextBox _turnMessage = new();
    private readonly TextBox _turnDuration = new();
    private readonly List<TurnDef> _turns = new();
    private TurnDef? _currentTurn;

    private readonly TextBox _owMode = new();
    private readonly TextBox _owDuration = new();
    private readonly TextBox _owDamage = new();
    private readonly TextBox _owRadius = new();
    private readonly TextBox _owSprite = new();
    private readonly DataGridView _patternGrid = new();

    private readonly TextBox _trigFlag = new();
    private readonly TextBox _trigRadius = new();
    private readonly CheckBox _trigOnce = new();
    private readonly CheckBox _persistOnce = new();
    private readonly TextBox _vicFlags = new();
    private readonly TextBox _vicClear = new();
    private readonly TextBox _vicHeal = new();
    private readonly TextBox _vicRoom = new();
    private readonly TextBox _vicBattle = new();
    private readonly TextBox _vicMsg = new();
    private readonly TextBox _defFlags = new();
    private readonly TextBox _defClear = new();
    private readonly TextBox _defHeal = new();
    private readonly TextBox _defRoom = new();
    private readonly TextBox _defBattle = new();
    private readonly TextBox _defMsg = new();

    private readonly Label _error = new();

    public BattleEditDialog(GameData gd, BattleDef battle, string room, uint instanceId, string objectName)
    {
        _gd = gd;
        _b = battle;
        _room = room;
        _instanceId = instanceId;
        _objectName = objectName;

        Text = "战斗编辑 —— " + (string.IsNullOrEmpty(room) ? "未绑定房间" : room) + " / " + objectName + " #" + instanceId;
        ClientSize = new Size(980, 760);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Bg0;
        ForeColor = Theme.Text;
        Font = Theme.UI(9f);
        MinimizeBox = false;

        BuildUi();
        LoadBattle();
    }

    private void BuildUi()
    {
        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 6) };
        tabs.TabPages.Add(BuildBasicTab());
        tabs.TabPages.Add(BuildEnemyTab());
        tabs.TabPages.Add(BuildTurnTab());
        tabs.TabPages.Add(BuildOverworldTab());
        tabs.TabPages.Add(BuildTriggerTab());

        _error.Dock = DockStyle.Fill;
        _error.ForeColor = Theme.Red;
        _error.TextAlign = ContentAlignment.MiddleLeft;
        Theme.StyleLabel(_error, Theme.Red);

        var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 52, ColumnCount = 3, Padding = new Padding(10, 8, 10, 8) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        bottom.Controls.Add(_error, 0, 0);
        var cancel = new Button { Text = "取消", Dock = DockStyle.Fill, Height = 30 };
        var ok = new Button { Text = "保存", Dock = DockStyle.Fill, Height = 30 };
        Theme.StyleButton(cancel);
        Theme.StyleButton(ok, accent: true);
        cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        ok.Click += (s, e) => { if (SaveToBattle()) { DialogResult = DialogResult.OK; Close(); } };
        bottom.Controls.Add(cancel, 1, 0);
        bottom.Controls.Add(ok, 2, 0);

        Controls.Add(tabs);
        Controls.Add(bottom);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    private TabPage NewTab(string text)
    {
        var tab = new TabPage(text) { BackColor = Theme.Bg0, ForeColor = Theme.Text, Padding = new Padding(8) };
        return tab;
    }

    private Label Lbl(string text) => new()
    {
        Text = text, AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Theme.TextDim, BackColor = Color.Transparent, Font = Theme.UI(9f),
    };

    private TextBox Txt(TextBox t)
    {
        t.Dock = DockStyle.Fill;
        Theme.StyleTextBox(t);
        t.Margin = new Padding(0, 3, 8, 3);
        return t;
    }

    private ComboBox Cmb(ComboBox c)
    {
        c.Dock = DockStyle.Fill;
        c.DropDownStyle = ComboBoxStyle.DropDown;
        c.BackColor = Theme.Bg2;
        c.ForeColor = Theme.Text;
        c.FlatStyle = FlatStyle.Flat;
        c.Margin = new Padding(0, 3, 8, 3);
        return c;
    }

    private CheckBox Chk(CheckBox c, string text)
    {
        c.Text = text;
        c.ForeColor = Theme.Text;
        c.BackColor = Color.Transparent;
        c.FlatStyle = FlatStyle.Flat;
        c.Dock = DockStyle.Fill;
        return c;
    }

    private TableLayoutPanel Form2(int rows)
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(4) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < rows; i++) t.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        return t;
    }

    private void AddRow(TableLayoutPanel t, int row, string label, Control c)
    {
        t.Controls.Add(Lbl(label), 0, row);
        t.Controls.Add(c, 1, row);
    }

    private TabPage BuildBasicTab()
    {
        var tab = NewTab("基本");
        var form = Form2(10);
        AddRow(form, 0, "战斗 ID", Txt(_id));
        AddRow(form, 1, "名称", Txt(_name));
        _kind.Items.AddRange(new object[] { "encounter", "overworld" });
        AddRow(form, 2, "类型（encounter/overworld）", Cmb(_kind));
        _triggerMode.Items.AddRange(new object[] { "auto（进入房间 / 触发半径内自动开战）", "interact（玩家面对该实体按 Z/Enter 开战）" });
        _triggerMode.SelectedIndex = 0;
        Cmb(_triggerMode);
        _triggerMode.DropDownStyle = ComboBoxStyle.DropDownList;
        AddRow(form, 3, "触发方式（auto/interact）", _triggerMode);
        AddRow(form, 4, "启用", Chk(_enabled, "参与运行时触发/控制台列表"));
        AddRow(form, 5, "开场文字", Txt(_startMessage));
        AddRow(form, 6, "回合上限（0=不限）", Txt(_roundLimit));
        AddRow(form, 7, "循环最后一回合", Chk(_loopLast, "回合数超过配置后重复最后一回合的弹幕"));
        AddRow(form, 8, "初始紧张度", Txt(_tension));
        AddRow(form, 9, "战斗前延迟（帧）", Txt(_preDelay));
        tab.Controls.Add(form);
        var hint = new Label
        {
            Dock = DockStyle.Bottom, Height = 84, ForeColor = Theme.TextDim, Font = Theme.UI(8.5f),
            Text = "说明：encounter = 进入战斗界面的回合制战斗（配置敌人与每回合弹幕）；overworld = 在地图上直接躲弹幕。\r\n" +
                   "这场战斗绑定在你选中的地图实体上：进入该房间（或走到触发半径内）就会按配置触发。\r\n" +
                   "触发方式：auto = 进入房间 / 触发半径内自动开战；interact = 玩家面对实体按 Z/Enter 才开战。",
        };
        tab.Controls.Add(hint);
        return tab;
    }

    private TabPage BuildEnemyTab()
    {
        var tab = NewTab("敌人");
        ConfigureGrid(_enemyGrid);
        _enemyGrid.Columns.Add(ComboCol("对象", 170));
        _enemyGrid.Columns.Add(TextCol("名称", 110));
        _enemyGrid.Columns.Add(TextCol("HP", 60));
        _enemyGrid.Columns.Add(TextCol("AT", 55));
        _enemyGrid.Columns.Add(TextCol("DF", 55));
        _enemyGrid.Columns.Add(TextCol("EXP", 55));
        _enemyGrid.Columns.Add(TextCol("Gold", 55));
        _enemyGrid.Columns.Add(TextCol("宽恕值", 65));
        _enemyGrid.Columns.Add(TextCol("宽恕上限", 70));
        _enemyGrid.Columns.Add(TextCol("状态", 65));
        _enemyGrid.Columns.Add(TextCol("评论", 130));
        _enemyGrid.Columns.Add(TextCol("ACT（逗号分隔）", 130));
        _enemyGrid.Dock = DockStyle.Fill;
        var host = new Panel { Dock = DockStyle.Fill };
        host.Controls.Add(_enemyGrid);
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 34, FlowDirection = FlowDirection.LeftToRight };
        var add = MakeBtn("添加敌人");
        add.Click += (s, e) => AddEnemyRow(new EnemyDef());
        var del = MakeBtn("删除选中");
        del.Click += (s, e) => { foreach (DataGridViewRow r in _enemyGrid.SelectedRows) _enemyGrid.Rows.Remove(r); };
        bar.Controls.Add(add);
        bar.Controls.Add(del);
        tab.Controls.Add(host);
        tab.Controls.Add(bar);
        var top = new Label
        {
            Dock = DockStyle.Top, Height = 30, ForeColor = Theme.TextDim, Font = Theme.UI(8.5f),
            Text = "encounter 战斗最多 3 个敌人；对象名来自 data.win（例如 obj_bloxer_enemy）。",
        };
        tab.Controls.Add(top);
        return tab;
    }

    private TabPage BuildTurnTab()
    {
        var tab = NewTab("回合 / 弹幕");
        _turnList.Dock = DockStyle.Fill;
        _turnList.BackColor = Theme.Bg1;
        _turnList.ForeColor = Theme.Text;
        _turnList.Font = Theme.UI(9f);
        _turnList.IntegralHeight = false;
        _turnList.SelectedIndexChanged += (s, e) => { FlushTurn(); SelectTurn(); };

        var left = new Panel { Dock = DockStyle.Left, Width = 190 };
        var leftBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 34 };
        var addTurn = MakeBtn("添加");
        addTurn.Click += (s, e) => { FlushTurn(); var t = new TurnDef(); _turns.Add(t); RefreshTurnList(_turns.Count - 1); SelectTurn(); };
        var delTurn = MakeBtn("删除");
        delTurn.Click += (s, e) => { if (_currentTurn == null) return; _turns.Remove(_currentTurn); RefreshTurnList(Math.Min(_turnList.Items.Count - 1, 0)); SelectTurn(); };
        var up = MakeBtn("上移");
        up.Click += (s, e) => MoveTurn(-1);
        var down = MakeBtn("下移");
        down.Click += (s, e) => MoveTurn(1);
        leftBar.Controls.Add(addTurn);
        leftBar.Controls.Add(delTurn);
        leftBar.Controls.Add(up);
        leftBar.Controls.Add(down);
        left.Controls.Add(_turnList);
        left.Controls.Add(leftBar);

        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 0, 0) };
        var head = new TableLayoutPanel { Dock = DockStyle.Top, Height = 64, ColumnCount = 2 };
        head.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        head.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        head.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        head.Controls.Add(Lbl("回合文字"), 0, 0);
        head.Controls.Add(Txt(_turnMessage), 1, 0);
        head.Controls.Add(Lbl("持续帧数"), 0, 1);
        head.Controls.Add(Txt(_turnDuration), 1, 1);

        ConfigureGrid(_barrageGrid);
        _barrageGrid.Columns.Add(TextCol("type", 60));
        _barrageGrid.Columns.Add(TextCol("数量", 55));
        _barrageGrid.Columns.Add(TextCol("ratio", 60));
        _barrageGrid.Columns.Add(TextCol("伤害(0=AT*5)", 85));
        _barrageGrid.Columns.Add(TextCol("timermax", 70));
        _barrageGrid.Columns.Add(TextCol("joker", 55));
        _barrageGrid.Columns.Add(TextCol("side", 55));
        _barrageGrid.Columns.Add(TextCol("graze", 55));
        _barrageGrid.Columns.Add(TextCol("time", 55));
        _barrageGrid.Columns.Add(TextCol("随机 type（逗号）", 130));
        _barrageGrid.Columns.Add(TextCol("ratio 浮动", 70));
        _barrageGrid.Columns.Add(TextCol("延迟帧", 60));
        _barrageGrid.Dock = DockStyle.Fill;
        var gridBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 34 };
        var addB = MakeBtn("添加弹幕");
        addB.Click += (s, e) => AddBarrageRow(new BarrageDef());
        var delB = MakeBtn("删除弹幕");
        delB.Click += (s, e) => { foreach (DataGridViewRow r in _barrageGrid.SelectedRows) _barrageGrid.Rows.Remove(r); };
        gridBar.Controls.Add(addB);
        gridBar.Controls.Add(delB);
        var hint = new Label
        {
            Dock = DockStyle.Bottom, Height = 64, ForeColor = Theme.TextDim, Font = Theme.UI(8.5f),
            Text = "type 是原版 obj_dbulletcontroller 的弹幕模板编号，按目标章节（生成 mod 的 leaf）取该章编号；不同章节同号形状不同（例：章节2 的 26/27 是 Rouxls 专用攻击，别的章节用会崩）。\r\n" +
                   "跨章节最稳的是 0（红心周围环形）与 1（上下边缘）；「随机 type」非空时每回合随机挑一个，ratio 浮动可给数量加随机。",
        };

        right.Controls.Add(_barrageGrid);
        right.Controls.Add(gridBar);
        right.Controls.Add(head);
        right.Controls.Add(hint);
        tab.Controls.Add(right);
        tab.Controls.Add(left);
        return tab;
    }

    private TabPage BuildOverworldTab()
    {
        var tab = NewTab("Overworld 弹幕");
        var form = Form2(5);
        AddRow(form, 0, "模式（danmaku/encounter）", Txt(_owMode));
        AddRow(form, 1, "持续帧数", Txt(_owDuration));
        AddRow(form, 2, "接触伤害", Txt(_owDamage));
        AddRow(form, 3, "敌人半径", Txt(_owRadius));
        AddRow(form, 4, "敌人精灵（可空）", Txt(_owSprite));
        ConfigureGrid(_patternGrid);
        _patternGrid.Columns.Add(ComboCol("kind", 110));
        foreach (var (h, w) in new[] { ("interval", 70), ("count", 60), ("speed", 65), ("spread", 65), ("damage", 65), ("life", 60), ("angle", 60), ("随机角度", 70), ("随机数量", 70), ("随机速度", 70) })
            _patternGrid.Columns.Add(TextCol(h, w));
        _patternGrid.Dock = DockStyle.Fill;
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 34 };
        var add = MakeBtn("添加模式");
        add.Click += (s, e) => AddPatternRow(new PatternDef());
        var del = MakeBtn("删除选中");
        del.Click += (s, e) => { foreach (DataGridViewRow r in _patternGrid.SelectedRows) _patternGrid.Rows.Remove(r); };
        bar.Controls.Add(add);
        bar.Controls.Add(del);
        var hint = new Label
        {
            Dock = DockStyle.Bottom, Height = 44, ForeColor = Theme.TextDim, Font = Theme.UI(8.5f),
            Text = "kind：rain 从上方落 / ring 环形爆发 / aim 朝玩家 / wall 横向墙。\n每个模式按 interval 帧定时生成 count 发子弹。",
        };
        tab.Controls.Add(_patternGrid);
        tab.Controls.Add(bar);
        tab.Controls.Add(form);
        tab.Controls.Add(hint);
        return tab;
    }

    private TabPage BuildTriggerTab()
    {
        var tab = NewTab("触发 / 结局");
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var form = Form2(14);
        AddRow(form, 0, "绑定房间", Lbl(_room));
        AddRow(form, 1, "绑定实体", Lbl(_objectName + "  #" + _instanceId));
        AddRow(form, 2, "需要 flag（0=不需要）", Txt(_trigFlag));
        AddRow(form, 3, "触发半径（0=进房间即触发）", Txt(_trigRadius));
        AddRow(form, 4, "只触发一次（本次游戏）", Chk(_trigOnce, "触发后本局不再重复（once）"));
        AddRow(form, 5, "跨重启只触发一次", Chk(_persistOnce, "写进持久数据（mods-shared.json），重开游戏也不会再触发"));
        AddRow(form, 6, "胜利时设置 flag", Txt(_vicFlags));
        AddRow(form, 7, "胜利时清除 flag", Txt(_vicClear));
        AddRow(form, 8, "胜利时回复队伍百分比", Txt(_vicHeal));
        AddRow(form, 9, "胜利后去房间", Txt(_vicRoom));
        AddRow(form, 10, "胜利后开始战斗 id", Txt(_vicBattle));
        AddRow(form, 11, "胜利提示文字", Txt(_vicMsg));
        AddRow(form, 12, "失败时设置 flag", Txt(_defFlags));
        AddRow(form, 13, "失败后去房间", Txt(_defRoom));
        scroll.Controls.Add(form);
        var hint = new Label
        {
            Dock = DockStyle.Bottom, Height = 76, ForeColor = Theme.TextDim, Font = Theme.UI(8.5f),
            Text = "flag 列表用逗号分隔（例如 777,778）。胜利效果在战斗控制器销毁后应用；\r\n" +
                   "失败默认交给原版 Game Over 流程，这里只额外做 flag/传送。\r\n" +
                   "「跨重启只触发一次」写 Neutraled 的共享持久数据（mods-shared.json）；只勾「只触发一次」时重开游戏会重置。",
        };
        tab.Controls.Add(scroll);
        tab.Controls.Add(hint);
        return tab;
    }

    // ---------------------------------------------------------------- 载入 / 保存

    private void LoadBattle()
    {
        _id.Text = _b.Id;
        _name.Text = _b.Name;
        _kind.Text = _b.Kind;
        _triggerMode.SelectedIndex = _b.TriggerMode == "interact" ? 1 : 0;
        _enabled.Checked = _b.Enabled;
        _startMessage.Text = _b.StartMessage;
        _roundLimit.Text = _b.RoundLimit.ToString();
        _loopLast.Checked = _b.LoopLastTurn;
        _tension.Text = _b.StartingTension.ToString();
        _preDelay.Text = _b.PreTurnDelay.ToString();

        foreach (var e in _b.Enemies) AddEnemyRow(e);
        if (_b.Enemies.Count == 0) AddEnemyRow(new EnemyDef());

        _turns.Clear();
        foreach (var t in _b.Turns) _turns.Add(t);
        RefreshTurnList(0);
        SelectTurn();

        _owMode.Text = _b.Overworld.Mode;
        _owDuration.Text = _b.Overworld.Duration.ToString();
        _owDamage.Text = _b.Overworld.ContactDamage.ToString();
        _owRadius.Text = _b.Overworld.EnemyRadius.ToString();
        _owSprite.Text = _b.Overworld.Sprite;
        foreach (var p in _b.Overworld.Patterns) AddPatternRow(p);

        _trigFlag.Text = _b.Trigger.RequiredFlag.ToString();
        _trigRadius.Text = _b.Trigger.Radius.ToString();
        _trigOnce.Checked = _b.Trigger.Once;
        _persistOnce.Checked = _b.Trigger.PersistOnce;
        FillEffect(_vicFlags, _vicClear, _vicHeal, _vicRoom, _vicBattle, _vicMsg, _b.Victory);
        FillEffect(_defFlags, _defClear, _defHeal, _defRoom, _defBattle, _defMsg, _b.Defeat);
    }

    private static void FillEffect(TextBox flags, TextBox clear, TextBox heal, TextBox room, TextBox battle, TextBox msg, EffectDef e)
    {
        flags.Text = string.Join(",", e.SetFlags);
        clear.Text = string.Join(",", e.ClearFlags);
        heal.Text = e.HealPercent.ToString();
        room.Text = e.GotoRoom;
        battle.Text = e.StartBattle;
        msg.Text = e.Message;
    }

    private bool SaveToBattle()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(_id.Text)) errors.Add("战斗 ID 不能为空");
        var kind = _kind.Text.Trim().ToLowerInvariant();
        if (kind != "encounter" && kind != "overworld") errors.Add("类型必须是 encounter 或 overworld");
        FlushTurn();
        var enemies = ReadEnemyGrid();
        if (kind == "encounter" && enemies.Count == 0) errors.Add("encounter 战斗至少需要一个敌人");
        if (enemies.Count > 3) errors.Add("最多 3 个敌人");
        if (kind == "encounter" && _turns.Count == 0) errors.Add("encounter 战斗至少需要一个回合（用来发弹幕）");
        if (errors.Count > 0)
        {
            _error.Text = string.Join("  |  ", errors);
            return false;
        }
        _error.Text = "";

        _b.Id = _id.Text.Trim();
        _b.Name = _name.Text.Trim();
        _b.Kind = kind;
        _b.TriggerMode = _triggerMode.SelectedIndex == 1 ? "interact" : "auto";
        _b.Enabled = _enabled.Checked;
        _b.StartMessage = _startMessage.Text;
        _b.RoundLimit = ParseInt(_roundLimit, 0);
        _b.LoopLastTurn = _loopLast.Checked;
        _b.StartingTension = ParseInt(_tension, 0);
        _b.PreTurnDelay = Math.Max(1, ParseInt(_preDelay, 24));
        _b.Enemies = enemies;
        _b.Turns = _turns.ToList();
        _b.Overworld.Mode = _owMode.Text.Trim();
        _b.Overworld.Duration = Math.Max(1, ParseInt(_owDuration, 300));
        _b.Overworld.ContactDamage = ParseInt(_owDamage, 4);
        _b.Overworld.EnemyRadius = ParseInt(_owRadius, 20);
        _b.Overworld.Sprite = _owSprite.Text.Trim();
        _b.Overworld.Patterns = ReadPatterns();
        _b.Trigger.Room = _room;
        _b.Trigger.RequiredFlag = ParseInt(_trigFlag, 0);
        _b.Trigger.Radius = ParseInt(_trigRadius, 0);
        _b.Trigger.Once = _trigOnce.Checked;
        _b.Trigger.PersistOnce = _persistOnce.Checked;
        _b.Trigger.X = _b.InstanceX;
        _b.Trigger.Y = _b.InstanceY;
        _b.Victory = ReadEffect(_vicFlags, _vicClear, _vicHeal, _vicRoom, _vicBattle, _vicMsg);
        _b.Defeat = ReadEffect(_defFlags, _defClear, _defHeal, _defRoom, _defBattle, _defMsg);
        return true;
    }

    private static EffectDef ReadEffect(TextBox flags, TextBox clear, TextBox heal, TextBox room, TextBox battle, TextBox msg) => new()
    {
        SetFlags = ParseInts(flags.Text),
        ClearFlags = ParseInts(clear.Text),
        HealPercent = ParseInt(heal, 0),
        GotoRoom = room.Text.Trim(),
        StartBattle = battle.Text.Trim(),
        Message = msg.Text.Trim(),
    };

    private static int ParseInt(TextBox t, int def) => int.TryParse(t.Text.Trim(), out var v) ? v : def;

    /// <summary>表格单元格里的文本版本（ReadBarrages/ReadPatterns 从 DataGridView 取值时用）。</summary>
    private static int ParseInt(string s, int def) => int.TryParse(s.Trim(), out var v) ? v : def;

    private static double ParseDbl(string s, double def) =>
        double.TryParse(s.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : def;

    private static List<int> ParseInts(string text) =>
        text.Split(new[] { ',', ' ', '，' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s.Trim(), out var v) ? v : int.MinValue)
            .Where(v => v != int.MinValue).ToList();

    // ---------------------------------------------------------------- 敌人表

    private List<string> ObjectNames()
    {
        var list = _gd.GameObjects.Select(o => GameData.Name(o.Name)).Where(n => n.Length > 0).OrderBy(n => n).ToList();
        return list;
    }

    private void ConfigureGrid(DataGridView g)
    {
        g.BackgroundColor = Theme.Bg1;
        g.GridColor = Theme.Line;
        g.BorderStyle = BorderStyle.None;
        g.EnableHeadersVisualStyles = false;
        g.ColumnHeadersDefaultCellStyle.BackColor = Theme.Bg2;
        g.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Text;
        g.ColumnHeadersDefaultCellStyle.Font = Theme.UI(8.5f, FontStyle.Bold);
        g.DefaultCellStyle.BackColor = Theme.Bg1;
        g.DefaultCellStyle.ForeColor = Theme.Text;
        g.DefaultCellStyle.SelectionBackColor = Theme.AccentDim;
        g.DefaultCellStyle.SelectionForeColor = Color.White;
        g.DefaultCellStyle.Font = Theme.UI(8.5f);
        g.RowHeadersVisible = false;
        g.AllowUserToAddRows = false;
        g.AllowUserToResizeRows = false;
        g.RowTemplate.Height = 24;
        g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        g.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
    }

    private static DataGridViewTextBoxColumn TextCol(string header, int width) => new()
    {
        HeaderText = header, Width = width, SortMode = DataGridViewColumnSortMode.NotSortable,
    };

    private DataGridViewComboBoxColumn ComboCol(string header, int width)
    {
        var col = new DataGridViewComboBoxColumn
        {
            HeaderText = header, Width = width, SortMode = DataGridViewColumnSortMode.NotSortable,
            FlatStyle = FlatStyle.Flat,
        };
        foreach (var n in ObjectNames()) col.Items.Add(n);
        return col;
    }

    private void AddEnemyRow(EnemyDef e)
    {
        int i = _enemyGrid.Rows.Add();
        var r = _enemyGrid.Rows[i];
        r.Tag = e;
        r.Cells[0].Value = e.Object;
        r.Cells[1].Value = e.Name;
        r.Cells[2].Value = e.Hp.ToString();
        r.Cells[3].Value = e.At.ToString();
        r.Cells[4].Value = e.Df.ToString();
        r.Cells[5].Value = e.Exp.ToString();
        r.Cells[6].Value = e.Gold.ToString();
        r.Cells[7].Value = e.SparePoint.ToString();
        r.Cells[8].Value = e.MercyMax.ToString();
        r.Cells[9].Value = e.Status.ToString();
        r.Cells[10].Value = e.Comment;
        r.Cells[11].Value = string.Join(",", e.Acts);
    }

    private List<EnemyDef> ReadEnemyGrid()
    {
        var list = new List<EnemyDef>();
        foreach (DataGridViewRow r in _enemyGrid.Rows)
        {
            if (r.IsNewRow) continue;
            var e = r.Tag as EnemyDef ?? new EnemyDef();
            var obj = (r.Cells[0].Value ?? "").ToString() ?? "";
            if (string.IsNullOrWhiteSpace(obj) && string.IsNullOrWhiteSpace((r.Cells[1].Value ?? "").ToString())) continue;
            e.Object = obj.Trim();
            e.Name = (r.Cells[1].Value ?? "").ToString() ?? "";
            e.Hp = Math.Max(1, ParseDbl((r.Cells[2].Value ?? "").ToString() ?? "", e.Hp));
            e.At = ParseDbl((r.Cells[3].Value ?? "").ToString() ?? "", e.At);
            e.Df = ParseDbl((r.Cells[4].Value ?? "").ToString() ?? "", e.Df);
            e.Exp = ParseDbl((r.Cells[5].Value ?? "").ToString() ?? "", e.Exp);
            e.Gold = ParseDbl((r.Cells[6].Value ?? "").ToString() ?? "", e.Gold);
            e.SparePoint = ParseDbl((r.Cells[7].Value ?? "").ToString() ?? "", e.SparePoint);
            e.MercyMax = ParseDbl((r.Cells[8].Value ?? "").ToString() ?? "", e.MercyMax);
            e.Status = ParseDbl((r.Cells[9].Value ?? "").ToString() ?? "", e.Status);
            e.Comment = (r.Cells[10].Value ?? "").ToString() ?? "";
            e.Acts = ((r.Cells[11].Value ?? "").ToString() ?? "").Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            if (e.Acts.Count == 0) e.Acts = new List<string> { "Check", "Talk" };
            list.Add(e);
        }
        return list;
    }

    private List<BarrageDef> ReadBarrages()
    {
        var list = new List<BarrageDef>();
        foreach (DataGridViewRow r in _barrageGrid.Rows)
        {
            if (r.IsNewRow) continue;
            var g = r.Tag as BarrageDef ?? new BarrageDef();
            g.Type = ParseInt((r.Cells[0].Value ?? "").ToString() ?? "", g.Type);
            g.Count = Math.Max(1, ParseInt((r.Cells[1].Value ?? "").ToString() ?? "", g.Count));
            g.Ratio = ParseDbl((r.Cells[2].Value ?? "").ToString() ?? "", g.Ratio);
            g.Damage = ParseDbl((r.Cells[3].Value ?? "").ToString() ?? "", g.Damage);
            g.TimerMax = Math.Max(1, ParseInt((r.Cells[4].Value ?? "").ToString() ?? "", g.TimerMax));
            g.Joker = ParseInt((r.Cells[5].Value ?? "").ToString() ?? "", g.Joker);
            g.Side = ParseInt((r.Cells[6].Value ?? "").ToString() ?? "", g.Side);
            g.GrazePoints = ParseDbl((r.Cells[7].Value ?? "").ToString() ?? "", g.GrazePoints);
            g.TimePoints = ParseDbl((r.Cells[8].Value ?? "").ToString() ?? "", g.TimePoints);
            g.RandomTypes = ParseInts((r.Cells[9].Value ?? "").ToString() ?? "");
            g.RandomRatio = ParseDbl((r.Cells[10].Value ?? "").ToString() ?? "", g.RandomRatio);
            g.Delay = ParseInt((r.Cells[11].Value ?? "").ToString() ?? "", g.Delay);
            list.Add(g);
        }
        return list;
    }

    private void AddBarrageRow(BarrageDef g)
    {
        int i = _barrageGrid.Rows.Add();
        var r = _barrageGrid.Rows[i];
        r.Tag = g;
        r.Cells[0].Value = g.Type.ToString();
        r.Cells[1].Value = g.Count.ToString();
        r.Cells[2].Value = g.Ratio.ToString("0.##");
        r.Cells[3].Value = g.Damage.ToString();
        r.Cells[4].Value = g.TimerMax.ToString();
        r.Cells[5].Value = g.Joker.ToString();
        r.Cells[6].Value = g.Side.ToString();
        r.Cells[7].Value = g.GrazePoints.ToString();
        r.Cells[8].Value = g.TimePoints.ToString();
        r.Cells[9].Value = string.Join(",", g.RandomTypes);
        r.Cells[10].Value = g.RandomRatio.ToString("0.##");
        r.Cells[11].Value = g.Delay.ToString();
    }

    private List<PatternDef> ReadPatterns()
    {
        var list = new List<PatternDef>();
        foreach (DataGridViewRow r in _patternGrid.Rows)
        {
            if (r.IsNewRow) continue;
            var p = r.Tag as PatternDef ?? new PatternDef();
            p.Kind = ((r.Cells[0].Value ?? "rain").ToString() ?? "rain").Trim();
            p.Interval = Math.Max(1, ParseInt((r.Cells[1].Value ?? "").ToString() ?? "", p.Interval));
            p.Count = Math.Max(1, ParseInt((r.Cells[2].Value ?? "").ToString() ?? "", p.Count));
            p.Speed = ParseDbl((r.Cells[3].Value ?? "").ToString() ?? "", p.Speed);
            p.Spread = ParseDbl((r.Cells[4].Value ?? "").ToString() ?? "", p.Spread);
            p.Damage = ParseDbl((r.Cells[5].Value ?? "").ToString() ?? "", p.Damage);
            p.Life = Math.Max(1, ParseInt((r.Cells[6].Value ?? "").ToString() ?? "", p.Life));
            p.Angle = ParseDbl((r.Cells[7].Value ?? "").ToString() ?? "", p.Angle);
            p.RandomAngle = ParseDbl((r.Cells[8].Value ?? "").ToString() ?? "", p.RandomAngle);
            p.RandomCount = ParseInt((r.Cells[9].Value ?? "").ToString() ?? "", p.RandomCount);
            p.RandomSpeed = ParseDbl((r.Cells[10].Value ?? "").ToString() ?? "", p.RandomSpeed);
            list.Add(p);
        }
        return list;
    }

    private void AddPatternRow(PatternDef p)
    {
        int i = _patternGrid.Rows.Add();
        var r = _patternGrid.Rows[i];
        r.Tag = p;
        r.Cells[0].Value = p.Kind;
        r.Cells[1].Value = p.Interval.ToString();
        r.Cells[2].Value = p.Count.ToString();
        r.Cells[3].Value = p.Speed.ToString("0.##");
        r.Cells[4].Value = p.Spread.ToString("0.##");
        r.Cells[5].Value = p.Damage.ToString("0.##");
        r.Cells[6].Value = p.Life.ToString();
        r.Cells[7].Value = p.Angle.ToString("0.##");
        r.Cells[8].Value = p.RandomAngle.ToString("0.##");
        r.Cells[9].Value = p.RandomCount.ToString();
        r.Cells[10].Value = p.RandomSpeed.ToString("0.##");
    }

    // ---------------------------------------------------------------- 回合列表

    private void RefreshTurnList(int select)
    {
        _turnList.Items.Clear();
        for (int i = 0; i < _turns.Count; i++)
        {
            var t = _turns[i];
            _turnList.Items.Add("回合 " + (i + 1) + "：" + (t.Barrages.Count == 0 ? "无弹幕" : t.Barrages.Count + " 组弹幕"));
        }
        if (_turnList.Items.Count > 0)
        {
            _turnList.SelectedIndex = Math.Clamp(select, 0, _turnList.Items.Count - 1);
        }
    }

    private void MoveTurn(int delta)
    {
        if (_currentTurn == null) return;
        int i = _turns.IndexOf(_currentTurn);
        int j = i + delta;
        if (i < 0 || j < 0 || j >= _turns.Count) return;
        FlushTurn();
        (_turns[i], _turns[j]) = (_turns[j], _turns[i]);
        RefreshTurnList(j);
        SelectTurn();
    }

    private void FlushTurn()
    {
        if (_currentTurn == null) return;
        _currentTurn.Message = _turnMessage.Text;
        _currentTurn.Duration = Math.Max(1, ParseInt(_turnDuration, 240));
        _currentTurn.Barrages = ReadBarrages();
    }

    private void SelectTurn()
    {
        _barrageGrid.Rows.Clear();
        var turn = (_turnList.SelectedIndex >= 0 && _turnList.SelectedIndex < _turns.Count) ? _turns[_turnList.SelectedIndex] : null;
        _currentTurn = turn;
        if (turn == null)
        {
            _turnMessage.Text = "";
            _turnDuration.Text = "";
            return;
        }
        _turnMessage.Text = turn.Message;
        _turnDuration.Text = turn.Duration.ToString();
        foreach (var g in turn.Barrages) AddBarrageRow(g);
    }

    private Button MakeBtn(string text)
    {
        var b = new Button { Text = text, AutoSize = true, Height = 28, Margin = new Padding(0, 2, 6, 2) };
        Theme.StyleButton(b);
        return b;
    }
}
