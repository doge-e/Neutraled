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
        // ★ 2026-11-04 修复：原来写 ntl_live_compile(_src, _full)（签名是 path, src, cache）→
        //   cache=undefined 直接 Code Error，且用 ds_map_find_value(_ast,"ok") 判语法错误
        //   （Lua 编译结果没有 "ok" 键）→ 对象事件 hook **从来跑不起来**。
        //   另外 _v 从来没被赋值过 → override 模式即使脚本返回了值也不会接管。
        //   现在走 ntl_lua_fastpath（路径+mtime 缓存），每帧 hook 不重复解析。
        var _fp = ntl_lua_fastpath(_full);
        if (_fp == undefined)
        {
            ntl_log("oev", "[警告] 找不到或编译失败: " + _full);
            continue;
        }
        var _ast = ds_map_find_value(_fp, "ast");
        global.ntl_last_was_lua = ds_map_find_value(_fp, "lua");
        // 独立环境 + 注入宿主（Lua 用表环境，NTL 用 NTL 环境）
        if (global.ntl_last_was_lua == 1)
        {
            if (!variable_global_exists("ntl_lua_globals")) { global.ntl_lua_globals = ntl_lua_table_new(); ntl_lua_stdlib(); }
            var _env = ntl_lua_env_new(global.ntl_lua_globals);
            ntl_lua_env_declare(_env, "self", _self);
            ntl_lua_env_declare(_env, "object_name", _obj);
            ntl_lua_env_declare(_env, "event_name", _evt);
            ntl_lua_env_declare(_env, "hook_mode", _mode);
            _v = ntl_lua_ev_block(_env, ds_map_find_value(_ast, "ast"));
        }
        else
        {
            var _nenv = ntl_e_new_env();
            ntl_e_env_set(_nenv, "self", _self);
            ntl_e_env_set(_nenv, "object_name", _obj);
            ntl_e_env_set(_nenv, "event_name", _evt);
            ntl_e_env_set(_nenv, "hook_mode", _mode);
            global.ntl_live_budget = 200000;
            _v = ntl_ex(_ast, _nenv);
        }
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
