/// ntl_love_init() —— 把 LOVE2D API 桥接到 GameMaker（供 Kristal 等 Lua 引擎使用）
/// 注册 global.ntl_lua_globals.love，以及 utf8 / 常用全局
if (!variable_global_exists("ntl_lua_globals")) return 0;
var _g = global.ntl_lua_globals;

var _love = ntl_lua_table_new();
ntl_log("love", "[init] 开始注册 LOVE 桥接");

// ---- love.graphics（映射到 GM 的 draw_* / surface）----
var _gfx = ntl_lua_table_new();
ntl_lua_table_set(_gfx, "setColor",   ntl_lua_fn_host("__love_gfx_setColor"));
ntl_lua_table_set(_gfx, "setColor4",  ntl_lua_fn_host("__love_gfx_setColor"));
ntl_lua_table_set(_gfx, "rectangle",  ntl_lua_fn_host("__love_gfx_rectangle"));
ntl_lua_table_set(_gfx, "circle",     ntl_lua_fn_host("__love_gfx_circle"));
ntl_lua_table_set(_gfx, "line",       ntl_lua_fn_host("__love_gfx_line"));
ntl_lua_table_set(_gfx, "print",      ntl_lua_fn_host("__love_gfx_print"));
ntl_lua_table_set(_gfx, "printf",     ntl_lua_fn_host("__love_gfx_print"));
ntl_lua_table_set(_gfx, "draw",       ntl_lua_fn_host("__love_gfx_draw"));
ntl_lua_table_set(_gfx, "push",       ntl_lua_fn_host("__love_noop"));
ntl_lua_table_set(_gfx, "pop",        ntl_lua_fn_host("__love_noop"));
ntl_lua_table_set(_gfx, "translate",  ntl_lua_fn_host("__love_noop"));
ntl_lua_table_set(_gfx, "scale",      ntl_lua_fn_host("__love_noop"));
ntl_lua_table_set(_gfx, "rotate",     ntl_lua_fn_host("__love_noop"));
ntl_lua_table_set(_gfx, "origin",     ntl_lua_fn_host("__love_noop"));
ntl_lua_table_set(_gfx, "getWidth",   ntl_lua_fn_host("__love_gfx_width"));
ntl_lua_table_set(_gfx, "getHeight",  ntl_lua_fn_host("__love_gfx_height"));
ntl_lua_table_set(_gfx, "newImage",   ntl_lua_fn_host("__love_noop"));
ntl_lua_table_set(_gfx, "newFont",    ntl_lua_fn_host("__love_noop"));
ntl_lua_table_set(_gfx, "newCanvas",  ntl_lua_fn_host("__love_noop"));
ntl_lua_table_set(_gfx, "newQuad",    ntl_lua_fn_host("__love_noop"));
ntl_lua_table_set(_gfx, "setCanvas",  ntl_lua_fn_host("__love_noop"));
ntl_lua_table_set(_gfx, "setShader",  ntl_lua_fn_host("__love_noop"));
ntl_lua_table_set(_gfx, "setBlendMode", ntl_lua_fn_host("__love_noop"));
ntl_lua_table_set(_gfx, "setLineWidth", ntl_lua_fn_host("__love_noop"));
ntl_lua_table_set(_love, "graphics", _gfx);

// ---- love.audio ----
var _aud = ntl_lua_table_new();
ntl_lua_table_set(_aud, "newSource",  ntl_lua_fn_host("__love_noop"));
ntl_lua_table_set(_aud, "play",       ntl_lua_fn_host("__love_noop"));
ntl_lua_table_set(_aud, "stop",       ntl_lua_fn_host("__love_noop"));
ntl_lua_table_set(_aud, "setVolume",  ntl_lua_fn_host("__love_audio_setVolume"));
ntl_lua_table_set(_love, "audio", _aud);

