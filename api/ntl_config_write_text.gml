/// ntl_config_write_text(path, txt) —— 写入文本并回读校验，返回 1 成功 / 0 失败
/// 注意：root 产物受 GameMaker 沙箱影响，写 bundle 路径会落到存档区（见 ntl_config_paths.gml）。
var _path = string(argument[0]);
var _txt = string(argument[1]);
if (_path == "" || _txt == "") return 0;
try
{
    var _f = file_text_open_write(_path);
    file_text_write_string(_f, _txt);
    file_text_close(_f);
}
catch (e)
{
    ntl_log("cfg", "[警告] 写入失败: " + _path + " -> " + string(e));
    return 0;
}
var _chk = ntl_config_read_text(_path);
if (string_length(_chk) <= 0)
{
    ntl_log("cfg", "[警告] 写入后回读为空: " + _path);
    return 0;
}
return 1;
