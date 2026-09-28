/// ntl_lua_p_mul(s) —— * / // %
var _s = argument[0];
var _e = ntl_lua_p_unary(_s);
while (1)
{
    var _op = "";
    if (ntl_lua_p_is(_s, "*")) _op = "*";
    else if (ntl_lua_p_is(_s, "/")) _op = "/";
    else if (ntl_lua_p_is(_s, "//")) _op = "//";
    else if (ntl_lua_p_is(_s, "%")) _op = "%";
    else break;
    ntl_lua_p_next(_s);
    var _n = ntl_lua_p_node("binop");
    ds_map_add(_n, "op", _op);
    ds_map_add(_n, "l", _e);
    ds_map_add(_n, "r", ntl_lua_p_unary(_s));
    _e = _n;
}
return _e;
