/// ntl_lua_ev_block(env, stmts) —— 顺序执行语句块，遇控制流信号立即返回
/// ★ 崩溃隔离：最外层包 try/catch，任何解释器内部异常都被拦住，不会崩溃游戏。
///   用 global.ntl_lua_depth 计数，只有最外层才建立 try/catch（避免递归时的性能开销）。
var _env = argument[0];
var _stmts = argument[1];
if (_stmts == undefined) return undefined;
var _last = undefined;

// 可选跟踪（config: debug_live 或 set_global("ntl_lua_trace", 1)）
var _trace = (variable_global_exists("ntl_lua_trace") && global.ntl_lua_trace == 1) ? 1 : 0;

if (!variable_global_exists("ntl_lua_depth")) global.ntl_lua_depth = 0;
global.ntl_lua_depth += 1;
var _isOuter = (global.ntl_lua_depth == 1);

if (_isOuter)
{
    // ===== 最外层：崩溃隔离 =====
    try
    {
        for (var _i = 0; _i < array_length(_stmts); _i += 1)
        {
            if (_trace) ntl_log("lua", "[trace] stmt " + string(_i) + " k=" + string(ds_map_find_value(_stmts[_i], "k")));
            var _r = ntl_lua_ev_stat(_env, _stmts[_i]);
            if (ntl_lua_is_ctrl(_r)) { global.ntl_lua_depth -= 1; return _r; }
            _last = _r;
        }
    }
    catch (_e)
    {
        global.ntl_lua_depth = 0;
        ntl_log("lua", "===== Lua 解释器异常已被隔离（游戏继续运行）=====");
        ntl_log("lua", "  错误: " + string(_e));
        if (variable_global_exists("ntl_ctx_module"))
            ntl_log("lua", "  模块: " + string(global.ntl_ctx_module));
        if (variable_global_exists("ntl_ctx_event"))
            ntl_log("lua", "  事件: " + string(global.ntl_ctx_event));
        ntl_log("lua", "  建议: 在控制台执行 reload 重载脚本，或检查该脚本最近改动");
        global.ntl_lua_err = "";
        return undefined;
    }
    global.ntl_lua_depth -= 1;
    return _last;
}

// ===== 内层：正常执行（不需要重复 try/catch）=====
for (var _j = 0; _j < array_length(_stmts); _j += 1)
{
    if (_trace) ntl_log("lua", "[trace] stmt " + string(_j) + " k=" + string(ds_map_find_value(_stmts[_j], "k")));
    var _rr = ntl_lua_ev_stat(_env, _stmts[_j]);
    if (ntl_lua_is_ctrl(_rr)) { global.ntl_lua_depth -= 1; return _rr; }
    _last = _rr;
}
global.ntl_lua_depth -= 1;
return _last;
