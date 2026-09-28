/// ntl_lua_p_or(s) —— 最低优先级（左结合）
var _s = argument[0];
var _e = ntl_lua_p_and(_s);
while (ntl_lua_p_is(_s, "or"))
{
    ntl_lua_p_next(_s);
    var _n = ntl_lua_p_node("binop");
    ds_map_add(_n, "op", "or");
    ds_map_add(_n, "l", _e);
    ds_map_add(_n, "r", ntl_lua_p_and(_s));
    _e = _n;
}
return _e;
