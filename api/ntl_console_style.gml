/// ntl_console_style(key, value) —— 控制台外观设置
/// key: lines（显示行数 5-30）/ alpha（透明度 0.3-1.0）/ font（字号缩放，暂不支持）
if (!variable_global_exists("ntl_console_lines_shown")) global.ntl_console_lines_shown = 14;
if (!variable_global_exists("ntl_console_alpha")) global.ntl_console_alpha = 0.88;

var _k = string_lower(string_trim(string(argument[0])));
var _v = (argument_count > 1) ? string_trim(string(argument[1])) : "";

if (_k == "" || _v == "")
{
    ntl_console_log(ntl_t("style.head"));
    ntl_console_log(ntl_ts("style.lines", [global.ntl_console_lines_shown]));
    ntl_console_log(ntl_ts("style.alpha", [round(global.ntl_console_alpha * 100)]));
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
