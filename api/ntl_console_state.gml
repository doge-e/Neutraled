/// ntl_console_state(cmd, rest) —— 状态类命令实现
var _cmd = string(argument[0]);
var _rest = (argument_count > 1) ? string(argument[1]) : "";
var _LIMIT = 40;   // 长列表的显示上限（控制台缓冲只有 200 行，刷屏会把前面的输出挤掉）

if (_cmd == "room")
{
    ntl_console_log(ntl_t("st.room") + ": " + string(room) + "  " + room_get_name(room));
    ntl_console_log(ntl_t("st.size") + ": " + string(room_width) + " x " + string(room_height));
    ntl_console_log(ntl_t("st.instances") + ": " + string(instance_count));
    // ★ 反人类/错值修复：原来这一行也是 instance_count（与上一行同一个数，两个不同的键），
    //   现在统计"有实例的对象**种类数**"，才是 st.objcount 该表达的东西。
    var _seen = ds_map_create();
    var _objN = 0;
    for (var _i2 = 0; _i2 < instance_count; _i2 += 1)
    {
        var _ins2 = instance_id_get(_i2);
        if (!instance_exists(_ins2)) continue;
        var _oid = string(_ins2.object_index);
        if (!ds_map_exists(_seen, _oid)) { ds_map_add(_seen, _oid, 1); _objN += 1; }
    }
    ds_map_destroy(_seen);
    ntl_console_log(ntl_t("st.objcount") + ": " + string(_objN));
    return 0;
}

if (_cmd == "inst")
{
    var _cnt = ds_map_create();
    var _n = instance_count;
    for (var _i = 0; _i < _n; _i += 1)
    {
        var _inst = instance_id_get(_i);
        if (!instance_exists(_inst)) continue;
        var _nm = object_get_name(_inst.object_index);
        if (string_length(_rest) > 0 && string_pos(string_lower(_rest), string_lower(_nm)) <= 0) continue;
        if (ds_map_exists(_cnt, _nm)) ds_map_replace(_cnt, _nm, ds_map_find_value(_cnt, _nm) + 1);
        else ds_map_add(_cnt, _nm, 1);
    }
    var _keys = ntl_dsmap_keys(_cnt);
    ntl_console_log(ntl_ts("st.inst_head", [string(array_length(_keys))]));
    var _shown = 0;
    for (var _i = 0; _i < array_length(_keys); _i += 1)
    {
        if (_shown >= _LIMIT) break;
        _shown += 1;
        ntl_console_log("  " + string(_keys[_i]) + " x " + string(ds_map_find_value(_cnt, _keys[_i])));
    }
    if (array_length(_keys) > _shown) ntl_console_log(ntl_ts("list.more", [string(array_length(_keys)), string(_shown), "inst"]));
    ds_map_destroy(_cnt);
    return 0;
}

if (_cmd == "objs")
{
    // ★ 反人类修复：以前完全忽略参数、且把全部对象一次性打完（几百行刷屏）。
    //   现在支持关键词过滤 + 显示上限 + "还有 N 条"。
    var _arr = ntl_inst_all_objs();
    var _kw = string_lower(string_trim(_rest));
    var _all = 0;
    var _shown = 0;
    var _body = "";
    for (var _i = 0; _i < array_length(_arr); _i += 1)
    {
        var _o = string(_arr[_i]);
        if (_kw != "" && string_pos(_kw, string_lower(_o)) <= 0) continue;
        _all += 1;
        if (_shown >= _LIMIT) continue;
        _shown += 1;
        _body += "  " + _o + chr(10);
    }
    if (_kw != "") ntl_console_log(ntl_ts("st.objs_head_kw", [string(_all), _rest]));
    else ntl_console_log(ntl_ts("st.objs_head", [string(_all)]));
    if (_shown > 0) ntl_console_log(string_delete(_body, string_length(_body), 1));
    if (_all > _shown) ntl_console_log(ntl_ts("list.more", [string(_all), string(_shown), "objs <关键词>"]));
    return 0;
}

if (_cmd == "flags")
{
    var _args = ntl_live_split_args(_rest);
    var _start = (array_length(_args) > 0) ? real(_args[0]) : 0;
    var _count = (array_length(_args) > 1) ? real(_args[1]) : 20;
    if (!variable_global_exists("flag")) { ntl_console_log(ntl_t("st.no_flag")); return 0; }
    if (_start < 0) _start = 0;
    if (_count <= 0) _count = 20;
    if (_count > 200) _count = 200;
    var _total = array_length(global.flag);
    var _end = min(_start + _count - 1, _total - 1);
    // ★ 反人类修复：以前只说 "global.flag[0..19]"，看不出总共有多少条、也不知道怎么翻页
    ntl_console_log(ntl_ts("st.flags_head", [string(_start), string(_end), string(_total)]));
    for (var _i = _start; _i <= _end; _i += 1)
        ntl_console_log("  [" + string(_i) + "] = " + string(global.flag[_i]));
    if (_end < _total - 1) ntl_console_log(ntl_ts("st.flags_next", [string(_end + 1)]));
    return 0;
}

