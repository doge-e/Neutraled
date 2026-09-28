/// ntl_i18n_init() —— 初始化中英双语（**只覆盖 Neutraled 新增的内容**，游戏原版文字不动）
///
/// 语言来源（优先级）：
///   1) config.json 的 "lang": "zh" | "en" | "auto"
///   2) auto → 跟随游戏语言（global.lang）
///   3) 缺省 → zh
global.ntl_lang = "zh";
global.ntl_i18n = ds_map_create();
var _L = global.ntl_i18n;

// ==================== 中文 ====================
var _zh = ds_map_create();
// 控制台框架
ds_map_add(_zh, "console.title",      "Neutraled 控制台");
ds_map_add(_zh, "console.close",      "[F2 关闭]");
ds_map_add(_zh, "console.empty",      "输入 help 查看所有命令");
ds_map_add(_zh, "console.close",      "[F2 关闭]");
ds_map_add(_zh, "console.empty",      "输入 help 查看所有命令");
ds_map_add(_zh, "console.hint_scroll", "↑↓滚动 PgUp/PgDn翻页 Tab补全 Ctrl+↑历史");
// 命令分类
ds_map_add(_zh, "cat.info",   "信息");
ds_map_add(_zh, "cat.state",  "状态");
ds_map_add(_zh, "cat.action", "操作");
ds_map_add(_zh, "cat.debug",  "调试");
ds_map_add(_zh, "cat.auto",   "自动化");
ds_map_add(_zh, "cat.mod",    "mod 命令");
ds_map_add(_zh, "cat.power",    "强力（玩家向）");
ds_map_add(_zh, "cmd.speed.d",  "游戏速度倍率（0.25-8；speed 1 还原）");
ds_map_add(_zh, "cmd.speed.u",  "speed <倍率>");
ds_map_add(_zh, "cmd.warp.d",   "直接跳到任意房间（比 goto 章节更激进）");
ds_map_add(_zh, "cmd.warp.u",   "warp <房间名>");
ds_map_add(_zh, "cmd.hp.d",     "查看/设置玩家血量（自动探测 HP 变量）");
ds_map_add(_zh, "cmd.hp.u",     "hp [值]");
ds_map_add(_zh, "cmd.god.d",    "无敌开关（每帧把血量拉满）");
ds_map_add(_zh, "cmd.god.u",    "god");
ds_map_add(_zh, "cmd.freeze.d", "每帧把任意变量锁成任意值（HP/坐标/计时都能锁）");
ds_map_add(_zh, "cmd.freeze.u", "freeze <对象|global> <变量> [值]");
ds_map_add(_zh, "cmd.pause.d",  "冻结游戏逻辑（控制台仍可用）");
ds_map_add(_zh, "cmd.pause.u",  "pause");
ds_map_add(_zh, "cmd.resume.d", "恢复游戏逻辑");
ds_map_add(_zh, "cmd.resume.u", "resume");
ds_map_add(_zh, "cmd.step.d",   "冻结状态下单帧推进（调试神器）");
ds_map_add(_zh, "cmd.step.u",   "step [帧数]");
ds_map_add(_zh, "cmd.dump.d",   "导出当前房间快照到 Neutraled/logs/");
ds_map_add(_zh, "cmd.dump.u",   "dump [名字]");
ds_map_add(_zh, "cmd.watch.d",  "逐帧跟踪某实例变量并写日志");
ds_map_add(_zh, "cmd.watch.u",  "watch <对象> <变量> [帧数]");
ds_map_add(_zh, "cmd.crash.d",  "故意触发运行时错误（验证崩溃隔离）");
ds_map_add(_zh, "cmd.crash.u",  "crash [1|2|3]");
ds_map_add(_zh, "cmd.autorun.d","执行启动脚本（每行一条命令，# 为注释）");
ds_map_add(_zh, "cmd.autorun.u","autorun [文件]");
// 通用提示
ds_map_add(_zh, "msg.unknown",     "未识别的命令");
ds_map_add(_zh, "msg.see_help",    "输入 help 查看所有命令");
ds_map_add(_zh, "msg.mod_prefix",  "mod 命令要带前缀，如 mymod:cmd（用 cmds mod 查看）");
ds_map_add(_zh, "msg.try_eval",    "想求值表达式请用: eval ");
ds_map_add(_zh, "msg.no_match",    "没有匹配项");
ds_map_add(_zh, "msg.usage",       "用法");
ds_map_add(_zh, "msg.desc",        "说明");
ds_map_add(_zh, "msg.category",    "分类");
ds_map_add(_zh, "msg.source",      "来源");
ds_map_add(_zh, "msg.source_ntl",  "Neutraled 内置");
ds_map_add(_zh, "msg.source_mod",  "mod");
ds_map_add(_zh, "msg.cmd_count",   "共 {n} 条命令");
ds_map_add(_zh, "msg.help_tip",    "提示: help <命令> 看详细用法；mod 命令带前缀如 mymod:cmd");
// 状态
ds_map_add(_zh, "st.room",      "房间");
ds_map_add(_zh, "st.size",      "尺寸");
ds_map_add(_zh, "st.instances", "实例数");
ds_map_add(_zh, "st.fps",       "帧率");
ds_map_add(_zh, "st.frames",    "帧数");
ds_map_add(_zh, "st.uptime",    "运行时长");
ds_map_add(_zh, "st.avg_frame", "平均每帧");
ds_map_add(_zh, "st.position",  "位置");
ds_map_add(_zh, "st.facing",    "朝向");
// HUD
ds_map_add(_zh, "hud.move",          "方向键/WASD 移动");
ds_map_add(_zh, "hud.interact",      "E/Z 交互");
ds_map_add(_zh, "hud.console",       "F2 控制台");
ds_map_add(_zh, "hud.near",          "附近对象");
ds_map_add(_zh, "hud.interacted",    "已交互");
ds_map_add(_zh, "hud.last_interact", "上次交互");
ds_map_add(_zh, "hud.frame",         "帧");
// 操作反馈
ds_map_add(_zh, "act.jumping",   "跳转到第 {n} 章...");
ds_map_add(_zh, "act.map_loaded","地图已加载，方向键移动 / E 交互");
ds_map_add(_zh, "act.spawned",   "已创建");
ds_map_add(_zh, "act.destroyed", "已销毁");
ds_map_add(_zh, "act.set",       "已设置");
ds_map_add(_zh, "act.flag_set",  "flag 已设置");
ds_map_add(_zh, "act.shot",      "已截图");
ds_map_add(_zh, "act.reloading", "重载 live 脚本...");
ds_map_add(_zh, "act.alias",     "别名已定义");
ds_map_add(_zh, "act.bound",     "已绑定");
// mod
ds_map_add(_zh, "mod.cmd_reg",  "已注册控制台命令");
ds_map_add(_zh, "mod.exports",  "导出");
// 错误
ds_map_add(_zh, "err.noscript", "找不到脚本");
ds_map_add(_zh, "err.syntax",   "语法错误：意外的符号");
ds_map_add(_zh, "err.runtime",  "运行错误");
ds_map_add(_zh, "err.isolated", "异常已被隔离，游戏继续运行");
ds_map_add(_zh, "err.usage_hint","用法");
ds_map_add(_zh, "cmd.help.d", "显示命令列表 / 查询某命令用法");
ds_map_add(_zh, "cmd.help.u", "help [命令名]");
ds_map_add(_zh, "cmd.cmds.d", "按分类列出所有命令");
ds_map_add(_zh, "cmd.cmds.u", "cmds [分类]");
ds_map_add(_zh, "cmd.version.d", "显示 Neutraled 版本");

