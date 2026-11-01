
namespace NtlMapEdit;

/// <summary>
/// 运行时 GML 模板。每个条目 = 一个 gml/&lt;名&gt;.gml（文件名必须等于函数名，Neutraled 的 Injector 会把
/// function 外壳剥掉生成同名脚本）。对象事件模板另行生成，见 <see cref="Events"/>。
/// 占位符：@@ENEMY_OBJ@@ / @@BULLET_OBJ@@ / @@OW_OBJ@@ / @@PREFIX@@，由 BattleGenerator 替换。
/// </summary>
public static class RuntimeTemplates
{
    public static readonly Dictionary<string, string> Files = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>对象事件文件（相对 mod 叶子的路径 → GML 事件体）。</summary>
    public static readonly Dictionary<string, string> Events = new(StringComparer.OrdinalIgnoreCase);

    static RuntimeTemplates()
    {
    // ---------------------------------------------------------------- 通用小工具
    Files["ntlb_get"] = """
function ntlb_get(_s, _k)
{
    if (is_struct(_s) && variable_struct_exists(_s, _k)) return variable_struct_get(_s, _k);
    return undefined;
}
""";

    Files["ntlb_num"] = """
function ntlb_num(_s, _k, _d)
{
    var _v = ntlb_get(_s, _k);
    if (is_real(_v)) return _v;
    return _d;
}
""";

    Files["ntlb_str"] = """
function ntlb_str(_s, _k, _d)
{
    var _v = ntlb_get(_s, _k);
    if (is_string(_v)) return _v;
    return _d;
}
""";

    Files["ntlb_bool"] = """
function ntlb_bool(_s, _k, _d)
{
    var _v = ntlb_get(_s, _k);
    if (is_bool(_v)) return _v;
    if (is_real(_v)) return _v != 0;
    return _d;
}
""";

    Files["ntlb_arr"] = """
function ntlb_arr(_s, _k)
{
    var _v = ntlb_get(_s, _k);
    if (is_array(_v)) return _v;
    return [];
}
""";

    Files["ntlb_flag"] = """
function ntlb_flag(_i)
{
    if (!variable_global_exists("flag")) return 0;
    var _f = global.flag;
    if (!is_array(_f)) return 0;
    var _n = floor(_i);
    if (_n < 0 || _n >= array_length(_f)) return 0;
    var _v = _f[_n];
    if (is_real(_v)) return _v;
    if (is_bool(_v)) { if (_v) return 1; return 0; }
    return 0;
}
""";

    Files["ntlb_setflag"] = """
function ntlb_setflag(_i, _v)
{
    if (!variable_global_exists("flag")) return 0;
    var _f = global.flag;
    if (!is_array(_f)) return 0;
    var _n = floor(_i);
    if (_n < 0) return 0;
    while (array_length(_f) <= _n) array_push(_f, 0);
    _f[_n] = _v;
    global.flag = _f;
    return 1;
}
""";

    Files["ntlb_find"] = """
function ntlb_find(_key)
{
    var _bs = global.ntlb_battles;
    if (!is_array(_bs)) return undefined;
    var _n = array_length(_bs);
    if (is_real(_key)) { var _i = floor(_key); if (_i >= 0 && _i < _n) return _bs[_i]; return undefined; }
    var _s = string(_key);
    for (var _i = 0; _i < _n; _i += 1) {
        if (ntlb_str(_bs[_i], "id", "") == _s) return _bs[_i];
    }
    return undefined;
}
""";

    // ---------------------------------------------------------------- 初始化
    Files["ntlb_init"] = """
function ntlb_init()
{
    if (variable_global_exists("ntlb_ready") && global.ntlb_ready == 1) return 1;
    global.ntlb_ready = 0;
    global.ntlb_battles = [];
    global.ntlb_cfg = undefined;
    global.ntlb_pending = -1;
    global.ntlb_pending_battle = undefined;
    global.ntlb_victory = 0;
    global.ntlb_turns = 0;
    global.ntlb_turn_last = -1;
    global.ntlb_battle_frame = 0;
    global.ntlb_ctrl_seen = 0;
    global.ntlb_after = 0;
    global.ntlb_ow_active = 0;
    global.ntlb_ow_battle = undefined;
    global.ntlb_ow_frame = 0;
    global.ntlb_spawn_seq = 0;
    global.ntlb_once = ds_map_create();
    global.ntlb_wait_start = undefined;
    global.ntlb_settings = [];
    global.ntlb_set_val = undefined;
    global.ntlb_dialogues = [];
    global.ntlb_dialog_active = 0;
    global.ntlb_dialog_cur = undefined;
    global.ntlb_dialog_frames = 0;
    global.ntlb_cutscenes = [];
    global.ntlb_cut_active = 0;
    global.ntlb_cut_cur = undefined;
    global.ntlb_cut_step = 0;
    global.ntlb_cut_frames = 0;
    global.ntlb_cut_sub = 0;
    global.ntlb_cut_actor = noone;
    global.ntlb_cut_sx = 0;
    global.ntlb_cut_sy = 0;
    global.ntlb_defeat_done = 0;
    global.ntlb_text_on = 0;
    global.ntlb_text_inst = noone;
    global.ntlb_text_msg = "";
    global.ntlb_text_life = 0;

    var _ok = 0;
    try {
        var _txt = ntlb_data_json();
        var _cfg = json_parse(_txt);
        if (is_struct(_cfg)) { global.ntlb_cfg = _cfg; _ok = 1; }
    } catch (e) {
        ntl_log("ntlb", "[错误] 战斗配置解析失败: " + string(e));
    }
    if (_ok == 0) { ntl_log("ntlb", "[错误] 没有可用的战斗配置，模块不启用"); return 0; }

    var _bs = ntlb_arr(global.ntlb_cfg, "battles");
    for (var _i = 0; _i < array_length(_bs); _i += 1) {
        if (ntlb_bool(_bs[_i], "enabled", true)) array_push(global.ntlb_battles, _bs[_i]);
    }
    ntlb_settings_init();
    ntlb_dialogues_init();
    ntlb_cutscenes_init();
    global.ntlb_ready = 1;
    ntl_hook("on_frame", ntlb_frame);
    ntl_console_register("battle", "Neutraled 战斗：列出/启动/停止配置的战斗", ntlb_cmd, "battle list|start <id>|ow <id>|stop|info", "action");
    ntl_console_register("dialogue", "Neutraled 纯对话：列出/播放配置的对话", ntlb_cmd_dialogue, "dialogue list|start <id>|stop", "action");
    ntl_console_register("cutscene", "Neutraled 简单剧情：列出/播放配置的剧情", ntlb_cmd_cutscene, "cutscene list|start <id>|stop", "action");
    ntl_console_register("setting", "Neutraled mod 设置项：列出/修改（实时生效）", ntlb_cmd_setting, "setting list|set <id> <值>|reset <id>", "action");
    ntl_log("ntlb", "[就绪] 已加载 " + string(array_length(global.ntlb_battles)) + " 场战斗、"
        + string(array_length(global.ntlb_dialogues)) + " 段对话、"
        + string(array_length(global.ntlb_cutscenes)) + " 段剧情、"
        + string(array_length(global.ntlb_settings)) + " 个设置项（控制台: battle / dialogue / cutscene / setting）");
    return 1;
}
""";

    // ---------------------------------------------------------------- 每帧
    Files["ntlb_frame"] = """
function ntlb_frame(_args)
{
    if (!variable_global_exists("ntlb_ready")) return 0;
    if (global.ntlb_ready != 1) return 0;
    // 控制台可能在章节还在加载（还没有 obj_mainchara）时就被调用：先挂起，等玩家实例出现再开战。
    var _mc0 = asset_get_index("obj_mainchara");
    if (variable_global_exists("ntlb_wait_start") && is_struct(global.ntlb_wait_start))
    {
        if (_mc0 >= 0 && instance_exists(_mc0))
        {
            var _pending = global.ntlb_wait_start;
            global.ntlb_wait_start = undefined;
            ntlb_start_encounter(_pending);
        }
        return 0;
    }
    if (!variable_global_exists("flag")) return 0;
    // 战斗监督必须在「主角实例存在」守卫之前：玩家全灭会立刻进 GAME OVER，obj_mainchara 被销毁，
    // 放在守卫之后则失败判定/失败效果永远不会执行（真机实测：画面已经 GAME OVER，但日志里没有判负行）。
    if (global.ntlb_pending >= 0) { ntlb_battle_tick(); return 0; }
    var _mc = _mc0;
    if (_mc < 0 || !instance_exists(_mc)) return 0;
    // 剧情进行中：由剧情状态机接管（其中可能包含对话），播完才继续检查其它触发。
    if (global.ntlb_cut_active == 1) { ntlb_cutscene_tick(); return 0; }
    // 纯对话进行中：等 obj_dialoguer 自己结束（结束后执行 end effects）。
    if (global.ntlb_dialog_active == 1) { ntlb_dialogue_tick(); return 0; }
    if (global.ntlb_ow_active == 1) return 0;
    ntlb_check_cutscenes();
    if (global.ntlb_cut_active == 1) return 0;
    ntlb_check_dialogues();
    if (global.ntlb_dialog_active == 1) return 0;
    ntlb_check_triggers();
    return 0;
}
""";

    Files["ntlb_check_triggers"] = """
function ntlb_check_triggers()
{
    var _bs = global.ntlb_battles;
    var _n = array_length(_bs);
    if (_n <= 0) return 0;
    var _rm = string(room_get_name(room));
    var _mc = asset_get_index("obj_mainchara");
    var _px = _mc.x;
    var _py = _mc.y;
    for (var _i = 0; _i < _n; _i += 1) {
        var _b = _bs[_i];
        var _tr = ntlb_get(_b, "trigger");
        if (!is_struct(_tr)) continue;
        var _room = ntlb_str(_tr, "room", "");
        var _rf = ntlb_num(_tr, "requiredFlag", 0);
        var _r = ntlb_num(_tr, "radius", 0);
        // 没有任何条件的战斗只能手动 battle start，不自动触发
        if (_room == "" && _rf <= 0 && _r <= 0) continue;
        if (_room != "" && _room != _rm) continue;
        if (_rf > 0 && ntlb_flag(_rf) == 0) continue;
        var _key = ntlb_str(_b, "id", "b" + string(_i));
        var _persist = ntlb_bool(_tr, "persistOnce", false);
        if (ntlb_bool(_tr, "once", true) && ntlb_once_seen(_key, _persist)) continue;
        var _tx = ntlb_num(_tr, "x", 0);
        var _ty = ntlb_num(_tr, "y", 0);
        if (_r > 0 && point_distance(_px, _py, _tx, _ty) > _r) continue;
        // auto = 进房间/进半径就开战；interact = 还要站进原版交互矩形并按 Z/Enter（obj_mainchara_Step 的四向矩形）。
        var _tm = ntlb_str(_b, "triggerMode", "auto");
        if (_tm == "interact") {
            if (!button1_p()) continue;
            if (!ntlb_in_interact_zone(_tx, _ty)) continue;
        }
        var _kind = ntlb_str(_b, "kind", "encounter");
        // 实时读取设置项：关掉对应功能后不再自动触发。
        if (_kind == "overworld") { if (ntlb_feature("overworldEnabled", 1) == 0) continue; }
        else { if (ntlb_feature("battlesEnabled", 1) == 0) continue; }
        if (ntlb_bool(_tr, "once", true)) ntlb_once_mark(_key, _persist);
        ntl_log("ntlb", "[触发] " + _key + "（房间 " + _rm + "）");
        if (_kind == "overworld") ntlb_start_overworld(_b);
        else ntlb_start_encounter(_b);
        return 1;
    }
    return 0;
}
""";

    // ---------------------------------------------------------------- 遭遇战启动 / 槽位
    Files["ntlb_setup_slots"] = """
function ntlb_setup_slots(_b)
{
    var _en = ntlb_arr(_b, "enemies");
    var _n = array_length(_en);
    var _vx = __view_get(0, 0);
    var _vy = __view_get(1, 0);
    // 缺省敌人：优先用本 mod 生成的自定义敌人对象（任何章节都存在；原版 dummy 在章节 2 等版本里
    // 缺少 recruitable 等父对象变量，直接拿它顶槽会在 scr_monster_makeinstance 的 event_user(15) 报错）。
    var _dummy = asset_get_index("@@ENEMY_OBJ@@");
    if (_dummy < 0) _dummy = asset_get_index("obj_dummyenemy");
    for (var _i = 0; _i < 3; _i += 1) {
        if (_i < _n) {
            var _e = _en[_i];
            var _obj = asset_get_index(ntlb_str(_e, "object", ""));
            if (_obj < 0) _obj = _dummy;
            global.monstertype[_i] = 1;
            global.monsterinstancetype[_i] = _obj;
            var _ex = ntlb_num(_e, "x", -1);
            var _ey = ntlb_num(_e, "y", -1);
            if (_ex < 0) _ex = _vx + 540;
            if (_ey < 0) _ey = _vy + 160 + 80 * _i;
            global.monstermakex[_i] = _ex;
            global.monstermakey[_i] = _ey;
        } else {
            global.monstertype[_i] = 0;
            global.monsterinstancetype[_i] = _dummy;
            global.monstermakex[_i] = _vx + 540;
            global.monstermakey[_i] = _vy + 160 + 80 * _i;
        }
    }
    global.battlemsg[0] = ntlb_str(_b, "startMessage", "");
    global.ntlb_spawn_seq = 0;
    return 1;
}
""";

    Files["ntlb_start_encounter"] = """
function ntlb_start_encounter(_b)
{
    if (!is_struct(_b)) return 0;
    if (ntlb_feature("battlesEnabled", 1) == 0) {
        ntl_log("ntlb", "[战斗] 已被 mod 设置项禁用（battlesEnabled），不启动");
        return -1;
    }
    var _mc0 = asset_get_index("obj_mainchara");
    if (_mc0 < 0 || !instance_exists(_mc0))
    {
        // 房间还没加载完：挂起，由 ntlb_frame 在玩家出现后重试（见 ntlb_init 里的 wait_start 初始化）。
        global.ntlb_wait_start = _b;
        ntl_log("ntlb", "[战斗] 玩家尚未就绪，已挂起开战请求");
        return -1;
    }
    var _bc = asset_get_index("obj_battlecontroller");
    if (_bc >= 0 && instance_exists(_bc)) { ntl_log("ntlb", "[战斗] 已有战斗在进行，忽略"); return 0; }
    var _ec = asset_get_index("obj_encounterbasic");
    if (_ec < 0) { ntl_log("ntlb", "[错误] 找不到 obj_encounterbasic"); return 0; }
    global.ntlb_pending_battle = _b;
    global.ntlb_pending = 1;
    global.ntlb_battle_frame = 0;
    global.ntlb_ctrl_seen = 0;
    global.ntlb_victory = 0;
    global.ntlb_after = 0;
    global.ntlb_turns = 0;
    global.ntlb_turn_last = -1;
    global.ntlb_defeat_done = 0;
    global.encounterno = 0;
    // 原版 obj_encounterbasic_Create_0 无条件用 with (global.cinstance[0])：
    // 只有大世界队伍跟随者（caterpillar）在跑时才存在这份数组；在普通房间手动开战会
    // 直接 "trying to index a variable which is not an array" 崩掉，所以缺省补两个空槽。
    if (!variable_global_exists("cinstance") || !is_array(global.cinstance))
    {
        global.cinstance = [-4, -4];
    }
    var _bb = asset_get_index("obj_battleback");
    if (_bb >= 0) instance_create(0, 0, _bb);
    instance_create(0, 0, _ec);
    ntlb_setup_slots(_b);
    global.interact = 1;
    ntl_log("ntlb", "[战斗] 开始遭遇战: " + ntlb_str(_b, "id", "?"));
    return 1;
}
""";

    // ---------------------------------------------------------------- 敌人
    Files["ntlb_monstersetup"] = """
function ntlb_monstersetup()
{
    var _b = global.ntlb_pending_battle;
    if (!is_struct(_b)) return 0;
    var _en = ntlb_arr(_b, "enemies");
    if (myself < 0 || myself >= array_length(_en)) return 0;
    var _e = _en[myself];
    global.monstername[myself] = ntlb_str(_e, "name", "Enemy");
    global.monstermaxhp[myself] = max(1, ntlb_num(_e, "hp", 60));
    global.monsterhp[myself] = global.monstermaxhp[myself];
    global.monsterat[myself] = ntlb_num(_e, "at", 6);
    global.monsterdf[myself] = ntlb_num(_e, "df", 0);
    global.monsterexp[myself] = ntlb_num(_e, "exp", 0);
    global.monstergold[myself] = ntlb_num(_e, "gold", 0);
    global.sparepoint[myself] = ntlb_num(_e, "sparePoint", 0);
    global.mercymod[myself] = ntlb_num(_e, "mercyMod", 0);
    global.mercymax[myself] = ntlb_num(_e, "mercyMax", 100);
    global.monsterstatus[myself] = ntlb_num(_e, "status", 0);
    global.monstercomment[myself] = ntlb_str(_e, "comment", "");
    var _acts = ntlb_arr(_e, "acts");
    var _na = min(6, array_length(_acts));
    for (var _j = 0; _j < 6; _j += 1) {
        global.act[myself][_j] = 0;
        global.acttype[myself][_j] = 0;
        global.actspell[myself][_j] = 0;
        global.actactor[myself][_j] = 1;
        global.actcost[myself][_j] = 0;
        global.actdesc[myself][_j] = "";
        if (_j < _na) {
            global.canact[myself][_j] = 1;
            global.actname[myself][_j] = string(_acts[_j]);
        } else {
            global.canact[myself][_j] = 0;
            global.actname[myself][_j] = " ";
        }
    }
    ntl_log("ntlb", "[敌人] 槽 " + string(myself) + " = " + global.monstername[myself] + "（HP " + string(global.monsterhp[myself]) + "）");
    return 1;
}
""";

    Files["ntlb_enemy_create"] = """
function ntlb_enemy_create()
{
    myself = -1;
    // 原版敌人对象在 Create 里调 scr_enemy_object_init() 定义这一批变量；战斗控制器/绘制代码会直接读它们
    // （例如章节 2 的 obj_battlecontroller_Draw 会读/写 flash、becomeflash、fsiner、sactionboxx/y），
    // 自定义敌人对象没有父对象，必须自己补齐，否则一开战就 "Variable ... not set before reading it"。
    becomeflash = 0;
    flash = 0;
    turnt = 0;
    talked = 0;
    talktimer = 0;
    talkmax = 90;
    rtimer = 0;
    attacked = 0;
    actcon = 0;
    acttimer = 0;
    acting = 0;
    mytarget = 0;
    turns = 0;
    state = 0;
    hurttimer = 0;
    hurtamt = 0;
    hurt = 0;
    hurtshake = 0;
    shakex = 0;
    candodge = 0;
    dodgetimer = 0;
    siner = 0;
    fsiner = 0;
    con = 0;
    fatal = 0;
    ambushed = 0;
    firststrike = 0;
    damage = -1;
    grazepoints = -1;
    timepoints = -1;
    inv = -1;
    target = -1;
    grazed = -1;
    grazetimer = -1;
    element = "none";
    mercymod = 0;
    maxmercy = 100;
    recruitable = 0;
    freezable = 1;
    recruitcount = 1;
    sactionboxx = 0;
    sactionboxy = 0;
    hurtspriteoffx = 0;
    hurtspriteoffy = 0;
    custombody = 0;
    mywriter = -4;
    idlesprite = -4;
    hurtsprite = -4;
    sparedsprite = -4;
    visible = 1;
    depth = -100;
    var _seq = 0;
    if (variable_global_exists("ntlb_spawn_seq")) _seq = global.ntlb_spawn_seq;
    global.ntlb_spawn_seq = _seq + 1;
    var _b = global.ntlb_pending_battle;
    if (is_struct(_b)) {
        var _en = ntlb_arr(_b, "enemies");
        if (_seq < array_length(_en)) {
            var _sp = ntlb_str(_en[_seq], "sprite", "");
            if (_sp != "") {
                var _si = asset_get_index(_sp);
                if (_si >= 0) sprite_index = _si;
            }
        }
    }
    return 0;
}
""";

    Files["ntlb_enemy_draw"] = """
function ntlb_enemy_draw()
{
    if (sprite_index >= 0) {
        draw_self();
    } else {
        draw_set_color(c_red);
        draw_rectangle(x - 24, y - 24, x + 24, y + 24, false);
        draw_set_color(c_white);
    }
}
""";

    Files["ntlb_spawn_turn"] = """
function ntlb_spawn_turn(_b)
{
    var _turns = ntlb_arr(_b, "turns");
    var _n = array_length(_turns);
    if (_n <= 0) return 0;
    if (global.ntlb_turn_last == global.ntlb_turns) return 0;
    var _idx = global.ntlb_turns;
    if (_idx >= _n) {
        if (!ntlb_bool(_b, "loopLastTurn", true)) return 0;
        _idx = _n - 1;
    }
    global.ntlb_turn_last = global.ntlb_turns;
    var _t = _turns[_idx];
    var _bars = ntlb_arr(_t, "barrages");
    var _dc = asset_get_index("obj_dbulletcontroller");
    for (var _i = 0; _i < array_length(_bars); _i += 1) {
        var _g = _bars[_i];
        var _cnt = max(1, ntlb_num(_g, "count", 1));
        var _types = ntlb_arr(_g, "randomTypes");
        for (var _c = 0; _c < _cnt; _c += 1) {
            if (_dc < 0) break;
            var _inst = instance_create(x + _c * 40, y, _dc);
            var _ty = ntlb_num(_g, "type", 0);
            if (array_length(_types) > 0) _ty = _types[irandom(array_length(_types) - 1)];
            _inst.type = _ty;
            _inst.target = mytarget;
            var _dmg = ntlb_num(_g, "damage", 0);
            if (_dmg <= 0) _dmg = global.monsterat[myself] * 5;
            // 实时读取「弹幕伤害 %」设置项：0 = 打不到人，100 = 原始伤害，200 = 双倍。
            var _pct = ntlb_feature("barrageDamagePercent", 100);
            if (_pct < 0) _pct = 0;
            if (_pct != 100) _dmg = round(_dmg * _pct / 100);
            _inst.damage = _dmg;
            _inst.timermax = max(1, ntlb_num(_g, "timerMax", 12));
            var _ratio = ntlb_num(_g, "ratio", 1);
            var _rg = ntlb_num(_g, "randomRatio", 0);
            if (_rg > 0) _ratio += random(_rg * 2) - _rg;
            if (_ratio <= 0) _ratio = 1;
            _inst.ratio = _ratio;
            _inst.joker = ntlb_num(_g, "joker", 0);
            _inst.side = ntlb_num(_g, "side", 1);
            _inst.grazepoints = ntlb_num(_g, "grazePoints", 1);
            _inst.timepoints = ntlb_num(_g, "timePoints", 1);
        }
    }
    var _dur = max(30, ntlb_num(_t, "duration", 240));
    global.turntimer = _dur;
    global.typer = 6;
    global.fc = 0;
    global.battlemsg[0] = ntlb_str(_t, "message", "");
    global.ntlb_turns += 1;
    ntl_log("ntlb", "[弹幕] 第 " + string(global.ntlb_turns) + " 回合（" + string(array_length(_bars)) + " 组，持续 " + string(_dur) + " 帧）");
    return 1;
}
""";

    Files["ntlb_enemy_step"] = """
function ntlb_enemy_step()
{
    if (!variable_global_exists("fighting")) { instance_destroy(); exit; }
    if (global.fighting == 0) { instance_destroy(); exit; }
    if (myself < 0) exit;
    if (global.myfight == 7) { hspeed = 15; return 0; }
    if (global.mnfight == 0) { attacked = 0; talked = 0; talktimer = 0; rtimer = 0; actcon = 0; return 0; }
    if (global.monster[myself] != 1) exit;
    var _b = global.ntlb_pending_battle;
    if (!is_struct(_b)) exit;
    var _en = ntlb_arr(_b, "enemies");
    var _e = undefined;
    if (myself < array_length(_en)) _e = _en[myself];
    // ① 首次进入菜单：扔气泡文本（enemyblcon 是原版通用气泡）
    if (global.mnfight == 1 && talked == 0) {
        scr_randomtarget();
        global.msg[0] = ntlb_str(_b, "startMessage", "");
        if (global.msg[0] == "" && is_struct(_e)) global.msg[0] = ntlb_str(_e, "actMessage", "");
        if (global.msg[0] == "") global.msg[0] = "...";
        scr_enemyblcon(x - 160, y, 3);
        talked = 1;
        talktimer = 0;
    }
    // ② 气泡结束 → 进入防御回合
    if (talked == 1 && global.mnfight == 1) {
        rtimer = 0;
        scr_blconskip(15);
        if (global.mnfight == 2) {
            var _mh = asset_get_index("obj_moveheart");
            if (_mh >= 0 && !instance_exists(_mh)) scr_moveheart();
        }
        return 0;
    }
    // ③ 防御回合：到点生成弹幕（多敌人时用全局去重，只生成一次）
    if (global.mnfight == 2 && attacked == 0) {
        rtimer += 1;
        var _pre = max(1, ntlb_num(_b, "preTurnDelay", 24));
        var _turns = ntlb_arr(_b, "turns");
        var _idx = min(global.ntlb_turns, max(0, array_length(_turns) - 1));
        var _dur = 120;
        if (array_length(_turns) > 0) _dur = max(30, ntlb_num(_turns[_idx], "duration", 240));
        if (global.turntimer < _dur) global.turntimer = _dur;
        if (rtimer >= _pre) {
            ntlb_spawn_turn(_b);
            attacked = 1;
        }
        return 0;
    }
    // ④ ACT / SPARE 等非 FIGHT 路径：显示文本 → 进入防御
    if (global.myfight == 3) {
        if (actcon == 0) {
            actcon = 1;
            global.msg[0] = ntlb_str(_b, "startMessage", "");
            if (is_struct(_e)) {
                var _am = ntlb_str(_e, "actMessage", "");
                if (_am != "") global.msg[0] = _am;
            }
            if (global.msg[0] == "") global.msg[0] = "...";
            scr_battletext_default();
        }
        var _wr = asset_get_index("obj_writer");
        if (actcon == 1 && (_wr < 0 || !instance_exists(_wr))) {
            actcon = 0;
            scr_attackphase();
        }
    }
    return 0;
}
""";

    // ---------------------------------------------------------------- 战斗监督 / 收尾
    Files["ntlb_battle_tick"] = """
function ntlb_battle_tick()
{
    var _b = global.ntlb_pending_battle;
    if (!is_struct(_b)) { global.ntlb_pending = -1; return 0; }
    // 队伍全灭：每帧都判，不依赖 battlecontroller —— 玩家全灭会立刻进 GAME OVER，
    // controller 与 obj_mainchara 可能在同一帧被销毁，放在 controller 分支里永远等不到。
    if (global.ntlb_defeat_done == 0 && variable_global_exists("hp") && variable_global_exists("char")) {
        var _alive = 0;
        for (var _ci = 0; _ci < 3; _ci += 1) {
            var _cc = global.char[_ci];
            if (_cc > 0 && _cc < array_length(global.hp)) {
                if (global.hp[_cc] > 0) _alive += 1;
            }
        }
        if (_alive == 0) {
            global.ntlb_defeat_done = 1;
            global.ntlb_victory = 0;
            ntl_log("ntlb", "[战斗] 队伍全灭，判定失败");
            var _deff = ntlb_get(_b, "defeat");
            if (is_struct(_deff)) ntlb_apply_effects(_deff);
            // 立刻收尾：GAME OVER 会打断后续帧，等不到 controller 消失后的 45 帧宽限。
            ntlb_finish_battle();
            return 0;
        }
    }
    var _bc = asset_get_index("obj_battlecontroller");
    var _ctr = (_bc >= 0 && instance_exists(_bc));
    if (_ctr) {
        // 启动等待期间（controller 还没出现）else 分支也会累加 ntlb_battle_frame，
        // 所以 controller 出现的首帧帧号可能早已 >1 —— 直接写 ==1 会永远错过初始紧张度
        // （真机实测：startingTension=50 但日志里没有「初始紧张度」这一行）。
        // 修：controller 首次出现时把计数归零，保证下面的 ==1 必然成立。
        if (global.ntlb_ctrl_seen == 0) global.ntlb_battle_frame = 0;
        global.ntlb_ctrl_seen = 1;
        global.ntlb_battle_frame += 1;
        // 原版 obj_battlecontroller_Create_0.gml:26 会把 global.tension 清 0，等 controller 出现后再写初始紧张度。
        if (global.ntlb_battle_frame == 1) {
            var _st = ntlb_num(_b, "startingTension", 0);
            if (_st > 0) {
                var _mt = 250;
                if (variable_global_exists("maxtension")) _mt = global.maxtension;
                global.tension = min(_mt, max(0, _st));
                ntl_log("ntlb", "[战斗] 初始紧张度 = " + string(global.tension));
            }
        }
        if (scr_monsterpop() == 0) global.ntlb_victory = 1;
        var _lim = ntlb_num(_b, "roundLimit", 0);
        if (_lim > 0 && global.ntlb_turns >= _lim && global.mnfight == 0 && global.ntlb_victory == 0) {
            ntl_log("ntlb", "[战斗] 达到回合上限 " + string(_lim) + "，判定胜利");
            global.ntlb_victory = 1;
            scr_wincombat();
        }
        if (global.ntlb_battle_frame > 36000) {
            ntl_log("ntlb", "[警告] 战斗超时（10 分钟），强制收尾");
            global.ntlb_pending = -1;
            global.ntlb_pending_battle = undefined;
            global.interact = 0;
        }
        return 0;
    }
    if (global.ntlb_ctrl_seen == 1) {
        global.ntlb_after += 1;
        if (global.ntlb_after >= 45) ntlb_finish_battle();
    } else {
        global.ntlb_battle_frame += 1;
        if (global.ntlb_battle_frame > 600) {
            ntl_log("ntlb", "[警告] 遭遇战启动失败（600 帧内没出现 battlecontroller），放弃");
            global.ntlb_pending = -1;
            global.ntlb_pending_battle = undefined;
            global.interact = 0;
        }
    }
    return 0;
}
""";

    Files["ntlb_finish_battle"] = """
function ntlb_finish_battle()
{
    var _b = global.ntlb_pending_battle;
    var _win = (global.ntlb_victory == 1);
    global.ntlb_pending = -1;
    global.ntlb_pending_battle = undefined;
    global.ntlb_ctrl_seen = 0;
    global.ntlb_battle_frame = 0;
    global.ntlb_after = 0;
    global.ntlb_turn_last = -1;
    global.interact = 0;
    var _mc = asset_get_index("obj_mainchara");
    if (_mc >= 0 && instance_exists(_mc)) {
        with (_mc) { visible = 1; }
    }
    if (!is_struct(_b)) return 0;
    var _eff = ntlb_get(_b, "victory");
    if (!_win) _eff = ntlb_get(_b, "defeat");
    // 全灭时已经立刻执行过失败效果（game over 会打断后续帧），这里避免重复执行。
    if (is_struct(_eff) && (_win || global.ntlb_defeat_done == 0)) ntlb_apply_effects(_eff);
    global.ntlb_defeat_done = 0;
    ntl_log("ntlb", "[战斗] 结束（" + (_win ? "胜利" : "失败") + "）");
    return 1;
}
""";

    Files["ntlb_apply_effects"] = """
function ntlb_apply_effects(_eff)
{
    if (!is_struct(_eff)) return 0;
    var _set = ntlb_arr(_eff, "setFlags");
    for (var _i = 0; _i < array_length(_set); _i += 1) ntlb_setflag(_set[_i], 1);
    var _clr = ntlb_arr(_eff, "clearFlags");
    for (var _i = 0; _i < array_length(_clr); _i += 1) ntlb_setflag(_clr[_i], 0);
    var _heal = ntlb_num(_eff, "healPercent", 0);
    if (_heal > 0 && variable_global_exists("maxhp") && variable_global_exists("hp")) {
        for (var _i = 0; _i < 3; _i += 1) {
            var _c = global.char[_i];
            if (_c > 0 && _c < array_length(global.maxhp)) {
                global.hp[_c] = min(global.maxhp[_c], global.hp[_c] + (_heal * global.maxhp[_c]) / 100);
            }
        }
    }
    var _msg = ntlb_str(_eff, "message", "");
    if (_msg != "") {
        ntl_log("ntlb", "[剧情] " + _msg);
        ntlb_show_text(_msg, 150);
    }
    var _room = ntlb_str(_eff, "gotoRoom", "");
    if (_room != "") {
        var _ri = asset_get_index(_room);
        if (_ri >= 0) room_goto(_ri);
    }
    var _next = ntlb_str(_eff, "startBattle", "");
    if (_next != "") {
        var _nb = ntlb_find(_next);
        if (is_struct(_nb)) ntlb_start_encounter(_nb);
    }
    return 1;
}
""";

    // ---------------------------------------------------------------- overworld
    Files["ntlb_start_overworld"] = """
function ntlb_start_overworld(_b)
{
    if (!is_struct(_b)) return 0;
    if (ntlb_feature("overworldEnabled", 1) == 0) {
        ntl_log("ntlb", "[overworld] 已被 mod 设置项禁用（overworldEnabled），不启动");
        return 0;
    }
    var _ow = asset_get_index("@@OW_OBJ@@");
    if (_ow < 0) { ntl_log("ntlb", "[错误] 找不到 overworld 控制器对象"); return 0; }
    if (instance_exists(_ow)) return 0;
    var _mc = asset_get_index("obj_mainchara");
    if (_mc < 0 || !instance_exists(_mc)) return 0;
    global.ntlb_ow_active = 1;
    global.ntlb_ow_battle = _b;
    global.ntlb_ow_frame = 0;
    instance_create(_mc.x + 120, _mc.y, _ow);
    global.interact = 1;
    ntl_log("ntlb", "[overworld] 开始弹幕战: " + ntlb_str(_b, "id", "?"));
    return 1;
}
""";

    Files["ntlb_ow_create"] = """
function ntlb_ow_create()
{
    ntlb_ow_timer = 0;
    ntlb_ow_frames = 0;
    ntlb_ow_contact = 0;
    visible = 1;
    depth = -9000;
}
""";

    Files["ntlb_ow_step"] = """
function ntlb_ow_step()
{
    var _b = global.ntlb_ow_battle;
    if (!is_struct(_b)) { instance_destroy(); exit; }
    var _ow = ntlb_get(_b, "overworld");
    if (!is_struct(_ow)) { instance_destroy(); exit; }
    if (variable_global_exists("ntlb_ow_frame")) ntlb_ow_frames = global.ntlb_ow_frame;
    ntlb_ow_frames += 1;
    global.ntlb_ow_frame = ntlb_ow_frames;
    ntlb_ow_timer += 1;
    var _bl = asset_get_index("@@BULLET_OBJ@@");
    var _mc = asset_get_index("obj_mainchara");
    var _px = 0;
    var _py = 0;
    if (_mc >= 0 && instance_exists(_mc)) { _px = _mc.x; _py = _mc.y; }
    var _dmg = ntlb_num(_ow, "contactDamage", 4);
    var _ps = ntlb_arr(_ow, "patterns");
    if (_bl >= 0) {
        for (var _i = 0; _i < array_length(_ps); _i += 1) {
            var _p = _ps[_i];
            var _iv = max(1, ntlb_num(_p, "interval", 20));
            if ((ntlb_ow_timer mod _iv) != 0) continue;
            var _kind = ntlb_str(_p, "kind", "rain");
            var _cnt = max(1, ntlb_num(_p, "count", 3));
            var _spd = ntlb_num(_p, "speed", 3);
            var _life = max(10, ntlb_num(_p, "life", 180));
            var _dmg2 = ntlb_num(_p, "damage", _dmg);
            var _spread = ntlb_num(_p, "spread", 24);
            var _ang = ntlb_num(_p, "angle", 90);
            var _rc = ntlb_num(_p, "randomCount", 0);
            if (_rc > 0) _cnt = max(1, _cnt + irandom(_rc));
            for (var _c = 0; _c < _cnt; _c += 1) {
                var _bx = _px + random_range(-140, 140);
                var _by = _py - 180;
                var _dir = _ang;
                var _sp = _spd;
                if (_kind == "rain") {
                    _bx = random(room_width);
                    _by = max(0, _py - 360);
                    _dir = 90 + random_range(-_spread, _spread);
                }
                if (_kind == "ring") {
                    _bx = _px;
                    _by = _py;
                    _dir = (360 * _c) / _cnt + random(_spread);
                }
                if (_kind == "aim") {
                    _bx = _px;
                    _by = _py - 240;
                    _dir = point_direction(_bx, _by, _px, _py) + random_range(-_spread, _spread);
                }
                if (_kind == "wall") {
                    _bx = room_width;
                    _dir = 180;
                    if ((_c mod 2) == 0) { _bx = 0; _dir = 0; }
                    _by = _py + random_range(-160, 160);
                }
                var _bi = instance_create(_bx, _by, _bl);
                _bi.hspeed = lengthdir_x(_sp, _dir);
                _bi.vspeed = lengthdir_y(_sp, _dir);
                _bi.life = _life;
                _bi.bulletDamage = _dmg2;
            }
        }
    }
    var _dur = max(30, ntlb_num(_ow, "duration", 300));
    if (ntlb_ow_frames >= _dur) {
        with (asset_get_index("@@BULLET_OBJ@@")) instance_destroy();
        global.ntlb_ow_active = 0;
        global.ntlb_ow_battle = undefined;
        global.ntlb_ow_frame = 0;
        global.interact = 0;
        var _eff = ntlb_get(_b, "victory");
        if (is_struct(_eff)) ntlb_apply_effects(_eff);
        ntl_log("ntlb", "[overworld] 弹幕结束（" + string(_dur) + " 帧）");
        instance_destroy();
    }
    return 0;
}
""";

    Files["ntlb_ow_draw"] = """
function ntlb_ow_draw()
{
    draw_set_color(c_yellow);
    draw_rectangle(x - 10, y - 10, x + 10, y + 10, true);
    draw_set_color(c_white);
}
""";

    Files["ntlb_bullet_create"] = """
function ntlb_bullet_create()
{
    life = 180;
    bulletDamage = 4;
    visible = 1;
    depth = -9500;
}
""";

    Files["ntlb_bullet_step"] = """
function ntlb_bullet_step()
{
    life -= 1;
    if (life <= 0) { instance_destroy(); exit; }
    if (x < -80 || x > room_width + 80 || y < -80 || y > room_height + 80) { instance_destroy(); exit; }
    if (variable_global_exists("inv") && global.inv < 0) {
        var _mc = asset_get_index("obj_mainchara");
        if (_mc >= 0 && instance_exists(_mc)) {
            if (point_distance(x, y, _mc.x, _mc.y) < 14) {
                damage = bulletDamage;
                target = 0;
                scr_damage_all_overworld();
                instance_destroy();
            }
        }
    }
}
""";

    Files["ntlb_bullet_draw"] = """
function ntlb_bullet_draw()
{
    draw_set_color(c_white);
    draw_circle(x, y, 5, false);
    draw_set_color(c_white);
}
""";

    // ---------------------------------------------------------------- Mod 设置项（游戏内 ntl Mod 设置面板）
    Files["ntlb_setting_index"] = """
function ntlb_setting_index(_id)
{
    if (!is_array(global.ntlb_settings)) return -1;
    for (var _i = 0; _i < array_length(global.ntlb_settings); _i += 1) {
        if (ntlb_str(global.ntlb_settings[_i], "id", "") == _id) return _i;
    }
    return -1;
}
""";

    Files["ntlb_setting_display"] = """
function ntlb_setting_display(_i)
{
    if (!is_array(global.ntlb_settings)) return "";
    if (_i < 0 || _i >= array_length(global.ntlb_settings)) return "";
    var _s = global.ntlb_settings[_i];
    var _id = ntlb_str(_s, "id", "");
    var _v = ds_map_find_value(global.ntlb_set_val, _id);
    if (!is_string(_v)) _v = string(_v);
    var _type = ntlb_str(_s, "type", "bool");
    if (_type == "bool") {
        if (_v == "0") return "关";
        return "开";
    }
    if (_type == "choice") {
        var _op = ntlb_arr(_s, "options");
        for (var _j = 0; _j < array_length(_op); _j += 1) {
            if (ntlb_str(_op[_j], "value", "") == _v) return ntlb_str(_op[_j], "label", _v);
        }
        return _v;
    }
    return _v;
}
""";

    Files["ntlb_setting_value"] = """
function ntlb_setting_value(_id, _def)
{
    if (!variable_global_exists("ntlb_set_val")) return _def;
    if (!ds_map_exists(global.ntlb_set_val, _id)) return _def;
    var _v = ds_map_find_value(global.ntlb_set_val, _id);
    if (is_string(_v)) return _v;
    return string(_v);
}
""";

    Files["ntlb_feature"] = """
function ntlb_feature(_name, _def)
{
    // 实时读取绑定了该 feature 的设置项；没有配置就返回默认值。
    if (!variable_global_exists("ntlb_settings")) return _def;
    if (!is_array(global.ntlb_settings)) return _def;
    var _lname = string_lower(_name);
    var _n = array_length(global.ntlb_settings);
    for (var _i = 0; _i < _n; _i += 1) {
        var _s = global.ntlb_settings[_i];
        var _f = string_lower(ntlb_str(_s, "feature", "none"));
        if (_f != _lname) continue;
        var _id = ntlb_str(_s, "id", "");
        var _v = ntlb_setting_value(_id, "");
        if (_lname == "barragedamagepercent") {
            var _num = _def;
            try { _num = real(_v); } catch (e) { _num = _def; }
            return _num;
        }
        if (_v == "0" || string_lower(_v) == "false") return 0;
        return 1;
    }
    return _def;
}
""";

    Files["ntlb_setting_apply"] = """
function ntlb_setting_apply(_i, _nv)
{
    if (!is_array(global.ntlb_settings)) return 0;
    if (_i < 0 || _i >= array_length(global.ntlb_settings)) return 0;
    var _s = global.ntlb_settings[_i];
    var _id = ntlb_str(_s, "id", "");
    if (!is_string(_nv)) _nv = string(_nv);
    ds_map_replace(global.ntlb_set_val, _id, _nv);
    ntl_mod_data_set("@@PREFIX@@.set." + _id, _nv);
    ntl_mod_data_save();
    ntl_menu_set_value("@@PREFIX@@." + _id, ntlb_setting_display(_i));
    ntl_log("ntlb", "[设置] " + _id + " = " + _nv);
    return 1;
}
""";

    Files["ntlb_setting_cycle"] = """
function ntlb_setting_cycle(_i)
{
    // 面板项动作：bool 取反 / choice 轮换下一项 / int 加 step（超过 max 回到 min）。
    if (!is_array(global.ntlb_settings)) return 0;
    if (_i < 0 || _i >= array_length(global.ntlb_settings)) return 0;
    var _s = global.ntlb_settings[_i];
    var _id = ntlb_str(_s, "id", "");
    var _type = ntlb_str(_s, "type", "bool");
    var _cur = ntlb_setting_value(_id, "0");
    var _nv = _cur;
    if (_type == "bool") {
        if (_cur == "0") _nv = "1"; else _nv = "0";
    } else if (_type == "choice") {
        var _op = ntlb_arr(_s, "options");
        var _n = array_length(_op);
        if (_n > 0) {
            var _k = 0;
            for (var _j = 0; _j < _n; _j += 1) {
                if (ntlb_str(_op[_j], "value", "") == _cur) _k = _j;
            }
            _k = (_k + 1) mod _n;
            _nv = ntlb_str(_op[_k], "value", "");
        }
    } else {
        var _v = 0;
        try { _v = real(_cur); } catch (e) { _v = ntlb_num(_s, "min", 0); }
        var _step = max(1, ntlb_num(_s, "step", 10));
        var _min = ntlb_num(_s, "min", 0);
        var _max = ntlb_num(_s, "max", 100);
        _v = _v + _step;
        if (_v > _max) _v = _min;
        _nv = string(round(_v));
    }
    return ntlb_setting_apply(_i, _nv);
}
""";

    Files["ntlb_settings_init"] = """
function ntlb_settings_init()
{
    global.ntlb_settings = [];
    global.ntlb_set_val = ds_map_create();
    var _ss = ntlb_arr(global.ntlb_cfg, "settings");
    for (var _i = 0; _i < array_length(_ss); _i += 1) {
        var _s = _ss[_i];
        var _id = ntlb_str(_s, "id", "setting" + string(_i));
        array_push(global.ntlb_settings, _s);
        var _def = ntlb_str(_s, "value", "0");
        var _pv = undefined;
        try { _pv = ntl_mod_data_get("@@PREFIX@@.set." + _id); } catch (e) { _pv = undefined; }
        if (is_string(_pv) && _pv != "") _def = _pv;
        else if (is_real(_pv)) _def = string(_pv);
        ds_map_add(global.ntlb_set_val, _id, _def);
        var _mid = "@@PREFIX@@." + _id;
        var _act = "@@PREFIX@@_setting_" + string(_i);
        ntl_menu_add(_mid, ntlb_str(_s, "label", _id), ntlb_setting_display(_i), _act, ntlb_str(_s, "description", ""));
        ntl_log("ntlb", "[设置] 已注册面板项: " + _id + " = " + ntlb_setting_display(_i));
    }
    ntl_log("ntlb", "[设置] 共 " + string(array_length(global.ntlb_settings)) + " 项（控制台: setting list）");
    return array_length(global.ntlb_settings);
}
""";

    Files["ntlb_cmd_setting"] = """
function ntlb_cmd_setting(_rest)
{
    var _raw = string_trim(string(_rest));
    var _a = string_lower(_raw);
    if (_a == "" || _a == "list") {
        ntl_console_log("[setting] 已注册 " + string(array_length(global.ntlb_settings)) + " 个 mod 设置项：");
        for (var _i = 0; _i < array_length(global.ntlb_settings); _i += 1) {
            var _s = global.ntlb_settings[_i];
            ntl_console_log("  " + ntlb_str(_s, "id", "?") + " = " + ntlb_setting_display(_i)
                + "（" + ntlb_str(_s, "label", "") + "，feature=" + ntlb_str(_s, "feature", "none") + "）");
        }
        ntl_console_log("用法: setting set <id> <值> | setting reset <id> | setting list");
        return 0;
    }
    if (string_pos("set ", _a) == 1) {
        var _body = string_trim(string_delete(_raw, 1, 4));
        var _sp = string_pos(" ", _body);
        if (_sp <= 0) { ntl_console_log("[setting] 用法: setting set <id> <值>"); return 0; }
        var _id = string_trim(string_copy(_body, 1, _sp - 1));
        var _val = string_trim(string_copy(_body, _sp + 1, string_length(_body) - _sp));
        var _i = ntlb_setting_index(_id);
        if (_i < 0) { ntl_console_log("[setting] 找不到: " + _id); return 0; }
        var _s = global.ntlb_settings[_i];
        var _type = ntlb_str(_s, "type", "bool");
        if (_type == "bool") {
            if (string_lower(_val) == "true" || _val == "开") _val = "1";
            if (string_lower(_val) == "false" || _val == "关") _val = "0";
            if (_val != "0" && _val != "1") { ntl_console_log("[setting] bool 只能是 0/1"); return 0; }
        } else if (_type == "choice") {
            var _op = ntlb_arr(_s, "options");
            var _found = 0;
            for (var _j = 0; _j < array_length(_op); _j += 1) {
                if (ntlb_str(_op[_j], "value", "") == _val || ntlb_str(_op[_j], "label", "") == _val) {
                    _val = ntlb_str(_op[_j], "value", "");
                    _found = 1;
                }
            }
            if (_found == 0) { ntl_console_log("[setting] 选项不存在: " + _val); return 0; }
        } else {
            var _num = 0;
            try { _num = real(_val); } catch (e) { _num = ntlb_num(_s, "min", 0); }
            _num = clamp(_num, ntlb_num(_s, "min", 0), ntlb_num(_s, "max", 100));
            _val = string(round(_num));
        }
        ntlb_setting_apply(_i, _val);
        return 0;
    }
    if (string_pos("reset ", _a) == 1) {
        var _id2 = string_trim(string_delete(_raw, 1, 6));
        var _i2 = ntlb_setting_index(_id2);
        if (_i2 < 0) { ntl_console_log("[setting] 找不到: " + _id2); return 0; }
        ntlb_setting_apply(_i2, ntlb_str(global.ntlb_settings[_i2], "value", "0"));
        return 0;
    }
    ntl_console_log("[setting] 未知子命令: " + _raw + "（list / set <id> <值> / reset <id>）");
    return 0;
}
""";

    // ---------------------------------------------------------------- 纯对话（原版 global.msg + obj_dialoguer）
    Files["ntlb_dialogue_find"] = """
function ntlb_dialogue_find(_key)
{
    var _ds = global.ntlb_dialogues;
    if (!is_array(_ds)) return undefined;
    var _n = array_length(_ds);
    if (is_real(_key)) { var _i = floor(_key); if (_i >= 0 && _i < _n) return _ds[_i]; return undefined; }
    var _s = string(_key);
    for (var _i = 0; _i < _n; _i += 1) {
        if (ntlb_str(_ds[_i], "id", "") == _s) return _ds[_i];
    }
    return undefined;
}
""";

    Files["ntlb_dialogue_build_msgs"] = """
function ntlb_dialogue_build_msgs(_d)
{
    // 原版 obj_writer：行尾 "%" 翻到下一行，最后一行用 "/%"。文本里真正的 % 会被删掉，
    // 否则会在半句话处提前翻页。
    var _lines = ntlb_arr(_d, "lines");
    var _n = array_length(_lines);
    var _sp = ntlb_str(_d, "speaker", "");
    global.msg = [];
    if (_n <= 0) {
        array_push(global.msg, "/%");
    } else {
        for (var _i = 0; _i < _n; _i += 1) {
            var _t = string(_lines[_i]);
            _t = string_replace_all(_t, "%", "");
            // 原版对白框没有名字栏（obj_writer 只画正文），非空 Speaker 就写在第一行前面。
            if (_i == 0 && _sp != "") _t = _sp + "：" + _t;
            if (_i < _n - 1) _t = _t + "%";
            else _t = _t + "/%";
            array_push(global.msg, _t);
        }
    }
    // obj_writer 的 Create 会无条件读 global.msg[0..99]（原版对话数组总是够长），
    // 我们只有几行时补空串，否则 Push 越界直接崩。
    var _pad = array_length(global.msg);
    while (_pad < 100) { global.msg[_pad] = ""; _pad += 1; }
    return _n;
}
""";

    Files["ntlb_start_dialogue"] = """
function ntlb_start_dialogue(_d, _manual)
{
    if (!is_struct(_d)) return -1;
    if (!variable_global_exists("ntlb_dialogues")) return -1;
    if (!variable_global_exists("flag")) { ntl_log("ntlb", "[对话] 还没进入章节，忽略"); return -1; }
    var _dg = asset_get_index("obj_dialoguer");
    if (_dg < 0) { ntl_log("ntlb", "[错误] 找不到 obj_dialoguer"); return -1; }
    var _mc = asset_get_index("obj_mainchara");
    if (_mc < 0 || !instance_exists(_mc)) { ntl_log("ntlb", "[对话] 玩家尚未就绪，忽略"); return -1; }
    if (global.interact != 0) { ntl_log("ntlb", "[对话] 当前有交互在进行（interact=" + string(global.interact) + "），忽略"); return -1; }
    ntlb_dialogue_build_msgs(_d);
    global.msc = 0;
    global.typer = 5;
    if (variable_global_exists("darkzone") && global.darkzone == 1) global.typer = 6;
    global.fc = 0;
    global.fe = 0;
    global.interact = 1;
    global.ntlb_dialog_active = 1;
    global.ntlb_dialog_cur = _d;
    global.ntlb_dialog_frames = 0;
    instance_create(0, 0, _dg);
    ntl_log("ntlb", "[对话] 开始: " + ntlb_str(_d, "id", "?") + "（" + string(array_length(ntlb_arr(_d, "lines"))) + " 行）");
    return 1;
}
""";

    Files["ntlb_dialogue_tick"] = """
function ntlb_dialogue_tick()
{
    if (global.ntlb_dialog_active != 1) return 0;
    global.ntlb_dialog_frames += 1;
    var _dg = asset_get_index("obj_dialoguer");
    if (_dg >= 0 && instance_exists(_dg)) return 0;
    // obj_dialoguer 在 writer 消失后自己销毁 —— 到这里说明对话已经完全结束。
    global.ntlb_dialog_active = 0;
    var _d = global.ntlb_dialog_cur;
    global.ntlb_dialog_cur = undefined;
    global.interact = 0;
    var _mc = asset_get_index("obj_mainchara");
    if (_mc >= 0 && instance_exists(_mc)) { with (_mc) onebuffer = 5; }
    ntl_log("ntlb", "[对话] 结束");
    if (is_struct(_d)) {
        var _end = ntlb_get(_d, "end");
        if (is_struct(_end)) ntlb_apply_effects(_end);
    }
    return 1;
}
""";

    Files["ntlb_check_dialogues"] = """
function ntlb_check_dialogues()
{
    if (!is_array(global.ntlb_dialogues)) return 0;
    var _n = array_length(global.ntlb_dialogues);
    if (_n <= 0) return 0;
    if (global.interact != 0) return 0;
    if (ntlb_feature("dialoguesEnabled", 1) == 0) return 0;
    var _rm = string(room_get_name(room));
    var _mc = asset_get_index("obj_mainchara");
    var _px = _mc.x;
    var _py = _mc.y;
    for (var _i = 0; _i < _n; _i += 1) {
        var _d = global.ntlb_dialogues[_i];
        var _tr = ntlb_get(_d, "trigger");
        if (!is_struct(_tr)) continue;
        var _room = ntlb_str(_tr, "room", "");
        var _rf = ntlb_num(_tr, "requiredFlag", 0);
        var _r = ntlb_num(_tr, "radius", 64);
        if (_room == "" && _rf <= 0) continue;
        if (_room != "" && _room != _rm) continue;
        if (_rf > 0 && ntlb_flag(_rf) == 0) continue;
        var _key = "dlg:" + ntlb_str(_d, "id", "d" + string(_i));
        var _persist = ntlb_bool(_tr, "persistOnce", false);
        if (ntlb_bool(_tr, "once", true) && ntlb_once_seen(_key, _persist)) continue;
        var _tx = ntlb_num(_tr, "x", _px);
        var _ty = ntlb_num(_tr, "y", _py);
        if (_r > 0 && point_distance(_px, _py, _tx, _ty) > _r) continue;
        // area = 走进半径自动播；interact = 站进原版交互矩形并按 Z/Enter。
        var _dm = ntlb_str(_d, "mode", "interact");
        if (_dm != "area") {
            if (!button1_p()) continue;
            if (!ntlb_in_interact_zone(_tx, _ty)) continue;
        }
        if (ntlb_bool(_tr, "once", true)) ntlb_once_mark(_key, _persist);
        ntl_log("ntlb", "[触发] 对话 " + ntlb_str(_d, "id", "?") + "（房间 " + _rm + "）");
        return ntlb_start_dialogue(_d, 0);
    }
    return 0;
}
""";

    Files["ntlb_dialogues_init"] = """
function ntlb_dialogues_init()
{
    global.ntlb_dialogues = [];
    global.ntlb_dialog_active = 0;
    global.ntlb_dialog_cur = undefined;
    global.ntlb_dialog_frames = 0;
    var _ds = ntlb_arr(global.ntlb_cfg, "dialogues");
    for (var _i = 0; _i < array_length(_ds); _i += 1) {
        if (ntlb_bool(_ds[_i], "enabled", true)) array_push(global.ntlb_dialogues, _ds[_i]);
    }
    ntl_log("ntlb", "[对话] 已加载 " + string(array_length(global.ntlb_dialogues)) + " 段纯对话（控制台: dialogue list）");
    return array_length(global.ntlb_dialogues);
}
""";

    Files["ntlb_cmd_dialogue"] = """
function ntlb_cmd_dialogue(_rest)
{
    var _raw = string_trim(string(_rest));
    var _a = string_lower(_raw);
    if (_a == "" || _a == "list") {
        ntl_console_log("[dialogue] 已加载 " + string(array_length(global.ntlb_dialogues)) + " 段对话：");
        for (var _i = 0; _i < array_length(global.ntlb_dialogues); _i += 1) {
            var _d = global.ntlb_dialogues[_i];
            ntl_console_log("  " + string(_i) + ". " + ntlb_str(_d, "id", "?") + "  [" + ntlb_str(_d, "room", "") + "]  "
                + string(array_length(ntlb_arr(_d, "lines"))) + " 行  " + ntlb_str(_d, "name", ""));
        }
        ntl_console_log("用法: dialogue start <id> | dialogue stop | dialogue list");
        return 0;
    }
    if (string_pos("start ", _a) == 1) {
        var _k = string_trim(string_delete(_raw, 1, 6));
        var _d2 = ntlb_dialogue_find(_k);
        if (!is_struct(_d2)) { ntl_console_log("[dialogue] 找不到: " + _k); return 0; }
        var _r = ntlb_start_dialogue(_d2, 1);
        if (_r != 1) ntl_console_log("[dialogue] 启动失败（code " + string(_r) + "）");
        return 0;
    }
    if (_a == "stop") {
        var _dg = asset_get_index("obj_dialoguer");
        if (_dg >= 0) { with (_dg) instance_destroy(); }
        var _wr = asset_get_index("obj_writer");
        if (_wr >= 0) { with (_wr) instance_destroy(); }
        global.ntlb_dialog_active = 0;
        global.ntlb_dialog_cur = undefined;
        global.interact = 0;
        ntl_console_log("[dialogue] 已停止");
        return 0;
    }
    ntl_console_log("[dialogue] 未知子命令: " + _raw + "（list / start <id> / stop）");
    return 0;
}
""";

    // ---------------------------------------------------------------- 简单剧情（cutscene）
    Files["ntlb_cutscene_find"] = """
function ntlb_cutscene_find(_key)
{
    var _cs = global.ntlb_cutscenes;
    if (!is_array(_cs)) return undefined;
    var _n = array_length(_cs);
    if (is_real(_key)) { var _i = floor(_key); if (_i >= 0 && _i < _n) return _cs[_i]; return undefined; }
    var _s = string(_key);
    for (var _i = 0; _i < _n; _i += 1) {
        if (ntlb_str(_cs[_i], "id", "") == _s) return _cs[_i];
    }
    return undefined;
}
""";

    Files["ntlb_cut_actor_inst"] = """
function ntlb_cut_actor_inst(_c, _st)
{
    // 步骤里的 actor：player = 玩家角色；trigger = 本段剧情绑定的那个地图实体。
    var _who = ntlb_str(_st, "actor", "player");
    var _mc = asset_get_index("obj_mainchara");
    if (_who == "player") {
        if (_mc >= 0 && instance_exists(_mc)) return _mc;
        return noone;
    }
    // trigger：原版实例没有地图 InstanceID，只能按「对象名 + 地图坐标」找最近的那个。
    var _on = ntlb_str(_c, "objectName", "");
    if (_on == "") return noone;
    var _oi = asset_get_index(_on);
    if (_oi < 0) return noone;
    var _tx = ntlb_num(_c, "instanceX", -1);
    var _ty = ntlb_num(_c, "instanceY", -1);
    if (_tx < 0 || _ty < 0) return noone;
    var _cnt = instance_number(_oi);
    var _best = noone;
    var _bd = 64;
    for (var _k = 0; _k < _cnt; _k += 1) {
        var _ii = instance_find(_oi, _k);
        if (_ii == noone || !instance_exists(_ii)) continue;
        var _dd = point_distance(_ii.x, _ii.y, _tx, _ty);
        if (_dd < _bd) { _bd = _dd; _best = _ii; }
    }
    return _best;
}
""";

    Files["ntlb_cut_face"] = """
function ntlb_cut_face(_inst, _dir)
{
    if (_inst == noone || !instance_exists(_inst)) return 0;
    var _d = string_lower(_dir);
    var _mc = asset_get_index("obj_mainchara");
    if (_mc >= 0 && _inst.object_index == _mc) {
        // 原版 obj_mainchara 的朝向走 global.facing（0=下 1=右 2=上 3=左），
        // 四向精灵变量 dsprite/rsprite/usprite/lsprite 在 Create 里定义。
        if (_d == "down") global.facing = 0;
        else if (_d == "right") global.facing = 1;
        else if (_d == "up") global.facing = 2;
        else global.facing = 3;
        var _spr = -1;
        if (variable_instance_exists(_inst, "dsprite")) {
            if (global.facing == 0) _spr = _inst.dsprite;
            else if (global.facing == 1) _spr = _inst.rsprite;
            else if (global.facing == 2) _spr = _inst.usprite;
            else _spr = _inst.lsprite;
        }
        if (_spr >= 0) _inst.sprite_index = _spr;
        return 1;
    }
    // 普通实例没有四向精灵：用左右翻转表示朝向。
    if (_d == "left") _inst.image_xscale = -abs(_inst.image_xscale);
    else if (_d == "right") _inst.image_xscale = abs(_inst.image_xscale);
    return 1;
}
""";

    Files["ntlb_cut_action"] = """
function ntlb_cut_action(_inst, _st)
{
    if (_inst == noone || !instance_exists(_inst)) return 0;
    var _sn = ntlb_str(_st, "sprite", "");
    if (_sn != "") {
        var _si = asset_get_index(_sn);
        if (_si >= 0) _inst.sprite_index = _si;
        else ntl_log("ntlb", "[剧情] 找不到精灵: " + _sn);
    }
    _inst.image_index = ntlb_num(_st, "imageIndex", 0);
    var _sp = ntlb_num(_st, "imageSpeed", -1);
    if (_sp >= 0) _inst.image_speed = _sp;
    var _dir = ntlb_str(_st, "direction", "");
    if (_dir != "") ntlb_cut_face(_inst, _dir);
    return 1;
}
""";

    Files["ntlb_cutscene_begin_step"] = """
function ntlb_cutscene_begin_step()
{
    var _c = global.ntlb_cut_cur;
    if (!is_struct(_c)) return 0;
    var _steps = ntlb_arr(_c, "steps");
    if (global.ntlb_cut_step >= array_length(_steps)) { ntlb_cutscene_finish(); return 0; }
    var _st = _steps[global.ntlb_cut_step];
    global.ntlb_cut_frames = 0;
    global.ntlb_cut_sub = 0;
    var _inst = ntlb_cut_actor_inst(_c, _st);
    global.ntlb_cut_actor = _inst;
    var _kind = ntlb_str(_st, "kind", "wait");
    if (_kind == "move" && _inst != noone && instance_exists(_inst)) {
        global.ntlb_cut_sx = _inst.x;
        global.ntlb_cut_sy = _inst.y;
        var _dx = ntlb_num(_st, "x", _inst.x) - _inst.x;
        if (_dx < -2) ntlb_cut_face(_inst, "left");
        else if (_dx > 2) ntlb_cut_face(_inst, "right");
    }
    if (_kind == "face") ntlb_cut_face(_inst, ntlb_str(_st, "direction", "right"));
    return 1;
}
""";

    Files["ntlb_cutscene_next"] = """
function ntlb_cutscene_next()
{
    global.ntlb_cut_step += 1;
    var _c = global.ntlb_cut_cur;
    var _n = 0;
    if (is_struct(_c)) _n = array_length(ntlb_arr(_c, "steps"));
    if (global.ntlb_cut_step >= _n) { ntlb_cutscene_finish(); return 0; }
    ntlb_cutscene_begin_step();
    return 1;
}
""";

    Files["ntlb_cutscene_finish"] = """
function ntlb_cutscene_finish()
{
    if (global.ntlb_cut_active != 1) return 0;
    var _c = global.ntlb_cut_cur;
    global.ntlb_cut_active = 0;
    global.ntlb_cut_cur = undefined;
    global.ntlb_cut_step = 0;
    global.ntlb_cut_frames = 0;
    global.ntlb_cut_sub = 0;
    global.ntlb_cut_actor = noone;
    global.interact = 0;
    var _mc = asset_get_index("obj_mainchara");
    if (_mc >= 0 && instance_exists(_mc)) { with (_mc) onebuffer = 5; }
    if (is_struct(_c)) {
        ntl_log("ntlb", "[剧情] 结束: " + ntlb_str(_c, "id", "?"));
        var _end = ntlb_get(_c, "end");
        if (is_struct(_end)) ntlb_apply_effects(_end);
    }
    return 1;
}
""";

    Files["ntlb_start_cutscene"] = """
function ntlb_start_cutscene(_c, _manual)
{
    if (!is_struct(_c)) return -1;
    if (!variable_global_exists("flag")) { ntl_log("ntlb", "[剧情] 还没进入章节，忽略"); return -1; }
    if (global.ntlb_cut_active == 1) return 0;
    if (global.ntlb_dialog_active == 1 || global.ntlb_pending >= 0 || global.ntlb_ow_active == 1 || global.interact != 0) {
        if (is_real(_manual) && _manual == 1) ntl_log("ntlb", "[剧情] 当前有其他交互/战斗，忽略");
        return -1;
    }
    var _mc = asset_get_index("obj_mainchara");
    if (_mc < 0 || !instance_exists(_mc)) { ntl_log("ntlb", "[剧情] 玩家尚未就绪，忽略"); return -1; }
    var _man = (is_real(_manual) && _manual == 1);
    if (!_man && ntlb_feature("cutscenesEnabled", 1) == 0) return 0;
    global.ntlb_cut_active = 1;
    global.ntlb_cut_cur = _c;
    global.ntlb_cut_step = 0;
    global.ntlb_cut_frames = 0;
    global.ntlb_cut_sub = 0;
    global.ntlb_cut_actor = noone;
    global.ntlb_cut_sx = 0;
    global.ntlb_cut_sy = 0;
    global.interact = 1;
    ntl_log("ntlb", "[剧情] 开始: " + ntlb_str(_c, "id", "?") + "（" + string(array_length(ntlb_arr(_c, "steps"))) + " 步）");
    ntlb_cutscene_begin_step();
    return 1;
}
""";

    Files["ntlb_cutscene_tick"] = """
function ntlb_cutscene_tick()
{
    if (global.ntlb_cut_active != 1) return 0;
    var _c = global.ntlb_cut_cur;
    if (!is_struct(_c)) { ntlb_cutscene_finish(); return 0; }
    var _steps = ntlb_arr(_c, "steps");
    if (global.ntlb_cut_step >= array_length(_steps)) { ntlb_cutscene_finish(); return 0; }
    var _st = _steps[global.ntlb_cut_step];
    var _kind = ntlb_str(_st, "kind", "wait");
    var _fr = max(1, ntlb_num(_st, "frames", 30));
    if (_kind == "dialogue") {
        if (global.ntlb_cut_sub == 0) {
            var _d = { lines: ntlb_arr(_st, "lines") };
            ntlb_dialogue_build_msgs(_d);
            global.msc = 0;
            global.typer = 5;
            if (variable_global_exists("darkzone") && global.darkzone == 1) global.typer = 6;
            global.fc = 0;
            global.fe = 0;
            var _dg = asset_get_index("obj_dialoguer");
            if (_dg >= 0) instance_create(0, 0, _dg);
            global.ntlb_cut_sub = 1;
            ntl_log("ntlb", "[剧情] 对话 " + string(array_length(ntlb_arr(_st, "lines"))) + " 行");
            return 0;
        }
        var _dg2 = asset_get_index("obj_dialoguer");
        if (_dg2 >= 0 && instance_exists(_dg2)) return 0;
        global.ntlb_cut_sub = 0;
        ntlb_cutscene_next();
        return 0;
    }
    global.ntlb_cut_frames += 1;
    if (_kind == "sound") {
        if (global.ntlb_cut_frames == 1) {
            var _snd = ntlb_str(_st, "sound", "");
            var _si = -1;
            if (_snd != "") _si = asset_get_index(_snd);
            if (_si >= 0) {
                var _gain = ntlb_num(_st, "gain", 1);
                if (_gain <= 0) _gain = 1;
                var _sinst = audio_play_sound(_si, 50, 0);
                if (_sinst >= 0) audio_sound_gain(_sinst, _gain, 0);
                ntl_log("ntlb", "[剧情] 音效 " + _snd + "（增益 " + string(_gain) + "）");
            } else if (_snd != "") {
                ntl_log("ntlb", "[剧情] [警告] 找不到音效: " + _snd);
            }
        }
        if (global.ntlb_cut_frames >= _fr) ntlb_cutscene_next();
        return 0;
    }
    if (_kind == "shake") {
        if (global.ntlb_cut_frames == 1) {
            var _sho = asset_get_index("obj_shake");
            if (_sho >= 0) {
                var _sh = instance_create(0, 0, _sho);
                var _amp = ntlb_num(_st, "x", 4);
                if (_amp <= 0) _amp = 4;
                with (_sh) { shakex = _amp; shakey = _amp; }
                ntl_log("ntlb", "[剧情] 震动 " + string(_amp) + " 像素（" + string(_fr) + " 帧）");
            } else {
                ntl_log("ntlb", "[剧情] [警告] 找不到 obj_shake，忽略震动");
            }
        }
        if (global.ntlb_cut_frames >= _fr) ntlb_cutscene_next();
        return 0;
    }
    if (_kind == "waitinput") {
        if (global.ntlb_cut_frames == 1) ntl_log("ntlb", "[剧情] 等待按键（Z/Enter）");
        if (global.ntlb_cut_frames > 2 && button1_p()) ntlb_cutscene_next();
        return 0;
    }
    if (_kind == "wait") {
        if (global.ntlb_cut_frames >= _fr) ntlb_cutscene_next();
        return 0;
    }
    if (_kind == "move") {
        var _inst = global.ntlb_cut_actor;
        if (_inst != noone && instance_exists(_inst)) {
            var _t = global.ntlb_cut_frames / _fr;
            if (_t > 1) _t = 1;
            _inst.x = lerp(global.ntlb_cut_sx, ntlb_num(_st, "x", global.ntlb_cut_sx), _t);
            _inst.y = lerp(global.ntlb_cut_sy, ntlb_num(_st, "y", global.ntlb_cut_sy), _t);
        }
        if (global.ntlb_cut_frames >= _fr) ntlb_cutscene_next();
        return 0;
    }
    if (_kind == "face") { ntlb_cutscene_next(); return 0; }
    if (_kind == "action") {
        if (global.ntlb_cut_frames == 1) ntlb_cut_action(global.ntlb_cut_actor, _st);
        if (global.ntlb_cut_frames >= _fr) ntlb_cutscene_next();
        return 0;
    }
    if (_kind == "flag") {
        var _set = ntlb_arr(_st, "setFlags");
        for (var _i = 0; _i < array_length(_set); _i += 1) ntlb_setflag(_set[_i], 1);
        var _clr = ntlb_arr(_st, "clearFlags");
        for (var _j = 0; _j < array_length(_clr); _j += 1) ntlb_setflag(_clr[_j], 0);
        ntlb_cutscene_next();
        return 0;
    }
    if (_kind == "message") {
        if (global.ntlb_cut_frames == 1) {
            var _msg = ntlb_str(_st, "message", "");
            if (_msg != "") { ntl_log("ntlb", "[剧情] " + _msg); ntlb_show_text(_msg, 120); }
        }
        if (global.ntlb_cut_frames >= _fr) ntlb_cutscene_next();
        return 0;
    }
    ntlb_cutscene_next();
    return 0;
}
""";

    Files["ntlb_check_cutscenes"] = """
function ntlb_check_cutscenes()
{
    if (!is_array(global.ntlb_cutscenes)) return 0;
    var _n = array_length(global.ntlb_cutscenes);
    if (_n <= 0) return 0;
    if (global.interact != 0) return 0;
    if (ntlb_feature("cutscenesEnabled", 1) == 0) return 0;
    var _rm = string(room_get_name(room));
    var _mc = asset_get_index("obj_mainchara");
    var _px = _mc.x;
    var _py = _mc.y;
    for (var _i = 0; _i < _n; _i += 1) {
        var _c = global.ntlb_cutscenes[_i];
        var _tr = ntlb_get(_c, "trigger");
        if (!is_struct(_tr)) continue;
        var _room = ntlb_str(_tr, "room", "");
        var _rf = ntlb_num(_tr, "requiredFlag", 0);
        var _r = ntlb_num(_tr, "radius", 64);
        if (_room == "" && _rf <= 0) continue;
        if (_room != "" && _room != _rm) continue;
        if (_rf > 0 && ntlb_flag(_rf) == 0) continue;
        var _key = "cut:" + ntlb_str(_c, "id", "c" + string(_i));
        var _persist = ntlb_bool(_tr, "persistOnce", false);
        if (ntlb_bool(_tr, "once", true) && ntlb_once_seen(_key, _persist)) continue;
        var _tx = ntlb_num(_tr, "x", _px);
        var _ty = ntlb_num(_tr, "y", _py);
        if (_r > 0 && point_distance(_px, _py, _tx, _ty) > _r) continue;
        var _mode = ntlb_str(_c, "mode", "interact");
        // area = 走进半径就自动播放；interact = 站进原版交互矩形并按 Z/Enter。
        if (_mode != "area") {
            if (!button1_p()) continue;
            if (!ntlb_in_interact_zone(_tx, _ty)) continue;
        }
        if (ntlb_bool(_tr, "once", true)) ntlb_once_mark(_key, _persist);
        ntl_log("ntlb", "[触发] 剧情 " + ntlb_str(_c, "id", "?") + "（房间 " + _rm + "）");
        return ntlb_start_cutscene(_c, 0);
    }
    return 0;
}
""";

    Files["ntlb_cutscenes_init"] = """
function ntlb_cutscenes_init()
{
    global.ntlb_cutscenes = [];
    global.ntlb_cut_active = 0;
    global.ntlb_cut_cur = undefined;
    global.ntlb_cut_step = 0;
    global.ntlb_cut_frames = 0;
    global.ntlb_cut_sub = 0;
    global.ntlb_cut_actor = noone;
    global.ntlb_cut_sx = 0;
    global.ntlb_cut_sy = 0;
    var _cs = ntlb_arr(global.ntlb_cfg, "cutscenes");
    for (var _i = 0; _i < array_length(_cs); _i += 1) {
        if (ntlb_bool(_cs[_i], "enabled", true)) array_push(global.ntlb_cutscenes, _cs[_i]);
    }
    ntl_log("ntlb", "[剧情] 已加载 " + string(array_length(global.ntlb_cutscenes)) + " 段剧情（控制台: cutscene list）");
    return array_length(global.ntlb_cutscenes);
}
""";

    Files["ntlb_cmd_cutscene"] = """
function ntlb_cmd_cutscene(_rest)
{
    var _raw = string_trim(string(_rest));
    var _a = string_lower(_raw);
    if (_a == "" || _a == "list") {
        ntl_console_log("[cutscene] 已加载 " + string(array_length(global.ntlb_cutscenes)) + " 段剧情：");
        for (var _i = 0; _i < array_length(global.ntlb_cutscenes); _i += 1) {
            var _c = global.ntlb_cutscenes[_i];
            ntl_console_log("  " + string(_i) + ". " + ntlb_str(_c, "id", "?") + "  [" + ntlb_str(_c, "mode", "interact") + "]  " + ntlb_str(_c, "name", ""));
        }
        ntl_console_log("用法: cutscene start <id> | cutscene stop | cutscene list");
        return 0;
    }
    if (string_pos("start ", _a) == 1) {
        var _k = string_trim(string_delete(_raw, 1, 6));
        var _c2 = ntlb_cutscene_find(_k);
        if (!is_struct(_c2)) { ntl_console_log("[cutscene] 找不到: " + _k); return 0; }
        var _r = ntlb_start_cutscene(_c2, 1);
        if (_r != 1) ntl_console_log("[cutscene] 启动失败（code " + string(_r) + "）");
        return 0;
    }
    if (_a == "stop") {
        if (global.ntlb_cut_active == 1) {
            var _dg = asset_get_index("obj_dialoguer");
            if (_dg >= 0) { with (_dg) instance_destroy(); }
            var _wr = asset_get_index("obj_writer");
            if (_wr >= 0) { with (_wr) instance_destroy(); }
            ntlb_cutscene_finish();
        }
        ntl_console_log("[cutscene] 已停止");
        return 0;
    }
    ntl_console_log("[cutscene] 未知子命令: " + _raw + "（list / start <id> / stop）");
    return 0;
}
""";

    // ---------------------------------------------------------------- 控制台命令
    Files["ntlb_cmd"] = """
function ntlb_cmd(_rest)
{
    var _raw = string_trim(string(_rest));
    var _a = string_lower(_raw);
    if (_a == "" || _a == "list") {
        ntl_console_log("[battle] 已加载 " + string(array_length(global.ntlb_battles)) + " 场战斗：");
        for (var _i = 0; _i < array_length(global.ntlb_battles); _i += 1) {
            var _b = global.ntlb_battles[_i];
            ntl_console_log("  " + string(_i) + ". " + ntlb_str(_b, "id", "?") + "  [" + ntlb_str(_b, "kind", "encounter") + "]  " + ntlb_str(_b, "name", ""));
        }
        ntl_console_log("用法: battle start <id> | battle ow <id> | battle info <id> | battle stop");
        return 0;
    }
    if (string_pos("start ", _a) == 1) {
        var _k = string_trim(string_delete(_raw, 1, 6));
        var _b = ntlb_find(_k);
        if (!is_struct(_b)) { ntl_console_log("[battle] 找不到: " + _k); return 0; }
        if (ntlb_str(_b, "kind", "encounter") == "overworld") ntlb_start_overworld(_b);
        else ntlb_start_encounter(_b);
        return 0;
    }
    if (string_pos("ow ", _a) == 1) {
        var _k = string_trim(string_delete(_raw, 1, 3));
        var _b = ntlb_find(_k);
        if (!is_struct(_b)) { ntl_console_log("[battle] 找不到: " + _k); return 0; }
        ntlb_start_overworld(_b);
        return 0;
    }
    if (string_pos("info ", _a) == 1) {
        var _k = string_trim(string_delete(_raw, 1, 5));
        var _b = ntlb_find(_k);
        if (!is_struct(_b)) { ntl_console_log("[battle] 找不到: " + _k); return 0; }
        ntl_console_log("[battle] " + ntlb_str(_b, "id", "?") + " / " + ntlb_str(_b, "name", "") + " / kind=" + ntlb_str(_b, "kind", "encounter"));
        ntl_console_log("  回合数: " + string(array_length(ntlb_arr(_b, "turns"))) + "，上限 " + string(ntlb_num(_b, "roundLimit", 0)));
        ntl_console_log("  敌人: " + string(array_length(ntlb_arr(_b, "enemies"))) + "，进度: pending=" + string(global.ntlb_pending) + " turns=" + string(global.ntlb_turns));
        return 0;
    }
    if (_a == "stop") {
        global.ntlb_pending = -1;
        global.ntlb_pending_battle = undefined;
        global.ntlb_ow_active = 0;
        global.ntlb_ow_battle = undefined;
        global.interact = 0;
        ntl_console_log("[battle] 已停止（若战斗界面仍在，请继续操作结束它）");
        return 0;
    }
    ntl_console_log("[battle] 未知子命令: " + _raw + "（list / start <id> / ow <id> / info <id> / stop）");
    return 0;
}
""";

    // ---------------------------------------------------------------- 补齐批：持久 once / 原版交互几何 / 屏幕文本

    Files["ntlb_once"] = """
function ntlb_once_seen(_key, _persist)
{
    var _k = "ntlb." + string(_key);
    if (!variable_global_exists("ntlb_once")) global.ntlb_once = ds_map_create();
    if (ds_map_exists(global.ntlb_once, _k)) return 1;
    if (_persist) {
        // 跨重启一次性：写 Neutraled 的 mod 共享持久数据（mods-shared.json），重开游戏仍然记得。
        var _v = ntl_mod_data_get(_k);
        if (is_real(_v) && _v != 0) {
            ds_map_add(global.ntlb_once, _k, 1);
            return 1;
        }
    }
    return 0;
}

function ntlb_once_mark(_key, _persist)
{
    var _k = "ntlb." + string(_key);
    if (!variable_global_exists("ntlb_once")) global.ntlb_once = ds_map_create();
    if (!ds_map_exists(global.ntlb_once, _k)) ds_map_add(global.ntlb_once, _k, 1);
    if (_persist) ntl_mod_data_set(_k, 1);
    return 1;
}
""";

    Files["ntlb_in_interact_zone"] = """
function ntlb_in_interact_zone(_tx, _ty)
{
    // 近似原版交互判定：只有站在 obj_mainchara 面前的原版交互矩形内才算（见反编译
    // obj_mainchara_Step_0.gml:475-617 的四向 collision_rectangle，这里换成等价的点判定）。
    // 仍不校验「唯一交互对象」：多个可交互物重叠时可能选错，但比纯半径精确。
    var _mo = asset_get_index("obj_mainchara");
    if (_mo < 0) return 0;
    var _mc = instance_find(_mo, 0);
    if (_mc == noone) return 0;
    if (!variable_global_exists("facing")) return point_distance(_mc.x, _mc.y, _tx, _ty) <= 40;
    var _d = 1;
    if (variable_global_exists("darkzone") && global.darkzone == 1) _d = 2;
    var _x = _mc.x;
    var _y = _mc.y;
    var _sw = _mc.sprite_width;
    var _sh = _mc.sprite_height;
    // global.facing：0=下 1=右 2=上 3=左（原版 mainchara 用它对 sprite）。
    if (global.facing == 1) return point_in_rectangle(_tx, _ty, _x + _sw / 2, _y + 6 * _d + _sh / 2, _x + _sw + 13 * _d, _y + _sh);
    if (global.facing == 3) return point_in_rectangle(_tx, _ty, _x - 13 * _d, _y + 6 * _d + _sh / 2, _x + _sw / 2, _y + _sh);
    if (global.facing == 0) return point_in_rectangle(_tx, _ty, _x + 4 * _d, _y + 28 * _d, _x + _sw - 4 * _d, _y + _sh + 15 * _d);
    if (global.facing == 2) return point_in_rectangle(_tx, _ty, _x + 3, _y + 5 * _d, _x + _sw - 5 * _d, _y + _sh - 5 * _d);
    return 0;
}
""";

    Files["ntlb_show_text"] = """
function ntlb_show_text(_msg, _frames)
{
    // 屏幕文本提示：用一个独立对象（@@TEXT_OBJ@@）在画面上画一段时间；
    // 用在战斗/剧情结束的 message 上（原版对白框只能显示 obj_writer 正文）。
    global.ntlb_text_msg = string(_msg);
    global.ntlb_text_life = max(30, _frames);
    ntl_log("ntlb", "[文本] " + global.ntlb_text_msg);
    var _to = asset_get_index("@@TEXT_OBJ@@");
    if (_to < 0) return 0;
    if (global.ntlb_text_on != 1 || !instance_exists(global.ntlb_text_inst)) {
        global.ntlb_text_inst = instance_create(0, 0, _to);
        global.ntlb_text_on = 1;
    }
    return 1;
}
""";

    Files["ntlb_text_create"] = """
function ntlb_text_create()
{
    // 文本对象本身不存状态（状态在 global.ntlb_text_*），Create 只占位。
    return 0;
}
""";

    Files["ntlb_text_step"] = """
function ntlb_text_step()
{
    if (global.ntlb_text_on != 1) return 0;
    global.ntlb_text_life -= 1;
    if (global.ntlb_text_life <= 0) {
        global.ntlb_text_on = 0;
        global.ntlb_text_inst = noone;
        global.ntlb_text_msg = "";
        instance_destroy();
    }
    return 0;
}
""";

    Files["ntlb_text_draw"] = """
function ntlb_text_draw()
{
    if (global.ntlb_text_on != 1) return 0;
    var _msg = global.ntlb_text_msg;
    if (_msg == "") return 0;
    var _cx = room_width / 2;
    var _cy = 96;
    var _cam = -1;
    if (array_length(view_camera) > 0) _cam = view_camera[0];
    if (_cam >= 0) {
        _cx = camera_get_view_x(_cam) + camera_get_view_width(_cam) / 2;
        _cy = camera_get_view_y(_cam) + 96;
    }
    var _f = asset_get_index("fnt_main");
    if (_f >= 0) draw_set_font(_f);
    draw_set_halign(fa_center);
    draw_set_valign(fa_middle);
    draw_set_color(c_black);
    draw_text(_cx + 1, _cy + 1, _msg);
    draw_set_color(c_white);
    draw_text(_cx, _cy, _msg);
    draw_set_halign(fa_left);
    draw_set_valign(fa_top);
    return 1;
}
""";
    }
}
