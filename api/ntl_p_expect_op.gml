/// ntl_p_expect_op —— 老式脚本（每个函数一个同名脚本资源）
var _s = argument[0];
var _op = argument[1];
    if (ntl_p_is_op(_s, _op)) { ntl_p_next(_s); return true; }
    ntl_p_err(_s, "expected '" + _op + "'");
    return false;
