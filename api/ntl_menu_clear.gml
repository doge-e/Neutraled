/// ntl_menu_clear() —— 清空所有 mod 注册项（mod 重载时用）
if (!variable_global_exists("ntl_menu_mods")) { global.ntl_menu_mods = ds_list_create(); return 0; }
for (var _i = ds_list_size(global.ntl_menu_mods) - 1; _i >= 0; _i -= 1)
{
    ds_map_destroy(ds_list_find_value(global.ntl_menu_mods, _i));
    ds_list_delete(global.ntl_menu_mods, _i);
}
return 1;