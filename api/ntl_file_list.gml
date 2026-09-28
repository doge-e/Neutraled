/// ntl_file_list(path, pattern) —— 列出目录下的文件
var _p = string(argument[0]);
var _pat = (argument_count > 1) ? string(argument[1]) : "*";
var _out = [];
if (!directory_exists(_p)) return _out;

var _f = file_find_first(_p + _pat, fa_none);
while (_f != "")
{
    array_push(_out, _f);
    _f = file_find_next();
}
file_find_close();
return _out;
