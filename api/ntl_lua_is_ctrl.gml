/// ntl_lua_is_ctrl(v) —— 是否控制流信号
var _v = argument[0];
if (_v == undefined) return 0;
if (!is_real(_v)) return 0;
if (!ds_map_exists(_v, "_ntlctrl")) return 0;
return 1;
