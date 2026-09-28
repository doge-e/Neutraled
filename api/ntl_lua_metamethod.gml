/// ntl_lua_metamethod(t, name) —— 取元方法（如 "__index"），无则 undefined
var _mt = ntl_lua_getmetatable(argument[0]);
if (_mt == undefined || !is_real(_mt)) return undefined;
var _m = ntl_lua_table_get(_mt, argument[1]);
return _m;
