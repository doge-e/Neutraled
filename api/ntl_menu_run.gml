/// ntl_menu_run(i) —— 触发第 i 个 mod 面板项的 action（面板内部用；1 = 真的调用了）
var _m = ntl_menu_entry(argument[0]);
if (_m == -1) return 0;
var _act = string(ds_map_find_value(_m, "action"));
if (string_length(_act) <= 0) return 0;
var _sc = asset_get_index(_act);
if (_sc == -1)
{
    ntl_log("menu-api", "[接口] 找不到脚本 " + _act + "（id=" + string(ds_map_find_value(_m, "id")) + "）");
    return 0;
}
ntl_log("menu-api", "[接口] 触发 " + _act);
script_execute(_sc);
return 1;