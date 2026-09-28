/// ntl_lua_p_accept(s, text) —— 若匹配则消费并返回 1
if (ntl_lua_p_is(argument[0], argument[1])) { ntl_lua_p_next(argument[0]); return 1; }
return 0;
