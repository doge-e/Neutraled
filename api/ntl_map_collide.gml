/// ntl_map_collide(mapMap, x, y, w, h) —— 与地图碰撞矩形做 AABB 检测
/// 返回 1 = 碰撞，0 = 可行走
var _mp = argument[0];
var _x = real(argument[1]);
var _y = real(argument[2]);
var _w = real(argument[3]);
var _h = real(argument[4]);
if (_mp == undefined || !is_real(_mp)) return 0;
var _coll = ds_map_find_value(_mp, "collision");
if (!is_array(_coll)) return 0;

for (var _i = 0; _i < array_length(_coll); _i += 1)
{
    var _c = _coll[_i];
    var cx = _c[0], cy = _c[1], cw = _c[2], ch = _c[3];
    if (_x < cx + cw && _x + _w > cx && _y < cy + ch && _y + _h > cy) return 1;
}
return 0;

