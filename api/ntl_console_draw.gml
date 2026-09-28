// ntl_console_draw() —— GUI 层绘制控制台面板（支持滚动、滚动条、状态行）
var _gw = display_get_gui_width();
if (_gw <= 0) _gw = room_width;
var _gh = display_get_gui_height();
if (_gh <= 0) _gh = room_height;

var _line_h = 22;
if (!variable_global_exists("ntl_console_lines_shown")) global.ntl_console_lines_shown = 14;
if (!variable_global_exists("ntl_console_alpha")) global.ntl_console_alpha = 0.88;
var _max_lines = global.ntl_console_lines_shown;
var _header_h = 30;
var _footer_h = 28;
var _panel_h = _header_h + _line_h * _max_lines + _footer_h;
// 优先用 Neutraled 自带中文字体（--make-cjk-font 生成、部署时自动注入）；
// 没有就退回游戏主字体（只有 ASCII，中文会画不出来）
var _font = asset_get_index("ntl_font_cjk");
if (_font == -1) _font = asset_get_index("fnt_main");
if (_font != -1) draw_set_font(_font);
// 像素字体关掉线性过滤：否则缩放后发糊、字距看着不匀
gpu_set_texfilter(false);

// 半透明背景
draw_set_alpha(global.ntl_console_alpha);
draw_set_color(ntl_theme_c("bg"));
draw_rectangle(0, 0, _gw, _panel_h, false);
draw_set_alpha(1);

// ---------- 滚动状态 ----------
if (!variable_global_exists("ntl_console_scroll")) global.ntl_console_scroll = 0;
if (!variable_global_exists("ntl_console_autoscroll")) global.ntl_console_autoscroll = 1;

var _n = 0;
if (variable_global_exists("ntl_console_lines")) _n = ds_list_size(global.ntl_console_lines);

// 底部位置（scroll=0 时显示最后 _max_lines 行）
var _maxScroll = max(0, _n - _max_lines);
if (global.ntl_console_scroll > _maxScroll) global.ntl_console_scroll = _maxScroll;
if (global.ntl_console_scroll < 0) global.ntl_console_scroll = 0;

var _endIdx = _n - global.ntl_console_scroll;       // 显示的最后一行的后一个下标
var _startIdx = max(0, _endIdx - _max_lines);

// ---------- 标题行 ----------
// ★ F5 接线 ntl_console_line_pass：过滤器生效时把状态显示在标题右边（否则用户不知道自己在过滤模式里）
var _ftr = "";
if (variable_global_exists("ntl_console_filter_mode") && string(global.ntl_console_filter_mode) != "all")
{
    _ftr = "  [filter: " + string(global.ntl_console_filter_mode) + "]";
    draw_set_color(ntl_theme_c("selection"));
}
else draw_set_color(ntl_theme_c("accent"));
draw_text(12, 6, ntl_t("console.title") + "  " + ntl_t("console.close") + _ftr);

// 滚动位置指示（右上角）
if (_n > _max_lines)
{
    var _pos = _n - _endIdx;                          // 距底部的行数
    var _ind = "[" + string(_pos) + "/" + string(_maxScroll) + "]";
    if (global.ntl_console_scroll == 0) draw_set_color(ntl_theme_c("ok"));
    else draw_set_color(ntl_theme_c("selection"));
    draw_text(_gw - 12 - string_width(_ind), 6, _ind);
}

// ---------- 分隔线 ----------
draw_set_alpha(0.5);
draw_set_color(ntl_theme_c("border"));
draw_line(0, _header_h - 2, _gw, _header_h - 2);
draw_set_alpha(1);

// ---------- 内容 ----------
draw_set_color(ntl_theme_c("fg"));
var _y = _header_h + 2;
for (var _i = _startIdx; _i < _endIdx; _i++)
{
    var _line = string(ds_list_find_value(global.ntl_console_lines, _i));
    // 按内容着色
    if (string_pos("[错误]", _line) > 0 || string_pos("[Error]", _line) > 0 || string_pos("[失败]", _line) > 0 || string_pos("[Failed]", _line) > 0)
        draw_set_color(ntl_theme_c("error"));
    else if (string_pos("[警告]", _line) > 0 || string_pos("[Warning]", _line) > 0 || string_pos("[注意]", _line) > 0 || string_pos("[Note]", _line) > 0)
        draw_set_color(ntl_theme_c("warn"));
    else if (string_copy(_line, 1, 1) == ">")
        draw_set_color(ntl_theme_c("highlight"));
    else if (string_copy(_line, 1, 2) == "==")
        draw_set_color(ntl_theme_c("accent"));
    else
        draw_set_color(ntl_theme_c("fg"));
    draw_text(12, _y, _line);
    _y += _line_h;
}

// 内容不足时仍占位（保持面板高度稳定）

// ---------- 滚动条 ----------
if (_n > _max_lines)
{
    var _bar_x = _gw - 6;
    var _track_y0 = _header_h + 2;
    var _track_h = _line_h * _max_lines;
    draw_set_alpha(0.3);
    draw_set_color(ntl_theme_c("border"));
    draw_rectangle(_bar_x, _track_y0, _bar_x + 4, _track_y0 + _track_h, false);
    draw_set_alpha(1);
    var _ratio = _max_lines / _n;
    var _thumbH = max(20, _track_h * _ratio);
    var _maxThumbY = _track_y0 + _track_h - _thumbH;
    var _scrollRatio = (_maxScroll > 0) ? (global.ntl_console_scroll / _maxScroll) : 0;
    var _thumbY = _maxThumbY - _scrollRatio * (_track_h - _thumbH);
    draw_set_color(global.ntl_console_scroll == 0 ? ntl_theme_c("ok") : ntl_theme_c("selection"));
    draw_rectangle(_bar_x, _thumbY, _bar_x + 4, _thumbY + _thumbH, false);
}

// ---------- 分隔线（输入区上方）----------
draw_set_alpha(0.5);
draw_set_color(ntl_theme_c("border"));
var _inY = _panel_h - _footer_h;
draw_line(0, _inY, _gw, _inY);
draw_set_alpha(1);

// ---------- 输入行 ----------
draw_set_color(ntl_theme_c("ok"));
draw_text(12, _inY + 4, "> " + global.ntl_console_input + "_");

// ---------- 操作提示（底部右侧，小字）----------
if (variable_global_exists("ntl_lang") && global.ntl_lang == "en")
    draw_set_color(ntl_theme_c("dim"));
else
    draw_set_color(ntl_theme_c("dim"));
var _hint = ntl_t("console.hint_scroll");
draw_text(_gw - 12 - string_width(_hint), _inY + 4, _hint);

draw_set_color(ntl_theme_c("fg"));
return 0;