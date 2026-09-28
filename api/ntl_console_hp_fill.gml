/// ntl_console_hp_fill() —— 把 HP 拉满（god 每帧调用；找不到就静默）
var _m = ntl_console_hp_find();
if (!is_real(_m) || _m == -1) return 0;
var _mx = string(ds_map_find_value(_m, "maxname"));
var _target = 999;
if (_mx != "")
{
    if (ds_map_find_value(_m, "kind") == "global")
    {
        if (variable_global_exists(_mx)) _target = real(variable_global_get(_mx));
    }
    else
    {
        var _oi = asset_get_index(string(ds_map_find_value(_m, "obj")));
        var _inst = (_oi >= 0) ? instance_find(_oi, 0) : noone;
        if (_inst != noone && variable_instance_exists(_inst, _mx)) _target = real(variable_instance_get(_inst, _mx));
    }
}
var _cur = ntl_console_hp_get();
if (_cur >= _target) return 0;
return ntl_console_hp_set(_target);
