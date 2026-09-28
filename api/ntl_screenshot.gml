/// ntl_screenshot(name) —— 让游戏自己保存一张截图（自动化验证用）
/// 文件落在游戏沙箱目录（game_save_id）；返回实际调用结果。
var _name = argument[0];
if (string_length(string(_name)) < 4) _name = string(_name) + ".png";
screen_save(_name);
ntl_log("auto", "ntl_screenshot -> " + string(_name) + " (sandbox: " + game_save_id + ")");
return 1;
