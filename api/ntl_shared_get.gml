/// ntl_shared_get(key) —— 读取跨 mod 共享状态
if (!variable_global_exists("ntl_shared")) return undefined;
var _k = string(argument[0]);
if (!ds_map_exists(global.ntl_shared, _k)) return undefined;
return ds_map_find_value(global.ntl_shared, _k);
