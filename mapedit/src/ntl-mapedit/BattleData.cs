using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NtlMapEdit;

// ------------------------------------------------------------------ 模型
// 编辑器/生成器/运行时三处共用同一份 schema；运行时 JSON 由 GML 用 json_parse 读成 struct。

public sealed class BattlesFile
{
    public int Version { get; set; } = 1;
    public BattleModInfo Mod { get; set; } = new();
    public List<BattleDef> Battles { get; set; } = new();
    /// <summary>纯对话配置（绑定地图实体；玩家靠近按 Z 时由生成的 mod 播放）。</summary>
    public List<DialogueDef> Dialogues { get; set; } = new();
    /// <summary>简单剧情（cutscene）：绑定地图实体，按 Z 或走进半径触发，按步骤驱动人物移动/转向/换动作/对话。</summary>
    public List<CutsceneDef> Cutscenes { get; set; } = new();
    /// <summary>mod 设置项：生成后注册到游戏内 Neutraled「Mod 设置」面板，可实时影响 mod 功能。</summary>
    public List<SettingDef> Settings { get; set; } = new();
    /// <summary>原版对话文本覆盖：按 lang key 改写原版台词（不改 data.win，生成 mod 后由 ntl_set_lang_string 生效）。</summary>
    public List<TextOverrideDef> TextOverrides { get; set; } = new();

    /// <summary>地图/房间差分：相对原版基线的改动（保存时重新 diff 得到，生成 mod.json 的 roomPatches 段）。</summary>
    public List<RoomPatch> RoomPatches { get; set; } = new();

    /// <summary>编辑时新建的房间（roomPatches 表达不了「新建」，走 builder 的 rooms 段；只支持空房间）。</summary>
    public List<NewRoomDef> NewRooms { get; set; } = new();

    /// <summary>
    /// 房间改名后，把配置里所有指向旧名的引用（战斗/对话/剧情的绑定房间与触发房间、结束效果的去向房间、
    /// 文本覆盖的来源房间）一起改成新名。不做这一步的话：基线里改名后 assets 中已无旧名，Validate 会报
    /// 「data.win 里没有房间 X」并阻断 mod 生成（真机/QA full10 实测踩过）。
    /// </summary>
    public int RenameRoomReferences(string oldName, string newName)
    {
        if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName)) return 0;
        if (string.Equals(oldName, newName, StringComparison.Ordinal)) return 0;
        int n = 0;
        string Fix(string s)
        {
            if (!string.Equals(s, oldName, StringComparison.Ordinal)) return s;
            n++;
            return newName;
        }
        foreach (var t in TextOverrides) t.Room = Fix(t.Room);
        foreach (var b in Battles)
        {
            b.Room = Fix(b.Room);
            b.Trigger.Room = Fix(b.Trigger.Room);
            b.Victory.GotoRoom = Fix(b.Victory.GotoRoom);
            b.Defeat.GotoRoom = Fix(b.Defeat.GotoRoom);
        }
        foreach (var d in Dialogues)
        {
            d.Room = Fix(d.Room);
            d.Trigger.Room = Fix(d.Trigger.Room);
            d.End.GotoRoom = Fix(d.End.GotoRoom);
        }
        foreach (var c in Cutscenes)
        {
            c.Room = Fix(c.Room);
            c.Trigger.Room = Fix(c.Trigger.Room);
            c.End.GotoRoom = Fix(c.End.GotoRoom);
        }
        return n;
    }
}

/// <summary>
/// 一条原版文本覆盖。key 是原版 lang key（如 obj_dialoguer_... 或 stringsetloc 的第二个参数）；
/// 生成 mod 时写成 ntl_set_lang_string(key, text)，运行时直接改 global.lang_map。
/// </summary>
public sealed class TextOverrideDef
{
    public string Key { get; set; } = "";
    public string Text { get; set; } = "";
    /// <summary>来源房间（便于在编辑器里按房间过滤；生成时不使用）。</summary>
    public string Room { get; set; } = "";
    /// <summary>来源对象/事件（只作记录）。</summary>
    public string ObjectName { get; set; } = "";
    public string EventName { get; set; } = "";
    /// <summary>编辑前的原版文本（只作记录，方便恢复/对比）。</summary>
    public string Original { get; set; } = "";
}

