/// ntl_lua_ex(env, node) —— 表达式求值
var _env = argument[0];
var _n = argument[1];
if (_n == undefined) return undefined;
var _k = ds_map_find_value(_n, "k");

switch (_k)
{
    case "num":  return ds_map_find_value(_n, "v");
    case "str":  return ds_map_find_value(_n, "v");
    case "nil":  return undefined;
    case "true": return true;
    case "false": return false;
    case "vararg":
    {
        var _va = ntl_lua_env_get(_env, "...");
        return (_va == undefined) ? [] : _va;
    }
    case "var":
    {
        var _nm = ds_map_find_value(_n, "name");
        // ★ 性能优化：一次走链完成"查找 + 取值"（原来要 has + get 两遍）
        var _hit = ntl_lua_env_find(_env, _nm);
        if (_hit[0] == 1 && _hit[1] != undefined) return _hit[1];
        // 兜底：查全局环境（_G）
        if (variable_global_exists("ntl_lua_globals"))
        {
            var _gv = ntl_lua_table_get(global.ntl_lua_globals, _nm);
            if (_gv != undefined) return _gv;
        }
        // ★ 未定义（或值为 nil）的全局名 → 回退为宿主函数名（调用时经 ntl_call_host 动态解析）
        return "__host:" + _nm;
    }
    case "function": return ntl_lua_fn_new(_n, _env);
    case "binop":
    {
        var _op = ds_map_find_value(_n, "op");
        var _a = ntl_lua_ex(_env, ds_map_find_value(_n, "l"));
        // 短路
        var _opc = string_copy(_op, 1, 1);
        if (_opc == "a" && _op == "and") return ntl_lua_truthy(_a) ? ntl_lua_ex(_env, ds_map_find_value(_n, "r")) : _a;
        if (_opc == "o" && _op == "or")  return ntl_lua_truthy(_a) ? _a : ntl_lua_ex(_env, ds_map_find_value(_n, "r"));
        var _b = ntl_lua_ex(_env, ds_map_find_value(_n, "r"));
        // ★ 性能：数字 + 运算符 直接内联（省一次 ntl_lua_binop 调用 + 参数打包）
        if (is_real(_a) && is_real(_b))
        {
            if (_op == "+")  return _a + _b;
            if (_op == "-")  return _a - _b;
            if (_op == "*")  return _a * _b;
            if (_op == "/")  return _a / _b;
            if (_op == "==") return (_a == _b) ? 1 : 0;
            if (_op == "~=") return (_a != _b) ? 1 : 0;
            if (_op == "<")  return (_a < _b) ? 1 : 0;
            if (_op == "<=") return (_a <= _b) ? 1 : 0;
            if (_op == ">")  return (_a > _b) ? 1 : 0;
            if (_op == ">=") return (_a >= _b) ? 1 : 0;
            if (_op == "%")  return (_b == 0) ? 0 : (_a mod _b);
            if (_op == "//") return (_b == 0) ? 0 : floor(_a / _b);
            if (_op == "^")  return power(_a, _b);
        }
        return ntl_lua_binop(_op, _a, _b);
    }
    case "unop":
        return ntl_lua_unop(ds_map_find_value(_n, "op"), ntl_lua_ex(_env, ds_map_find_value(_n, "e")));
    case "table":
    {
        var _t = ntl_lua_table_new();
        var _fields = ds_map_find_value(_n, "fields");
        var _auto = 0;
        for (var _i = 0; _i < array_length(_fields); _i += 1)
        {
            var _f = _fields[_i];
            var _key = ds_map_find_value(_f, "key");
            var _val = ntl_lua_ex(_env, ds_map_find_value(_f, "val"));
            if (_key == 0) { _auto += 1; ntl_lua_table_set(_t, _auto, _val); }
            else ntl_lua_table_set(_t, ntl_lua_ex(_env, _key), _val);
        }
        return _t;
    }
    case "field":
    {
        var _obj = ntl_lua_ex(_env, ds_map_find_value(_n, "obj"));
        return ntl_lua_index_get(_obj, ds_map_find_value(_n, "name"));
    }
    case "index":
    {
        var _obj2 = ntl_lua_ex(_env, ds_map_find_value(_n, "obj"));
        var _idx = ntl_lua_ex(_env, ds_map_find_value(_n, "idx"));
        return ntl_lua_index_get(_obj2, _idx);
    }
    case "call":
    {
        var _fn = ntl_lua_ex(_env, ds_map_find_value(_n, "fn"));
        var _args = ntl_lua_eval_args(_env, ds_map_find_value(_n, "args"));
        return ntl_lua_call(_fn, _args);
    }
    case "method":
    {
        var _obj3 = ntl_lua_ex(_env, ds_map_find_value(_n, "obj"));
        var _mname = ds_map_find_value(_n, "name");
        var _fn2 = ntl_lua_index_get(_obj3, _mname);   // 必须走元表链（obj:method 常见于类实例）
        var _raw = ntl_lua_eval_args(_env, ds_map_find_value(_n, "args"));
        // self 作为第一个参数（显式构造，避免 array_insert 索引歧义）
        var _args2 = [_obj3];
        for (var _mi = 0; _mi < array_length(_raw); _mi += 1) array_push(_args2, _raw[_mi]);
        return ntl_lua_call(_fn2, _args2);
    }
}
return undefined;
