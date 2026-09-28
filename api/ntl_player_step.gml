/// ntl_player_step(mapHandle) —— 玩家每帧更新（移动 + 碰撞 + 相机）
/// 按键：方向键 / WASD；碰撞用 ntl_map_collide
var _mp = argument[0];
if (!variable_global_exists("ntl_player")) return 0;
var _p = global.ntl_player;
if (ds_map_find_value(_p, "active") != 1) return 0;

// --- 输入 ---
var _l = keyboard_check(vk_left) || keyboard_check(ord("A"));
var _r = keyboard_check(vk_right) || keyboard_check(ord("D"));
var _u = keyboard_check(vk_up) || keyboard_check(ord("W"));
var _d = keyboard_check(vk_down) || keyboard_check(ord("S"));
var _sp = ds_map_find_value(_p, "speed");

var _dx = (_r ? _sp : 0) - (_l ? _sp : 0);
var _dy = (_d ? _sp : 0) - (_u ? _sp : 0);
if (_dx != 0 && _dy != 0) { _dx *= 0.7071; _dy *= 0.7071; }

var _px = ds_map_find_value(_p, "x");
var _py = ds_map_find_value(_p, "y");
var _pw = ds_map_find_value(_p, "w");
var _ph = ds_map_find_value(_p, "h");

// 记录朝向
if (_dx > 0) ds_map_replace(_p, "dir", 1);
else if (_dx < 0) ds_map_replace(_p, "dir", 3);
if (_dy > 0) ds_map_replace(_p, "dir", 2);
else if (_dy < 0) ds_map_replace(_p, "dir", 0);

// --- 分轴移动（可沿墙滑动）---
if (_dx != 0)
{
    var _nx = _px + _dx;
    if (ntl_map_collide(_mp, _nx, _py, _pw, _ph) == 0) _px = _nx;
}
if (_dy != 0)
{
    var _ny = _py + _dy;
    if (ntl_map_collide(_mp, _px, _ny, _pw, _ph) == 0) _py = _ny;
}

// --- 地图边界 ---
if (_mp != undefined && is_real(_mp) && ds_map_find_value(_mp, "ok") == 1)
{
    var _mw = ds_map_find_value(_mp, "width");
    var _mh = ds_map_find_value(_mp, "height");
    _px = clamp(_px, 0, _mw - _pw);
    _py = clamp(_py, 0, _mh - _ph);
    global.ntl_cam_x = clamp(_px + _pw / 2 - room_width / 2, 0, max(0, _mw - room_width));
    global.ntl_cam_y = clamp(_py + _ph / 2 - room_height / 2, 0, max(0, _mh - room_height));
}

ds_map_replace(_p, "x", _px);
ds_map_replace(_p, "y", _py);
return 1;
