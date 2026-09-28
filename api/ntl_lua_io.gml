/// ntl_lua_io(op, ...) —— io 库最小兼容
/// Kristal 偶尔用 io.write 做调试输出，这里映射到日志
var _op = string_lower(string(argument[0]));

if (_op == "write")
{
    // 把所有参数拼起来写日志
    var _s = "";
    for (var _i = 1; _i < argument_count; _i += 1)
    {
        var _v = argument[_i];
        if (_v == undefined) continue;
        _s += ntl_lua_tostring(_v);
    }
    if (string_length(_s) > 0) ntl_log("io", _s);
    return 1;
}

if (_op == "read") return undefined;   // 不支持读
if (_op == "open") return undefined;
if (_op == "close") return 1;
if (_op == "lines") return undefined;

return undefined;
