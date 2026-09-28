/// ntl_asset_import(modId, name, kind) —— 取得其他 mod 导出的资源
///
/// 用法（mod B）：
///   local spr = ntl_asset_import("a.mod", "kris_custom")              -- 自动找
///   local spr = ntl_asset_import("a.mod", "kris_custom", "sprite")    -- 指定类型
var _modId = string(argument[0]);
var _name = string(argument[1]);
var _kind = (argument_count > 2) ? string_lower(string(argument[2])) : "";

if (!variable_global_exists("ntl_asset_reg"))
{
    ntl_log("asset", "[共享] 资源注册表不存在");
    return undefined;
}
if (!ds_map_exists(global.ntl_asset_reg, _modId))
{
    ntl_log("asset", "[共享] 找不到 mod: " + _modId);
    return undefined;
}
var _mod = ds_map_find_value(global.ntl_asset_reg, _modId);

// 指定类型 → 直接查
if (_kind != "")
{
    if (!ds_map_exists(_mod, _kind)) return undefined;
    var _km = ds_map_find_value(_mod, _kind);
    if (!is_real(_km) || !ds_exists(_km, ds_type_map)) return undefined;
    if (!ds_map_exists(_km, _name)) return undefined;
    return ds_map_find_value(_km, _name);
}

// 未指定 → 遍历所有类型找（sprite 优先）
var _order = ["sprite", "sound", "object", "map", "path", "data"];
for (var _i = 0; _i < array_length(_order); _i += 1)
{
    if (!ds_map_exists(_mod, _order[_i])) continue;
    var _km2 = ds_map_find_value(_mod, _order[_i]);
    if (!is_real(_km2) || !ds_exists(_km2, ds_type_map)) continue;
    if (ds_map_exists(_km2, _name))
    {
        ntl_log("asset", "[共享] " + _modId + " 的 " + _order[_i] + ":" + _name + " 已取到");
        return ds_map_find_value(_km2, _name);
    }
}
ntl_log("asset", "[共享] 找不到资源: " + _modId + "/" + _name);
return undefined;
