/// ntl_lua_p_concat(s) —— .. 右结合
var _s = argument[0];
var _e = ntl_lua_p_add(_s);
if (ntl_lua_p_is(_s, ".."))
{
    ntl_lua_p_next(_s);
    var _n = ntl_lua_p_node("binop");
    ds_map_add(_n, "op", "..");
    ds_map_add(_n, "l", _e);
    ds_map_add(_n, "r", ntl_lua_p_concat(_s));   // 右结合
    return _n;
}
return _e;
