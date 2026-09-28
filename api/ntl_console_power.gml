/// ntl_console_power(cmd, rest) —— 强力指令（玩家向 + 开发者向）分派
/// 返回 1 = 已处理（调用方直接 return）
///
/// 玩家向：speed 游戏速度 / warp 直跳房间 / hp 血量 / god 无敌
/// 开发者向：pause resume step 单帧 / dump 房间快照 / watch 变量跟踪 / crash 崩溃隔离自测 / autorun 脚本
var _cmd = string(argument[0]);
var _rest = (argument_count > 1) ? string(argument[1]) : "";
var _args = ntl_live_split_args(_rest);

// ==================== 玩家向 ====================

// ---- speed [倍率]：游戏速度倍率（0.25 ~ 8；speed 1 = 还原）----
if (_cmd == "speed")
{
    if (!variable_global_exists("ntl_speed_orig")) global.ntl_speed_orig = game_get_speed(gamespeed_fps);
    var _cur = game_get_speed(gamespeed_fps);
    if (array_length(_args) == 0)
    {
        ntl_console_log(ntl_ts("speed.cur", [string(_cur), string(global.ntl_speed_orig)]));
        ntl_console_log(ntl_t("speed.u"));
        return 1;
    }
    var _mul = real(_args[0]);
    if (_mul <= 0)
    {
        ntl_console_log(ntl_t("out.fail") + ntl_t("speed.bad"));
        return 1;
    }
    if (_mul > 8) _mul = 8;
    if (_mul < 0.25) _mul = 0.25;
    var _fps = max(1, round(global.ntl_speed_orig * _mul));
    try
    {
        game_set_speed(_fps, gamespeed_fps);
        ntl_console_log(ntl_ts("speed.ok", [string(_mul), string(_cur), string(_fps)]));
    }
    catch (e) { ntl_console_log(ntl_t("out.fail") + string(e)); }
    return 1;
}

// ---- warp <房间名>：直接跳房间（比 goto 章节更激进）----
if (_cmd == "warp")
{
    if (array_length(_args) == 0) { ntl_console_log(ntl_t("warp.u")); return 1; }
    var _rn = _args[0];
    var _rid = asset_get_index(_rn);
    if (_rid < 0 || !room_exists(_rid))
    {
        ntl_console_log(ntl_t("out.fail") + ntl_ts("warp.no", [_rn]));
        return 1;
    }
    ntl_console_log(ntl_ts("warp.ok", [_rn, string(_rid)]));
    try { room_goto(_rid); } catch (e) { ntl_console_log(ntl_ts("warp.roomfail", [e])); }
    return 1;
}

// ---- hp [值]：查看/设置玩家血量（best-effort：自动找常见 HP 变量）----
if (_cmd == "hp")
{
    var _m = ntl_console_hp_find();
    if (!is_real(_m) || _m == -1)
    {
        ntl_console_log(ntl_t("out.fail") + ntl_t("hp.no"));
        ntl_console_log(ntl_t("hp.hint"));
        return 1;
    }
    var _cur = ntl_console_hp_get();
    if (array_length(_args) == 0)
    {
        ntl_console_log(ntl_ts("hp.cur", [string(_cur), string(ds_map_find_value(_m, "where"))]));
        return 1;
    }
    var _v = real(_args[0]);
    if (_v < 0) _v = 0;
    ntl_console_hp_set(_v);
    ntl_console_log("HP: " + string(_cur) + " -> " + string(_v));
    return 1;
}

// ---- god：无敌开关（每帧把 HP 拉满）----
if (_cmd == "god")
{
    if (!variable_global_exists("ntl_god")) global.ntl_god = 0;
    if (global.ntl_god == 0)
    {
        var _m2 = ntl_console_hp_find();
        if (!is_real(_m2) || _m2 == -1)
        {
            ntl_console_log(ntl_t("out.fail") + ntl_t("god.no"));
            return 1;
        }
        global.ntl_god = 1;
        global.ntl_god_where = string(ds_map_find_value(_m2, "where"));
        ntl_console_log(ntl_ts("god.on", [global.ntl_god_where]));
    }
    else
    {
        global.ntl_god = 0;
        ntl_console_log(ntl_t("god.off"));
    }
    return 1;
}

