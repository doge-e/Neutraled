using Microsoft.Win32;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>安装器：把 Neutraled 装到玩家的 DELTARUNE 上，并可完整还原。
///
/// 设计原则：
///   1. **不改动任何原有文件**：只新增 Neutraled/ 目录，部署时把 data.win 的备份放到
///      Neutraled/backup/（不是原地改）
///   2. **可完整还原**：--uninstall 把 data.win 恢复成备份版本并删除 Neutraled/ 相关产物
///   3. **不碰存档**：官方存档在 %LOCALAPPDATA%\DELTARUNE，安装/卸载都不动它
///   4. **Steam 兼容**：安装后游戏仍由 Steam 启动（计时/云存档正常）
/// </summary>
public static class Installer
{
    /// <summary>从 Steam 注册表/常见路径自动找 DELTARUNE 安装目录。</summary>
    public static List<string> FindGameDirs()
    {
        var found = new List<string>();
        void Add(string? p)
        {
            if (string.IsNullOrWhiteSpace(p)) return;
            try
            {
                var full = Path.GetFullPath(p);
                if (File.Exists(Path.Combine(full, "DELTARUNE.exe")) && !found.Contains(full))
                    found.Add(full);
            }
            catch { }
        }

        // 1) Steam 库文件夹
        foreach (var root in new[]
        {
            @"C:\Program Files (x86)\Steam",
            @"C:\Program Files\Steam",
            @"D:\Steam", @"E:\Steam", @"F:\Steam",
            @"D:\steam", @"E:\steam", @"F:\steam",
        })
        {
            try
            {
                var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(vdf)) continue;
                foreach (var line in File.ReadAllLines(vdf))
                {
                    var t = line.Trim();
                    var i = t.IndexOf("\"path\"", StringComparison.Ordinal);
                    if (i < 0) continue;
                    var start = t.IndexOf('"', i + 6);
                    var end = t.IndexOf('"', start + 1);
                    if (start < 0 || end < 0) continue;
                    var lib = t.Substring(start + 1, end - start - 1).Replace("\\\\", "\\");
                    Add(Path.Combine(lib, "steamapps", "common", "DELTARUNE"));
                }
            }
            catch { }
        }

