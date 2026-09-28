/// ntl_asset_export(kind, name, value) —— 把一个**游戏资源**导出给其他 mod 使用
///
/// kind: "sprite" / "sound" / "object" / "map" / "path" / "data"
///
/// 用法（mod A）：
///   local spr = ntl_sprite_from_file(__mod_dir .. "/kris_custom.png")
///   ntl_asset_export("sprite", "kris_custom", spr)
///
/// 其他 mod 就能：
///   local spr = ntl_asset_import("a.mod", "kris_custom")
///   draw_sprite(spr, 0, x, y)
if (!variable_global_exists("ntl_asset_reg")) global.ntl_asset_reg = ds_map_create();

var _kind = string_lower(string(argument[0]));
var _name = string(argument[1]);
var _value = (argument_count > 2) ? argument[2] : undefined;

var _self = "unknown";
if (variable_global_exists("ntl_current_mod") && string(global.ntl_current_mod) != "")
    _self = string(global.ntl_current_mod);

// 结构：reg[modId][kind][name] = value
if (!ds_map_exists(global.ntl_asset_reg, _self))
    ds_map_add(global.ntl_asset_reg, _self, ds_map_create());
var _mod = ds_map_find_value(global.ntl_asset_reg, _self);
if (!is_real(_mod) || !ds_exists(_mod, ds_type_map)) return 0;

if (!ds_map_exists(_mod, _kind)) ds_map_add(_mod, _kind, ds_map_create());
var _kindMap = ds_map_find_value(_mod, _kind);
if (!is_real(_kindMap) || !ds_exists(_kindMap, ds_type_map)) return 0;

if (ds_map_exists(_kindMap, _name)) ds_map_replace(_kindMap, _name, _value);
else ds_map_add(_kindMap, _name, _value);

ntl_log("asset", "[共享] " + _self + " 导出 " + _kind + ":" + _name);
return 1;
