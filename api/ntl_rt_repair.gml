/// ntl_rt_repair() —— 让运行时目录（游戏沙箱 "Neutraled/"）重新可写
/// 返回可用前缀（"Neutraled/" 或 <安装区>/Neutraled/）；修不好返回 ""
/// ★ 用户主诉（2026-10-03）：「无存档进入 kristal 章节不应当提示，应当自动修复后继续」
/// 事故：%LOCALAPPDATA%\DELTARUNE 是指向 <游戏根>\Neutraled\saves\DELTARUNE 的目录联接；
///   saves 下的目标目录被删掉后联接悬空，而 Windows **无法透过悬空联接创建文件** ——
///   launch-request.json / external-*.txt 标记 / dr-api.log 全部静默写失败（玩家只看到「点了没反应」）。
/// 修法：用**绝对路径**把联接目标目录 <游戏根>\Neutraled\saves\<存档区名>\Neutraled\ 建回来
///   （绝对路径不受沙箱重定向；见 ntl_rt_probe.gml 的路径口径说明）。
var _pd = string(program_directory);
_pd = string_replace_all(_pd, chr(92), "/");
if (string_length(_pd) > 0 && string_char_at(_pd, string_length(_pd)) != "/") _pd = _pd + "/";

// ① 本来就正常（绝大多数情况，只多写一个几十字节的探针文件）
if (ntl_rt_probe("Neutraled/") == 1) return "Neutraled/";

// ② 只是 Neutraled 子目录缺失（联接有效）：补建即可
try { ntl_ensure_dir("Neutraled/.rw-probe"); } catch (e_rt0) { }
if (ntl_rt_probe("Neutraled/") == 1) return "Neutraled/";

// ③ 联接悬空：按约定把目标目录建回来
var _base = _pd;
if (string_pos("chapter", string_lower(ntl_rt_savename(_pd))) == 1)
{
    var _up = ntl_rt_parentdir(_pd);
    if (_up != "") _base = _up;
}
var _nm = ntl_rt_savename();
if (_nm != "")
{
    var _tgt = _base + "Neutraled/saves/" + _nm + "/Neutraled/.rw-probe";
    try { ntl_ensure_dir(_tgt); } catch (e_rt1) { ntl_log("fs", "[rt] 补建存档目录失败: " + string(e_rt1)); }
    if (ntl_rt_probe("Neutraled/") == 1)
    {
        ntl_log("rt", "[修复] 存档联接的目标目录不存在（悬空），已自动补建: " + _base + "Neutraled/saves/" + _nm + "/");
        return "Neutraled/";
    }
    ntl_log("rt", "[rt] 补建后仍不可写（联接目标在别处？）: " + _base + "Neutraled/saves/" + _nm + "/");
}
else
{
    ntl_log("rt", "[rt] 拿不到存档区目录名（game_save_id=" + string(game_save_id) + "），无法补建联接目标");
}

// ④ 兜底：安装区（守候进程同样轮询 <安装区>\Neutraled\launch-request.json）
if (ntl_rt_probe(_pd + "Neutraled/") == 1)
{
    ntl_log("rt", "[rt] 存档区不可写，退回安装区前缀: " + _pd + "Neutraled/");
    return _pd + "Neutraled/";
}
return "";