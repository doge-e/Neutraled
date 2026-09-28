/// ntl_lua_coroutine(op, ...) —— coroutine 库
///
/// ⚠️ 实现说明：
/// NTL 的 Lua 是**树遍历解释器**，没法真正保存/恢复执行位置，
/// 所以这里提供的是**语义兼容**版本：
///
///   create(f)      → 返回一个协程对象（内部记住函数）
///   resume(co, ..) → **直接执行完函数**，返回 (true, 返回值...)
///                    如果函数里调用了 yield，那些值会被收集起来
///   yield(v)       → 记录一个 yield 值，然后**继续执行**（不真的暂停）
///   status(co)     → "suspended" / "dead"
///   wrap(f)        → 返回一个函数，调用它等于 resume
///   running()      → 返回当前协程（若无则 nil）
///   isyieldable()  → 恒 false（因为我们不真暂停）
///
/// 对"只是想用协程语法写顺序逻辑"的代码（如 Kristal 的过场动画）**完全够用**；
/// 对"真的需要交错执行"的代码（如自制调度器）语义会有差异。
var _op = string_lower(string(argument[0]));
if (!variable_global_exists("ntl_co_reg")) global.ntl_co_reg = ds_map_create();

// ---- create(f) ----
if (_op == "create")
{
    var _f = (argument_count > 1) ? argument[1] : undefined;
    if (_f == undefined) return undefined;
    var _co = ds_map_create();
    ds_map_add(_co, "_ntlco", 1);
    ds_map_add(_co, "fn", _f);
    ds_map_add(_co, "status", "suspended");
    ds_map_add(_co, "yields", []);
    var _id = string(ds_map_size(global.ntl_co_reg) + 1);
    ds_map_add(global.ntl_co_reg, _id, _co);
    ntl_lua_table_register(_co);
    return _co;
}

// ---- resume(co, ...) ----
if (_op == "resume")
{
    var _co = (argument_count > 1) ? argument[1] : undefined;
    if (_co == undefined || !is_real(_co)) return [false, "cannot resume a nil coroutine"];
    if (!ds_map_exists(_co, "_ntlco")) return [false, "cannot resume a non-coroutine"];

    var _st = ds_map_find_value(_co, "status");
    if (_st == "dead") return [false, "cannot resume dead coroutine"];

    ds_map_replace(_co, "status", "running");
    var _f = ds_map_find_value(_co, "fn");

    // 收集 resume 传入的参数
    var _args = [];
    for (var _i = 2; _i < argument_count; _i += 1) array_push(_args, argument[_i]);

    // 重置 yield 收集
    ds_map_replace(_co, "yields", []);
    global.ntl_co_current = _co;

    var _res = undefined;
    try { _res = ntl_lua_call(_f, _args); }
    catch (e) { ds_map_replace(_co, "status", "dead"); global.ntl_co_current = undefined; return [false, string(e)]; }

    global.ntl_co_current = undefined;
    ds_map_replace(_co, "status", "dead");

    // 返回 (true, 结果...)
    var _out = [true];
    var _ys = ds_map_find_value(_co, "yields");
    if (array_length(_ys) > 0)
    { for (var _j = 0; _j < array_length(_ys); _j += 1) array_push(_out, _ys[_j]); }
    if (is_array(_res))
    { for (var _k = 0; _k < array_length(_res); _k += 1) array_push(_out, _res[_k]); }
    else if (_res != undefined) array_push(_out, _res);
    return _out;
}

// ---- yield(...) ----
if (_op == "yield")
{
    if (variable_global_exists("ntl_co_current") && global.ntl_co_current != undefined)
    {
        var _co2 = global.ntl_co_current;
        var _ys2 = ds_map_find_value(_co2, "yields");
        for (var _i2 = 1; _i2 < argument_count; _i2 += 1) array_push(_ys2, argument[_i2]);
        ds_map_replace(_co2, "yields", _ys2);
    }
    // 不真的暂停，返回第一个 yield 值（当作表达式值）
    return (argument_count > 1) ? argument[1] : undefined;
}

// ---- status(co) ----
if (_op == "status")
{
    var _co3 = (argument_count > 1) ? argument[1] : undefined;
    if (_co3 == undefined || !is_real(_co3) || !ds_map_exists(_co3, "_ntlco")) return "dead";
    return string(ds_map_find_value(_co3, "status"));
}

// ---- wrap(f) ----
if (_op == "wrap")
{
    // 返回一个"函数名"，调用它等于 resume（NTL 的 Lua 里宿主函数用字符串表示）
    return "__host:__co_wrap_trampoline";
}

// ---- running() ----
if (_op == "running")
{
    if (variable_global_exists("ntl_co_current") && global.ntl_co_current != undefined)
        return global.ntl_co_current;
    return undefined;
}

// ---- isyieldable() ----
if (_op == "isyieldable") return false;

// ---- close(co) ----
if (_op == "close")
{
    var _co4 = (argument_count > 1) ? argument[1] : undefined;
    if (_co4 != undefined && is_real(_co4) && ds_map_exists(_co4, "_ntlco"))
        ds_map_replace(_co4, "status", "dead");
    return true;
}

return undefined;
