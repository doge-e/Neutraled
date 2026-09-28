/// ntl_lua_p_unary(s) —— not # - ~
var _s = argument[0];
if (ntl_lua_p_is(_s, "not") || ntl_lua_p_is(_s, "#") || ntl_lua_p_is(_s, "-") || ntl_lua_p_is(_s, "~"))
{
    var _op = ds_map_find_value(ntl_lua_p_next(_s), "v");
    var _n = ntl_lua_p_node("unop");
    ds_map_add(_n, "op", _op);
    ds_map_add(_n, "e", ntl_lua_p_unary(_s));
    return _n;
}
return ntl_lua_p_pow(_s);
