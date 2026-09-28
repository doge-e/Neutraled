/// ntl_lua_stdlib() —— 注册标准库到全局环境（Lua ↔ GML 兼容层的核心）
if (!variable_global_exists("ntl_lua_globals")) return 0;
var _g = global.ntl_lua_globals;

// ---- 基础函数（宿主实现）----
ntl_lua_table_set(_g, "print",     ntl_lua_fn_host("__lua_print"));
ntl_lua_table_set(_g, "tostring",  ntl_lua_fn_host("__lua_tostring"));
ntl_lua_table_set(_g, "tonumber",  ntl_lua_fn_host("__lua_tonumber"));
ntl_lua_table_set(_g, "type",      ntl_lua_fn_host("__lua_type"));
ntl_lua_table_set(_g, "pairs",     ntl_lua_fn_host("__lua_pairs"));
ntl_lua_table_set(_g, "ipairs",    ntl_lua_fn_host("__lua_ipairs"));
ntl_lua_table_set(_g, "rawget",    ntl_lua_fn_host("__lua_rawget"));
ntl_lua_table_set(_g, "rawset",    ntl_lua_fn_host("__lua_rawset"));
ntl_lua_table_set(_g, "error",     ntl_lua_fn_host("__lua_error"));
ntl_lua_table_set(_g, "assert",    ntl_lua_fn_host("__lua_assert"));
ntl_lua_table_set(_g, "pcall",     ntl_lua_fn_host("__lua_pcall"));
ntl_lua_table_set(_g, "unpack",    ntl_lua_fn_host("__lua_unpack"));
ntl_lua_table_set(_g, "select",    ntl_lua_fn_host("__lua_select"));
ntl_lua_table_set(_g, "require",   ntl_lua_fn_host("__lua_require"));

// ---- math ----
var _math = ntl_lua_table_new();
ntl_lua_table_set(_math, "pi", 3.14159265358979);
ntl_lua_table_set(_math, "huge", power(10, 308));
ntl_lua_table_set(_math, "floor", ntl_lua_fn_host("__lua_math_floor"));
ntl_lua_table_set(_math, "ceil",  ntl_lua_fn_host("__lua_math_ceil"));
ntl_lua_table_set(_math, "abs",   ntl_lua_fn_host("__lua_math_abs"));
ntl_lua_table_set(_math, "sqrt",  ntl_lua_fn_host("__lua_math_sqrt"));
ntl_lua_table_set(_math, "sin",   ntl_lua_fn_host("__lua_math_sin"));
ntl_lua_table_set(_math, "cos",   ntl_lua_fn_host("__lua_math_cos"));
ntl_lua_table_set(_math, "random", ntl_lua_fn_host("__lua_math_random"));
ntl_lua_table_set(_math, "min",   ntl_lua_fn_host("__lua_math_min"));
ntl_lua_table_set(_math, "max",   ntl_lua_fn_host("__lua_math_max"));
ntl_lua_table_set(_math, "fmod",  ntl_lua_fn_host("__lua_math_fmod"));
ntl_lua_table_set(_math, "pow",   ntl_lua_fn_host("__lua_math_pow"));
ntl_lua_table_set(_g, "math", _math);

// ---- string ----
var _str = ntl_lua_table_new();
ntl_lua_table_set(_str, "len",    ntl_lua_fn_host("__lua_str_len"));
ntl_lua_table_set(_str, "sub",    ntl_lua_fn_host("__lua_str_sub"));
ntl_lua_table_set(_str, "upper",  ntl_lua_fn_host("__lua_str_upper"));
ntl_lua_table_set(_str, "lower",  ntl_lua_fn_host("__lua_str_lower"));
ntl_lua_table_set(_str, "find",   ntl_lua_fn_host("__lua_str_find"));
ntl_lua_table_set(_str, "rep",    ntl_lua_fn_host("__lua_str_rep"));
ntl_lua_table_set(_str, "format", ntl_lua_fn_host("__lua_str_format"));
ntl_lua_table_set(_str, "char",   ntl_lua_fn_host("__lua_str_char"));
ntl_lua_table_set(_str, "byte",   ntl_lua_fn_host("__lua_str_byte"));
ntl_lua_table_set(_g, "string", _str);

// ---- table ----
var _tab = ntl_lua_table_new();
ntl_lua_table_set(_tab, "insert", ntl_lua_fn_host("__lua_tab_insert"));
ntl_lua_table_set(_tab, "remove", ntl_lua_fn_host("__lua_tab_remove"));
ntl_lua_table_set(_tab, "concat", ntl_lua_fn_host("__lua_tab_concat"));
ntl_lua_table_set(_g, "table", _tab);

// ---- GML 兼容直通表：游戏侧函数 ----
//   允许 Lua 里写 game.xxx(...) / ntl.xxx(...)
var _game = ntl_lua_table_new();
ntl_lua_table_set(_g, "game", _game);
var _ntl = ntl_lua_table_new();
ntl_lua_table_set(_ntl, "log", ntl_lua_fn_host("ntl_log"));
ntl_lua_table_set(_ntl, "screenshot", ntl_lua_fn_host("ntl_screenshot"));
ntl_lua_table_set(_ntl, "goto_chapter", ntl_lua_fn_host("ntl_goto_chapter"));
ntl_lua_table_set(_ntl, "call", ntl_lua_fn_host("__lua_gml_call"));
ntl_lua_table_set(_g, "ntl", _ntl);

ntl_lua_table_set(_g, "_G", _g);

// ---- GML 直通表：gml.string(x) / gml.floor(x) / gml.inst_count("obj") … ----
//   ★ 为什么需要：本文件把 string / math / table / print 这些名字注册成了 Lua 标准库，
//   而 ntl_call_host 里同名的 GML 包装（string / real / floor / ceil / min / max …）会被遮蔽 ⇒
//   Lua 里写 GML 风格的 string(5) 会以 "attempt to call a non-function value" 失败。
//   这里把 GML 口径的包装集中挂到 gml.*（值仍是宿主函数串 __host:<名字>，调用时经 ntl_call_host 分派）。
//   名单只收 ntl_call_host 里已登记的名字（未登记的会走"未知函数"分支，不要写进来）。
var _gml = ntl_lua_table_new();
var _gmlNames = [
    "string", "real", "floor", "ceil", "round", "sign", "abs", "sqrt", "power", "min", "max", "clamp", "lerp", "random", "irandom",
    "len", "str_len", "str_sub", "str_find", "str_upper", "str_lower", "str_split", "str_contains", "str_char",
    "arr_new", "arr_push", "arr_get", "arr_set", "arr_len", "arr_pop", "arr_join",
    "map_new", "map_get", "map_set", "map_has", "map_del", "map_keys",
    "inst_all", "inst_count", "inst_first", "inst_nth", "inst_find", "inst_get", "inst_set", "inst_create", "inst_destroy", "inst_exists",
    "obj_spawn", "obj_count", "room_width_v", "room_height_v",
    "draw_text", "draw_sprite", "draw_line", "draw_rectangle", "draw_circle", "draw_set_color", "draw_set_alpha", "draw_set_font",
    "file_read", "file_write", "json_parse_safe", "log", "get_global", "set_global", "var_get", "var_set",
    "skip_intro", "sprite_resolve", "sprite_replace", "sprite_from_file", "perf_time", "whatis", "res_report", "res_untrack"
];
for (var _gi = 0; _gi < array_length(_gmlNames); _gi += 1)
    ntl_lua_table_set(_gml, _gmlNames[_gi], ntl_lua_fn_host(_gmlNames[_gi]));
ntl_lua_table_set(_g, "gml", _gml);

return 1;
