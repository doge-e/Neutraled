// ntl_log(tag, msg) —— 写运行日志（相对路径，沙箱重定向到 %LOCALAPPDATA%\DELTARUNE\）
var _tag = string(argument[0]);
var _msg = string(argument[1]);

if (!variable_global_exists("ntl_log_path")) return 0;
if (!variable_global_exists("ntl_log_ok")) global.ntl_log_ok = true;
if (!variable_global_exists("ntl_log_lines")) global.ntl_log_lines = 0;
// ★ 缺陷 2 修复：写失败不再"一次失败、本进程永久闭嘴"。
//   global.ntl_log_fail_n = 失败后累计的调用次数；每满 60 次调用才重试一次 open（≈1 秒 @60fps）。
//   一切正常时热路径只多一次整数比较，不做任何文件系统探测（不每帧探测磁盘）。
if (!variable_global_exists("ntl_log_fail_n")) global.ntl_log_fail_n = 0;

var _retry = 0;
if (global.ntl_log_ok) _retry = 1;
else
{
    global.ntl_log_fail_n += 1;
    if (global.ntl_log_fail_n >= 60) _retry = 1;
}

var _wrote = 0;
if (_retry == 1)
{
    // 目录被删 / 盘暂时忙都可能自愈：只在"本进程首次调用"和"重试"这两条罕见路径上探测目录
    if (!variable_global_exists("ntl_log_dir_ready") || global.ntl_log_fail_n > 0)
    {
        global.ntl_log_dir_ready = 1;
        ntl_ensure_dir(global.ntl_log_path);
    }

    var _f = -1;
    try { _f = file_text_open_append(global.ntl_log_path); } catch (e) { _f = -1; }
    if (_f != -1)
    {
        var _wasDown = (global.ntl_log_ok) ? 0 : 1;
        file_text_write_string(_f, "[" + _tag + "] " + _msg);
        file_text_writeln(_f);
        if (_wasDown == 1)
        {
            // 恢复本身要留痕：否则失败到恢复之间那段日志是静默丢失的，看日志的人不知道缺口在哪
            file_text_write_string(_f, "[log] 日志写盘已恢复（此前约 " + string(global.ntl_log_fail_n) + " 次调用未能落盘）");
            file_text_writeln(_f);
        }
        file_text_close(_f);
        global.ntl_log_ok = true;
        global.ntl_log_fail_n = 0;
        _wrote = 1;
    }
    else
    {
        global.ntl_log_ok = false;
        global.ntl_log_fail_n = 0;
    }
}
if (_wrote == 0) show_debug_message("[Neutraled/" + _tag + "] " + _msg);

global.ntl_log_lines += 1;
return 0;
