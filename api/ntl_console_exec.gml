// ntl_console_exec(cmdline) —— 解析并执行一条控制台命令
//
// ★ 范围解析（用户要求）：
//   1) 别名（alias 定义的）优先
//   2) 内置命令（ntl 的）—— 不需要前缀
//   3) mod 命令 —— 必须带 "modid:cmd" 前缀，各自在命名空间里
//   4) 都不是 → 打"未识别的命令"并提示用 eval <表达式>（★ 不自动求值；求值入口只有 eval）
var _line = string_trim(string(argument[0]));
if (string_length(_line) <= 0) return 0;

ntl_console_log("> " + _line);

// 解析命令名与参数
var _sp = string_pos(" ", _line);
var _cmd = _line;
var _rest = "";
if (_sp > 0)
{
    _cmd = string_copy(_line, 1, _sp - 1);
    _rest = string_trim(string_delete(_line, 1, _sp));
}

// ---------- 1) 别名展开 ----------
if (variable_global_exists("ntl_console_aliases") && ds_map_exists(global.ntl_console_aliases, _cmd))
{
    var _exp = string(ds_map_find_value(global.ntl_console_aliases, _cmd));
    ntl_console_log(ntl_t("out.alias") + _cmd + " → " + _exp);
    var _parts = string_split(_exp, ";");
    for (var _ai = 0; _ai < array_length(_parts); _ai += 1)
    {
        var _one = string_trim(_parts[_ai]);
        if (_one != "") ntl_console_exec(_one);
    }
    return 0;
}

// ---------- 2) 内置命令分派 ----------

// ===== 信息类 =====
if (_cmd == "help")      { ntl_console_help(_rest); return 0; }
if (_cmd == "cmds")      { ntl_console_cmds(_rest); return 0; }
if (_cmd == "version")   { ntl_console_log("Neutraled API " + global.ntl_version + "  (live " + global.ntl_live_api + ")"); return 0; }
if (_cmd == "api")       { ntl_console_api(_rest); return 0; }
if (_cmd == "vars")      { ntl_console_vars(_rest); return 0; }
if (_cmd == "profile")   { ntl_console_profile(); return 0; }

// 信息类（mods/hooks/modinfo/timing）
if (_cmd == "mods" || _cmd == "hooks" || _cmd == "modinfo" || _cmd == "timing")
{ ntl_console_info(_cmd, _rest); return 0; }

// 状态类
if (_cmd == "room" || _cmd == "inst" || _cmd == "objs" || _cmd == "flags" ||
    _cmd == "player" || _cmd == "maps" || _cmd == "saves" || _cmd == "cache" || _cmd == "world")
{ ntl_console_state(_cmd, _rest); return 0; }

// 操作类
if (_cmd == "goto" || _cmd == "loadmap" || _cmd == "spawn" || _cmd == "destroy" ||
    _cmd == "setvar" || _cmd == "setflag" || _cmd == "screenshot")
{ ntl_console_action(_cmd, _rest); return 0; }
if (_cmd == "reload")    { ntl_console_log(ntl_t("act.reloading")); try { ntl_live_reload(); } catch (e) { ntl_log("console", "[ntl] ntl_console_exec.gml:60 reload 失败: " + string(e)); } return 0; }
if (_cmd == "clear")     { ds_list_clear(global.ntl_console_lines); ntl_console_log(ntl_t("act.cleared")); return 0; }
if (_cmd == "quit")      { global.ntl_console_open = false; return 0; }

