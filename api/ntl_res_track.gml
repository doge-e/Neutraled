/// ntl_res_track(kind, id, tag) —— 登记动态创建的资源（用于泄漏检测）
/// kind: "sprite" / "surface" / "sound" / "buffer" / "ds"
if (!variable_global_exists("ntl_res_tracked")) global.ntl_res_tracked = ds_map_create();
var _kind = string(argument[0]);
var _id = argument[1];
var _tag = (argument_count > 2) ? string(argument[2]) : "";

if (!ds_map_exists(global.ntl_res_tracked, _kind)) ds_map_add(global.ntl_res_tracked, _kind, ds_map_create());
var _m = ds_map_find_value(global.ntl_res_tracked, _kind);
if (_m == undefined || !is_real(_m) || !ds_exists(_m, ds_type_map)) return 0;
var _key = string(_id);
if (ds_map_exists(_m, _key)) ds_map_replace(_m, _key, _tag);
else ds_map_add(_m, _key, _tag);
return 1;
