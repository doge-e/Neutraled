/// ntl_map_objects(map, groupName) —— 取某对象组的对象列表（ds_map 数组）
/// 组名如 "markers" / "objects" / "collision"
var _mp = argument[0];
var _gn = (argument_count > 1) ? string(argument[1]) : "";
if (_mp == undefined || !is_real(_mp)) return [];
var _groups = ds_map_find_value(_mp, "groups");
if (_groups == undefined || !is_real(_groups)) return [];
if (!ds_map_exists(_groups, _gn)) return [];
return ds_map_find_value(_groups, _gn);
