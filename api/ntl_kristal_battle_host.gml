/// ntl_kristal_battle_host(name, args) —— 战斗/对话桥接的宿主实现
var _name = string(argument[0]);
var _args = argument[1];
if (_args == undefined) _args = [];
var _n = array_length(_args);
var _a0 = (_n > 0) ? _args[0] : undefined;

switch (_name)
{
    case "__kr_battle_getEnemy":
    {
        var _want = string(_a0);
        var _b = ntl_lua_index_get(ntl_lua_table_get(global.ntl_lua_globals, "Game"), "battle");
        var _list = ntl_lua_index_get(_b, "enemies");
        if (_list == undefined || !is_real(_list)) return undefined;
        var _i = 1;
        while (1)
        {
            var _e = ntl_lua_index_get(_list, _i);
            if (_e == undefined) break;
            if (ntl_lua_index_get(_e, "name") == _want) return _e;
            _i += 1;
            if (_i > 50) break;
        }
        return undefined;
    }

    case "__kr_battle_addWave":
    {
        var _b2 = ntl_lua_index_get(ntl_lua_table_get(global.ntl_lua_globals, "Game"), "battle");
        var _waves = ntl_lua_index_get(_b2, "waves");
        if (_waves != undefined && is_real(_waves))
        {
            ntl_lua_table_set(_waves, ntl_lua_table_count(_waves) + 1, _a0);
        }
        ntl_log("kristal", "[battle] 加入一波敌人");
        return 1;
    }

    case "__kr_startEncounter":
    {
        ntl_log("kristal", "[battle] startEncounter（当前为兼容桩：记录请求，未真正切换战斗场景）");
        if (variable_global_exists("ntl_encounter_log"))
            array_push(global.ntl_encounter_log, _a0);
        else global.ntl_encounter_log = [_a0];
        return 1;
    }

    case "__kr_startCutscene":
    {
        // 直接调用传入的函数
        if (_a0 != undefined && ntl_lua_is_fn(_a0))
        {
            return ntl_lua_call(_a0, []);
        }
        ntl_log("kristal", "[cutscene] 开始过场");
        return 1;
    }

    case "__kr_world_say":
    {
        // 显示对话：写到控制台历史 + 屏幕提示
        var _who = (_n > 0) ? string(_args[0]) : "";
        var _what = (_n > 1) ? string(_args[1]) : "";
        var _line = (_who != "") ? (_who + ": " + _what) : _what;
        ntl_console_log(ntl_ts("btl.line", [_line]));
        ntl_log("kristal", "[say] " + _line);
        global.ntl_say_text = _line;
        global.ntl_say_frames = 300;    // 显示 5 秒
        return 1;
    }
}
return undefined;
