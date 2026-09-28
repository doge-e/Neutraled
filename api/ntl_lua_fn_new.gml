/// ntl_lua_fn_new(node, env) —— 由 function 节点创建函数值
var _node = argument[0];
var _env = argument[1];
var _fn = ds_map_create();
ds_map_add(_fn, "_ntlfn", 1);          // 标记：Lua 函数
// ★ 关键：必须登记到 ntl_lua_tables，否则 ntl_lua_is_fn 的安全过滤会拒绝它
//   （表现为：跨 mod 取到的函数"无法调用"，且返回值变成 __host:<变量名>）
ntl_lua_table_register(_fn);
ds_map_add(_fn, "params", ds_map_find_value(_node, "params"));
ds_map_add(_fn, "vararg", ds_map_find_value(_node, "vararg"));
ds_map_add(_fn, "body", ds_map_find_value(_node, "body"));
ds_map_add(_fn, "env", _env);           // 闭包环境
ds_map_add(_fn, "name", "");
return _fn;
