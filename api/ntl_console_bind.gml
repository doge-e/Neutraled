/// ntl_console_bind(keyName, command) —— 把命令绑定到按键（在控制台打开时生效）
///   bind F5 "profile"
///   bind F6 "reload"
if (!variable_global_exists("ntl_console_binds")) global.ntl_console_binds = ds_map_create();
var _k = string_upper(string(argument[0]));
var _c = (argument_count > 1) ? string(argument[1]) : "";

if (_k == "" || _c == "")
{
    ntl_console_log(ntl_t("bind.head"));
    var _keys = ntl_dsmap_keys(global.ntl_console_binds);
    for (var _i = 0; _i < array_length(_keys); _i += 1)
        ntl_console_log("  " + _keys[_i] + " -> " + string(ds_map_find_value(global.ntl_console_binds, _keys[_i])));
    if (array_length(_keys) == 0) ntl_console_log(ntl_t("list.none"));
    return 0;
}

if (string_copy(_c, 1, 1) == "\"") _c = string_delete(_c, 1, 1);
var _l = string_length(_c);
if (_l > 0 && string_copy(_c, _l, 1) == "\"") _c = string_delete(_c, _l, 1);

if (ds_map_exists(global.ntl_console_binds, _k)) ds_map_replace(global.ntl_console_binds, _k, _c);
else ds_map_add(global.ntl_console_binds, _k, _c);
ntl_console_log(ntl_t("act.bound") + ": " + _k + " -> " + _c);
return 1;