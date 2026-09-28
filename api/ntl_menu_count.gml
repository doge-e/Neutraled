/// ntl_menu_count() —— 当前有几个 mod 注册的面板项
if (!variable_global_exists("ntl_menu_mods")) return 0;
return ds_list_size(global.ntl_menu_mods);