/// ntl_lua_p_args(s) —— 函数调用参数：'(' explist ')' | table | string
var _s = argument[0];
var _args = [];
if (ntl_lua_p_is(_s, "("))
{
    ntl_lua_p_next(_s);
    if (!ntl_lua_p_is(_s, ")"))
    {
        array_push(_args, ntl_lua_p_expr(_s));
        while (ntl_lua_p_accept(_s, ",")) array_push(_args, ntl_lua_p_expr(_s));
    }
    ntl_lua_p_expect(_s, ")");
    return _args;
}
if (ntl_lua_p_is(_s, "{")) { array_push(_args, ntl_lua_p_table(_s)); return _args; }
var _t = ntl_lua_p_peek(_s, 0);
if (ds_map_find_value(_t, "t") == "str")
{
    ntl_lua_p_next(_s);
    var _n = ntl_lua_p_node("str");
    ds_map_add(_n, "v", ds_map_find_value(_t, "v"));
    array_push(_args, _n);
    return _args;
}
ntl_lua_p_err(_s, "expected function arguments");
return _args;
