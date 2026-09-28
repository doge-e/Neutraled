/// ntl_kb_guard_string() —— 控制台打开时返回空字符串
if (variable_global_exists("ntl_console_open") && global.ntl_console_open) return "";
return keyboard_string;
