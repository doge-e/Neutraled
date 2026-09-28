/// ntl_config_paths() —— Neutraled/config.json 的候选路径数组（存档区在前、游戏根在后）
/// ★ 为什么是两个路径（2026-09-27 真机探针取证，别再改回单路径）：
///   · 章节产物（chapterN_windows/data.win）读写的是游戏根 <program_directory>Neutraled/config.json；
///   · root 产物（游戏自带启动器 data.win）带 GameMaker 文件沙箱：
///       写 bundle 路径 → 实际落到存档区 <game_save_id>Neutraled/config.json；
///       读 bundle 路径 → 只要存档区存在同名文件就被它遮蔽（逐文件遮蔽：
///       同一次启动读 Neutraled/mods/ 仍命中真实 bundle，因为存档区没有 mods 目录）。
///   实测：root 阶段 program_directory 明明是游戏根，读 config.json 却拿到存档区那份的内容。
/// ⇒ 两个阶段的读路径天然不同，所以：**两处都读**（后者 = 游戏根优先）、**两处都写**。
var _a = [];
var _save = string(game_save_id) + "Neutraled/config.json";
array_push(_a, _save);
var _bundle = program_directory + "Neutraled/config.json";
if (_bundle != _save) array_push(_a, _bundle);
return _a;
