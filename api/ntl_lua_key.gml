/// ntl_lua_key(k) —— 把 Lua 表键规范成 ds_map 键字符串（数字键与字符串键不冲突）
var _k = argument[0];
if (is_string(_k)) return "s" + _k;
if (_k == undefined) return "nil";
if (_k == true) return "b1";
if (_k == false) return "b0";
return "n" + string(_k);
