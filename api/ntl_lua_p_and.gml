/// ntl_lua_p_and(s)
var _s = argument[0];
var _e = ntl_lua_p_cmp(_s);
while (ntl_lua_p_is(_s, "and"))
{
    ntl_lua_p_next(_s);
    var _n = ntl_lua_p_node("binop");
    ds_map_add(_n, "op", "and");
    ds_map_add(_n, "l", _e);
    ds_map_add(_n, "r", ntl_lua_p_cmp(_s));
    _e = _n;
}
return _e;
