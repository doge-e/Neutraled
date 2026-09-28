/// ntl_console_help(query) —— 显示命令列表 / 查询某命令用法
var _q = string_trim(string(argument[0]));

if (!variable_global_exists("ntl_console_cmds"))
{
    ntl_console_log(ntl_t("cmd.noreg"));
    return 0;
}
var _cmds = global.ntl_console_cmds;

// ---- 查询单个命令 ----
if (_q != "")
{
    if (ds_map_exists(_cmds, _q))
    {
        var _r = ds_map_find_value(_cmds, _q);
        if (!is_real(_r) || !ds_exists(_r, ds_type_map)) return 0;
        ntl_console_log("  " + ntl_t("msg.desc") + ": " + string(ds_map_find_value(_r, "desc")));
        var _u = string(ds_map_find_value(_r, "usage"));
        if (_u != "") ntl_console_log("  " + ntl_t("msg.usage") + ": " + _u);
        ntl_console_log("  " + ntl_t("msg.category") + ": " + string(ds_map_find_value(_r, "cat")));
        var _sc = string(ds_map_find_value(_r, "scope"));
        if (_sc == "mod") ntl_console_log("  " + ntl_t("msg.source") + ": " + ntl_t("msg.source_mod") + " " + string(ds_map_find_value(_r, "mod")));
        else ntl_console_log("  " + ntl_t("msg.source") + ": " + ntl_t("msg.source_ntl"));
        return 0;
    }
    // 模糊匹配
    var _keys = ntl_dsmap_keys(_cmds);
    var _hits = 0;
    ntl_console_log(ntl_t("msg.no_match") + ": " + _q);
    for (var _i = 0; _i < array_length(_keys); _i += 1)
    {
        if (string_pos(string_lower(_q), string_lower(string(_keys[_i]))) > 0)
        {
            ntl_console_log("  " + string(_keys[_i]));
            _hits += 1;
            if (_hits >= 15) break;
        }
    }
    if (_hits == 0) ntl_console_log(ntl_t("list.none"));
    return 0;
}

// ---- 总览（按分类）----
ntl_console_cmds("");
return 0;