// ==================== 开发者向 ====================

// ---- pause / resume：冻结 / 恢复游戏逻辑 ----
if (_cmd == "pause")
{
    global.ntl_paused = 1;
    global.ntl_step_frames = 0;
    try { instance_deactivate_all(true); } catch (e) { ntl_console_log(ntl_t("out.fail") + string(e)); return 1; }
    ntl_console_log(ntl_t("pause.ok"));
    return 1;
}
if (_cmd == "resume")
{
    global.ntl_paused = 0;
    global.ntl_step_frames = 0;
    try { instance_activate_all(); } catch (e) { ntl_console_log(ntl_t("out.fail") + string(e)); return 1; }
    ntl_console_log(ntl_t("resume.ok"));
    return 1;
}
// ---- step [n]：冻结状态下推进 n 帧 ----
if (_cmd == "step")
{
    var _n = (array_length(_args) > 0) ? max(1, floor(real(_args[0]))) : 1;
    if (_n > 600) _n = 600;
    global.ntl_paused = 1;
    global.ntl_step_frames = _n;
    ntl_console_log(ntl_ts("step.ok", [string(_n)]));
    return 1;
}

// ---- dump [名字]：导出当前房间快照到 Neutraled/logs/ ----
if (_cmd == "dump")
{
    var _nm = (array_length(_args) > 0) ? _args[0] : ("room-" + string(room));
    // ★ 反人类修复：原来打相对路径（Neutraled/logs/...），用户不知道文件在哪；改成绝对路径。
    var _path = program_directory + "Neutraled/logs/dump-" + _nm + ".txt";
    ntl_ensure_dir(_path);
    var _out = ntl_t("dump.title") + "\n";
    _out += ntl_ts("dump.room", [room_get_name(room), string(room)]) + "\n";
    _out += ntl_ts("dump.size", [string(room_width), string(room_height)]) + "\n";
    _out += ntl_ts("dump.inst", [string(instance_count)]) + "\n\n";
    var _cnt = 0;
    for (var _i = 0; _i < instance_count; _i += 1)
    {
        var _in = instance_find(all, _i);
        if (_in == noone) continue;
        _out += object_get_name(_in.object_index) + "  x=" + string(round(_in.x)) + " y=" + string(round(_in.y)) + "  id=" + string(_in.id) + "\n";
        _cnt += 1;
    }
    var _ok = 0;
    try
    {
        var _f = file_text_open_write(_path);
        file_text_write_string(_f, _out);
        file_text_close(_f);
        _ok = 1;
    }
    catch (e) { ntl_console_log(ntl_t("out.fail") + ntl_ts("dump.err", [string(e)])); }
    if (_ok) ntl_console_log(ntl_ts("dump.ok", [_path, string(_cnt)]));
    return 1;
}

// ---- watch <对象> <变量> [帧数] / watch clear：逐帧跟踪变量到日志 ----
if (_cmd == "watch")
{
    if (!variable_global_exists("ntl_watch")) global.ntl_watch = [];
    if (array_length(_args) == 0 || _args[0] == "list")
    {
        ntl_console_log(ntl_ts("watch.head", [string(array_length(global.ntl_watch))]));
        for (var _wi = 0; _wi < array_length(global.ntl_watch); _wi += 1)
        {
            var _w = global.ntl_watch[_wi];
            ntl_console_log(ntl_ts("watch.line", [string(_w[0]), string(_w[1]), string(_w[2])]));
        }
        ntl_console_log(ntl_t("watch.u"));
        return 1;
    }
    if (_args[0] == "clear") { global.ntl_watch = []; ntl_console_log(ntl_t("watch.cleared")); return 1; }
    if (array_length(_args) < 2) { ntl_console_log(ntl_t("watch.u2")); return 1; }
    var _frames = (array_length(_args) > 2) ? max(1, floor(real(_args[2]))) : 60;
    array_push(global.ntl_watch, [_args[0], _args[1], _frames]);
    ntl_console_log(ntl_ts("watch.start", [_args[0], _args[1], string(_frames)]));
    return 1;
}