ds_map_add(_zh, "cmd.mods.d", "列出已加载的 mod（含来源）");

ds_map_add(_zh, "cmd.hooks.d", "列出所有 hook（脚本/内置/对象事件）");

ds_map_add(_zh, "cmd.api.d", "搜索游戏资源（脚本/对象/精灵/声音）");
ds_map_add(_zh, "cmd.api.u", "api <关键词>");
ds_map_add(_zh, "cmd.vars.d", "列出全局变量");
ds_map_add(_zh, "cmd.vars.u", "vars [前缀]");
ds_map_add(_zh, "cmd.profile.d", "性能统计（帧率/缓存/Hook/对象）");

ds_map_add(_zh, "cmd.timing.d", "运行时长与帧时间");

ds_map_add(_zh, "cmd.modinfo.d", "查看某个 mod 的详细信息");
ds_map_add(_zh, "cmd.modinfo.u", "modinfo <modid>");
ds_map_add(_zh, "cmd.room.d", "当前房间信息");

ds_map_add(_zh, "cmd.inst.d", "实例统计（按对象分组）");
ds_map_add(_zh, "cmd.inst.u", "inst [对象名]");
ds_map_add(_zh, "cmd.objs.d", "列出当前房间所有对象名");

ds_map_add(_zh, "cmd.flags.d", "查看游戏 flag 数组");
ds_map_add(_zh, "cmd.flags.u", "flags [起始] [数量]");
ds_map_add(_zh, "cmd.player.d", "玩家位置与朝向");

