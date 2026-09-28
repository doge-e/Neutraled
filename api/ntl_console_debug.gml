/// ntl_console_debug(cmd, rest) —— 调试类命令实现
var _cmd = string(argument[0]);
var _rest = (argument_count > 1) ? string(argument[1]) : "";

if (_cmd == "err")
{
    if (_rest == "clear")
    {
        if (variable_global_exists("ntl_last_errors")) array_resize(global.ntl_last_errors, 0);
        ntl_console_log(ntl_t("dbg.err_cleared"));
        return 0;
    }
    if (!variable_global_exists("ntl_last_errors") || array_length(global.ntl_last_errors) == 0)
    {
        ntl_console_log(ntl_t("dbg.err_none"));
        return 0;
    }
    ntl_console_log(ntl_ts("dbg.err_head", [string(array_length(global.ntl_last_errors))]));
    var _n = array_length(global.ntl_last_errors);
    var _start = max(0, _n - 10);
    for (var _i = _start; _i < _n; _i += 1)
        ntl_console_log("  " + string(global.ntl_last_errors[_i]));
    return 0;
}

if (_cmd == "ctx")
{
    ntl_console_log(ntl_t("dbg.ctx_head"));
    ntl_console_log(ntl_ts("dbg.ctx_mod", [(variable_global_exists("ntl_current_mod") ? string(global.ntl_current_mod) : ntl_t("dbg.none"))]));
    ntl_console_log(ntl_ts("dbg.ctx_event", [(variable_global_exists("ntl_ctx_event") ? string(global.ntl_ctx_event) : ntl_t("dbg.none"))]));
    ntl_console_log(ntl_ts("dbg.ctx_dir", [(variable_global_exists("ntl_lua_mod_dir") ? string(global.ntl_lua_mod_dir) : ntl_t("dbg.none"))]));
    return 0;
}

if (_cmd == "trace")
{
    if (_rest == "on")  { global.ntl_lua_trace = 1; ntl_console_log(ntl_t("dbg.trace_on")); }
    else if (_rest == "off") { global.ntl_lua_trace = 0; ntl_console_log(ntl_t("dbg.trace_off")); }
    else ntl_console_log(ntl_ts("dbg.trace_cur", [((variable_global_exists("ntl_lua_trace") && global.ntl_lua_trace == 1) ? ntl_t("dbg.on") : ntl_t("dbg.off"))]));
    return 0;
}

if (_cmd == "budget")
{
    if (_rest != "")
    {
        var _v = real(_rest);
        if (_v > 0) { global.ntl_live_budget = _v; ntl_console_log(ntl_ts("dbg.budget_set", [string(_v)])); return 0; }
    }
    ntl_console_log(ntl_ts("dbg.budget", [string(global.ntl_live_budget)]));
    ntl_console_log(ntl_t("dbg.budget_hint"));
    return 0;
}

return 0;
