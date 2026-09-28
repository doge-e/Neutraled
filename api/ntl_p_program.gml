/// ntl_p_program —— 老式脚本（每个函数一个同名脚本资源）
var _s = argument[0];
    var _n = ntl_p_node("program");
    var _stmts = ds_list_create();
    while (!ntl_p_is(_s, "eof", ""))
    {
        var _st = ntl_p_statement(_s);
        if (ds_map_find_value(_s, "err") != "") break;
        if (_st != undefined) ds_list_add(_stmts, _st);
    }
    ds_map_add(_n, "stmts", _stmts);
    return _n;
