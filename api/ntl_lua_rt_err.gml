/// ntl_lua_rt_err(msg) —— 记录运行时错误，并带上"当前执行上下文"
/// 需要 global.ntl_ctx_module / ntl_ctx_event 来定位（由 run/require/emit 设置）
if (!variable_global_exists("ntl_lua_err")) global.ntl_lua_err = "";
if (global.ntl_lua_err == "")
{
    var _msg = string(argument[0]);

    // 组装上下文
    var _mod = variable_global_exists("ntl_ctx_module") ? string(global.ntl_ctx_module) : "";
    var _evt = variable_global_exists("ntl_ctx_event") ? string(global.ntl_ctx_event) : "";
    var _ctx = "";
    if (_mod != "") _ctx += _mod;
    if (_evt != "") _ctx += (_ctx != "" ? " / " : "") + _evt;

    // 转成中文友好信息
    var _friendly = "";
    try { _friendly = ntl_err_friendly(_msg, _ctx, -1); } catch (e) { _friendly = ""; }
    global.ntl_lua_err = (_friendly != "") ? _friendly : _msg;
}
return 0;
