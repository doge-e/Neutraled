/// ntl_live_split_args(s) —— 按空格分割参数（支持 "引号包裹" 的参数）
var _s = string_trim(string(argument[0]));
var _out = [];
if (_s == "") return _out;

var _cur = "";
var _inQ = false;
var _i = 1;
while (_i <= string_length(_s))
{
    var _c = string_copy(_s, _i, 1);
    if (_c == "\"") { _inQ = !_inQ; _i += 1; continue; }
    if (_c == " " && !_inQ)
    {
        if (string_length(_cur) > 0) { array_push(_out, _cur); _cur = ""; }
    }
    else _cur += _c;
    _i += 1;
}
if (string_length(_cur) > 0) array_push(_out, _cur);
return _out;
