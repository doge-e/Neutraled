/// ntl_console_help_lookup(name) —— 查询某个名字：是游戏资源？mod 函数？内置 API？
var _name = string(argument[0]);
if (string_length(_name) <= 0) return 0;

ntl_console_log(ntl_ts("hl.q", [_name]), 1);

// 1) 是否是可解析的资源（函数/对象/精灵/声音…）
var _idx = asset_get_index(_name);
if (_idx >= 0)
{
    ntl_console_log(ntl_ts("hl.ok", [_idx]), 1);
    // 尝试判断类型
    if (script_exists(_idx)) ntl_console_log(ntl_t("hl.script"), 1);
    else ntl_console_log(ntl_t("hl.res"), 1);
}
else
{
    ntl_console_log(ntl_t("hl.bad"), 1);
}

// 2) 是否是 mod 导出的命名空间函数
if (variable_global_exists("ntl_ns_map"))
{
    var _hit = 0;
    var _keys = ntl_dsmap_keys(global.ntl_ns_map);
    for (var _i = 0; _i < array_length(_keys); _i += 1)
    {
        if (string_pos(_name, _keys[_i]) > 0)
        {
            ntl_console_log("  [mod] " + _keys[_i], 1);
            _hit += 1;
            if (_hit >= 10) break;
        }
    }
}

// 3) 是否是 Neutraled 内置函数
var _builtins = ["ntl_log", "ntl_console_log", "ntl_map_load", "ntl_player_test", "ntl_obj_spawn",
                 "ntl_goto_chapter", "ntl_screenshot", "ntl_live_reload", "ntl_require_lua",
                 "map_verify_all", "require_lua", "draw_text", "draw_set_color"];
for (var _b = 0; _b < array_length(_builtins); _b += 1)
{
    if (string_pos(string_lower(_name), string_lower(_builtins[_b])) > 0)
        ntl_console_log(ntl_ts("hl.builtin", [_builtins[_b]]), 1);
}

ntl_console_log(ntl_t("hl.tip"), 1);
return 0;
