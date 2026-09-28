/// ntl_modmenu_close() —— 关闭面板：回到游戏的设置菜单页（submenu 30）
if (variable_global_exists("submenu") && global.submenu == 51) global.submenu = 30;
global.ntl_modmenu_open = 0;
global.ntl_modmenu_view = "main";
global.ntl_modmenu_msg = "";
global.ntl_modmenu_msg_frames = 0;
global.ntl_modmenu_exit = 0;
ntl_log("modmenu", "[面板] 已关闭");
return 0;
