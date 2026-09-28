/// ntl_modmenu_lang_cycle([step]) —— 切到上/下一个可用语言（←→ 快切；step 省略 = +1）
var _step = (argument_count > 0) ? real(argument[0]) : 1;
if (_step == 0) _step = 1;
var _list = ntl_lang_list();
var _n = array_length(_list);
if (_n <= 0) return 0;
var _cur = (variable_global_exists("ntl_lang")) ? string_lower(string(global.ntl_lang)) : "zh";
var _idx = 0;
for (var _i = 0; _i < _n; _i += 1)
{
    if (string_lower(string(_list[_i])) == _cur) _idx = _i;
}
var _next = ((_idx + _step) mod _n + _n) mod _n;
return ntl_modmenu_lang_set(_list[_next]);