/// ntl_lua_p_prefix(s) —— 变量 / 括号 / 索引 / 字段 / 调用 / 方法调用
var _s = argument[0];
var _t = ntl_lua_p_peek(_s, 0);
var _ty = ds_map_find_value(_t, "t");
var _v = ds_map_find_value(_t, "v");

var _e = undefined;
if (_ty == "name")
{
    ntl_lua_p_next(_s);
    _e = ntl_lua_p_node("var");
    ds_map_add(_e, "name", _v);
}
else if (_ty == "op" && _v == "(")
{
    ntl_lua_p_next(_s);
    _e = ntl_lua_p_expr(_s);
    ntl_lua_p_expect(_s, ")");
}
else
{
    ntl_lua_p_err(_s, "unexpected token '" + string(_v) + "'");
    _e = ntl_lua_p_node("nil");
    ntl_lua_p_next(_s);
    return _e;
}

// 后缀链：.name / [expr] / (args) / :name(args) / string / table
while (1)
{
    if (ntl_lua_p_is(_s, "."))
    {
        ntl_lua_p_next(_s);
        var _nt = ntl_lua_p_next(_s);
        var _n1 = ntl_lua_p_node("field");
        ds_map_add(_n1, "obj", _e);
        ds_map_add(_n1, "name", ds_map_find_value(_nt, "v"));
        _e = _n1;
        continue;
    }
    if (ntl_lua_p_is(_s, "["))
    {
        ntl_lua_p_next(_s);
        var _idx = ntl_lua_p_expr(_s);
        ntl_lua_p_expect(_s, "]");
        var _n2 = ntl_lua_p_node("index");
        ds_map_add(_n2, "obj", _e);
        ds_map_add(_n2, "idx", _idx);
        _e = _n2;
        continue;
    }
    if (ntl_lua_p_is(_s, ":"))
    {
        ntl_lua_p_next(_s);
        var _mt = ntl_lua_p_next(_s);
        var _c = ntl_lua_p_node("method");
        ds_map_add(_c, "obj", _e);
        ds_map_add(_c, "name", ds_map_find_value(_mt, "v"));
        ds_map_add(_c, "args", ntl_lua_p_args(_s));
        _e = _c;
        continue;
    }
    var _nt2 = ntl_lua_p_peek(_s, 0);
    var _ty2 = ds_map_find_value(_nt2, "t");
    if (ntl_lua_p_is(_s, "(") || _ty2 == "str" ||
        (ntl_lua_p_is(_s, "{")))
    {
        var _c2 = ntl_lua_p_node("call");
        ds_map_add(_c2, "fn", _e);
        ds_map_add(_c2, "args", ntl_lua_p_args(_s));
        _e = _c2;
        continue;
    }
    break;
}
return _e;
