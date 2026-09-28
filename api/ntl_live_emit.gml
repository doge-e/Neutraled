/// ntl_live_emit —— 老式脚本（每个函数一个同名脚本资源）
var _event = argument[0];
var _arg = argument[1];
global.ntl_ctx_event = _event;   // 错误定位：当前事件名（on_frame / on_draw …）
    if (!variable_global_exists("ntl_live_mods")) return 0;
    if (!variable_global_exists("ntl_live_enabled")) return 0;
    if (global.ntl_live_enabled != 1) return 0;

    // ★ 首次广播 on_init 前，按依赖关系重排加载顺序（mod 互操作要求被依赖者先跑）
    if (_event == "on_init" && !variable_global_exists("ntl_live_sorted"))
    {
        global.ntl_live_sorted = 1;
        try { ntl_live_sort_by_deps(); } catch (e) { ntl_log("live", "[ntl] ntl_live_emit.gml:13 ntl_live_sort_by_deps 失败: " + string(e)); }
    }

    var _mods = global.ntl_live_mods;
    var _n = ds_list_size(_mods);
    var _ran = 0;

    for (var _i = 0; _i < _n; _i += 1)
    {
        var _entry = ds_list_find_value(_mods, _i);
        var _hooks = ds_map_find_value(_entry, "hooks");
        if (!is_real(_hooks) || !ds_exists(_hooks, ds_type_map)) continue;
        if (!ds_map_exists(_hooks, _event)) continue;

        var _file = ds_map_find_value(_hooks, _event);
        var _path = ds_map_find_value(_entry, "dir") + _file;

        // 源码读取（仅首次）+ AST 缓存（避免每帧重新解析）
        var _srcCache = ds_map_find_value(_entry, "cache");
        var _astCache = ds_map_find_value(_entry, "astcache");
        if (!is_real(_srcCache) || !ds_exists(_srcCache, ds_type_map)) continue;
        var _src = "";
        if (ds_map_exists(_srcCache, _path)) _src = ds_map_find_value(_srcCache, _path);
        else
        {
            if (!file_exists(_path)) { ntl_log("live", "[警告] 脚本缺失: " + _path); continue; }
            _src = ntl_live_file_read(_path);
            ds_map_add(_srcCache, _path, _src);
        }
        // 调试埋点（global.ntl_live_debug = 1 时开启）
        var _dbg = 0;
        if (variable_global_exists("ntl_live_debug") && global.ntl_live_debug == 1) _dbg = 1;
        if (_dbg == 1) ntl_log("live", "  > " + _event + " -> " + _file + " (" + string(string_length(_src)) + " chars)");
        var _env = ds_map_find_value(_entry, "env");
        // 事件参数存入环境，脚本可用 arg 读取
        ntl_e_env_set(_env, "arg", _arg);
        ntl_e_env_set(_env, "mod_name", ds_map_find_value(_entry, "name"));
        // 当前 mod 目录（Lua 的 require 用它做模块搜索）
        global.ntl_lua_mod_dir = ds_map_find_value(_entry, "dir");
        ntl_e_env_set(_env, "__mod_dir", global.ntl_lua_mod_dir);
        // ★ 专用变量：当前正在执行的 mod id（供 ntl_mod_export 等互操作 API 使用）
        //   不用 ntl_ctx_module 是因为它会被多处改写，不可靠
        global.ntl_current_mod = string(ds_map_find_value(_entry, "name"));
        global.ntl_ctx_module = global.ntl_current_mod;
        var _ast = ntl_live_compile(_path, _src, _astCache);
        var _ok = false;
        var _isLua = (string_length(_path) >= 4 &&
                      string_lower(string_copy(_path, string_length(_path) - 3, 4)) == ".lua");
        if (_ast != undefined)
        {
            var _lbl = ds_map_find_value(_entry, "name") + "/" + _file;
            // ★ F4：记录每个 mod 的耗时（用于 profile 排行）
            var _t0 = current_time;
            if (_isLua)
            {
                _ok = ntl_live_run_lua(_ast, _env, _lbl, _arg);
            }
            else _ok = ntl_live_run_ast(_ast, _env, _lbl);
            var _dt = current_time - _t0;
            try { ntl_perf_record(string(ds_map_find_value(_entry, "name")), _dt); } catch (e) { ntl_log("live", "[ntl] ntl_live_emit.gml:72 ntl_perf_record 失败: " + string(e)); }
        }
        if (_ok) _ran += 1;
        if (_dbg == 1 || !_ok)
        {
            if (_ok) ntl_log("live", "    result=OK");
            else ntl_log("live", "    result=FAIL");
        }
    }
    return _ran;
