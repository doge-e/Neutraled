/// ntl_lua_is_table(v) —— 精确判断是否为 Lua 表
/// GML 的 ds_map 句柄是实数，与普通数字无法区分；因此所有 Lua 表在创建时登记到
/// global.ntl_lua_tables，这里查登记表来判定（比试探键名可靠）
var _v = argument[0];
if (!is_real(_v)) return 0;
if (!variable_global_exists("ntl_lua_tables")) return 0;
return ds_map_exists(global.ntl_lua_tables, string(_v));
