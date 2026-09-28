/// ntl_oev_run(objName, eventName, self, mode) —— 对象事件 Hook 的运行时分派
/// 返回 [handled, value]：
///   handled=1 → 包装直接返回 value，跳过原事件代码（仅 override 模式）
///   handled=0 → 继续执行原事件代码
var _obj = string(argument[0]);
var _evt = string(argument[1]);
var _self = argument[2];
var _mode = string(argument[3]);

if (!variable_global_exists("ntl_oev")) return [0, undefined];
var _key = _obj + ":" + _evt;
if (!ds_map_exists(global.ntl_oev, _key)) return [0, undefined];

var _list = ds_map_find_value(global.ntl_oev, _key);
var _n = ds_list_size(_list);
if (_n <= 0) return [0, undefined];

for (var _i = 0; _i < _n; _i += 1)
{
    var _rec = ds_list_find_value(_list, _i);
    if (ds_map_find_value(_rec, "mode") != _mode) continue;

    var _hpath = ds_map_find_value(_rec, "handler");
    var _mdir = ds_map_find_value(_rec, "moddir");
    var _full = (_mdir != "") ? (_mdir + "/" + _hpath) : _hpath;

    global.ntl_lua_err = "";
    var _v = undefined;
    try
    {
        var _src = ntl_live_file_read(_full);
        if (_src == undefined)
        {
            ntl_log("oev", "[警告] 找不到 hook 脚本: " + _full);
            continue;
        }
        var _ast = ntl_live_compile(_src, _full);
        if (ds_map_find_value(_ast, "ok") != 1)
        {
            ntl_log("oev", "[语法错误] " + _full + ": " + string(ds_map_find_value(_ast, "err")));
            continue;
        }
        // 独立环境 + 注入宿主
        var _env = ntl_lua_env_new(global.ntl_lua_globals);
        ntl_lua_env_declare(_env, "self", _self);
        ntl_lua_env_declare(_env, "object_name", _obj);
        ntl_lua_env_declare(_env, "event_name", _evt);
        ntl_lua_env_declare(_env, "hook_mode", _mode);
        ntl_lua_ev_block(_env, ds_map_find_value(_ast, "ast"));
    }
    catch (_e)
    {
        ntl_log("oev", "[hook 异常已隔离] " + string(_e));
        continue;
    }

    if (global.ntl_lua_err != "")
    {
        ntl_log("oev", "[hook 错误] " + string(global.ntl_lua_err));
        global.ntl_lua_err = "";
        continue;
    }

    // override 模式：返回非 nil 即接管
    if (_mode == "override" && _v != undefined) return [1, _v];
}
return [0, undefined];
