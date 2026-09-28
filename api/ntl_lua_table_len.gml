/// ntl_lua_table_len(t) —— # 运算
var _t = argument[0];
if (is_string(_t)) return string_length(_t);
if (_t == undefined) return 0;
if (!is_real(_t)) return 0;
if (ds_map_exists(_t, "_ntln")) return ds_map_find_value(_t, "_ntln");
return 0;
