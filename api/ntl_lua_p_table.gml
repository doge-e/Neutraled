/// ntl_lua_p_table(s) —— { fieldlist }，字段：无键值 / Name=exp / [exp]=exp
var _s = argument[0];
ntl_lua_p_expect(_s, "{");
var _fields = [];

while (!ntl_lua_p_is(_s, "}"))
{
    var _f = ds_map_create();
    if (ntl_lua_p_is(_s, "["))
    {
        ntl_lua_p_next(_s);
        ds_map_add(_f, "key", ntl_lua_p_expr(_s));
        ntl_lua_p_expect(_s, "]");
        ntl_lua_p_expect(_s, "=");
        ds_map_add(_f, "val", ntl_lua_p_expr(_s));
    }
    else
    {
        var _t = ntl_lua_p_peek(_s, 0);
        var _isNameKey = 0;
        if (ds_map_find_value(_t, "t") == "name" && ntl_lua_p_is(_s, "=", 1)) _isNameKey = 1;
        if (_isNameKey)
        {
            var _nm = ntl_lua_p_next(_s);
            ntl_lua_p_next(_s);   // '='
            var _kn = ntl_lua_p_node("str");
            ds_map_add(_kn, "v", ds_map_find_value(_nm, "v"));
            ds_map_add(_f, "key", _kn);
            ds_map_add(_f, "val", ntl_lua_p_expr(_s));
        }
        else
        {
            ds_map_add(_f, "key", 0);   // 0 = 数组式（按顺序）
            ds_map_add(_f, "val", ntl_lua_p_expr(_s));
        }
    }
    array_push(_fields, _f);
    if (!ntl_lua_p_accept(_s, ",") && !ntl_lua_p_accept(_s, ";")) break;
}
ntl_lua_p_expect(_s, "}");

var _n = ntl_lua_p_node("table");
ds_map_add(_n, "fields", _fields);
return _n;
