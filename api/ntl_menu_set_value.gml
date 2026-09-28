/// ntl_menu_set_value(id, value) —— 改自己那一项右侧的数值文案（1 = 改到了）
if (!variable_global_exists("ntl_menu_mods")) return 0;
var _id = string(argument[0]);
var _v = (argument_count > 1) ? string(argument[1]) : "";
for (var _i = 0; _i < ds_list_size(global.ntl_menu_mods); _i += 1)
{
    var _m = ds_list_find_value(global.ntl_menu_mods, _i);
    if (string(ds_map_find_value(_m, "id")) == _id)
    {
        ds_map_replace(_m, "value", _v);
        return 1;
    }
}
return 0;