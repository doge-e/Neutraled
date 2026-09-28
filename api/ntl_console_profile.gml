/// ntl_console_profile() —— 显示运行时性能统计（找出谁拖慢了游戏）
ntl_console_log(ntl_t("perf.head"));

// Lua 编译缓存
var _cs = ntl_lua_cache_stats();
ntl_console_log(ntl_ts("perf.lua_cache", [string(_cs.hit), string(_cs.miss), string(_cs.rate)]));

// 帧率
ntl_console_log(ntl_ts("info.fps_real", [string(round(fps)), string(round(fps_real))]));
ntl_console_log(ntl_ts("perf.frames", [string(global.ntl_frames)]));

// 对象与绘制
if (variable_global_exists("ntl_obj_count")) ntl_console_log(ntl_ts("perf.kr_objs", [string(global.ntl_obj_count)]));
ntl_console_log(ntl_ts("perf.inst_total", [string(instance_count)]));

// Hook 数量
if (variable_global_exists("ntl_hooks"))
{
    var _hk = ntl_dsmap_keys(global.ntl_hooks);
    var _total = 0;
    for (var _i = 0; _i < array_length(_hk); _i += 1)
    {
        var _l = ds_map_find_value(global.ntl_hooks, _hk[_i]);
        if (is_real(_l)) _total += ds_list_size(_l);
    }
    ntl_console_log(ntl_ts("perf.hooks_fn", [string(_total), string(array_length(_hk))]));
}
if (variable_global_exists("ntl_bh_map"))
    ntl_console_log(ntl_ts("perf.hooks_bh", [string(ds_map_size(global.ntl_bh_map))]));
if (variable_global_exists("ntl_oev") && ds_map_size(global.ntl_oev) > 0)
    ntl_console_log(ntl_ts("perf.hooks_oev", [string(ds_map_size(global.ntl_oev))]));
if (variable_global_exists("ntl_mod_reg"))
    ntl_console_log(ntl_ts("perf.mods_reg", [string(ds_map_size(global.ntl_mod_reg))]));

// 地图
if (variable_global_exists("ntl_test_map"))
{
    var _mp = global.ntl_test_map;
    if (is_real(_mp))
        ntl_console_log(ntl_ts("perf.map", [string(ds_map_find_value(_mp, "id")), string(ds_map_find_value(_mp, "width")), string(ds_map_find_value(_mp, "height"))]));
}

// 内存 / 纹理统计：★ 这两行原来是
//     ntl_t(texture_debug_size_get ? "perf.tex_ok" : "perf.tex_unknown")
//     sprite_get_number(global.ntl_test_map != undefined ? 0 : 0)
//   两个都会在正式版运行时报错，而且 try/catch **拦不住**（变量名解析失败发生在进 try 之前）：
//     ① 把函数名当变量读 → "Variable obj_ntl_core.texture_debug_size_get(...) not set before reading it."
//     ② global.ntl_test_map 未定义时无条件读它 → "global variable name 'ntl_test_map' index not set before reading it."
//   实测这两条让每局都往 dr-api.log 里刷 4 条错误行（r13 真机验证时发现）。GM 的纹理统计只在
//   debug 版存在，Neutraled 数不出来就如实不报 —— 上面已经报了实例数、hook 数、对象数。

ntl_console_log(ntl_t("perf.end"));
return 0;
