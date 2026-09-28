/// ntl_ex —— 老式脚本（每个函数一个同名脚本资源）
var _node = argument[0];
var _env = argument[1];
    if (_node == undefined) return 0;
    global.ntl_live_budget -= 1;
    if (global.ntl_live_budget <= 0)
    {
        ntl_log("live", "[错误] 脚本执行超出预算（疑似死循环），已中止");
        return -1;   // 中止信号
    }

    var _t = ds_map_find_value(_node, "t");

    if (_t == "block" || _t == "program")
    {
        var _stmts = ds_map_find_value(_node, "stmts");
        var _n = ds_list_size(_stmts);
        for (var _i = 0; _i < _n; _i += 1)
        {
            var _r = ntl_ex(ds_list_find_value(_stmts, _i), _env);
            if (_r != 0) return _r;
        }
        return 0;
    }

    if (_t == "let" || _t == "assign")
    {
        var _name = ds_map_find_value(_node, "name");
        var _v = ntl_ev(ds_map_find_value(_node, "v"), _env);
        ntl_e_env_set(_env, _name, _v);
        return 0;
    }

    if (_t == "exprStmt")
    {
        ntl_ev(ds_map_find_value(_node, "e"), _env);
        return 0;
    }

    if (_t == "if")
    {
        var _c = real(ntl_ev(ds_map_find_value(_node, "cond"), _env));
        if (_c != 0) return ntl_ex(ds_map_find_value(_node, "then"), _env);
        var _els = ds_map_find_value(_node, "els");
        if (_els != undefined) return ntl_ex(_els, _env);
        return 0;
    }

    if (_t == "while")
    {
        var _guard = 0;
        while (real(ntl_ev(ds_map_find_value(_node, "cond"), _env)) != 0)
        {
            var _r2 = ntl_ex(ds_map_find_value(_node, "body"), _env);
            if (_r2 != 0) return _r2;
            _guard += 1;
            if (_guard > 100000)
            {
                ntl_log("live", "[错误] while 循环超过 100000 次，已中止");
                return -1;
            }
        }
        return 0;
    }

    if (_t == "return")
    {
        var _rv = ntl_ev(ds_map_find_value(_node, "v"), _env);
        ntl_e_env_set(_env, "__ret", _rv);
        return 2;   // 返回信号
    }

    return 0;
