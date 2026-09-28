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
    game_change(_pfx + _dir, "-game data.win" + _params);
    return 1;
}
// ---- 兜底：仍走官方对象 ----
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
