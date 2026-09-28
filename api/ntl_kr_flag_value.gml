/// ntl_kr_flag_value(args) —— 从参数表里取出 flag 的值（最后一个非表参数）
var _args = argument[0];
var _n = array_length(_args);
var _val = undefined;
for (var _i = 0; _i < _n; _i += 1)
{
    var _v = _args[_i];
    if (is_string(_v)) { _val = _v; continue; }
    if (_v == true || _v == false) { _val = _v; continue; }
    if (is_real(_v))
    {
        if (ntl_lua_is_table(_v) == 0 && ntl_lua_is_fn(_v) == 0) _val = _v;
    }
}
return _val;
