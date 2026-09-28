/// ntl_modmenu_lang_set(code) —— 把界面语言切到指定代码（语言列表 / ←→ 快切 都用它）
var _code = string_lower(string(argument[0]));
var _list = ntl_lang_list();
var _ok = false;
for (var _i = 0; _i < array_length(_list); _i += 1)
{
    if (string_lower(string(_list[_i])) == _code) _ok = true;
}
if (!_ok) return 0;
if (ntl_lang_set(_code) == 1)
{
    ntl_config_set_lang(_code);
    global.ntl_modmenu_msg = ntl_ts("menu.lang_now", [_code]);
    global.ntl_modmenu_msg_frames = 120;
    ntl_log("modmenu", "[面板] 语言已切换为 " + _code + "（共 " + string(array_length(_list)) + " 种可选）");
    return 1;
}
global.ntl_modmenu_msg = ntl_ts("root.lang.fail", [_code]);
global.ntl_modmenu_msg_frames = 120;
ntl_log("modmenu", "[面板] 语言 " + _code + " 加载失败");
return 0;