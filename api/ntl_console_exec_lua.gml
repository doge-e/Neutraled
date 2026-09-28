/// ntl_console_exec_lua(src) —— 执行一段 Lua 代码（可多条语句；与 eval 的区别：这里不求值表达式）
/// 例：exec ntl_console_log("hi")   /   exec local a = 1  （eval 则是 eval 1+2）
var _src = string(argument[0]);
if (string_length(string_trim(_src)) <= 0)
{
    ntl_console_log(ntl_t("cmd.exec.u"));
    return 0;
}

// 与 eval 共用同一个全局 Lua 环境（早期调用时按需建立）
if (!variable_global_exists("ntl_lua_globals"))
{
    global.ntl_lua_globals = ntl_lua_table_new();
    ntl_lua_stdlib();
}
else if (!ds_map_exists(global.ntl_lua_globals, "sprint_check")) ntl_lua_stdlib();
if (!variable_global_exists("ntl_lua_env")) global.ntl_lua_env = ntl_lua_env_new(global.ntl_lua_globals);

global.ntl_lua_err = "";
try
{
    var _lc = ntl_lua_compile(_src);
    if (ds_map_find_value(_lc, "ok") != 1)
    {
        ntl_console_log(ntl_t("out.syntax") + string(ds_map_find_value(_lc, "err")));
        ds_map_destroy(_lc);
        return 0;
    }
    ntl_lua_ev_block(global.ntl_lua_env, ds_map_find_value(_lc, "ast"));
    ds_map_destroy(_lc);
}
catch (e)
{
    ntl_console_log(ntl_t("exec.exc") + string(e));
    return 0;
}
if (global.ntl_lua_err != "")
{
    ntl_console_log(ntl_t("exec.fail") + ntl_err_friendly(global.ntl_lua_err, "<exec>", -1));
    global.ntl_lua_err = "";
}
else ntl_console_log(ntl_t("exec.ok"));
return 0;
