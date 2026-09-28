/// ntl_lua_meta_unop(op, a) —— 一元元方法（__len / __unm / __tostring）
var _op = string(argument[0]);
var _a = argument[1];
var _name = "";
if (_op == "#") _name = "__len";
else if (_op == "-") _name = "__unm";
var _out = ds_map_create();
ds_map_add(_out, "ok", 0);
ds_map_add(_out, "value", undefined);
if (_name == "") return _out;
var _m = ntl_lua_metamethod(_a, _name);
if (_m == undefined) return _out;
ds_map_replace(_out, "ok", 1);
ds_map_replace(_out, "value", ntl_lua_call(_m, [_a]));
return _out;
