/// ntl_console_filter(mode) —— 日志过滤（只看某类输出）
/// mode: "all" / "error" / "warn" / "cmd"
if (!variable_global_exists("ntl_console_filter_mode")) global.ntl_console_filter_mode = "all";
var _m = string_lower(string_trim(string(argument[0])));
if (_m == "")
{
    ntl_console_log(ntl_ts("flt.cur", [global.ntl_console_filter_mode]), 1);
    ntl_console_log(ntl_t("flt.u"), 1);
    return 0;
}
if (_m != "all" && _m != "error" && _m != "warn" && _m != "cmd")
{
    ntl_console_log(ntl_ts("flt.unknown", [_m]), 1);
    return 0;
}
global.ntl_console_filter_mode = _m;
ntl_console_log(ntl_ts("flt.set", [_m]), 1);
return 1;
