/// ntl_console_history_load() —— 从文件恢复命令历史
var _path = program_directory + "Neutraled/console-history.txt";
if (!file_exists(_path)) return 0;
if (!variable_global_exists("ntl_console_history")) global.ntl_console_history = [];
var _txt = "";
try
{
    var _f = file_text_open_read(_path);
    while (!file_text_eof(_f))
    {
        var _line = file_text_read_string(_f);
        file_text_readln(_f);
        if (string_length(_line) > 0) array_push(global.ntl_console_history, _line);
    }
    file_text_close(_f);
}
catch (e) { ntl_log("console", "[ntl] ntl_console_history_load.gml:17 读取命令历史失败: " + string(e)); }
return array_length(global.ntl_console_history);