ds_map_add(_zh, "cmd.maps.d", "列出可用的 Kristal 地图");
ds_map_add(_zh, "cmd.maps.u", "maps [关键词]");
ds_map_add(_zh, "cmd.saves.d", "列出存档快照");

ds_map_add(_zh, "cmd.cache.d", "部署缓存状态");

ds_map_add(_zh, "cmd.world.d", "Kristal world 状态");

ds_map_add(_zh, "cmd.goto.d", "跳转到指定章节");
ds_map_add(_zh, "cmd.goto.u", "goto <1-7>");
ds_map_add(_zh, "cmd.loadmap.d", "加载地图并放置玩家");
ds_map_add(_zh, "cmd.loadmap.u", "loadmap <地图名>");
ds_map_add(_zh, "cmd.spawn.d", "创建对象实例");
ds_map_add(_zh, "cmd.spawn.u", "spawn <对象> [x] [y]");
ds_map_add(_zh, "cmd.destroy.d", "销毁某对象的所有实例");
ds_map_add(_zh, "cmd.destroy.u", "destroy <对象>");
ds_map_add(_zh, "cmd.setvar.d", "改任意实例的任意变量");
ds_map_add(_zh, "cmd.setvar.u", "setvar <对象> <变量> <值>");
ds_map_add(_zh, "cmd.setflag.d", "设置游戏 flag");
ds_map_add(_zh, "cmd.setflag.u", "setflag <编号> <值>");
ds_map_add(_zh, "cmd.screenshot.d", "截图到游戏存档目录");
ds_map_add(_zh, "cmd.screenshot.u", "screenshot [名字]");
ds_map_add(_zh, "cmd.reload.d", "热重载 live 脚本");

ds_map_add(_zh, "cmd.clear.d", "清空控制台历史");

ds_map_add(_zh, "cmd.quit.d", "关闭控制台");

ds_map_add(_zh, "cmd.eval.d", "求值 Lua 表达式");
ds_map_add(_zh, "cmd.eval.u", "eval <表达式>");
ds_map_add(_zh, "cmd.exec.d", "执行一段 Lua 代码");
ds_map_add(_zh, "cmd.exec.u", "exec <代码>");
ds_map_add(_zh, "cmd.err.d", "显示/清空最近的错误");
ds_map_add(_zh, "cmd.err.u", "err [clear]");
ds_map_add(_zh, "cmd.ctx.d", "当前执行上下文");

