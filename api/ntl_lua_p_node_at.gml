/// ntl_lua_p_node_at(s, type) —— 新建带行号的 AST 节点
var _s = argument[0];
var _n = ntl_lua_p_node(argument[1]);
var _t = ntl_lua_p_peek(_s, 0);
ds_map_add(_n, "line", ds_map_find_value(_t, "line"));
return _n;
