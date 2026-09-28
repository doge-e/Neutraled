/// ntl_asset_list() —— 列出所有可用的共享资源（Lua 表）
if (!variable_global_exists("ntl_asset_reg")) return ntl_lua_table_new();
var _t = ntl_lua_table_new();
var _mods = ntl_dsmap_keys(global.ntl_asset_reg);
var _idx = 0;
for (var _i = 0; _i < array_length(_mods); _i += 1)
{
    var _mid = string(_mods[_i]);
    var _mod = ds_map_find_value(global.ntl_asset_reg, _mid);
    if (!is_real(_mod) || !ds_exists(_mod, ds_type_map)) continue;
    var _kinds = ntl_dsmap_keys(_mod);
    for (var _j = 0; _j < array_length(_kinds); _j += 1)
    {
        var _kind = string(_kinds[_j]);
        var _km = ds_map_find_value(_mod, _kind);
        if (!is_real(_km) || !ds_exists(_km, ds_type_map)) continue;
        var _names = ntl_dsmap_keys(_km);
        for (var _k = 0; _k < array_length(_names); _k += 1)
        {
            _idx += 1;
            var _item = ntl_lua_table_new();
            ntl_lua_table_set(_item, "mod", _mid);
            ntl_lua_table_set(_item, "kind", _kind);
            ntl_lua_table_set(_item, "name", string(_names[_k]));
            ntl_lua_table_set(_t, _idx, _item);
        }
    }
}
return _t;
