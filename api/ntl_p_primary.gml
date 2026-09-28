/// ntl_p_primary —— 老式脚本（每个函数一个同名脚本资源）
var _s = argument[0];
    var _t = ntl_p_next(_s);
    var _type = ds_map_find_value(_t, "t");
    var _val = ds_map_find_value(_t, "v");

    if (_type == "num") { var _n = ntl_p_node("num"); ds_map_add(_n, "v", _val); return _n; }
    if (_type == "str") { var _s2 = ntl_p_node("str"); ds_map_add(_s2, "v", _val); return _s2; }

    if (_type == "op" && _val == "(")
    {
        var _e = ntl_p_expr(_s);
        ntl_p_expect_op(_s, ")");
        return _e;
    }

    if (_type == "id")
    {
        // 函数调用
        if (ntl_p_is_op(_s, "("))
        {
            ntl_p_next(_s);
            var _c = ntl_p_node("call");
            ds_map_add(_c, "name", _val);
            var _args = ds_list_create();
            if (!ntl_p_is_op(_s, ")"))
            {
                ds_list_add(_args, ntl_p_expr(_s));
                while (ntl_p_is_op(_s, ","))
                {
                    ntl_p_next(_s);
                    ds_list_add(_args, ntl_p_expr(_s));
                }
            }
            ntl_p_expect_op(_s, ")");
            ds_map_add(_c, "args", _args);
            return _c;
        }
        // 变量（支持 a.b 形式的点号名 → 拼成字符串键）
        var _name = _val;
        while (ntl_p_is_op(_s, "."))
        {
            ntl_p_next(_s);
            var _t2 = ntl_p_next(_s);
            _name += "." + string(ds_map_find_value(_t2, "v"));
        }

        // 命名空间函数调用：ns.func(...)
        if (ntl_p_is_op(_s, "("))
        {
            ntl_p_next(_s);
            var _c2 = ntl_p_node("call");
            ds_map_add(_c2, "name", _name);
            var _args2 = ds_list_create();
            if (!ntl_p_is_op(_s, ")"))
            {
                ds_list_add(_args2, ntl_p_expr(_s));
                while (ntl_p_is_op(_s, ","))
                {
                    ntl_p_next(_s);
                    ds_list_add(_args2, ntl_p_expr(_s));
                }
            }
            ntl_p_expect_op(_s, ")");
            ds_map_add(_c2, "args", _args2);
            return _c2;
        }
        var _v = ntl_p_node("var");
        ds_map_add(_v, "name", _name);
        return _v;
    }

    ntl_p_err(_s, "unexpected token");
    return undefined;
