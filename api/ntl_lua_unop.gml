/// ntl_lua_unop(op, a) —— 一元运算
var _op = string(argument[0]);
var _a = argument[1];

if (is_real(_a))
{
    var _mu = ntl_lua_meta_unop(_op, _a);
    if (ds_map_find_value(_mu, "ok") == 1)
    {
        var _muv = ds_map_find_value(_mu, "value");
        ds_map_destroy(_mu);
        return _muv;
    }
    ds_map_destroy(_mu);
}

switch (_op)
{
    case "-": return -real(_a);
    case "not": return ntl_lua_truthy(_a) ? 0 : 1;
    case "#": return ntl_lua_table_len(_a);
    case "~": return ~round(_a);
}
return _a;
