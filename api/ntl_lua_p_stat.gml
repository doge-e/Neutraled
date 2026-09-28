/// ntl_lua_p_stat(s) —— 单条语句
var _s = argument[0];
var _t = ntl_lua_p_peek(_s, 0);
var _ty = ds_map_find_value(_t, "t");
var _v = ds_map_find_value(_t, "v");

// ---- return ----
if (_ty == "kw" && _v == "return")
{
    ntl_lua_p_next(_s);
    var _n = ntl_lua_p_node("return");
    var _es = [];
    var _t2 = ntl_lua_p_peek(_s, 0);
    if (!(ds_map_find_value(_t2, "t") == "eof") &&
        !(_t2 != undefined && ds_map_find_value(_t2, "t") == "kw" &&
          (ds_map_find_value(_t2, "v") == "end" || ds_map_find_value(_t2, "v") == "else" ||
           ds_map_find_value(_t2, "v") == "elseif" || ds_map_find_value(_t2, "v") == "until")))
    {
        array_push(_es, ntl_lua_p_expr(_s));
        while (ntl_lua_p_accept(_s, ",")) array_push(_es, ntl_lua_p_expr(_s));
    }
    ds_map_add(_n, "exprs", _es);
    return _n;
}

// ---- break ----
if (_ty == "kw" && _v == "break") { ntl_lua_p_next(_s); return ntl_lua_p_node("break"); }

// ---- do block end ----
if (_ty == "kw" && _v == "do")
{
    ntl_lua_p_next(_s);
    var _n2 = ntl_lua_p_node("do");
    ds_map_add(_n2, "body", ntl_lua_p_block(_s));
    ntl_lua_p_expect(_s, "end");
    return _n2;
}

// ---- while exp do block end ----
if (_ty == "kw" && _v == "while")
{
    ntl_lua_p_next(_s);
    var _n3 = ntl_lua_p_node("while");
    ds_map_add(_n3, "cond", ntl_lua_p_expr(_s));
    ntl_lua_p_expect(_s, "do");
    ds_map_add(_n3, "body", ntl_lua_p_block(_s));
    ntl_lua_p_expect(_s, "end");
    return _n3;
}

// ---- repeat block until exp ----
if (_ty == "kw" && _v == "repeat")
{
    ntl_lua_p_next(_s);
    var _n4 = ntl_lua_p_node("repeat");
    ds_map_add(_n4, "body", ntl_lua_p_block(_s));
    ntl_lua_p_expect(_s, "until");
    ds_map_add(_n4, "cond", ntl_lua_p_expr(_s));
    return _n4;
}

// ---- if exp then block {elseif} [else] end ----
if (_ty == "kw" && _v == "if")
{
    ntl_lua_p_next(_s);
    var _n5 = ntl_lua_p_node("if");
    var _conds = [];
    var _blocks = [];
    array_push(_conds, ntl_lua_p_expr(_s));
    ntl_lua_p_expect(_s, "then");
    array_push(_blocks, ntl_lua_p_block(_s));
    while (ntl_lua_p_is(_s, "elseif"))
    {
        ntl_lua_p_next(_s);
        array_push(_conds, ntl_lua_p_expr(_s));
        ntl_lua_p_expect(_s, "then");
        array_push(_blocks, ntl_lua_p_block(_s));
    }
    ds_map_add(_n5, "conds", _conds);
    ds_map_add(_n5, "blocks", _blocks);
    var _elseBlock = [];
    if (ntl_lua_p_accept(_s, "else")) _elseBlock = ntl_lua_p_block(_s);
    ds_map_add(_n5, "else", _elseBlock);
    ntl_lua_p_expect(_s, "end");
    return _n5;
}

