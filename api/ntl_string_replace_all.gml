/// ntl_string_replace_all(s, find, repl) —— 全局替换（GM 没有内置的 string_replace_all）
var _s = string(argument[0]);
var _find = string(argument[1]);
var _repl = string(argument[2]);
if (_find == "") return _s;
var _out = "";
var _i = 1;
var _fl = string_length(_find);
while (_i <= string_length(_s))
{
    if (string_copy(_s, _i, _fl) == _find)
    {
        _out += _repl;
        _i += _fl;
    }
    else
    {
        _out += string_copy(_s, _i, 1);
        _i += 1;
    }
}
return _out;