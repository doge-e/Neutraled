/// ntl_console_cmds_builtin() —— 注册所有内置命令（ntl 的，无前缀）
///
/// 分类：info（信息）/ state（状态）/ action（操作）/ debug（调试）/ auto（自动化）
///
/// ★ 范围解析：内置命令直接用名字，mod 命令必须带 "modid:" 前缀
var _R = function(_n, _d, _u, _c) {
    // _d / _u 是 i18n key，注册时按当前语言解析
    var _desc = ntl_t(_d);
    var _use = ntl_t(_u);
    if (_use == _u) _use = "";
    ntl_console_register(_n, _desc, -1, _use, _c, "builtin");
};

// ==================== 信息类 ====================
_R("help", "cmd.help.d", "cmd.help.u", "info");
_R("cmds", "cmd.cmds.d", "cmd.cmds.u", "info");
_R("version", "cmd.version.d", "cmd.version.u", "info");
_R("mods", "cmd.mods.d", "cmd.mods.u", "info");
_R("hooks", "cmd.hooks.d", "cmd.hooks.u", "info");
_R("api", "cmd.api.d", "cmd.api.u", "info");
_R("vars", "cmd.vars.d", "cmd.vars.u", "info");
_R("profile", "cmd.profile.d", "cmd.profile.u", "info");
_R("timing", "cmd.timing.d", "cmd.timing.u", "info");
_R("modinfo", "cmd.modinfo.d", "cmd.modinfo.u", "info");
// ★ F5 接线：把 ntl_res_report / ntl_console_help_lookup 变成可发现的命令
_R("res", "cmd.res.d", "cmd.res.u", "info");
_R("whatis", "cmd.whatis.d", "cmd.whatis.u", "info");
// ★ 缺陷 2 修复：save / style / timeit 早就在 ntl_console_exec.gml:104-106 里分派了，
//   但注册表没登记 ⇒ help / cmds / Tab 补全里看不见（用户根本不知道它们存在）。这里补齐。
_R("save", "cmd.save.d", "cmd.save.u", "info");
_R("style", "cmd.style.d", "cmd.style.u", "info");
_R("timeit", "cmd.timeit.d", "cmd.timeit.u", "debug");

// ==================== 状态类 ====================
_R("room", "cmd.room.d", "cmd.room.u", "state");
_R("inst", "cmd.inst.d", "cmd.inst.u", "state");
_R("objs", "cmd.objs.d", "cmd.objs.u", "state");
_R("flags", "cmd.flags.d", "cmd.flags.u", "state");
_R("player", "cmd.player.d", "cmd.player.u", "state");
_R("maps", "cmd.maps.d", "cmd.maps.u", "state");
_R("saves", "cmd.saves.d", "cmd.saves.u", "state");
_R("cache", "cmd.cache.d", "cmd.cache.u", "state");
_R("world", "cmd.world.d", "cmd.world.u", "state");

// ==================== 操作类 ====================
// ★ 破坏性命令的干跑开关 —— 放在操作类第一位，用户最先看到
_R("dry", "cmd.dry.d", "cmd.dry.u", "action");
_R("goto", "cmd.goto.d", "cmd.goto.u", "action");
_R("loadmap", "cmd.loadmap.d", "cmd.loadmap.u", "action");
_R("spawn", "cmd.spawn.d", "cmd.spawn.u", "action");
_R("destroy", "cmd.destroy.d", "cmd.destroy.u", "action");
_R("setvar", "cmd.setvar.d", "cmd.setvar.u", "action");
_R("setflag", "cmd.setflag.d", "cmd.setflag.u", "action");
_R("screenshot", "cmd.screenshot.d", "cmd.screenshot.u", "action");
_R("reload", "cmd.reload.d", "cmd.reload.u", "action");
_R("clear", "cmd.clear.d", "cmd.clear.u", "action");
_R("quit", "cmd.quit.d", "cmd.quit.u", "action");

// ==================== 强力（玩家向）====================
_R("speed", "cmd.speed.d", "cmd.speed.u", "power");
_R("warp", "cmd.warp.d", "cmd.warp.u", "power");
_R("hp", "cmd.hp.d", "cmd.hp.u", "power");
_R("god", "cmd.god.d", "cmd.god.u", "power");
_R("freeze", "cmd.freeze.d", "cmd.freeze.u", "power");

// ==================== 强力（开发者向）====================
_R("pause", "cmd.pause.d", "cmd.pause.u", "debug");
_R("resume", "cmd.resume.d", "cmd.resume.u", "debug");
_R("step", "cmd.step.d", "cmd.step.u", "debug");
_R("dump", "cmd.dump.d", "cmd.dump.u", "debug");
_R("watch", "cmd.watch.d", "cmd.watch.u", "debug");
_R("crash", "cmd.crash.d", "cmd.crash.u", "debug");

// ==================== 调试类 ====================
_R("eval", "cmd.eval.d", "cmd.eval.u", "debug");
_R("exec", "cmd.exec.d", "cmd.exec.u", "debug");
_R("err", "cmd.err.d", "cmd.err.u", "debug");
_R("ctx", "cmd.ctx.d", "cmd.ctx.u", "debug");
_R("trace", "cmd.trace.d", "cmd.trace.u", "debug");
_R("log", "cmd.log.d", "cmd.log.u", "debug");
_R("budget", "cmd.budget.d", "cmd.budget.u", "debug");
_R("lang", "cmd.lang.d", "cmd.lang.u", "debug");
// ★ F5 接线：filter 早就能执行（ntl_console_exec:69）但没注册，help/cmds/Tab 里看不到
_R("filter", "cmd.filter.d", "cmd.filter.u", "debug");

// ==================== 自动化 ====================
_R("run", "cmd.run.d", "cmd.run.u", "auto");
_R("alias", "cmd.alias.d", "cmd.alias.u", "auto");
_R("bind", "cmd.bind.d", "cmd.bind.u", "auto");
_R("macro", "cmd.macro.d", "cmd.macro.u", "auto");
_R("sleep", "cmd.sleep.d", "cmd.sleep.u", "auto");
_R("loop", "cmd.loop.d", "cmd.loop.u", "auto");
_R("batch", "cmd.batch.d", "cmd.batch.u", "auto");
_R("autorun", "cmd.autorun.d", "cmd.autorun.u", "auto");
// ★ 缺陷 2 修复：hist 同样"能执行但没注册"（ntl_console_exec.gml:107-118）
_R("hist", "cmd.hist.d", "cmd.hist.u", "auto");

return 1;
