/// ntl_bh_invoke(rec, args, origValue) —— 调用一个内置函数 hook
/// 返回 [handled, value]
var _rec = argument[0];
var _args = argument[1];
var _orig = (argument_count > 2) ? argument[2] : undefined;

global.ntl_lua_err = "";
var _v = undefined;
try { _v = ntl_hook_run_lua(ds_map_find_value(_rec, "script"), ds_map_find_value(_rec, "handler"), _args, _rec); }
catch (_e) { ntl_log("bh", "[hook 异常已隔离] " + string(_e)); return [0, undefined]; }

if (global.ntl_lua_err != "")
{
    ntl_log("bh", "[hook 错误] " + string(global.ntl_lua_err));
    global.ntl_lua_err = "";
    return [0, undefined];
}

// nil → 不接管
if (_v == undefined) return [0, undefined];

// 非 nil 且是 override/pre → 接管；post → 替换返回值
var _mode = ds_map_find_value(_rec, "mode");
if (_mode == "post") return [0, undefined];   // post 的返回值在包装里处理
return [1, _v];
