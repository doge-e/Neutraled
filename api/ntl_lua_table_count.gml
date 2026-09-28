/// ntl_lua_table_count(t) —— 有效键数量（不含内部键）
var _t = argument[0];
if (!is_real(_t) || !ds_exists(_t, ds_type_map)) return 0;
var _keys = ntl_dsmap_keys(_t);
var _n = 0;
for (var _i = 0; _i < array_length(_keys); _i += 1)
{
    var _k = _keys[_i];
    if (string_copy(string(_k), 1, 1) == chr(1)) continue;
    _n += 1;
}
return _n;
