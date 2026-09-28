/// ntl_lua_index_set(t, k, v) —— 带 __newindex 的赋值
var _t = argument[0];
var _k = argument[1];
var _v = argument[2];
if (_t == undefined || !is_real(_t)) return _v;
var _dk = ntl_lua_key(_k);
if (ds_map_exists(_t, _dk)) { ds_map_replace(_t, _dk, _v); return _v; }
// 键不存在时才走 __newindex
var _ni = ntl_lua_metamethod(_t, "__newindex");
if (_ni != undefined)
{
    if (is_real(_ni)) { ntl_lua_table_set(_ni, _k, _v); return _v; }
    ntl_lua_call(_ni, [_t, _k, _v]);
    return _v;
}
// 无元方法 → 直接写入（并维护数组边界）
return ntl_lua_table_set(_t, _k, _v);
