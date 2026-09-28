/// ntl_hook_list(script) —— 取某脚本的 hook 列表（无则 undefined）
if (!variable_global_exists("ntl_hooks")) return undefined;
var _s = string(argument[0]);
if (!ds_map_exists(global.ntl_hooks, _s)) return undefined;
return ds_map_find_value(global.ntl_hooks, _s);
