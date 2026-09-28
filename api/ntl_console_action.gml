/// ntl_console_action(cmd, rest) —— 操作类命令实现
var _cmd = string(argument[0]);
var _rest = (argument_count > 1) ? string(argument[1]) : "";
var _args = ntl_live_split_args(_rest);

if (_cmd == "goto")
{
    var _ch = (array_length(_args) > 0) ? real(_args[0]) : 0;
    if (_ch < 1 || _ch > 7) { ntl_console_log(ntl_t("act.goto_u")); return 0; }
    ntl_console_log(ntl_tf("act.jumping", _ch));
    try { ntl_goto_chapter(_ch); } catch (e) { ntl_console_log(ntl_t("out.fail") + string(e)); }
    return 0;
}

if (_cmd == "loadmap")
{
    if (array_length(_args) == 0) { ntl_console_log(ntl_t("act.loadmap_u")); return 0; }
    var _ok = 0;
    try { ntl_player_test(_args[0]); _ok = 1; } catch (e) { ntl_console_log(ntl_t("out.fail") + string(e)); }
    if (_ok) ntl_console_log(ntl_t("act.map_loaded"));
    return 0;
}

if (_cmd == "spawn")
{
    if (array_length(_args) == 0) { ntl_console_log(ntl_t("act.spawn_u")); return 0; }
    var _x = (array_length(_args) > 1) ? real(_args[1]) : room_width / 2;
    var _y = (array_length(_args) > 2) ? real(_args[2]) : room_height / 2;
    var _inst = ntl_inst_create(_args[0], _x, _y, 0);
    if (_inst == noone) ntl_console_log(ntl_t("out.fail") + ntl_ts("act.no_obj", [_args[0]]));
    else ntl_console_log(ntl_t("act.spawned") + " " + _args[0]);
    return 0;
}

if (_cmd == "destroy")
{
    // ★ 反人类修复：以前 destroy <对象> 直接销毁（可能一次干掉几十个实例）且无任何确认。
    //   现在先报数量，要真销毁必须再带一个 yes：destroy <对象> yes
    //   （脚本/autorun 里的 destroy 也要带 yes —— 已在 docs/CONSOLE.md 写明）
    if (array_length(_args) == 0) { ntl_console_log(ntl_t("act.destroy_u")); return 0; }
    var _list = ntl_inst_find(_args[0]);
    var _n = array_length(_list);
    if (_n == 0) { ntl_console_log(ntl_ts("act.no_inst", [_args[0]])); return 0; }
    if (array_length(_args) < 2 || string_lower(string(_args[1])) != "yes")
    {
        ntl_console_log(ntl_ts("act.destroy_cfm", [string(_n), _args[0]]));
        return 0;
    }
    for (var _i = 0; _i < _n; _i += 1) ntl_inst_destroy(_list[_i]);
    ntl_console_log(ntl_t("act.destroyed") + " " + _args[0] + " x" + string(_n));
    return 0;
}

if (_cmd == "setvar")
{
    if (array_length(_args) < 3) { ntl_console_log(ntl_t("act.setvar_u")); return 0; }
    var _list2 = ntl_inst_find(_args[0]);
    if (array_length(_list2) == 0) { ntl_console_log(ntl_ts("act.no_inst", [_args[0]])); return 0; }
    var _v = _args[2];
    if (is_real(real(_v)) && string(real(_v)) == _v) _v = real(_v);
    ntl_inst_set(_list2[0], _args[1], _v);
    ntl_console_log(ntl_t("act.set") + " " + _args[0] + "." + _args[1] + " = " + string(_v));
    // ★ 反人类修复：只改第一个实例却不说 —— 同类实例有几十个时用户以为全改了
    if (array_length(_list2) > 1) ntl_console_log(ntl_ts("act.setvar_multi", [string(array_length(_list2))]));
    return 0;
}

if (_cmd == "setflag")
{
    if (array_length(_args) < 2) { ntl_console_log(ntl_t("act.setflag_u")); return 0; }
    if (!variable_global_exists("flag")) { ntl_console_log(ntl_t("st.no_flag")); return 0; }
    var _idx = real(_args[0]);
    var _val = real(_args[1]);
    // ★ 反人类修复：只说"编号超范围"，不告诉合法范围
    if (_idx < 0 || _idx >= array_length(global.flag)) { ntl_console_log(ntl_ts("act.idx_range2", [string(array_length(global.flag))])); return 0; }
    global.flag[_idx] = _val;
    ntl_console_log("flag[" + string(_idx) + "] = " + string(_val));
    return 0;
}

if (_cmd == "screenshot")
{
    var _nm = (array_length(_args) > 0) ? _args[0] : ("ntl_cmd_" + string(current_time));
    // ★ 延时截图（用户建议）：screenshot [名字] [秒] —— 塞进 sleep 用的延时队列，
    //   过 N 秒后由队列再执行一次本分支（那时 _delay == 0，直接 screen_save）。
    //   用途：截"几秒后才会出现"的画面；自动化验证时由游戏自己截图，不受窗口焦点影响。
    var _delay = (array_length(_args) > 1) ? real(_args[1]) : 0;
    if (_delay > 0)
    {
        if (!variable_global_exists("ntl_script_queue")) global.ntl_script_queue = [];
        var _fps = 30;
        try { _fps = game_get_speed(gamespeed_fps); } catch (e0) { _fps = 30; }
        if (_fps <= 0) _fps = 30;
        var _frames = max(1, round(_delay * _fps));
        array_push(global.ntl_script_queue, [_frames, "screenshot " + _nm]);
        ntl_console_log(ntl_ts("act.shot_delayed", [string(_delay), _nm]));
        ntl_log("console", "[延时截图] " + string(_delay) + " 秒后（约 " + string(_frames) + " 帧）→ " + _nm + ".png");
        return 0;
    }
    try { screen_save(_nm + ".png"); ntl_console_log(ntl_ts("act.shot_saved", [_nm])); }
    catch (e) { ntl_console_log(ntl_t("out.fail") + string(e)); }
    return 0;
}

return 0;