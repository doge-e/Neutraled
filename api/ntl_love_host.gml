/// ntl_love_host(name, args) —— LOVE2D API 的宿主实现（由 ntl_lua_host 分派 __love_*）
var _name = string(argument[0]);
var _args = argument[1];
if (_args == undefined) _args = [];
var _argc = array_length(_args);
var _a0 = (_argc > 0) ? _args[0] : undefined;
var _a1 = (_argc > 1) ? _args[1] : undefined;
var _a2 = (_argc > 2) ? _args[2] : undefined;
var _a3 = (_argc > 3) ? _args[3] : undefined;
var _a4 = (_argc > 4) ? _args[4] : undefined;   // ★ lint 规则 16 抓到：__love_gfx_rectangle 用了 _a4 但从没声明

switch (_name)
{
    case "__love_noop": return undefined;

    // ---- graphics ----
    case "__love_gfx_setColor":
        draw_set_color(make_color_rgb(round(real(_a0) * 255), round(real(_a1) * 255), round(real(_a2) * 255)));
        if (_argc > 3) draw_set_alpha(real(_a3));
        return undefined;
    case "__love_gfx_rectangle":
    {
        var _mode = string(_a0);
        if (_mode == "fill") draw_rectangle(real(_a1), real(_a2), real(_a1) + real(_a3), real(_a2) + real(_a4), false);
        else draw_rectangle(real(_a1), real(_a2), real(_a1) + real(_a3), real(_a2) + real(_a4), true);
        return undefined;
    }
    case "__love_gfx_circle":
    {
        var _mode2 = string(_a0);
        draw_circle(real(_a1), real(_a2), real(_a3), (_mode2 != "fill") ? true : false);
        return undefined;
    }
    case "__love_gfx_line":
        draw_line(real(_a0), real(_a1), real(_a2), real(_a3));
        return undefined;
    case "__love_gfx_print":
        draw_set_color(c_white);
        if (_argc >= 3) draw_text(real(_a1), real(_a2), ntl_lua_tostring(_a0));
        else draw_text(0, 0, ntl_lua_tostring(_a0));
        return undefined;
    case "__love_gfx_draw":
        return undefined;   // 绘制图像资源需要额外映射
    case "__love_gfx_width":  return room_width;
    case "__love_gfx_height": return room_height;

    // ---- timer ----
    case "__love_timer_getTime":  return current_time / 1000;
    case "__love_timer_getDelta": return delta_time / 1000000;
    case "__love_timer_getFPS":   return 1 / max(delta_time / 1000000, 0.0001);

    // ---- audio ----
    case "__love_audio_setVolume":
        if (ntl_lua_tostring(_a0) != "") { try { audio_master_gain(real(_a0)); } catch (e) { ntl_log("love", "[ntl] ntl_love_host.gml:53 audio_master_gain 失败: " + string(e)); } }
        return undefined;

    // ---- filesystem ----
    case "__love_fs_read":
    {
        var _p = ntl_lua_tostring(_a0);
        if (!file_exists(_p)) return undefined;
        return ntl_live_file_read(_p);
    }
    case "__love_fs_write":
    {
        var _p2 = ntl_lua_tostring(_a0);
        var _c = ntl_lua_tostring(_a1);
// 确保目录存在（GM 不会自动建目录，file_text_open_write 会静默失败）
// 仅当路径含分隔符时才建目录：ntl_ensure_dir 对裸文件名会建出同名目录，反而让写入失败
if (string_pos("/", _p2) > 0 || string_pos(chr(92), _p2) > 0) ntl_ensure_dir(_p2);
        var _f = file_text_open_write(_p2);
        file_text_write_string(_f, _c);
        file_text_close(_f);
        return true;
    }
    case "__love_fs_exists": return file_exists(ntl_lua_tostring(_a0));
    case "__love_fs_getInfo":
    {
        var _p3 = ntl_lua_tostring(_a0);
        if (!file_exists(_p3)) return undefined;
        var _t = ntl_lua_table_new();
        ntl_lua_table_set(_t, "type", "file");
        ntl_lua_table_set(_t, "size", file_size(_p3));
        return _t;
    }
    case "__love_fs_list":
    {
        var _d = ntl_lua_tostring(_a0);
        var _out = ntl_lua_table_new();
        var _idx = 0;
        var _fn = file_find_first(_d + "/*", fa_directory);
        while (_fn != "")
        {
            if (_fn != "." && _fn != "..") { _idx += 1; ntl_lua_table_set(_out, _idx, _fn); }
            _fn = file_find_next();
        }
        file_find_close();
        var _ff = file_find_first(_d + "/*", 0);
        while (_ff != "")
        {
            _idx += 1; ntl_lua_table_set(_out, _idx, _ff);
            _ff = file_find_next();
        }
        file_find_close();
        return _out;
    }

    // ---- window / input ----
    case "__love_window_mode":
    {
        var _m = ntl_lua_table_new();
        ntl_lua_table_set(_m, "width", window_get_width());
        ntl_lua_table_set(_m, "height", window_get_height());
        ntl_lua_table_set(_m, "fullscreen", window_get_fullscreen());
        return _m;
    }
    case "__love_kb_isDown":
    {
        var _k = string_lower(ntl_lua_tostring(_a0));
        var _map = ds_map_create();
        ds_map_add(_map, "left", vk_left); ds_map_add(_map, "right", vk_right);
        ds_map_add(_map, "up", vk_up); ds_map_add(_map, "down", vk_down);
        ds_map_add(_map, "z", ord("Z")); ds_map_add(_map, "x", ord("X"));
        ds_map_add(_map, "c", ord("C")); ds_map_add(_map, "return", vk_enter);
        ds_map_add(_map, "escape", vk_escape); ds_map_add(_map, "space", vk_space);
        ds_map_add(_map, "shift", vk_shift); ds_map_add(_map, "lshift", vk_lshift);
        var _vk = ds_map_find_value(_map, _k);
        ds_map_destroy(_map);
        if (_vk == undefined) return false;
        return keyboard_check(_vk);
    }
    case "__love_mouse_x": return device_mouse_x_to_gui(0);
    case "__love_mouse_y": return device_mouse_y_to_gui(0);
    case "__love_mouse_pos":
    {
        var _p4 = ntl_lua_table_new();
        ntl_lua_table_set(_p4, 1, device_mouse_x_to_gui(0));
        ntl_lua_table_set(_p4, 2, device_mouse_y_to_gui(0));
        return _p4;
    }
}
return undefined;
