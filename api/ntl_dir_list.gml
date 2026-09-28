/// ntl_dir_list(path) —— 列出一个目录下的子目录名
/// 注意：GM 的 file_find_first 对绝对路径支持有限，这里做双重尝试
var _p = string(argument[0]);
var _out = [];
if (!directory_exists(_p)) return _out;

// 尝试 1：绝对路径 + fa_directory
var _f = file_find_first(_p + "*", fa_directory);
while (_f != "" && _f != -1)
{
    if (_f != "." && _f != "..") array_push(_out, _f);
    _f = file_find_next();
}
file_find_close();

// 尝试 2：如果没结果，改用带结束斜杠的形式
if (array_length(_out) == 0)
{
    var _p2 = _p;
    if (string_copy(_p2, string_length(_p2), 1) != "/") _p2 += "/";
    var _f2 = file_find_first(_p2 + "*", fa_directory);
    while (_f2 != "" && _f2 != -1)
    {
        if (_f2 != "." && _f2 != "..") array_push(_out, _f2);
        _f2 = file_find_next();
    }
    file_find_close();
}

// 尝试 3：不限定属性
if (array_length(_out) == 0)
{
    var _f3 = file_find_first(_p + "*", 0);   // ★ fa_none 在本运行时不存在，用 0
    while (_f3 != "" && _f3 != -1)
    {
        var _full = _p + _f3;
        if (directory_exists(_full)) array_push(_out, _f3);
        _f3 = file_find_next();
    }
    file_find_close();
}

return _out;
