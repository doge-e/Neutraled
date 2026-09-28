/// ntl_lua_is_fn(v) —— 是否 Lua 函数值
/// ⚠️ 关键：ds_map_exists 对无效句柄会抛 Code Error（不是返回 false），
///    所以必须先查"Lua 对象登记表"确认它是真的 Lua 表，再试探函数标记键。
var _v = argument[0];
if (!is_real(_v)) return 0;
if (!variable_global_exists("ntl_lua_tables")) return 0;
if (!ds_map_exists(global.ntl_lua_tables, string(_v))) return 0;   // ★ 安全过滤
if (!ds_map_exists(_v, "_ntlfn")) return 0;
return 1;
