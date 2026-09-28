/// ntl_lua_chunk_reg() —— 取（必要时创建）load 产生的代码块注册表
if (!variable_global_exists("ntl_lua_chunks"))
{
    global.ntl_lua_chunks = ds_map_create();
    global.ntl_lua_chunk_seq = 0;
}
return global.ntl_lua_chunks;