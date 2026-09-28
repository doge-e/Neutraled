/// ntl_kristal_host(name, args) —— Kristal 兼容层的宿主实现（__kr_* 分派）
var _name = string(argument[0]);
var _args = argument[1];
if (_args == undefined) _args = [];
var _argc = array_length(_args);
var _a0 = (_argc > 0) ? _args[0] : undefined;
var _a1 = (_argc > 1) ? _args[1] : undefined;

// Game.world 相关
if (string_copy(_name, 1, 11) == "__kr_world_") return ntl_kristal_world_host(_name, _args);
// 战斗/对话相关
if (string_copy(_name, 1, 12) == "__kr_battle_") return ntl_kristal_battle_host(_name, _args);
if (string_copy(_name, 1, 18) == "__kr_startEncounter" || string_copy(_name, 1, 15) == "__kr_startCuts")
    return ntl_kristal_battle_host(_name, _args);

switch (_name)
{
    case "__kr_noop":  return undefined;
    case "__kr_false": return false;
    case "__kr_true":  return true;

    case "__kr_log":
        ntl_log("kristal", ntl_lua_tostring(_a0));
        return undefined;

    case "__kr_mod":
        if (!variable_global_exists("ntl_lua_mod_dir")) return ntl_lua_table_new();
        var _m = ntl_lua_table_new();
        ntl_lua_table_set(_m, "id", global.ntl_lua_mod_dir);
        ntl_lua_table_set(_m, "path", global.ntl_lua_mod_dir);
        return _m;

    case "__kr_state": return "Game";

    // ---- flags（支持 命名 flag 与 原版数字 flag；自动跳过方法调用的 self）----
    case "__kr_get_flag":
    {
        var _key = ntl_kr_flag_key(_args);
        if (_key == undefined) return undefined;
        if (is_string(_key))
        {
            if (!variable_global_exists("ntl_kristal_flags")) global.ntl_kristal_flags = ds_map_create();
            if (ds_map_exists(global.ntl_kristal_flags, _key)) return ds_map_find_value(global.ntl_kristal_flags, _key);
            return false;
        }
        var _id = round(real(_key));
        if (_id < 0 || _id > 9999) return false;
        try { return global.flag[_id]; } catch (e) { return false; }
    }
    case "__kr_set_flag":
    {
        var _key2 = ntl_kr_flag_key(_args);
        var _val = ntl_kr_flag_value(_args);
        if (_key2 == undefined) return undefined;
        if (is_string(_key2))
        {
            if (!variable_global_exists("ntl_kristal_flags")) global.ntl_kristal_flags = ds_map_create();
            if (ds_map_exists(global.ntl_kristal_flags, _key2)) ds_map_replace(global.ntl_kristal_flags, _key2, _val);
            else ds_map_add(global.ntl_kristal_flags, _key2, _val);
            return undefined;
        }
        var _id2 = round(real(_key2));
        if (_id2 < 0 || _id2 > 9999) return undefined;
        try { global.flag[_id2] = _val; } catch (e) { ntl_log("kristal", "[ntl] ntl_kristal_host.gml:64 写 global.flag 失败: " + string(e)); }
        return undefined;
    }
    case "__kr_add_flag":
    {
        if (!variable_global_exists("ntl_kristal_flags")) global.ntl_kristal_flags = ds_map_create();
        var _nm = ntl_lua_tostring(ntl_kr_flag_key(_args));
        var _v2 = ntl_kr_flag_value(_args);
        if (_v2 == undefined) _v2 = 0;
        if (ds_map_exists(global.ntl_kristal_flags, _nm)) ds_map_replace(global.ntl_kristal_flags, _nm, _v2);
        else ds_map_add(global.ntl_kristal_flags, _nm, _v2);
        return undefined;
    }

    case "__kr_game_money":
        try { return global.money; } catch (e) { return 0; }

    // ---- 音频 ----
    case "__kr_play_sound":
    {
        var _n = ntl_lua_tostring(_a0);
        var _idx = asset_get_index(_n);
        if (_idx >= 0) { try { snd_play(_idx, 1, 1); } catch (e) { ntl_log("kristal", "[ntl] ntl_kristal_host.gml:86 snd_play 失败: " + string(e)); } }
        return undefined;
    }
    case "__kr_play_music":
    {
        var _n2 = ntl_lua_tostring(_a0);
        var _idx2 = asset_get_index(_n2);
        if (_idx2 >= 0) { try { mus_loop(_idx2); } catch (e) { ntl_log("kristal", "[ntl] ntl_kristal_host.gml:93 mus_loop 失败: " + string(e)); } }
        return undefined;
    }
    case "__kr_stop_music":
        try { audio_stop_all(); } catch (e) { ntl_log("kristal", "[ntl] ntl_kristal_host.gml:97 audio_stop_all 失败: " + string(e)); }
        return undefined;

    // ---- 资源（桥接到 GM 的资源索引）----
    case "__kr_asset":
    {
        var _n3 = ntl_lua_tostring(_a0);
        var _idx3 = asset_get_index(_n3);
        if (_idx3 < 0) return undefined;
        var _t = ntl_lua_table_new();
        ntl_lua_table_set(_t, "index", _idx3);
        ntl_lua_table_set(_t, "name", _n3);
        return _t;
    }
    case "__kr_asset_exists":
    {
        var _idx4 = asset_get_index(ntl_lua_tostring(_a0));
        return (_idx4 >= 0);
    }

    // ---- 兼容 API ----
    case "__kr_presence":
    {
        var _p = ntl_lua_table_new();
        ntl_lua_table_set(_p, "state", "Neutraled");
        ntl_lua_table_set(_p, "largeImageKey", "frlogo");
        ntl_lua_table_set(_p, "largeImageName", "");
        return _p;
    }
    case "__kr_add_flag":
    {
        if (!variable_global_exists("ntl_kristal_flags")) global.ntl_kristal_flags = ds_map_create();
        var _n = ntl_lua_tostring(_a0);
        var _v = (_argc > 1) ? _a1 : 0;
        if (ds_map_exists(global.ntl_kristal_flags, _n)) ds_map_replace(global.ntl_kristal_flags, _n, _v);
        else ds_map_add(global.ntl_kristal_flags, _n, _v);
        return undefined;
    }
    case "__kr_timer":
    {
        // 简易计时器对象：提供 after / tween / during 等常用方法（当前为立即执行的近似实现）
        var _t = ntl_lua_table_new();
        ntl_lua_table_set(_t, "after", ntl_lua_fn_host("__kr_timer_after"));
        ntl_lua_table_set(_t, "tween", ntl_lua_fn_host("__kr_noop"));
        ntl_lua_table_set(_t, "during", ntl_lua_fn_host("__kr_noop"));
        ntl_lua_table_set(_t, "script", ntl_lua_fn_host("__kr_noop"));
        ntl_lua_table_set(_t, "cancel", ntl_lua_fn_host("__kr_noop"));
        ntl_lua_table_set(_t, "clear", ntl_lua_fn_host("__kr_noop"));
        return _t;
    }
    case "__kr_timer_after":
    {
        // after(seconds, fn)：延迟执行（当前立即调用，满足初始化类用法）
        if (_argc > 1)
        {
            // 找到第一个函数参数（Kristal 有 after(delay, fn) 与 after(delay, obj, fn) 两种）
            for (var _i = 1; _i < _argc; _i += 1)
            {
                if (ntl_lua_is_fn(_args[_i]))
                {
                    global.ntl_lua_err = "";
                    ntl_lua_call(_args[_i], []);
                    if (global.ntl_lua_err != "")
                    {
                        ntl_log("kristal", "[timer.after] " + global.ntl_lua_err);
                        global.ntl_lua_err = "";
                    }
                    break;
                }
            }
        }
        return undefined;
    }

    // ---- Registry（暂返回空表；mod 数据后续按需接入）----
    case "__kr_reg_get": return undefined;
    case "__kr_reg_all": return ntl_lua_table_new();

    // ---- 输入 ----
    case "__kr_key_confirm": return keyboard_check(ord("Z"));
    case "__kr_key_cancel":  return keyboard_check(ord("X"));
    case "__kr_key_menu":    return keyboard_check(vk_escape);
}
return undefined;