/// ntl_t(key) —— 取当前语言的文案（只用于 Neutraled 新增的内容）
/// 找不到时返回 key 本身（便于发现缺翻译）
var _key = string(argument[0]);
if (!variable_global_exists("ntl_i18n")) return _key;
var _lang = variable_global_exists("ntl_lang") ? string(global.ntl_lang) : "zh";
if (!ds_map_exists(global.ntl_i18n, _lang)) return _key;
var _tbl = ds_map_find_value(global.ntl_i18n, _lang);
if (!is_real(_tbl) || !ds_exists(_tbl, ds_type_map)) return _key;
if (!ds_map_exists(_tbl, _key)) return _key;
return string(ds_map_find_value(_tbl, _key));