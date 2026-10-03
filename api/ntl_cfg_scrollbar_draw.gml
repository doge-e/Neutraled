/// ntl_cfg_scrollbar_draw(xx, yy) —— CONFIG 页右侧的滚动条（8 项 / 可见 7 行）。
/// 官方二级菜单没有滚动条；用户要求「不要伸缩窗口，用滚动条装下更多内容」。
/// 轨道 = 可见行区间（yy+150 … yy+384），滑块长度/位置按 可见÷总数 与 窗口起点 算。
/// 始终画（不是"滚动后才出现"）：让玩家一眼看出下面还有内容（第 8 项 Back）。
if (!variable_global_exists("submenu")) return 0;
var _x = argument[0] + 572;      // 框内右侧（官方填充区是 xx+60 … xx+580）
var _y0 = argument[1] + 150;
var _y1 = argument[1] + 384;
var _total = 8;
var _vis = 7;
var _off = ntl_cfg_scroll();

var _ocol = draw_get_color();
var _oalpha = draw_get_alpha();
draw_set_alpha(1);

// 轨道
draw_set_color(8421504);
draw_rectangle(_x, _y0, _x + 4, _y1, false);

// 滑块
var _h = (_y1 - _y0) * (_vis / _total);
var _ty = _y0 + ((_y1 - _y0) - _h) * (_off / (_total - _vis));
draw_set_color(c_white);
draw_rectangle(_x, _ty, _x + 4, _ty + _h, false);

draw_set_color(_ocol);
draw_set_alpha(_oalpha);
return 1;
