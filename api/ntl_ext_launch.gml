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
// ★ 用户主诉修复（2026-10-03「无存档下无法进入 kristal 章节」）：
//   存档区 %LOCALAPPDATA%\DELTARUNE 是指向 Neutraled/saves/DELTARUNE 的目录联接。玩家把 saves 下的
//   目标目录删掉后联接就悬空，而 Windows **无法透过悬空联接创建文件** —— 请求文件根本写不出去，
//   连 dr-api.log 都写不了（失败是静默的，玩家只看到"点了没反应"）。这里先探一次可写性：
//   写得出探针文件才继续，否则给出**屏幕上可见**的可操作提示。
var _rt_ok = 0;
try
{
    var _pf = file_text_open_write("Neutraled/.rw-probe");
    file_text_write_string(_pf, "ok");
    file_text_close(_pf);
    if (file_exists("Neutraled/.rw-probe"))
    {
        _rt_ok = 1;
        file_delete("Neutraled/.rw-probe");
    }
}
catch (e_rtw) { _rt_ok = 0; }
if (_rt_ok != 1)
{
    global.ntl_root_toast = ntl_t("ext.no_runtime");
    global.ntl_root_toast_frames = 600;
    ntl_log("ext", "[错误] 运行时目录不可写（Neutraled/ 写不出探针文件）—— 存档联接可能已悬空");
    return 0;
}
// ---- ★ 用户主诉修复（2026-10-03）：发请求前清掉**上一局残留**的外部标记 ----
//   现象（真机复现 10:15）：进入外部章节后章节选择器 0 秒就回到前台（日志「0 秒回程」）、
//   而且从没静音 —— park 常驻分支第一帧就看到上一局留下的 external-exited.txt，
//   立刻判定「外部章节已退出」。运行标记 external-running.txt 同理（残留会让游戏以为
//   引擎还开着而屏蔽输入）。必须在写请求**之前**清理：此刻守候进程还没动，删掉的一定是残留。
try
{
    if (file_exists("Neutraled/external-exited.txt"))
    {
        file_delete("Neutraled/external-exited.txt");
        ntl_log("ext", "[ext] 已清掉上一局残留的回程标记 external-exited.txt");
    }
}
catch (e_ntlclr1) { ntl_log("ext", "[ext] 清理残留回程标记失败: " + string(e_ntlclr1)); }
try
{
    if (file_exists("Neutraled/external-running.txt"))
    {
        file_delete("Neutraled/external-running.txt");
        ntl_log("ext", "[ext] 已清掉上一局残留的运行标记 external-running.txt");
    }
}
catch (e_ntlclr2) { ntl_log("ext", "[ext] 清理残留运行标记失败: " + string(e_ntlclr2)); }
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
    global.ntl_root_toast = ntl_t("ext.no_runtime");
    global.ntl_root_toast_frames = 600;
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
// ★ park 门控现场（2026-09-30）：记下请求是从第几帧写下的，谁来判断"等太久"见 ntl_root_step.gml
//   已经是 pending 时**不重置计时** —— 否则玩家多按几次 Enter 就永远等不到"没人消费"的判定。
if (!variable_global_exists("ntl_ext_launching") || global.ntl_ext_launching != 1)
{
    global.ntl_ext_req_frame = global.ntl_frames;
    global.ntl_ext_confirmed = 0;
    global.ntl_ext_park_seen = 0;
    // ★ F-3（t25 复核）：请求文件刚刚回读确认存在（见上面 :55-60 的存在性校验），
    //   在此登记「本进程确实看到它存在过」。门控那边只在「存在 → 不存在」时才认为
    //   被守候进程消费（api/ntl_root_step.gml 的确认段），于是「请求从未落盘 / 被
    //   第三方删掉」不再被误判成已确认（假 park）。已在 pending 时不重置，避免反复
    //   按 Enter 把「已被消费」的证据冲掉（与上面不重置计时的理由一致）。
    global.ntl_ext_req_seen = 1;
}
global.ntl_ext_launching = 1;
global.ntl_ext_wait = 20;
return 1;
