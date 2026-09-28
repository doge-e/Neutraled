/// ntl_lua_table_register(handle) —— 把新建的 Lua 表登记到全局表注册表
var _h = argument[0];
if (!is_real(_h)) return 0;
if (!variable_global_exists("ntl_lua_tables")) global.ntl_lua_tables = ds_map_create();
if (!ds_map_exists(global.ntl_lua_tables, string(_h)))
    ds_map_add(global.ntl_lua_tables, string(_h), 1);
return 1;
