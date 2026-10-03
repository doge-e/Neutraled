/// ntl_rt_probe(prefix) —— 探测运行时目录前缀是否可写（prefix 形如 "Neutraled/" 或 "E:/.../Neutraled/"）
/// 返回 1 = 能写出文件（探针写完立刻删掉）；0 = 写不出
/// ★ GM 的文件写入失败是**静默的**（file_text_open_write 不抛异常），必须靠回读判定。
/// 路径口径（2026-10-03 实证）：相对路径被重定向进游戏沙箱 <game_save_id>
///   （%LOCALAPPDATA%\DELTARUNE\ = 指向 <游戏根>\Neutraled\saves\DELTARUNE 的目录联接）；
///   绝对路径原样使用（<安装区>\Neutraled\logs\*.txt 等绝对写产物都落在安装区）。
var _pre = string(argument[0]);
if (string_length(_pre) <= 0) return 0;
if (string_char_at(_pre, string_length(_pre)) != "/") _pre = _pre + "/";
var _pf = _pre + ".rw-probe";
var _ok = 0;
// 目录先确保存在（悬空联接上这一步也会失败，不影响探测语义）
try { ntl_ensure_dir(_pf); } catch (e_rtd) { }
try
{
    var _f = file_text_open_write(_pf);
    if (_f != -1) { file_text_write_string(_f, "ok"); file_text_close(_f); }
    if (file_exists(_pf))
    {
        _ok = 1;
        file_delete(_pf);
    }
}
catch (e_rtp) { _ok = 0; }
return _ok;