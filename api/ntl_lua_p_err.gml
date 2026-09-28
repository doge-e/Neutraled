/// ntl_lua_p_err(s, msg) —— 记录解析错误
var _s = argument[0];
var _msg = string(argument[1]);
if (!is_real(_s) || !ds_exists(_s, ds_type_map)) return 0;
if (ds_map_find_value(_s, "err") != "") return 0;
var _t = ntl_lua_p_peek(_s, 0);
var _line = ds_map_find_value(_t, "line");
ds_map_replace(_s, "err", "line " + string(_line) + ": " + _msg);
return 0;
