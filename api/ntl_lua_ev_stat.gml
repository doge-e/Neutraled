/// ntl_lua_ev_stat(env, node) —— 单条语句求值
var _env = argument[0];
var _n = argument[1];
if (_n == undefined) return undefined;
var _k = ds_map_find_value(_n, "k");

// ---- return ----
if (_k == "return")
{
    var _nodes = ds_map_find_value(_n, "exprs");
    var _vals = [];
    for (var _i = 0; _i < array_length(_nodes); _i += 1)
    {
        var _v = ntl_lua_ex(_env, _nodes[_i]);
        if (variable_global_exists("ntl_lua_dbg") && global.ntl_lua_dbg == 1)
            ntl_log("lua", "[DIAG-return] i=" + string(_i) + " nodeK=" + string(ds_map_find_value(_nodes[_i], "k")) + " valType=" + ((_v == undefined) ? "undefined" : (is_real(_v) ? "real:" + string(_v) : (is_string(_v) ? "str" : "other"))));
        if (_i == array_length(_nodes) - 1 && is_array(_v))
        { for (var _j = 0; _j < array_length(_v); _j += 1) array_push(_vals, _v[_j]); }
        else array_push(_vals, _v);
    }
    return ntl_lua_ctrl("return", _vals);
}

// ---- break ----
if (_k == "break") return ntl_lua_ctrl("break", []);

// ---- local ----
if (_k == "local")
{
    var _names = ds_map_find_value(_n, "names");
    var _vnodes = ds_map_find_value(_n, "values");
    var _vals2 = [];
    for (var _a = 0; _a < array_length(_vnodes); _a += 1)
    {
        var _vv = ntl_lua_ex(_env, _vnodes[_a]);
        if (_a == array_length(_vnodes) - 1 && is_array(_vv))
        { for (var _b = 0; _b < array_length(_vv); _b += 1) array_push(_vals2, _vv[_b]); }
        else array_push(_vals2, _vv);
    }
    for (var _c = 0; _c < array_length(_names); _c += 1)
        ntl_lua_env_declare(_env, _names[_c], (_c < array_length(_vals2)) ? _vals2[_c] : undefined);
    return undefined;
}

// ---- local function ----
if (_k == "localfunc")
{
    var _name = ds_map_find_value(_n, "name");
    ntl_lua_env_declare(_env, _name, undefined);
    var _fn = ntl_lua_fn_new(_n, _env);
    ds_map_replace(_fn, "name", _name);
    ntl_lua_env_declare(_env, _name, _fn);
    return undefined;
}

// ---- function a.b.c():d() ----
if (_k == "funcdef")
{
    var _path = ds_map_find_value(_n, "path");
    var _self = ds_map_find_value(_n, "self");
    var _fn2 = ntl_lua_fn_new(_n, _env);
    ds_map_replace(_fn2, "name", _path[array_length(_path) - 1]);

    if (array_length(_path) == 1 && _self == "")
    {
        ntl_lua_env_set(_env, _path[0], _fn2);
        return undefined;
    }
    // 逐级解析对象：中间层（0 .. n-2）逐级创建/穿透，最后一段才是函数名
    var _plen = array_length(_path);
    var _obj = (_path[0] == "global") ? ntl_lua_env_get(_env, "_G") : ntl_lua_env_get(_env, _path[0]);
    if (_obj == undefined && _path[0] == "global") _obj = global.ntl_lua_globals;
    // 回退：查全局表（模块环境里定义全局函数时常见）
    if (_obj == undefined && variable_global_exists("ntl_lua_globals"))
        _obj = ntl_lua_table_get(global.ntl_lua_globals, _path[0]);
    // 仍不存在 → 在全局表里自动创建（Lua 里给未定义全局的字段赋值是合法的）
    if (_obj == undefined)
    {
        _obj = ntl_lua_table_new();
        if (variable_global_exists("ntl_lua_globals"))
            ntl_lua_table_set(global.ntl_lua_globals, _path[0], _obj);
        else
            ntl_lua_env_declare(_env, _path[0], _obj);
    }

    for (var _d = 1; _d < _plen - 1; _d += 1)
    {
        var _nx = ntl_lua_index_get(_obj, _path[_d]);
        if (_nx == undefined || !is_real(_nx))
        {
            _nx = ntl_lua_table_new();
            ntl_lua_table_set(_obj, _path[_d], _nx);
        }
        _obj = _nx;
    }
    if (_self != "") ntl_lua_table_set(_obj, _self, _fn2);
    else ntl_lua_table_set(_obj, _path[_plen - 1], _fn2);
    return undefined;
}

