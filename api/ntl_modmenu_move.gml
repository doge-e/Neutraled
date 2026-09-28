/// ntl_modmenu_move(coord, dir) —— 光标移动：跳过分组标题、两端循环
/// dir > 0 往下、dir < 0 往上。整屏都不可选（理论上不会发生）时原样返回，绝不死循环。
var _c = real(argument[0]);
var _dir = (real(argument[1]) >= 0) ? 1 : -1;
var _n = ntl_modmenu_count();
if (_n <= 0) return 0;
var _i = _c;
for (var _s = 0; _s < _n; _s += 1)
{
    _i = (((_i + _dir) mod _n) + _n) mod _n;
    if (ntl_modmenu_selectable(_i)) return _i;
}
return _c;
