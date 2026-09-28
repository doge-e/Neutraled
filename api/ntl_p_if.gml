/// ntl_p_if —— 老式脚本（每个函数一个同名脚本资源）
var _s = argument[0];
    ntl_p_next(_s);   // if
    ntl_p_expect_op(_s, "(");
    var _n = ntl_p_node("if");
    ds_map_add(_n, "cond", ntl_p_expr(_s));
    ntl_p_expect_op(_s, ")");
    ds_map_add(_n, "then", ntl_p_statement(_s));
    ds_map_add(_n, "els", undefined);
    if (ntl_p_is(_s, "id", "else"))
    {
        ntl_p_next(_s);
        ds_map_replace(_n, "els", ntl_p_statement(_s));
    }
    return _n;
