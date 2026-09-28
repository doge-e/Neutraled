/// ntl_res_untrack(kind, id) —— 注销资源（释放时调用）
if (!variable_global_exists("ntl_res_tracked")) return 0;
var _kind = string(argument[0]);
if (!ds_map_exists(global.ntl_res_tracked, _kind)) return 0;
var _m = ds_map_find_value(global.ntl_res_tracked, _kind);
if (!is_real(_m) || !ds_exists(_m, ds_type_map)) return 0;
var _key = string(argument[1]);
if (ds_map_exists(_m, _key)) { ds_map_delete(_m, _key); return 1; }
return 0;
