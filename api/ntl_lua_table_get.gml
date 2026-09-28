/// ntl_lua_table_get(t, k) —— 取值（不存在返回 undefined）
var _t = argument[0];
if (_t == undefined) return undefined;
if (!is_real(_t)) return undefined;
var _dk = ntl_lua_key(argument[1]);
if (ds_map_exists(_t, _dk)) return ds_map_find_value(_t, _dk);
return undefined;
