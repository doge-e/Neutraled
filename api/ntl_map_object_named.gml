/// ntl_map_object_named(map, objectName) —— 在所有对象组里按名字查找对象
/// 返回 ds_map（含 name/type/x/y/w/h/point/prop:*），找不到返回 undefined
var _mp = argument[0];
var _want = string(argument[1]);
if (_mp == undefined || !is_real(_mp)) return undefined;
var _groups = ds_map_find_value(_mp, "groups");
if (_groups == undefined || !is_real(_groups)) return undefined;

var _keys = ntl_dsmap_keys(_groups);
for (var _k = 0; _k < array_length(_keys); _k += 1)
{
    var _list = ds_map_find_value(_groups, _keys[_k]);
    if (!is_array(_list)) continue;
    for (var _i = 0; _i < array_length(_list); _i += 1)
    {
        var _o = _list[_i];
        if (!is_real(_o)) continue;
        if (ds_map_find_value(_o, "name") == _want) return _o;
    }
}
return undefined;
