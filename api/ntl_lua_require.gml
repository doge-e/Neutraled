/// ntl_lua_require(name) —— Lua 模块加载（require 的宿主实现）
///   查找顺序：<mod_dir>/lib/<name>.lua → <mod_dir>/lib/<name>/init.lua
///             → <mod_dir>/<name>.lua → <mod_dir>/scripts/<name>.lua
///             → <mod_dir>/libraries/**（递归）
///             → <游戏根>/Kristal-main/<name>.lua（Kristal 引擎源码）
///   结果缓存到 global.ntl_lua_modules（同一模块只执行一次）
var _name = string(argument[0]);
var _dot = string_replace_all(_name, ".", "/");

var _modDir = "";
if (variable_global_exists("ntl_lua_mod_dir")) _modDir = global.ntl_lua_mod_dir;

if (!variable_global_exists("ntl_lua_modules")) global.ntl_lua_modules = ds_map_create();
var _cacheKey = _modDir + "|" + _name;
if (ds_map_exists(global.ntl_lua_modules, _cacheKey))
    return ds_map_find_value(global.ntl_lua_modules, _cacheKey);

var _cands = [];
if (_modDir != "")
{
    array_push(_cands, _modDir + "lib/" + _dot + ".lua");
    array_push(_cands, _modDir + "lib/" + _dot + "/init.lua");
    array_push(_cands, _modDir + _dot + ".lua");
    array_push(_cands, _modDir + "scripts/" + _dot + ".lua");
}
array_push(_cands, program_directory + "Kristal-main/" + _dot + ".lua");
array_push(_cands, program_directory + "Kristal-main/src/" + _dot + ".lua");
array_push(_cands, program_directory + "Kristal-main/lib/" + _dot + ".lua");

var _found = "";
for (var _i = 0; _i < array_length(_cands); _i += 1)
{
    if (file_exists(_cands[_i])) { _found = _cands[_i]; break; }
}

// mod 的 libraries 目录（递归查找）
if (_found == "" && _modDir != "")
{
    var _libDir = _modDir + "libraries/";
    if (directory_exists(_libDir))
    {
        var _stack = [_libDir];
        var _guard = 0;
        while (array_length(_stack) > 0 && _guard < 200 && _found == "")
        {
            _guard += 1;
            var _cur = _stack[0];
            array_delete(_stack, 0, 1);
            var _fn = file_find_first(_cur + "/*", fa_directory);
            while (_fn != "" && _fn != -1)
            {
                if (_fn != "." && _fn != "..") array_push(_stack, _cur + "/" + _fn + "/");
                _fn = file_find_next();
            }
            file_find_close();
            var _ff = file_find_first(_cur + "/*.lua", 0);
            while (_ff != "" && _ff != -1)
            {
                if (string_pos(_dot, _ff) > 0) { _found = _cur + "/" + _ff; break; }
                _ff = file_find_next();
            }
            file_find_close();
        }
    }
}

if (_found == "")
{
    ntl_log("lua", "[require] 未找到模块: " + _name);
    return undefined;
}

// 执行模块（隔离环境，捕获顶层 return 值）
var _prevDir = global.ntl_lua_mod_dir;
var _src = ntl_live_file_read(_found);
var _c = ntl_lua_compile(_src);
var _result = undefined;

if (ds_map_find_value(_c, "ok") == 1)
{
    var _env = ntl_lua_env_new(global.ntl_lua_globals);

    // 模块所在目录（手动找最后一个路径分隔符，get_filename 对混合斜杠不可靠）
    var _sep = 0;
    for (var _ci = string_length(_found); _ci >= 1; _ci -= 1)
    {
        var _cc = string_char_at(_found, _ci);
        if (_cc == "/" || _cc == chr(92)) { _sep = _ci; break; }
    }
    var _dirOnly = (_sep > 0) ? string_copy(_found, 1, _sep) : _found;
    ntl_lua_env_declare(_env, "__mod_dir", _dirOnly);
    ntl_lua_env_declare(_env, "__mod_path", _found);

    var _r = ntl_lua_ev_block(_env, ds_map_find_value(_c, "ast"));
    if (ntl_lua_is_ctrl(_r))
    {
        var _vals = ds_map_find_value(_r, "vals");
        if (array_length(_vals) > 0) _result = _vals[0];
    }
    else _result = _r;

    if (global.ntl_lua_err != "")
    {
        ntl_log("lua", "[require] 模块运行错误 " + _found + ": " + global.ntl_lua_err);
        global.ntl_lua_err = "";
    }
}
else
{
    ntl_log("lua", "[require] 模块语法错误 " + _found + ": " + string(ds_map_find_value(_c, "err")));
}
ds_map_destroy(_c);
global.ntl_lua_mod_dir = _prevDir;

if (_result == undefined) _result = true;
ds_map_add(global.ntl_lua_modules, _cacheKey, _result);
return _result;
