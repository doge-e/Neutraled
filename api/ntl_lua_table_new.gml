/// ntl_lua_table_new() —— 新建 Lua 表（用 ds_map 承载，含数组长度缓存）
var _t = ds_map_create();
ds_map_add(_t, "_ntlmt", 0);   // 元表占位
ds_map_add(_t, "_ntln", 0);            // 数组边界（# 运算用）
ntl_lua_table_register(_t);
return _t;
