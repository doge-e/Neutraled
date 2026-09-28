/// ntl_modmenu_deploy() —— 写重新部署请求，然后退出游戏交给守候进程
/// 本运行时**没有任何启动进程的内置函数**（execute_program / execute_shell / os_start_process / url_open
/// 在 data.win 的字符串池里都不存在），所以只能「写请求文件 + game_end()」，由 builder --watch-external 消费。
/// ★ 只写 ASCII：GM 的 file_text_write_string 按 GBK 落盘，中文会乱码。
var _p = program_directory + "Neutraled/launch-request.json";
var _ok = false;
try
{
    var _f = file_text_open_write(_p);
    file_text_write_string(_f, "{\"action\":\"deploy\",\"name\":\"panel\"}");
    file_text_close(_f);
    _ok = true;
}
catch (e) { ntl_log("modmenu", "[面板] 写部署请求失败: " + string(e)); }

if (_ok)
{
    global.ntl_modmenu_msg = ntl_t("menu.deploy_req");
    global.ntl_modmenu_msg_frames = 150;
    global.ntl_modmenu_exit = 90;      // 30fps ⇒ 约 3 秒后 game_end()
    ntl_log("modmenu", "[面板] 已写部署请求: " + _p);
}
else
{
    global.ntl_modmenu_msg = ntl_t("menu.deploy_fail");
    global.ntl_modmenu_msg_frames = 150;
}
return _ok ? 1 : 0;
