/// ntl_mod_export(name, value) —— 把函数/常量导出给其他 mod 使用
///
/// 用法（在 mod 的 main.lua 里）：
///   function my_api(a, b) return a + b end
///   ntl_mod_export("add", my_api)
///   ntl_mod_export("VERSION", "1.2.0")
///
/// 其他 mod 就能：
///   local them = ntl_mod_require("my.mod")
///   them.add(1, 2)      -- 3
var _name = string(argument[0]);
var _val = (argument_count > 1) ? argument[1] : undefined;

if (!variable_global_exists("ntl_mod_reg")) ntl_mod_registry_init();

// 优先用专用变量（live_emit 设置），回退到 ctx_module
var _self = "unknown";
if (variable_global_exists("ntl_current_mod") && string(global.ntl_current_mod) != "")
    _self = string(global.ntl_current_mod);
else if (variable_global_exists("ntl_ctx_module") && string(global.ntl_ctx_module) != "")
    _self = string(global.ntl_ctx_module);
ntl_log("mod", "[互操作] " + _self + " 导出 " + _name);
if (!ds_map_exists(global.ntl_mod_reg, _self))
{
    var _newRec = ds_map_create();
    ds_map_add(_newRec, "id", _self);
    var _expTbl = ntl_lua_table_new();
    ntl_lua_table_register(_expTbl);   // ★ 确保登记（跨 mod 读取得靠它）
    ds_map_add(_newRec, "exports", _expTbl);
    ds_map_add(_newRec, "state", ds_map_create());
    ds_map_add(global.ntl_mod_reg, _self, _newRec);
}
var _rec2 = ds_map_find_value(global.ntl_mod_reg, _self);
if (!is_real(_rec2) || !ds_exists(_rec2, ds_type_map)) return 0;
var _exp = ds_map_find_value(_rec2, "exports");
ntl_lua_table_set(_exp, _name, _val);
return 1;
