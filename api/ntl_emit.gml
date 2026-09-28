// ntl_emit(event_name, args_array) —— 广播事件到所有订阅者（handler 以 args 单参调用）
var _ev = string(argument[0]);
var _args = argument[1];
if (is_undefined(_args)) _args = [];
if (!variable_global_exists("ntl_hooks")) return 0;
if (!ds_map_exists(global.ntl_hooks, _ev)) return 0;

var _list = ds_map_find_value(global.ntl_hooks, _ev);
if (is_undefined(_list)) return 0;

var _n = ds_list_size(_list);
var _called = 0;
for (var _i = 0; _i < _n; _i++)
{
    var _fn = ds_list_find_value(_list, _i);
    if (is_undefined(_fn)) continue;
    script_execute(_fn, _args);
    _called += 1;
}
return _called;
