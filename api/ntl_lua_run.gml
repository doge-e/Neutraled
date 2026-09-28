/// ntl_lua_run(src, chunkName) → ds_map { ok, value, err }
/// 执行一段 Lua 源码（自动初始化全局环境与标准库）
var _src = string(argument[0]);
var _name = (argument_count > 1) ? string(argument[1]) : "chunk";

global.ntl_lua_err = "";

// 全局环境
if (!variable_global_exists("ntl_lua_globals"))
{
    global.ntl_lua_globals = ntl_lua_table_new();
    ntl_lua_stdlib();
}
else if (!ds_map_exists(global.ntl_lua_globals, "sprint_check"))
{
    ntl_lua_stdlib();
}

var _out = ds_map_create();
ds_map_add(_out, "ok", 0);
ds_map_add(_out, "value", 0);
ds_map_add(_out, "err", "");

var _c = ntl_lua_compile(_src);
if (ds_map_find_value(_c, "ok") != 1)
{
    ds_map_replace(_out, "err", "compile error: " + string(ds_map_find_value(_c, "err")));
    ntl_log("lua", "[" + _name + "] 编译失败: " + string(ds_map_find_value(_c, "err")));
    return _out;
}

var _env = ntl_lua_env_new(global.ntl_lua_globals);
var _r = ntl_lua_ev_block(_env, ds_map_find_value(_c, "ast"));

if (global.ntl_lua_err != "")
{
    ds_map_replace(_out, "err", global.ntl_lua_err);
    ntl_log("lua", "[" + _name + "] 运行错误: " + global.ntl_lua_err);
    return _out;
}

var _val = undefined;
if (ntl_lua_is_ctrl(_r))
{
    var _vs = ds_map_find_value(_r, "vals");
    if (array_length(_vs) > 0) _val = _vs[0];
}
else _val = _r;

ds_map_replace(_out, "ok", 1);
ds_map_replace(_out, "value", _val);
return _out;
