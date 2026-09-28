/// ntl_console_run(path) —— 执行一个命令脚本文件
///
/// ★ 自动化（用户要求）：把常用操作写成脚本，一条命令批量执行。
///
/// 脚本格式（.ntlcmd / .txt）：
///   # 以 # 或 // 开头的是注释
///   log 开始自动化
///   goto 4
///   sleep 60
///   screenshot after_load
///   eval Kristal.getFlag("wr_set")
///
/// 路径解析顺序：
///   1) 绝对路径 / 相对游戏目录
///   2) Neutraled/scripts/<名字>
///   3) 当前 mod 目录下的 scripts/<名字>
var _p = string(argument[0]);
if (_p == "") { ntl_console_log(ntl_t("run.u")); return 0; }

var _cands = [];
array_push(_cands, _p);
array_push(_cands, program_directory + "Neutraled/scripts/" + _p);
if (variable_global_exists("ntl_lua_mod_dir"))
    array_push(_cands, string(global.ntl_lua_mod_dir) + "scripts/" + _p);
if (string_pos(".", _p) <= 0)
{
    var _n2 = array_length(_cands);
    for (var _i = 0; _i < _n2; _i += 1) array_push(_cands, _cands[_i] + ".ntlcmd");
}

var _found = "";
for (var _i = 0; _i < array_length(_cands); _i += 1)
{
    if (file_exists(_cands[_i])) { _found = _cands[_i]; break; }
}
if (_found == "")
{
    ntl_console_log(ntl_ts("run.nofile", [_p]));
    return 0;
}

var _txt = ntl_live_file_read(_found);
if (_txt == undefined) { ntl_console_log(ntl_t("run.readfail")); return 0; }

// 按行执行
var _lines = string_split(_txt, chr(10));
var _ran = 0;
ntl_console_log(ntl_ts("run.head", [_found, array_length(_lines)]));
for (var _i = 0; _i < array_length(_lines); _i += 1)
{
    var _line = string_trim(_lines[_i]);
    if (string_length(_line) == 0) continue;
    if (string_copy(_line, 1, 1) == "#") continue;
    if (string_copy(_line, 1, 2) == "//") continue;
    ntl_console_exec(_line);
    _ran += 1;
    if (_ran > 500) { ntl_console_log(ntl_t("run.toolong")); break; }
}
ntl_console_log(ntl_ts("run.done", [_ran]));
return _ran;
