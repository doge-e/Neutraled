/// ntl_lua_p_is(s, text, offset=0) —— 当前 token 是否为指定关键字/运算符
var _s = argument[0];
var _text = string(argument[1]);
var _off = (argument_count > 2) ? argument[2] : 0;
var _t = ntl_lua_p_peek(_s, _off);
var _ty = ds_map_find_value(_t, "t");
if (_ty != "kw" && _ty != "op") return 0;
return (ds_map_find_value(_t, "v") == _text) ? 1 : 0;
