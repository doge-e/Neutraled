/// ntl_json_esc(s) —— JSON 字符串转义（GM 的 json_encode 只接受 real，不能转字符串）
var _s = string(argument[0]);
var _out = "\"";
var _i = 1;
var _n = string_length(_s);
while (_i <= _n)
{
    var _c = string_copy(_s, _i, 1);
    if (_c == "\"") _out += "\\\"";
    else if (_c == "\\") _out += "\\\\";
    else if (_c == chr(10)) _out += "\\n";
    else if (_c == chr(13)) _out += "\\r";
    else if (_c == chr(9)) _out += "\\t";
    else _out += _c;
    _i += 1;
}
_out += "\"";
return _out;
