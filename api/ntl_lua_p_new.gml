/// ntl_lua_p_new(tokens) —— 创建 Lua 解析器状态
var _s = ds_map_create();
ds_map_add(_s, "toks", argument[0]);
ds_map_add(_s, "pos", 0);
ds_map_add(_s, "err", "");
return _s;