// ---- crash [yes|1|2|3]：故意触发运行时错误，验证崩溃隔离 ----
if (_cmd == "crash")
{
    // ★ 反人类修复：以前光打一个 "crash" 就**立刻把游戏崩掉**（默认 kind=1），没有任何确认。
    //   现在：crash 单独输入只是确认提示；crash yes 或 crash 1|2|3 才真的触发。
    if (array_length(_args) == 0)
    {
        ntl_console_log(ntl_t("crash.confirm"));
        return 1;
    }
    var _kind = 1;
    if (string_lower(string(_args[0])) != "yes") _kind = floor(real(_args[0]));
    if (_kind != 1 && _kind != 2 && _kind != 3) _kind = 1;
    ntl_console_log(ntl_t("out.dev") + ntl_ts("crash.dev", [string(_kind)]));
    ntl_log("crash", "console crash test kind=" + string(_kind));
    if (_kind == 2)
    {
        var _arr = [];
        var _bad = _arr[99];
        ntl_console_log(ntl_t("crash.unreachable") + string(_bad));
    }
    else if (_kind == 3)
    {
        var _badid = asset_get_index("__ntl_no_such_object__");
        instance_create_depth(0, 0, 0, _badid);
    }
    else
    {
        var _m3 = -12345;
        ds_map_find_value(_m3, "boom");
    }
    return 1;
}

// ---- freeze <对象|global> <变量> [值]：每帧把变量锁成某值（不填值 = 锁住当前值）----
//   比 hp/god 更通用：不依赖猜游戏内部命名，知道变量名就能锁（HP、坐标、计时、flag 都行）
if (_cmd == "freeze")
{
    if (!variable_global_exists("ntl_freeze")) global.ntl_freeze = [];
    if (array_length(_args) == 0 || _args[0] == "list")
    {
        ntl_console_log(ntl_ts("freeze.head", [string(array_length(global.ntl_freeze))]));
        for (var _fi = 0; _fi < array_length(global.ntl_freeze); _fi += 1)
        {
            var _fe = global.ntl_freeze[_fi];
            ntl_console_log("  " + string(_fe[0]) + "." + string(_fe[1]) + " = " + ((is_undefined(_fe[2])) ? ntl_t("freeze.cur") : string(_fe[2])));
        }
        ntl_console_log(ntl_t("freeze.u"));
        return 1;
    }
    if (_args[0] == "clear") { global.ntl_freeze = []; ntl_console_log(ntl_t("freeze.cleared")); return 1; }
    if (array_length(_args) < 2) { ntl_console_log(ntl_t("freeze.u2")); return 1; }
    var _fscope = _args[0];
    var _fvar = _args[1];
    var _fval = undefined;
    if (array_length(_args) > 2)
    {
        _fval = _args[2];
        if (is_real(real(_fval)) && string(real(_fval)) == string(_fval)) _fval = real(_fval);
    }
    // 立刻校验一次，避免锁一个不存在的东西还以为生效了
    var _okf = 0;
    if (_fscope == "global") { if (variable_global_exists(_fvar)) _okf = 1; }
    else
    {
        var _foi = asset_get_index(_fscope);
        if (_foi >= 0) { var _finst = instance_find(_foi, 0); if (_finst != noone && variable_instance_exists(_finst, _fvar)) _okf = 1; }
    }
    if (_okf == 0)
    {
        ntl_console_log(ntl_t("out.fail") + ntl_ts("freeze.nf", [_fscope, _fvar]));
        return 1;
    }
    array_push(global.ntl_freeze, [_fscope, _fvar, _fval]);
    ntl_console_log((is_undefined(_fval)) ? ntl_ts("freeze.ok_cur", [_fscope, _fvar]) : ntl_ts("freeze.ok_val", [_fscope, _fvar, string(_fval)]));
    return 1;
}

// ---- autorun [文件]：执行启动脚本 ----
if (_cmd == "autorun") { ntl_console_autorun(_rest); return 1; }

return 0;
