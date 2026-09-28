/// ntl_hook_run_lua(src, name, args, script, mode) —— 执行 Lua hook
///  hook 脚本约定：可定义 function hook(args, script, mode) 或直接写顶层代码
var _src = string(argument[0]);
var _name = string(argument[1]);
var _args = argument[2];
var _script = string(argument[3]);
var _mode = string(argument[4]);

global.ntl_lua_err = "";
// 编译（ntl_lua_compile 内部已有 AST 缓存）
var _c = ntl_lua_compile(_src);
if (ds_map_find_value(_c, "ok") != 1)
{
    ntl_log("hook", "[Lua 语法错误] " + _name + ": " + string(ds_map_find_value(_c, "err")));
    ds_map_destroy(_c);
    return undefined;
}

var _env = ntl_lua_env_new(global.ntl_lua_globals);

// 参数表
var _targs = ntl_lua_table_new();
for (var _i = 0; _i < array_length(_args); _i += 1) ntl_lua_table_set(_targs, _i + 1, _args[_i]);
ntl_lua_env_declare(_env, "args", _targs);
ntl_lua_env_declare(_env, "script_name", _script);
ntl_lua_env_declare(_env, "hook_mode", _mode);
ntl_lua_env_declare(_env, "argc", array_length(_args));
// 位置参数 a1..a8（方便直接使用）
for (var _j = 0; _j < array_length(_args) && _j < 8; _j += 1)
    ntl_lua_env_declare(_env, "a" + string(_j + 1), _args[_j]);

var _r = ntl_lua_ev_block(_env, ds_map_find_value(_c, "ast"));
ds_map_destroy(_c);

// 若定义了 hook 函数则调用它
var _hookFn = ntl_lua_env_get(_env, "hook");
if (ntl_lua_is_fn(_hookFn)) _r = ntl_lua_call(_hookFn, [_targs, _script, _mode]);

if (global.ntl_lua_err != "")
{
    ntl_log("hook", "[Lua 运行错误] " + _name + ": " + global.ntl_lua_err);
    global.ntl_lua_err = "";
    return undefined;
}
return _r;
