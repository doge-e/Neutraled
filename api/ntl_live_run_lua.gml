/// ntl_live_run_lua(astNode, env, label) —— 执行 Lua AST（由 emit 按文件扩展名调用）
var _ast = argument[0];
var _label = (argument_count > 2) ? string(argument[2]) : "lua";

if (!variable_global_exists("ntl_lua_globals"))
{
    global.ntl_lua_globals = ntl_lua_table_new();
    ntl_lua_stdlib();
}
ntl_lua_stdlib2();   // 元表 / require / os 等补充库
if (!variable_global_exists("ntl_love_registered"))
{
    ntl_love_init();      // LOVE2D API 桥接
    ntl_kristal_init();   // Kristal 引擎兼容层
    try { ntl_kristal_world_init(); } catch (e) { ntl_log("live", "[ntl] ntl_live_run_lua.gml:15 ntl_kristal_world_init 失败: " + string(e)); }     // Game.world 映射
    try { ntl_kristal_battle_init(); } catch (e) { ntl_log("live", "[ntl] ntl_live_run_lua.gml:16 ntl_kristal_battle_init 失败: " + string(e)); }    // 战斗/对话桥接
    try { ntl_mod_registry_init(); } catch (e) { ntl_log("live", "[ntl] ntl_live_run_lua.gml:17 ntl_mod_registry_init 失败: " + string(e)); }      // mod 互操作注册表
    ntl_kristal_class_init();   // Kristal 类系统 + 基类
    global.ntl_love_registered = 1;
}
if (!variable_global_exists("ntl_lua_env"))
{
    global.ntl_lua_env = ntl_lua_env_new(global.ntl_lua_globals);
}

global.ntl_lua_err = "";
// 错误定位：当前脚本/mod（只改 ctx_module 用于报错，不动 ntl_current_mod）
if (!variable_global_exists("ntl_ctx_module") || string(global.ntl_ctx_module) == "")
    global.ntl_ctx_module = _label;
// 注入事件参数（on_frame 的帧号 / on_room_load 的房间号等）
var _argVal = undefined;
if (argument_count > 3) _argVal = argument[3];
if (_argVal != undefined)
{
    ntl_lua_env_declare(global.ntl_lua_env, "arg", _argVal);
    ntl_lua_env_declare(global.ntl_lua_env, "event_arg", _argVal);
}
if (variable_global_exists("ntl_lua_mod_dir"))
    ntl_lua_env_declare(global.ntl_lua_env, "__mod_dir", global.ntl_lua_mod_dir);

var _body = ds_map_find_value(_ast, "ast");
if (!is_array(_body)) { ntl_log("lua", "[run_lua] AST 不是语句数组!"); return false; }

// ★★★ mod 崩溃隔离：Lua 执行整段包在 try/catch 里
//     否则任何 GML 层异常（如解释器内部错误）会直接崩溃整个游戏
try
{
    ntl_lua_ev_block(global.ntl_lua_env, _body);
}
catch (_e)
{
    ntl_log("lua", "[" + _label + "] **解释器异常已被隔离**（游戏继续运行）");
    ntl_log("lua", "        错误: " + string(_e));
    ntl_log("lua", "        上下文: " + (variable_global_exists("ntl_ctx_module") ? string(global.ntl_ctx_module) : "?") +
                    " / " + (variable_global_exists("ntl_ctx_event") ? string(global.ntl_ctx_event) : "?"));
    ntl_log("lua", "        建议: 检查该脚本最近的修改；可先在控制台执行 reload 重载");
    global.ntl_lua_err = "";
    return false;
}

if (global.ntl_lua_err != "")
{
    ntl_log("lua", "[" + _label + "] 运行错误: " + global.ntl_lua_err);
    global.ntl_lua_err = "";
    return false;
}
return true;
