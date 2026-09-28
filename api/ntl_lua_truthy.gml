/// ntl_lua_truthy(v) —— Lua 真值判断（只有 nil 与 false 为假）
var _v = argument[0];
if (_v == undefined) return 0;
if (_v == false) return 0;
return 1;
