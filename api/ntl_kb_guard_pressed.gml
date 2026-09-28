/// ntl_kb_guard_pressed(key) —— keyboard_check_pressed 的控制台守卫
var _k = argument[0];
if (variable_global_exists("ntl_console_open") && global.ntl_console_open) return false;
return keyboard_check_pressed(_k);
