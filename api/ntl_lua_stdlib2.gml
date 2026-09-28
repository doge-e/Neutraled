/// ntl_lua_stdlib2() —— 元表 / 模块 / 常用补充库注册
if (!variable_global_exists("ntl_lua_globals")) return 0;
var _g = global.ntl_lua_globals;

ntl_lua_table_set(_g, "setmetatable",   ntl_lua_fn_host("__lua_setmetatable"));
ntl_lua_table_set(_g, "getmetatable",   ntl_lua_fn_host("__lua_getmetatable"));
ntl_lua_table_set(_g, "rawequal",       ntl_lua_fn_host("__lua_rawequal"));
ntl_lua_table_set(_g, "next",           ntl_lua_fn_host("__lua_next"));
ntl_lua_table_set(_g, "collectgarbage", ntl_lua_fn_host("__lua_noop"));
ntl_lua_table_set(_g, "print",          ntl_lua_fn_host("__lua_print"));
ntl_lua_table_set(_g, "require",        ntl_lua_fn_host("__lua_require"));

// os（Kristal 常用）
var _os = ntl_lua_table_new();
ntl_lua_table_set(_os, "time",  ntl_lua_fn_host("__lua_os_time"));
ntl_lua_table_set(_os, "clock", ntl_lua_fn_host("__lua_os_clock"));
ntl_lua_table_set(_os, "date",  ntl_lua_fn_host("__lua_os_date"));
ntl_lua_table_set(_os, "getenv", ntl_lua_fn_host("__lua_noop"));
ntl_lua_table_set(_g, "os", _os);

// io（仅占位，避免 nil 报错）
var _io = ntl_lua_table_new();
ntl_lua_table_set(_io, "write", ntl_lua_fn_host("__lua_print"));
ntl_lua_table_set(_g, "io", _io);

// table 补充
var _tab = ntl_lua_table_get(_g, "table");
if (_tab == undefined) { _tab = ntl_lua_table_new(); ntl_lua_table_set(_g, "table", _tab); }
ntl_lua_table_set(_tab, "unpack", ntl_lua_fn_host("__lua_unpack"));
ntl_lua_table_set(_tab, "getn",   ntl_lua_fn_host("__lua_str_len"));
// ---- coroutine（语义兼容版，见 ntl_lua_coroutine.gml 的说明）----
var _co = ntl_lua_table_new();
ntl_lua_table_set(_co, "create",       ntl_lua_fn_host("__co_create"));
ntl_lua_table_set(_co, "resume",       ntl_lua_fn_host("__co_resume"));
ntl_lua_table_set(_co, "yield",        ntl_lua_fn_host("__co_yield"));
ntl_lua_table_set(_co, "status",       ntl_lua_fn_host("__co_status"));
ntl_lua_table_set(_co, "wrap",         ntl_lua_fn_host("__co_wrap"));
ntl_lua_table_set(_co, "running",      ntl_lua_fn_host("__co_running"));
ntl_lua_table_set(_co, "isyieldable",  ntl_lua_fn_host("__co_isyieldable"));
ntl_lua_table_set(_co, "close",        ntl_lua_fn_host("__co_close"));
ntl_lua_table_set(_g, "coroutine", _co);

// ---- load / loadstring ----
ntl_lua_table_set(_g, "load",       ntl_lua_fn_host("__lua_load"));
ntl_lua_table_set(_g, "loadstring", ntl_lua_fn_host("__lua_loadstring"));

// ---- debug 库 ----
var _dbg = ntl_lua_table_new();
ntl_lua_table_set(_dbg, "traceback",  ntl_lua_fn_host("__dbg_traceback"));
ntl_lua_table_set(_dbg, "getinfo",    ntl_lua_fn_host("__dbg_getinfo"));
ntl_lua_table_set(_dbg, "sethook",    ntl_lua_fn_host("__dbg_sethook"));
ntl_lua_table_set(_dbg, "gethook",    ntl_lua_fn_host("__dbg_gethook"));
ntl_lua_table_set(_dbg, "getlocal",   ntl_lua_fn_host("__dbg_getlocal"));
ntl_lua_table_set(_dbg, "setlocal",   ntl_lua_fn_host("__dbg_setlocal"));
ntl_lua_table_set(_g, "debug", _dbg);

// ---- love.event（最小兼容）----
if (variable_global_exists("love"))
{
    var _ev = ntl_lua_table_new();
    ntl_lua_table_set(_ev, "quit", ntl_lua_fn_host("__loveev_quit"));
    ntl_lua_table_set(_ev, "push", ntl_lua_fn_host("__loveev_push"));
    ntl_lua_table_set(_ev, "pump", ntl_lua_fn_host("__loveev_pump"));
    ntl_lua_table_set(_ev, "poll", ntl_lua_fn_host("__loveev_poll"));
    ntl_lua_table_set(_ev, "clear", ntl_lua_fn_host("__loveev_clear"));
    ntl_lua_table_set(love, "event", _ev);
}

// ---- io / os 的部分兼容（Kristal 偶尔用）----
var _io = ntl_lua_table_new();
ntl_lua_table_set(_io, "write", ntl_lua_fn_host("__io_write"));
ntl_lua_table_set(_g, "io", _io);
return 1;

