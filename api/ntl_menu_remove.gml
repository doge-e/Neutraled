/// ntl_menu_remove(id) —— 撤掉自己加的面板项（1 = 删掉了，0 = 本来没有）
if (!variable_global_exists("ntl_menu_mods")) return 0;
var _id = string(argument[0]);
for (var _i = ds_list_size(global.ntl_menu_mods) - 1; _i >= 0; _i -= 1)
{
    var _m = ds_list_find_value(global.ntl_menu_mods, _i);
    if (string(ds_map_find_value(_m, "id")) == _id)
    {
        ds_map_destroy(_m);
        ds_list_delete(global.ntl_menu_mods, _i);
        return 1;
    }
}
return 0;