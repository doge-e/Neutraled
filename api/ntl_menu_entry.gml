/// ntl_menu_entry(i) —— 取第 i 个 mod 面板项的 ds_map（越界返回 -1）；面板内部用
if (!variable_global_exists("ntl_menu_mods")) return -1;
if (argument[0] < 0 || argument[0] >= ds_list_size(global.ntl_menu_mods)) return -1;
return ds_list_find_value(global.ntl_menu_mods, argument[0]);