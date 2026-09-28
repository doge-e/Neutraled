/// ntl_p_unary —— 老式脚本（每个函数一个同名脚本资源）
var _s = argument[0];
    if (ntl_p_is_op(_s, "-") || ntl_p_is_op(_s, "!"))
    {
        var _op = ds_map_find_value(ntl_p_next(_s), "v");
        var _n = ntl_p_node("un");
        ds_map_add(_n, "op", _op);
        ds_map_add(_n, "e", ntl_p_unary(_s));
        return _n;
    }
    return ntl_p_primary(_s);
