/// ntl_lua_p_pow(s) —— ^ 右结合，优先级高于一元
var _s = argument[0];
var _e = ntl_lua_p_simple(_s);
if (ntl_lua_p_is(_s, "^"))
{
    ntl_lua_p_next(_s);
    var _n = ntl_lua_p_node("binop");
    ds_map_add(_n, "op", "^");
    ds_map_add(_n, "l", _e);
    ds_map_add(_n, "r", ntl_lua_p_unary(_s));   // 右侧可再出现一元/幂
    return _n;
}
return _e;
