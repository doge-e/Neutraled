/// ntl_lua_tostring(v) —— Lua 风格字符串化
/// 注意：GML 里 1 == true 为真，必须先判断表/函数，再判断 real
var _v = argument[0];
if (_v == undefined) return "nil";
if (is_real(_v))
{
    if (ntl_lua_is_fn(_v)) return "function";
    if (ntl_lua_is_table(_v)) return "table";            // Lua 表（走登记表精确判定）
    if (_v == floor(_v) && abs(_v) < power(10, 15)) return string(round(_v));
    var _s = string_format(_v, 0, 8);
    while (string_length(_s) > 0 && string_copy(_s, string_length(_s), 1) == "0")
        _s = string_delete(_s, string_length(_s), 1);
    if (string_length(_s) > 0 && string_copy(_s, string_length(_s), 1) == ".")
        _s = string_delete(_s, string_length(_s), 1);
    return _s;
}
if (is_bool(_v))
{
    if (_v) return "true";
    return "false";
}
if (is_string(_v)) return _v;
return "table";
