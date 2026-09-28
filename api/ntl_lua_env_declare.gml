/// ntl_lua_env_declare(env, name, value) —— 声明局部变量
var _e = argument[0];
var _n = string(argument[1]);
var _v = argument[2];
if (_e == undefined || !is_real(_e)) return _v;
if (ds_map_exists(_e, _n)) ds_map_replace(_e, _n, _v);
else ds_map_add(_e, _n, _v);
return _v;
