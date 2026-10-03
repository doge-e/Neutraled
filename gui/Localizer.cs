using System.Text.Json;

namespace Neutraled.Gui;

/// <summary>GUI 双语（中/英）。语言选择保存在 Neutraled/gui_lang.txt。</summary>
public static class Localizer
{
    public static string Current { get; set; } = "zh";

    private static readonly Dictionary<string, string> En = new(StringComparer.Ordinal)
    {
        ["章节:"] = "Chapter:",
        ["刷新"] = "Refresh",
        ["部署"] = "Deploy",
        ["部署并启动"] = "Deploy & Launch",
        ["恢复原版"] = "Restore Vanilla",
        ["导入旧 mod"] = "Import Legacy Mod",
        ["在线获取"] = "Online",
        ["检查更新"] = "Check Updates",
        ["部署全部"] = "Deploy All",
        ["增强选项"] = "Enhancements",
        ["Neutraled 增强选项（全部可选）"] = "Neutraled Enhancements (all optional)",
        ["跳过章节选择器（直接进入指定章节）"] = "Skip chapter selector (jump to a chapter)",
        ["自动进入章节:"] = "Auto chapter:",
        ["跳过章节内“传说”画面"] = "Skip in-chapter 'Legend' scene",
        ["跳过“DELTARUNE”报幕"] = "Skip 'DELTARUNE' logo intro",
        ["跳过前等待帧数:"] = "Delay frames before skip:",
        ["live 脚本调试日志（debug_on）"] = "Live-script debug log (debug_on)",
        ["提示：修改后需重启游戏生效；全部默认关闭，不影响原版体验。"] =
            "Note: restart the game to apply; everything is off by default and does not affect vanilla.",
        ["保存"] = "Save",
        ["增强选项已保存: "] = "Enhancements saved: ",
        ["增强选项已保存（重启游戏生效）"] = "Enhancements saved (restart game to apply)",
        ["导出日志"] = "Export Log",
        ["未部署（原版）"] = "not deployed (vanilla)",
        ["已部署"] = "deployed",
        ["部署全部章节中..."] = "Deploying all chapters...",
        ["日志已导出: {0}"] = "Log exported: {0}",
        ["拖入 mod 文件"] = "Drop mod files",
        ["就绪"] = "Ready",
        ["共 {0} 个 mod"] = "{0} mod(s)",
        ["启用"] = "on",
        ["禁用"] = "off",
        ["状态"] = "Status",
        ["Mod"] = "Mod",
        ["ID"] = "ID",
        ["部署中..."] = "Deploying...",
        ["部署完成"] = "Deploy done",
        ["部署失败 (exit={0})"] = "Deploy failed (exit={0})",
        ["恢复中..."] = "Restoring...",
        ["已恢复原版"] = "Restored to vanilla",
        ["恢复失败"] = "Restore failed",
        ["转换中..."] = "Converting...",
        ["转换完成"] = "Convert done",
        ["转换失败"] = "Convert failed",
        ["启动游戏（Steam）..."] = "Launching game (Steam)...",
        ["已启动游戏（通过 Steam）"] = "Game launched via Steam",
        ["游戏根: {0}"] = "Game root: {0}",
        ["已加载 {0} 个 mod（目录: {1}）"] = "Loaded {0} mod(s) (dir: {1})",
        ["[错误] mods 目录不存在: {0}"] = "[Error] mods directory not found: {0}",
        ["[警告] 未检测到 DELTARUNE.exe —— 请把整个 Neutraled 文件夹放到游戏根目录下运行"] =
            "[Warning] DELTARUNE.exe not found - put the whole Neutraled folder inside the game root",
        ["  启用状态: {0} -> {1}"] = "  enabled: {0} -> {1}",
        ["[警告] 解析失败 {0}: {1}"] = "[Warning] parse failed {0}: {1}",
        ["[警告] 写入失败 {0}: {1}"] = "[Warning] write failed {0}: {1}",
        ["all 不支持一键恢复，请分别选择单章恢复。"] = "\"all\" cannot be restored at once; restore chapter by chapter.",
        ["把 {0} 恢复到原版？"] = "Restore {0} to vanilla?",
        ["旧 mod (*.zip;*.xdelta;*.win)|*.zip;*.xdelta;*.win|所有文件|*.*"] =
            "Legacy mod (*.zip;*.xdelta;*.win)|*.zip;*.xdelta;*.win|All files|*.*",
        ["选择要转换的旧 mod（zip / xdelta / data.win）"] = "Select a legacy mod (zip / xdelta / data.win)",
        ["导入旧 mod"] = "Import Legacy Mod",
        ["选择要转换的旧 mod（文件或整个补丁目录）"] = "Select a legacy mod (file or patch directory)",
        ["选择文件"] = "Choose File",
        ["选择目录"] = "Choose Folder",
        ["选择补丁目录（含 chapterN.xdelta）"] = "Select patch directory (contains chapterN.xdelta)",
        ["作者"] = "Author",
        ["章节"] = "Chapter",
        ["mod 名称"] = "Mod name",
        ["确定"] = "OK",
        ["在线获取（GameBanana）"] = "Online (GameBanana)",
        ["搜索"] = "Search",
        ["下载并转换"] = "Download & Convert",
        ["关闭"] = "Close",
        ["下载中..."] = "Downloading...",
        ["完成 ✓"] = "Done ✓",
        ["失败"] = "Failed",
        ["启动游戏失败: {0}"] = "Failed to launch game: {0}",
        ["游戏退出，正在恢复原版..."] = "Game exited, restoring vanilla...",
        ["已自动恢复原版"] = "Auto-restored to vanilla",
        ["工具箱"] = "Toolbox",
        ["部署加速档（跳过输入重定向）"] = "Fast deploy (skip input redirection)",
        ["开启后部署会附加 --fast-deploy：跳过「输入函数重定向」，部署更快。"] =
            "When on, deploys pass --fast-deploy: the input-redirection pass is skipped, so deploying is faster.",
        ["代价：章节内「控制台输入屏蔽」失效 —— 控制台仍能打开，但游戏自身的 keyboard_check_direct 仍会读到按键。"] =
            "Cost: in-chapter console input blocking stops working - the console still opens, but the game's own keyboard_check_direct still reads your keys.",
        ["chapter5 实测约省 20 秒（命中输入扫描缓存后约省 1 秒）。默认关闭，随时可改回。"] =
            "Measured on chapter5: saves ~20 s (~1 s once the input-scan cache is warm). Off by default; can be turned back off anytime.",
        ["部署加速档：已开启（之后部署会附加 --fast-deploy，控制台输入屏蔽失效）"] =
            "Fast deploy: ON (later deploys pass --fast-deploy; console input blocking is disabled)",
        ["部署加速档：已关闭（之后部署会做完整的输入函数重定向）"] =
            "Fast deploy: OFF (later deploys run the full input-redirection pass)",
        ["部署加速档: {0}（工具箱里可改）"] = "Fast deploy: {0} (change it in Toolbox)",
        ["注意：部署缓存签名不含这个开关，切换后会提示清空缓存，否则可能直接复用上一次的产物。"] =
            "Note: the deploy-cache signature does not include this switch, so switching prompts to clear the cache; otherwise the previous artifact may be reused as-is.",
        ["部署缓存的签名不包含这个开关，旧缓存会在下次部署时被直接复用。\n\n现在清空部署缓存，确保新设置下次真正生效？"] =
            "The deploy-cache signature does not include this switch, so an old cached artifact would be reused on the next deploy.\n\nClear the deploy cache now so the new setting really takes effect?",
        ["部署加速档"] = "Fast deploy",
        ["部署缓存已清空，下次部署会按当前设置重建。"] =
            "Deploy cache cleared; the next deploy rebuilds with the current setting.",
        ["[警告] 未清空缓存：若下次部署命中了旧缓存，这个开关可能看起来不生效（缓存管理里可随时清空）。"] =
            "[Warning] Cache not cleared: if the next deploy hits an old cache entry, this switch may appear to have no effect (clear it anytime in Cache Manager).",

        // ==================== 任务 A：T() 已调用但缺 En 词条（45 条）====================
        ["缓存管理"] = "Cache Manager",
        ["转换失败（详见日志）"] = "Convert failed (see log)",
        ["准备启动（查缓存）..."] = "Preparing launch (cache check)...",
        ["启动失败 (exit={0})"] = "Launch failed (exit={0})",
        ["部署缓存管理"] = "Deploy Cache Manager",
        ["清空缓存"] = "Clear Cache",
        ["上限(MB)"] = "Limit (MB)",
        ["设置"] = "Set",
        ["确定清空所有部署缓存？"] = "Clear all deploy caches?",
        ["确认"] = "Confirm",
        ["配置已变更，重启游戏后生效"] = "Config changed; restart the game to apply",
        ["选择要转换的旧 mod"] = "Select a legacy mod to convert",
        ["Kristal 宿主合并"] = "Kristal Host Merge",
        ["项目 + 插件 → 一个 mod（插件型 mod 缺宿主时用）"] = "Project + plug-ins → one mod (for plug-in mods missing a host)",
        ["制作 B 面存档"] = "Create B-side Save",
        ["任意章节直接开 B 面，不用从第二章重打"] = "Open the B-side in any chapter, no replay from chapter 2",
        ["导出资源包"] = "Export Asset Packs",
        ["data.win → 精灵/声音/字体，可叠加到任意基底"] = "data.win → sprites/sounds/fonts, stackable on any base",
        ["检查 mod 冲突"] = "Check Mod Conflicts",
        ["只查不部署（退出码 2 = 有冲突），写 conflicts.json"] = "Check only, no deploy (exit 2 = conflicts); writes conflicts.json",
        ["源码级差异层"] = "Source-level Diff Layer",
        ["整包 mod → 可叠加 patch 层（反编译真实改动，绕过索引问题）"] =
            "Whole-pack mod → stackable patch layer (real changes, bypasses index drift)",
        // ---- 守候进程（Kristal 等外部章节的接管者；见 builder/WatchAutostart.cs）----
        ["守候进程：状态 / 立即启动"] = "Watcher: Status / Start Now",
        ["看守候是否在跑、自启装了没有；没在跑就立刻起一个（选中 Kristal 章节需要它）"] =
            "Check the watcher; start one now if it is not running (Kristal needs it)",
        ["守候进程自启：开启"] = "Watcher Autostart: Enable",
        ["登录 / 解锁 / 每 1 分钟兜底自动拉起守候（计划任务 + 开机启动项，都不需要管理员权限）"] =
            "Start the watcher at logon / unlock / every minute (no admin rights)",
        ["守候进程自启：关闭"] = "Watcher Autostart: Disable",
        ["移除计划任务与开机启动项（不会杀掉当前正在跑的守候）"] =
            "Remove the scheduled task and the Startup shortcut (does not kill a running watcher)",
        ["选择 Kristal 宿主项目目录（含 mod.json）"] = "Select the Kristal host project folder (with mod.json)",
        ["宿主: "] = "Host: ",
        ["添加插件目录..."] = "Add plug-in folder...",
        ["移除选中"] = "Remove Selected",
        ["开始合并转换"] = "Start Merge & Convert",
        ["选择插件 mod 目录（含 scripts/ 或 assets/）"] = "Select a plug-in mod folder (with scripts/ or assets/)",
        ["至少添加一个插件目录"] = "Add at least one plug-in folder",
        ["合并转换中..."] = "Merging & converting...",
        ["完成"] = "Done",
        ["失败（详见日志）"] = "Failed (see log)",
        ["章节（1-5）:"] = "Chapter (1-5):",
        ["槽位（0-2）:"] = "Slot (0-2):",
        ["全部 3 个槽位"] = "All 3 slots",
        ["取消"] = "Cancel",
        ["B 面存档已创建：进游戏选该章节即可直接开 B 面"] = "B-side save created: pick that chapter in-game to start on the B-side",
        ["导入 B 面存档"] = "Import B-side Save",
        ["把一份真实 B 面存档导入并直接加载（名字 ≤12 个字母写进存档第 1 行）"] =
            "Import a real B-side save and load it right away (name of up to 12 letters goes into save line 1)",
        ["存档文件|filech*;*.sav|所有文件|*.*"] = "Save files|filech*;*.sav|All files|*.*",
        ["选择要导入的真实 B 面存档"] = "Select the real B-side save to import",
        ["存档: "] = "Save: ",
        ["名字（≤12 字母）:"] = "Name (max 12 letters):",
        ["存档第 1 行就是角色名：填玩家名字（最多 12 个字母，会自动转大写）"] =
            "Line 1 of the save is the character name: enter the player name (max 12 letters, uppercased automatically)",
        ["直接写进存档并标记 B 面（取消勾选 = 只导入模板）"] =
            "Write into the save and mark B-side (unchecked = import the template only)",
        ["导入"] = "Import",
        ["导入 B 面存档中..."] = "Importing the B-side save...",
        ["请先输入名字：存档第 1 行就是角色名（最多 12 个字母）"] =
            "Enter a name first: line 1 of the save is the character name (max 12 letters)",
        ["B 面存档已导入并写入槽位：进游戏选该章节即可直接开 B 面"] =
            "B-side save imported into the slot: pick that chapter in-game to start on the B-side",
        ["B 面模板已导入（只导入模板，没有写进存档）"] =
            "B-side template imported (template only; nothing was written into the save)",
        ["失败，详见日志"] = "Failed; see log",
        ["选择要导出资源的 data.win"] = "Select the data.win to export assets from",
        ["导出资源包中（大文件可能几分钟）..."] = "Exporting asset packs (large files may take minutes)...",
        ["导出完成"] = "Export done",
        ["未发现冲突"] = "No conflicts found",
        ["发现冲突：详见日志与 conflicts.json"] = "Conflicts found: see log and conflicts.json",
        ["选择整包 mod 的 ref/data.win"] = "Select the whole-pack mod's ref/data.win",
        ["反编译提取差异层中..."] = "Extracting diff layer (decompiling)...",

        // ==================== 任务 B：裸中文接线（MainForm）====================
        ["诊断"] = "Diagnostics",
        ["联动"] = "Interop",
        ["权限"] = "Permissions",
        ["[警告] 读取 config.json 失败: {0}"] = "[Warning] failed to read config.json: {0}",
        ["[警告] 写入 config.json 失败: {0}"] = "[Warning] failed to write config.json: {0}",
        ["脚本"] = "Scripts",
        ["补丁"] = "Patches",
        ["精灵"] = "Sprites",
        ["声音"] = "Sounds",
        ["字体"] = "Fonts",
        ["外部文件"] = "Files",
        ["资源基底(data.win)"] = "asset base (data.win)",
        ["包含: "] = "Contains: ",
        ["(仅 mod.json)"] = "(mod.json only)",
        ["（拖入了 {0} 项，仅处理第一个）"] = "(dropped {0} items; only the first is used)",
        ["data.win|*.win|所有文件|*.*"] = "data.win|*.win|All files|*.*",
        ["   {0}: 已是最新 ({1})"] = "   {0}: up to date ({1})",
        ["   {0}: 检查失败 ({1})"] = "   {0}: check failed ({1})",
        ["发现 {0} 个更新"] = "{0} update(s) found",
        ["全部为最新"] = "All up to date",
        ["（没有可用于检查更新的 mod：需要 mod.json 里带 gb_id）"] =
            "(no mod can be update-checked: mod.json needs a gb_id)",
        ["检查失败"] = "Check failed",
        ["[检查更新] {0}"] = "[Check updates] {0}",
        ["检测到 mod 冲突：\n\n  严重冲突 {0} 个\n  覆盖警告 {1} 个\n\n是否查看详情？"] =
            "Mod conflicts detected:\n\n  {0} error(s)\n  {1} warning(s)\n\nView details?",
        ["Neutraled — mod 冲突提醒"] = "Neutraled — Mod Conflict Alert",
        ["欢迎使用 Neutraled！\n\n1. 确认游戏目录（自动检测）\n2. 备份原版存档（BASELINE 快照，永不删除）\n3. 选择要启用的 mod\n4. 部署并启动游戏\n\n之后每次启动只需点「部署并启动」。\n游戏内按 F2 可以打开控制台。"] =
            "Welcome to Neutraled!\n\n1. Confirm the game folder (auto-detected)\n2. Back up vanilla saves (BASELINE snapshot, never deleted)\n3. Pick the mods to enable\n4. Deploy and launch the game\n\nAfter that, just click \"Deploy & Launch\" each time.\nPress F2 in-game to open the console.",
        ["Neutraled — 首次运行向导"] = "Neutraled — First-Run Wizard",
        ["已建立原版存档备份（BASELINE）"] = "BASELINE vanilla save backup created",
        ["首启初始化失败: {0}"] = "First-run init failed: {0}",
        ["日志里发现 mod 异常记录"] = "mod error records found in the log",
        ["检测到上次游戏出现异常。\n\n{0}\n\n可以尝试：\n  • 恢复存档到上一次快照（推荐）\n  • 禁用所有 mod 后重新部署\n  • 先看诊断详情\n\n要恢复存档吗？"] =
            "The game crashed last session.\n\n{0}\n\nThings to try:\n  • Restore saves to the last snapshot (recommended)\n  • Disable all mods and deploy again\n  • Check the diagnostics first\n\nRestore saves now?",
        ["Neutraled — 异常恢复"] = "Neutraled — Crash Recovery",
        ["已恢复存档"] = "Saves restored",
        ["恢复失败: {0}"] = "Restore failed: {0}",
        ["========== mod 冲突分析 =========="] = "========== Mod conflict analysis ==========",
        ["  严重冲突: {0}"] = "  errors: {0}",
        ["  覆盖警告: {0}"] = "  warnings: {0}",
        ["  可共存  : {0}"] = "  coexist : {0}",
        ["[冲突]"] = "[conflict]",
        ["[覆盖]"] = "[override]",
        ["[共存]"] = "[coexist]",
        ["  （还没有冲突报告，先部署一次）"] = "  (no conflict report yet - deploy once first)",
        ["  读取失败: {0}"] = "  read failed: {0}",
        ["========== 存档快照 =========="] = "========== Save snapshots ==========",
        ["  （还没有快照）"] = "  (no snapshots yet)",
        ["========== 最近日志 =========="] = "========== Recent log ==========",
        ["  （无日志）"] = "  (no log)",
        ["Neutraled — 诊断"] = "Neutraled — Diagnostics",
        ["启动 builder 失败"] = "Failed to start builder",
        ["执行失败: {0}"] = "Run failed: {0}",
        ["========== mod 联动关系 =========="] = "========== Mod interop ==========",
        ["已安装 mod: {0} 个"] = "{0} mod(s) installed",
        ["提示: 游戏内按 F2 输入 mods 可以看到运行时的导出/依赖情况"] =
            "Tip: press F2 in-game and type mods to see runtime exports/dependencies",
        ["读取失败: {0}"] = "Read failed: {0}",
        ["Neutraled — mod 联动"] = "Neutraled — Mod Interop",
        ["========== mod 权限报告 =========="] = "========== Mod permission report ==========",
        ["      已声明: "] = "      declared: ",
        ["      ⚠️ 未声明: "] = "      ⚠️ undeclared: ",
        ["  （所有 mod 都没有敏感操作）"] = "  (no mod performs sensitive operations)",
        ["  （还没有报告，先部署一次）"] = "  (no report yet - deploy once first)",
        ["权限级别: safe（安全） / risky（注意） / danger（危险）"] = "Risk levels: safe / risky / danger",
        ["Neutraled — mod 权限"] = "Neutraled — Mod Permissions",
        ["选择 Kristal mod 目录（含 *.lua）"] = "Select a Kristal mod folder (with *.lua)",
        ["[错误] 找不到 ntl-builder.exe"] = "[Error] ntl-builder.exe not found",
        ["========== Kristal 批量验证 =========="] = "========== Kristal batch validation ==========",
        ["验证完成，报告在 Neutraled/kristal-validation/REPORT.md"] =
            "Validation done; report at Neutraled/kristal-validation/REPORT.md",

        // ==================== 任务 C：原本只显英文的 UI 文案 ====================
        ["Neutraled — DELTARUNE Mod 管理器"] = "Neutraled — DELTARUNE Mod Manager",
        ["作者 {0}"] = "by {0}",
        ["路径: {0}"] = "Path: {0}",
        ["{0}: （缺失）"] = "{0}: (missing)",
        ["日志 (*.txt)|*.txt"] = "Log (*.txt)|*.txt",

        // ==================== 任务 B：裸中文接线（Program.cs）====================
        ["[错误] 找不到 ntl-builder.exe: {0}"] = "[Error] ntl-builder.exe not found: {0}",

        // ==================== 游戏更新检测（2026-10，docs/UPDATE.md）====================
        ["更新检测失败（退出码 {0}），详见日志。\n\n仍要继续{1}吗？"] = "Update check failed (exit code {0}); see the log.\n\nContinue {1} anyway?",
        ["检测到游戏更新"] = "Game update detected",
        ["采纳新基线并继续"] = "Adopt new baseline & continue",
        ["直接继续"] = "Continue anyway",
        ["采纳新基线失败（退出码 {0}），本次已取消，游戏目录没有改动。详见日志。"] = "Adopting the new baseline failed (exit code {0}); this run was cancelled and the game folder was not modified. See the log.",
        ["部署全部章节"] = "Deploy all chapters",
        ["启动"] = "Launch",
        ["检测到游戏更新 / 新章节 / 整包基底漂移（详见下方报告）。\n\n① 采纳新基线并继续：把当前原版采纳为新基线，旧备份移进 backup/history/<buildid>/（不删）；新章节同时登记为已确认，然后继续本次{0}。\n② 直接继续：不做采纳，本次{0}可能被检测拦下（拦下时不会改动游戏目录，日志里有处置步骤）。"] = "A game update / new chapter / whole-pack base drift was detected (see the report below).\n\n① Adopt new baseline & continue: adopt the current vanilla as the new baseline and move the old backups into backup/history/<buildid>/ (never deleted); new chapters are also marked confirmed, then this {0} continues.\n② Continue anyway: no adoption; this {0} may be blocked by the check (when blocked the game folder is not modified and the log lists the next steps)."
    };

