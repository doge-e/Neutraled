/// ntl_mod_data_get(key) —— 读 mod 间共享的持久数据
if (!variable_global_exists("ntl_mod_data")) return undefined;
var _key = string(argument[0]);
if (!ds_map_exists(global.ntl_mod_data, _key)) return undefined;
return ds_map_find_value(global.ntl_mod_data, _key);
