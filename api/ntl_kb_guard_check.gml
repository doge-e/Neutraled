/// ntl_kb_guard_check(key) —— keyboard_check 的控制台守卫
var _k = argument[0];
if (variable_global_exists("ntl_console_open") && global.ntl_console_open) return false;
return keyboard_check(_k);
