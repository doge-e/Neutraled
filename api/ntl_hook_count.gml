// ntl_hook_count(event_name) —— 某事件的订阅者数量
var _ev = string(argument[0]);
if (!variable_global_exists("ntl_hooks")) return 0;
if (!ds_map_exists(global.ntl_hooks, _ev)) return 0;
var _list = ds_map_find_value(global.ntl_hooks, _ev);
if (is_undefined(_list)) return 0;
return ds_list_size(_list);
