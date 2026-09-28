/// ntl_modmenu_loaded_count() —— 本产物**已加载**的 mod 数。
/// 返回 -1 = 本产物没有 Neutraled/mods.json（老产物 / 外部章节 exe）⇒ 调用方回退显示"已安装"数。
/// 每帧都可能被入口行调用，所以缓存（面板打开时由 ntl_modmenu_open() 清掉重读）。
if (!variable_global_exists("ntl_modmenu_loaded_count_cache")) global.ntl_modmenu_loaded_count_cache = -2;
if (global.ntl_modmenu_loaded_count_cache != -2) return global.ntl_modmenu_loaded_count_cache;

var _p = string(working_directory) + "Neutraled/mods.json";
global.ntl_modmenu_loaded_count_cache = file_exists(_p) ? array_length(ntl_modmenu_loaded()) : -1;
return global.ntl_modmenu_loaded_count_cache;
