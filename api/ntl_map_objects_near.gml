/// ntl_map_objects_near(map, x, y, radius) —— 找出玩家附近的可交互对象
/// 返回数组：[{obj, dist, name, type, x, y, props}]
var _mp = argument[0];
var _px = real(argument[1]);
var _py = real(argument[2]);
var _rad = real(argument[3]);
if (_mp == undefined || !is_real(_mp)) return [];

var _groups = ds_map_find_value(_mp, "groups");
if (_groups == undefined || !is_real(_groups)) return [];

var _out = [];
var _keys = ntl_dsmap_keys(_groups);
for (var _k = 0; _k < array_length(_keys); _k += 1)
{
    var _gname = _keys[_k];
    if (string_pos("collision", string_lower(_gname)) > 0) continue;   // 跳过碰撞层
    var _list = ds_map_find_value(_groups, _gname);
    if (!is_array(_list)) continue;

    for (var _i = 0; _i < array_length(_list); _i += 1)
    {
        var _o = _list[_i];
        if (!is_real(_o)) continue;
        var _ox = ds_map_find_value(_o, "x");
        var _oy = ds_map_find_value(_o, "y");
        var _ow = ds_map_find_value(_o, "w");
        var _oh = ds_map_find_value(_o, "h");
        // 点对象或矩形对象：算到中心的距离
        var _cx = _ox + _ow / 2;
        var _cy = _oy + _oh / 2;
        var _d = point_distance(_px, _py, _cx, _cy);
        if (_d <= _rad)
        {
            var _rec = ds_map_create();
            ds_map_add(_rec, "obj", _o);
            ds_map_add(_rec, "group", _gname);
            ds_map_add(_rec, "dist", _d);
            ds_map_add(_rec, "name", ds_map_find_value(_o, "name"));
            ds_map_add(_rec, "type", ds_map_find_value(_o, "type"));
            ds_map_add(_rec, "x", _ox);
            ds_map_add(_rec, "y", _oy);
            ds_map_add(_rec, "w", _ow);
            ds_map_add(_rec, "h", _oh);
            array_push(_out, _rec);
        }
    }
}

// 按距离排序（简单插入排序）
for (var _a = 1; _a < array_length(_out); _a += 1)
{
    var _cur = _out[_a];
    var _b = _a - 1;
    while (_b >= 0 && ds_map_find_value(_out[_b], "dist") > ds_map_find_value(_cur, "dist"))
    {
        _out[_b + 1] = _out[_b];
        _b -= 1;
    }
    _out[_b + 1] = _cur;
}
return _out;

