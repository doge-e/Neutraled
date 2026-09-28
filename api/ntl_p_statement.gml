/// ntl_p_statement —— 老式脚本（每个函数一个同名脚本资源）
var _s = argument[0];
    var _t = ntl_p_peek(_s);
    var _type = ds_map_find_value(_t, "t");
    var _val = ds_map_find_value(_t, "v");

    if (_type == "op" && _val == "{") return ntl_p_block(_s);
    if (_type == "id")
    {
        if (_val == "let") return ntl_p_let(_s);
        if (_val == "if") return ntl_p_if(_s);
        if (_val == "while") return ntl_p_while(_s);
        if (_val == "return")
        {
            ntl_p_next(_s);
            var _r = ntl_p_node("return");
            if (!ntl_p_is_op(_s, ";")) ds_map_add(_r, "v", ntl_p_expr(_s));
            else ds_map_add(_r, "v", undefined);
            ntl_p_expect_op(_s, ";");
            return _r;
        }
    }

    // 赋值 或 表达式语句
    var _e = ntl_p_expr(_s);
    if (ntl_p_is_op(_s, "="))
    {
        ntl_p_next(_s);
        if (ds_map_find_value(_e, "t") != "var") { ntl_p_err(_s, "assignment target must be a variable"); return undefined; }
        var _a = ntl_p_node("assign");
        ds_map_add(_a, "name", ds_map_find_value(_e, "name"));
        ds_map_add(_a, "v", ntl_p_expr(_s));
        ntl_p_expect_op(_s, ";");
        return _a;
    }
    // 复合赋值 += -= *= /=
    if (ntl_p_is_op(_s, "+=") || ntl_p_is_op(_s, "-=") || ntl_p_is_op(_s, "*=") || ntl_p_is_op(_s, "/="))
    {
        var _op = ds_map_find_value(ntl_p_next(_s), "v");
        if (ds_map_find_value(_e, "t") != "var") { ntl_p_err(_s, "assignment target must be a variable"); return undefined; }
        var _a2 = ntl_p_node("assign");
        ds_map_add(_a2, "name", ds_map_find_value(_e, "name"));
        var _rhs = ntl_p_expr(_s);
        // 展开为 x = x OP rhs
        var _b = ntl_p_node("bin");
        ds_map_add(_b, "op", string_copy(_op, 1, 1));
        var _lv = ntl_p_node("var");
        ds_map_add(_lv, "name", ds_map_find_value(_e, "name"));
        ds_map_add(_b, "l", _lv);
        ds_map_add(_b, "r", _rhs);
        ds_map_add(_a2, "v", _b);
        ntl_p_expect_op(_s, ";");
        return _a2;
    }

    var _es = ntl_p_node("exprStmt");
    ds_map_add(_es, "e", _e);
    ntl_p_expect_op(_s, ";");
    return _es;
