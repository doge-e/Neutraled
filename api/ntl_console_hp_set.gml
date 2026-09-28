/// ntl_console_hp_set(v) —— 写 HP（成功返回 1）
var _v = argument[0];
var _m = ntl_console_hp_find();
if (!is_real(_m) || _m == -1) return 0;
var _n = string(ds_map_find_value(_m, "name"));
try
{
    if (ds_map_find_value(_m, "kind") == "global") variable_global_set(_n, _v);
    else
    {
        var _oi = asset_get_index(string(ds_map_find_value(_m, "obj")));
        var _inst = (_oi >= 0) ? instance_find(_oi, 0) : noone;
        if (_inst == noone) return 0;
        variable_instance_set(_inst, _n, _v);
    }
}
catch (e) { return 0; }
return 1;
