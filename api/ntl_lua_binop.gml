/// ntl_lua_binop(op, a, b) —— 二元运算
var _op = string(argument[0]);
var _a = argument[1];
var _b = argument[2];

// ★★★ 性能快速路径：两边都是数字 → 直接算，完全跳过元方法查询
//     元方法查询会创建/销毁 ds_map，对纯数字运算是纯开销。
//     实测 2 万次累加从 3.03 秒降到（见基准）。
if (is_real(_a) && is_real(_b))
{
    var _c0 = string_copy(_op, 1, 1);
    if (_c0 == "+") { if (string_length(_op) == 1) return _a + _b; }
    else if (_c0 == "-") { if (string_length(_op) == 1) return _a - _b; }
    else if (_c0 == "*") { if (string_length(_op) == 1) return _a * _b; }
    else if (_c0 == "/") { if (string_length(_op) == 1) return _a / _b; }
    else if (_c0 == "<")
    {
        if (_op == "<")  return (_a < _b) ? 1 : 0;
        if (_op == "<=") return (_a <= _b) ? 1 : 0;
    }
    else if (_c0 == ">")
    {
        if (_op == ">")  return (_a > _b) ? 1 : 0;
        if (_op == ">=") return (_a >= _b) ? 1 : 0;
    }
    else if (_op == "==") return (_a == _b) ? 1 : 0;
    else if (_op == "~=") return (_a != _b) ? 1 : 0;
    else if (_op == "%")  return (_b == 0) ? 0 : (_a mod _b);
    else if (_op == "//") return (_b == 0) ? 0 : floor(_a / _b);
    else if (_op == "^")  return power(_a, _b);
}

// 元方法优先（仅当参与方是表时）
if (is_real(_a) || is_real(_b))
{
    var _mm = ntl_lua_meta_binop(_op, _a, _b);
    if (_mm != undefined && is_real(_mm) && ds_exists(_mm, ds_type_map))
    {
        if (ds_map_find_value(_mm, "ok") == 1)
        {
            var _mv = ds_map_find_value(_mm, "value");
            ds_map_destroy(_mm);
            return _mv;
        }
        ds_map_destroy(_mm);
    }
}

// 算术运算的 nil 安全（Lua 里 nil 参与算术会报错，这里返回 undefined 并记录）
if (_op == "+" || _op == "-" || _op == "*" || _op == "/" || _op == "//" || _op == "%" || _op == "^")
{
    if (_a == undefined || _b == undefined)
    {
        ntl_lua_rt_err("attempt to perform arithmetic on a nil value");
        return undefined;
    }
}

switch (_op)
{
    case "+":  return real(_a) + real(_b);
    case "-":  return real(_a) - real(_b);
    case "*":  return real(_a) * real(_b);
    case "/":  return real(_a) / real(_b);
    case "//": { var _d = real(_b); return (_d == 0) ? 0 : floor(real(_a) / _d); }
    case "%":  { var _m = real(_b); return (_m == 0) ? 0 : (real(_a) mod _m); }
    case "^":  return power(real(_a), real(_b));
    case "..": return ntl_lua_tostring(_a) + ntl_lua_tostring(_b);
    case "==":
        if (_a == undefined && _b == undefined) return 1;
        if (_a == undefined || _b == undefined) return 0;
        if (is_string(_a) && is_string(_b)) return (_a == _b) ? 1 : 0;
        if (is_real(_a) && is_real(_b)) return (_a == _b) ? 1 : 0;
        if (is_bool(_a) && is_bool(_b)) return (_a == _b) ? 1 : 0;
        return (_a == _b) ? 1 : 0;
    case "~=": return (ntl_lua_binop("==", _a, _b) == 1) ? 0 : 1;
    case "<":  return (real(_a) < real(_b)) ? 1 : 0;
    case ">":  return (real(_a) > real(_b)) ? 1 : 0;
    case "<=": return (real(_a) <= real(_b)) ? 1 : 0;
    case ">=": return (real(_a) >= real(_b)) ? 1 : 0;
    case "and": return ntl_lua_truthy(_a) ? _b : _a;
    case "or":  return ntl_lua_truthy(_a) ? _a : _b;
}
return undefined;
