/// ntl_map_draw(mapMap, camX, camY) —— 绘制地图（整图，按相机偏移）
/// 地图数据由 ntl_map_load 返回
var _mp = argument[0];
var _cx = (argument_count > 1) ? real(argument[1]) : 0;
var _cy = (argument_count > 2) ? real(argument[2]) : 0;
if (_mp == undefined || !is_real(_mp)) return 0;
if (ds_map_find_value(_mp, "ok") != 1) return 0;
var _spr = ds_map_find_value(_mp, "sprite");
if (_spr < 0) return 0;

draw_sprite(_spr, 0, -_cx, -_cy);
return 1;
