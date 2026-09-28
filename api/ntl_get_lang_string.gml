// ntl_get_lang_string(key) —— 读取生效的文本（覆盖优先，其次 lang_map）
var _key = string(argument[0]);
if (variable_global_exists("ntl_lang_overrides") && ds_map_exists(global.ntl_lang_overrides, _key))
    return ds_map_find_value(global.ntl_lang_overrides, _key);
if (variable_global_exists("lang_map") && ds_map_exists(global.lang_map, _key))
    return ds_map_find_value(global.lang_map, _key);
return "--missing-string--";
