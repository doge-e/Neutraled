/// ntl_lua_fastpath(path) —— 取得（并缓存）某个 Lua 文件的编译结果
/// 性能优化：hook / 对象事件这类"每帧可能要跑"的脚本，
/// 编译结果（AST）按文件路径 + 修改时间缓存，避免重复读盘与解析。
///
/// 返回 ds_map { ok, ast, env_factory }；失败返回 undefined
var _path = string(argument[0]);
if (!variable_global_exists("ntl_lua_fastcache")) global.ntl_lua_fastcache = ds_map_create();
var _cache = global.ntl_lua_fastcache;

// 文件修改时间作为失效依据
var _mtime = 0;
try { _mtime = file_datetime(_path); } catch (e) { _mtime = 0; }
var _key = _path + "|" + string(_mtime);

if (ds_map_exists(_cache, _key))
{
    global.ntl_lua_cache_hit += 1;
    return ds_map_find_value(_cache, _key);
}

// 未命中：读盘 + 编译
var _src = ntl_live_file_read(_path);
if (_src == undefined) return undefined;
var _ast = ntl_live_compile(_src, _path);

// 缓存（限制条数，避免无限增长）
if (ds_map_size(_cache) > 200)
{
    ds_map_clear(_cache);
    ntl_log("lua", "[性能] 编译缓存已清空（超过 200 条）");
}
ds_map_add(_cache, _key, _ast);
if (!variable_global_exists("ntl_lua_cache_miss")) global.ntl_lua_cache_miss = 0;
global.ntl_lua_cache_miss += 1;
return _ast;
