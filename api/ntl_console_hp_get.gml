/// ntl_console_hp_get() —— 读当前 HP（找不到返回 -1）
var _m = ntl_console_hp_find();
if (_m == -1) return -1;
var _n = string(ds_map_find_value(_m, "name"));
if (ds_map_find_value(_m, "kind") == "global")
{
    if (!variable_global_exists(_n)) return -1;
    // ★ 实测事故修复：可能是数组（global.hp），real(数组) 会抛错；非数值就当没找到。
    var _gv = variable_global_get(_n);
    if (!is_real(_gv)) return -1;
    return _gv;
}
var _oi = asset_get_index(string(ds_map_find_value(_m, "obj")));
var _inst = (_oi >= 0) ? instance_find(_oi, 0) : noone;
if (_inst == noone) return -1;
var _iv = variable_instance_get(_inst, _n);
if (!is_real(_iv)) return -1;
return _iv;
