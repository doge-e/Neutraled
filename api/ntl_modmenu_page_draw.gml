/// ntl_modmenu_page_draw(xx, yy) —— Mod 设置页（submenu 51）的绘制：照游戏自己的二级菜单写
/// 参考实现：mods/deltarune_60_fps（BadArtAdventure）的 submenu 50「MOD SETTINGS」页 ——
///   行高 35、标签列 _xPos、数值列 _selectXPos、选中用官方红心精灵（名字解析，y = yy+160+行*35）、
///   可见 5 行、yy+330 分隔线、yy+340 说明行。
/// ★ 与参考实现的差别（用户要求）：
///   - 字体用游戏自己的 mainbig（EmSize 24，部署期已补 CJK 字形），不再切 ntl_font_cjk（只有一半大）；
///   - 说明行用游戏正文 main（EmSize 12）；
///   - 滚动提示改成**真正的滚动条**（参考页只有上下两个三角）；
///   - 官方素材没被拉伸：菜单框仍是原版高度（我们不画框，框由官方 Draw 画）。
/// ★ 长文本处理（用户反馈「mod 设置里没有处理长文本，设置选项无法分清是哪个 mod 的」）：
///   - 标签列/数值列都有列宽，超宽一律 ntl_text_fit 截断 + "..."（原来是裸 draw_text，直接压到隔壁列、
///     甚至顶穿面板右边框 —— 章节视图/模组视图的截图是铁证）；
///   - 分组标题行（kind 1）用灰色画，没有数值；模组项（kind 2）缩进一档；
///   - 右上角画「当前 / 总数」；被截断的那一行把**全名回显到说明行**（截断不丢信息）；
///   - 说明行过 ntl_text_fit_lines(..., 470, 3)，再长的说明也不会画到菜单框外面。
/// 由 builder 注入 obj_darkcontroller 的 Draw（builder/Injector.cs）。
/// ★ 绘制状态（字体/颜色/对齐/alpha/纹理过滤）必须完整保存恢复，否则会把后面的界面染坏。
if (!variable_global_exists("submenu") || global.submenu != 51) return 0;
if (!variable_global_exists("submenucoord")) return 0;

var _xx = argument[0];
var _yy = argument[1];
var _view = variable_global_exists("ntl_modmenu_view") ? string(global.ntl_modmenu_view) : "main";
var _slot = ntl_modmenu_slot(_view);
if (array_length(global.submenucoord) <= _slot) global.submenucoord[_slot] = 0;   // 原版只初始化了 0..35（Create 里 for i<36），我们自己补上 51+
var _n = ntl_modmenu_count();
var _coord = real(global.submenucoord[_slot]);
if (_n <= 0)
{
    _n = 0;
    _coord = 0;
}
else
{
    if (_coord < 0) _coord = 0;
    if (_coord >= _n) _coord = _n - 1;
}
global.submenucoord[_slot] = _coord;      // 钳位写回（和游戏自己的做法一致）

// 与官方 CONFIG 页同一套列位置（Draw 第 58-60 行）
var _xPos = (global.lang == "en") ? (_xx + 170) : (_xx + 150);
var _heartXPos = (global.lang == "en") ? (_xx + 145) : (_xx + 125);
var _selectXPos = (global.lang == "ja" && global.is_console) ? (_xx + 385) : (_xx + 430);
var _VIS = 5;
var _scroll = (_coord >= _VIS) ? (_coord - (_VIS - 1)) : 0;

// 列宽：标签列 [_xPos, _selectXPos-16]、数值列 [_selectXPos, _xx+556]、右边留 16px 间隙
var _indent = 18;
var _labMaxW = _selectXPos - _xPos - 16;
var _valMaxW = (_xx + 556) - _selectXPos;
var _groupMaxW = (_xx + 556) - _xPos;

var _ofont = draw_get_font();
var _ocol = draw_get_color();
var _oalpha = draw_get_alpha();
var _oha = draw_get_halign();
var _ova = draw_get_valign();

var _big = ntl_font_big();
var _main = ntl_font_main();
if (_big == -1) _big = _ofont;
if (_main == -1) _main = _ofont;

draw_set_halign(fa_left);
draw_set_valign(fa_top);
draw_set_alpha(1);
draw_set_color(c_white);

// 标题：与官方 CONFIG 标题同一个位置与画法
draw_set_font(_big);
draw_set_halign(fa_center);
draw_text(_xx + 320, _yy + 100, ntl_t("menu.page_title"));
draw_set_halign(fa_left);

// 右上角「当前 / 总数」：长列表里一眼能看出还有多少项
if (_n > 0)
{
    draw_set_font(_main);
    draw_set_halign(fa_right);
    draw_set_color(8421504);
    draw_text(_xx + 556, _yy + 100, string(_coord + 1) + " / " + string(_n));
    draw_set_halign(fa_left);
    draw_set_color(c_white);
}

