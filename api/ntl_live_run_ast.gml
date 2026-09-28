/// ntl_live_run_ast(ast, env, label) —— 执行已编译的 AST
var _ast = argument[0];

// ---- Lua 脚本（由 ntl_live_compile 置标志识别）----
if (variable_global_exists("ntl_last_was_lua") && global.ntl_last_was_lua == 1)
{
    if (!variable_global_exists("ntl_lua_globals")) { global.ntl_lua_globals = ntl_lua_table_new(); ntl_lua_stdlib(); }
    if (!variable_global_exists("ntl_lua_env")) global.ntl_lua_env = ntl_lua_env_new(global.ntl_lua_globals);
    global.ntl_lua_err = "";
    // ★ 崩溃隔离
    try
    {
        ntl_lua_ev_block(global.ntl_lua_env, ds_map_find_value(_ast, "ast"));
    }
    catch (_e)
    {
        ntl_log("live", "[Lua 解释器异常已隔离] " + string(_e));
    }
    if (global.ntl_lua_err != "") { ntl_log("live", "[Lua 运行错误] " + global.ntl_lua_err); global.ntl_lua_err = ""; }
    return 0;
}
var _env = argument[1];
var _label = argument[2];

if (_ast == undefined) return false;
global.ntl_live_budget = 200000;

var _r = 0;
try { _r = ntl_ex(_ast, _env); }
catch (e)
{
    var _msg = "unknown";
    try { _msg = string(e.message); } catch (e2) { ntl_log("live", "[ntl] ntl_live_run_ast.gml:33 读取异常消息失败: " + string(e2)); }
    ntl_log("live", "[运行时错误] " + string(_label) + ": " + _msg);
    _r = -1;
}
return (_r >= 0);
