/// ntl_text_fit_lines(text, w, maxlines) —— 说明区多行适配：整段折行高度超过 maxlines 行就砍尾巴补 "…"
/// 为什么需要：说明区是 draw_text_ext(x, y, txt, 18, 470) —— 会自动折行但**没有行数上限**，
/// 长说明（例如带 mod 全名的回显）会把文字画到菜单框外面去。
/// 这里按"折行后的真实高度"收敛，保证永远装得进说明区。
/// 必须在 draw_set_font(说明行字体) 之后调用。maxlines < 1 按 1 行处理；w <= 0 不做限制。
var _s = string(argument[0]);
var _w = real(argument[1]);
var _max = real(argument[2]);
if (_max < 1) _max = 1;
if (_w <= 0) return _s;
var _lh = 18;                                    // 行距，必须与 draw_text_ext 的 sep 一致
var _lim = _lh * _max;
if (string_height_ext(_s, _lh, _w) <= _lim) return _s;
var _ell = "...";
var _n = string_length(_s);
var _guard = 0;
while (_n > 0 && _guard < 500)                   // guard：极端字体下也不会死循环
{
    _guard += 1;
    _n -= 2;                                     // 每次退两个字，收敛快；保留的永远是开头（信息量最大）
    if (_n < 1) _n = 0;
    var _t = string_copy(_s, 1, _n);
    if (_n > 0) _t += _ell;
    if (string_height_ext(_t, _lh, _w) <= _lim) return _t;
}
return _ell;
