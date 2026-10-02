// ============================================================
// Neutraled 引导 —— builder 会把它 prepend 到 obj_initializer2 Create 最前
// 职责：防御性全局初始化 → 运行时状态 → 控制器创建 → mod 清单 → 入口
// 约束：老式脚本；每个函数都是独立同名脚本资源；禁用 struct/constructor
// ============================================================

// --- 路径与版本（builder 可替换占位符）---
global.ntl_log_path = "Neutraled/dr-api.log";
// ★ GM 不会自动建目录：目录一旦不存在，file_text_open_append 会**静默失败**，
//   整场游戏一个字都不写（实测踩过：%LOCALAPPDATA%\DELTARUNE\Neutraled\ 被清掉后，
//   游戏跑得好好的却完全没有日志，排查了很久）。这里先确保目录存在。
ntl_ensure_dir(global.ntl_log_path);
global.ntl_version = "1.0.1";
global.ntl_live_api = "1.2.0";

// 运行时脚本解释器的各个函数都是独立同名脚本资源（UTMT 编译器要求），无需预加载。

// --- 关键全局安全初始化（防 mod 基底早期读取未定义变量崩溃）---
if (!variable_global_exists("plot")) global.plot = 0;
if (!variable_global_exists("fighting")) global.fighting = 0;
if (!variable_global_exists("truename")) global.truename = "";
if (!variable_global_exists("time")) global.time = 0;
if (!variable_global_exists("chapter")) global.chapter = 4;
if (!variable_global_exists("gold")) global.gold = 0;
if (!variable_global_exists("xp")) global.xp = 0;
if (!variable_global_exists("lv")) global.lv = 1;
if (!variable_global_exists("encounterno")) global.encounterno = 0;
if (!variable_global_exists("specialbattle")) global.specialbattle = 0;
if (!variable_global_exists("ambush")) global.ambush = 0;
if (!variable_global_exists("tension")) global.tension = 0;
if (!variable_global_exists("charselect")) global.charselect = 0;
if (!variable_global_exists("lang")) global.lang = "en";
if (!variable_global_exists("is_console")) global.is_console = false;
if (!variable_global_exists("names")) global.names = 0;

// --- 语言系统安全初始化 ---
if (!variable_global_exists("lang_map")) global.lang_map = ds_map_create();
if (!variable_global_exists("lang_missing_map")) global.lang_missing_map = ds_map_create();
if (!variable_global_exists("font_map")) global.font_map = ds_map_create();
if (!variable_global_exists("chemg_sprite_map")) global.chemg_sprite_map = ds_map_create();
if (!variable_global_exists("chemg_sound_map")) global.chemg_sound_map = ds_map_create();
if (!variable_global_exists("chemg_font")) global.chemg_font = -1;
if (!variable_global_exists("chemg_last_get_font")) global.chemg_last_get_font = -1;

// font_map 预填（真实存在的字体资源；真正的语言初始化之后会重建覆盖）
if (ds_map_size(global.font_map) <= 0)
{
    var _fm = asset_get_index("fnt_main");
    var _fb = asset_get_index("fnt_mainbig");
    if (_fm != -1) ds_map_add(global.font_map, "main", _fm);
    if (_fb != -1) ds_map_add(global.font_map, "mainbig", _fb);
}

// --- Neutraled 运行时状态 ---
if (!variable_global_exists("ntl_hooks")) global.ntl_hooks = ds_map_create();
if (!variable_global_exists("ntl_mods")) global.ntl_mods = ds_list_create();
if (!variable_global_exists("ntl_mod_ids")) global.ntl_mod_ids = ds_map_create();
if (!variable_global_exists("ntl_lang_overrides")) global.ntl_lang_overrides = ds_map_create();
if (!variable_global_exists("ntl_frames")) global.ntl_frames = 0;
if (!variable_global_exists("ntl_ready")) global.ntl_ready = false;
if (!variable_global_exists("ntl_console_open")) global.ntl_console_open = false;
if (!variable_global_exists("ntl_room_last")) global.ntl_room_last = room;
if (!variable_global_exists("ntl_fighting_last")) global.ntl_fighting_last = 0;

ntl_log("core", "Neutraled API v" + global.ntl_version + " init (room=" + string(room) + ")");

// 一次性诊断：本产物 Neutraled/mods.json 的读取/解析链路（用户 m23281「显示 0 mod 加载」）
//   只打一条日志、不影响逻辑；定性后（1.0.1）可删。
try { ntl_json_diag(); } catch (e) { ntl_log("jdiag", "[jdiag] 诊断脚本异常（已忽略）"); }

// --- 创建常驻控制器 ---
var _core = asset_get_index("obj_ntl_core");
if (_core != -1)
{
    if (!instance_exists(_core))
    {
        instance_create(0, 0, _core);
        ntl_log("core", "controller created");
    }
}
else
{
    ntl_log("core", "ERROR: obj_ntl_core missing");
}

// --- mod 清单（builder 在部署时内联注册调用）---
//__NTL_MANIFEST__

// ★ F5 接线 ntl_mod_data_load：恢复 Neutraled/mods-shared.json（ntl_mod_data_save 写的那份）。
//   必须在 ntl_run_mods() 之前 —— mod 在 on_init 里就要读得到共享数据。
try { ntl_mod_data_load(); }
catch (e) { ntl_log("mod", "[共享数据] 恢复失败: " + string(e)); }

// --- 运行 mod 入口 ---
ntl_run_mods();

// --- 配置（config.json）---
ntl_config_load();
global.ntl_autoskip_frame = 0;
global.ntl_autoskip_done = 0;
global.ntl_skip_frame = 0;
global.ntl_skip_room = -1;

// --- 命名空间（依赖 mod 的接口映射）—— 必须先于 live 脚本加载 ---
ntl_ns_load();

// --- 函数 hook（编译期包装的脚本会调用 ntl_hook_run）---
ntl_hook_init();

// --- 性能基准（写入 [perf] 日志，供对比 GML 原生与 Lua 解释器开销）---
if (file_exists(program_directory + "Neutraled/perf.flag"))
{
    try { ntl_perf_test(); } catch (e) { ntl_log("perf", "基准失败: " + string(e)); }
}

// --- 运行时 mod（live 目录：改文件即生效，无需重新部署 data.win）---
ntl_live_init();

// --- 章节注册表（顶层章节界面用）---
ntl_root_data();

// --- 初始化完成事件 ---
ntl_emit("on_dr_init", []);
return 0;
