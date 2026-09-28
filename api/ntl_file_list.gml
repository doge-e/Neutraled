/// ntl_file_list(path, pattern) —— 列出目录下的文件
var _p = string(argument[0]);
var _pat = (argument_count > 1) ? string(argument[1]) : "*";
var _out = [];
if (!directory_exists(_p)) return _out;

// ★ 修复：GM 的 file_find_next() 结束时返回 -1（不是 ""）；旧写法会在末尾继续调用导致未捕获致命错误（进程消失）。
var _f = file_find_first(_p + _pat, 0);   // ★ 修复：DR 运行时没有 fa_none 常量（读它会报 not set before reading it），用 0
while (_f != "" && _f != -1)
{
    array_push(_out, _f);
    _f = file_find_next();
}
file_find_close();
return _out;
