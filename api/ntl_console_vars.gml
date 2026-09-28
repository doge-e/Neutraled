/// ntl_console_vars(prefix) —— 列出全局变量的当前值（便于调试）
var _pre = string_lower(string(argument[0]));

// GM 没有"枚举所有 global"的 API，所以枚举我们自己关心的名字空间
// ★ 修复：原来的清单里 "ntl_ready" 出现了两次（重复显示一行），并补上控制台/暂停/语言等常用项
var _names = [
    "ntl_version", "ntl_live_api", "ntl_lang", "ntl_frames", "ntl_ready",
    "ntl_console_open", "ntl_console_input", "ntl_console_lines_shown", "ntl_console_alpha",
    "ntl_console_filter_mode", "ntl_paused", "ntl_step_frames", "ntl_god",
    "ntl_lua_err", "ntl_ctx_module", "ntl_ctx_event",
    "ntl_obj_count", "ntl_near_count", "ntl_interact_count", "ntl_interact_name",
    "ntl_ch_sel", "ntl_ch_page", "ntl_ch_search", "ntl_ch_search_mode",
    "ntl_say_text"
];

ntl_console_log(ntl_t("vars.head"));
var _shown = 0;
for (var _i = 0; _i < array_length(_names); _i += 1)
{
    var _nm = _names[_i];
    if (_pre != "" && string_pos(_pre, string_lower(_nm)) <= 0) continue;
    if (!variable_global_exists(_nm)) continue;
    var _v = variable_global_get(_nm);
    var _sv = "";
    if (_v == undefined) _sv = "nil";
    else if (is_real(_v)) _sv = string(_v);
    else if (is_bool(_v)) _sv = (_v ? "true" : "false");
    else if (is_string(_v)) _sv = "\"" + string_copy(_v, 1, 60) + "\"";
    else _sv = "<" + string(typeof(_v)) + ">";
    ntl_console_log("  " + _nm + " = " + _sv);
    _shown += 1;
}
if (_shown == 0) ntl_console_log(ntl_t("vars.none"));
// ★ 反人类修复：以前只列 19 个名字、也没有"怎么查更多"的线索
ntl_console_log(ntl_ts("vars.count", [string(_shown), string(array_length(_names))]));
if (_pre == "") ntl_console_log(ntl_t("vars.tip"));
return 0;
