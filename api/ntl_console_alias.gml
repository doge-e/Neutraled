/// ntl_console_alias(aliasName, expansion) —— 定义命令别名
///   alias ff "goto 4; sleep 60; screenshot"
/// 之后输入 ff 就等同执行那一串
if (!variable_global_exists("ntl_console_aliases")) global.ntl_console_aliases = ds_map_create();
var _a = string(argument[0]);
var _e = (argument_count > 1) ? string(argument[1]) : "";

if (_a == "" || _e == "")
{
    ntl_console_log(ntl_t("alias.head"));
    var _keys = ntl_dsmap_keys(global.ntl_console_aliases);
    for (var _i = 0; _i < array_length(_keys); _i += 1)
        ntl_console_log("  " + _keys[_i] + " = " + string(ds_map_find_value(global.ntl_console_aliases, _keys[_i])));
    if (array_length(_keys) == 0) ntl_console_log(ntl_t("list.none"));
    return 0;
}

// 去掉引号
if (string_copy(_e, 1, 1) == "\"") _e = string_delete(_e, 1, 1);
var _l = string_length(_e);
if (_l > 0 && string_copy(_e, _l, 1) == "\"") _e = string_delete(_e, _l, 1);

if (ds_map_exists(global.ntl_console_aliases, _a)) ds_map_replace(global.ntl_console_aliases, _a, _e);
else ds_map_add(global.ntl_console_aliases, _a, _e);
ntl_console_log(ntl_t("act.alias") + ": " + _a + " = " + _e);
return 1;