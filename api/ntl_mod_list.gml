/// ntl_mod_list() —— 列出所有已注册的 mod（互操作视角）
/// ★ 返回 Lua 表（数组形式），Lua 里可以用 #mods 和 ipairs 遍历
if (!variable_global_exists("ntl_mod_reg")) return ntl_lua_table_new();
var _keys = ntl_dsmap_keys(global.ntl_mod_reg);

// 返回 Lua 表：每个元素是 { id, exports }
var _t = ntl_lua_table_new();
for (var _i = 0; _i < array_length(_keys); _i += 1)
{
    var _rec = ds_map_find_value(global.ntl_mod_reg, _keys[_i]);
    if (!is_real(_rec) || !ds_exists(_rec, ds_type_map)) continue;
    var _exp = ds_map_find_value(_rec, "exports");
    var _item = ntl_lua_table_new();
    ntl_lua_table_set(_item, "id", string(_keys[_i]));
    ntl_lua_table_set(_item, "exports", ntl_lua_table_count(_exp));
    ntl_lua_table_set(_t, _i + 1, _item);
}
return _t;
