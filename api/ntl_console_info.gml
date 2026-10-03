/// ntl_console_info(cmd, rest) —— 信息类命令实现（mods/hooks/modinfo/timing）
var _cmd = string(argument[0]);
var _rest = (argument_count > 1) ? string(argument[1]) : "";

// ==================== mods ====================
// ★ 用户主诉修复（"全 mod 环境下输入 mods 只列出了 1 个 mod"）：
//   旧实现只数 global.ntl_live_mods（**运行时脚本层**，只有 live/ 下被加载的脚本包），
//   与本产物真正加载的 mod 清单无关 ⇒ 12 个已加载 mod 只显示 1 个。
//   正确数据源 = <working_directory>Neutraled/mods.json（builder 部署时写，含 Id/Name/Version），
//   由只读 API ntl_modmenu_loaded() / ntl_modmenu_loaded_count() / ntl_modmenu_mods() 提供。
if (_cmd == "mods")
{
    var _ld = ntl_modmenu_loaded();           // struct 数组，字段首字母大写：Id / Name / Version
    var _dirs = ntl_modmenu_mods();           // 磁盘 mods/ 下的目录名（已排序）
    var _nLoad = ntl_modmenu_loaded_count();  // -1 = 本产物没有 mods.json（老产物 / 外部章节 exe）
    var _dirN = array_length(_dirs);
    var _kw = string_lower(string_trim(_rest));

    if (_nLoad < 0) ntl_console_log(ntl_ts("mods.head_nolist", [string(_dirN)]));
    else ntl_console_log(ntl_ts("mods.head", [string(array_length(_ld)), string(_dirN)]));

    // ★ 用户主诉修复：不再限制显示条数（原为 30 条上限）—— 命中的 mod 全部列出
    var _shown = 0;
    var _hit = 0;
    for (var _i = 0; _i < array_length(_ld); _i += 1)
    {
        var _e = _ld[_i];
        if (!is_struct(_e)) continue;
        var _id = variable_struct_exists(_e, "Id") ? string(variable_struct_get(_e, "Id")) : "";
        var _nm = variable_struct_exists(_e, "Name") ? string(variable_struct_get(_e, "Name")) : "";
        var _vr = variable_struct_exists(_e, "Version") ? string(variable_struct_get(_e, "Version")) : "";
        if (_kw != "" && string_pos(_kw, string_lower(_nm + " " + _id + " " + _vr)) <= 0) continue;
        _hit += 1;
        _shown += 1;
        if (_nm == "") _nm = ntl_t("mods.noname");
        ntl_console_log(ntl_ts("mods.item", [string(_shown), _nm, _id, _vr]));
    }
    if (_hit == 0)
    {
        if (_kw != "") ntl_console_log(ntl_ts("mods.nomatch", [_rest]));
        else ntl_console_log(ntl_t("mods.none"));
    }
    else if (_hit > _shown) ntl_console_log(ntl_ts("mods.more", [string(_hit - _shown)]));

    // 运行时脚本层（live / mods 目录下的 .gml 脚本包）—— 与"已加载 mod 清单"是两件事，分开列
    var _nl = 0;
    if (variable_global_exists("ntl_live_mods")) _nl = ds_list_size(global.ntl_live_mods);
    if (_nl > 0)
    {
        ntl_console_log(ntl_ts("mods.live_head", [string(_nl)]));
        for (var _i2 = 0; _i2 < _nl; _i2 += 1)
        {
            var _e2 = ds_list_find_value(global.ntl_live_mods, _i2);
            var _src = (ds_map_find_value(_e2, "from_mods") == 1) ? "mods/" : "live/";
            ntl_console_log("  [" + _src + "] " + string(ds_map_find_value(_e2, "name")));
        }
    }

    // Lua 互操作注册表（只有参与互操作的 mod 才在里面）
    if (_kw == "" && variable_global_exists("ntl_mod_reg") && ds_map_size(global.ntl_mod_reg) > 0)
    {
        ntl_console_log(ntl_ts("info.mods_reg", [string(ds_map_size(global.ntl_mod_reg))]));
        var _ks = ntl_dsmap_keys(global.ntl_mod_reg);
        for (var _i3 = 0; _i3 < array_length(_ks); _i3 += 1)
        {
            var _r = ds_map_find_value(global.ntl_mod_reg, _ks[_i3]);
            if (!is_real(_r) || !ds_exists(_r, ds_type_map)) continue;
            var _exp = ds_map_find_value(_r, "exports");
            ntl_console_log("  " + string(_ks[_i3]) + ntl_ts("info.mods_exports", [string(ntl_lua_table_count(_exp))]));
        }
    }

    if (_kw == "") ntl_console_log(ntl_t("mods.hint"));
    return 0;
}