        // 2) 注册表（Steam App 1671210）
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 1671210");
            Add(k?.GetValue("InstallLocation") as string);
        }
        catch { }
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam");
            var sp = k?.GetValue("SteamPath") as string;
            Add(Path.Combine(sp ?? "", "steamapps", "common", "DELTARUNE"));
        }
        catch { }

        return found;
    }

    /// <summary>--install [--game &lt;目录&gt;] [--src &lt;内容目录&gt;]：把 Neutraled 装到游戏目录。</summary>
    public static int Install(string? gameRoot, string? srcArg = null)
    {
        Console.WriteLine(L("===== Neutraled 安装器 ====="));

        if (string.IsNullOrEmpty(gameRoot))
        {
            var dirs = FindGameDirs();
            if (dirs.Count == 0)
            {
                Console.WriteLine(L("[错误] 没找到 DELTARUNE 安装目录，请用 --game <目录> 指定"));
                return 1;
            }
            gameRoot = dirs[0];
            Console.WriteLine(L("  自动检测到: {0}", gameRoot));
            if (dirs.Count > 1) Console.WriteLine(L("  （另有 {0} 个候选，可用 --game 指定）", dirs.Count - 1));
        }

        if (!File.Exists(Path.Combine(gameRoot, "DELTARUNE.exe")))
        {
            Console.WriteLine(L("[错误] 该目录没有 DELTARUNE.exe: {0}", gameRoot));
            return 1;
        }

        var ntlRoot = Paths.NeutraledRoot(gameRoot);
        var selfDir = AppContext.BaseDirectory;
        var srcRoot = ResolveSourceRoot(selfDir, srcArg);

        Console.WriteLine(L("  游戏目录: {0}", gameRoot));
        Console.WriteLine(L("  源目录:   {0}", srcRoot));
        Console.WriteLine(L("  目标:     {0}", ntlRoot));

        static bool HasApi(string d) =>
            Directory.Exists(Path.Combine(d, "api")) && Directory.Exists(Path.Combine(d, "docs"));

        /// <summary>定位要安装的"源"（含 api/ live/ docs/ 的那个目录）。四种布局都要支持：
        ///   显式：--src &lt;目录&gt;
        ///   新发布包：&lt;out&gt;/install/ntl-builder.exe + &lt;out&gt;/src/ ⇒ 取同级的 src/
        ///   开发：&lt;游戏&gt;/Neutraled/builder/bin/Release/net9.0 → 上溯 4 层 = Neutraled/
        ///   旧发布包：&lt;out&gt;/bin/ntl-builder.exe → 上溯 1~3 层 = &lt;out&gt;
        /// （曾只认开发布局，导致独立发布包安装时"已复制 0 个目录"）</summary>
        static string ResolveSourceRoot(string selfDir, string? srcOverride)
        {
            if (!string.IsNullOrEmpty(srcOverride))
            {
                var s = Path.GetFullPath(srcOverride);
                if (HasApi(s)) return s;
                Console.WriteLine(L("  [警告] --src 目录里没有 api/ 与 docs/，忽略: {0}", s));
            }
            foreach (var cand in new[] { Path.Combine(selfDir, "..", "src"), Path.Combine(selfDir, "src") })
            {
                var c = Path.GetFullPath(cand);
                if (HasApi(c)) return c;
            }
            var dev = Path.GetFullPath(Path.Combine(selfDir, "..", "..", "..", ".."));
            if (HasApi(dev)) return dev;
            var dir = new DirectoryInfo(selfDir);
            for (int i = 0; i < 4 && dir != null; i++)
            {
                if (HasApi(dir.FullName)) return dir.FullName;
                dir = dir.Parent;
            }
            return dev;
        }

        // 1) 备份原版 data.win（只备份一次）
        //    写**两处**：Neutraled\backup（随项目走，Steam 重装后仍可能幸存）
        //             <游戏根>\backup（工具链的规范位置，Paths.BackupDataWin 优先读这里）
        var backupDir = Path.Combine(ntlRoot, "backup");
        var canonicalBackupDir = Path.Combine(gameRoot, "backup");
        Directory.CreateDirectory(backupDir);
        int backed = 0;
        var backupRels = new List<string> { "data.win" };                       // 章节目录后缀随平台（windows/linux/unix/macos）
        // 章节数不硬编码 5：游戏更新/DLC 可能带来新章节（chapter6_windows…）。备份是「只增不改」的
        // 安全网，少备份一个 = 那个章节以后永远还原不回原版（宁可在列表里多写几个不存在的路径）。
        int slotMax = 5;
        try { slotMax = Math.Max(slotMax, GameUpdate.DiscoveredSlotCount(gameRoot)); } catch { }
        for (int ci = 1; ci <= slotMax; ci++) backupRels.Add("chapter" + ci + "_" + Paths.ChapterSuffix(gameRoot) + "/data.win");
        foreach (var rel in backupRels)
        {
            var src = Path.Combine(gameRoot, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(src)) continue;
            var any = false;
            foreach (var root in new[] { canonicalBackupDir, backupDir })
            {
                var dst = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(dst)) continue;                 // 已备份过，不覆盖
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Copy(src, dst);
                any = true;
            }
            if (any) backed++;
        }
        Console.WriteLine(L("  原版备份: {0} 个 data.win → {1}（同时在 {2} 留一份）", backed, canonicalBackupDir, backupDir));

        // 2) 复制 Neutraled 运行所需内容（api / live / mods / docs / builder / gui）
        // 保护：开发环境下"源目录 == 目标目录"，此时跳过复制（否则是自己拷自己，会锁文件）
        bool samePlace = string.Equals(Path.GetFullPath(srcRoot).TrimEnd('\\'),
                                       Path.GetFullPath(ntlRoot).TrimEnd('\\'),
                                       StringComparison.OrdinalIgnoreCase);
        if (samePlace)
        {
            Console.WriteLine(L("  源与目标相同（开发环境），跳过文件复制"));
        }
        else
        {
            // tools/ = LÖVE 运行时（Kristal 外部章节要用）；bside/ = B 面存档模板；scripts/ = 辅助脚本
            // ★ 第十二批新增：lang = 外部语言包（游戏内 lang <code> 要用）、themes = 外部主题、
            //   plugins = 用户插件、web = 网页界面的静态页（WebUi 会优先读它，缺了就退回内置兜底页）
            // ★ 第十四批：fonts = 部署时注入的中文字体包（Injector 读 Neutraled/fonts/*.json）、
            //   templates = --new-chapter 的脚手架模板、kristal = 融合版 Kristal 控制台要注入的 lib.lua、
            //   kristal-maps = 房间地图（api/ntl_map_load.gml 从 Neutraled/kristal-maps/ 读）、
            //   sdk = 插件 SDK 契约源码。原来只复制 api/live/mods/docs 等，从发布包安装会缺这几样。
            string[] copyDirs = { "api", "live", "mods", "docs", "tools", "bside", "scripts", "lang", "themes", "plugins", "web", "fonts", "templates", "kristal", "kristal-maps", "sdk", "console-theme.json" };
            int copied = 0;
            foreach (var d in copyDirs)
            {
                var s = Path.Combine(srcRoot, d);
                if (!Directory.Exists(s)) continue;
                var t = Path.Combine(ntlRoot, d);
                try { CopyDir(s, t); copied++; }
                catch (Exception ex) { Console.WriteLine(L("  [警告] 复制 {0} 出错: {1}", d, ex.Message)); }
            }
            // 可执行文件：**两种布局都要认**
            //   开发布局：builder/bin/Release/net9.0、gui/bin/Release/net9.0-windows
            //   发布包布局：bin/（ntl-builder.exe + ntl-gui.exe）
            // 曾经只认开发布局 → 从发布包安装时"装完找不到 GUI/没有 LÖVE 运行时"。
            bool exeCopied = false;
            foreach (var rel in new[] { "builder/bin/Release/net9.0", "gui/bin/Release/net9.0-windows", "bin" })
            {
                var s = Path.Combine(srcRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                if (Directory.Exists(s))
                {
                    try { CopyDir(s, Path.Combine(ntlRoot, rel.Replace('/', Path.DirectorySeparatorChar))); copied++; exeCopied = true; }
                    catch (Exception ex) { Console.WriteLine(L("  [警告] 复制 {0} 出错: {1}", rel, ex.Message)); }
                }
            }
            // 新发布包布局（install/ + src/）里没有 bin/：把安装器自身装进 <游戏>\Neutraled\bin\
            //   否则"装完游戏目录里没有工具"，用户还得自己找 exe。
            if (!exeCopied)
            {
                var selfExe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(selfExe) && File.Exists(selfExe))
                {
                    var binDst = Path.Combine(ntlRoot, "bin");
                    try
                    {
                        Directory.CreateDirectory(binDst);
                        File.Copy(selfExe, Path.Combine(binDst, Path.GetFileName(selfExe)), true);
                        // 单文件发布没内嵌的原生库（Magick）要一起带过去
                        var selfDir2 = Path.GetDirectoryName(selfExe)!;
                        foreach (var side in new[] { "Magick.Native-Q8-x64.dll", "Magick.Native-Q8-x86.dll", "Magick.Native-Q8-arm64.dll" })
                        {
                            var sp = Path.Combine(selfDir2, side);
                            if (File.Exists(sp)) File.Copy(sp, Path.Combine(binDst, side), true);
                        }
                        Console.WriteLine(L("  安装器已装到 {0}", binDst));
                        copied++;
                    }
                    catch (Exception ex) { Console.WriteLine(L("  [警告] 复制 {0} 出错: {1}", "bin", ex.Message)); }
                }
            }
            Console.WriteLine(L("  已复制 {0} 个目录", copied));
        }

        // 3) 写安装标记
        var marker = Path.Combine(ntlRoot, "installed.json");
        File.WriteAllText(marker, new JsonObject
        {
            ["installedAt"] = DateTime.Now.ToString("s"),
            ["gameRoot"] = gameRoot,
            ["version"] = Paths.ApiVersion(),
            ["source"] = srcRoot
        }.ToJsonString(new JsonSerializerOptions(Paths.Json) { WriteIndented = true }));

        // 存档基准快照（首次安装留底，任何时候都能回到"未装 mod"的状态）
        SaveGuard.SnapshotBaseline(gameRoot);

        // 4) 桌面 / 开始菜单快捷方式（用户是从桌面打开 GUI 管理器的）
        var guiExe = FindGui(ntlRoot);
        if (guiExe != null)
        {
            CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "Neutraled Mod Manager.lnk"), guiExe, gameRoot);
            CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                "Neutraled Mod Manager.lnk"), guiExe, gameRoot);
        }
        else
        {
            Console.WriteLine(L("  [警告] 找不到 ntl-gui.exe，跳过快捷方式（GUI 需要先构建 gui 项目）"));
        }

        // 外部章节（Kristal 等）需要"守候进程"在跑：游戏内没有启动进程的内置函数，只能靠它消费
        // launch-request.json。安装时**不**擅自替用户改开机项 / 计划任务，只给两条可复制的命令。
        try
        {
            bool hasExternal = false;
            foreach (var e in Chapters.LoadExternalList(gameRoot))
                if (e.TryGetValue("exe", out var v) && v is string s && s.Length > 0) { hasExternal = true; break; }
            if (hasExternal)
            {
                Console.WriteLine(L("  提示：你装了外部章节（如 Kristal）—— 在游戏里选中它需要守候进程在跑。"));
                Console.WriteLine(L("        现在开一次：{0}", "ntl-builder --ensure-watcher"));
                Console.WriteLine(L("        开机就有（免管理员）：{0}（管理器工具箱里也能一键开）", "ntl-builder --watch-autostart on"));
            }
        }
        catch { }

        Console.WriteLine(L("  ✅ 安装完成"));
        if (guiExe != null)
            Console.WriteLine(L("  下一步：双击桌面上的 {0} 选择章节与 mod，然后点「部署并启动」", ShortcutName()));
        else
            // 发布包（install/ + src/）里没有 GUI：给出等价的命令行/网页界面入口，别让用户去找一个不存在的桌面图标
            Console.WriteLine(L("  下一步：本包不含图形界面 —— 运行 {0} 打开网页界面，或运行 {1} 直接部署并启动",
                "Neutraled\\bin\\ntl-builder.exe --web", "Neutraled\\bin\\ntl-builder.exe --deploy-all"));
        return 0;
    }

    private static string ShortcutName() => "Neutraled Mod Manager";

    /// <summary>找 GUI 可执行文件：开发构建优先，独立发布包在 gui\ 下。</summary>
    private static string? FindGui(string ntlRoot)
    {
        foreach (var c in new[]
                 {
                     Path.Combine(ntlRoot, "gui", "bin", "Release", "net9.0-windows", "ntl-gui.exe"),
                     Path.Combine(ntlRoot, "gui", "ntl-gui.exe"),
                     Path.Combine(ntlRoot, "bin", "ntl-gui.exe"),      // 独立发布包的布局
                     Path.Combine(ntlRoot, "ntl-gui.exe")
                 })
            if (File.Exists(c)) return c;
        return null;
    }

    /// <summary>用 WScript.Shell 建 .lnk（COM 在 .NET 9 的 Windows 上可用，但走 PowerShell 更省事且够稳）。</summary>
    private static void CreateShortcut(string lnkPath, string target, string workDir)
    {
        try
        {
            var q = (string s) => "'" + s.Replace("'", "''") + "'";
            var script = "$s=(New-Object -ComObject WScript.Shell).CreateShortcut(" + q(lnkPath) + ");" +
                         "$s.TargetPath=" + q(target) + ";$s.WorkingDirectory=" + q(Path.GetDirectoryName(target)!) + ";" +
                         "$s.IconLocation=" + q(target + ",0") + ";$s.Description=" + q("Neutraled - DELTARUNE Mod Manager") + ";$s.Save()";
            var psi = new ProcessStartInfo("powershell",
                "-NoProfile -ExecutionPolicy Bypass -Command \"" + script + "\"")
            { UseShellExecute = false, CreateNoWindow = true };
            Process.Start(psi)?.WaitForExit(15000);
            if (File.Exists(lnkPath)) Console.WriteLine(L("  快捷方式: {0}", lnkPath));
            else Console.WriteLine(L("  [警告] 快捷方式未创建: {0}", lnkPath));
        }
        catch (Exception ex) { Console.WriteLine(L("  [警告] 创建快捷方式失败: {0}", ex.Message)); }
    }

    private static void RemoveShortcuts()
    {
        foreach (var dir in new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                                    Environment.GetFolderPath(Environment.SpecialFolder.Programs) })
        {
            var lnk = Path.Combine(dir, "Neutraled Mod Manager.lnk");
            if (!File.Exists(lnk)) continue;
            try
            {
                File.Delete(lnk);
                Console.WriteLine(L("  已移除快捷方式: {0}", lnk));
            }
            catch (Exception ex)
            {
                // 以前这里是 catch { }：删失败看不出任何原因（OneDrive 桌面/杀软占用时会静默留下快捷方式）
                Console.WriteLine(L("  [警告] 移除快捷方式失败: {0}（{1}）", lnk, ex.Message));
            }
            if (File.Exists(lnk)) Console.WriteLine(L("  [警告] 快捷方式仍然存在（被占用或正在同步）: {0}", lnk));
        }
    }

    /// <summary>--uninstall [--game &lt;目录&gt;]：还原原版并移除 Neutraled 产物。</summary>
    public static int Uninstall(string? gameRoot)
    {
        Console.WriteLine(L("===== Neutraled 卸载/还原 ====="));
        if (string.IsNullOrEmpty(gameRoot))
        {
            var dirs = FindGameDirs();
            gameRoot = dirs.FirstOrDefault();
        }
        if (string.IsNullOrEmpty(gameRoot))
        {
            Console.WriteLine(L("[错误] 没找到游戏目录，请用 --game <目录> 指定"));
            return 1;
        }

        var ntlRoot = Paths.NeutraledRoot(gameRoot);
        var backupDir = Path.Combine(ntlRoot, "backup");
        if (!Directory.Exists(backupDir))
        {
            Console.WriteLine(L("[错误] 没有找到备份目录（Neutraled/backup），无法安全还原"));
            return 1;
        }

        // ★ 陈旧备份守卫：如果游戏本体已经更新（活跃文件是新原版、指纹 ≠ 我们的备份），直接还原旧备份
        //   就等于把游戏**降级回旧版本**（Steam 随后还会再下一遍，甚至存档不兼容）。
        //   这种时候必须用户显式 --force；顺手提示 --adopt-current（旧备份移进 history/，全留不删）。
        try
        {
            var rep = GameUpdate.Check(gameRoot);
            if (rep.Verdict == "update")
            {
                Console.WriteLine(L("  [警告] 游戏本体已更新：Neutraled/backup 里的原版备份是**旧版本**"));
                Console.WriteLine(L("         直接还原 = 把游戏降级。推荐先跑 --adopt-current --yes 重新建立基线（旧备份移进 backup/history，不删）"));
                if (!Program.ForceDeploy)
                {
                    Console.WriteLine(L("         确认要还原旧版本请加 --force：--uninstall --force"));
                    return 3;
                }
                Console.WriteLine(L("         已指定 --force：按旧备份还原（会把游戏降级）"));
            }
        }
        catch (Exception ex) { Console.WriteLine(L("  [警告] 陈旧备份检查失败（跳过该守卫）: {0}", ex.Message)); }

        int restored = 0;
        var histPrefix = GameUpdate.HistoryDirName + Path.DirectorySeparatorChar;
        var histPrefixAlt = GameUpdate.HistoryDirName + "/";
        foreach (var f in Directory.GetFiles(backupDir, "data.win", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(backupDir, f);
            // ★ history/ 是**旧备份归档**（采纳新原版时移进去的过期备份），不是「可以贴回游戏根的备份」。
            //   不跳过就会去还原 <gameRoot>\history\<buildid>\{game|neutraled}\data.win ⇒ 一串「还原失败」警告
            //   （沙箱 S6 实测），而且语义上等于把几十个过期副本当成待还原目标，必须排除。
            if (rel.StartsWith(histPrefix, StringComparison.OrdinalIgnoreCase) ||
                rel.StartsWith(histPrefixAlt, StringComparison.OrdinalIgnoreCase)) continue;
            var dst = Path.Combine(gameRoot, rel);
            try
            {
                if (File.Exists(dst)) { File.SetAttributes(dst, FileAttributes.Normal); File.Delete(dst); }
                File.Copy(f, dst);
                restored++;
                Console.WriteLine(L("  已还原: {0}", rel));
            }
            catch (Exception ex) { Console.WriteLine(L("  [警告] 还原失败 {0}: {1}", rel, ex.Message)); }
        }

        // 删掉部署产物（保留 backup 与 mods，玩家可能想留）
        foreach (var rel in new[] { "chapters.json", "hook-registry.json", "api-registry.json", "ns-registry.json" })
        {
            var p = Path.Combine(ntlRoot, rel);
            try { if (File.Exists(p)) File.Delete(p); } catch { }
        }
        foreach (var d in new[] { "cache", "kristal-maps" })
        {
            var p = Path.Combine(ntlRoot, d);
            try { if (Directory.Exists(p)) Directory.Delete(p, true); } catch { }
        }

        RemoveShortcuts();

        Console.WriteLine(L("  ✅ 已还原 {0} 个 data.win，Neutraled 产物已清理", restored));
        Console.WriteLine(L("  （Neutraled/ 目录本身与你的 mods/存档 都保留了，如需彻底删除请手动移除）"));
        return 0;
    }

    private static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src))
        {
            try { File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true); }
            catch (IOException) { /* 目标被占用，跳过 */ }
            catch (UnauthorizedAccessException) { }
        }
        foreach (var d in Directory.GetDirectories(src))
            CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }
}
