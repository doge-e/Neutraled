/// ntl_lua_env_get(env, name) —— 沿作用域链查找变量
var _e = argument[0];
var _n = string(argument[1]);
var _guard = 0;
while (_e != undefined && is_real(_e) && ds_exists(_e, ds_type_map) && _guard < 64)
{
    var _v = ds_map_find_value(_e, _n);
    if (_v != undefined) return _v;
    _e = ds_map_find_value(_e, "_ntlp");
    _guard += 1;
}
return undefined;
