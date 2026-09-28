/// ntl_live_compile(path, src, cache) —— 编译脚本为 AST 并缓存
/// cache: ds_map(path -> AST)。命中缓存时直接返回，避免每帧重新解析。
var _path = argument[0];
var _src = argument[1];
var _cache = argument[2];

if (ds_map_exists(_cache, _path)) return ds_map_find_value(_cache, _path);

// ---- .lua 文件走 Lua 解释器（返回标记 map，由 ntl_live_run_ast 识别）----
if (string_length(_path) >= 4 && string_lower(string_copy(_path, string_length(_path) - 3, 4)) == ".lua")
{
    ntl_log("lua", "[编译] " + string(_path) + "  (" + string(string_length(_src)) + " 字符)");
    var _lc = ntl_lua_compile(_src);
    var _wrap = ds_map_create();
    ds_map_add(_wrap, "_ntllua", 1);
    ds_map_add(_wrap, "ast", ds_map_find_value(_lc, "ast"));
    if (ds_map_find_value(_lc, "ok") != 1)
    {
        ntl_log("live", "[Lua 语法错误] " + string(_path) + ": " + string(ds_map_find_value(_lc, "err")));
        ds_map_destroy(_lc);
        return undefined;
    }
    ds_map_destroy(_lc);
    ds_map_add(_cache, _path, _wrap);
    global.ntl_last_was_lua = 1;
    return _wrap;
}
global.ntl_last_was_lua = 0;

var _toks = ntl_tok(_src);
var _s = ntl_p_new(_toks);
var _ast = ntl_p_program(_s);
var _err = ds_map_find_value(_s, "err");
ds_list_destroy(_toks);
ds_map_destroy(_s);

if (_err != "")
{
    ntl_log("live", "[语法错误] " + string(_path) + ": " + string(_err));
    return undefined;
}
ds_map_add(_cache, _path, _ast);
return _ast;
