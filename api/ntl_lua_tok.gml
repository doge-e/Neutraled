/// ntl_lua_tok(src) → ds_list（Lua token 序列）
/// token = ds_map { t: kw|name|num|str|op|eof, v: 值, line: 行号 }
var _src = string(argument[0]);
var _len = string_length(_src);
var _toks = ds_list_create();
var _i = 1;
var _line = 1;

while (_i <= _len)
{
    var _c = string_char_at(_src, _i);
    var _c2 = (_i + 1 <= _len) ? string_char_at(_src, _i + 1) : "";
    var _c3 = (_i + 2 <= _len) ? string_char_at(_src, _i + 2) : "";

    // ---- 空白 ----
    if (_c == " " || _c == chr(9) || _c == chr(13)) { _i += 1; continue; }
    if (_c == chr(10)) { _line += 1; _i += 1; continue; }

    // ---- 注释 ----
    if (_c == "-" && _c2 == "-")
    {
        // 长注释 --[[ ... ]] 或 --[=[ ... ]=]
        if (_c3 == "[")
        {
            var _j = _i + 3;
            var _eq = 0;
            while (_j <= _len && string_char_at(_src, _j) == "=") { _eq += 1; _j += 1; }
            if (_j <= _len && string_char_at(_src, _j) == "[")
            {
                _i = _j + 1;
                var _close = "]" + string_repeat("=", _eq) + "]";
                var _clen = string_length(_close);
                while (_i <= _len)
                {
                    if (string_char_at(_src, _i) == chr(10)) _line += 1;
                    if (string_copy(_src, _i, _clen) == _close) { _i += _clen; break; }
                    _i += 1;
                }
                continue;
            }
        }
        // 行注释
        while (_i <= _len && string_char_at(_src, _i) != chr(10)) _i += 1;
        continue;
    }

    // ---- 字符串（含转义）----
    if (_c == "\"" || _c == "'")
    {
        var _q = _c;
        _i += 1;
        var _s = "";
        while (_i <= _len)
        {
            var _ch = string_char_at(_src, _i);
            if (_ch == chr(10)) { _line += 1; }
            if (_ch == "\\")
            {
                var _nx = string_char_at(_src, _i + 1);
                if (_nx == "n") _s += chr(10);
                else if (_nx == "t") _s += chr(9);
                else if (_nx == "r") _s += chr(13);
                else if (_nx == "0") _s += chr(0);
                else _s += _nx;
                _i += 2;
                continue;
            }
            if (_ch == _q) { _i += 1; break; }
            _s += _ch;
            _i += 1;
        }
        ntl_lua_tok_push(_toks, "str", _s, _line);
        continue;
    }

    // ---- 长字符串 [[ ... ]] ----
    if (_c == "[")
    {
        var _k = _i + 1;
        var _eq2 = 0;
        while (_k <= _len && string_char_at(_src, _k) == "=") { _eq2 += 1; _k += 1; }
        if (_k <= _len && string_char_at(_src, _k) == "[")
        {
            _i = _k + 1;
            if (string_char_at(_src, _i) == chr(10)) { _i += 1; _line += 1; }
            var _close2 = "]" + string_repeat("=", _eq2) + "]";
            var _clen2 = string_length(_close2);
            var _ls = "";
            while (_i <= _len)
            {
                if (string_char_at(_src, _i) == chr(10)) _line += 1;
                if (string_copy(_src, _i, _clen2) == _close2) { _i += _clen2; break; }
                _ls += string_char_at(_src, _i);
                _i += 1;
            }
            ntl_lua_tok_push(_toks, "str", _ls, _line);
            continue;
        }
    }

    // ---- 数字（十进制 / 十六进制 / 科学计数 / 小数点）----
    if (ntl_tok_is_digit(_c) || (_c == "." && ntl_tok_is_digit(_c2)))
    {
        var _num = "";
        if (_c == "0" && (_c2 == "x" || _c2 == "X"))
        {
            _num = "0x"; _i += 2;
            while (_i <= _len && (ntl_tok_is_digit(string_char_at(_src, _i)) ||
                   (string_lower(string_char_at(_src, _i)) >= "a" && string_lower(string_char_at(_src, _i)) <= "f")))
            { _num += string_char_at(_src, _i); _i += 1; }
            ntl_lua_tok_push(_toks, "num", string(real(_num)), _line);
            continue;
        }
        while (_i <= _len)
        {
            var _nc = string_char_at(_src, _i);
            if (ntl_tok_is_digit(_nc) || _nc == ".") { _num += _nc; _i += 1; continue; }
            if ((_nc == "e" || _nc == "E") && _num != "")
            {
                _num += _nc; _i += 1;
                if (_i <= _len && (string_char_at(_src, _i) == "+" || string_char_at(_src, _i) == "-"))
                { _num += string_char_at(_src, _i); _i += 1; }
                continue;
            }
            break;
        }
        ntl_lua_tok_push(_toks, "num", string(real(_num)), _line);
        continue;
    }

    // ---- 标识符 / 关键字 ----
    if (ntl_tok_is_alpha(_c))
    {
        var _id = "";
        while (_i <= _len && ntl_tok_is_alnum(string_char_at(_src, _i)))
        { _id += string_char_at(_src, _i); _i += 1; }
        if (ntl_lua_is_kw(_id)) ntl_lua_tok_push(_toks, "kw", _id, _line);
        else ntl_lua_tok_push(_toks, "name", _id, _line);
        continue;
    }

    // ---- 运算符（最长匹配优先）----
    var _three = string_copy(_src, _i, 3);
    var _two = string_copy(_src, _i, 2);
    if (_three == "...") { ntl_lua_tok_push(_toks, "op", "...", _line); _i += 3; continue; }
    if (_two == "==" || _two == "~=" || _two == "<=" || _two == ">=" || _two == ".." || _two == "::" || _two == "//")
    { ntl_lua_tok_push(_toks, "op", _two, _line); _i += 2; continue; }
    if (string_pos(_c, "+-*/%^#<>=(){}[];:,.&|~") > 0)
    { ntl_lua_tok_push(_toks, "op", _c, _line); _i += 1; continue; }

    // 未知字符 → 跳过（容错）
    _i += 1;
}

ntl_lua_tok_push(_toks, "eof", "", _line);
return _toks;
