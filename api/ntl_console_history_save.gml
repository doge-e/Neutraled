/// ntl_console_history_save() —— 把命令历史存到文件（下次启动可恢复）
/// ★ 返回值改为**写入的完整路径**（没有历史时返回 ""）。
///   旧版返回 0/1、调用方硬编码一句相对路径提示，用户根本不知道文件在哪。
if (!variable_global_exists("ntl_console_history")) return "";
var _path = program_directory + "Neutraled/console-history.txt";
var _h = global.ntl_console_history;
var _n = array_length(_h);
if (_n == 0) return "";
var _out = "";
var _start = max(0, _n - 100);
for (var _i = _start; _i < _n; _i += 1) _out += string(_h[_i]) + chr(10);
// 确保目录存在（GM 不会自动建目录，file_text_open_write 会静默失败）
ntl_ensure_dir(_path);
try { var _f = file_text_open_write(_path); file_text_write_string(_f, _out); file_text_close(_f); }
catch (e) { ntl_log("console", "[ntl] ntl_console_history_save.gml:13 保存命令历史失败: " + string(e)); }
return _path;
