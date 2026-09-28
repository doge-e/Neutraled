/// ntl_modmenu_modinfo(dirname) —— 把 mods/ 目录名对到「本产物已加载清单」：返回 [已加载(0/1), 显示名, 版本]
/// 数据源：ntl_modmenu_loaded()（<working_directory>Neutraled/mods.json，deploy 时写）。
/// 匹配规则见 ntl_keynorm（大小写/空格/下划线/点不敏感；Id 另外按 "." 分段逐段比，
/// 例如目录 dojo 命中 Id "converted.dojo.xanzo1" 的第 2 段）。
/// 结果缓存在 global.ntl_modmenu_modinfo_cache（面板打开 / 每次按键时清空）；
/// 读不到清单（老产物、外部 exe）时一律返回「未加载」，绝不猜。
var _nm = string(argument[0]);
var _d = ntl_keynorm(_nm);
if (string_length(_d) <= 0) return [0, _nm, ""];
if (!variable_global_exists("ntl_modmenu_modinfo_cache")) global.ntl_modmenu_modinfo_cache = ds_map_create();
if (is_undefined(global.ntl_modmenu_modinfo_cache)) global.ntl_modmenu_modinfo_cache = ds_map_create();
if (ds_map_exists(global.ntl_modmenu_modinfo_cache, _d)) return ds_map_find_value(global.ntl_modmenu_modinfo_cache, _d);

var _res = [0, _nm, ""];
var _ld = ntl_modmenu_loaded();
for (var _i = 0; _i < array_length(_ld); _i += 1)
{
    var _e = _ld[_i];
    if (!is_struct(_e)) continue;
    var _en = variable_struct_exists(_e, "Name") ? string(variable_struct_get(_e, "Name")) : "";
    var _ei = variable_struct_exists(_e, "Id") ? string(variable_struct_get(_e, "Id")) : "";
    var _ev = variable_struct_exists(_e, "Version") ? string(variable_struct_get(_e, "Version")) : "";
    var _hit = (string_length(_en) > 0 && ntl_keynorm(_en) == _d) || (string_length(_ei) > 0 && ntl_keynorm(_ei) == _d);
    if (!_hit && string_length(_ei) > 0)
    {
        var _id = _ei;
        while (string_length(_id) > 0)
        {
            var _p = string_pos(".", _id);
            var _seg = (_p > 0) ? string_copy(_id, 1, _p - 1) : _id;
            _id = (_p > 0) ? string_delete(_id, 1, _p) : "";
            if (string_length(_seg) > 0 && ntl_keynorm(_seg) == _d) { _hit = true; break; }
        }
    }
    if (_hit)
    {
        _res = [1, (string_length(_en) > 0) ? _en : _nm, _ev];
        break;
    }
}
ds_map_add(global.ntl_modmenu_modinfo_cache, _d, _res);
return _res;
