/// ntl_console_style(key, value) —— 控制台外观设置
/// key: lines（显示行数 5-30）/ alpha（透明度 0.3-1.0）/ font（字号缩放，暂不支持）
if (!variable_global_exists("ntl_console_lines_shown")) global.ntl_console_lines_shown = 14;
if (!variable_global_exists("ntl_console_alpha")) global.ntl_console_alpha = 0.88;

// ★ 反人类缺陷修复（2026-09-28 实测）：分派方 ntl_console_exec 传进来的只有一整条参数串
//   （style lines 10 → argument[0] = "lines 10"），而这里原来只读 argument[0]、
//   把 argument[1] 当成值 ⇒ 「style lines 10」/「style alpha 0.7」永远走"无参"分支，值根本改不掉。
var _k = "";
var _v = "";
if (argument_count > 1)
{
    _k = string_lower(string_trim(string(argument[0])));
    _v = string_trim(string(argument[1]));
}
else
{
    var _raw = string_trim(string(argument[0]));
    var _sp = string_pos(" ", _raw);
    if (_sp > 0)
    {
        _k = string_lower(string_trim(string_copy(_raw, 1, _sp - 1)));
        _v = string_trim(string_delete(_raw, 1, _sp));
    }
    else _k = string_lower(_raw);
}

if (_k == "" || _v == "")
{
    ntl_console_log(ntl_t("style.head"));
    ntl_console_log(ntl_ts("style.lines", [global.ntl_console_lines_shown]));
    ntl_console_log(ntl_ts("style.alpha", [round(global.ntl_console_alpha * 100)]));
    ntl_console_log(ntl_t("style.usage"));
    return 0;
}

if (_k == "lines")
{
    var _n = real(_v);
    if (_n < 5) _n = 5;
    if (_n > 30) _n = 30;
    global.ntl_console_lines_shown = _n;
    ntl_console_log(ntl_ts("style.set_lines", [_n]));
    return 1;
}
if (_k == "alpha")
{
    var _a = real(_v);
    if (_a < 0.3) _a = 0.3;
    if (_a > 1.0) _a = 1.0;
    global.ntl_console_alpha = _a;
    ntl_console_log(ntl_ts("style.set_alpha", [round(_a * 100)]));
    return 1;
}
ntl_console_log(ntl_ts("style.unknown", [_k]));
return 0;
