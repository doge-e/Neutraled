/// ntl_font_big() —— 我们界面要用的「官方菜单字号」字体（mainbig，EmSize 24）。
/// 为什么不用 ntl_font_cjk：那是内置字体包（EmSize 12），只有官方菜单字号的一半大
/// ⇒ 用户反馈「mod设置及其二级菜单字体太小了」（m10511/m11431）。部署期 FontMerge 已把
/// CJK 字形补进 fnt_mainbig/fnt_main，所以直接用游戏自己的字体画中文就是官方字号。
/// 用 asset_get_index(名字) 而不是硬编码索引：主字体索引随游戏语言变化（日文语境下
/// font_map 的 mainbig 变成 fnt_ja_mainbig，那个字体没补我们的字形），按名字取才稳。
var _f = asset_get_index("fnt_mainbig");
if (_f != -1 && font_exists(_f)) return _f;
if (variable_global_exists("font_map") && ds_map_exists(global.font_map, "mainbig"))
{
    var _m = global.font_map[? "mainbig"];
    if (font_exists(_m)) return _m;
}
return -1;