    /// <summary>载入界面语言。优先 Neutraled/gui_lang.txt；该文件不存在时回退读
    /// Neutraled/config.json 的 lang 字段（en → en；zh/auto/缺省/其它 → zh），
    /// 让「游戏内语言」与「GUI 语言」共用同一个开关。读失败不影响启动。</summary>
    public static void Load(string neutraledRoot)
    {
        try
        {
            var f = Path.Combine(neutraledRoot, "gui_lang.txt");
            if (File.Exists(f))
            {
                var v = File.ReadAllText(f).Trim();
                if (v == "en" || v == "zh") Current = v;
                return;
            }

            // 回退：游戏侧的语言开关（builder/游戏读的是同一个 config.json 的 lang）
            var cfg = Path.Combine(neutraledRoot, "config.json");
            if (!File.Exists(cfg)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(cfg),
                new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (doc.RootElement.TryGetProperty("lang", out var l) && l.ValueKind == JsonValueKind.String)
            {
                var v = (l.GetString() ?? "").Trim().ToLowerInvariant();
                // en → en；zh / auto / 其它取值 → 一律 zh（与 studio 侧逐字一致，两个编辑器同一个开关）
                Current = (v == "en") ? "en" : "zh";
            }
        }
        catch { }
    }

    public static void Save(string neutraledRoot)
    {
        try { File.WriteAllText(Path.Combine(neutraledRoot, "gui_lang.txt"), Current); } catch { }
    }

    public static string T(string zh, params object[] args)
    {
        var s = zh;
        if (Current == "en" && En.TryGetValue(zh, out var en)) s = en;
        return args.Length > 0 ? string.Format(s, args) : s;
    }
}
