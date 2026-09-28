/// ntl_call_host —— 老式脚本（每个函数一个同名脚本资源）
var _name = argument[0];
var _args = argument[1];
if (_args == undefined) _args = [];
var _n = array_length(_args);
// 位置参数简写（供下面的分派使用）
var _a0 = (_n > 0) ? _args[0] : undefined;
var _a1 = (_n > 1) ? _args[1] : undefined;
var _a2 = (_n > 2) ? _args[2] : undefined;

    // ---- coroutine 库（必须在 __lua_ 之前，否则会被标准库分支拦掉）----
    if (string_copy(_name, 1, 5) == "__co_")
    {
        var _coArgs = [string_delete(_name, 1, 5)];
        for (var _ci2 = 0; _ci2 < _n; _ci2 += 1) array_push(_coArgs, _args[_ci2]);
        return ntl_lua_coroutine_call(_coArgs);
    }
    // ---- load 出来的代码块（名字形如 __loaded_N）----
    if (string_copy(_name, 1, 9) == "__loaded_")
        return ntl_lua_chunk_run(_name, _args);
    // ---- load / loadstring ----
    if (_name == "__lua_load" || _name == "__lua_loadstring" || _name == "load" || _name == "loadstring")
        return ntl_lua_load((_n > 0) ? string(_a0) : "", (_n > 1) ? string(_a1) : "=(load)");
    // ---- debug 库 ----
    if (string_copy(_name, 1, 6) == "__dbg_")
        return ntl_lua_debug(string_delete(_name, 1, 6), (_n > 0) ? _a0 : undefined, (_n > 1) ? _a1 : undefined);
    // ---- love.event ----
    if (string_copy(_name, 1, 10) == "__loveev_")
        return ntl_love_event(string_delete(_name, 1, 10), (_n > 0) ? _a0 : undefined, (_n > 1) ? _a1 : undefined);
    // ---- Lua 标准库分派 ----
    if (string_copy(_name, 1, 6) == "__lua_") return ntl_lua_host(_name, _args);
    // ---- coroutine 库 ----
    if (string_copy(_name, 1, 3) == "co_")
        return ntl_lua_coroutine(string_delete(_name, 1, 3), (_n > 0) ? _a0 : "", (_n > 1) ? _a1 : undefined, (_n > 2) ? _a2 : undefined);
    // ---- LOVE2D 桥接分派 ----
    if (string_copy(_name, 1, 7) == "__love_") return ntl_love_host(_name, _args);
    // ---- Kristal 兼容层分派 ----
    if (string_copy(_name, 1, 5) == "__kr_")
    {
        // 类系统相关走单独的宿主
        var _clsFns = ["__kr_Class", "__kr_new_instance", "__kr_super_init", "__kr_class_extend"];
        for (var _ci = 0; _ci < array_length(_clsFns); _ci += 1)
            if (_name == _clsFns[_ci]) return ntl_kristal_class_host(_name, _args);
        return ntl_kristal_host(_name, _args);
    }
    // ---- 宿主前缀（Lua 里未定义的全局名回退到此）----
    if (string_copy(_name, 1, 7) == "__host:")
    {
        var _real = string_delete(_name, 1, 7);
        // Lua 标准库函数（注册名可能带或不带 __lua_ 前缀，这里统一映射）
        var _bare = _real;
        if (string_copy(_bare, 1, 6) == "__lua_") _bare = string_delete(_bare, 1, 6);
        if (ntl_lua_is_stdlib(_bare)) return ntl_lua_host("__lua_" + _bare, _args);
        return ntl_call_host(_real, _args);
    }

    // ---- 颜色常量（Lua 里写 c_red / c_white 等）----
    if (_name == "c_red") return c_red;
    if (_name == "c_white") return c_white;
    if (_name == "c_black") return c_black;
    if (_name == "c_yellow") return c_yellow;
    if (_name == "c_green") return c_green;
    if (_name == "c_blue") return c_blue;
    if (_name == "c_aqua") return c_aqua;
    if (_name == "c_gray") return c_gray;
    if (_name == "c_orange") return c_orange;
    if (_name == "c_purple") return c_purple;

    // ---- 命名空间调用（依赖 mod 的接口：ns.func）----
    var _nsr = ntl_call_ns(_name, _args);
    if (ds_map_find_value(_nsr, "found") == 1)
    {
        var _nsv = ds_map_find_value(_nsr, "value");
        ds_map_destroy(_nsr);
        return _nsv;
    }
    ds_map_destroy(_nsr);

    // ---- 解释器内置 ----
    if (_name == "log") { ntl_log("live", string(_args[0])); return 0; }

    // ---- Lua 模块加载（供 NTL Script 调用）----
    if (_name == "require_lua" || _name == "lua_require")
    {
        var _r = ntl_require_lua((_n > 0) ? string(_args[0]) : "");
        var _okv = ds_map_find_value(_r, "ok");
        var _val = ds_map_find_value(_r, "value");
        ds_map_destroy(_r);
        if (_okv == 1) return _val;
        return undefined;
    }

    // ---- Kristal 对象实例化 ----
    if (_name == "obj_spawn" || _name == "ntl_obj_spawn")
        return ntl_obj_spawn((_n > 0) ? string(_args[0]) : "", (_n > 1) ? _args[1] : 0, (_n > 2) ? _args[2] : 0);
    if (_name == "obj_count" || _name == "ntl_obj_count_now") return (variable_global_exists("ntl_obj_count") ? global.ntl_obj_count : 0);

    // ---- 地图批量验证 ----
    if (_name == "map_verify_all" || _name == "ntl_map_verify_all") return ntl_map_verify_all();

    // ---- 控制台命令系统 ----
    if (_name == "console_register_mod" || _name == "ntl_console_register_mod")
    {
        // 显式边界检查（避免越界读取 _args[i]）
        return ntl_console_register_mod(string(_a0), string(_a1), (_n > 2) ? string(_a2) : "", (_n > 3) ? string(_args[3]) : "");
    }
    if (_name == "console_exec" || _name == "ntl_console_exec")
        return ntl_console_exec((_n > 0) ? _a0 : "");
    if (_name == "console_run" || _name == "ntl_console_run")
        return ntl_console_run((_n > 0) ? _a0 : "");
    if (_name == "console_log" || _name == "ntl_console_log")
    { ntl_console_log((_n > 0) ? string(_a0) : ""); return 1; }

    // ---- mod 互操作（mod 之间任意联动）----
    if (_name == "mod_require" || _name == "ntl_mod_require") return ntl_mod_require((_n > 0) ? _a0 : "");
