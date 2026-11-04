/// ntl_lua_fastpath(path) —— 取得（并缓存）某个脚本的编译结果
/// 性能优化：hook / 对象事件这类"每帧可能要跑"的脚本，
/// 编译结果（AST）按文件路径 + 修改时间缓存，避免每帧重复读盘与解析。
///
/// 返回 ds_map { ast, lua }；失败返回 undefined
///   ast = 编译结果（Lua 是 { _ntllua, ast } 包装，NTL 是 AST 根）
///   lua = 1 表示这是 Lua 脚本（0 = NTL）
/// ★ 2026-11-04 修复：原来调用 ntl_live_compile(_src, _path) 参数顺序颠倒且缺 cache
///   → ds_map_exists(undefined) Code Error；且 global.ntl_lua_cache_hit 未初始化。
var _path = string(argument[0]);
if (!variable_global_exists("ntl_lua_fastcache")) global.ntl_lua_fastcache = ds_map_create();
if (!variable_global_exists("ntl_lua_cache_hit")) global.ntl_lua_cache_hit = 0;
if (!variable_global_exists("ntl_lua_cache_miss")) global.ntl_lua_cache_miss = 0;
var _cache = global.ntl_lua_fastcache;

// 文件修改时间作为失效依据
var _mtime = 0;
try { if (file_exists(_path)) _mtime = file_datetime(_path); } catch (e) { _mtime = 0; }
var _key = _path + "|" + string(_mtime);

if (ds_map_exists(_cache, _key))
{
    var _hit = ds_map_find_value(_cache, _key);
    if (ds_exists(_hit, ds_type_map))
    {
        global.ntl_lua_cache_hit += 1;
        global.ntl_last_was_lua = ds_map_find_value(_hit, "lua");
        return _hit;
    }
    ds_map_delete(_cache, _key);
}

// 未命中：读盘 + 编译
var _src = ntl_live_file_read(_path);
if (_src == undefined) return undefined;
var _lc = ds_map_create();
var _ast = ntl_live_compile(_path, _src, _lc);
ds_map_destroy(_lc);
if (_ast == undefined) return undefined;

var _entry = ds_map_create();
ds_map_add(_entry, "ast", _ast);
ds_map_add(_entry, "lua", global.ntl_last_was_lua);

// 缓存（限制条数，避免无限增长）
if (ds_map_size(_cache) > 200)
{
    ds_map_clear(_cache);
    ntl_log("lua", "[性能] 编译缓存已清空（超过 200 条）");
}
ds_map_add(_cache, _key, _entry);
global.ntl_lua_cache_miss += 1;
return _entry;
