/// ntl_lang_set(lang) —— 切换语言（zh / en / auto）
/// ★ F5 接线：同时提供文本覆盖子命令（ntl_set_lang_string / ntl_get_lang_string 的可观测入口）
///   lang set <key> <文本>   写一条覆盖并立刻生效
///   lang get <key>          读一条文本（覆盖优先，其次 lang_map）
var _raw = string_trim(string(argument[0]));
var _low = string_lower(_raw);
if (_low == "set" || string_pos("set ", _low) == 1)
{
    var _body = (_low == "set") ? "" : string_trim(string_delete(_raw, 1, 4));
    var _sp = string_pos(" ", _body);
    if (_sp <= 0)
    {
        ntl_console_log(ntl_t("lang.set_u"), 1);
        return 0;
    }
    var _ok = string_copy(_body, 1, _sp - 1);
    var _ov = string_delete(_body, 1, _sp);
    ntl_set_lang_string(_ok, _ov);
    ntl_console_log(ntl_ts("lang.set_ok", [_ok, ntl_get_lang_string(_ok)]), 1);
    return 1;
}
if (_low == "get" || string_pos("get ", _low) == 1)
{
    var _gk = (_low == "get") ? "" : string_trim(string_delete(_raw, 1, 4));
    if (_gk == "")
    {
        ntl_console_log(ntl_t("lang.get_u"), 1);
        return 0;
    }
    ntl_console_log(_gk + " = " + string(ntl_get_lang_string(_gk)), 1);
    return 1;
}
var _l = string_lower(string_trim(string(argument[0])));
if (_l == "")
{
    ntl_console_log(ntl_t("console.title"));
    ntl_console_log(ntl_t("lang.usage"));
    ntl_console_log(ntl_ts("lang.cur", [global.ntl_lang]));
    return 0;
}
if (_l == "auto")
{
    var _gl = "en";
    if (variable_global_exists("lang")) _gl = string_lower(string(global.lang));
    _l = (string_pos("zh", _gl) > 0 || string_pos("cn", _gl) > 0) ? "zh" : "en";
}
if (!ds_map_exists(global.ntl_i18n, _l))
{
    // 外部语言包（Neutraled/lang/lang_<code>.json）：任意语言码都先试一次，
    // 命中即把该语言的表注册进 global.ntl_i18n —— 之后 ntl_t 立刻说这门语言。
    if (ntl_lang_pack(_l) != 1)
    {
        ntl_console_log(ntl_ts("lang.unsupported", [_l]));
        return 0;
    }
}
global.ntl_lang = _l;
ntl_log("i18n", "语言已切换: " + _l);
ntl_console_log("Language: " + _l + "  |  语言: " + _l);   // ntl:i18n-exempt（中英并排，故意不翻）
return 1;