/// ntl_ev —— 老式脚本（每个函数一个同名脚本资源）
var _node = argument[0];
var _env = argument[1];
    if (_node == undefined) return undefined;
    var _t = ds_map_find_value(_node, "t");

    if (_t == "num") return ds_map_find_value(_node, "v");
    if (_t == "str") return ds_map_find_value(_node, "v");

    if (_t == "var")
    {
        var _name = ds_map_find_value(_node, "name");
        // 特殊：true/false/null
        if (_name == "true") return 1;
        if (_name == "false") return 0;
        if (_name == "undefined" || _name == "null") return undefined;
        // 命名空间常量（ns.CONST）
        if (string_pos(".", _name) > 0 && variable_global_exists("ntl_ns_const"))
        {
            if (ds_map_exists(global.ntl_ns_const, _name))
                return ds_map_find_value(global.ntl_ns_const, _name);
        }
        return ntl_e_env_get(_env, _name);
    }

    if (_t == "un")
    {
        var _op = ds_map_find_value(_node, "op");
        var _v = ntl_ev(ds_map_find_value(_node, "e"), _env);
        if (_op == "-")
        {
            if (_v == undefined) return undefined;
            return -(real(_v));
        }
        if (_op == "!") return (_v == undefined || real(_v) == 0) ? 1 : 0;
    }

    if (_t == "bin")
    {
        var _op = ds_map_find_value(_node, "op");
        // 短路求值
        if (_op == "&&")
        {
            var _l0 = real(ntl_ev(ds_map_find_value(_node, "l"), _env));
            if (_l0 == 0) return 0;
            return (real(ntl_ev(ds_map_find_value(_node, "r"), _env)) != 0) ? 1 : 0;
        }
        if (_op == "||")
        {
            var _l1 = real(ntl_ev(ds_map_find_value(_node, "l"), _env));
            if (_l1 != 0) return 1;
            return (real(ntl_ev(ds_map_find_value(_node, "r"), _env)) != 0) ? 1 : 0;
        }

        var _a = ntl_ev(ds_map_find_value(_node, "l"), _env);
        var _b = ntl_ev(ds_map_find_value(_node, "r"), _env);

        // 字符串拼接
        if (_op == "+" && (is_string(_a) || is_string(_b)))
            return string(_a) + string(_b);

        var _x = (_a == undefined) ? 0 : real(_a);
        var _y = (_b == undefined) ? 0 : real(_b);
        if (_op == "+") return _x + _y;
        if (_op == "-") return _x - _y;
        if (_op == "*") return _x * _y;
        if (_op == "/") { if (_y == 0) return 0; return _x / _y; }
        if (_op == "%") { if (_y == 0) return 0; return _x mod _y; }
        if (_op == "==") return (_x == _y) ? 1 : 0;
        if (_op == "!=") return (_x != _y) ? 1 : 0;
        if (_op == "<") return (_x < _y) ? 1 : 0;
        if (_op == ">") return (_x > _y) ? 1 : 0;
        if (_op == "<=") return (_x <= _y) ? 1 : 0;
        if (_op == ">=") return (_x >= _y) ? 1 : 0;
        return 0;
    }

    if (_t == "call")
    {
        var _name = ds_map_find_value(_node, "name");
        var _argNodes = ds_map_find_value(_node, "args");
        var _args = [];
        var _cnt = ds_list_size(_argNodes);
        for (var _i = 0; _i < _cnt; _i += 1)
            _args[_i] = ntl_ev(ds_list_find_value(_argNodes, _i), _env);
        return ntl_call_host(_name, _args);
    }

    return undefined;
