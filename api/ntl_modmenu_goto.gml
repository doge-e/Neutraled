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
// 修复 2026-09-29：时间线条目以前直接落到 ntl_goto_chapter(order)，而它只认 kind==official，
//   于是「在面板点平行时间线」打开的是同 Order 的官方章节（点 StoryNarrators Test 进了第 1 章）。
//   时间线有自己的部署目录 dir，必须像章节选择器那样 game_change 过去。
if (_kind == "timeline")
{
    var _dir = string(ds_map_find_value(_m, "dir"));
    if (string_length(_dir) <= 0)
    {
        global.ntl_modmenu_msg = ntl_t("menu.ch_none");
        global.ntl_modmenu_msg_frames = 90;
        return 0;
    }
    var _params = "";
    try { _params = get_chapter_switch_parameters(); } catch (e) { _params = " launcher"; }
    // game_change 的目录按当前游戏目录解析：root 进程用 /，章节进程必须先回游戏根（见 ntl_goto_chapter.gml:24-29）
    var _pfx = (ntl_is_root() == 1) ? "/" : "/../";
    ntl_log("modmenu", "[面板] 启动平行时间线 " + string(ds_map_find_value(_m, "id")) + " -> " + _pfx + _dir);
    game_change(_pfx + _dir, "-game data.win" + _params);
    return 1;
}
return ntl_goto_chapter(_order);
