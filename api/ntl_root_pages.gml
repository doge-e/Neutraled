/// ntl_root_pages() —— 总页数（每页 7 项，基于显示列表长度）
if (!variable_global_exists("ntl_ch_display")) return 1;
var _total = array_length(global.ntl_ch_display);
var _need = (_total + 6) div 7;
if (_need < 1) _need = 1;
return _need;
