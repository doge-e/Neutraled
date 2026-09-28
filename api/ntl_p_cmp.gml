/// ntl_p_cmp —— 老式脚本（每个函数一个同名脚本资源）
var _s = argument[0];
    var _l = ntl_p_add(_s);
    while (ntl_p_is_op(_s, "==") || ntl_p_is_op(_s, "!=") || ntl_p_is_op(_s, "<") ||
           ntl_p_is_op(_s, ">") || ntl_p_is_op(_s, "<=") || ntl_p_is_op(_s, ">="))
    {
        var _op = ds_map_find_value(ntl_p_next(_s), "v");
        var _n = ntl_p_node("bin");
        ds_map_add(_n, "op", _op);
        ds_map_add(_n, "l", _l);
        ds_map_add(_n, "r", ntl_p_add(_s));
        _l = _n;
    }
    return _l;
