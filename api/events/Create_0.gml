// obj_ntl_core :: Create —— 控制器状态初始化
persistent = true;

global.ntl_frames = 0;
global.ntl_ready = false;
global.ntl_console_open = false;
global.ntl_console_f2_held = false;
global.ntl_console_input = "";
if (!variable_global_exists("ntl_console_lines")) global.ntl_console_lines = ds_list_create();
if (!variable_global_exists("ntl_console_cmds")) global.ntl_console_cmds = ds_map_create();
global.ntl_room_last = room;
global.ntl_fighting_last = 0;
global.ntl_turn_last = 0;

// 控制台状态（滚动 / 历史 / 补全）
if (!variable_global_exists("ntl_console_scroll")) global.ntl_console_scroll = 0;
if (!variable_global_exists("ntl_console_autoscroll")) global.ntl_console_autoscroll = 1;
if (!variable_global_exists("ntl_console_history")) global.ntl_console_history = [];
if (!variable_global_exists("ntl_console_hist_idx")) global.ntl_console_hist_idx = 0;
try { ntl_console_history_load(); } catch (e) { ntl_log("console", "[ntl] events/Create_0.gml:20 恢复命令历史失败: " + string(e)); }   // 恢复上次的命令历史

// 配置**必须先于** i18n 加载：控制台界面语言取自 config.json 的 lang。
// 实测顺序颠倒时日志是「[i18n] 语言: en（配置 en）」在前、「[cfg] 解析字段: … lang=zh」在后
// → 配置里的 zh 被忽略，控制台永远是英文（章节选择器显示中文是因为它不走 i18n）。
try { if (!variable_global_exists("ntl_cfg")) ntl_config_load(); }
catch (e) { ntl_log("cfg", "[ntl] events/Create_0.gml 预加载配置失败: " + string(e)); }

// 中英双语（只覆盖 Neutraled 新增的内容）
if (!variable_global_exists("ntl_i18n")) ntl_i18n_init();

// 内置控制台命令（43 条，分 5 类；mod 命令带 modid: 前缀）
if (!variable_global_exists("ntl_console_cmds")) global.ntl_console_cmds = ds_map_create();
ntl_console_cmds_builtin();

// 启动脚本（Neutraled/autorun.console，每行一条命令）—— 给玩家/开发者做可复现场景
ntl_console_autorun("");

// 控制台启动消息（让玩家打开时不是空的）
ntl_console_log("Neutraled API " + global.ntl_version + "  " + ntl_t("console.empty"));
ntl_console_log("");

ntl_log("core", "controller ready (room=" + string(room) + ")");
return 0;