ds_map_add(_zh, "cmd.trace.d", "切换 Lua 语句级跟踪");
ds_map_add(_zh, "cmd.trace.u", "trace on");
ds_map_add(_zh, "cmd.log.d", "写一行到日志");
ds_map_add(_zh, "cmd.log.u", "log <文字>");
ds_map_add(_zh, "cmd.budget.d", "查看/设置 Lua 执行预算");
ds_map_add(_zh, "cmd.budget.u", "budget [数值]");
ds_map_add(_zh, "cmd.lang.d", "切换语言（中/英/自动）");
ds_map_add(_zh, "cmd.lang.u", "lang zh");
ds_map_add(_zh, "cmd.run.d", "执行命令脚本文件");
ds_map_add(_zh, "cmd.run.u", "run <脚本路径>");
ds_map_add(_zh, "cmd.batch.d", "批量执行脚本（同 run）");
ds_map_add(_zh, "cmd.batch.u", "batch <脚本路径>");
ds_map_add(_zh, "cmd.alias.d", "定义命令别名");
ds_map_add(_zh, "cmd.alias.u", "alias <别名> <命令>");
ds_map_add(_zh, "cmd.bind.d", "把命令绑定到按键");
ds_map_add(_zh, "cmd.bind.u", "bind <键名> <命令>");
ds_map_add(_zh, "cmd.macro.d", "录制/回放命令序列");
ds_map_add(_zh, "cmd.macro.u", "macro start");
ds_map_add(_zh, "cmd.sleep.d", "等待 N 帧后继续脚本");
ds_map_add(_zh, "cmd.sleep.u", "sleep <帧数>");
ds_map_add(_zh, "cmd.loop.d", "重复执行命令 N 次");
ds_map_add(_zh, "cmd.loop.u", "loop <次数> <命令>");
ds_map_add(_zh, "cmd.filter.d", "日志过滤（只看某类输出）");
ds_map_add(_zh, "cmd.filter.u", "filter all|error|warn|cmd");
ds_map_add(_zh, "cmd.hist.d",   "查看/管理命令历史");
ds_map_add(_zh, "cmd.hist.u",   "hist [clear|save]");
ds_map_add(_zh, "cmd.save.d",   "把控制台输出保存到文件");
ds_map_add(_zh, "cmd.save.u",   "save [名字]");
ds_map_add(_zh, "cmd.res.d",    "资源监控报告（动态精灵等；res untrack 注销一条）");
ds_map_add(_zh, "cmd.res.u",    "res [untrack <类别> <id>]");
ds_map_add(_zh, "cmd.whatis.d", "查询一个名字：游戏资源 / mod 命名空间 / 内置 API");
ds_map_add(_zh, "cmd.whatis.u", "whatis <名字>");
ds_map_add(_zh, "cmd.style.d",  "控制台外观（行数/透明度）");
ds_map_add(_zh, "cmd.style.u",  "style lines <5-30> | alpha <0.3-1.0>");
ds_map_add(_zh, "cmd.timeit.d", "执行命令并显示耗时");
ds_map_add(_zh, "cmd.timeit.u", "timeit <命令>");
ds_map_add(_L, "zh", _zh);