// ==================== hooks ====================
if (_cmd == "hooks")
{
    // ★ 缺陷 1 配套：global.ntl_hooks 现在同时装着"事件订阅槽"（mod 入口脚本 ntl_hook()）
    //   和"函数 hook 槽"（hook-registry.json）。分两段列，否则开发者看不出自己的订阅在不在。
    ntl_console_log(ntl_t("info.hooks_ev"));
    var _evN = 0;
    var _fnN = 0;
    if (variable_global_exists("ntl_hooks"))
    {
        var _evs = ntl_dsmap_keys(global.ntl_hooks);
        for (var _i = 0; _i < array_length(_evs); _i += 1)
        {
            var _isReg = (variable_global_exists("ntl_hook_reg_keys") && ds_list_find_index(global.ntl_hook_reg_keys, _evs[_i]) >= 0);
            if (_isReg) { _fnN += 1; continue; }
            _evN += 1;
            ntl_console_log("    " + string(_evs[_i]) + ntl_ts("info.hooks_subs", [string(ntl_hook_count(_evs[_i]))]));
        }
        if (_evN == 0) ntl_console_log(ntl_t("info.hooks_none_ev"));
    }
    ntl_console_log(ntl_t("info.hooks_fn"));
    ntl_console_log(ntl_ts("info.hooks_cov", [string(_fnN)]));
    if (variable_global_exists("ntl_hooks"))
    {
        var _fns = ntl_dsmap_keys(global.ntl_hooks);
        for (var _i2 = 0; _i2 < array_length(_fns); _i2 += 1)
        {
            var _isReg2 = (variable_global_exists("ntl_hook_reg_keys") && ds_list_find_index(global.ntl_hook_reg_keys, _fns[_i2]) >= 0);
            if (!_isReg2) continue;
            ntl_console_log("    " + string(_fns[_i2]) + ntl_ts("info.hooks_n", [string(ntl_hook_count(_fns[_i2]))]));
        }
        if (_fnN == 0) ntl_console_log(ntl_t("info.none"));
    }
    ntl_console_log(ntl_t("info.hooks_bh"));
    if (variable_global_exists("ntl_bh_map"))
    {
        var _bk = ntl_dsmap_keys(global.ntl_bh_map);
        for (var _i = 0; _i < array_length(_bk); _i += 1)
            ntl_console_log("    " + string(_bk[_i]));
        if (array_length(_bk) == 0) ntl_console_log(ntl_t("info.none"));
    }
    ntl_console_log(ntl_t("info.hooks_oev"));
    if (variable_global_exists("ntl_oev"))
    {
        var _ok = ntl_dsmap_keys(global.ntl_oev);
        for (var _i = 0; _i < array_length(_ok); _i += 1)
            ntl_console_log("    " + string(_ok[_i]));
        if (array_length(_ok) == 0) ntl_console_log(ntl_t("info.none"));
    }
    return 0;
}

// ==================== modinfo ====================
if (_cmd == "modinfo")
{
    if (_rest == "") { ntl_console_log(ntl_t("info.modinfo_u")); return 0; }
    var _found = 0;
    var _kw2 = string_lower(_rest);

    // ★ 缺陷 1 配套修复：先查"本产物已加载清单"（mods.json）。
    //   旧实现只查 global.ntl_live_mods ⇒ 12 个已加载 mod 用 modinfo 一个都查不到。
    var _ld2 = ntl_modmenu_loaded();
    for (var _i = 0; _i < array_length(_ld2); _i += 1)
    {
        var _e3 = _ld2[_i];
        if (!is_struct(_e3)) continue;
        var _id2 = variable_struct_exists(_e3, "Id") ? string(variable_struct_get(_e3, "Id")) : "";
        var _nm2 = variable_struct_exists(_e3, "Name") ? string(variable_struct_get(_e3, "Name")) : "";
        var _vr2 = variable_struct_exists(_e3, "Version") ? string(variable_struct_get(_e3, "Version")) : "";
        if (_nm2 != "" && _id2 == "") _id2 = _nm2;
        if (string_pos(_kw2, string_lower(_nm2 + " " + _id2)) <= 0) continue;
        ntl_console_log(ntl_t("info.mod") + ((_nm2 != "") ? _nm2 : _id2));
        ntl_console_log(ntl_ts("info.mod_id", [_id2]));
        ntl_console_log(ntl_ts("info.mod_ver", [_vr2]));
        var _dir = ntl_console_mod_dir(_id2, _nm2);
        if (_dir != "") ntl_console_log(ntl_ts("info.mod_dir", [_dir]));
        else ntl_console_log(ntl_t("info.mod_dir_none"));
        _found += 1;
    }

    // 运行时脚本层（live）：保留原有的 hooks 明细
    if (variable_global_exists("ntl_live_mods"))
    {
        var _n2 = ds_list_size(global.ntl_live_mods);
        for (var _i2 = 0; _i2 < _n2; _i2 += 1)
        {
            var _e2 = ds_list_find_value(global.ntl_live_mods, _i2);
            if (string_pos(_kw2, string_lower(string(ds_map_find_value(_e2, "name")))) <= 0) continue;
            ntl_console_log(ntl_t("info.mod_live") + string(ds_map_find_value(_e2, "name")));
            ntl_console_log(ntl_ts("info.mod_dir", [string(ds_map_find_value(_e2, "dir"))]));
            var _hj = ds_map_find_value(_e2, "hooks");
            if (!is_real(_hj) || !ds_exists(_hj, ds_type_map)) { _found += 1; continue; }
            var _hk = ntl_dsmap_keys(_hj);
            ntl_console_log(ntl_ts("info.mod_hooks", [string(array_length(_hk))]));
            for (var _k = 0; _k < array_length(_hk); _k += 1)
                ntl_console_log("    " + string(_hk[_k]) + " -> " + string(ds_map_find_value(_hj, _hk[_k])));
            _found += 1;
        }
    }
    if (_found == 0) ntl_console_log(ntl_ts("info.no_mod", [_rest]));
    return 0;
}

// ==================== timing ====================
if (_cmd == "timing")
{
    ntl_console_log(ntl_ts("info.uptime", [string(round(current_time / 1000))]));
    ntl_console_log(ntl_t("st.frames") + ": " + string(global.ntl_frames));
    ntl_console_log(ntl_ts("info.fps_real", [string(round(fps)), string(round(fps_real))]));
    if (global.ntl_frames > 0)
    {
        var _avg = (current_time / 1000) / global.ntl_frames;
        ntl_console_log(ntl_ts("info.avg_frame", [string(round(_avg * 1000) / 1000)]));
    }
    return 0;
}

return 0;
