/// ntl_mod_import(modId, only) —— 把一个 mod 的导出表**导入到当前环境**
///
/// 和 ntl_mod_require 的区别：
///   require → 返回表：  local a = ntl_mod_require("x"); a.fn()
///   import  → 直接注入：ntl_mod_import("x"); fn()
///
/// 用法（mod 的 main.lua）：
///   local n = ntl_mod_import("frostveil.core")
///   -- 或只导入指定名字
///   local n = ntl_mod_import("frostveil.core", { "heal", "damage" })
var _modId = string(argument[0]);
var _only = (argument_count > 1) ? argument[1] : undefined;

var _tbl = ntl_mod_require(_modId);
if (_tbl == undefined)
{
    ntl_log("mod", "[导入] 找不到 mod: " + _modId);
    return 0;
}

// 枚举导出名（用 table_keys，跳过 _ntl* 内部键）
var _keys = ntl_lua_table_keys(_tbl);
ntl_log("mod", "[导入诊断] tbl=" + string(_tbl) + " isReal=" + string(is_real(_tbl)) +
        " keys=" + string(array_length(_keys)) +
        " tblSize=" + string((is_real(_tbl)) ? ds_map_size(_tbl) : -1));
var _n = 0;
for (var _i = 0; _i < array_length(_keys); _i += 1)
{
    var _k = string(_keys[_i]);

    // 白名单过滤
    if (_only != undefined && is_array(_only))
    {
        var _hit = 0;
        for (var _j = 0; _j < array_length(_only); _j += 1)
            if (string(_only[_j]) == _k) { _hit = 1; break; }
        if (_hit == 0) continue;
    }

    var _v = ntl_lua_table_get(_tbl, _k);
    if (_v == undefined) continue;

    // 注入到全局环境
    if (variable_global_exists("ntl_lua_globals"))
        ntl_lua_table_set(global.ntl_lua_globals, _k, _v);
    _n += 1;
}
ntl_log("mod", "[导入] 从 " + _modId + " 导入了 " + string(_n) + " 个符号");
return _n;
