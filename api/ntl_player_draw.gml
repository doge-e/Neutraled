/// ntl_player_draw() —— 绘制玩家（用 GM 基本图形，无需外部素材）
if (!variable_global_exists("ntl_player")) return 0;
var _p = global.ntl_player;
if (ds_map_find_value(_p, "active") != 1) return 0;

var _cx = variable_global_exists("ntl_cam_x") ? global.ntl_cam_x : 0;
var _cy = variable_global_exists("ntl_cam_y") ? global.ntl_cam_y : 0;
var _x = ds_map_find_value(_p, "x") - _cx;
var _y = ds_map_find_value(_p, "y") - _cy;
var _w = ds_map_find_value(_p, "w");
var _h = ds_map_find_value(_p, "h");

// 身体（深蓝）+ 头（浅色）
draw_set_alpha(0.9);
draw_set_color(make_color_rgb(60, 80, 160));
draw_rectangle(_x, _y + 8, _x + _w, _y + _h, false);
draw_set_color(make_color_rgb(240, 230, 210));
draw_rectangle(_x + 2, _y, _x + _w - 2, _y + 10, false);

// 朝向标记
draw_set_color(c_yellow);
var _dir = ds_map_find_value(_p, "dir");
if (_dir == 0) draw_rectangle(_x + 6, _y - 4, _x + _w - 6, _y - 1, false);
if (_dir == 2) draw_rectangle(_x + 6, _y + _h + 1, _x + _w - 6, _y + _h + 4, false);
if (_dir == 1) draw_rectangle(_x + _w + 1, _y + 8, _x + _w + 4, _y + _h - 4, false);
if (_dir == 3) draw_rectangle(_x - 4, _y + 8, _x - 1, _y + _h - 4, false);
draw_set_alpha(1);
return 1;
