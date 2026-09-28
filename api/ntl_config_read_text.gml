/// ntl_config_read_text(path) —— 原样读回文本（文件不存在或读失败返回 ""）
var _path = string(argument[0]);
if (_path == "" || !file_exists(_path)) return "";
var _txt = "";
try
{
    var _f = file_text_open_read(_path);
    while (!file_text_eof(_f))
    {
        _txt += file_text_read_string(_f);
        file_text_readln(_f);
        if (!file_text_eof(_f)) _txt += chr(10);
    }
    file_text_close(_f);
}
catch (e)
{
    ntl_log("cfg", "[警告] 读取失败: " + _path + " -> " + string(e));
    return "";
}
return _txt;