// ---- assign ----
if (_k == "assign")
{
    var _targets = ds_map_find_value(_n, "targets");
    var _vns = ds_map_find_value(_n, "values");
    var _vals3 = [];
    for (var _e = 0; _e < array_length(_vns); _e += 1)
    {
        var _v3 = ntl_lua_ex(_env, _vns[_e]);
        if (_e == array_length(_vns) - 1 && is_array(_v3))
        { for (var _f = 0; _f < array_length(_v3); _f += 1) array_push(_vals3, _v3[_f]); }
        else array_push(_vals3, _v3);
    }
    for (var _g = 0; _g < array_length(_targets); _g += 1)
        ntl_lua_assign_target(_env, _targets[_g], (_g < array_length(_vals3)) ? _vals3[_g] : undefined);
    return undefined;
}

// ---- do ----
if (_k == "do") return ntl_lua_ev_block(ntl_lua_env_pool_get(_env, "do"), ds_map_find_value(_n, "body"));

// ---- if ----
if (_k == "if")
{
    var _conds = ds_map_find_value(_n, "conds");
    var _blocks = ds_map_find_value(_n, "blocks");
    for (var _h = 0; _h < array_length(_conds); _h += 1)
    {
        if (ntl_lua_truthy(ntl_lua_ex(_env, _conds[_h])))
            return ntl_lua_ev_block(ntl_lua_env_pool_get(_env, "if" + string(_h)), _blocks[_h]);
    }
    var _elseB = ds_map_find_value(_n, "else");
    if (array_length(_elseB) > 0) return ntl_lua_ev_block(ntl_lua_env_pool_get(_env, "else"), _elseB);
    return undefined;
}

// ---- while ----
if (_k == "while")
{
    var _guard = 0;
    var _wCond = ds_map_find_value(_n, "cond");
    var _wBody = ds_map_find_value(_n, "body");
    var _wEnv = ntl_lua_env_new(_env);
    while (ntl_lua_truthy(ntl_lua_ex(_env, _wCond)))
    {
        // ★ 同上：环境复用（原来每轮 ds_map_create）
        ds_map_clear(_wEnv);
        ds_map_add(_wEnv, "_ntlp", _env);
        var _r = ntl_lua_ev_block(_wEnv, _wBody);
        if (ntl_lua_is_ctrl(_r))
        {
            if (ds_map_find_value(_r, "_ntlctrl") == "break") break;
            return _r;
        }
        _guard += 1;
        if (_guard > 200000) { ntl_lua_rt_err("loop limit exceeded"); break; }
    }
    return undefined;
}

// ---- repeat ----
if (_k == "repeat")
{
    var _guard2 = 0;
    // ★ 环境复用（原来每轮 ds_map_create）
    var _rpBody = ds_map_find_value(_n, "body");
    var _rpCond = ds_map_find_value(_n, "cond");
    var _rpEnv = ntl_lua_env_new(_env);
    while (1)
    {
        ds_map_clear(_rpEnv);
        ds_map_add(_rpEnv, "_ntlp", _env);
        var _r2 = ntl_lua_ev_block(_rpEnv, _rpBody);
        if (ntl_lua_is_ctrl(_r2))
        {
            if (ds_map_find_value(_r2, "_ntlctrl") == "break") break;
            return _r2;
        }
        if (ntl_lua_truthy(ntl_lua_ex(_env, _rpCond))) break;
        _guard2 += 1;
        if (_guard2 > 200000) { ntl_lua_rt_err("loop limit exceeded"); break; }
    }
    return undefined;
}

