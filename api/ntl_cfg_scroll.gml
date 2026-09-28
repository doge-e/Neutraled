/// ntl_cfg_scroll() —— 设置(CONFIG)页当前的行窗口起点（0 或 1）。
/// 原版 7 项 + 我们的「Mod 设置」= 8 项；可见窗口是 7 行（y = yy+150 … yy+360，行高 35）。
/// ★ 用户 m11431 明确要求「不要伸缩设置窗口，可以设置滚动条来装下更多内容」⇒
///   菜单框回到原版高度（langopt([90,410,420],[85,412,422])），多出来的那一行靠滚动容纳：
///   光标在第 8 项(Back)时窗口下移一行，第 1 项(Master Volume)让位。
var _c = 0;
if (variable_global_exists("submenucoord") && array_length(global.submenucoord) > 30)
{
    _c = real(global.submenucoord[30]);
}
var _off = _c - 6;
if (_off < 0) _off = 0;
if (_off > 1) _off = 1;
return _off;
