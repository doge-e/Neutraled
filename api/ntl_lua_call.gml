/// ntl_lua_call(fn, args) —— 调用函数（Lua 函数 / 宿主函数字符串）
var _fn = argument[0];
var _args = argument[1];
if (_args == undefined) _args = [];
if (variable_global_exists("ntl_lua_dbg") && global.ntl_lua_dbg == 1)
{
    ntl_log("lua", "[DIAG] call fn=" + string(_fn) + " isReal=" + string(is_real(_fn)) +
            " isFn=" + string(ntl_lua_is_fn(_fn)) +
            " hasKey=" + string((is_real(_fn)) ? ds_map_exists(_fn, "\x01fn") : -1) +
            " keys=" + string((is_real(_fn)) ? string(ds_map_size(_fn)) : -1));
}

// ---- Lua 函数 ----
if (ntl_lua_is_fn(_fn))
{
    var _env = ntl_lua_env_new(ds_map_find_value(_fn, "env"));
    var _params = ds_map_find_value(_fn, "params");
    var _pc = array_length(_params);
    var _ac = array_length(_args);
    for (var _i = 0; _i < _pc; _i += 1)
        ntl_lua_env_declare(_env, _params[_i], (_i < _ac) ? _args[_i] : undefined);

    // 可变参数
    if (ds_map_find_value(_fn, "vararg") == 1)
    {
        var _rest = [];
        for (var _j = _pc; _j < _ac; _j += 1) array_push(_rest, _args[_j]);
        ntl_lua_env_declare(_env, "...", _rest);
    }

    var _bodyNode = ds_map_find_value(_fn, "body");
    if (variable_global_exists("ntl_lua_dbg") && global.ntl_lua_dbg == 1)
        ntl_log("lua", "[DIAG-call] fn=" + string(ds_map_find_value(_fn, "name")) +
                " bodyType=" + ((is_array(_bodyNode)) ? "array(" + string(array_length(_bodyNode)) + ")" : (is_real(_bodyNode) ? "dsmap" : "other")) +
                " params=" + string(array_length(_params)));
    var _r = ntl_lua_ev_block(_env, _bodyNode);
    if (variable_global_exists("ntl_lua_dbg") && global.ntl_lua_dbg == 1)
    {
        var _isC = ntl_lua_is_ctrl(_r);
        var _d1 = "nil";
        if (_isC) _d1 = "ctrl:" + string(ds_map_find_value(_r, "_ntlctrl")) + " vals=" + string(array_length(ds_map_find_value(_r, "vals")));
        else if (is_array(_r)) _d1 = "array(" + string(array_length(_r)) + ")";
        else if (_r == undefined) _d1 = "undefined";
        else _d1 = string(_r);
        ntl_log("lua", "[DIAG-r] result=" + _d1);
    }

    // 解包 return 信号（函数体里的 return a+b 会变成控制信号）
    if (ntl_lua_is_ctrl(_r))
    {
        var _kind = ds_map_find_value(_r, "_ntlctrl");
        if (_kind == "return")
        {
            var _vals = ds_map_find_value(_r, "vals");
            var _vc = array_length(_vals);
            if (_vc == 0) return undefined;
            if (_vc == 1) return _vals[0];
            return _vals;
        }
    }
    return _r;
}

// ---- 宿主函数（字符串形式）----
if (is_string(_fn) && string_length(_fn) > 0)
{
    var _f = _fn;
    if (string_copy(_f, 1, 7) == "__host:") _f = string_delete(_f, 1, 7);
    // ★ 崩溃隔离：宿主函数内部的 GML 错误（如 ds_map 传错句柄）不能崩溃游戏
    try
    {
        if (string_copy(_f, 1, 6) == "__lua_") return ntl_lua_host(_f, _args);
        if (ntl_lua_is_stdlib(_f)) return ntl_lua_host("__lua_" + _f, _args);
        return ntl_call_host(_f, _args);
    }
    catch (_he)
    {
        ntl_log("lua", "[宿主函数异常已隔离] " + _f + ": " + string(_he));
        return undefined;
    }
}

// ---- 表带 __call ----
if (is_real(_fn))
{
    var _cm = ntl_lua_metamethod(_fn, "__call");
    if (_cm != undefined)
    {
        var _newArgs = [_fn];
        for (var _ci = 0; _ci < array_length(_args); _ci += 1) array_push(_newArgs, _args[_ci]);
        return ntl_lua_call(_cm, _newArgs);
    }
}

if (_fn == undefined) { ntl_lua_rt_err("attempt to call a nil value"); return undefined; }
ntl_lua_rt_err("attempt to call a non-function value");
return undefined;
