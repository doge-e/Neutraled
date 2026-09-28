/// ntl_tok —— 老式脚本（每个函数一个同名脚本资源）
var _src = argument[0];
    var _toks = ds_list_create();
    var _len = string_length(_src);
    var _i = 1;
    var _line = 1;

    while (_i <= _len)
    {
        var _c = string_char_at(_src, _i);

        // 换行
        if (_c == chr(10)) { _line += 1; _i += 1; continue; }
        // 空白
        if (_c == " " || _c == chr(9) || _c == chr(13)) { _i += 1; continue; }

        // 注释 //...
        if (_c == "/" && string_char_at(_src, _i + 1) == "/")
        {
            while (_i <= _len && string_char_at(_src, _i) != chr(10)) _i += 1;
            continue;
        }
        // 注释 /* ... */
        if (_c == "/" && string_char_at(_src, _i + 1) == "*")
        {
            _i += 2;
            while (_i <= _len)
            {
                if (string_char_at(_src, _i) == chr(10)) _line += 1;
                if (string_char_at(_src, _i) == "*" && string_char_at(_src, _i + 1) == "/") { _i += 2; break; }
                _i += 1;
            }
            continue;
        }

        // 数字（整数/小数）
        if (ntl_tok_is_digit(_c))
        {
            var _s = "";
            while (_i <= _len && (ntl_tok_is_digit(string_char_at(_src, _i)) || string_char_at(_src, _i) == "."))
            {
                _s += string_char_at(_src, _i);
                _i += 1;
            }
            var _m = ds_map_create();
            ds_map_add(_m, "t", "num");
            ds_map_add(_m, "v", real(_s));
            ds_map_add(_m, "line", _line);
            ds_list_add(_toks, _m);
            continue;
        }

        // 字符串 "..."
        if (_c == "\"")
        {
            var _str = "";
            _i += 1;
            while (_i <= _len && string_char_at(_src, _i) != "\"")
            {
                var _ch = string_char_at(_src, _i);
                if (_ch == "\\" && _i + 1 <= _len)
                {
                    var _nx = string_char_at(_src, _i + 1);
                    if (_nx == "n") { _str += chr(10); _i += 2; continue; }
                    if (_nx == "t") { _str += chr(9); _i += 2; continue; }
                    if (_nx == "\"") { _str += "\""; _i += 2; continue; }
                    if (_nx == "\\") { _str += "\\"; _i += 2; continue; }
                }
                _str += _ch;
                _i += 1;
            }
            _i += 1;
            var _ms = ds_map_create();
            ds_map_add(_ms, "t", "str");
            ds_map_add(_ms, "v", _str);
            ds_map_add(_ms, "line", _line);
            ds_list_add(_toks, _ms);
            continue;
        }

        // 标识符 / 关键字
        if (ntl_tok_is_alpha(_c))
        {
            var _id = "";
            while (_i <= _len && ntl_tok_is_alnum(string_char_at(_src, _i)))
            {
                _id += string_char_at(_src, _i);
                _i += 1;
            }
            var _mi = ds_map_create();
            ds_map_add(_mi, "t", "id");
            ds_map_add(_mi, "v", _id);
            ds_map_add(_mi, "line", _line);
            ds_list_add(_toks, _mi);
            continue;
        }

        // 运算符（双字符优先）
        var _two = string_char_at(_src, _i) + string_char_at(_src, _i + 1);
        var _isTwo = (_two == "==" || _two == "!=" || _two == "<=" || _two == ">=" || _two == "&&" || _two == "||");
        var _mop = ds_map_create();
        ds_map_add(_mop, "t", "op");
        if (_isTwo) { ds_map_add(_mop, "v", _two); _i += 2; }
        else { ds_map_add(_mop, "v", _c); _i += 1; }
        ds_map_add(_mop, "line", _line);
        ds_list_add(_toks, _mop);
    }

    var _me = ds_map_create();
    ds_map_add(_me, "t", "eof");
    ds_map_add(_me, "v", "");
    ds_map_add(_me, "line", _line);
    ds_list_add(_toks, _me);
    return _toks;
