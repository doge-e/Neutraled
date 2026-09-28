/// ntl_lua_setmetatable(t, mt) —— 设置元表（mt 可为 undefined 清除）
var _t = argument[0];
var _mt = argument[1];
if (_t == undefined || !is_real(_t)) return _t;
if (ds_map_exists(_t, "_ntlmt")) ds_map_replace(_t, "_ntlmt", _mt);
else ds_map_add(_t, "_ntlmt", _mt);
return _t;