// 行
draw_set_font(_big);
for (var _i = 0; _i < _VIS; _i += 1)
{
    var _row = _scroll + _i;
    if (_row >= _n) break;
    var _d = ntl_modmenu_row(_row);
    if (array_length(_d) < 3) break;
    var _lab = string(_d[0]);
    var _val = string(_d[1]);
    var _dim = (array_length(_d) > 3) ? real(_d[3]) : 0;
    var _kind = (array_length(_d) > 4) ? real(_d[4]) : 0;
    var _y = _yy + 150 + (_i * 35);
    if (_kind == 1)
    {
        // 分组标题（如「【60fps_layer】」）：纯排版，灰色、没有数值、没有红心、光标也跳过
        draw_set_color(8421504);
        draw_text(_xPos, _y, ntl_text_fit(_lab, _groupMaxW));
    }
    else
    {
        draw_set_color((_dim == 1) ? 8421504 : c_white);     // 8421504 = c_gray：不可用项（官方也是这么画不可用项）
        var _lx = (_kind == 2) ? (_xPos + _indent) : _xPos;  // 模组项缩进一档，与内置行区分
        var _lw = (_kind == 2) ? (_labMaxW - _indent) : _labMaxW;
        draw_text(_lx, _y, ntl_text_fit(_lab, _lw));
        if (string_length(_val) > 0) draw_text(_selectXPos, _y, ntl_text_fit(_val, _valMaxW));
    }
}

// 选中红心（官方 y 公式；精灵**按名字解析**——索引每章不同，见 ntl_heart_sprite.gml）
var _hsp = ntl_heart_sprite();
var _hy = _yy + 160 + ((_coord - _scroll) * 35);
var _hkind = 0;
if (_n > 0)
{
    var _hd = ntl_modmenu_row(_coord);
    if (array_length(_hd) > 4) _hkind = real(_hd[4]);
}
draw_set_color(c_white);
if (_hkind == 1)
{
    // 分组标题：不给红心（它不可选）
}
else if (_hsp != -1 && sprite_exists(_hsp))
{
    draw_sprite(_hsp, 0, _heartXPos, _hy);
}
else
{
    // 兜底：一个红心精灵都找不到时也要画得出来，绝不再弹 Trying to draw non-existing sprite
    draw_set_color(c_red);
    draw_rectangle(_heartXPos, _hy, _heartXPos + 16, _hy + 16, false);
    draw_set_color(c_white);
}

// 滚动条（用户要求：不要伸缩窗口，用滚动条装下更多内容）
if (_n > _VIS)
{
    var _bx = _xx + 566;
    var _by0 = _yy + 150;
    var _by1 = _yy + 310;
    draw_set_color(8421504);
    draw_rectangle(_bx, _by0, _bx + 5, _by1, false);
    var _bh = (_by1 - _by0) * (_VIS / _n);
    var _bmax = _n - _VIS;
    var _bt = _by0 + ((_by1 - _by0) - _bh) * ((_bmax > 0) ? (_scroll / _bmax) : 0);
    draw_set_color(c_white);
    draw_rectangle(_bx, _bt, _bx + 5, _bt + _bh, false);
    draw_set_color(c_white);
}

// 分隔线 + 说明行（官方位置 yy+330 / yy+340，菜单框内）
draw_line(_xx + 90, _yy + 330, _xx + 560, _yy + 330);

// 说明行：有消息画消息（黄）> 当前行说明 > 键位提示（白）
// ★ 当前行的标签/数值被截断时，把**全名**回显到说明行（"截断" 只是排版，信息不能丢）
var _desc = "";
var _curLab = "";
var _curCut = 0;
if (_n > 0)
{
    var _dd = ntl_modmenu_row(_coord);
    if (array_length(_dd) > 2)
    {
        _desc = string(_dd[2]);
        _curLab = string(_dd[0]);
        var _ck = (array_length(_dd) > 4) ? real(_dd[4]) : 0;
        var _cw = (_ck == 2) ? (_labMaxW - _indent) : ((_ck == 1) ? _groupMaxW : _labMaxW);
        if (ntl_text_fit(_curLab, _cw) != _curLab) _curCut = 1;
    }
}
var _msg = variable_global_exists("ntl_modmenu_msg") ? string(global.ntl_modmenu_msg) : "";
var _txt = "";
if (string_length(_msg) > 0) _txt = _msg;
else if (_curCut == 1) _txt = (string_length(_desc) > 0) ? (_curLab + "  -  " + _desc) : _curLab;
else if (string_length(_desc) > 0) _txt = _desc;
else _txt = ntl_t("menu.keys");
draw_set_font(_main);
draw_set_color((string_length(_msg) > 0) ? c_yellow : c_white);
draw_text_ext(_xx + 90, _yy + 340, ntl_text_fit_lines(_txt, 470, 3), 18, 470);

draw_set_font(_ofont);
draw_set_color(_ocol);
draw_set_alpha(_oalpha);
draw_set_halign(_oha);
draw_set_valign(_ova);
return 1;
