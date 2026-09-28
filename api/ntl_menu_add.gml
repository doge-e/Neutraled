/// ntl_menu_add(id, label, value, action, desc, owner) —— 往 Mod 设置面板主列表加一项（mod 开发者接口）
/// label/value 传文案，或传 "i18n:键名" 走 i18n 表；action 是脚本名（玩家按 Z/Enter 时调用），空串 = 只显示。
/// owner（可选，第 6 参）= 这个开关属于哪个 mod（如 "60fps_layer"）——
///   面板会把这些项收进分组标题「── <owner> ──」下面并缩进一档，
///   这样玩家一眼能看出哪些开关是 mod 的、哪个 mod 的（用户 m17504 的诉求）。
///   不传 owner 的项会归到通用组「模组选项」。
/// 同 id 重复调用 = 覆盖（热重载不会出现两行）。例：
///   ntl_menu_add("mymod.speed", "i18n:mymod.speed", "2x", "mymod_toggle_speed", "i18n:mymod.d_speed", "mymod");
if (!variable_global_exists("ntl_menu_mods")) global.ntl_menu_mods = ds_list_create();
var _id = string(argument[0]);
var _label = (argument_count > 1) ? string(argument[1]) : _id;
var _value = (argument_count > 2) ? string(argument[2]) : "";
var _action = (argument_count > 3) ? string(argument[3]) : "";
var _desc = (argument_count > 4) ? string(argument[4]) : "";      // 这一项的说明行（画在面板底部说明区）
var _owner = (argument_count > 5) ? string(argument[5]) : "";     // 归属 mod（分组标题用）
for (var _i = 0; _i < ds_list_size(global.ntl_menu_mods); _i += 1)
{
    var _m = ds_list_find_value(global.ntl_menu_mods, _i);
    if (string(ds_map_find_value(_m, "id")) == _id)
    {
        ds_map_replace(_m, "label", _label);
        ds_map_replace(_m, "value", _value);
        ds_map_replace(_m, "action", _action);
        if (argument_count > 4) ds_map_replace(_m, "desc", _desc);
        if (argument_count > 5) ds_map_replace(_m, "owner", _owner);
        if (!variable_global_exists("ntl_modmenu_rows_cache")) global.ntl_modmenu_rows_cache = undefined;
        global.ntl_modmenu_rows_cache = undefined;                // 行内容变了，缓存作废
        return 1;
    }
}
var _e = ds_map_create();
ds_map_add(_e, "id", _id);
ds_map_add(_e, "label", _label);
ds_map_add(_e, "value", _value);
ds_map_add(_e, "action", _action);
ds_map_add(_e, "desc", _desc);
ds_map_add(_e, "owner", _owner);
ds_list_add(global.ntl_menu_mods, _e);
global.ntl_modmenu_rows_cache = undefined;                        // 行内容变了，缓存作废
ntl_log("menu-api", "[接口] 面板项已注册: " + _id + ((string_length(_owner) > 0) ? ("（mod: " + _owner + "）") : ""));
return 1;