if (_name == "mod_import" || _name == "ntl_mod_import") return ntl_mod_import((_n > 0) ? _a0 : "", (_n > 1) ? _a1 : undefined);
if (_name == "hook_summary" || _name == "ntl_hook_summary") return ntl_hook_summary();
if (_name == "hook_plan" || _name == "ntl_hook_plan") return ntl_hook_plan((_n > 0) ? _a0 : "");
    if (_name == "mod_export" || _name == "ntl_mod_export")
        return ntl_mod_export((_n > 0) ? _a0 : "", (_n > 1) ? _a1 : undefined);
    if (_name == "mod_emit" || _name == "ntl_mod_emit")
        return ntl_mod_emit((_n > 0) ? _a0 : "", (_n > 1) ? _a1 : undefined);
    if (_name == "mod_on" || _name == "ntl_mod_on") return ntl_mod_on((_n > 0) ? _a0 : "", (_n > 1) ? _a1 : undefined);
    if (_name == "asset_export" || _name == "ntl_asset_export") return ntl_asset_export((_n > 0) ? _a0 : "", (_n > 1) ? _a1 : "", (_n > 2) ? _a2 : undefined);
if (_name == "asset_import" || _name == "ntl_asset_import") return ntl_asset_import((_n > 0) ? _a0 : "", (_n > 1) ? _a1 : "", (_n > 2) ? _a2 : "");
if (_name == "asset_list" || _name == "ntl_asset_list") return ntl_asset_list();
if (_name == "mod_ids" || _name == "ntl_mod_ids") return ntl_mod_ids();
if (_name == "mod_list" || _name == "ntl_mod_list") return ntl_mod_list();
    if (_name == "shared_set" || _name == "ntl_shared_set")
        return ntl_shared_set((_n > 0) ? _a0 : "", (_n > 1) ? _a1 : undefined);
    if (_name == "shared_get" || _name == "ntl_shared_get") return ntl_shared_get((_n > 0) ? _a0 : "");
