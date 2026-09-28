/// ntl_p_let —— 老式脚本（每个函数一个同名脚本资源）
var _s = argument[0];
    ntl_p_next(_s);   // let
    var _t = ntl_p_next(_s);
    if (ds_map_find_value(_t, "t") != "id") { ntl_p_err(_s, "expected variable name after let"); return undefined; }
    var _n = ntl_p_node("let");
    ds_map_add(_n, "name", ds_map_find_value(_t, "v"));
    ds_map_add(_n, "v", undefined);
    if (ntl_p_is_op(_s, "="))
    {
        ntl_p_next(_s);
        ds_map_replace(_n, "v", ntl_p_expr(_s));
    }
    ntl_p_expect_op(_s, ";");
    return _n;
