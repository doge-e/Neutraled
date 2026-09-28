/// ntl_modmenu_goto(idx) —— 面板章节视图：进入 global.ntl_ch[idx] 对应的章节
if (!variable_global_exists("ntl_ch_loaded") || global.ntl_ch_loaded != 1)
{
    global.ntl_modmenu_msg = ntl_t("menu.ch_none");
    global.ntl_modmenu_msg_frames = 90;
    return 0;
}
var _n = array_length(global.ntl_ch);
if (argument[0] < 0 || argument[0] >= _n) return 0;
var _m = global.ntl_ch[argument[0]];
if (ds_map_find_value(_m, "enabled") != 1)
{
    global.ntl_modmenu_msg = ntl_t("menu.ch_none");
    global.ntl_modmenu_msg_frames = 90;
    return 0;
}
var _order = real(ds_map_find_value(_m, "order"));
var _kind = string(ds_map_find_value(_m, "kind"));
ntl_log("modmenu", "[面板] 进入章节 " + string(_order) + "（kind=" + _kind + "）");
ntl_modmenu_close();
if (_kind == "external") return ntl_ext_launch(_m);
return ntl_goto_chapter(_order);
