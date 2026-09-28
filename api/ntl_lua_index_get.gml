/// ntl_lua_index_get(t, k) —— 带 __index 的取值
var _t = argument[0];
var _k = argument[1];
if (_t == undefined) return undefined;
if (is_real(_t))
{
    var _dk = ntl_lua_key(_k);
    if (ds_map_exists(_t, _dk)) return ds_map_find_value(_t, _dk);
}
// __index 链（最大深度 16 防止死循环）
var _cur = _t;
var _guard = 0;
while (_cur != undefined && _guard < 16)
{
    var _idx = ntl_lua_metamethod(_cur, "__index");
    if (_idx == undefined) break;
    if (is_real(_idx))              // __index 是表 → 继续查
    {
        var _dk2 = ntl_lua_key(_k);
        if (ds_map_exists(_idx, _dk2)) return ds_map_find_value(_idx, _dk2);
        _cur = _idx;
    }
    else                             // __index 是函数 → 调用
        return ntl_lua_call(_idx, [_t, _k]);
    _guard += 1;
}
return undefined;
