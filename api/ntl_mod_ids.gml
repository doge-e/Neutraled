/// ntl_mod_ids() —— 只返回 mod id 的数组（Lua 表形式，便于 # 和 ipairs）
if (!variable_global_exists("ntl_mod_reg")) return ntl_lua_table_new();
var _keys = ntl_dsmap_keys(global.ntl_mod_reg);
var _t = ntl_lua_table_new();
for (var _i = 0; _i < array_length(_keys); _i += 1)
    ntl_lua_table_set(_t, _i + 1, string(_keys[_i]));
return _t;
