/// ntl_root_filter() —— 构建当前搜索条件下的"显示列表"
///  global.ntl_ch_display: 数组，元素 = 章节索引（>=0）或 -1（空槽/无内容）
///  组成：官方 1..7 槽位（含空槽灰显）+ 全部 mod 章节（按序号排序）

function _ntl_match(_m, _kw)
{
    if (string_length(_kw) == 0) return true;
    var _k = string_lower(_kw);
    if (string_pos(_k, string_lower(ds_map_find_value(_m, "name"))) > 0) return true;
    if (string_pos(_k, string_lower(ds_map_find_value(_m, "source"))) > 0) return true;
    if (string_pos(_k, string_lower(ds_map_find_value(_m, "author"))) > 0) return true;
    if (string_pos(_k, string_lower(ds_map_find_value(_m, "id"))) > 0) return true;
    if (string_pos(_k, string_lower(string(ds_map_find_value(_m, "order")))) > 0) return true;
    return false;
}

var _kw = global.ntl_ch_search;
var _n = array_length(global.ntl_ch);
var _display = [];

// 1) 官方槽位 1..7（每槽取 official 条目；没有则 -1 空槽）
for (var _slot = 1; _slot <= 7; _slot += 1)
{
    var _best = -1;
    for (var _i = 0; _i < _n; _i += 1)
    {
        var _m = global.ntl_ch[_i];
        if (ds_map_find_value(_m, "kind") != "official") continue;
        if (real(ds_map_find_value(_m, "order")) != _slot) continue;
        if (!_ntl_match(_m, _kw)) continue;
        _best = _i;
        break;
    }
    // 无搜索词时保留空槽（灰显占位）；有搜索词时空槽不占位
    if (_best >= 0 || string_length(_kw) == 0) array_push(_display, _best);
}

// 2) mod 章节（patch / timeline），按序号排序
var _mods = [];
for (var _i2 = 0; _i2 < _n; _i2 += 1)
{
    var _m2 = global.ntl_ch[_i2];
    var _kind = ds_map_find_value(_m2, "kind");
    if (_kind == "official") continue;
    if (!_ntl_match(_m2, _kw)) continue;
    array_push(_mods, _i2);
}
// 简单排序（按 order）
var _cnt = array_length(_mods);
for (var _a = 0; _a < _cnt; _a += 1)
{
    for (var _b = _a + 1; _b < _cnt; _b += 1)
    {
        var _oa = real(ds_map_find_value(global.ntl_ch[_mods[_a]], "order"));
        var _ob = real(ds_map_find_value(global.ntl_ch[_mods[_b]], "order"));
        if (_ob < _oa)
        {
            var _tmp = _mods[_a];
            _mods[_a] = _mods[_b];
            _mods[_b] = _tmp;
        }
    }
}
for (var _k = 0; _k < _cnt; _k += 1) array_push(_display, _mods[_k]);

global.ntl_ch_display = _display;
global.ntl_ch_filtered = _display;
return array_length(_display);
