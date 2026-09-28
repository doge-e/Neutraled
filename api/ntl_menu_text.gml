/// ntl_menu_text(s) —— 面板文案解析："i18n:键名" 走 i18n 表，否则原样返回
var _s = string(argument[0]);
if (string_length(_s) > 5 && string_copy(_s, 1, 5) == "i18n:") return ntl_t(string_delete(_s, 1, 5));
return _s;