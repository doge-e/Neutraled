/// ntl_p_while —— 老式脚本（每个函数一个同名脚本资源）
var _s = argument[0];
    ntl_p_next(_s);   // while
    ntl_p_expect_op(_s, "(");
    var _n = ntl_p_node("while");
    ds_map_add(_n, "cond", ntl_p_expr(_s));
    ntl_p_expect_op(_s, ")");
    ds_map_add(_n, "body", ntl_p_statement(_s));
    return _n;
