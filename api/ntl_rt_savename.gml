/// ntl_rt_savename(path?) —— 取路径的最后一段（目录名/文件名）
/// 不给参数时用 game_save_id，也就是**存档区目录名**（%LOCALAPPDATA% 下那个联接的名字）
/// 例：game_save_id = "C:\Users\x\AppData\Local\DELTARUNE\" -> 返回 "DELTARUNE"
/// 用途：悬空联接的目标目录约定是 <游戏根>\Neutraled\saves\<这个名字>
var _p = "";
if (argument_count >= 1) _p = string(argument[0]); else _p = string(game_save_id);
_p = string_replace_all(_p, chr(92), "/");
while (string_length(_p) > 1 && string_char_at(_p, string_length(_p)) == "/") { _p = string_delete(_p, string_length(_p), 1); }
var _pos = 0;
for (var _i = string_length(_p); _i >= 1; _i -= 1)
{
    if (string_char_at(_p, _i) == "/") { _pos = _i; break; }
}
var _nm = (_pos > 0) ? string_delete(_p, 1, _pos) : _p;
if (string_pos(":", _nm) > 0) return "";
return _nm;