// ntl_apply_lang_overrides() —— 把覆盖表写入 global.lang_map（须在语言加载完成后调用）
if (!variable_global_exists("ntl_lang_overrides")) return 0;
if (!variable_global_exists("lang_map")) return 0;
if (!is_undefined(global.lang_map) == false) return 0;

var _n = 0;
var _keys = ntl_dsmap_keys(global.ntl_lang_overrides);
for (var _i = 0; _i < array_length(_keys); _i++)
{
    var _k = _keys[_i];
    if (variable_global_exists("lang_missing_map") && ds_map_exists(global.lang_missing_map, _k))
        ds_map_delete(global.lang_missing_map, _k);
    ds_map_replace(global.lang_map, _k, ds_map_find_value(global.ntl_lang_overrides, _k));
    _n += 1;
}
return _n;
