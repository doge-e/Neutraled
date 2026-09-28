/// ntl_kb_guard_direct(key) —— keyboard_check_direct 的控制台守卫
var _k = argument[0];
if (variable_global_exists("ntl_console_open") && global.ntl_console_open) return false;
return keyboard_check_direct(_k);
