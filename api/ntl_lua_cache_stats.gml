/// ntl_lua_cache_stats() —— 返回编译缓存的命中统计
if (!variable_global_exists("ntl_lua_cache_hit")) global.ntl_lua_cache_hit = 0;
if (!variable_global_exists("ntl_lua_cache_miss")) global.ntl_lua_cache_miss = 0;
var _hit = global.ntl_lua_cache_hit;
var _miss = global.ntl_lua_cache_miss;
var _total = _hit + _miss;
var _rate = (_total > 0) ? round(_hit / _total * 100) : 0;
return { hit: _hit, miss: _miss, rate: _rate };
