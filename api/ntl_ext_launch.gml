/// ntl_ext_launch(chapterMap) —— 启动"外部引擎章节"（Kristal 等项目）
/// 背景：Kristal 是 LÖVE 工程（main.lua + conf.lua + data/），**没有 GameMaker data.win**，
/// 在本运行时里加载不了它。与其移植引擎，不如让用户去玩它自己的引擎：
/// 插件加载、进度保存、通关判定全部是 Kristal 原生行为，一点都不用仿。
///
/// ★ 实测：本运行时**没有任何启动进程的内置函数**（execute_program / execute_shell /
///   os_start_process / url_open 在 data.win 字符串池里一个都不存在），所以 GML 自己拉不起程序。
///   做法改成：**写一个启动请求文件，然后退出游戏**；外面的启动器（GUI / --watch-external）
///   看到请求就把 Kristal 拉起来。
/// 返回 1 = 已写出请求（调用方必须立刻 return，不要再做章节切换）
var _m = argument[0];
if (_m == undefined) return 0;

var _exe = string(ds_map_find_value(_m, "exe"));
var _args = string(ds_map_find_value(_m, "args"));
var _cwd = string(ds_map_find_value(_m, "cwd"));
var _name = string(ds_map_find_value(_m, "name"));
if (_exe == "" || _exe == "undefined")
{
    ntl_log("ext", "[错误] 外部章节没有可执行文件路径（exe）");
    return 0;
}

// 用相对路径（GM 会把它重定向进存档区：%LOCALAPPDATA%\DELTARUNE\Neutraled\）；
// 并确保目录存在 —— 目录不存在时 file_text_open_write 会失败，而失败**不会抛异常**
var _req = "Neutraled/launch-request.json";
ntl_ensure_dir(_req);
// ⚠ ntl_json_esc 已经返回**带引号的**字符串（"xxx"），外面不要再套 chr(34)，
//   否则会变成 ""xxx"" —— 实测就是这么写出非法 JSON 的。
// 注意 file_text_write_string 按本地代码页(GBK)写盘，中文经手会乱码
//   （实测 冰封帷幕 Frostveil 写成 鍐板皝甯峰箷 Frostveil）。
//   所以请求里只写 ASCII 名；显示名由启动器从 chapters.json 读（C# 写的 UTF-8，没问题）。
var _name_ascii = "";
for (var _ni = 1; _ni <= string_length(_name); _ni++) {
    var _nc = string_char_at(_name, _ni);
    if (ord(_nc) < 128) _name_ascii += _nc;
}
var _txt = "{"
    + chr(34) + "exe" + chr(34) + ":" + ntl_json_esc(_exe) + ","
    + chr(34) + "args" + chr(34) + ":" + ntl_json_esc(_args) + ","
    + chr(34) + "cwd" + chr(34) + ":" + ntl_json_esc(_cwd) + ","
    + chr(34) + "name" + chr(34) + ":" + ntl_json_esc(_name_ascii)
    + "}";

var _ok = 0;
try
{
    var _f = file_text_open_write(_req);
    file_text_write_string(_f, _txt);
    file_text_close(_f);
    _ok = 1;
}
catch (e) { ntl_log("ext", "[错误] 写启动请求失败: " + string(e)); }

// 必须回读确认 —— file_text_open_write 失败时不会抛异常，只看 _ok 会误判成功
if (_ok != 1 || !file_exists(_req))
{
    ntl_log("ext", "[错误] 启动请求没有落盘: " + _req);
    return 0;
}
var _fc = -1;
try { _fc = file_text_open_read(_req); } catch (e2) { _fc = -1; }
if (_fc != -1)
{
    var _back = file_text_read_string(_fc);
    file_text_close(_fc);
    // ⚠ GML 里 字符串 + real 会抛 "DoAdd :: Execution Error"，必须显式 string()
    ntl_log("ext", "  请求内容回读: " + string(string_length(_back)) + " 字符");
}

ntl_log("ext", "外部章节「" + _name + "」: 已写出启动请求，退出游戏交给启动器");
ntl_log("ext", "  " + _exe + " " + _args);
global.ntl_ext_launching = 1;
global.ntl_ext_wait = 20;
return 1;
