/// ntl_modmenu_goto(idx) —— 面板章节视图：进入 global.ntl_ch[idx] 对应的章节
if (!variable_global_exists("ntl_ch_loaded") || global.ntl_ch_loaded != 1)
{
    global.ntl_modmenu_msg = ntl_t("menu.ch_none");
    global.ntl_modmenu_msg_frames = 90;
    ntl_log("modmenu", "[面板提示] lang=" + string(global.ntl_lang) + " key=menu.ch_none text=" + string(global.ntl_modmenu_msg));
    return 0;
}
var _n = array_length(global.ntl_ch);
if (argument[0] < 0 || argument[0] >= _n) return 0;
var _m = global.ntl_ch[argument[0]];
if (ds_map_find_value(_m, "enabled") != 1)
{
    global.ntl_modmenu_msg = ntl_t("menu.ch_none");
    global.ntl_modmenu_msg_frames = 90;
    ntl_log("modmenu", "[面板提示] lang=" + string(global.ntl_lang) + " key=menu.ch_none text=" + string(global.ntl_modmenu_msg));
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
        ntl_log("modmenu", "[面板提示] lang=" + string(global.ntl_lang) + " key=menu.ch_none text=" + string(global.ntl_modmenu_msg));
        return 0;
    }
    var _params = "";
    try { _params = get_chapter_switch_parameters(); } catch (e) { _params = " launcher"; }
    // game_change 的目录按当前游戏目录解析：root 进程用 /，章节进程必须先回游戏根（见 ntl_goto_chapter.gml:24-29）
    var _pfx = (ntl_is_root() == 1) ? "/" : "/../";
    ntl_log("modmenu", "[面板] 启动平行时间线 " + string(ds_map_find_value(_m, "id")) + " -> " + _pfx + _dir);
    // ★ 看门狗现场（2026-09-30）：见 api/ntl_chg_watch.gml
    global.ntl_chg_pending = 1;
    global.ntl_chg_frame = global.ntl_frames;
    global.ntl_chg_try = 0;
    global.ntl_chg_dir = _dir;
    global.ntl_chg_full = _pfx + _dir;
    global.ntl_chg_kind = "timeline";
    global.ntl_chg_order = _order;
    global.ntl_chg_args = "-game data.win" + _params;
    global.ntl_chg_wd = working_directory;
    global.ntl_chg_pd = program_directory;
    // ★ t35 F3（t30 真机 §12：契约要求的 ntl_chg*/working_directory/program_directory/
    //   parameter_string/ntl_is_root 在正常路径不落盘）——登记现场时一次性写全，真机 review 不必再猜。
    var _chgps = "";
    // ★ 2026-10-02 真机实证：parameter_string() 在本运行时直接 0xc0000005 崩掉整个进程，已移除该调用，勿加回。
    _chgps = "<parameter_string 不可用>";
    var _chgmsg = "[chg] dir=" + _dir + " pfx=" + _pfx + " full=" + _pfx + _dir
            + " kind=timeline order=" + string(_order) + " wd=" + working_directory + " pd=" + program_directory
            + " parameter_string=" + _chgps + " args=" + ("-game data.win" + _params) + " ntl_is_root=" + string(ntl_is_root())
            + " frame=" + string(global.ntl_frames);
    ntl_log("modmenu", _chgmsg);
    game_change(_pfx + _dir, "-game data.win" + _params);
    return 1;
}
return ntl_goto_chapter(_order);
