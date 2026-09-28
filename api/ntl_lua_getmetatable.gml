/// ntl_lua_getmetatable(t) —— 取元表（无则 undefined）
///
/// ⚠️ 关键：ds_map_exists 对**无效句柄**会抛 "Data structure with index does not exist"，
///    不是返回 false。所以必须先用 ds_exists 确认句柄有效。
///    （os.clock 触发过这个问题：取 os 表的元表时句柄失效）
var _t = argument[0];
if (_t == undefined || !is_real(_t)) return undefined;
if (!ds_exists(_t, ds_type_map)) return undefined;
if (!ds_map_exists(_t, "_ntlmt")) return undefined;
return ds_map_find_value(_t, "_ntlmt");
