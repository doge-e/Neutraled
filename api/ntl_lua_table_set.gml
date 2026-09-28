/// ntl_lua_table_set(t, k, v) —— 设值，并维护数组边界
var _t = argument[0];
var _k = argument[1];
var _v = argument[2];
if (_t == undefined || !is_real(_t)) return _v;
var _dk = ntl_lua_key(_k);
if (ds_map_exists(_t, _dk)) ds_map_replace(_t, _dk, _v);
else ds_map_add(_t, _dk, _v);

if (is_real(_k))
{
    var _num = real(_k);
    if (_num == floor(_num) && _num >= 1)
    {
        var _n = ds_map_find_value(_t, "_ntln");
        if (_num == _n + 1 && _v != undefined) ds_map_replace(_t, "_ntln", _num);
    }
}
return _v;
