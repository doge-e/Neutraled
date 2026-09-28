/// ntl_lua_debug(op, ...) —— debug 库（最小可用集）
var _op = string_lower(string(argument[0]));

// traceback(msg) —— NTL 没有调用栈信息，返回消息本身
if (_op == "traceback") return (argument_count > 1) ? string(argument[1]) : "stack traceback:";

// getinfo(fn) —— 返回基本信息
if (_op == "getinfo")
{
    var _t = ntl_lua_table_new();
    ntl_lua_table_set(_t, "what", "Lua");
    ntl_lua_table_set(_t, "source", "=(ntl)");
    ntl_lua_table_set(_t, "short_src", "ntl");
    ntl_lua_table_set(_t, "currentline", -1);
    return _t;
}

// sethook / gethook —— 空实现（NTL 不暴露钩子）
if (_op == "sethook" || _op == "gethook") return undefined;

// getlocal / setlocal / getupvalue / setupvalue —— 空实现
if (_op == "getlocal" || _op == "getupvalue") return undefined;
if (_op == "setlocal" || _op == "setupvalue") return undefined;

// traceback 是唯一常被真正调用的
return undefined;
