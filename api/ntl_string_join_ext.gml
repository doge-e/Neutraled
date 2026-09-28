/// ntl_string_join_ext(sep, arr) —— 数组连接成字符串
var _sep = string(argument[0]);
var _arr = argument[1];
var _s = "";
for (var _i = 0; _i < array_length(_arr); _i += 1)
{
    if (_i > 0) _s += _sep;
    _s += string(_arr[_i]);
}
return _s;
