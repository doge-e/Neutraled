/// ntl_chg_watch() —— game_change 看门狗：切换章节静默失败时按阶梯重试
// 背景（2026-09-30 用户实测）：从章节内菜单「回到章节选择」返回到 hub 之后，连续 30 次
//   Enter 都没能切走 —— 日志里每次都有 ntl_goto_chapter -> /chapterN_windows，但进程
//   一次都没换。game_change 没有返回值，失败时**不抛异常也不报错**，调用方因此永远以为
//   自己成功了（这就是「静默失效」）。
// 做法：调用方在 game_change 之前记录现场（ntl_chg_* 一串 global，含 working_directory /
//   program_directory / 目标目录 / 参数 / 序数 / 章节类型）；本函数由 events/Step_1.gml
//   每帧调用（任何进程都跑）。≥90 帧后本进程仍在运行 = 那一次 game_change 没生效，
//   于是按阶梯重试，每级打一张含运行现场的日志；全部失败就给可见提示并停手。
// 阶梯：① 官方入口 obj_CHAPTER_SELECT.launch_game(章节号)（只对官方章节；实例被停用时先激活）
//       ② 备选前缀 "/"、""（相对当前目录）、"/../" 各试一次（game_change 的目录前缀随进程而异）
//       ③ 都不行：toast「章节启动失败」+ 日志，清 pending 不再重试
// 防双重启动：①global 是进程级的 —— 真的切走了，新进程里根本没有 ntl_chg_pending；
//   ②每次重试前比对 working_directory / program_directory，变了说明上下文已经换了；
//   ③每级只试一次、每 90 帧才试一级（同一帧不会连发两个 game_change）；④失败后清标志。
if (!variable_global_exists("ntl_chg_pending")) return 0;
if (global.ntl_chg_pending != 1) return 0;

var _chgRetryFrames = 90;   // ≈1.5 秒；切换成功时进程早已换掉
if (!variable_global_exists("ntl_chg_frame")) global.ntl_chg_frame = global.ntl_frames;
if (!variable_global_exists("ntl_chg_try")) global.ntl_chg_try = 0;

// 帧计数保护：万一 ntl_frames 被重置（同进程重开游戏），只把计时往后挪，不会提前重试
if (global.ntl_chg_frame > global.ntl_frames) global.ntl_chg_frame = global.ntl_frames;
if (global.ntl_frames - global.ntl_chg_frame < _chgRetryFrames) return 0;

// 还在原进程吗？（上下文变了 ⇒ 切换其实已经生效）
var _chgSame = 1;
if (variable_global_exists("ntl_chg_wd"))
{
    try { if (working_directory != global.ntl_chg_wd) _chgSame = 0; } catch (e_chgwd) { }
}
if (variable_global_exists("ntl_chg_pd"))
{
    try { if (program_directory != global.ntl_chg_pd) _chgSame = 0; } catch (e_chgpd) { }
}
if (_chgSame != 1)
{
    global.ntl_chg_pending = 0;
    ntl_log("auto", "[chg] 看门狗撤回：working_directory / program_directory 已变化 —— 切换其实生效了");
    return 0;
}

global.ntl_chg_try += 1;
global.ntl_chg_frame = global.ntl_frames;
var _chgTry = global.ntl_chg_try;
var _chgDir = "?";
if (variable_global_exists("ntl_chg_dir")) _chgDir = string(global.ntl_chg_dir);
var _chgArgs = "-game data.win";
if (variable_global_exists("ntl_chg_args")) _chgArgs = string(global.ntl_chg_args);
var _chgOrder = -1;
if (variable_global_exists("ntl_chg_order")) _chgOrder = global.ntl_chg_order;
var _chgKind = "official";
if (variable_global_exists("ntl_chg_kind")) _chgKind = string(global.ntl_chg_kind);

ntl_log("auto", "[chg] 看门狗第 " + string(_chgTry) + " 级重试：目标=" + _chgDir + " 参数=" + _chgArgs
        + " 序数=" + string(_chgOrder) + " 类型=" + _chgKind
        + " working_directory=" + working_directory + " program_directory=" + program_directory
        + " parameter_string()=" + string(parameter_string()) + " ntl_is_root()=" + string(ntl_is_root())
        + " 帧=" + string(global.ntl_frames));

if (_chgTry == 1)
{
    // ① 官方入口 —— 但只对官方章节用：时间线/外部条目的序数不等于官方章节号，
    //    拿它调 launch_game 会切到另一个**官方**章节（更糟），所以时间线直接跳到第 ② 级。
    if (_chgKind != "official")
    {
        ntl_log("auto", "[chg] 目标不是官方章节（类型=" + _chgKind + "），跳过官方入口这一级");
    }
    else
    {
        var _chgSel = asset_get_index("obj_CHAPTER_SELECT");
        if (_chgSel < 0)
        {
            ntl_log("auto", "[chg] 本产物没有 obj_CHAPTER_SELECT，跳过官方入口这一级");
        }
        else
        {
            // 官方选择器被我们停用（instance_deactivate_object）过 —— 调它之前先激活
            try { if (instance_number(_chgSel) <= 0) instance_activate_object(_chgSel); } catch (e_chgact) { }
            var _chgInst = instance_find(_chgSel, 0);
            if (_chgInst == noone)
            {
                ntl_log("auto", "[chg] 没有可用的官方选择器实例，跳过官方入口这一级");
            }
            else
            {
                try
                {
                    _chgInst._target_chapter = _chgOrder;
                    _chgInst.launch_game(_chgOrder);
                    ntl_log("auto", "[chg] 已改走官方入口 launch_game(" + string(_chgOrder) + ")");
                }
                catch (e_chgl) { ntl_log("auto", "[chg] 官方入口调用失败: " + string(e_chgl)); }
            }
        }
    }
}
else if (_chgTry <= 4)
{
    // ② 备选前缀：第 2 级 "/"、第 3 级 ""（相对当前目录）、第 4 级 "/../"
    var _chgPfxs = ["/", "", "/../"];
    var _chgPf = _chgPfxs[_chgTry - 2];
    ntl_log("auto", "[chg] 换前缀重试 game_change(" + string(_chgPf) + _chgDir + ", " + _chgArgs + ")");
    try { game_change(_chgPf + _chgDir, _chgArgs); } catch (e_chgg) { ntl_log("auto", "[chg] 换前缀重试异常: " + string(e_chgg)); }
}
else
{
    // ③ 全部失败：给可见提示（章节选择器 toast / 面板提示），然后停手
    global.ntl_chg_pending = 0;
    global.ntl_root_toast = ntl_t("root.launch_fail");
    global.ntl_root_toast_frames = 300;
    ntl_log("root", "[toast] lang=" + string(global.ntl_lang) + " key=root.launch_fail text=" + string(global.ntl_root_toast));
    if (variable_global_exists("ntl_modmenu_msg"))
    {
        global.ntl_modmenu_msg = ntl_t("root.launch_fail");
        global.ntl_modmenu_msg_frames = 300;
        ntl_log("modmenu", "[面板提示] lang=" + string(global.ntl_lang) + " key=root.launch_fail text=" + string(global.ntl_modmenu_msg));
    }
    ntl_log("auto", "[chg] 章节启动失败：官方入口与三种前缀都试过，目标 " + _chgDir + " 仍未生效（已在原地停手）");
}

return 1;
