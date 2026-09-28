/// ntl_lua_p_chunk(s) —— 解析整个 chunk，返回语句数组（错误写入 s.err）
var _s = argument[0];
if (!is_real(_s) || !ds_exists(_s, ds_type_map)) return undefined;
var _stmts = ntl_lua_p_block(_s);
if (ds_map_find_value(_s, "err") == "")
{
    var _t = ntl_lua_p_peek(_s, 0);
    if (ds_map_find_value(_t, "t") != "eof")
        ntl_lua_p_err(_s, "unexpected '" + string(ds_map_find_value(_t, "v")) + "' after chunk");
}
return _stmts;
