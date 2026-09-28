/// ntl_lua_chunk_run(name, args) —— 执行一个 load 出来的代码块
var _name = string(argument[0]);
var _args = (argument_count > 1) ? argument[1] : [];
if (!variable_global_exists("ntl_lua_chunks")) return undefined;
if (!ds_map_exists(global.ntl_lua_chunks, _name))
{
    ntl_log("lua", "[load] 找不到代码块: " + _name);
    return undefined;
}

var _body = ds_map_find_value(global.ntl_lua_chunks, _name);

var _env;
if (variable_global_exists("ntl_lua_env")) _env = global.ntl_lua_env;
else if (variable_global_exists("ntl_lua_globals")) _env = ntl_lua_env_new(global.ntl_lua_globals);
else _env = ntl_lua_env_new(undefined);

if (is_array(_args) && array_length(_args) > 0) ntl_lua_env_declare(_env, "...", _args);
else ntl_lua_env_declare(_env, "...", []);

var _r = undefined;
try { _r = ntl_lua_ev_block(_env, _body); }
catch (e) { ntl_log("lua", "[load] 执行异常: " + string(e)); return undefined; }

if (ntl_lua_is_ctrl(_r))
{
    if (ds_map_find_value(_r, "_ntlctrl") == "return")
    {
        var _vals = ds_map_find_value(_r, "vals");
        var _vc = array_length(_vals);
        if (_vc == 0) return undefined;
        if (_vc == 1) return _vals[0];
        return _vals;
    }
}
return _r;