if (_cmd == "player")
{
    if (!variable_global_exists("ntl_player")) { ntl_console_log(ntl_t("st.no_player")); return 0; }
    var _p = global.ntl_player;
    ntl_console_log(ntl_t("st.position") + ": (" + string(round(ds_map_find_value(_p, "x"))) + ", " + string(round(ds_map_find_value(_p, "y"))) + ")");
    ntl_console_log(ntl_t("st.size") + ": " + string(ds_map_find_value(_p, "w")) + " x " + string(ds_map_find_value(_p, "h")));
    ntl_console_log(ntl_t("st.facing") + ": " + string(ds_map_find_value(_p, "face")));
    return 0;
}

if (_cmd == "maps")
{
    var _dir = program_directory + "Neutraled/kristal-maps/";
    if (!directory_exists(_dir)) { ntl_console_log(ntl_t("st.no_mapdir")); return 0; }
    var _files = ntl_file_list(_dir, "*.map.json");
    var _kw2 = string_lower(string_trim(_rest));
    var _hit = [];
    for (var _i = 0; _i < array_length(_files); _i += 1)
    {
        var _nm = string_replace(string(_files[_i]), ".map.json", "");
        if (_kw2 != "" && string_pos(_kw2, string_lower(_nm)) <= 0) continue;
        array_push(_hit, _nm);
    }
    // ★ 反人类修复（2026-09-28 实测）：原来无论有没有关键词都只打「可用地图: 123」，
    //   关键词命中 0 条时整段输出就一行数字，看着像坏了。现在表头反映过滤结果、0 条时说明原因。
    var _tot = array_length(_files);
    if (_kw2 == "") ntl_console_log(ntl_ts("st.maps", [string(_tot)]));
    else if (array_length(_hit) == 0) ntl_console_log(ntl_ts("st.maps_none", [_rest, string(_tot)]));
    else ntl_console_log(ntl_ts("st.maps_kw", [_rest, string(array_length(_hit)), string(_tot)]));
    var _shown = min(_LIMIT, array_length(_hit));
    for (var _j2 = 0; _j2 < _shown; _j2 += 1) ntl_console_log("  " + _hit[_j2]);
    if (array_length(_hit) > _LIMIT) ntl_console_log(ntl_ts("list.more", [string(array_length(_hit)), string(_shown), "maps <关键词>"]));
    return 0;
}

if (_cmd == "saves")
{
    var _bak = program_directory + "Neutraled/save-backups/";
    if (!directory_exists(_bak)) { ntl_console_log(ntl_t("st.no_saves")); return 0; }
    var _zips = ntl_file_list(_bak, "*.zip");
    ntl_console_log(ntl_ts("st.saves", [string(array_length(_zips))]));
    var _shown = 0;
    // 备份是按时间追加的，最新的在最后 → 只显示最近 20 个（旧备份意义不大）
    var _from = max(0, array_length(_zips) - 20);
    for (var _i = _from; _i < array_length(_zips); _i += 1)
    {
        _shown += 1;
        ntl_console_log("  " + string(_zips[_i]));
    }
    if (array_length(_zips) > _shown) ntl_console_log(ntl_ts("st.saves_old", [string(array_length(_zips) - _shown)]));
    ntl_console_log(ntl_t("st.restore_hint"));
    return 0;
}

if (_cmd == "cache")
{
    var _ci = program_directory + "Neutraled/cache/index.json";
    if (!file_exists(_ci)) { ntl_console_log(ntl_t("st.no_cache")); return 0; }
    ntl_console_log(ntl_ts("st.cache", [_ci]));
    ntl_console_log(ntl_t("st.cache_hint"));
    return 0;
}

if (_cmd == "world")
{
    ntl_console_log(ntl_t("st.world_head"));
    // ★ 反人类修复：以前三项 global 都不存在时只打印一行标题就没了（看着像坏了）。
    //   现在没有任何上下文时明确说清楚，并给出下一步该做什么。
    var _any = 0;
    if (variable_global_exists("ntl_objects"))
    {
        _any = 1;
        ntl_console_log(ntl_ts("st.world_objs", [string(ds_list_size(global.ntl_objects))]));
    }
    if (variable_global_exists("ntl_test_map"))
    {
        _any = 1;
        var _mp = global.ntl_test_map;
        ntl_console_log(ntl_ts("st.world_map", [string(ds_map_find_value(_mp, "id"))]));
    }
    if (variable_global_exists("ntl_kristal_ready"))
    {
        _any = 1;
        ntl_console_log(ntl_t("st.world_ready"));
    }
    if (!_any) ntl_console_log(ntl_t("st.world_none"));
    return 0;
}

return 0;
