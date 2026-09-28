/// ntl_lua_meta_binop(op, a, b) —— 尝试用元方法处理二元运算（成功返回 ds_map{ok:1,value:...}）
var _op = string(argument[0]);
var _a = argument[1];
var _b = argument[2];

var _names = [];
if (_op == "+") _names = ["__add"];
else if (_op == "-") _names = ["__sub"];
else if (_op == "*") _names = ["__mul"];
else if (_op == "/") _names = ["__div"];
else if (_op == "%") _names = ["__mod"];
else if (_op == "^") _names = ["__pow"];
else if (_op == "..") _names = ["__concat"];
else if (_op == "==") _names = ["__eq"];
else if (_op == "<") _names = ["__lt"];
else if (_op == "<=") _names = ["__le"];

var _out = ds_map_create();
ds_map_add(_out, "ok", 0);
ds_map_add(_out, "value", undefined);
if (array_length(_names) == 0) return _out;

for (var _i = 0; _i < array_length(_names); _i += 1)
{
    var _m = ntl_lua_metamethod(_a, _names[_i]);
    if (_m == undefined) _m = ntl_lua_metamethod(_b, _names[_i]);
    if (_m == undefined) continue;
    ds_map_replace(_out, "ok", 1);
    ds_map_replace(_out, "value", ntl_lua_call(_m, [_a, _b]));
    return _out;
}
return _out;