public sealed class BattleModInfo
{
    public string Id { get; set; } = "battle.demo";
    public string Name { get; set; } = "Neutraled Battle";
    public string Author { get; set; } = "doge-e";
    public string Version { get; set; } = "1.0.0";
    /// <summary>部署进哪个产物叶子：chapter1..5 / root。</summary>
    public string Leaf { get; set; } = "chapter1";
    public string Description { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public sealed class BattleDef
{
    /// <summary>绑定的地图房间名（由地图编辑器选中实体时自动填入）。</summary>
    public string Room { get; set; } = "";
    /// <summary>绑定的地图实例 ID（GameObject.InstanceID，全数据唯一）。</summary>
    public uint InstanceId { get; set; }
    /// <summary>绑定实例的对象名（换回地图时用于定位/显示）。</summary>
    public string ObjectName { get; set; } = "";
    /// <summary>绑定实例在地图上的坐标（生成触发条件时用）。</summary>
    public double InstanceX { get; set; } = -1;
    public double InstanceY { get; set; } = -1;
    public string Id { get; set; } = "battle1";
    public string Name { get; set; } = "Battle 1";
    /// <summary>encounter = 进入战斗界面；overworld = 在地图上打弹幕。</summary>
    public string Kind { get; set; } = "encounter";
    /// <summary>auto = 进入房间/半径自动开战；interact = 玩家面对绑定实体按 Z/Enter 才开战。</summary>
    public string TriggerMode { get; set; } = "auto";
    public bool Enabled { get; set; } = true;
    public string StartMessage { get; set; } = "";
    public int PreTurnDelay { get; set; } = 24;
    public int RoundLimit { get; set; }
    public bool LoopLastTurn { get; set; } = true;
    public int StartingTension { get; set; }
    public List<EnemyDef> Enemies { get; set; } = new();
    public List<TurnDef> Turns { get; set; } = new();
    public TriggerDef Trigger { get; set; } = new();
    public EffectDef Victory { get; set; } = new();
    public EffectDef Defeat { get; set; } = new();
    public OverworldDef Overworld { get; set; } = new();
}

public sealed class TriggerDef
{
    public string Room { get; set; } = "";
    public int RequiredFlag { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Radius { get; set; }
    public bool Once { get; set; } = true;
    /// <summary>跨重启仍只触发一次（用 ntl_mod_data 持久化；勾选后重启游戏也不会重复触发）。</summary>
    public bool PersistOnce { get; set; }
}

public sealed class EnemyDef
{
    public string Object { get; set; } = "obj_bloxer_enemy";
    public string Name { get; set; } = "Enemy";
    public string Sprite { get; set; } = "";
    public double Hp { get; set; } = 60;
    public double At { get; set; } = 6;
    public double Df { get; set; } = 0;
    public double Exp { get; set; } = 0;
    public double Gold { get; set; } = 0;
    public double SparePoint { get; set; } = 0;
    public double MercyMod { get; set; } = 0;
    public double MercyMax { get; set; } = 100;
    public double Status { get; set; } = 0;
    public string Comment { get; set; } = "";
    public string ActMessage { get; set; } = "";
    public List<string> Acts { get; set; } = new() { "Check", "Talk" };
    /// <summary>出现位置；-1 = 自动（右侧 540, 按槽位纵向排列）。</summary>
    public double X { get; set; } = -1;
    public double Y { get; set; } = -1;
}

public sealed class TurnDef
{
    public string Message { get; set; } = "";
    public int Duration { get; set; } = 240;
    public List<BarrageDef> Barrages { get; set; } = new();
}

public sealed class BarrageDef
{
    /// <summary>obj_dbulletcontroller 的 type 编号（决定弹幕形状）。</summary>
    public int Type { get; set; }
    /// <summary>同一回合创建几个控制器（叠加出更多弹幕）。</summary>
    public int Count { get; set; } = 1;
    /// <summary>伤害；0 = 用敌人 AT×5。</summary>
    public double Damage { get; set; }
    public double Ratio { get; set; } = 1.0;
    public int TimerMax { get; set; } = 12;
    public int Joker { get; set; }
    public int Side { get; set; } = 1;
    public double GrazePoints { get; set; } = 1;
    public double TimePoints { get; set; } = 1;
    /// <summary>随机候选 type（非空时每回合随机挑一个，实现「随机弹幕」）。</summary>
    public List<int> RandomTypes { get; set; } = new();
    /// <summary>ratio 随机浮动（±该值）。</summary>
    public double RandomRatio { get; set; }
    public int Delay { get; set; }
}

public sealed class EffectDef
{
    public List<int> SetFlags { get; set; } = new();
    public List<int> ClearFlags { get; set; } = new();
    public int HealPercent { get; set; }
    public string GotoRoom { get; set; } = "";
    public string StartBattle { get; set; } = "";
    public string Message { get; set; } = "";
}

public sealed class OverworldDef
{
    /// <summary>danmaku = 原地躲弹幕；encounter = 碰到就进入战斗界面。</summary>
    public string Mode { get; set; } = "danmaku";
    public int Duration { get; set; } = 300;
    public int ContactDamage { get; set; } = 4;
    public int EnemyRadius { get; set; } = 20;
    public string Sprite { get; set; } = "";
    public List<PatternDef> Patterns { get; set; } = new();
}

public sealed class PatternDef
{
    /// <summary>rain（从上方落）/ ring（环形）/ aim（朝玩家）/ wall（横向墙）。</summary>
    public string Kind { get; set; } = "rain";
    public int Interval { get; set; } = 20;
    public int Count { get; set; } = 3;
    public double Speed { get; set; } = 3.0;
    public double Spread { get; set; } = 24;
    public double Damage { get; set; } = 4;
    public int Life { get; set; } = 180;
    public double Angle { get; set; } = 90;
    public double RandomAngle { get; set; }
    public int RandomCount { get; set; }
    public double RandomSpeed { get; set; }
}

/// <summary>纯对话实体：绑定地图实例，玩家靠近按 Z（原版交互键）时由生成的 mod 播放多行文本。</summary>
public sealed class DialogueDef
{
    public string Room { get; set; } = "";
    public uint InstanceId { get; set; }
    public string ObjectName { get; set; } = "";
    public double InstanceX { get; set; } = -1;
    public double InstanceY { get; set; } = -1;
    public string Id { get; set; } = "dialogue1";
    public string Name { get; set; } = "对话 1";
    public bool Enabled { get; set; } = true;
    /// <summary>interact = 靠近后按 Z/Enter 触发；area = 走进触发半径自动播放。</summary>
    public string Mode { get; set; } = "interact";
    /// <summary>说话者（留空 = 不显示名字，纯旁白）。仅作配置记录，原版纯文本对话不画名字。</summary>
    public string Speaker { get; set; } = "";
    /// <summary>按顺序显示的文本行；"%"/ 由运行时自动加在原版 obj_writer 需要的行尾。</summary>
    public List<string> Lines { get; set; } = new();
    public TriggerDef Trigger { get; set; } = new();
    public EffectDef End { get; set; } = new();
}

/// <summary>简单剧情：进入指定区域（area）或交互指定实体（interact）后播放一串步骤，结束后执行 effects。</summary>
public sealed class CutsceneDef
{
    public string Room { get; set; } = "";
    public uint InstanceId { get; set; }
    public string ObjectName { get; set; } = "";
    public double InstanceX { get; set; } = -1;
    public double InstanceY { get; set; } = -1;
    public string Id { get; set; } = "cutscene1";
    public string Name { get; set; } = "剧情 1";
    public bool Enabled { get; set; } = true;
    /// <summary>interact = 靠近后按 Z/Enter 触发；area = 走进触发半径自动触发。</summary>
    public string Mode { get; set; } = "interact";
    public TriggerDef Trigger { get; set; } = new();
    public List<CutsceneStep> Steps { get; set; } = new();
    public EffectDef End { get; set; } = new();
}

/// <summary>
/// 剧情步骤。actor 目前支持 player（玩家）与 trigger（本段剧情绑定的地图实体）。
/// kind 语义：wait 等 N 帧；move 在 N 帧内从当前位置移到 (x,y)；face 立即转向；action 换精灵/帧后停留 N 帧；
/// dialogue 播放多行文本（结束后继续）；flag 立即设/清 flag；message 只写日志并停留 N 帧。
/// </summary>
public sealed class CutsceneStep
{
    public string Kind { get; set; } = "wait";
    public string Actor { get; set; } = "player";
    public int Frames { get; set; } = 30;
    public double X { get; set; }
    public double Y { get; set; }
    public string Direction { get; set; } = "right";
    public string Sprite { get; set; } = "";
    public int ImageIndex { get; set; }
    /// <summary>-1 = 不修改 image_speed。</summary>
    public double ImageSpeed { get; set; } = -1;
    public List<string> Lines { get; set; } = new();
    public List<int> SetFlags { get; set; } = new();
    public List<int> ClearFlags { get; set; } = new();
    public string Message { get; set; } = "";
    /// <summary>sound 步骤：要播放的音效资源名（asset_get_index 可用；也接受 snd_play 包装）。</summary>
    public string Sound { get; set; } = "";
    /// <summary>sound 步骤的音量/增益（0-1，默认 1）。shake 步骤复用 Frames=持续帧数、X=强度。</summary>
    public double Gain { get; set; } = 1;

    // ---------------- 原版代码引用（kind = event / script）
    /// <summary>event 步骤：要在哪个对象的实例上执行（对象名；留空 = 在触发点/玩家上执行）。</summary>
    public string EvObject { get; set; } = "";
    /// <summary>原版脚本名（如 gml_Object_obj_x_Step_0）；运行时 asset_get_index + script_execute。</summary>
    public string Script { get; set; } = "";
    /// <summary>事件类型 / 子类型（仅作记录与选择器回显；真正执行靠 Script 名）。</summary>
    public int EvType { get; set; } = -1;
    public int EvSub { get; set; } = -1;
    /// <summary>给用户看的说明（如「obj_x 的 Step」）。</summary>
    public string Note { get; set; } = "";
}

/// <summary>游戏内 Mod 设置项（注册到 Neutraled「Mod 设置」面板，开关实时影响生成 mod 的功能）。</summary>
public sealed class SettingDef
{
    public string Id { get; set; } = "setting1";
    public string Label { get; set; } = "设置 1";
    /// <summary>bool | choice | int。</summary>
    public string Type { get; set; } = "bool";
    /// <summary>默认值：bool 用 1/0；choice 用选项的 value；int 用数字字符串。</summary>
    public string Value { get; set; } = "1";
    public List<SettingOption> Options { get; set; } = new();
    public string Description { get; set; } = "";
    /// <summary>实时功能绑定：none / battlesEnabled / overworldEnabled / dialoguesEnabled / cutscenesEnabled / barrageDamagePercent。</summary>
    public string Feature { get; set; } = "none";
    public int Min { get; set; }
    public int Max { get; set; } = 100;
    public int Step { get; set; } = 10;
}

public sealed class SettingOption
{
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
}

// ------------------------------------------------------------------ 读 / 写 / 校验

public sealed class ValidationIssue
{
    public bool IsError;
    public string Message = "";
    public override string ToString() => (IsError ? "[错误] " : "[警告] ") + Message;
}

public static class BattleJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static readonly JsonSerializerOptions Compact = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Serialize<T>(T value, bool compact = false) =>
        JsonSerializer.Serialize(value, compact ? Compact : Options);

    public static T? Deserialize<T>(string text) => JsonSerializer.Deserialize<T>(text, Options);
}

public sealed class BattleProject
{
    /// <summary>sidecar 文件名：与 data.win 同目录，<win 文件名>.ntlbattle.json。</summary>
    public static string SidecarFor(string winPath) => winPath + ".ntlbattle.json";

    /// <summary>
    /// 编辑用的 sidecar 路径。live data.win 仍放同目录；原版基线（backup/…）不放存档备份目录，
    /// 统一收到 &lt;gameRoot&gt;\Neutraled\editor\baseline_&lt;章节&gt;.ntlbattle.json，
    /// 这样「打开原版 data 直接编辑对话/战斗/剧情」不会往 backup 里写任何东西，也不会丢。
    /// </summary>
    public static string SidecarForEdit(string winPath, string? gameRoot)
    {
        if (FileRole.Classify(winPath, gameRoot) != FileKind.Baseline) return SidecarFor(winPath);
        var root = gameRoot ?? FileRole.FindGameRoot();
        if (string.IsNullOrWhiteSpace(root)) return SidecarFor(winPath);
        var leaf = ChapterLeaf(winPath);
        var dir = Path.Combine(root, "Neutraled", "editor");
        return Path.Combine(dir, "baseline_" + leaf + ".ntlbattle.json");
    }

    /// <summary>从 data.win 路径推断部署叶子：…\chapter3_windows\data.win → chapter3；其余 → root。</summary>
    public static string ChapterLeaf(string winPath)
    {
        var m = System.Text.RegularExpressions.Regex.Match(winPath ?? "", @"chapter(\d+)_windows",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success ? "chapter" + m.Groups[1].Value : "root";
    }

    public TextOverrideDef? TextOverrideFor(string key) =>
        File.TextOverrides.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.Ordinal));

    public static BattleProject LoadOrNew(string sidecarPath)
    {
        if (System.IO.File.Exists(sidecarPath))
        {
            try { return Load(sidecarPath); }
            catch (Exception ex) { Console.Error.WriteLine("[battle] sidecar 解析失败，忽略: " + ex.Message); }
        }
        return new BattleProject { SourcePath = Path.GetFullPath(sidecarPath) };
    }

    public BattleDef? ForInstance(string room, uint instanceId) =>
        File.Battles.FirstOrDefault(b => string.Equals(b.Room, room, StringComparison.OrdinalIgnoreCase) && b.InstanceId == instanceId);

    public BattleDef GetOrCreateForInstance(string room, uint instanceId, string objectName)
    {
        var b = ForInstance(room, instanceId);
        if (b != null) return b;
        b = new BattleDef
        {
            Id = "battle_" + File.Battles.Count.ToString() + "_" + instanceId.ToString(),
            Name = objectName + " 战斗",
            Room = room,
            InstanceId = instanceId,
            ObjectName = objectName,
        };
        b.Trigger.Room = room;
        File.Battles.Add(b);
        return b;
    }

    public bool RemoveBattle(string room, uint instanceId)
    {
        var b = ForInstance(room, instanceId);
        return b != null && File.Battles.Remove(b);
    }

    public DialogueDef? DialogueForInstance(string room, uint instanceId) =>
        File.Dialogues.FirstOrDefault(d => string.Equals(d.Room, room, StringComparison.OrdinalIgnoreCase) && d.InstanceId == instanceId);

    public DialogueDef? DialogueById(string id) =>
        File.Dialogues.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));

    public DialogueDef GetOrCreateDialogueForInstance(string room, uint instanceId, string objectName)
    {
        var d = DialogueForInstance(room, instanceId);
        if (d != null) return d;
        d = new DialogueDef
        {
            Id = "dialogue_" + File.Dialogues.Count.ToString() + "_" + instanceId.ToString(),
            Name = objectName + " 对话",
            Room = room,
            InstanceId = instanceId,
            ObjectName = objectName,
        };
        d.Trigger.Room = room;
        d.Trigger.Radius = 64;
        if (d.Lines.Count == 0) d.Lines.Add("");
        File.Dialogues.Add(d);
        return d;
    }

    public bool RemoveDialogue(string room, uint instanceId)
    {
        var d = DialogueForInstance(room, instanceId);
        return d != null && File.Dialogues.Remove(d);
    }

    // ---------------- 简单剧情

    public CutsceneDef? CutsceneForInstance(string room, uint instanceId) =>
        File.Cutscenes.FirstOrDefault(c => string.Equals(c.Room, room, StringComparison.OrdinalIgnoreCase) && c.InstanceId == instanceId);

    public CutsceneDef? CutsceneById(string id) =>
        File.Cutscenes.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));

    public CutsceneDef GetOrCreateCutsceneForInstance(string room, uint instanceId, string objectName)
    {
        var c = CutsceneForInstance(room, instanceId);
        if (c != null) return c;
        c = new CutsceneDef
        {
            Id = "cutscene_" + File.Cutscenes.Count.ToString() + "_" + instanceId.ToString(),
            Name = objectName + " 剧情",
            Room = room,
            InstanceId = instanceId,
            ObjectName = objectName,
            Mode = "interact",
        };
        c.Trigger.Room = room;
        c.Trigger.Radius = 64;
        c.Steps.Add(new CutsceneStep { Kind = "face", Actor = "player", Direction = "down" });
        // 默认不再给「空文本的 dialogue」：那样打开新剧情不改任何东西、直接点保存就会被必填校验拦下
        // （红字「对话步骤至少要写一行文本」），用户会以为编辑器坏了。给一条可编辑的等待步骤更直观。
        c.Steps.Add(new CutsceneStep { Kind = "wait", Frames = 30, Note = "新剧情：按需插入对话 / 动作步骤" });
        File.Cutscenes.Add(c);
        return c;
    }

    public bool RemoveCutscene(string room, uint instanceId)
    {
        var c = CutsceneForInstance(room, instanceId);
        return c != null && File.Cutscenes.Remove(c);
    }

    public SettingDef? SettingById(string id) =>
        File.Settings.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

    public bool RemoveSetting(string id)
    {
        var s = SettingById(id);
        return s != null && File.Settings.Remove(s);
    }

    /// <summary>
    /// 实体在地图上被移动后，按 InstanceID 在整份 data.win 里找回绑定实例的最新坐标，
    /// 刷新 InstanceX/InstanceY 与 Trigger.X/Y（打开旧 sidecar 再保存即可同步；找不到实例时保持原值）。
    /// 返回发生更新的条目数。
    /// </summary>
    public int RefreshBoundCoordinates(GameData? data)
    {
        if (data == null) return 0;
        var positions = new Dictionary<uint, (string Room, string Object, double X, double Y)>();
        foreach (var room in data.Rooms)
        {
            if (room == null) continue;
            var roomName = GameData.Name(room.Name);
            foreach (var (_, inst) in RoomEdit.AllInstances(room))
            {
                if (inst.InstanceID == 0 || positions.ContainsKey(inst.InstanceID)) continue;
                positions[inst.InstanceID] = (roomName, GameData.Name(inst.ObjectDefinition?.Name), inst.X, inst.Y);
            }
        }
        int changed = 0;
        void Sync(uint id, string room, string obj, TriggerDef trigger, double currentX, double currentY, Action<double, double> apply)
        {
            if (string.IsNullOrWhiteSpace(trigger.Room)) trigger.Room = room;
            if (id == 0 || !positions.TryGetValue(id, out var pos)) return;
            if (string.Equals(trigger.Room, pos.Room, StringComparison.OrdinalIgnoreCase) && currentX == pos.X && currentY == pos.Y) return;
            apply(pos.X, pos.Y);
            if (!string.IsNullOrWhiteSpace(obj)) { trigger.X = pos.X; trigger.Y = pos.Y; }
            changed++;
        }
        foreach (var b in File.Battles)
            Sync(b.InstanceId, b.Room, b.ObjectName, b.Trigger, b.InstanceX, b.InstanceY, (x, y) => { b.InstanceX = x; b.InstanceY = y; });
        foreach (var d in File.Dialogues)
            Sync(d.InstanceId, d.Room, d.ObjectName, d.Trigger, d.InstanceX, d.InstanceY, (x, y) => { d.InstanceX = x; d.InstanceY = y; });
        foreach (var c in File.Cutscenes)
            Sync(c.InstanceId, c.Room, c.ObjectName, c.Trigger, c.InstanceX, c.InstanceY, (x, y) => { c.InstanceX = x; c.InstanceY = y; });
        return changed;
    }

    public BattlesFile File { get; set; } = new();
    public string SourcePath { get; set; } = "";

    public static BattleProject Load(string path)
    {
        var text = System.IO.File.ReadAllText(path);
        var f = BattleJson.Deserialize<BattlesFile>(text) ?? throw new InvalidDataException("battles.json 解析失败");
        var p = new BattleProject { File = f, SourcePath = Path.GetFullPath(path) };
        // 老 sidecar 可能带着「房间已改名、但触发还指向旧名」的坏引用（修好同步逻辑之前存的）→ 开档时补一次。
        p.MigrateRenamedRoomReferences();
        return p;
    }

    /// <summary>
    /// 把 roomPatches 里「已改名房间」的旧名引用迁移到新名。读盘后跑一次，历史配置也能正常生成 mod；
    /// 返回改动的引用数（0 = 没有需要迁移的）。
    /// </summary>
    public int MigrateRenamedRoomReferences()
    {
        int n = 0;
        foreach (var p in File.RoomPatches)
        {
            if (string.IsNullOrWhiteSpace(p.Name) || string.IsNullOrWhiteSpace(p.Rename)) continue;
            if (string.Equals(p.Name, p.Rename, StringComparison.Ordinal)) continue;
            n += File.RenameRoomReferences(p.Name, p.Rename);
        }
        return n;
    }

    /// <summary>
    /// sidecar 写盘。与 EditorDocument.Save 同一道基线闸门：backup 目录下的
    /// &lt;win&gt;.ntlbattle.json 不创建/不覆盖、不生成 .bak；live 正常写。
    /// </summary>
    public SaveResult Save(string? path = null, bool allowBaselineWrite = false, string? gameRoot = null)
    {
        path ??= SourcePath;
        if (string.IsNullOrWhiteSpace(path)) return SaveResult.Refused(path ?? "", "没有保存路径");
        var kind = FileRole.Classify(path, gameRoot ?? FileRole.FindGameRoot());
        if (!allowBaselineWrite && kind == FileKind.Baseline)
            return SaveResult.Refused(path, FileRole.BaselineError(path));
        Normalize();
        var text = BattleJson.Serialize(File);
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        if (System.IO.File.Exists(path)) System.IO.File.Copy(path, path + ".bak", true);
        System.IO.File.WriteAllText(path, text);
        SourcePath = Path.GetFullPath(path);
        return SaveResult.Success(path);
    }

    public void Normalize()
    {
        foreach (var b in File.Battles)
        {
            b.Kind = string.IsNullOrWhiteSpace(b.Kind) ? "encounter" : b.Kind.Trim().ToLowerInvariant();
            b.TriggerMode = string.IsNullOrWhiteSpace(b.TriggerMode) ? "auto" : b.TriggerMode.Trim().ToLowerInvariant();
            if (b.TriggerMode != "auto" && b.TriggerMode != "interact") b.TriggerMode = "auto";
            b.Trigger ??= new TriggerDef();
            b.Victory ??= new EffectDef();
            b.Defeat ??= new EffectDef();
            b.Overworld ??= new OverworldDef();
            if (b.Turns.Count == 0) b.Turns.Add(new TurnDef());
            foreach (var t in b.Turns)
            {
                if (t.Duration <= 0) t.Duration = 240;
                foreach (var g in t.Barrages)
                {
                    if (g.Count <= 0) g.Count = 1;
                    if (g.TimerMax <= 0) g.TimerMax = 12;
                    if (g.Ratio <= 0) g.Ratio = 1.0;
                    if (g.Damage < 0) g.Damage = 0;
                }
            }
            if (b.PreTurnDelay <= 0) b.PreTurnDelay = 24;
            if (b.Overworld.Duration <= 0) b.Overworld.Duration = 300;
        }

        for (int i = 0; i < File.Cutscenes.Count; i++)
        {
            var c = File.Cutscenes[i];
            if (string.IsNullOrWhiteSpace(c.Id)) c.Id = "cutscene_" + i.ToString();
            if (string.IsNullOrWhiteSpace(c.Name)) c.Name = c.Id;
            c.Mode = string.IsNullOrWhiteSpace(c.Mode) ? "interact" : c.Mode.Trim().ToLowerInvariant();
            if (c.Mode != "interact" && c.Mode != "area") c.Mode = "interact";
            c.Trigger ??= new TriggerDef();
            c.End ??= new EffectDef();
            c.Steps ??= new List<CutsceneStep>();
            if (string.IsNullOrWhiteSpace(c.Trigger.Room)) c.Trigger.Room = c.Room;
            if (c.Trigger.Radius <= 0) c.Trigger.Radius = 64;
            if (c.Trigger.X == 0 && c.Trigger.Y == 0 && c.InstanceX != 0 && c.InstanceY != 0)
            {
                c.Trigger.X = c.InstanceX;
                c.Trigger.Y = c.InstanceY;
            }
            foreach (var st in c.Steps)
            {
                st.Kind = string.IsNullOrWhiteSpace(st.Kind) ? "wait" : st.Kind.Trim().ToLowerInvariant();
                st.Actor = string.IsNullOrWhiteSpace(st.Actor) ? "player" : st.Actor.Trim().ToLowerInvariant();
                if (st.Actor != "player" && st.Actor != "trigger") st.Actor = "player";
                st.Direction = string.IsNullOrWhiteSpace(st.Direction) ? "right" : st.Direction.Trim().ToLowerInvariant();
                if (st.Direction != "left" && st.Direction != "right" && st.Direction != "up" && st.Direction != "down")
                    st.Direction = "right";
                if (st.Frames < 0) st.Frames = 0;
                if (st.Kind == "wait" || st.Kind == "move" || st.Kind == "action" || st.Kind == "message")
                {
                    if (st.Frames <= 0) st.Frames = 30;
                }
                st.Lines ??= new List<string>();
                if (st.Kind == "dialogue" && st.Lines.Count == 0) st.Lines.Add("");
                st.SetFlags ??= new List<int>();
                st.ClearFlags ??= new List<int>();
                st.Sprite ??= "";
                st.Message ??= "";
                st.EvObject = (st.EvObject ?? "").Trim();
                st.Script = (st.Script ?? "").Trim();
                st.Note = (st.Note ?? "").Trim();
                if (st.Kind == "script") st.EvObject = "";
                // 原版代码引用步骤：Frames 是「执行后额外等待的帧数」，0 = 立即继续。
                if (st.Kind == "event" || st.Kind == "script")
                {
                    if (st.Frames < 0) st.Frames = 0;
                }
            }
            if (c.Steps.Count == 0)
                c.Steps.Add(new CutsceneStep { Kind = "wait", Frames = 30, Note = "空剧情：按需插入对话 / 动作步骤" });
        }

        for (int i = 0; i < File.Dialogues.Count; i++)
        {
            var d = File.Dialogues[i];
            d.Trigger ??= new TriggerDef();
            d.End ??= new EffectDef();
            if (string.IsNullOrWhiteSpace(d.Id)) d.Id = "dialogue_" + i.ToString();
            if (string.IsNullOrWhiteSpace(d.Name)) d.Name = d.Id;
            d.Mode = string.IsNullOrWhiteSpace(d.Mode) ? "interact" : d.Mode.Trim().ToLowerInvariant();
            if (d.Mode != "interact" && d.Mode != "area") d.Mode = "interact";
            if (d.Lines == null) d.Lines = new List<string>();
            if (d.Lines.Count == 0) d.Lines.Add("");
            if (string.IsNullOrWhiteSpace(d.Trigger.Room)) d.Trigger.Room = d.Room;
            if (d.Trigger.Radius <= 0) d.Trigger.Radius = 64;
            if (d.Trigger.X == 0 && d.Trigger.Y == 0 && d.InstanceX != 0 && d.InstanceY != 0)
            {
                d.Trigger.X = d.InstanceX;
                d.Trigger.Y = d.InstanceY;
            }
        }

        foreach (var s in File.Settings)
        {
            if (string.IsNullOrWhiteSpace(s.Id)) s.Id = "setting" + File.Settings.IndexOf(s).ToString();
            if (string.IsNullOrWhiteSpace(s.Label)) s.Label = s.Id;
            s.Type = string.IsNullOrWhiteSpace(s.Type) ? "bool" : s.Type.Trim().ToLowerInvariant();
            if (s.Type != "bool" && s.Type != "choice" && s.Type != "int") s.Type = "bool";
            s.Feature = string.IsNullOrWhiteSpace(s.Feature) ? "none" : s.Feature.Trim().ToLowerInvariant();
            if (s.Options == null) s.Options = new List<SettingOption>();
            foreach (var o in s.Options)
            {
                if (string.IsNullOrWhiteSpace(o.Value)) o.Value = string.IsNullOrWhiteSpace(o.Label) ? "1" : o.Label;
                if (string.IsNullOrWhiteSpace(o.Label)) o.Label = o.Value;
            }
            if (s.Type == "bool") s.Value = (s.Value == "0" || s.Value == "false") ? "0" : "1";
            else if (s.Type == "choice")
            {
                if (s.Options.Count > 0 && !s.Options.Any(o => o.Value == s.Value)) s.Value = s.Options[0].Value;
            }
            else
            {
                if (!int.TryParse(s.Value, out var iv)) iv = s.Min;
                if (s.Max <= s.Min) s.Max = s.Min + 100;
                if (s.Step <= 0) s.Step = 10;
                s.Value = Math.Clamp(iv, s.Min, s.Max).ToString();
            }
            s.Description ??= "";
        }

        // 原版文本覆盖：去掉空 key/空文本，同一个 key 只保留最后一条（用户最后一次编辑生效）。
        File.TextOverrides ??= new List<TextOverrideDef>();
        var seenTextKeys = new HashSet<string>(StringComparer.Ordinal);
        for (int i = File.TextOverrides.Count - 1; i >= 0; i--)
        {
            var t = File.TextOverrides[i];
            t.Key = (t.Key ?? "").Trim();
            t.Text ??= "";
            t.Room = (t.Room ?? "").Trim();
            t.ObjectName = (t.ObjectName ?? "").Trim();
            t.EventName = (t.EventName ?? "").Trim();
            t.Original ??= "";
            if (t.Key.Length == 0 || t.Text.Length == 0 || !seenTextKeys.Add(t.Key))
                File.TextOverrides.RemoveAt(i);
        }

        // 地图/房间差分：名字 trim、null 列表补空、空 patch 丢掉（只留真改动）。
        File.RoomPatches ??= new List<RoomPatch>();
        for (int i = File.RoomPatches.Count - 1; i >= 0; i--)
        {
            var rp = File.RoomPatches[i];
            rp.Name = (rp.Name ?? "").Trim();
            rp.Rename = string.IsNullOrWhiteSpace(rp.Rename) ? null : rp.Rename.Trim();
            rp.InstancesAdd ??= new List<RoomInstancePatch>();
            rp.InstancesModify ??= new List<RoomInstancePatch>();
            rp.InstancesRemove ??= new List<RoomInstanceRef>();
            rp.TilesSet ??= new List<RoomTileSet>();
            rp.LayersAdd ??= new List<RoomLayer>();
            rp.LayersRemove ??= new List<string>();
            rp.LegacyTilesSet ??= new List<RoomLegacyTiles>();
            foreach (var ts in rp.TilesSet)
            {
                ts.Layer = string.IsNullOrWhiteSpace(ts.Layer) ? null : ts.Layer.Trim();
                ts.Cells ??= new List<RoomTileCell>();
            }
            rp.TilesSet.RemoveAll(ts => ts.Cells.Count == 0);
            foreach (var ip in rp.InstancesAdd.Concat(rp.InstancesModify))
            {
                ip.Object = string.IsNullOrWhiteSpace(ip.Object) ? null : ip.Object.Trim();
                ip.Layer = string.IsNullOrWhiteSpace(ip.Layer) ? null : ip.Layer.Trim();
            }
            for (int j = rp.LayersRemove.Count - 1; j >= 0; j--)
            {
                var ln = (rp.LayersRemove[j] ?? "").Trim();
                if (ln.Length == 0) rp.LayersRemove.RemoveAt(j);
                else rp.LayersRemove[j] = ln;
            }
            NormalizeLayers(rp.LayersAdd);
            for (int j = rp.LegacyTilesSet.Count - 1; j >= 0; j--)
            {
                var lt = rp.LegacyTilesSet[j];
                if (lt == null) { rp.LegacyTilesSet.RemoveAt(j); continue; }
                lt.Layer = string.IsNullOrWhiteSpace(lt.Layer) ? null : lt.Layer.Trim();
                lt.Tiles ??= new List<RoomTileDef>();
                NormalizeTileDefs(lt.Tiles);
            }
            if (rp.Name.Length == 0 || rp.IsEmpty) File.RoomPatches.RemoveAt(i);
        }
        File.NewRooms ??= new List<NewRoomDef>();
        for (int i = File.NewRooms.Count - 1; i >= 0; i--)
        {
            var nr = File.NewRooms[i];
            nr.Name = (nr.Name ?? "").Trim();
            if (nr.Width == 0) nr.Width = 640;
            if (nr.Height == 0) nr.Height = 480;
            // Speed 不补默认值：0 是合法房间速度（GameMaker 视作默认 30），builder 也原样保留；
            // 补成 30 会让「diff → 回灌 → 再 diff」在新房间上产生假差异（回灌后 30 vs 编辑态 0）。
            nr.Layers ??= new List<RoomLayer>();
            NormalizeLayers(nr.Layers);
            if (nr.Name.Length == 0) File.NewRooms.RemoveAt(i);
        }
    }

    /// <summary>图层规格归一：名字/类型/图块集 trim，列表补空，空名/无图源的瓦片定义丢掉。</summary>
    private static void NormalizeLayers(List<RoomLayer>? layers)
    {
        if (layers == null) return;
        for (int i = layers.Count - 1; i >= 0; i--)
        {
            RoomLayer? l = layers[i];
            if (l == null) { layers.RemoveAt(i); continue; }
            l.Name = (l.Name ?? "").Trim();
            l.Type = (l.Type ?? "").Trim();
            if (l.Type.Length == 0) l.Type = "instances";
            l.Tileset = string.IsNullOrWhiteSpace(l.Tileset) ? null : l.Tileset.Trim();
            l.Instances ??= new List<RoomInstancePatch>();
            l.Tiles ??= new List<RoomTileCell>();
            l.LegacyTiles ??= new List<RoomTileDef>();
            foreach (var ip in l.Instances)
            {
                ip.Object = string.IsNullOrWhiteSpace(ip.Object) ? null : ip.Object.Trim();
                ip.Layer = string.IsNullOrWhiteSpace(ip.Layer) ? null : ip.Layer.Trim();
            }
            NormalizeTileDefs(l.LegacyTiles);
            if (l.Name.Length == 0) layers.RemoveAt(i);
        }
    }

    /// <summary>旧式瓦片定义归一（Width/Height 默认 16；没有 sprite/background 的定义无法落地，丢掉）。</summary>
    private static void NormalizeTileDefs(List<RoomTileDef>? defs)
    {
        if (defs == null) return;
        for (int i = defs.Count - 1; i >= 0; i--)
        {
            RoomTileDef? d = defs[i];
            if (d == null) { defs.RemoveAt(i); continue; }
            d.Sprite = string.IsNullOrWhiteSpace(d.Sprite) ? null : d.Sprite.Trim();
            d.Background = string.IsNullOrWhiteSpace(d.Background) ? null : d.Background.Trim();
            if (d.Width == 0) d.Width = 16;
            if (d.Height == 0) d.Height = 16;
            if (d.Sprite == null && d.Background == null) defs.RemoveAt(i);
        }
    }

    /// <summary>
    /// 校验编辑配置。assets = 当前打开的游戏数据（查对象/房间是否存在）；
    /// baseline = 差分基线（原版 data.win，可选）：房间补丁的目标房间与新增房间的「是否已有」都应对它判断，
    /// 否则新增房间在内存里本来就存在，会被误判成「已有同名房间」而永远生成不了 mod。
    /// </summary>
    public List<ValidationIssue> Validate(GameData? assets = null, GameData? baseline = null)
    {
        var list = new List<ValidationIssue>();
        void Err(string m) => list.Add(new ValidationIssue { IsError = true, Message = m });
        void Warn(string m) => list.Add(new ValidationIssue { IsError = false, Message = m });

        if (File.Version != 1) Warn($"version={File.Version}（当前生成器只写 1）");
        var m = File.Mod;
        if (string.IsNullOrWhiteSpace(m.Id)) Err("mod.id 不能为空");
        else if (!System.Text.RegularExpressions.Regex.IsMatch(m.Id, "^[A-Za-z0-9_.-]{2,64}$"))
            Err("mod.id 只能含字母/数字/下划线/点/短横线（2..64）");
        if (string.IsNullOrWhiteSpace(m.Leaf)) Err("mod.leaf 不能为空");
        else if (!System.Text.RegularExpressions.Regex.IsMatch(m.Leaf, "^(root|chapter[1-7])$"))
            Err("mod.leaf 必须是 root 或 chapter1..chapter7");
        if (string.IsNullOrWhiteSpace(m.Author)) Warn("mod.author 为空，会生成到 mods/<id>/Neutraled/ 下");

        if (File.Battles.Count == 0 && File.Dialogues.Count == 0 && File.Cutscenes.Count == 0
            && File.TextOverrides.Count == 0 && File.RoomPatches.Count == 0 && File.NewRooms.Count == 0)
            Err("至少需要一场战斗、一段对话、一段剧情、一条原版文本改写或一处地图/房间改动（否则生成的 mod 没有任何内容）");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in File.Battles)
        {
            if (string.IsNullOrWhiteSpace(b.Id)) { Err("战斗 id 不能为空"); continue; }
            if (!ids.Add(b.Id)) Err($"战斗 id 重复: {b.Id}");
            if (b.Kind != "encounter" && b.Kind != "overworld") Err($"{b.Id}: kind 必须是 encounter 或 overworld");
            if (b.TriggerMode != "auto" && b.TriggerMode != "interact") Err($"{b.Id}: 触发方式必须是 auto（进入即触发）或 interact（按 Z 交互触发）");
            if (string.IsNullOrWhiteSpace(b.Room)) Warn($"{b.Id}: 没有绑定地图实体（只能从控制台触发）");
            if (b.InstanceId == 0 && string.IsNullOrWhiteSpace(b.Room)) Warn($"{b.Id}: 没有绑定实例 ID");
            if (b.Enemies.Count == 0 && b.Kind == "encounter") Err($"{b.Id}: encounter 至少需要一个敌人");
            if (b.Enemies.Count > 3) Err($"{b.Id}: 最多 3 个敌人（原版战斗只有 3 个怪物槽）");
            foreach (var e in b.Enemies)
            {
                if (string.IsNullOrWhiteSpace(e.Object)) Err($"{b.Id}: 敌人 object 不能为空");
                else if (!e.Object.StartsWith("ntlb_", StringComparison.OrdinalIgnoreCase) &&
                         assets != null && !assets.GameObjects.Any(o => string.Equals(GameData.Name(o.Name), e.Object, StringComparison.OrdinalIgnoreCase)))
                    Err($"{b.Id}: data.win 里没有对象 {e.Object}");
                // ntlb_ 开头 = 生成器随 mod 建的自定义敌人对象（任何章节都有），不按 data.win 校验。
                if (e.Hp < 1) Err($"{b.Id}/{e.Name}: hp 必须 >= 1");
                if (e.Acts.Count > 6) Err($"{b.Id}/{e.Name}: acts 最多 6 条");
            }
            if (b.Kind == "encounter" && b.Turns.Count == 0) Warn($"{b.Id}: 没有回合配置，敌人不会发射弹幕");
            foreach (var t in b.Turns)
            {
                foreach (var g in t.Barrages)
                {
                    if (g.Type < 0) Err($"{b.Id}: 弹幕 type 不能为负");
                    if (g.RandomTypes.Count > 0 && g.RandomTypes.Any(x => x < 0)) Err($"{b.Id}: 随机 type 不能为负");
                }
            }
            var trig = b.Trigger;
            if (string.IsNullOrWhiteSpace(trig.Room) && trig.RequiredFlag == 0 && trig.Radius <= 0)
                Warn($"{b.Id}: 没有触发条件（room/flag/radius 都为空），只能从控制台 battle start 或 battle ow 触发");
            if (!string.IsNullOrWhiteSpace(trig.Room) && assets != null &&
                !assets.Rooms.Any(r => string.Equals(GameData.Name(r.Name), trig.Room, StringComparison.OrdinalIgnoreCase)))
                Err($"{b.Id}: data.win 里没有房间 {trig.Room}");
            if (b.Kind == "overworld" && b.Overworld.Patterns.Count == 0)
                Warn($"{b.Id}: overworld 战斗没有弹幕模式，只有接触/计时判定");
        }

        // ---------------- 纯对话
        var dlgIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in File.Dialogues)
        {
            if (string.IsNullOrWhiteSpace(d.Id)) { Err("对话 id 不能为空"); continue; }
            if (!dlgIds.Add(d.Id)) Err($"对话 id 重复: {d.Id}");
            if (d.Mode != "interact" && d.Mode != "area") Err($"{d.Id}: 触发方式必须是 interact 或 area");
            if (string.IsNullOrWhiteSpace(d.Room))
                Warn($"{d.Id}: 没有绑定地图实体（只能从控制台 dialogue start 触发）");
            if (d.Lines == null || d.Lines.Count == 0) Warn($"{d.Id}: 没有任何文本行，显示为空");
            else if (d.Lines.Any(l => l != null && l.Contains('%')))
                Warn($"{d.Id}: 文本里的 % 是原版对话翻页符，运行时会删掉");
            var dt = d.Trigger ?? new TriggerDef();
            if (!string.IsNullOrWhiteSpace(dt.Room) && assets != null &&
                !assets.Rooms.Any(r => string.Equals(GameData.Name(r.Name), dt.Room, StringComparison.OrdinalIgnoreCase)))
                Err($"{d.Id}: data.win 里没有房间 {dt.Room}");
            if (string.IsNullOrWhiteSpace(dt.Room) && dt.RequiredFlag == 0)
                Warn($"{d.Id}: 没有触发条件（room/flag 都为空），只能从控制台 dialogue start 触发");
        }

        // ---------------- 简单剧情
        var cutIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in File.Cutscenes)
        {
            if (string.IsNullOrWhiteSpace(c.Id)) { Err("剧情 id 不能为空"); continue; }
            if (!cutIds.Add(c.Id)) Err($"剧情 id 重复: {c.Id}");
            if (!System.Text.RegularExpressions.Regex.IsMatch(c.Id, "^[A-Za-z0-9_.-]{1,64}$"))
                Err($"剧情 id 只能含字母/数字/下划线/点/短横线（1..64）: {c.Id}");
            if (c.Mode != "interact" && c.Mode != "area") Err($"{c.Id}: 触发方式必须是 interact 或 area");
            if (string.IsNullOrWhiteSpace(c.Room))
                Warn($"{c.Id}: 没有绑定地图实体（只能从控制台 cutscene start 触发）");
            var ct = c.Trigger ?? new TriggerDef();
            if (!string.IsNullOrWhiteSpace(ct.Room) && assets != null &&
                !assets.Rooms.Any(r => string.Equals(GameData.Name(r.Name), ct.Room, StringComparison.OrdinalIgnoreCase)))
                Err($"{c.Id}: data.win 里没有房间 {ct.Room}");
            if (string.IsNullOrWhiteSpace(ct.Room) && ct.RequiredFlag == 0 && ct.Radius <= 0)
                Warn($"{c.Id}: 没有触发条件（room/flag/radius 都为空），只能从控制台 cutscene start 触发");
            if (c.Steps == null || c.Steps.Count == 0) { Err($"{c.Id}: 剧情至少需要 1 个步骤"); continue; }
            for (int si = 0; si < c.Steps.Count; si++)
            {
                var st = c.Steps[si];
                var prefix = $"{c.Id} 第 {si + 1} 步";
                if (st.Kind != "wait" && st.Kind != "move" && st.Kind != "face" && st.Kind != "action"
                    && st.Kind != "dialogue" && st.Kind != "flag" && st.Kind != "message"
                    && st.Kind != "sound" && st.Kind != "shake" && st.Kind != "waitinput"
                    && st.Kind != "event" && st.Kind != "script")
                    Err($"{prefix}: 未知步骤类型 {st.Kind}（wait/move/face/action/dialogue/flag/message/sound/shake/waitinput/event/script）");
                if ((st.Kind == "event" || st.Kind == "script") && string.IsNullOrWhiteSpace(st.Script))
                    Err($"{prefix}: 原版代码步骤需要指定要执行的脚本（事件名，如 gml_Object_obj_x_Step_0）");
                if ((st.Kind == "event" || st.Kind == "script") && st.Frames > 3600)
                    Warn($"{prefix}: 原版代码步骤执行后等待 {st.Frames} 帧（>1 分钟），确认不是误填");
                if (st.Kind == "sound" && string.IsNullOrWhiteSpace(st.Sound))
                    Err($"{prefix}: 音效步骤需要填写音效资源名");
                if (st.Kind == "sound" && (st.Gain < 0 || st.Gain > 4))
                    Warn($"{prefix}: 音效增益 {st.Gain} 超出 0..4（会被原版音量系统截断）");
                if (st.Kind == "shake" && st.Frames <= 0)
                    Warn($"{prefix}: 震动步骤没填帧数（生成器按 20 帧兜底）");
                if (st.Kind == "dialogue" && (st.Lines == null || st.Lines.Count == 0 || st.Lines.All(string.IsNullOrWhiteSpace)))
                    Err($"{prefix}: 对话步骤至少要有一行文本");
                if (st.Kind == "action" && !string.IsNullOrWhiteSpace(st.Sprite) && assets != null &&
                    !assets.Sprites.Any(s => string.Equals(GameData.Name(s.Name), st.Sprite, StringComparison.OrdinalIgnoreCase)))
                    Warn($"{prefix}: data.win 里没有精灵 {st.Sprite}");
                if (st.Kind == "move" && st.Actor == "trigger" && string.IsNullOrWhiteSpace(c.ObjectName))
                    Warn($"{prefix}: 让触发实体移动，但剧情没有绑定实体对象名");
            }
        }

        // ---------------- Mod 设置项
        var setIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var featureUse = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in File.Settings)
        {
            if (string.IsNullOrWhiteSpace(s.Id)) { Err("设置项 id 不能为空"); continue; }
            if (!System.Text.RegularExpressions.Regex.IsMatch(s.Id, "^[A-Za-z0-9_.-]{1,48}$"))
                Err($"设置项 id 只能含字母/数字/下划线/点/短横线（1..48）: {s.Id}");
            if (!setIds.Add(s.Id)) Err($"设置项 id 重复: {s.Id}");
            var type = (s.Type ?? "").Trim().ToLowerInvariant();
            if (type != "bool" && type != "choice" && type != "int") Err($"{s.Id}: 类型必须是 bool/choice/int");
            if (type == "choice" && (s.Options == null || s.Options.Count < 2))
                Err($"{s.Id}: choice 类型至少需要 2 个选项");
            if (type == "int" && s.Max <= s.Min) Err($"{s.Id}: int 类型要求 最大值 > 最小值");
            var feature = (s.Feature ?? "none").Trim().ToLowerInvariant();
            if (feature != "none" && feature != "battlesenabled" && feature != "overworldenabled"
                && feature != "dialoguesenabled" && feature != "cutscenesenabled" && feature != "barragedamagepercent")
                Err($"{s.Id}: feature 必须是 none/battlesEnabled/overworldEnabled/dialoguesEnabled/cutscenesEnabled/barrageDamagePercent");
            else if (feature != "none")
            {
                if (featureUse.ContainsKey(feature)) Warn($"{s.Id}: 功能绑定 {feature} 已由 {featureUse[feature]} 占用（后者优先）");
                else featureUse[feature] = s.Id;
                if (feature == "barragedamagepercent" && type != "int")
                    Warn($"{s.Id}: barrageDamagePercent 建议用 int 类型（0..200）");
            }
            if (string.IsNullOrWhiteSpace(s.Description)) Warn($"{s.Id}: 没有说明文字（面板上第二行会空着）");
        }

        // ---------------- 原版文本覆盖
        var textKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in File.TextOverrides)
        {
            var key = (t.Key ?? "").Trim();
            if (key.Length == 0) { Err("原版文本改写：lang key 不能为空"); continue; }
            if (!textKeys.Add(key)) Err($"原版文本改写：key 重复 {key}（同一个 key 只能有一条覆盖）");
            if (string.IsNullOrEmpty(t.Text)) Err($"原版文本改写 {key}: 覆盖文本为空（要恢复原文请删掉这一条）");
            else if (t.Text.Contains('%')) Warn($"原版文本改写 {key}: 文本里的 % 是原版对话翻页符，会被运行时删掉");
            if (key.StartsWith("gml_", StringComparison.Ordinal) || key.Contains(' '))
                Warn($"原版文本改写 {key}: 不像 lang key（原版 key 一般形如 obj_xxx_Draw_0_gml_12_0）");
        }

        // ---------------- 地图/房间差分
        var patchedRooms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rp in File.RoomPatches)
        {
            var name = (rp.Name ?? "").Trim();
            if (name.Length == 0) { Err("地图改动：目标房间名不能为空"); continue; }
            if (!patchedRooms.Add(name)) Err($"地图改动：房间 {name} 出现多条补丁（合并成一条再保存）");
            if (rp.IsEmpty) Warn($"地图改动 {name}: 没有任何改动（保存时会被丢掉）");
            var patchLookup = baseline ?? assets;
            bool roomExists = patchLookup == null
                || patchLookup.Rooms.Any(x => string.Equals(GameData.Name(x.Name), name, StringComparison.OrdinalIgnoreCase));
            if (!roomExists && rp.RemoveFromOrder == true)
                Warn($"地图改动 {name}: data.win 里没有这个房间（从房间顺序摘除会跳过）");
            else if (!roomExists)
                Err($"地图改动 {name}: data.win 里没有这个房间（已改名或删除的房间暂不支持）");
            if (rp.Width != null && rp.Width == 0) Err($"地图改动 {name}: 宽度不能为 0");
            if (rp.Height != null && rp.Height == 0) Err($"地图改动 {name}: 高度不能为 0");
            var addIds = new HashSet<uint>();
            foreach (var ai in rp.InstancesAdd)
            {
                if (string.IsNullOrWhiteSpace(ai.Object)) Err($"地图改动 {name}: 新增实例缺少对象名");
                else if (assets != null && !assets.GameObjects.Any(o => string.Equals(GameData.Name(o.Name), ai.Object, StringComparison.OrdinalIgnoreCase)))
                    Err($"地图改动 {name}: data.win 里没有对象 {ai.Object}");
                if (ai.InstanceId != 0 && !addIds.Add(ai.InstanceId))
                    Err($"地图改动 {name}: 新增实例 ID 重复 {ai.InstanceId}");
            }
            foreach (var mi in rp.InstancesModify)
            {
                if (mi.InstanceId == 0) { Err($"地图改动 {name}: 修改实例必须带 InstanceId"); continue; }
                if (string.IsNullOrWhiteSpace(mi.Object) && mi.X == null && mi.Y == null && mi.Layer == null
                    && mi.ScaleX == null && mi.ScaleY == null && mi.Rotation == null && mi.Color == null
                    && mi.ImageIndex == null && mi.ImageSpeed == null && mi.Nonexistent == null)
                    Warn($"地图改动 {name}: 实例 {mi.InstanceId} 没写任何字段（不会产生改动）");
            }
            var removeIds = new HashSet<uint>();
            foreach (var ri in rp.InstancesRemove)
            {
                if (ri.InstanceId == 0) Err($"地图改动 {name}: 删除实例必须带 InstanceId");
                else if (!removeIds.Add(ri.InstanceId)) Warn($"地图改动 {name}: 删除实例 {ri.InstanceId} 重复");
            }
            foreach (var ts in rp.TilesSet)
            {
                if (ts.Cells.Count == 0) Warn($"地图改动 {name}: 有一组瓦片写入是空的");
            }
            foreach (var layerName in rp.LayersRemove)
                if (string.IsNullOrWhiteSpace(layerName))
                    Warn($"地图改动 {name}: 有一个空的图层删除条目（会被忽略）");
            var addLayerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var la in rp.LayersAdd)
            {
                var ln = (la.Name ?? "").Trim();
                if (ln.Length == 0) { Warn($"地图改动 {name}: 新增图层缺 name（会被忽略）"); continue; }
                if (!addLayerNames.Add(ln)) Warn($"地图改动 {name}: 新增图层 {ln} 名字重复");
                var lt = (la.Type ?? "").Trim().ToLowerInvariant();
                if (lt != "instances" && lt != "tiles" && lt != "assets")
                    Warn($"地图改动 {name}: 图层 {ln} 的类型 {la.Type} 不支持（只支持 instances/tiles/assets，部署时会跳过）");
                foreach (var ai in la.Instances)
                {
                    if (string.IsNullOrWhiteSpace(ai.Object)) Warn($"地图改动 {name}: 图层 {ln} 里有实例缺对象名");
                    else if (assets != null && !assets.GameObjects.Any(o => string.Equals(GameData.Name(o.Name), ai.Object, StringComparison.OrdinalIgnoreCase)))
                        Warn($"地图改动 {name}: 图层 {ln} 的实例对象 {ai.Object} 在 data.win 里不存在");
                }
            }
            foreach (var lt in rp.LegacyTilesSet)
                if (lt.Tiles.Count == 0)
                    Warn($"地图改动 {name}: 旧式瓦片替换 {lt.Layer ?? "(第一层)"} 是空列表（会清空该层）");
            if (!string.IsNullOrWhiteSpace(rp.Rename))
            {
                var renamed = rp.Rename.Trim();
                if (string.Equals(renamed, name, StringComparison.Ordinal))
                    Warn($"地图改动 {name}: rename 与 name 相同（不会改名）");
                var renameLookup = baseline ?? assets;
                if (renameLookup != null && renameLookup.Rooms.Any(x => string.Equals(GameData.Name(x.Name), renamed, StringComparison.OrdinalIgnoreCase)))
                    Warn($"地图改动 {name}: 改名目标 {renamed} 已存在于 data.win（部署时会跳过改名）");
                if (rp.RemoveFromOrder == true)
                    Warn($"地图改动 {name}: 同时改名 + 从房间顺序摘除（两者都会应用；摘除用旧名定位）");
            }
        }
        var newRoomNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var nr in File.NewRooms)
        {
            var name = (nr.Name ?? "").Trim();
            if (name.Length == 0) { Err("新增房间：名字不能为空"); continue; }
            if (!newRoomNames.Add(name)) Err($"新增房间：名字重复 {name}");
            if (patchedRooms.Contains(name)) Err($"新增房间 {name}: 同一名字又出现在地图改动里（新房间不能再打补丁）");
            var roomLookup = baseline ?? assets;
            if (roomLookup != null && roomLookup.Rooms.Any(x => string.Equals(GameData.Name(x.Name), name, StringComparison.OrdinalIgnoreCase)))
                Err($"新增房间 {name}: data.win 里已经有同名房间（改名，或改用地图改动）");
            if (nr.Width == 0 || nr.Height == 0) Err($"新增房间 {name}: 宽高不能为 0");
            var newLayerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var la in nr.Layers)
            {
                var ln = (la.Name ?? "").Trim();
                if (ln.Length == 0) { Warn($"新增房间 {name}: 有图层缺 name（会被忽略）"); continue; }
                if (!newLayerNames.Add(ln)) Warn($"新增房间 {name}: 图层 {ln} 名字重复");
                var lt = (la.Type ?? "").Trim().ToLowerInvariant();
                if (lt != "instances" && lt != "tiles" && lt != "assets")
                    Warn($"新增房间 {name}: 图层 {ln} 的类型 {la.Type} 不支持（只支持 instances/tiles/assets，部署时会跳过）");
                foreach (var ai in la.Instances)
                    if (string.IsNullOrWhiteSpace(ai.Object))
                        Warn($"新增房间 {name}: 图层 {ln} 里有实例缺对象名");
            }
        }
        return list;
    }
}
