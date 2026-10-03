/// ntl_rt_parentdir(path) —— 去掉最后一段（取父目录），返回值末尾带 "/"
/// 例："E:/game/chapter4_windows/" -> "E:/game/"
/// 用途：章节进程的 program_directory 是 <游戏根>/chapterN_windows/，联接目标在游戏根下
var _p = string(argument[0]);
_p = string_replace_all(_p, chr(92), "/");
while (string_length(_p) > 1 && string_char_at(_p, string_length(_p)) == "/") { _p = string_delete(_p, string_length(_p), 1); }
var _pos = 0;
for (var _i = string_length(_p); _i >= 1; _i -= 1)
{
    if (string_char_at(_p, _i) == "/") { _pos = _i; break; }
}
if (_pos <= 0) return "";
return string_copy(_p, 1, _pos);