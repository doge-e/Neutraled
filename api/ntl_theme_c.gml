/// ntl_theme_c(name) —— 取主题颜色（GameMaker BGR 整数）；没有主题文件时返回内置默认值。
/// 默认值 = 改造前 draw_set_color 里写死的那些常量 ⇒ 未装主题时像素级不变。
/// 首次调用会懒加载 Neutraled/console-theme.json（省掉在 scr_ntl_init 里插一行）。
if (!variable_global_exists("ntl_theme"))
{
    global.ntl_theme = ds_map_create();
    global.ntl_theme_checked = 0;
}
// 只在第一次（或还没探测过）读一次文件 —— 这个函数每帧会被调 ~19 次
if (!variable_global_exists("ntl_theme_checked") || global.ntl_theme_checked == 0) ntl_theme_load();
var _n = string_lower(string(argument[0]));
var _v = undefined;
try { _v = ds_map_find_value(global.ntl_theme, _n); } catch (e) { _v = undefined; }
if (!is_undefined(_v) && is_real(_v)) return _v;
switch (_n)
{
    case "fg":        return c_white;
    case "dim":       return c_dkgray;
    case "border":    return c_gray;
    case "accent":    return c_yellow;
    case "highlight": return c_ltgray;
    case "selection": return c_orange;
    case "ok":        return c_lime;
    case "warn":      return c_orange;
    case "error":     return c_red;
    case "panel":     return c_black;
    case "bg":        return c_black;
    default:          return c_white;
}
