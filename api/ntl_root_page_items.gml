/// ntl_root_page_items(page) —— 该页 7 个槽位对应的章节索引（-1 = 空）
var _page = argument[0];
var _items = [];
if (!variable_global_exists("ntl_ch_display")) { for (var _z = 0; _z < 7; _z += 1) _items[_z] = -1; return _items; }

var _all = global.ntl_ch_display;
var _total = array_length(_all);
var _start = _page * 7;
for (var _k = 0; _k < 7; _k += 1)
{
    var _idx = _start + _k;
    _items[_k] = (_idx < _total) ? _all[_idx] : -1;
}
return _items;
