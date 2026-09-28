/// ntl_lua_key_decode(dsKey) —— 反向解码（用于 pairs 遍历）
var _k = string(argument[0]);
if (string_length(_k) < 1) return _k;
var _p = string_copy(_k, 1, 1);
var _rest = string_delete(_k, 1, 1);
switch (_p)
{
    case "s": return _rest;
    case "n": return real(_rest);
    case "b": return (_rest == "1") ? true : false;
}
return _rest;
