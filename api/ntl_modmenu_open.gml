/// ntl_modmenu_open() —— 打开 Mod 设置面板：切进游戏自己的二级菜单 id 51
/// ★ 面板不是自绘遮罩，而是游戏 submenu 体系里的一页
///   （参考实现：mods/deltarune_60_fps 的 submenu 50「MOD SETTINGS」）：
///   绘制 = 注入 obj_darkcontroller 的 Draw 的 ntl_modmenu_page_draw(xx, yy)
///   输入 = 注入 obj_darkcontroller 的 Step 的 ntl_modmenu_page_step()
///   ⇒ 坐标、字体（mainbig）、红心、滚动条、按键缓冲全部沿用游戏自己的那一套。
/// submenu id 选择：原版占 1-7 / 10-14 / 20-22 / 30-36，60fps 占 50 ⇒ 我们取 51。
if (!variable_global_exists("submenucoord")) global.submenucoord = array_create(64, 0);
global.submenu = 51;
global.submenucoord[51] = 0;      // 主列表光标
global.submenucoord[52] = 0;      // 章节列表光标
global.submenucoord[53] = 0;      // 语言列表光标
global.submenucoord[54] = 0;      // 模组列表光标
global.ntl_modmenu_open = 1;
global.ntl_modmenu_view = "main";
global.ntl_modmenu_msg = "";
global.ntl_modmenu_msg_frames = 0;
global.ntl_modmenu_exit = 0;
global.ntl_modmenu_mods = ntl_modmenu_mods();
global.ntl_modmenu_modcount_cache = array_length(global.ntl_modmenu_mods);
// 已加载清单每次打开面板都重读（重新部署后不用重启就能看到新数字）
global.ntl_modmenu_loaded_cache = undefined;
global.ntl_modmenu_loaded_count_cache = -2;
// 目录名 → 已加载 的匹配结果也要重算（同一张 mods.json 变了，匹配结果就变了）
if (variable_global_exists("ntl_modmenu_modinfo_cache") && !is_undefined(global.ntl_modmenu_modinfo_cache)) ds_map_destroy(global.ntl_modmenu_modinfo_cache);
global.ntl_modmenu_modinfo_cache = ds_map_create();
// 行列表缓存（分组标题/已加载标记都在里面）
global.ntl_modmenu_rows_cache = undefined;
ntl_log("menu", "[menu] 打开 Mod 设置面板（submenu=" + string(global.submenu) +
    " coord30=" + string(global.submenucoord[30]) + "）");
var _ld = ntl_modmenu_loaded_count();
ntl_log("modmenu", "[面板] 已打开（submenu 51，已加载 " + string(_ld >= 0 ? _ld : -1) +
    " / 已安装 " + string(array_length(global.ntl_modmenu_mods)) + " 个模组）");
return 1;
