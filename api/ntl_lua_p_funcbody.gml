/// ntl_lua_p_funcbody(s, node) —— ( parlist ) block end
var _s = argument[0];
var _node = argument[1];
var _params = [];
var _isVararg = 0;

ntl_lua_p_expect(_s, "(");
if (!ntl_lua_p_is(_s, ")"))
{
    while (1)
    {
        if (ntl_lua_p_is(_s, "...")) { ntl_lua_p_next(_s); _isVararg = 1; break; }
        var _pt = ntl_lua_p_next(_s);
        array_push(_params, ds_map_find_value(_pt, "v"));
        if (!ntl_lua_p_accept(_s, ",")) break;
    }
}
ntl_lua_p_expect(_s, ")");
ds_map_add(_node, "params", _params);
ds_map_add(_node, "vararg", _isVararg);
ds_map_add(_node, "body", ntl_lua_p_block(_s));
ntl_lua_p_expect(_s, "end");
return _node;