// ==================== English ====================
var _en = ds_map_create();
ds_map_add(_en, "console.title",      "Neutraled Console");
ds_map_add(_en, "console.close",      "[F2 close]");
ds_map_add(_en, "console.empty",      "Type help to list all commands");
ds_map_add(_en, "console.close",      "[F2 close]");
ds_map_add(_en, "console.empty",      "Type help to list all commands");
ds_map_add(_en, "console.hint_scroll", "Up/Dn scroll PgUp/PgDn page Tab complete Ctrl+Up history");
ds_map_add(_en, "cat.info",   "Info");
ds_map_add(_en, "cat.state",  "State");
ds_map_add(_en, "cat.action", "Action");
ds_map_add(_en, "cat.debug",  "Debug");
ds_map_add(_en, "cat.auto",   "Automation");
ds_map_add(_en, "cat.mod",    "Mod commands");
ds_map_add(_en, "cat.power",    "Power (player)");
ds_map_add(_en, "cmd.speed.d",  "Game speed multiplier (0.25-8; speed 1 restores)");
ds_map_add(_en, "cmd.speed.u",  "speed <multiplier>");
ds_map_add(_en, "cmd.warp.d",   "Jump straight to any room (more aggressive than goto)");
ds_map_add(_en, "cmd.warp.u",   "warp <room name>");
ds_map_add(_en, "cmd.hp.d",     "Show/set player HP (auto-detects the HP variable)");
ds_map_add(_en, "cmd.hp.u",     "hp [value]");
ds_map_add(_en, "cmd.god.d",    "Invincibility toggle (refills HP every frame)");
ds_map_add(_en, "cmd.god.u",    "god");
ds_map_add(_en, "cmd.freeze.d", "Lock any variable to any value every frame (HP / position / timer)");
ds_map_add(_en, "cmd.freeze.u", "freeze <object|global> <variable> [value]");
ds_map_add(_en, "cmd.pause.d",  "Freeze game logic (console stays usable)");
ds_map_add(_en, "cmd.pause.u",  "pause");
ds_map_add(_en, "cmd.resume.d", "Resume game logic");
ds_map_add(_en, "cmd.resume.u", "resume");
ds_map_add(_en, "cmd.step.d",   "Advance N frames while frozen (frame stepping)");
ds_map_add(_en, "cmd.step.u",   "step [frames]");
ds_map_add(_en, "cmd.dump.d",   "Export a room snapshot to Neutraled/logs/");
ds_map_add(_en, "cmd.dump.u",   "dump [name]");
ds_map_add(_en, "cmd.watch.d",  "Log an instance variable every frame");
ds_map_add(_en, "cmd.watch.u",  "watch <object> <variable> [frames]");
ds_map_add(_en, "cmd.crash.d",  "Deliberately raise a runtime error (crash isolation test)");
ds_map_add(_en, "cmd.crash.u",  "crash [1|2|3]");
ds_map_add(_en, "cmd.autorun.d","Run a boot script (one command per line, # comments)");
ds_map_add(_en, "cmd.autorun.u","autorun [file]");
ds_map_add(_en, "msg.unknown",     "Unknown command");
ds_map_add(_en, "msg.see_help",    "Type help to list all commands");
ds_map_add(_en, "msg.mod_prefix",  "Mod commands need a prefix, e.g. mymod:cmd (see: cmds mod)");
ds_map_add(_en, "msg.try_eval",    "To evaluate an expression use: eval ");
ds_map_add(_en, "msg.no_match",    "No match");
ds_map_add(_en, "msg.usage",       "Usage");
ds_map_add(_en, "msg.desc",        "Description");
ds_map_add(_en, "msg.category",    "Category");
ds_map_add(_en, "msg.source",      "Source");
ds_map_add(_en, "msg.source_ntl",  "Neutraled built-in");
ds_map_add(_en, "msg.source_mod",  "mod");
ds_map_add(_en, "msg.cmd_count",   "{n} commands");
ds_map_add(_en, "msg.help_tip",    "Tip: help <cmd> for details; mod commands use a prefix like mymod:cmd");
ds_map_add(_en, "st.room",      "Room");
ds_map_add(_en, "st.size",      "Size");
ds_map_add(_en, "st.instances", "Instances");
ds_map_add(_en, "st.fps",       "FPS");
ds_map_add(_en, "st.frames",    "Frames");
ds_map_add(_en, "st.uptime",    "Uptime");
ds_map_add(_en, "st.avg_frame", "Avg frame");
ds_map_add(_en, "st.position",  "Position");
ds_map_add(_en, "st.facing",    "Facing");
ds_map_add(_en, "hud.move",          "Arrows/WASD to move");
ds_map_add(_en, "hud.interact",      "E/Z to interact");
ds_map_add(_en, "hud.console",       "F2 console");
ds_map_add(_en, "hud.near",          "Nearby");
ds_map_add(_en, "hud.interacted",    "Interacted");
ds_map_add(_en, "hud.last_interact", "Last interact");
ds_map_add(_en, "hud.frame",         "Frame");
ds_map_add(_en, "act.jumping",   "Jumping to chapter {n}...");
ds_map_add(_en, "act.map_loaded","Map loaded - arrows to move, E to interact");
ds_map_add(_en, "act.spawned",   "Spawned");
ds_map_add(_en, "act.destroyed", "Destroyed");
ds_map_add(_en, "act.set",       "Set");
ds_map_add(_en, "act.flag_set",  "Flag set");
ds_map_add(_en, "act.shot",      "Screenshot saved");
ds_map_add(_en, "act.reloading", "Reloading live scripts...");
ds_map_add(_en, "act.alias",     "Alias defined");
ds_map_add(_en, "act.bound",     "Bound");
ds_map_add(_en, "mod.cmd_reg",  "Registered console command");
ds_map_add(_en, "mod.exports",  "exports");
ds_map_add(_en, "err.noscript", "Script not found");
ds_map_add(_en, "err.syntax",   "syntax error: unexpected symbol");
ds_map_add(_en, "err.runtime",  "Runtime error");
ds_map_add(_en, "err.isolated", "Exception isolated, game continues");
ds_map_add(_en, "err.usage_hint","Usage");
ds_map_add(_en, "cmd.help.d", "List commands / show usage of one");
ds_map_add(_en, "cmd.help.u", "help [command]");
ds_map_add(_en, "cmd.cmds.d", "List all commands by category");
ds_map_add(_en, "cmd.cmds.u", "cmds [category]");
ds_map_add(_en, "cmd.version.d", "Show Neutraled version");

