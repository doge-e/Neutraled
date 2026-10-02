/// ntl_goto_chapter(chapter) —— 直接切换章节
/// 【已搬迁】官方选择器的 launch_game 内部就是 game_change(dir, "-game data.win" + params)，
/// 这里改用我们**自己的章节表**（chapters.json → global.ntl_ch）拿 dir，不再依赖 obj_CHAPTER_SELECT。
/// 这样章节选择就能完全由 Neutraled 掌控（官方对象不再监听 Enter，外部章节才能常驻秒回）。
/// 返回 1 成功 / 0 失败（表里查不到时回退到官方对象）。
var _ch = argument[0];
var _dir = "";
if (global.ntl_ch_loaded == 1)
{
    var _n = array_length(global.ntl_ch);
    for (var _i = 0; _i < _n; _i += 1)
    {
        var _m = global.ntl_ch[_i];
        if (real(ds_map_find_value(_m, "order")) != _ch) continue;
        if (ds_map_find_value(_m, "kind") != "official") continue;
        var _d = ds_map_find_value(_m, "dir");
        if (is_string(_d) && string_length(_d) > 0) { _dir = _d; break; }
    }
}
if (_dir != "")
{
    var _params = "";
    try { _params = get_chapter_switch_parameters(); } catch (e) { _params = " launcher"; }
    // ★ game_change 的目录是相对**当前**游戏目录解析的（2026-09-28 全流程实测）：
    //   root 进程里 "/章节目录" 就够；但在章节进程里（working_directory = chapterN_windows）
    //   必须 "/../章节目录" 才能先回到游戏根、再进兄弟目录，否则 game_change 静默不生效
    //   ——现象：章节内 goto 3 只打印「跳转到第 3 章...」，游戏一动不动（日志里却记了 game_change）。
    //   官方 scr_chapterswitch 在 Windows 上用的正是 "/../chapter" + N + "_windows"。
    var _pfx = (ntl_is_root() == 1) ? "/" : "/../";
    ntl_log("auto", "ntl_goto_chapter -> " + _pfx + _dir + "（搬迁实现：直接 game_change）");
    // ★ 看门狗现场（2026-09-30）：game_change 失败时**既不报错也不返回**（用户实测：日志里有
    //   调用、进程却纹丝不动），只能先记下现场，由 ntl_chg_watch()（api/ntl_chg_watch.gml）
    //   在 ≥90 帧后按阶梯重试：官方入口 → 换前缀 → 可见提示。
    global.ntl_chg_pending = 1;
    global.ntl_chg_frame = global.ntl_frames;
    global.ntl_chg_try = 0;
    global.ntl_chg_dir = _dir;
    global.ntl_chg_full = _pfx + _dir;
    global.ntl_chg_kind = "official";
    global.ntl_chg_order = _ch;
    global.ntl_chg_args = "-game data.win" + _params;
    global.ntl_chg_wd = working_directory;
    global.ntl_chg_pd = program_directory;
    // ★ t35 F3（t30 真机 §12：契约要求的 ntl_chg*/working_directory/program_directory/
    //   parameter_string/ntl_is_root 在正常路径不落盘）——登记现场时一次性写全，真机 review 不必再猜。
    var _chgps = "";
    // ★ 2026-10-02 真机实证：parameter_string() 在本运行时直接 0xc0000005 崩掉整个进程，已移除该调用，勿加回。
    _chgps = "<parameter_string 不可用>";
    var _chgmsg = "[chg] dir=" + _dir + " pfx=" + _pfx + " full=" + _pfx + _dir
            + " kind=official order=" + string(_ch) + " wd=" + working_directory + " pd=" + program_directory
            + " parameter_string=" + _chgps + " args=" + ("-game data.win" + _params) + " ntl_is_root=" + string(ntl_is_root())
            + " frame=" + string(global.ntl_frames);
    ntl_log("auto", _chgmsg);
    game_change(_pfx + _dir, "-game data.win" + _params);
    return 1;
}
// ---- 兜底：仍走官方对象（官方实例现在是被我们**停用**的 —— 扫描前先激活）----
try { instance_activate_object(asset_get_index("obj_CHAPTER_SELECT")); } catch (e_act) { }
var _inst = noone;
var _count = instance_count;
for (var _i = 0; _i < _count; _i += 1)
{
    var _o = instance_id[_i];
    if (_o == noone) continue;
    if (object_get_name(_o.object_index) == "obj_CHAPTER_SELECT")
    {
        _inst = _o;
        break;
    }
}
if (_inst == noone)
{
    ntl_log("auto", "ntl_goto_chapter: 未找到 obj_CHAPTER_SELECT 实例");
    return 0;
}
_inst._target_chapter = _ch;
ntl_log("auto", "ntl_goto_chapter -> chapter " + string(_ch));
_inst.launch_game(0);
return 1;
