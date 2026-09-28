/// ntl_require_lua(moduleName) —— 供 NTL Script 调用的 Lua 模块加载
/// 返回 ds_map { ok: 0/1, value: 模块返回值 }
var _name = string(argument[0]);
var _out = ds_map_create();
ds_map_add(_out, "ok", 0);
ds_map_add(_out, "value", undefined);

// 确保 Lua 环境就绪
if (!variable_global_exists("ntl_lua_globals"))
{
    global.ntl_lua_globals = ntl_lua_table_new();
    ntl_lua_stdlib();
    ntl_lua_stdlib2();
}
if (!variable_global_exists("ntl_lua_env"))
    global.ntl_lua_env = ntl_lua_env_new(global.ntl_lua_globals);
if (!variable_global_exists("ntl_love_registered"))
{
    ntl_love_init();
    ntl_kristal_init();
    ntl_kristal_class_init();
    global.ntl_love_registered = 1;
}

global.ntl_lua_err = "";
var _v = ntl_lua_require(_name);
if (global.ntl_lua_err == "")
{
    ds_map_replace(_out, "ok", 1);
    ds_map_replace(_out, "value", _v);
}
else
{
    ds_map_add(_out, "err", global.ntl_lua_err);
    global.ntl_lua_err = "";
}
return _out;
