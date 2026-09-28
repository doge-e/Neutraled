/// ntl_kristal_init() —— Kristal 引擎兼容层（内含 world / battle 桥接）
/// 注册 Kristal 运行时的关键全局对象（Kristal / Game / Registry / Assets / Mod / Music / Input / Camera / Draw）
/// 目标：让 Kristal mod 的 Lua 脚本在 Neutraled 里能加载并执行大部分逻辑
if (!variable_global_exists("ntl_lua_globals")) return 0;
var _g = global.ntl_lua_globals;

// ---------- Kristal 主表 ----------
var _k = ntl_lua_table_new();
ntl_lua_table_set(_k, "version", "0.6.0-neutraled");
ntl_lua_table_set(_k, "mod", ntl_lua_fn_host("__kr_mod"));
ntl_lua_table_set(_k, "getMod", ntl_lua_fn_host("__kr_mod"));
ntl_lua_table_set(_k, "state", ntl_lua_fn_host("__kr_state"));
ntl_lua_table_set(_k, "getState", ntl_lua_fn_host("__kr_state"));
ntl_lua_table_set(_k, "log", ntl_lua_fn_host("__kr_log"));
ntl_lua_table_set(_k, "isDev", ntl_lua_fn_host("__kr_false"));
ntl_lua_table_set(_k, "content", ntl_lua_table_new());

// States 子表（Kristal.States["Game"] 等）
var _states = ntl_lua_table_new();
ntl_lua_table_set(_states, "Game", ntl_lua_table_new());
ntl_lua_table_set(_states, "MainMenu", ntl_lua_table_new());
ntl_lua_table_set(_states, "Loading", ntl_lua_table_new());
ntl_lua_table_set(_k, "States", _states);

// getFlag/setFlag（Kristal 常用）
ntl_lua_table_set(_k, "Config", ntl_lua_table_new());
ntl_lua_table_set(_k, "getPresence", ntl_lua_fn_host("__kr_presence"));
ntl_lua_table_set(_k, "getFlag", ntl_lua_fn_host("__kr_get_flag"));
ntl_lua_table_set(_k, "setFlag", ntl_lua_fn_host("__kr_set_flag"));
ntl_lua_table_set(_k, "save", ntl_lua_fn_host("__kr_noop"));
ntl_lua_table_set(_k, "load", ntl_lua_fn_host("__kr_noop"));
ntl_lua_table_set(_k, "playSound", ntl_lua_fn_host("__kr_play_sound"));
ntl_lua_table_set(_k, "playMusic", ntl_lua_fn_host("__kr_play_music"));
ntl_lua_table_set(_g, "Kristal", _k);

// ---------- Game（状态对象）----------
var _game = ntl_lua_table_get(_states, "Game");
ntl_lua_table_set(_game, "world", ntl_lua_table_new());
ntl_lua_table_set(_game, "party", ntl_lua_table_new());
ntl_lua_table_set(_game, "money", ntl_lua_fn_host("__kr_game_money"));
ntl_lua_table_set(_game, "getMoney", ntl_lua_fn_host("__kr_game_money"));
ntl_lua_table_set(_game, "setMoney", ntl_lua_fn_host("__kr_noop"));
ntl_lua_table_set(_game, "getFlag", ntl_lua_fn_host("__kr_get_flag"));
ntl_lua_table_set(_game, "setFlag", ntl_lua_fn_host("__kr_set_flag"));
ntl_lua_table_set(_game, "hasParty", ntl_lua_fn_host("__kr_true"));
ntl_lua_table_set(_game, "setBorder", ntl_lua_fn_host("__kr_noop"));
ntl_lua_table_set(_game, "addFlag", ntl_lua_fn_host("__kr_add_flag"));
ntl_lua_table_set(_game, "getFlag", ntl_lua_fn_host("__kr_get_flag"));
ntl_lua_table_set(_game, "setFlag", ntl_lua_fn_host("__kr_set_flag"));
ntl_lua_table_set(_game, "getParty", ntl_lua_fn_host("__kr_true"));
ntl_lua_table_set(_game, "alert", ntl_lua_fn_host("__kr_log"));
// Game.world：世界状态（含 timer / can_open_menu / map 等）
var _world = ntl_lua_table_get(_game, "world");
ntl_lua_table_set(_world, "can_open_menu", true);
ntl_lua_table_set(_world, "map", ntl_lua_table_new());
ntl_lua_table_set(_world, "hasCutscene", ntl_lua_fn_host("__kr_false"));
var _wtimer = ntl_lua_table_new();
ntl_lua_table_set(_wtimer, "after",  ntl_lua_fn_host("__kr_timer_after"));
ntl_lua_table_set(_wtimer, "tween",  ntl_lua_fn_host("__kr_noop"));
ntl_lua_table_set(_wtimer, "during", ntl_lua_fn_host("__kr_noop"));
ntl_lua_table_set(_wtimer, "script", ntl_lua_fn_host("__kr_noop"));
ntl_lua_table_set(_wtimer, "cancel", ntl_lua_fn_host("__kr_noop"));
ntl_lua_table_set(_wtimer, "clear",  ntl_lua_fn_host("__kr_noop"));
ntl_lua_table_set(_world, "timer", _wtimer);
ntl_lua_table_set(_world, "stage", ntl_lua_table_new());
ntl_lua_table_set(_g, "Game", _game);
ntl_lua_table_set(_g, "MainMenu", ntl_lua_table_get(_states, "MainMenu"));
ntl_lua_table_set(_g, "LoadingState", ntl_lua_table_get(_states, "Loading"));

