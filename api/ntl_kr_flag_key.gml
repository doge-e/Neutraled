/// ntl_kr_flag_key(args) —— 从参数表里取出 flag 键
/// 跳过表参数（方法调用的 self 或配置对象），返回第一个字符串或数字
var _args = argument[0];
var _n = array_length(_args);
for (var _i = 0; _i < _n; _i += 1)
{
    var _v = _args[_i];
    if (is_string(_v)) return _v;
    if (is_real(_v))
    {
        if (ntl_lua_is_table(_v) == 0 && ntl_lua_is_fn(_v) == 0) return _v;
    }
}
return undefined;
