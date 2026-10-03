/// ntl_settings_row_draw(x, selx, y) —— 在游戏自己的设置菜单里画「Mod 设置」这一行
/// ★ 位置与样式完全照 mods/deltarune_60_fps 的「Mod Settings」：入口行插在原版第 6 行，
///   原版「Return to Title」「Back」由 builder 顺移一位（见 builder/Injector.cs 2.6 / 2.9）。
/// ★ 选中红心不归我们画：官方红心公式 y = yy+160+(coord-滚动)*35 在 coord 5 时正好落在本行。
/// 字体：改用游戏自己的 mainbig（EmSize 24，部署期 FontMerge 已补 CJK 字形）——
///   与上下官方行同字号。旧版只要出现非 ASCII 就切 ntl_font_cjk（EmSize 12，只有一半大），
///   用户反馈「mod设置及其二级菜单字体太小了」指的就是这个。
/// ★ 必须完整保存/恢复绘制状态：漏了颜色会把游戏的光标心形染成黄色（实测踩过）。
if (!variable_global_exists("submenu") || global.submenu != 30) return 0;
if (!variable_global_exists("submenucoord")) return 0;

// ★ 诊断：这一行真的被画出来 ⇒ 玩家已经站在设置页上（真机测试的机器人靠这行判断状态）
if (!variable_global_exists("ntl_cfg_page_seen")) {
    global.ntl_cfg_page_seen = 1;
    ntl_log("menu", "[设置页] 已进入：我们的行已绘制（submenu=30 coord30=" +
        string(global.submenucoord[30]) + "）");
}

var _x = argument[0];
var _selx = argument[1];
var _y = argument[2];

var _ofont = draw_get_font();
var _ocol = draw_get_color();
var _oalpha = draw_get_alpha();
var _oha = draw_get_halign();
var _ova = draw_get_valign();

var _label = ntl_t("set.row");
// ★ 右列显示"已加载"数：本产物实际加载的 mod 数；读不到 mods.json 时回退"已安装"数
var _loaded = ntl_modmenu_loaded_count();
var _value = ntl_ts("set.value", [string(_loaded >= 0 ? _loaded : ntl_modmenu_modcount())]);

draw_set_halign(fa_left);
draw_set_valign(fa_top);
draw_set_alpha(1);
var _f = ntl_font_big();
if (_f != -1) draw_set_font(_f);
draw_set_color(c_white);
draw_text(_x, _y, _label);
draw_text(_selx, _y, _value);

draw_set_font(_ofont);
draw_set_color(_ocol);
draw_set_alpha(_oalpha);
draw_set_halign(_oha);
draw_set_valign(_ova);
return 1;
