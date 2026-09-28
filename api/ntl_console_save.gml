/// ntl_console_save(name) —— 把控制台全部输出保存到文件
/// 便于把日志发给别人排查
var _nm = string_trim(string(argument[0]));
if (_nm == "") _nm = "console-" + string(current_time);
var _path = program_directory + "Neutraled/logs/" + _nm + ".txt";

// 确保目录存在（GM 不会自动建目录，file_text_open_write 会静默失败）
ntl_ensure_dir(_path);
// 确保目录存在（GM 的 file_text_open_write 不会建目录）
try { if (!directory_exists(program_directory + "Neutraled/logs/")) { } } catch (e) { ntl_log("console", "[ntl] ntl_console_save.gml:10 检查日志目录失败: " + string(e)); }

var _n = ds_list_size(global.ntl_console_lines);
var _out = "";
// 头部信息
_out += ntl_t("save.head") + chr(10);
// ★ current_datetime_string 在这个 GameMaker 运行时里**不存在**（实测：一执行就
//   "Variable <unknown_object>.current_datetime_string not set before reading it" → 整个 Create 事件中断、
//   游戏弹 Code Error）。改用标准的 date_current_datetime() + date_datetime_string()。
var _now = "?";
try { _now = date_datetime_string(date_current_datetime()); }
catch (e) { _now = string(current_time); }
_out += ntl_ts("save.time", [_now]) + chr(10);
_out += "Neutraled: " + global.ntl_version + "  live " + global.ntl_live_api + chr(10);
_out += ntl_ts("save.lang", [global.ntl_lang]) + chr(10);
_out += ntl_ts("save.lines", [string(_n)]) + chr(10);
_out += "================================" + chr(10) + chr(10);
for (var _i = 0; _i < _n; _i += 1) _out += string(ds_list_find_value(global.ntl_console_lines, _i)) + chr(10);

try
{
    var _f = file_text_open_write(_path);
    file_text_write_string(_f, _out);
    file_text_close(_f);
    ntl_console_log(ntl_ts("save.ok", [_path]));
}
catch (e) { ntl_console_log(ntl_ts("save.fail", [e])); }
return _path;
