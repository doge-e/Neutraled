/// ntl_kristal_battle_init() —— Kristal 战斗/对话系统桥接
///
/// Kristal 的战斗 API：
///   Game.battle                     当前战斗
///   Game.battle:getEnemy(name)      取敌人
///   Game.battle.enemies             敌人列表
///   Game.battle:addWave(wave)       加一波
///   Game.battle:setState(state)
///   Game:startEncounter(encounter)  开始遭遇战
///   Game.world:startCutscene(fn)    开始过场
///
/// 对话 API：
///   Text(text, x, y)                创建文本对象
///   Game.world:say(...)             显示对话
///   Textbox                         文本框
if (!variable_global_exists("ntl_lua_globals")) return 0;
var _g = global.ntl_lua_globals;

// ---------- Game.battle ----------
var _game = ntl_lua_table_get(_g, "Game");
if (_game == undefined || !is_real(_game)) return 0;

var _battle = ntl_lua_table_new();
ntl_lua_table_set(_battle, "enemies", ntl_lua_table_new());
ntl_lua_table_set(_battle, "party", ntl_lua_table_new());
ntl_lua_table_set(_battle, "turn", 0);
ntl_lua_table_set(_battle, "state", "NONE");
ntl_lua_table_set(_battle, "waves", ntl_lua_table_new());

// getEnemy
var _ge = ntl_lua_table_new();
ntl_lua_table_set(_ge, "_ntlfn", 1);
ntl_lua_table_set(_ge, "_ntlctrl", "__krfn");
ntl_lua_table_set(_ge, "_krname", "__kr_battle_getEnemy");
ntl_lua_index_set(_battle, "getEnemy", _ge);

// addWave
var _aw = ntl_lua_table_new();
ntl_lua_table_set(_aw, "_ntlfn", 1);
ntl_lua_table_set(_aw, "_ntlctrl", "__krfn");
ntl_lua_table_set(_aw, "_krname", "__kr_battle_addWave");
ntl_lua_index_set(_battle, "addWave", _aw);

ntl_lua_index_set(_game, "battle", _battle);

// ---------- Game:startEncounter ----------
var _se = ntl_lua_table_new();
ntl_lua_table_set(_se, "_ntlfn", 1);
ntl_lua_table_set(_se, "_ntlctrl", "__krfn");
ntl_lua_table_set(_se, "_krname", "__kr_startEncounter");
ntl_lua_index_set(_game, "startEncounter", _se);

// ---------- Game.world:startCutscene ----------
var _world = ntl_lua_index_get(_game, "world");
if (_world != undefined && is_real(_world))
{
    var _sc = ntl_lua_table_new();
    ntl_lua_table_set(_sc, "_ntlfn", 1);
    ntl_lua_table_set(_sc, "_ntlctrl", "__krfn");
    ntl_lua_table_set(_sc, "_krname", "__kr_startCutscene");
    ntl_lua_index_set(_world, "startCutscene", _sc);

    // say：显示对话（用我们的控制台 + 屏幕文字）
    var _say = ntl_lua_table_new();
    ntl_lua_table_set(_say, "_ntlfn", 1);
    ntl_lua_table_set(_say, "_ntlctrl", "__krfn");
    ntl_lua_table_set(_say, "_krname", "__kr_world_say");
    ntl_lua_index_set(_world, "say", _say);
}

ntl_log("kristal", "战斗/对话系统已桥接（Game.battle / startEncounter / world:say / startCutscene）");
return 1;
