/// ntl_lua_p_simple(s) —— 字面量 / 表 / 函数 / 前缀表达式
var _s = argument[0];
var _t = ntl_lua_p_peek(_s, 0);
var _ty = ds_map_find_value(_t, "t");
var _v = ds_map_find_value(_t, "v");

if (_ty == "num") { ntl_lua_p_next(_s); var _n = ntl_lua_p_node("num"); ds_map_add(_n, "v", real(_v)); return _n; }
if (_ty == "str") { ntl_lua_p_next(_s); var _n2 = ntl_lua_p_node("str"); ds_map_add(_n2, "v", _v); return _n2; }
if (_ty == "kw")
{
    if (_v == "nil")   { ntl_lua_p_next(_s); return ntl_lua_p_node("nil"); }
    if (_v == "true")  { ntl_lua_p_next(_s); return ntl_lua_p_node("true"); }
    if (_v == "false") { ntl_lua_p_next(_s); return ntl_lua_p_node("false"); }
    if (_v == "function")
    {
        ntl_lua_p_next(_s);
        var _fn = ntl_lua_p_node("function");
        ntl_lua_p_funcbody(_s, _fn);
        return _fn;
    }
}
if (_ty == "op" && _v == "...") { ntl_lua_p_next(_s); return ntl_lua_p_node("vararg"); }
if (_ty == "op" && _v == "{") return ntl_lua_p_table(_s);
return ntl_lua_p_prefix(_s);
