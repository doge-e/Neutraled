/// ntl_lua_p_expect(s, text) —— 期望指定关键字/运算符
var _s = argument[0];
var _text = string(argument[1]);
if (ntl_lua_p_is(_s, _text)) { ntl_lua_p_next(_s); return 1; }
var _t = ntl_lua_p_peek(_s, 0);
var _got = ds_map_find_value(_t, "v");
if (_got == "") _got = ds_map_find_value(_t, "t");
ntl_lua_p_err(_s, "expected '" + _text + "' but got '" + string(_got) + "'");
return 0;
