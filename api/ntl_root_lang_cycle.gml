/// ntl_root_lang_cycle() —— 章节选择器按 L：切到下一个可用语言、立刻生效并写回 config.json
var _list = ntl_lang_list();
var _cur = (variable_global_exists("ntl_lang")) ? string_lower(string(global.ntl_lang)) : "zh";
var _idx = -1;
for (var _i = 0; _i < array_length(_list); _i += 1)
{
    if (string_lower(string(_list[_i])) == _cur) _idx = _i;
}
var _next = _list[(_idx + 1) % array_length(_list)];
if (ntl_lang_set(_next) == 1)
{
    ntl_config_set_lang(_next);
    global.ntl_root_toast = ntl_ts("root.lang.cur", [_next]);
    global.ntl_root_toast_frames = 120;
    ntl_log("root", "[toast] lang=" + string(global.ntl_lang) + " key=root.lang.cur text=" + string(global.ntl_root_toast));
    ntl_log("root", "[root] 语言已切换为 " + _next + "（共 " + string(array_length(_list)) + " 种可选）");
}
else
{
    global.ntl_root_toast = ntl_ts("root.lang.fail", [_next]);
    global.ntl_root_toast_frames = 120;
    ntl_log("root", "[toast] lang=" + string(global.ntl_lang) + " key=root.lang.fail text=" + string(global.ntl_root_toast));
    ntl_log("root", "[root] 语言 " + _next + " 加载失败");
}
return 1;
