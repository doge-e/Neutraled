/// ntl_mod_emit(eventName, data) —— 跨 mod 事件广播
///
/// 用法：
///   发送方：  ntl_mod_emit("boss_defeated", { name = "snowlver" })
///   接收方：  ntl_mod_on("boss_defeated", function(d) print(d.name) end)
///
/// 事件名建议加 mod 前缀避免碰撞："mymod:boss_defeated"
var _evt = string(argument[0]);
var _data = (argument_count > 1) ? argument[1] : undefined;

if (!variable_global_exists("ntl_mod_events")) ntl_mod_registry_init();
if (!ds_map_exists(global.ntl_mod_events, _evt)) return 0;

var _subs = ds_map_find_value(global.ntl_mod_events, _evt);
var _n = ds_list_size(_subs);
var _called = 0;
for (var _i = 0; _i < _n; _i += 1)
{
    var _sub = ds_list_find_value(_subs, _i);
    var _fn = ds_map_find_value(_sub, "fn");
    var _who = ds_map_find_value(_sub, "mod");
    global.ntl_lua_err = "";
    var _oldCtx = variable_global_exists("ntl_ctx_module") ? global.ntl_ctx_module : "";
    global.ntl_ctx_module = _who;
    try
    {
        if (ntl_lua_is_fn(_fn)) ntl_lua_call(_fn, [_data]);
        _called += 1;
    }
    catch (e)
    {
        ntl_log("mod", "[互操作] 事件 " + _evt + " 的订阅者 " + _who + " 出错（已隔离）: " + string(e));
    }
    global.ntl_ctx_module = _oldCtx;
    if (global.ntl_lua_err != "")
    {
        ntl_log("mod", "[互操作] " + _who + " 处理 " + _evt + " 时报错: " + global.ntl_lua_err);
        global.ntl_lua_err = "";
    }
}
ntl_log("mod", "[互操作] 事件 " + _evt + " 已通知 " + string(_called) + " 个 mod");
return _called;
