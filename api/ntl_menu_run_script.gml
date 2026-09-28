/// ntl_menu_run_script(name) —— 按脚本名跑一个面板项的动作（mod 接口 ntl_menu_add 的内部实现）
/// 返回 1 = 跑成功；0 = 找不到脚本（面板会显示「只读」提示，绝不静默失败）。
/// 与 ntl_menu_run(下标) 的区别：分组标题插进来之后下标会漂，所以面板按脚本名调用。
var _sn = string(argument[0]);
if (string_length(_sn) <= 0) return 0;
var _sc = asset_get_index(_sn);
if (_sc == -1)
{
    ntl_log("menu-api", "[接口] 找不到脚本 " + _sn + "（面板项动作）");
    return 0;
}
script_execute(_sc);
return 1;
