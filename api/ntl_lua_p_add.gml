/// ntl_lua_p_add(s) —— + -
var _s = argument[0];
var _e = ntl_lua_p_mul(_s);
while (ntl_lua_p_is(_s, "+") || ntl_lua_p_is(_s, "-"))
{
    var _op = ds_map_find_value(ntl_lua_p_next(_s), "v");
    var _n = ntl_lua_p_node("binop");
    ds_map_add(_n, "op", _op);
    ds_map_add(_n, "l", _e);
    ds_map_add(_n, "r", ntl_lua_p_mul(_s));
    _e = _n;
}
return _e;
