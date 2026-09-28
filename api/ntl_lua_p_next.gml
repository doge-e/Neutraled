/// ntl_lua_p_next(s) —— 消费并返回当前 token
var _s = argument[0];
if (!is_real(_s) || !ds_exists(_s, ds_type_map)) return undefined;
var _t = ntl_lua_p_peek(_s, 0);
ds_map_replace(_s, "pos", ds_map_find_value(_s, "pos") + 1);
return _t;
