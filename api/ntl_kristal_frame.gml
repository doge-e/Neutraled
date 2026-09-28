/// ntl_kristal_frame() —— 每帧调用 Kristal mod 的回调（postUpdate / update）
/// 由 Step_1 的每帧流程调用，让 mod 的逐帧逻辑真正运行
if (!variable_global_exists("ntl_lua_globals")) return 0;
if (!variable_global_exists("ntl_lua_env")) return 0;

var _mod = ntl_lua_table_get(global.ntl_lua_globals, "Mod");
if (_mod == undefined || !is_real(_mod)) return 0;

global.ntl_lua_err = "";
var _called = 0;

// postUpdate（Kristal 的世界更新后回调）
var _pu = ntl_lua_index_get(_mod, "postUpdate");
if (_pu != undefined && (ntl_lua_is_fn(_pu) || is_string(_pu)))
{
    ntl_lua_call(_pu, [_mod]);
    _called += 1;
}

// update（更常见的每帧回调）
var _up = ntl_lua_index_get(_mod, "update");
if (_up != undefined && (ntl_lua_is_fn(_up) || is_string(_up)))
{
    ntl_lua_call(_up, [_mod]);
    _called += 1;
}

if (global.ntl_lua_err != "")
{
    ntl_log("kristal", "[frame] " + global.ntl_lua_err);
    global.ntl_lua_err = "";
}
return _called;