// ---- love.timer ----
var _tm = ntl_lua_table_new();
ntl_lua_table_set(_tm, "getTime",  ntl_lua_fn_host("__love_timer_getTime"));
ntl_lua_table_set(_tm, "getDelta", ntl_lua_fn_host("__love_timer_getDelta"));
ntl_lua_table_set(_tm, "getFPS",   ntl_lua_fn_host("__love_timer_getFPS"));
ntl_lua_table_set(_tm, "sleep",    ntl_lua_fn_host("__love_noop"));
ntl_lua_table_set(_love, "timer", _tm);

// ---- love.math ----
var _lm = ntl_lua_table_new();
ntl_lua_table_set(_lm, "random",     ntl_lua_fn_host("__lua_math_random"));
ntl_lua_table_set(_lm, "floor",      ntl_lua_fn_host("__lua_math_floor"));
ntl_lua_table_set(_lm, "ceil",       ntl_lua_fn_host("__lua_math_ceil"));
ntl_lua_table_set(_lm, "abs",        ntl_lua_fn_host("__lua_math_abs"));
ntl_lua_table_set(_lm, "sqrt",       ntl_lua_fn_host("__lua_math_sqrt"));
ntl_lua_table_set(_lm, "min",        ntl_lua_fn_host("__lua_math_min"));
ntl_lua_table_set(_lm, "max",        ntl_lua_fn_host("__lua_math_max"));
ntl_lua_table_set(_lm, "noise",      ntl_lua_fn_host("__lua_math_random"));
ntl_lua_table_set(_lm, "setRandomSeed", ntl_lua_fn_host("__love_noop"));
ntl_lua_table_set(_love, "math", _lm);

// ---- love.filesystem ----
var _fs = ntl_lua_table_new();
ntl_lua_table_set(_fs, "read",        ntl_lua_fn_host("__love_fs_read"));
ntl_lua_table_set(_fs, "write",       ntl_lua_fn_host("__love_fs_write"));
ntl_lua_table_set(_fs, "exists",      ntl_lua_fn_host("__love_fs_exists"));
ntl_lua_table_set(_fs, "getInfo",     ntl_lua_fn_host("__love_fs_getInfo"));
ntl_lua_table_set(_fs, "getDirectoryItems", ntl_lua_fn_host("__love_fs_list"));
ntl_lua_table_set(_fs, "createDirectory",   ntl_lua_fn_host("__love_noop"));
ntl_lua_table_set(_fs, "load",        ntl_lua_fn_host("__love_fs_read"));
ntl_lua_table_set(_love, "filesystem", _fs);

// ---- love.window / keyboard / mouse / event ----
var _win = ntl_lua_table_new();
ntl_lua_table_set(_win, "setTitle",  ntl_lua_fn_host("__love_noop"));
ntl_lua_table_set(_win, "getMode",   ntl_lua_fn_host("__love_window_mode"));
ntl_lua_table_set(_win, "setMode",   ntl_lua_fn_host("__love_noop"));
ntl_lua_table_set(_love, "window", _win);

var _kb = ntl_lua_table_new();
ntl_lua_table_set(_kb, "isDown",   ntl_lua_fn_host("__love_kb_isDown"));
ntl_lua_table_set(_kb, "isScancodeDown", ntl_lua_fn_host("__love_kb_isDown"));
ntl_lua_table_set(_love, "keyboard", _kb);

var _ms = ntl_lua_table_new();
ntl_lua_table_set(_ms, "getX",     ntl_lua_fn_host("__love_mouse_x"));
ntl_lua_table_set(_ms, "getY",     ntl_lua_fn_host("__love_mouse_y"));
ntl_lua_table_set(_ms, "getPosition", ntl_lua_fn_host("__love_mouse_pos"));
ntl_lua_table_set(_ms, "isDown",   ntl_lua_fn_host("__love_noop"));
ntl_lua_table_set(_love, "mouse", _ms);

var _ev = ntl_lua_table_new();
ntl_lua_table_set(_ev, "push", ntl_lua_fn_host("__love_noop"));
ntl_lua_table_set(_love, "event", _ev);

ntl_lua_table_set(_g, "love", _love);
ntl_lua_table_set(_g, "utf8", ntl_lua_table_new());
ntl_log("love", "[init] LOVE 桥接注册完成");
return 1;
