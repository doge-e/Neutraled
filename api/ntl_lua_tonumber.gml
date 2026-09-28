/// ntl_lua_tonumber(v) —— 数字转换（失败返回 undefined）
var _v = argument[0];
if (is_real(_v)) return _v;
if (is_string(_v))
{
    var _t = _v;
    while (string_length(_t) > 0 && (string_copy(_t, 1, 1) == " " || string_copy(_t, 1, 1) == chr(9))) _t = string_delete(_t, 1, 1);
    while (string_length(_t) > 0 && string_copy(_t, string_length(_t), 1) == " ") _t = string_delete(_t, string_length(_t), 1);
    if (_t == "") return undefined;
    return real(_t);
}
return undefined;
