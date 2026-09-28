/// ntl_loop_emit(phase) —— 主循环各阶段调用挂载的 mod 脚本
/// 返回 1 = mod 已接管（调用方应跳过默认行为）
var _phase = string(argument[0]);
if (!variable_global_exists("ntl_loop_hooks")) return 0;
if (!ds_map_exists(global.ntl_loop_hooks, _phase)) return 0;

var _h = ds_map_find_value(global.ntl_loop_hooks, _phase);
if (_h == "" || _h == undefined) return 0;

// 支持逗号分隔的多个 handler（按注册顺序）
var _parts = string_split(_h, ",");
for (var _i = 0; _i < array_length(_parts); _i += 1)
{
    var _path = string_trim(_parts[_i]);
    if (_path == "") continue;
    global.ntl_lua_err = "";
    try
    {
        var _src = ntl_live_file_read(_path);
        if (_src == undefined) continue;
        var _ast = ntl_live_compile(_src, _path);
        if (ds_map_find_value(_ast, "ok") != 1) continue;
        ntl_lua_env_declare(global.ntl_lua_env, "phase", _phase);
        ntl_lua_ev_block(global.ntl_lua_env, ds_map_find_value(_ast, "ast"));
    }
    catch (e) { ntl_log("loop", "[异常已隔离] " + string(e)); }
    if (global.ntl_lua_err != "")
    {
        ntl_log("loop", "[错误] " + global.ntl_lua_err);
        global.ntl_lua_err = "";
    }
}
return 1;
