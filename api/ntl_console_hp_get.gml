/// ntl_console_hp_get() —— 读当前 HP（找不到返回 -1）
var _m = ntl_console_hp_find();
if (!is_real(_m) || _m == -1) return -1;
var _n = string(ds_map_find_value(_m, "name"));
if (ds_map_find_value(_m, "kind") == "global")
{
    if (!variable_global_exists(_n)) return -1;
    return real(variable_global_get(_n));
}
var _oi = asset_get_index(string(ds_map_find_value(_m, "obj")));
var _inst = (_oi >= 0) ? instance_find(_oi, 0) : noone;
if (_inst == noone) return -1;
return real(variable_instance_get(_inst, _n));