// ===== 调试类 =====
if (_cmd == "eval")      { ntl_console_eval(_rest); return 0; }
// ★ 缺陷 3 修复：exec 与 eval 原来都走 ntl_console_eval(_rest)（同一实现），
//   而文档说 exec 执行 Lua 代码、eval 求值表达式。现在 exec 走 ntl_console_exec_lua()：真执行语句块。
if (_cmd == "exec")      { ntl_console_exec_lua(_rest); return 0; }
if (_cmd == "log")       { ntl_console_log(_rest); ntl_log("user", _rest); return 0; }
if (_cmd == "lang")      { ntl_lang_set(_rest); return 0; }
if (_cmd == "filter")    { ntl_console_filter(_rest); return 0; }
// ★ F5 接线 ntl_res_report / ntl_res_untrack —— 资源监控
if (_cmd == "res")
{
    var _resArg = string_lower(string_trim(_rest));
    if (_resArg == "" || _resArg == "all")
    {
        ntl_console_log(ntl_t("exec.res_head"), 1);
        ntl_res_report();
        return 0;
    }
    if (string_pos("untrack ", _resArg) == 1)
    {
        var _ub = string_trim(string_delete(_rest, 1, 8));
        var _us = string_pos(" ", _ub);
        if (_us <= 0)
        {
            ntl_console_log(ntl_t("exec.res_untrack_u"), 1);
            return 0;
        }
        var _uk = string_copy(_ub, 1, _us - 1);
        var _uid = string_delete(_ub, 1, _us);
        ntl_console_log("res untrack " + _uk + " " + _uid + " -> " + string(ntl_res_untrack(_uk, real(_uid))), 1);
        return 0;
    }
    ntl_console_log(ntl_t("exec.res_u"), 1);
    return 0;
}
// ★ F5 接线 ntl_console_help_lookup —— 名字查询
if (_cmd == "whatis")
{
    if (string_trim(_rest) == "") ntl_console_log(ntl_t("exec.whatis_u"), 1);
    else ntl_console_help_lookup(string_trim(_rest));
    return 0;
}
if (_cmd == "save")      { ntl_console_save(_rest); return 0; }
if (_cmd == "style")     { ntl_console_style(_rest); return 0; }
if (_cmd == "timeit")    { ntl_console_timed_exec(_rest); return 0; }
if (_cmd == "hist")
{
    // ★ 反人类修复：原来整段是硬编码英文（"History cleared" / "Command history (N):" / Ctrl+Up/Down 提示），
    //   zh 环境下看不懂；改成 i18n 键，并把 clear 一起落盘（否则下次启动又从文件读回来）。
    var _ha = string_lower(string_trim(_rest));
    if (_ha == "clear")
    {
        global.ntl_console_history = [];
        ntl_console_history_save();
        ntl_console_log(ntl_t("hist.cleared"));
        return 0;
    }
    if (_ha == "save") { ntl_console_history_save(); ntl_console_log(ntl_t("hist.saved")); return 0; }
    var _hh = global.ntl_console_history;
    if (array_length(_hh) == 0) { ntl_console_log(ntl_t("hist.empty")); return 0; }
    var _s2 = max(0, array_length(_hh) - 20);
    ntl_console_log(ntl_ts("hist.head", [string(array_length(_hh)), string(array_length(_hh) - _s2)]));
    for (var _i = _s2; _i < array_length(_hh); _i += 1)
        ntl_console_log("  " + string(_i + 1) + ". " + string(_hh[_i]));
    ntl_console_log(ntl_t("hist.tip"));
    return 0;
}

// 调试类
if (_cmd == "err" || _cmd == "ctx" || _cmd == "trace" || _cmd == "budget")
{ ntl_console_debug(_cmd, _rest); return 0; }

// 自动化类
if (_cmd == "run" || _cmd == "batch") { ntl_console_run(_rest); return 0; }
if (_cmd == "alias" || _cmd == "bind" || _cmd == "macro" || _cmd == "loop")
{
    // ★ 缺陷修复：以前统一传 _cmd + "_split"，但 ntl_console_auto 只认 "alias_split"/"bind_split"
    //   与 "macro"/"loop" ⇒ macro 与 loop 传进去的是 "macro_split"/"loop_split"，两条命令**注册了却执行不了**
    //   （一路掉到"未识别的命令"）。这里按 auto 的实际分支名分派。
    var _autoCmd = (_cmd == "alias" || _cmd == "bind") ? (_cmd + "_split") : _cmd;
    ntl_console_auto(_autoCmd, _rest);
    return 0;
}
if (_cmd == "sleep")
{
    // ★ 缺陷 4 修复：交互态原来只打一行"sleep 只在 autorun 脚本里可用"。
    //   现在 sleep <帧数> <命令> 真的延后执行（复用 power_tick 每帧驱动的 global.ntl_script_queue）。
    var _sa = ntl_live_split_args(_rest);
    if (array_length(_sa) < 1) { ntl_console_log(ntl_t("exec.sleep_u")); return 0; }
    var _fr = real(string(_sa[0]));
    if (!is_real(_fr) || _fr <= 0 || _fr > 36000) { ntl_console_log(ntl_t("exec.sleep_range")); return 0; }
    var _after = string_trim(string_delete(_rest, 1, string_length(string(_sa[0]))));
    if (_after == "")
    {
        ntl_console_log(ntl_t("out.tip") + ntl_t("exec.sleep_hint"));
        return 0;
    }
    if (!variable_global_exists("ntl_script_queue")) global.ntl_script_queue = [];
    array_push(global.ntl_script_queue, [round(_fr), _after]);
    ntl_console_log(ntl_ts("exec.slept", [string(round(_fr)), _after]));
    return 0;
}

