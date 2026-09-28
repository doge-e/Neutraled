/// ntl_console_autorun(path) —— 执行启动脚本（每行一条控制台命令，# 开头为注释）
/// 默认路径：Neutraled/autorun.console（存档区）——给 mod 开发者做可复现的测试场景用
var _p = (argument_count > 0) ? string_trim(string(argument[0])) : "";
var _explicit = (_p != "");
if (_p == "") _p = "Neutraled/autorun.console";
if (!file_exists(_p))
{
    if (_explicit) ntl_console_log(ntl_ts("au.nofile", [_p]));
    else
    {
        // ★ 反人类修复：默认脚本不存在时原来"什么都不打印"，用户以为命令坏了/没反应。
        //   现在明确告知：没有启动脚本 + 默认路径 + 怎么用。
        ntl_console_log(ntl_ts("au.none_default", [_p]));
        ntl_console_log(ntl_t("au.howto"));
    }
    return 0;
}
var _n = 0;
var _bad = 0;
var _delay = 0;
var _queued = 0;
if (!variable_global_exists("ntl_script_queue")) global.ntl_script_queue = [];
var _f = file_text_open_read(_p);
if (_f == -1) { ntl_console_log(ntl_t("out.fail") + ntl_ts("au.open_fail", [_p])); return 0; }
while (!file_text_eof(_f))
{
    var _ln = string_trim(file_text_read_string(_f));
    file_text_readln(_f);
    if (_ln == "") continue;
    if (string_copy(_ln, 1, 1) == "#") continue;
    // sleep <帧数>：把**剩余行**排进延时队列，之后每帧推进 —— 让启动脚本能写"进游戏再检查"这类场景
    if (string_copy(_ln, 1, 6) == "sleep ")
    {
        var _wait = max(1, floor(real(string_trim(string_delete(_ln, 1, 6)))));
        _delay += _wait;
        while (!file_text_eof(_f))
        {
            var _rest = string_trim(file_text_read_string(_f));
            file_text_readln(_f);
            if (_rest == "" || string_copy(_rest, 1, 1) == "#") continue;
            var _sub = (string_copy(_rest, 1, 6) == "sleep ")
                ? max(1, floor(real(string_trim(string_delete(_rest, 1, 6)))))
                : 0;
            if (_sub > 0) { _delay += _sub; continue; }
            array_push(global.ntl_script_queue, [_delay, _rest]);
            _queued += 1;
        }
        break;
    }
    // 逐条容错：脚本里一条命令出错，不应该让游戏在启动时就崩
    try { ntl_console_exec(_ln); _n += 1; }
    catch (e)
    {
        ntl_console_log(ntl_t("au.fail_head") + _ln + " → " + string(e));
        ntl_log("autorun", "命令失败: " + _ln + " → " + string(e));
        _bad += 1;
    }
}
file_text_close(_f);
if (_queued > 0) ntl_console_log(ntl_ts("au.queued", [string(_queued), string(_delay)]));
ntl_console_log(ntl_ts("au.immediate", [string(_n), ((_bad > 0) ? ntl_ts("au.failed_part", [string(_bad)]) : ""), _p]));
return _n;
