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
        // ★ 2026-11-04 修复：参数顺序原来是 (src, path)（签名是 path, src, cache），
        //   cache=undefined → ntl_live_compile.gml:7 ds_map_exists(undefined) 直接 Code Error，
        //   于是 loop 钩子**从来跑不起来**（错被 catch 成「[异常已隔离]」）。
        //   同时原来用 ds_map_find_value(_ast,"ok") 判语法错误 —— Lua 编译结果没有 "ok" 键，
        //   即使编译成功也会 continue。现在统一走 ntl_lua_fastpath（按 路径+mtime 缓存 AST，
        //   每帧 hook 不再重复读盘/解析），并按 Lua/NTL 分派执行。
        var _fp = ntl_lua_fastpath(_path);
        if (_fp == undefined) continue;
        var _ast = ds_map_find_value(_fp, "ast");
        global.ntl_last_was_lua = ds_map_find_value(_fp, "lua");
        var _env2 = undefined;
        if (global.ntl_last_was_lua == 1)
        {
            if (!variable_global_exists("ntl_lua_globals")) { global.ntl_lua_globals = ntl_lua_table_new(); ntl_lua_stdlib(); }
            if (!variable_global_exists("ntl_lua_env")) global.ntl_lua_env = ntl_lua_env_new(global.ntl_lua_globals);
            _env2 = global.ntl_lua_env;
            ntl_lua_env_declare(_env2, "phase", _phase);
        }
        else
        {
            _env2 = ntl_e_new_env();
            ntl_e_env_set(_env2, "phase", _phase);
        }
        ntl_live_run_ast(_ast, _env2, _path);
    }
    catch (e) { ntl_log("loop", "[异常已隔离] " + string(e)); }
    if (global.ntl_lua_err != "")
    {
        ntl_log("loop", "[错误] " + global.ntl_lua_err);
        global.ntl_lua_err = "";
    }
}
return 1;
