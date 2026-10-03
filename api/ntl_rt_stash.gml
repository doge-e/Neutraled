/// ntl_rt_stash(txt, name) —— 运行时目录当前不可写：把拼好的启动请求**挂起**，稍后自动重试
/// ★ 用户要求（2026-10-03）：这种情况不许弹提示、更不许直接放弃 —— 自动修复后继续。
/// 由 ntl_root_step.gml 的门控段每约 20 帧调用 ntl_rt_retry() 重试落盘。
var _txt = string(argument[0]);
var _name = string(argument[1]);
global.ntl_rt_pending_txt = _txt;
global.ntl_rt_pending_name = _name;
global.ntl_rt_retry_frame = global.ntl_frames;
global.ntl_ext_launching = 1;
global.ntl_ext_wait = 20;
ntl_log("ext", "[rt] 运行时目录暂不可写（存档联接可能悬空）—— 启动请求已挂起，自动修复后继续，不打扰玩家");
return 1;