/// ntl_console_timed_exec(cmdline) —— 执行命令并显示耗时
/// 用于调试"为什么这条命令这么慢"
var _line = string(argument[0]);
var _t0 = current_time;
ntl_console_exec(_line);
var _dt = current_time - _t0;
if (_dt >= 1) ntl_console_log(ntl_ts("exec.took", [_dt]));
return _dt;
