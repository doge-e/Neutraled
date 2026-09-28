/// ntl_mod_require(modId) —— 取得另一个 mod 的导出表（用于跨 mod 调用）
///
/// 用法（Lua）：
///   local other = ntl_mod_require("frostveil.core")
///   other.do_something(1, 2)
///
/// 返回一个 Lua 表：包含该 mod 通过 ntl_mod_export() 注册的函数与常量。
/// 若 mod 不存在或未启用 → 返回 nil（调用方应判空）
var _id = string(argument[0]);
if (!variable_global_exists("ntl_mod_reg")) ntl_mod_registry_init();
if (!ds_map_exists(global.ntl_mod_reg, _id))
{
    ntl_log("mod", "[互操作] 找不到 mod: " + _id + "（可能未安装或未启用）");
    return undefined;
}
var _rec = ds_map_find_value(global.ntl_mod_reg, _id);
if (!is_real(_rec) || !ds_exists(_rec, ds_type_map)) return undefined;
return ds_map_find_value(_rec, "exports");
