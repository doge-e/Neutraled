/// ntl_live_reload —— 老式脚本（每个函数一个同名脚本资源）
    if (!variable_global_exists("ntl_live_mods")) { ntl_live_init(); return 1; }
    var _mods = global.ntl_live_mods;
    var _n = ds_list_size(_mods);
    for (var _i = 0; _i < _n; _i += 1)
    {
        var _entry = ds_list_find_value(_mods, _i);
        ds_map_destroy(ds_map_find_value(_entry, "hooks"));
        ds_map_destroy(ds_map_find_value(_entry, "cache"));
        ds_map_destroy(ds_map_find_value(_entry, "astcache"));
        ds_map_destroy(ds_map_find_value(_entry, "env"));
        ds_map_destroy(_entry);
    }
    ds_list_destroy(_mods);
    // 重建整张表后必须让 ntl_live_emit 重新按依赖排序（那个标志以前只置 1、从不重置）
    global.ntl_live_sorted = 0;
    ntl_live_init();
    return ds_list_size(global.ntl_live_mods);