ds_map_add(_en, "cmd.mods.d", "List loaded mods");

ds_map_add(_en, "cmd.hooks.d", "List all hooks (function/builtin/object)");

ds_map_add(_en, "cmd.api.d", "Search game assets");
ds_map_add(_en, "cmd.api.u", "api <keyword>");
ds_map_add(_en, "cmd.vars.d", "List global variables");
ds_map_add(_en, "cmd.vars.u", "vars [prefix]");
ds_map_add(_en, "cmd.profile.d", "Performance stats");

ds_map_add(_en, "cmd.timing.d", "Uptime and frame time");

ds_map_add(_en, "cmd.modinfo.d", "Show details of a mod");
ds_map_add(_en, "cmd.modinfo.u", "modinfo <modid>");
ds_map_add(_en, "cmd.room.d", "Current room info");

ds_map_add(_en, "cmd.inst.d", "Instance counts by object");
ds_map_add(_en, "cmd.inst.u", "inst [object]");
ds_map_add(_en, "cmd.objs.d", "List object names in current room");

ds_map_add(_en, "cmd.flags.d", "Show game flag array");
ds_map_add(_en, "cmd.flags.u", "flags [start] [count]");
ds_map_add(_en, "cmd.player.d", "Player position and facing");

ds_map_add(_en, "cmd.maps.d", "List available Kristal maps");
ds_map_add(_en, "cmd.maps.u", "maps [keyword]");
ds_map_add(_en, "cmd.saves.d", "List save snapshots");

ds_map_add(_en, "cmd.cache.d", "Deploy cache status");

ds_map_add(_en, "cmd.world.d", "Kristal world status");

ds_map_add(_en, "cmd.goto.d", "Jump to a chapter");
ds_map_add(_en, "cmd.goto.u", "goto <1-7>");
ds_map_add(_en, "cmd.loadmap.d", "Load a map and place player");
ds_map_add(_en, "cmd.loadmap.u", "loadmap <map>");
ds_map_add(_en, "cmd.spawn.d", "Create an object instance");
ds_map_add(_en, "cmd.spawn.u", "spawn <object> [x] [y]");
ds_map_add(_en, "cmd.destroy.d", "Destroy all instances of an object");
ds_map_add(_en, "cmd.destroy.u", "destroy <object>");
ds_map_add(_en, "cmd.setvar.d", "Set any variable on any instance");
ds_map_add(_en, "cmd.setvar.u", "setvar <object> <var> <value>");
ds_map_add(_en, "cmd.setflag.d", "Set a game flag");
ds_map_add(_en, "cmd.setflag.u", "setflag <index> <value>");
ds_map_add(_en, "cmd.screenshot.d", "Save a screenshot");
ds_map_add(_en, "cmd.screenshot.u", "screenshot [name]");
ds_map_add(_en, "cmd.reload.d", "Hot-reload live scripts");

ds_map_add(_en, "cmd.clear.d", "Clear console history");

ds_map_add(_en, "cmd.quit.d", "Close the console");

ds_map_add(_en, "cmd.eval.d", "Evaluate a Lua expression");
ds_map_add(_en, "cmd.eval.u", "eval <expr>");
ds_map_add(_en, "cmd.exec.d", "Execute Lua code");
ds_map_add(_en, "cmd.exec.u", "exec <code>");
ds_map_add(_en, "cmd.err.d", "Show/clear recent errors");
ds_map_add(_en, "cmd.err.u", "err [clear]");
ds_map_add(_en, "cmd.ctx.d", "Current execution context");

