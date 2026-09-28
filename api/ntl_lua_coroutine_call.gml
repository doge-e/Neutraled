/// ntl_lua_coroutine_call(args) —— 数组形式的 coroutine 调用包装
/// args[0] = 操作名（create/resume/...），args[1..] = 实际参数
var _args = argument[0];
var _n = array_length(_args);
var _op = (_n > 0) ? string(_args[0]) : "";
var _a1 = (_n > 1) ? _args[1] : undefined;
var _a2 = (_n > 2) ? _args[2] : undefined;
var _a3 = (_n > 3) ? _args[3] : undefined;
return ntl_lua_coroutine(_op, _a1, _a2, _a3);