// ---- for ----
if (_ty == "kw" && _v == "for")
{
    ntl_lua_p_next(_s);
    var _fname = ds_map_find_value(ntl_lua_p_next(_s), "v");
    var _names = [_fname];
    while (ntl_lua_p_accept(_s, ",")) array_push(_names, ds_map_find_value(ntl_lua_p_next(_s), "v"));

    if (ntl_lua_p_accept(_s, "="))   // 数值 for
    {
        var _n6 = ntl_lua_p_node("fornum");
        ds_map_add(_n6, "name", _fname);
        ds_map_add(_n6, "from", ntl_lua_p_expr(_s));
        ntl_lua_p_expect(_s, ",");
        ds_map_add(_n6, "to", ntl_lua_p_expr(_s));
        if (ntl_lua_p_accept(_s, ",")) ds_map_add(_n6, "step", ntl_lua_p_expr(_s));
        ntl_lua_p_expect(_s, "do");
        ds_map_add(_n6, "body", ntl_lua_p_block(_s));
        ntl_lua_p_expect(_s, "end");
        return _n6;
    }
    ntl_lua_p_expect(_s, "in");      // 泛型 for
    var _n7 = ntl_lua_p_node("forin");
    ds_map_add(_n7, "names", _names);
    var _exps = [ntl_lua_p_expr(_s)];
    while (ntl_lua_p_accept(_s, ",")) array_push(_exps, ntl_lua_p_expr(_s));
    ds_map_add(_n7, "exprs", _exps);
    ntl_lua_p_expect(_s, "do");
    ds_map_add(_n7, "body", ntl_lua_p_block(_s));
    ntl_lua_p_expect(_s, "end");
    return _n7;
}

// ---- local ----
if (_ty == "kw" && _v == "local")
{
    ntl_lua_p_next(_s);
    if (ntl_lua_p_accept(_s, "function"))
    {
        var _fn = ntl_lua_p_node("localfunc");
        ds_map_add(_fn, "name", ds_map_find_value(ntl_lua_p_next(_s), "v"));
        ntl_lua_p_funcbody(_s, _fn);
        return _fn;
    }
    var _n8 = ntl_lua_p_node("local");
    var _names2 = [ds_map_find_value(ntl_lua_p_next(_s), "v")];
    while (ntl_lua_p_accept(_s, ",")) array_push(_names2, ds_map_find_value(ntl_lua_p_next(_s), "v"));
    ds_map_add(_n8, "names", _names2);
    var _vals = [];
    if (ntl_lua_p_accept(_s, "="))
    {
        array_push(_vals, ntl_lua_p_expr(_s));
        while (ntl_lua_p_accept(_s, ",")) array_push(_vals, ntl_lua_p_expr(_s));
    }
    ds_map_add(_n8, "values", _vals);
    return _n8;
}

// ---- function name.a.b:c() ----
if (_ty == "kw" && _v == "function")
{
    ntl_lua_p_next(_s);
    var _path = [ds_map_find_value(ntl_lua_p_next(_s), "v")];
    while (ntl_lua_p_accept(_s, ".")) array_push(_path, ds_map_find_value(ntl_lua_p_next(_s), "v"));
    var _selfName = "";
    if (ntl_lua_p_accept(_s, ":")) _selfName = ds_map_find_value(ntl_lua_p_next(_s), "v");
    var _n9 = ntl_lua_p_node("funcdef");
    ds_map_add(_n9, "path", _path);
    ds_map_add(_n9, "self", _selfName);
    ntl_lua_p_funcbody(_s, _n9);
    // Lua 语义：function T:m(...) 等价于 function T.m(self, ...) —— 必须把 self 插到参数最前
    if (_selfName != "")
    {
        var _oldParams = ds_map_find_value(_n9, "params");
        var _newParams = ["self"];
        for (var _pi = 0; _pi < array_length(_oldParams); _pi += 1)
            array_push(_newParams, _oldParams[_pi]);
        ds_map_replace(_n9, "params", _newParams);
    }
    return _n9;
}

// ---- 表达式语句：赋值 或 函数调用 ----
var _e = ntl_lua_p_prefix(_s);
if (ntl_lua_p_is(_s, "=") || ntl_lua_p_is(_s, ","))
{
    var _targets = [_e];
    while (ntl_lua_p_accept(_s, ",")) array_push(_targets, ntl_lua_p_prefix(_s));
    ntl_lua_p_expect(_s, "=");
    var _vals2 = [ntl_lua_p_expr(_s)];
    while (ntl_lua_p_accept(_s, ",")) array_push(_vals2, ntl_lua_p_expr(_s));
    var _na = ntl_lua_p_node("assign");
    ds_map_add(_na, "targets", _targets);
    ds_map_add(_na, "values", _vals2);
    return _na;
}
return _e;   // 函数调用