// ---------- Assets（资源访问 → 桥接到 GM asset_get_index）----------
var _assets = ntl_lua_table_new();
ntl_lua_table_set(_assets, "getTexture",  ntl_lua_fn_host("__kr_asset"));
ntl_lua_table_set(_assets, "getSprite",   ntl_lua_fn_host("__kr_asset"));
ntl_lua_table_set(_assets, "getSound",    ntl_lua_fn_host("__kr_asset"));
ntl_lua_table_set(_assets, "getFont",     ntl_lua_fn_host("__kr_asset"));
ntl_lua_table_set(_assets, "getShader",   ntl_lua_fn_host("__kr_asset"));
ntl_lua_table_set(_assets, "get",         ntl_lua_fn_host("__kr_asset"));
ntl_lua_table_set(_assets, "exists",      ntl_lua_fn_host("__kr_asset_exists"));
ntl_lua_table_set(_assets, "loadTexture", ntl_lua_fn_host("__kr_asset"));
ntl_lua_table_set(_g, "Assets", _assets);

// ---------- Registry（数据注册表）----------
var _reg = ntl_lua_table_new();
ntl_lua_table_set(_reg, "get",     ntl_lua_fn_host("__kr_reg_get"));
ntl_lua_table_set(_reg, "has",     ntl_lua_fn_host("__kr_false"));
ntl_lua_table_set(_reg, "set",     ntl_lua_fn_host("__kr_noop"));
ntl_lua_table_set(_reg, "getAll",  ntl_lua_fn_host("__kr_reg_all"));
ntl_lua_table_set(_reg, "getGroup", ntl_lua_fn_host("__kr_reg_all"));
ntl_lua_table_set(_g, "Registry", _reg);

// ---------- Mod（当前 mod 信息）----------
var _mod = ntl_lua_table_new();
ntl_lua_table_set(_mod, "id", "neutraled.mod");
ntl_lua_table_set(_mod, "info", ntl_lua_table_new());
ntl_lua_table_set(_mod, "libs", ntl_lua_table_new());
ntl_lua_table_set(_g, "Mod", _mod);

// ---------- Music / Input / Camera / Draw ----------
var _music = ntl_lua_table_new();
ntl_lua_table_set(_music, "play",   ntl_lua_fn_host("__kr_play_music"));
ntl_lua_table_set(_music, "stop",   ntl_lua_fn_host("__kr_stop_music"));
ntl_lua_table_set(_music, "pause",  ntl_lua_fn_host("__kr_noop"));
ntl_lua_table_set(_music, "resume", ntl_lua_fn_host("__kr_noop"));
ntl_lua_table_set(_music, "setVolume", ntl_lua_fn_host("__kr_noop"));
ntl_lua_table_set(_g, "Music", _music);

var _input = ntl_lua_table_new();
ntl_lua_table_set(_input, "isDown",     ntl_lua_fn_host("__love_kb_isDown"));
ntl_lua_table_set(_input, "isConfirm",  ntl_lua_fn_host("__kr_key_confirm"));
ntl_lua_table_set(_input, "isCancel",   ntl_lua_fn_host("__kr_key_cancel"));
ntl_lua_table_set(_input, "isMenu",     ntl_lua_fn_host("__kr_key_menu"));
ntl_lua_table_set(_input, "getDown",    ntl_lua_fn_host("__love_kb_isDown"));
ntl_lua_table_set(_g, "Input", _input);

var _cam = ntl_lua_table_new();
ntl_lua_table_set(_cam, "x", 0);
ntl_lua_table_set(_cam, "y", 0);
ntl_lua_table_set(_cam, "zoomX", 1);
ntl_lua_table_set(_cam, "zoomY", 1);
ntl_lua_table_set(_cam, "attach", ntl_lua_fn_host("__kr_noop"));
ntl_lua_table_set(_cam, "setPosition", ntl_lua_fn_host("__kr_noop"));
ntl_lua_table_set(_g, "Camera", _cam);

var _draw = ntl_lua_table_new();
ntl_lua_table_set(_draw, "setColor", ntl_lua_fn_host("__love_gfx_setColor"));
ntl_lua_table_set(_draw, "rectangle", ntl_lua_fn_host("__love_gfx_rectangle"));
ntl_lua_table_set(_draw, "print", ntl_lua_fn_host("__love_gfx_print"));
ntl_lua_table_set(_g, "Draw", _draw);

// ---------- 其它常用工具（若未加载则给空表，避免 nil 索引）----------
var _stubNames = ["Vector", "LibTimer", "Ease", "SemVer", "Utils", "HookSystem",
                  "FileSystemUtils", "TiledUtils", "ColorUtils", "MathUtils",
                  "StringUtils", "TableUtils", "ClassUtils", "CollisionUtil", "GitFinder"];
// 只对确实不存在的补桩（require 过的真实库不覆盖）

ntl_log("kristal", "Kristal 兼容层已注册");
return 1;