// ---------- 2.5) 强力指令（玩家向 + 开发者向）----------
if (ntl_console_power(_cmd, _rest)) return 0;

// ---------- 3) mod 命令（带命名空间前缀）----------
if (variable_global_exists("ntl_console_cmds") && ds_map_exists(global.ntl_console_cmds, _cmd))
{
    var _rec = ds_map_find_value(global.ntl_console_cmds, _cmd);
    if (!is_real(_rec) || !ds_exists(_rec, ds_type_map)) return 0;
    var _scope = string(ds_map_find_value(_rec, "scope"));
    if (_scope == "mod")
    {
        ntl_console_run_mod_cmd(_rec, _cmd, _rest);
        return 0;
    }
    var _fn = ds_map_find_value(_rec, "fn");
    if (is_real(_fn) && _fn != -1) { script_execute(_fn, _rest); return 0; }
}

// ---------- 3.5) 大小写容错（Mods / HELP 这类大写输入）----------
// ★ 反人类修复：命令名大小写敏感，打 "Mods" 只会得到"未识别的命令"且毫无线索。
//   只对**内置**命令做小写重试；mod 命令带 "modid:cmd" 前缀，大小写可能有意义，不动。
var _lc = string_lower(_cmd);
if (_lc != _cmd && variable_global_exists("ntl_console_cmds") && ds_map_exists(global.ntl_console_cmds, _lc))
{
    var _rec2 = ds_map_find_value(global.ntl_console_cmds, _lc);
    if (is_real(_rec2) && ds_exists(_rec2, ds_type_map) && string(ds_map_find_value(_rec2, "scope")) == "builtin")
    {
        ntl_console_log(ntl_ts("msg.case_fix", [_cmd, _lc]));
        ntl_console_exec(_lc + ((_rest != "") ? (" " + _rest) : ""));
        return 0;
    }
}

// ---------- 4) 未识别：给最相近的命令建议（★ 不做隐式求值；求值入口只有 eval）----------
if (string_length(_line) > 0)
{
    ntl_console_log("[" + ntl_t("msg.unknown") + "] " + _cmd);
    ntl_console_log("  · " + ntl_t("msg.see_help"));
    ntl_console_log("  · " + ntl_t("msg.mod_prefix"));
    ntl_console_log("  · " + ntl_t("msg.try_eval") + _line);
    // ★ 反人类修复：打错字原来只给"未识别"，没有任何线索。
    if (variable_global_exists("ntl_console_cmds"))
    {
        var _keys = ntl_dsmap_keys(global.ntl_console_cmds);
        var _best = "";
        var _bestSc = 0;
        for (var _ki = 0; _ki < array_length(_keys); _ki += 1)
        {
            var _kk = string(_keys[_ki]);
            if (string_pos(":", _kk) > 0) continue;   // mod 命名空间命令不参与
            var _sc2 = ntl_console_similar(_cmd, _kk);
            if (_sc2 > _bestSc) { _bestSc = _sc2; _best = _kk; }
        }
        if (_best != "" && _bestSc >= 3) ntl_console_log("  · " + ntl_ts("msg.did_you_mean", [_best]));
    }
}
return 0;
