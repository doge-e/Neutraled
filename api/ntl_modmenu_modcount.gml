/// ntl_modmenu_modcount() —— 已装模组数量（设置菜单第 6 行右列显示用）
/// 每帧都会调，所以缓存一次；面板打开时会刷新（见 ntl_modmenu_open.gml）。
if (!variable_global_exists("ntl_modmenu_modcount_cache")) global.ntl_modmenu_modcount_cache = -1;
if (global.ntl_modmenu_modcount_cache < 0) global.ntl_modmenu_modcount_cache = array_length(ntl_modmenu_mods());
return global.ntl_modmenu_modcount_cache;
