/// ntl_hotreload_check() —— 检测 IDE 写入的 reload.flag，触发热重载 live 脚本
/// 让开发者在 IDE 里改脚本 → 保存 → 游戏内立即生效（无需重启游戏）
if (!variable_global_exists("ntl_hot_counter")) global.ntl_hot_counter = 0;
global.ntl_hot_counter += 1;
if (global.ntl_hot_counter < 60) return 0;      // 每秒检查一次
global.ntl_hot_counter = 0;

var _flag = program_directory + "Neutraled/reload.flag";
if (!file_exists(_flag)) return 0;

// 删除标志（避免重复触发）
try { file_delete(_flag); } catch (e) { ntl_log("hot", "[ntl] ntl_hotreload_check.gml:12 删除 reload.flag 失败: " + string(e)); }

ntl_log("hot", "检测到热重载请求 -> 重新加载 live 脚本");
var _n = ntl_live_reload();
ntl_log("hot", "热重载完成: " + string(_n) + " 个 live mod");
return 1;
