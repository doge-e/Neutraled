/// ntl_cfg_row(k) —— 给 CONFIG 页第 k 项设置"该不该画"（返回 1/0 并写入 draw_set_alpha）。
/// 窗口外的行必须**彻底透明**：第 0 项落到 yy+115 会压到标题「CONFIG」，
/// 第 7 项(Back)在窗口未滚动时会画到 yy+395（超出原版框底 yy+410）。
/// 每一项绘制前都要调用（含数值列），否则滚动时会看到"鬼影行"。
/// ⚠ 踩坑：这里本来漏了 `var _k = argument[0];` ⇒ 运行期直接 Code Error
///   「Variable obj_darkcontroller._k not set before reading it」（真机 nat-13 第一次就崩在 CONFIG 页）。
var _k = argument[0];
var _off = ntl_cfg_scroll();
var _vis = (_k >= _off && _k < _off + 7) ? 1 : 0;
draw_set_alpha(_vis);
return _vis;
