/// ntl_lua_assign_target(env, node, value) —— 给变量/字段/索引赋值
var _env = argument[0];
var _n = argument[1];
var _v = argument[2];
var _k = ds_map_find_value(_n, "k");
if (_k == "var") return ntl_lua_env_set(_env, ds_map_find_value(_n, "name"), _v);
if (_k == "field")
{
    var _o = ntl_lua_ex(_env, ds_map_find_value(_n, "obj"));
    return ntl_lua_index_set(_o, ds_map_find_value(_n, "name"), _v);
}
if (_k == "index")
{
    var _o2 = ntl_lua_ex(_env, ds_map_find_value(_n, "obj"));
    var _i2 = ntl_lua_ex(_env, ds_map_find_value(_n, "idx"));
    return ntl_lua_index_set(_o2, _i2, _v);
}
ntl_lua_rt_err("cannot assign to this expression");
return undefined;
