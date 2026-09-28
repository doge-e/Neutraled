/// ntl_lua_compile(src) → ds_map { ok, ast, err }
var _src = string(argument[0]);
var _out = ds_map_create();

var _toks = ntl_lua_tok(_src);
var _s = ntl_lua_p_new(_toks);
var _ast = ntl_lua_p_chunk(_s);
var _err = ntl_lua_p_errmsg(_s);

ds_map_add(_out, "ok", (_err == "") ? 1 : 0);
ds_map_add(_out, "ast", _ast);
ds_map_add(_out, "err", _err);
ds_map_add(_out, "tokens", ds_list_size(_toks));
return _out;
