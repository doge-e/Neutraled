/// ntl_font_main() —— 游戏正文/说明行字号（main，EmSize 12，部署期已补 CJK 字形）。
/// 二级菜单底部的说明行用它：官方那行本来就是小字体，用 mainbig 会超出菜单框宽度
/// （说明文案里有「本产物实际加载 0 个（磁盘上已安装 47 个）。」这类长句）。
var _f = asset_get_index("fnt_main");
if (_f != -1 && font_exists(_f)) return _f;
if (variable_global_exists("font_map") && ds_map_exists(global.font_map, "main"))
{
    var _m = global.font_map[? "main"];
    if (font_exists(_m)) return _m;
}
return -1;
