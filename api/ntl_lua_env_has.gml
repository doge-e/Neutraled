/// ntl_lua_env_has(env, name) —— 变量是否已定义
var _e = argument[0];
var _n = string(argument[1]);
var _guard = 0;
while (_e != undefined && is_real(_e) && ds_exists(_e, ds_type_map) && _guard < 64)
{
    if (ds_map_exists(_e, _n)) return 1;
    _e = ds_map_find_value(_e, "_ntlp");
    _guard += 1;
}
return 0;