if (_name == "mod_data_set" || _name == "ntl_mod_data_set") return ntl_mod_data_set((_n > 0) ? _a0 : "", (_n > 1) ? _a1 : undefined);
if (_name == "mod_data_get" || _name == "ntl_mod_data_get") return ntl_mod_data_get((_n > 0) ? _a0 : "");
if (_name == "mod_data_save" || _name == "ntl_mod_data_save") return ntl_mod_data_save();
    // ---- 文本覆盖（mod 改界面文字）★ F5 接线 ----
    if (_name == "set_lang_string" || _name == "ntl_set_lang_string")
        return ntl_set_lang_string((_n > 0) ? _a0 : "", (_n > 1) ? _a1 : "");
    if (_name == "get_lang_string" || _name == "ntl_get_lang_string")
        return ntl_get_lang_string((_n > 0) ? _a0 : "");
    // ---- mod 自检声明的 API 版本 ★ F5 接线 ----
    if (_name == "api_version_check" || _name == "ntl_api_version_check")
        return ntl_api_version_check((_n > 0) ? _a0 : "");
    if (_name == "mod_save" || _name == "ntl_mod_save") return ntl_mod_save((_n > 0) ? _a0 : "", (_n > 1) ? _a1 : "auto");
    if (_name == "mod_load" || _name == "ntl_mod_load") return ntl_mod_load((_n > 0) ? _a0 : "", (_n > 1) ? _a1 : "auto");

    // ---- 运行时实例操作（完全支配）----
    if (_name == "inst_create" || _name == "ntl_inst_create")
        return ntl_inst_create((_n > 0) ? _a0 : "", (_n > 1) ? _a1 : 0, (_n > 2) ? _a2 : 0, (_n > 3) ? _args[3] : 0);
    if (_name == "inst_destroy" || _name == "ntl_inst_destroy") return ntl_inst_destroy(_a0);
    if (_name == "inst_set" || _name == "ntl_inst_set")
        return ntl_inst_set(_a0, (_n > 1) ? _a1 : "", (_n > 2) ? _a2 : undefined);
    if (_name == "inst_get" || _name == "ntl_inst_get") return ntl_inst_get(_a0, (_n > 1) ? _a1 : "");
    if (_name == "inst_find" || _name == "ntl_inst_find") return ntl_inst_find(_a0);
    if (_name == "inst_all_objs" || _name == "ntl_inst_all_objs") return ntl_inst_all_objs();

    // ---- 资源热替换 ----
    if (_name == "sprite_replace" || _name == "ntl_sprite_replace")
        return ntl_sprite_replace((_n > 0) ? _a0 : "", (_n > 1) ? _a1 : "");
    if (_name == "sprite_from_file" || _name == "ntl_sprite_from_file")
        return ntl_sprite_from_file((_n > 0) ? _a0 : "");
    // ★ F5 接线：查询/注销替换过的资源
    if (_name == "sprite_resolve" || _name == "ntl_sprite_resolve")
        return ntl_sprite_resolve((_n > 0) ? _a0 : -1);
    if (_name == "res_report" || _name == "ntl_res_report") return ntl_res_report();
    if (_name == "res_untrack" || _name == "ntl_res_untrack")
        return ntl_res_untrack((_n > 0) ? _a0 : "", (_n > 1) ? _a1 : -1);

    // ---- 主循环接管 ----
    if (_name == "loop_hook" || _name == "ntl_loop_hook")
        return ntl_loop_hook((_n > 0) ? _a0 : "", (_n > 1) ? _a1 : "");

    // ---- 错误中文化 ----
    if (_name == "err_friendly" || _name == "ntl_err_friendly")
        return ntl_err_friendly((_n > 0) ? _args[0] : "", (_n > 1) ? _args[1] : "", (_n > 2) ? _args[2] : -1);

    // ---- 性能测试辅助 ----
    if (_name == "perf_time") return current_time;
    if (_name == "ntl_perf_time") return current_time;

    // ---- Neutraled 地图/玩家 API（live 脚本可直接调用）----
    if (_name == "ntl_map_load")   return ntl_map_load(_n > 0 ? string(_args[0]) : "before_palace");
    if (_name == "ntl_map_test")   return ntl_map_test();
    if (_name == "ntl_player_test") return ntl_player_test(_n > 0 ? string(_args[0]) : "before_palace");
    if (_name == "nl_player_test") return ntl_player_test(_n > 0 ? string(_args[0]) : "before_palace");
    if (_name == "ntl_map_draw")
    {
        if (_n >= 1) ntl_map_draw(_args[0], (_n > 1) ? _args[1] : 0, (_n > 2) ? _args[2] : 0);
        return 0;
    }

    // ---- 常用绘图函数（无需前缀即可在 live 脚本中使用）----
    if (_name == "draw_text") { draw_text(real(_args[0]), real(_args[1]), string(_args[2])); return 0; }
    if (_name == "draw_set_color")
    {
        // ⚠ 参数里的 Lua 常量是 "__host:c_black" 这种字符串（Lua 侧把未定义全局名回退成占位符），
        //   这里必须再解析一层，否则 real("__host:c_black") 直接报
        //   "unable to convert string ... to number" —— 而且是**每帧**报（Draw 事件里），
        //   日志被刷爆、游戏卡到章节选择器都建不出来。实测踩过。
        var _col = _args[0];
        if (is_string(_col) && string_copy(_col, 1, 7) == "__host:")
            _col = ntl_call_host(string_delete(_col, 1, 7), []);
        try { draw_set_color(real(_col)); } catch (e) { ntl_log("host", "[ntl] draw_set_color 捕获: " + string(e)); }
        return 0;
    }
    if (_name == "draw_set_alpha") { try { draw_set_alpha(real(_args[0])); } catch (e) { ntl_log("host", "[ntl] ntl_call_host.gml:180 draw_set_alpha 捕获: " + string(e)); } return 0; }
    if (_name == "draw_set_font") { try { draw_set_font(real(_args[0])); } catch (e) { ntl_log("host", "[ntl] ntl_call_host.gml:181 draw_set_font 捕获: " + string(e)); } return 0; }
    if (_name == "draw_rectangle") { draw_rectangle(real(_args[0]), real(_args[1]), real(_args[2]), real(_args[3]), (_n > 4) ? _args[4] : false); return 0; }
    if (_name == "draw_circle") { draw_circle(real(_args[0]), real(_args[1]), real(_args[2]), (_n > 3) ? _args[3] : false); return 0; }
    if (_name == "draw_line") { draw_line(real(_args[0]), real(_args[1]), real(_args[2]), real(_args[3])); return 0; }
    if (_name == "draw_sprite") { draw_sprite(real(_args[0]), real(_args[1]), real(_args[2]), real(_args[3])); return 0; }
    if (_name == "room_width_v") { return room_width; }
    if (_name == "room_height_v") { return room_height; }
    if (_name == "get_global") { var _g = string(_args[0]); if (!variable_global_exists(_g)) return undefined; return variable_global_get(_g); }
    if (_name == "set_global")
    {
        variable_global_set(string(_args[0]), _args[1]);
        return 1;
    }
    if (_name == "inst_count") { var _o = asset_get_index(string(_args[0])); if (_o < 0) return -1; return instance_number(_o); }
    if (_name == "inst_first") { var _o2 = asset_get_index(string(_args[0])); if (_o2 < 0) return noone; return instance_find(_o2, 0); }
    if (_name == "var_get") { return variable_instance_get(_args[0], string(_args[1])); }
    if (_name == "var_set") { variable_instance_set(_args[0], string(_args[1]), _args[2]); return 1; }
    if (_name == "file_read")
    {
        var _p = string(_args[0]);
        if (!file_exists(_p)) { ntl_log("live", "[错误] 文件不存在: " + _p); return ""; }
        var _f = file_text_open_read(_p);
        var _out = "";
        while (!file_text_eof(_f))
        {
            _out += file_text_read_string(_f);
            file_text_readln(_f);
            if (!file_text_eof(_f)) _out += chr(10);
        }
        file_text_close(_f);
        return _out;
    }
    if (_name == "file_write")
    {
        var _fp2 = string(_args[0]);
        // 仅当路径含分隔符时才建目录（裸文件名走 GM 当前目录，建目录会反过来让写入失败）
        if (string_pos("/", _fp2) > 0 || string_pos(chr(92), _fp2) > 0) ntl_ensure_dir(_fp2);
        var _f2 = file_text_open_write(_fp2);
        file_text_write_string(_f2, string(_args[1]));
        file_text_close(_f2);
        return 1;
    }
    if (_name == "json_parse_safe")
    {
        try { return json_parse(string(_args[0])); } catch (e) { ntl_log("live", "[错误] JSON 解析失败"); return undefined; }
    }
    if (_name == "len") { if (is_array(_args[0])) return array_length(_args[0]); if (is_string(_args[0])) return string_length(_args[0]); return 0; }
    if (_name == "floor") return floor(real(_args[0]));
    if (_name == "ceil") return ceil(real(_args[0]));
    if (_name == "abs") return abs(real(_args[0]));
    if (_name == "min") return min(real(_args[0]), real(_args[1]));
    if (_name == "max") return max(real(_args[0]), real(_args[1]));
    if (_name == "random") return random(real(_args[0]));
    if (_name == "irandom") return irandom(real(_args[0]));
    if (_name == "string") return string(_args[0]);
    if (_name == "real") return real(_args[0]);
    if (_name == "sleep_frames") { return 0; }   // 占位（脚本不阻塞）

    // ---- 数组（arr_*）----
    if (_name == "arr_new") return [];
    if (_name == "arr_push") { var _a = _args[0]; array_push(_a, _args[1]); return array_length(_a); }
    if (_name == "arr_get") return _args[0][real(_args[1])];
    if (_name == "arr_set") { var _arrTmp = _args[0]; _arrTmp[real(_args[1])] = _args[2]; return 1; }
    if (_name == "arr_len") return array_length(_args[0]);
    if (_name == "arr_pop") { var _a3 = _args[0]; var _v3 = array_pop(_a3); return _v3; }
    if (_name == "arr_join") { var _a4 = _args[0]; var _s4 = ""; for (var _i4 = 0; _i4 < array_length(_a4); _i4 += 1) { if (_i4 > 0) _s4 += string(_args[1]); _s4 += string(_a4[_i4]); } return _s4; }

    // ---- 字典（map_*）----
    if (_name == "map_new") return ds_map_create();
    if (_name == "map_set")
    {
        var _m1 = _args[0];
        var _k1 = string(_args[1]);
        if (ds_map_exists(_m1, _k1)) ds_map_replace(_m1, _k1, _args[2]);
        else ds_map_add(_m1, _k1, _args[2]);
        return 1;
    }
    if (_name == "map_get") { var _m2 = _args[0]; var _k2 = string(_args[1]); if (!ds_map_exists(_m2, _k2)) return undefined; return ds_map_find_value(_m2, _k2); }
    if (_name == "map_has") return ds_map_exists(_args[0], string(_args[1]));
    if (_name == "map_del") { ds_map_delete(_args[0], string(_args[1])); return 1; }
    if (_name == "map_keys")
    {
        var _m3 = _args[0];
        var _out = [];
        var _ks = ntl_dsmap_keys(_m3);
        for (var _i5 = 0; _i5 < array_length(_ks); _i5 += 1) _out[_i5] = _ks[_i5];
        return _out;
    }

    // ---- 字符串（str_*）----
    if (_name == "str_len") return string_length(string(_args[0]));
    if (_name == "str_sub") return string_copy(string(_args[0]), real(_args[1]), real(_args[2]));
    if (_name == "str_find") return string_pos(string(_args[1]), string(_args[0]));
    if (_name == "str_upper") return string_upper(string(_args[0]));
    if (_name == "str_lower") return string_lower(string(_args[0]));
    if (_name == "str_char") return string_char_at(string(_args[0]), real(_args[1]));
    if (_name == "str_split") return string_split(string(_args[0]), string(_args[1]));
    if (_name == "str_contains") return (string_pos(string(_args[1]), string(_args[0])) > 0) ? 1 : 0;

    // ---- 数学 ----
    if (_name == "sqrt") return sqrt(real(_args[0]));
    if (_name == "power") return power(real(_args[0]), real(_args[1]));
    if (_name == "round") return round(real(_args[0]));
    if (_name == "sign") return sign(real(_args[0]));
    if (_name == "clamp") return clamp(real(_args[0]), real(_args[1]), real(_args[2]));
    if (_name == "lerp") return lerp(real(_args[0]), real(_args[1]), real(_args[2]));

    // ---- 实例遍历 ----
    if (_name == "inst_all")
    {
        var _obj = asset_get_index(string(_args[0]));
        if (_obj < 0) return [];
        var _cnt = instance_number(_obj);
        var _list = [];
        for (var _ii = 0; _ii < _cnt; _ii += 1) _list[_ii] = instance_find(_obj, _ii);
        return _list;
    }
    if (_name == "inst_nth") { var _ob = asset_get_index(string(_args[0])); if (_ob < 0) return noone; return instance_find(_ob, real(_args[1])); }
    if (_name == "inst_exists") return (instance_exists(_args[0])) ? 1 : 0;

    // ---- 调试 ----
    if (_name == "debug_on") { global.ntl_live_debug = 1; return 1; }
    if (_name == "debug_off") { global.ntl_live_debug = 0; return 1; }
    if (_name == "version") return global.ntl_version + "/live" + global.ntl_live_api;
    // ---- 开场跳过 / 名字查询（★ F5 接线：以前 mod 完全够不到）----
    if (_name == "skip_intro" || _name == "ntl_skip_intro") return ntl_skip_intro();
    if (_name == "whatis" || _name == "ntl_console_help_lookup")
        return ntl_console_help_lookup((_n > 0) ? _a0 : "");

    // ---- 动态调用游戏 / Neutraled 函数 ----
    var _idx = asset_get_index(_name);
    if (_idx < 0)
    {
        // ★ 专用标记：调用方（eval/exec）可以据此给出「未知函数」的友好提示；
        //   故意不走 ntl_lua_rt_err —— 那会改动 live/hook/mod 各路径的错误语义。
        global.ntl_lua_unknown = _name;
        ntl_log("live", "[错误] 未知函数: " + _name);
        return undefined;
    }
    switch (_n)
    {
        case 0: return script_execute(_idx);
        case 1: return script_execute(_idx, _args[0]);
        case 2: return script_execute(_idx, _args[0], _args[1]);
        case 3: return script_execute(_idx, _args[0], _args[1], _args[2]);
        case 4: return script_execute(_idx, _args[0], _args[1], _args[2], _args[3]);
        case 5: return script_execute(_idx, _args[0], _args[1], _args[2], _args[3], _args[4]);
        case 6: return script_execute(_idx, _args[0], _args[1], _args[2], _args[3], _args[4], _args[5]);
        case 7: return script_execute(_idx, _args[0], _args[1], _args[2], _args[3], _args[4], _args[5], _args[6]);
        case 8: return script_execute(_idx, _args[0], _args[1], _args[2], _args[3], _args[4], _args[5], _args[6], _args[7]);
        default:
            ntl_log("live", "[错误] 参数过多: " + _name + " (" + string(_n) + ")");
            // ★ 通用兜底：名字是真实 GML 脚本名 → 直接调用
//   这条路径让 mod 里 ntl.xxx() / 任何 ntl_* 函数都能被 Lua 调用，
//   不需要在 call_host 里逐个登记（ntl_perf_time 就因为这漏了）
var _siFall = asset_get_index("gml_Script_" + _name);
if (_siFall != -1)
{
    if (_n == 0) return script_execute(_siFall);
    if (_n == 1) return script_execute(_siFall, _args[0]);
    if (_n == 2) return script_execute(_siFall, _args[0], _args[1]);
    if (_n == 3) return script_execute(_siFall, _args[0], _args[1], _args[2]);
    if (_n == 4) return script_execute(_siFall, _args[0], _args[1], _args[2], _args[3]);
    if (_n == 5) return script_execute(_siFall, _args[0], _args[1], _args[2], _args[3], _args[4]);
    if (_n == 6) return script_execute(_siFall, _args[0], _args[1], _args[2], _args[3], _args[4], _args[5]);
    if (_n == 7) return script_execute(_siFall, _args[0], _args[1], _args[2], _args[3], _args[4], _args[5], _args[6]);
}

return undefined;
    }
