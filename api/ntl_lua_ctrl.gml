/// ntl_lua_ctrl(kind, vals) —— 控制流信号（return / break）
var _s = ds_map_create();
ds_map_add(_s, "_ntlctrl", string(argument[0]));
ds_map_add(_s, "vals", (argument_count > 1 && is_array(argument[1])) ? argument[1] : []);
return _s;
