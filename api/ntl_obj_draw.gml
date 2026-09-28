/// ntl_obj_draw(camX, camY) —— 绘制所有 Kristal 对象实例
/// 用 GM 基本图形（颜色由 Lua 侧 color 字段决定，缺省蓝色）
if (!variable_global_exists("ntl_objects")) return 0;
var _cx = (argument_count > 0) ? real(argument[0]) : 0;
var _cy = (argument_count > 1) ? real(argument[1]) : 0;

var _n = ds_list_size(global.ntl_objects);
var _i = 0;
var _drawn = 0;
while (_i < _n)
{
    var _o = ds_list_find_value(global.ntl_objects, _i);
    _i += 1;
    if (!is_real(_o)) continue;
    if (ds_map_find_value(_o, "visible") != 1) continue;

    var _x = ds_map_find_value(_o, "x") - _cx;
    var _y = ds_map_find_value(_o, "y") - _cy;

    // 从 Lua 侧取颜色（可选）
    var _col = c_blue;
    var _inst = ds_map_find_value(_o, "lua");
    if (_inst != undefined && is_real(_inst))
    {
        var _c = ntl_lua_index_get(_inst, "color");
        if (_c != undefined && is_real(_c)) _col = _c;
    }

    draw_set_color(_col);
    draw_rectangle(_x, _y, _x + 16, _y + 16, false);
    _drawn += 1;
}
return _drawn;
