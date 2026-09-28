/// ntl_console_run_mod_cmd(rec, fullName, rest) —— 执行一个 mod 注册的控制台命令
var _rec = argument[0];
var _full = string(argument[1]);
var _rest = string(argument[2]);

var _handler = string(ds_map_find_value(_rec, "handler"));
var _mod = string(ds_map_find_value(_rec, "mod"));

if (_handler == "")
{
    ntl_console_log(ntl_ts("modcmd.nohandler", [_full]));
    return 0;
}

// 定位脚本：mod 目录 + handler
var _path = _handler;
if (string_pos("/", _path) <= 0 && string_pos("\\", _path) <= 0) _path = "console/" + _path;

// 优先在 mod 目录下找
if (!variable_global_exists("ntl_mod_reg"))
{
    ntl_console_log(ntl_t("modcmd.noreg"));
    return 0;
}

// 从 ntl_live_mods 里找到该 mod 的目录
var _dir = "";
if (variable_global_exists("ntl_live_mods"))
{
    var _n = ds_list_size(global.ntl_live_mods);
    for (var _i = 0; _i < _n; _i += 1)
    {
        var _e = ds_list_find_value(global.ntl_live_mods, _i);
        if (string(ds_map_find_value(_e, "name")) == _mod)
        {
            _dir = string(ds_map_find_value(_e, "dir"));
            break;
        }
    }
}
if (_dir == "") { ntl_console_log(ntl_ts("modcmd.nodir", [_mod])); return 0; }

var _fullPath = _dir + _path;
if (!file_exists(_fullPath)) { ntl_console_log(ntl_ts("modcmd.noscript", [_fullPath])); return 0; }

// 执行（在 mod 的上下文里）
global.ntl_console_arg = _rest;
global.ntl_current_mod = _mod;
global.ntl_ctx_module = _mod;
try
{
    var _src = ntl_live_file_read(_fullPath);
    var _ast = ntl_live_compile(_src, _fullPath);
    if (ds_map_find_value(_ast, "ok") != 1)
    {
        ntl_console_log(ntl_ts("modcmd.syntax", [ds_map_find_value(_ast, "err")]));
        return 0;
    }
    var _env = ntl_lua_env_new(global.ntl_lua_globals);
    ntl_lua_env_declare(_env, "arg", _rest);
    ntl_lua_env_declare(_env, "args", ntl_live_split_args(_rest));
    ntl_lua_env_declare(_env, "__mod_dir", _dir);
    ntl_lua_ev_block(_env, ds_map_find_value(_ast, "ast"));
}
catch (e) { ntl_console_log(ntl_ts("modcmd.exc", [e])); }
if (global.ntl_lua_err != "")
{
    ntl_console_log(ntl_ts("modcmd.err", [global.ntl_lua_err]));
    global.ntl_lua_err = "";
}
return 1;
