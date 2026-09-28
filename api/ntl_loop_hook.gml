/// ntl_loop_hook(phase, handler) —— 主循环接管
///   phase: "step" / "draw" / "begin" / "end"
/// mod 用它可以完全掌控每一帧（比如替换整个渲染流程）
/// handler 为 "" 时取消注册
var _phase = string(argument[0]);
var _handler = string(argument[1]);

if (!variable_global_exists("ntl_loop_hooks")) global.ntl_loop_hooks = ds_map_create();
ds_map_replace(global.ntl_loop_hooks, _phase, _handler);
if (_handler == "") ntl_log("loop", "已取消 " + _phase + " 接管");
else ntl_log("loop", "已接管 " + _phase + " -> " + _handler);
return 1;
