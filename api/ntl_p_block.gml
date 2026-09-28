/// ntl_p_block —— 老式脚本（每个函数一个同名脚本资源）
var _s = argument[0];
    if (!ntl_p_expect_op(_s, "{")) return undefined;
    var _n = ntl_p_node("block");
    var _stmts = ds_list_create();
    while (!ntl_p_is_op(_s, "}") && !ntl_p_is(_s, "eof", ""))
    {
        var _st = ntl_p_statement(_s);
        if (ds_map_find_value(_s, "err") != "") break;
        if (_st != undefined) ds_list_add(_stmts, _st);
    }
    ntl_p_expect_op(_s, "}");
    ds_map_add(_n, "stmts", _stmts);
    return _n;
