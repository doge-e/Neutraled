/// ntl_modmenu_step() —— 面板的计时器与状态同步（由 api/events/Step_1.gml 每帧调用）
/// ★ 面板自己的按键**不在这里**：面板已经是游戏 submenu 体系里的一页（id 51），
///   输入由注入 obj_darkcontroller 的 ntl_modmenu_page_step() 处理（与游戏同帧、同一套按键缓冲）。
/// 这里只做三件事：消息倒计时、部署退出倒计时、把 global.ntl_modmenu_open 与真实的 submenu 对齐。
if (!variable_global_exists("ntl_modmenu_open")) global.ntl_modmenu_open = 0;
if (!variable_global_exists("ntl_modmenu_msg")) global.ntl_modmenu_msg = "";
if (!variable_global_exists("ntl_modmenu_msg_frames")) global.ntl_modmenu_msg_frames = 0;
if (!variable_global_exists("ntl_modmenu_exit")) global.ntl_modmenu_exit = 0;
if (!variable_global_exists("ntl_modmenu_view")) global.ntl_modmenu_view = "main";

if (global.ntl_modmenu_msg_frames > 0)
{
    global.ntl_modmenu_msg_frames -= 1;
    if (global.ntl_modmenu_msg_frames <= 0) global.ntl_modmenu_msg = "";
}

if (global.ntl_modmenu_exit > 0)
{
    global.ntl_modmenu_exit -= 1;
    if (global.ntl_modmenu_exit <= 0)
    {
        ntl_log("modmenu", "[面板] 退出游戏，交给守候进程重新部署");
        game_end();
    }
}

// 与游戏状态对齐：面板 = submenu 51。玩家按 X 回菜单、或菜单被整体关掉时，
// 标志必须跟着落回 0，否则下一帧会以为面板还开着。
var _in = (variable_global_exists("submenu") && global.submenu == 51);
if (_in)
{
    global.ntl_modmenu_open = 1;
}
else if (global.ntl_modmenu_open == 1)
{
    global.ntl_modmenu_open = 0;
    global.ntl_modmenu_view = "main";
}
return 0;