// ---- 数值 for ----
if (_k == "fornum")
{
    var _from = real(ntl_lua_ex(_env, ds_map_find_value(_n, "from")));
    var _to   = real(ntl_lua_ex(_env, ds_map_find_value(_n, "to")));
    var _step = 1;
    if (ds_map_exists(_n, "step")) _step = real(ntl_lua_ex(_env, ds_map_find_value(_n, "step")));
    if (_step == 0) _step = 1;
    var _loopEnv = ntl_lua_env_new(_env);
    var _guard3 = 0;
    var _i3 = _from;
    // ★★★ 性能 + 内存：循环体环境只创建一次，每轮清空复用
    //     原来每轮都 ntl_lua_env_new()（= ds_map_create()），2 万次循环会创建
    //     2 万个 ds_map 且从不释放 —— 既慢又漏。
    var _nameNode = ds_map_find_value(_n, "name");
    var _bodyNode = ds_map_find_value(_n, "body");
    var _bodyEnv = ntl_lua_env_new(_loopEnv);
    while ((_step > 0 && _i3 <= _to) || (_step < 0 && _i3 >= _to))
    {
        ntl_lua_env_set(_loopEnv, _nameNode, _i3);
        // 清空局部变量但保留父链
        ds_map_clear(_bodyEnv);
        ds_map_add(_bodyEnv, "_ntlp", _loopEnv);
        var _r3 = ntl_lua_ev_block(_bodyEnv, _bodyNode);
        if (ntl_lua_is_ctrl(_r3))
        {
            if (ds_map_find_value(_r3, "_ntlctrl") == "break") break;
            return _r3;
        }
        _i3 += _step;
        _guard3 += 1;
        if (_guard3 > 200000) { ntl_lua_rt_err("loop limit exceeded"); break; }
    }
    return undefined;
}

// ---- 泛型 for（pairs / ipairs）----
if (_k == "forin")
{
    var _exps = ds_map_find_value(_n, "exprs");
    var _iter = ntl_lua_ex(_env, _exps[0]);
    var _names2 = ds_map_find_value(_n, "names");
    var _keys = ntl_dsmap_keys(_iter);
    var _loopEnv2 = ntl_lua_env_new(_env);
    // ★ 性能 + 内存：循环体环境只创建一次，每轮清空复用
    var _fiBody = ds_map_find_value(_n, "body");
    var _fiEnv = ntl_lua_env_new(_loopEnv2);
    var _n0 = (array_length(_names2) >= 1) ? _names2[0] : "";
    var _n1 = (array_length(_names2) >= 2) ? _names2[1] : "";
    var _kc2 = array_length(_keys);
    for (var _m = 0; _m < _kc2; _m += 1)
    {
        var _dk = _keys[_m];
        if (string_copy(string(_dk), 1, 1) == chr(1)) continue;
        var _key = ntl_lua_key_decode(_dk);
        var _val = ds_map_find_value(_iter, _dk);
        if (_n0 != "") ntl_lua_env_set(_loopEnv2, _n0, _key);
        if (_n1 != "") ntl_lua_env_set(_loopEnv2, _n1, _val);
        ds_map_clear(_fiEnv);
        ds_map_add(_fiEnv, "_ntlp", _loopEnv2);
        var _r4 = ntl_lua_ev_block(_fiEnv, _fiBody);
        if (ntl_lua_is_ctrl(_r4))
        {
            if (ds_map_find_value(_r4, "_ntlctrl") == "break") break;
            return _r4;
        }
    }
    return undefined;
}

// ---- 其它：表达式语句（函数调用）----
return ntl_lua_ex(_env, _n);
