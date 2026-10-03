/// ntl_text_fit(text, maxw) —— 单行宽度适配：宽度超过 maxw 就按字符截断并在尾巴补 "…"
/// 为什么需要：面板的标签列/数值列都是固定列宽，而 draw_text 没有宽度限制 ——
/// 长名字会直接压到隔壁列上（用户的截图：章节视图里 "Chapter 1 The Beginning" 与「官方」叠成一团，
/// 模组视图里 "deltarune_but_it_s__percentage___color" 顶穿面板右边框）。
/// 必须在 draw_set_font(要用的字体) 之后调用。maxw <= 0 = 不做限制，原样返回。
var _s = string(argument[0]);
var _w = real(argument[1]);
if (_w <= 0) return _s;
if (string_width(_s) <= _w) return _s;
var _ell = "...";                                // 纯 ASCII：字体只保证有 ASCII（95 字形）+ 汉化补的 CJK，U+2026 不一定在
var _ew = string_width(_ell);
var _n = string_length(_s);
while (_n > 0)
{
    var _t = string_copy(_s, 1, _n);             // 一个字一个字退：CJK/ASCII 混排时只有按宽度算才准
    if (string_width(_t) + _ew <= _w) return _t + _ell;
    _n -= 1;
}
return _ell;
