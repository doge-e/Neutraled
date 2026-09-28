/// ntl_lua_p_block(s) —— 语句序列，直到块结束关键字
var _s = argument[0];
var _stmts = [];
while (1)
{
    var _t = ntl_lua_p_peek(_s, 0);
    var _ty = ds_map_find_value(_t, "t");
    var _v = ds_map_find_value(_t, "v");
    if (_ty == "eof") break;
    if (_ty == "kw" && (_v == "end" || _v == "else" || _v == "elseif" || _v == "until")) break;
    var _st = ntl_lua_p_stat(_s);
    if (_st != undefined) array_push(_stmts, _st);
    if (ds_map_find_value(_s, "err") != "") break;
    ntl_lua_p_accept(_s, ";");
    // return / break 之后块应结束
    if (ds_map_find_value(_st, "k") == "return" || ds_map_find_value(_st, "k") == "break") break;
}
return _stmts;