ds_map_add(_en, "cmd.trace.d", "Toggle Lua statement tracing");
ds_map_add(_en, "cmd.trace.u", "trace on");
ds_map_add(_en, "cmd.log.d", "Write a line to the log");
ds_map_add(_en, "cmd.log.u", "log <text>");
ds_map_add(_en, "cmd.budget.d", "Show/set Lua execution budget");
ds_map_add(_en, "cmd.budget.u", "budget [value]");
ds_map_add(_en, "cmd.lang.d", "Switch language (zh/en/auto)");
ds_map_add(_en, "cmd.lang.u", "lang zh");
ds_map_add(_en, "cmd.run.d", "Run a command script file");
ds_map_add(_en, "cmd.run.u", "run <script>");
ds_map_add(_en, "cmd.batch.d", "Run a script in batch");
ds_map_add(_en, "cmd.batch.u", "batch <script>");
ds_map_add(_en, "cmd.alias.d", "Define a command alias");
ds_map_add(_en, "cmd.alias.u", "alias <name> <command>");
ds_map_add(_en, "cmd.bind.d", "Bind a command to a key");
ds_map_add(_en, "cmd.bind.u", "bind <key> <command>");
ds_map_add(_en, "cmd.macro.d", "Record/replay command sequences");
ds_map_add(_en, "cmd.macro.u", "macro start");
ds_map_add(_en, "cmd.sleep.d", "Wait N frames in a script");
ds_map_add(_en, "cmd.sleep.u", "sleep <frames>");
ds_map_add(_en, "cmd.loop.d", "Repeat a command N times");
ds_map_add(_en, "cmd.loop.u", "loop <count> <command>");
ds_map_add(_en, "cmd.filter.d", "Filter console output");
ds_map_add(_en, "cmd.filter.u", "filter all|error|warn|cmd");
ds_map_add(_en, "cmd.hist.d",   "View/manage command history");
ds_map_add(_en, "cmd.hist.u",   "hist [clear|save]");
ds_map_add(_en, "cmd.save.d",   "Save console output to file");
ds_map_add(_en, "cmd.save.u",   "save [name]");
ds_map_add(_en, "cmd.res.d",    "Resource monitor report (dynamic sprites etc.; res untrack to unregister one)");
ds_map_add(_en, "cmd.res.u",    "res [untrack <kind> <id>]");
ds_map_add(_en, "cmd.whatis.d", "Look up a name: game asset / mod namespace / built-in API");
ds_map_add(_en, "cmd.whatis.u", "whatis <name>");
ds_map_add(_en, "cmd.style.d",  "Console appearance (lines/alpha)");
ds_map_add(_en, "cmd.style.u",  "style lines <5-30> | alpha <0.3-1.0>");
ds_map_add(_en, "cmd.timeit.d", "Run a command and show elapsed time");
ds_map_add(_en, "cmd.timeit.u", "timeit <command>");
ntl_i18n_out(_zh, _en);   // ★ 第二批输出文案（控制台输出行/HUD/错误提示），见 ntl_i18n_out.gml
ds_map_add(_L, "en", _en);

// ---------- 决定当前语言 ----------
var _want = "auto";
if (variable_global_exists("ntl_cfg"))
{
    var _c = ds_map_find_value(global.ntl_cfg, "lang");
    if (_c != undefined && string(_c) != "") _want = string_lower(string(_c));
}
if (_want == "auto")
{
    var _gl = "en";
    if (variable_global_exists("lang")) _gl = string_lower(string(global.lang));
    _want = (string_pos("zh", _gl) > 0 || string_pos("cn", _gl) > 0) ? "zh" : "en";
}
// 内置语言（zh/en）直接用硬编码表；其它语言码（ja/ko/ru/es/de/fr/zh-tw…）尝试外部语言包，
// 语言包缺失才回退中文 —— 这样「8 种界面语言」在游戏内也成立。
if (ds_map_exists(_L, _want))
{
    global.ntl_lang = _want;
}
else if (ntl_lang_pack(_want) == 1)
{
    global.ntl_lang = _want;
}
else
{
    global.ntl_lang = "zh";
}
ntl_log("i18n", "语言: " + global.ntl_lang + "（配置 " + _want + "）");
return 1;