/// ntl_lua_env_new(parent) —— 新建作用域（parent 为 undefined 时为全局作用域）
var _e = ds_map_create();
ds_map_add(_e, "_ntlp", argument[0]);
return _e;
