/// ntl_lua_env_set(env, name, value) —— 就近赋值
var _e = argument[0];
var _n = string(argument[1]);
var _v = argument[2];
if (_e == undefined || !is_real(_e)) return _v;
var _cur = _e;
var _guard = 0;
while (_cur != undefined && is_real(_cur) && ds_exists(_cur, ds_type_map) && _guard < 64)
{
    if (ds_map_exists(_cur, _n)) { ds_map_replace(_cur, _n, _v); return _v; }
    _cur = ds_map_find_value(_cur, "_ntlp");
    _guard += 1;
}
if (ds_map_exists(_e, _n)) ds_map_replace(_e, _n, _v);
else ds_map_add(_e, _n, _v);
return _v;
