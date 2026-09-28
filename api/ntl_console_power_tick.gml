/// ntl_console_power_tick() —— 强力指令的每帧驱动（god / 单帧推进 / watch）
if (!variable_global_exists("ntl_paused")) global.ntl_paused = 0;
if (!variable_global_exists("ntl_step_frames")) global.ntl_step_frames = 0;

// 冻结状态下的单帧推进：放开一帧再冻回去
if (global.ntl_paused == 1 && global.ntl_step_frames > 0)
{
    global.ntl_step_frames -= 1;
    try { instance_activate_all(); }
    catch (e) { ntl_log("power", "单帧推进: 激活实例失败 " + string(e)); }
    if (global.ntl_step_frames <= 0)
    {
        try { instance_deactivate_all(true); }
        catch (e) { ntl_log("power", "单帧推进: 重新冻结失败 " + string(e)); }
    }
}

// 延时脚本队列（autorun 里的 sleep）：每帧推进，到点执行
if (variable_global_exists("ntl_script_queue") && array_length(global.ntl_script_queue) > 0)
{
    var _q = global.ntl_script_queue;
    var _keep2 = [];
    for (var _qi = 0; _qi < array_length(_q); _qi += 1)
    {
        var _it = _q[_qi];
        var _left2 = _it[0] - 1;
        if (_left2 <= 0)
        {
            try { ntl_console_exec(string(_it[1])); }
            catch (e)
            {
                ntl_console_log(ntl_ts("au.delay_fail", [_it[1], e]));
                // ★ 缺陷 3 修复：同一错误必须落盘 dr-api.log —— 无人值守的 autorun
                //   （sleep N 之后的命令）失败时只进内存控制台等于无迹可查。
                ntl_log("console", "[延时命令失败] " + string(_it[1]) + " → " + string(e));
            }
        }
        else array_push(_keep2, [_left2, _it[1]]);
    }
    global.ntl_script_queue = _keep2;
}

// freeze：每帧把指定变量锁成指定值（不填值 = 首次记下当前值后保持）
if (variable_global_exists("ntl_freeze") && array_length(global.ntl_freeze) > 0)
{
    var _fzn = array_length(global.ntl_freeze);
    for (var _fi2 = 0; _fi2 < _fzn; _fi2 += 1)
    {
        var _fe2 = global.ntl_freeze[_fi2];
        var _sc2 = string(_fe2[0]);
        var _vr2 = string(_fe2[1]);
        var _vl2 = _fe2[2];
        try
        {
            if (_sc2 == "global")
            {
                if (variable_global_exists(_vr2))
                {
                    if (is_undefined(_vl2)) { _vl2 = variable_global_get(_vr2); global.ntl_freeze[_fi2][2] = _vl2; }
                    variable_global_set(_vr2, _vl2);
                }
            }
            else
            {
                var _oi2 = asset_get_index(_sc2);
                if (_oi2 >= 0)
                {
                    var _in2 = instance_find(_oi2, 0);
                    if (_in2 != noone && variable_instance_exists(_in2, _vr2))
                    {
                        if (is_undefined(_vl2)) { _vl2 = variable_instance_get(_in2, _vr2); global.ntl_freeze[_fi2][2] = _vl2; }
                        variable_instance_set(_in2, _vr2, _vl2);
                    }
                }
            }
        }
        catch (e)
        {
            // 只报一次，避免每帧刷日志
            if (!variable_global_exists("ntl_freeze_err")) global.ntl_freeze_err = "";
            var _emsg = string(e);
            if (global.ntl_freeze_err != _emsg)
            {
                global.ntl_freeze_err = _emsg;
                ntl_log("power", "freeze 锁定失败 " + _sc2 + "." + _vr2 + " → " + _emsg);
            }
        }
    }
}

// 无敌：每帧把 HP 拉满
if (variable_global_exists("ntl_god") && global.ntl_god == 1)
{
    // ★ 2026-09-28 实测事故修复：本 tick 跑在 obj_ntl_core 的 Step 事件里，
    //   ntl_console_exec 的 try/catch 兜不到这里 ⇒ 一旦 hp_fill 抛错，游戏当场 Code Error。
    //   现在出错就自动关掉无敌，并把原因写进控制台与 dr-api.log。
    try { ntl_console_hp_fill(); }
    catch (e)
    {
        global.ntl_god = 0;
        ntl_log("power", "god: 每帧拉满 HP 失败，已自动关闭无敌: " + string(e));
        try { ntl_console_log(ntl_ts("god.auto_off", [string(e)])); } catch (e2) {}
    }
}

// watch：逐帧把变量值写进日志（不动控制台，避免刷屏）
if (variable_global_exists("ntl_watch") && array_length(global.ntl_watch) > 0)
{
    var _keep = [];
    var _wn = array_length(global.ntl_watch);
    for (var _i = 0; _i < _wn; _i += 1)
    {
        var _w = global.ntl_watch[_i];
        var _obj = string(_w[0]);
        var _var = string(_w[1]);
        var _left = _w[2];
        var _val = "(无实例)";
        var _oi = asset_get_index(_obj);
        if (_oi >= 0)
        {
            var _inst = instance_find(_oi, 0);
            if (_inst != noone)
            {
                if (variable_instance_exists(_inst, _var)) _val = string(variable_instance_get(_inst, _var));
                else _val = "(无此变量)";
            }
        }
        ntl_log("watch", _obj + "." + _var + " = " + _val + "   剩余 " + string(_left) + " 帧");
        _left -= 1;
        if (_left > 0) array_push(_keep, [_obj, _var, _left]);
    }
    global.ntl_watch = _keep;
}
return 0;
