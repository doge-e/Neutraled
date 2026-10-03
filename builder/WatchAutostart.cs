using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>
/// 守候进程（--watch-external）的生命周期：单实例判据 / 立刻拉起 / 开机自启（登录·解锁·每分钟兜底）。
///
/// 为什么必须存在：游戏内**没有**任何"启动进程 / 打开 URL"的内置函数（本机 data.win 函数表里
/// url_open / os_start_process / execute_program / execute_shell 全部不存在，2026-10-03 字节级取证），
/// 所以玩家在选择器里选中 Kristal 这类外部章节时，只能靠**已经在跑**的守候进程读到
/// launch-request.json 去拉起外部引擎；守候没跑就只会看到游戏内提示「未检测到启动器」。
/// 于是：① 启动游戏前确保守候在跑；② 登录/解锁/每分钟兜底，保证它被误杀后能自己回来。
/// </summary>
public static class WatchAutostart
{
    /// <summary>跨进程单实例判据；Local\ = 每个登录会话一份（守候是纯用户态进程，不需要跨会话）。</summary>
    public const string MutexName = @"Local\NeutraledWatchExternal";

    private const string TaskName = "NeutraledWatcher";
    private const string StartupName = "Neutraled Watch.vbs";

    /// <summary>守候进程是否已在运行。守候被强杀时互斥体对象自动消失 ⇒ 这里会返回 false（可据此重启）。</summary>
    public static bool IsWatcherAlive()
    {
        try
        {
            using var m = Mutex.OpenExisting(MutexName);
            return true;
        }
        catch { return false; }
    }

    /// <summary>守候进程**自己**调用：拿到就独占；已有守候在跑则返回 null（本次启动应当直接退出）。</summary>
    public static Mutex? TryAcquireWatcherMutex()
    {
        try
        {
            var m = new Mutex(true, MutexName, out bool createdNew);
            if (createdNew) return m;
            m.Dispose();
            return null;
        }
        catch { return null; }
    }

    private static string WatcherVbs(string gameRoot) =>
        Path.Combine(Paths.NeutraledRoot(gameRoot), "scripts", "watch-external.vbs");

    private static string StartupVbs() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "Microsoft", "Windows", "Start Menu", "Programs", "Startup", StartupName);

    /// <summary>--ensure-watcher：守候没在跑就立刻起一个；已在跑 = no-op（幂等，可安全地被 GUI/部署/启动反复调用）。</summary>
    public static int EnsureRunningCli(string gameRoot)
    {
        if (IsWatcherAlive())
        {
            Console.WriteLine(L("  守候进程已在运行（无需重复启动）"));
            return 0;
        }
        Console.WriteLine(L("  没有检测到守候进程 → 正在启动..."));
        return StartWatcher(gameRoot) ? 0 : 1;
    }

    /// <summary>立刻起一个守候进程（无窗口、脱离控制台）。返回 true = 3 秒内确认到单实例互斥体。</summary>
    public static bool StartWatcher(string gameRoot, bool quiet = false)
    {
        try
        {
            var self = Environment.ProcessPath;
            if (string.IsNullOrEmpty(self))
            {
                if (!quiet) Console.WriteLine(L("  [警告] 拿不到自身 exe 路径，无法启动守候进程"));
                return false;
            }
            var root = Paths.NeutraledRoot(gameRoot);
            Directory.CreateDirectory(root);
            var args = "--watch-external --game \"" + gameRoot + "\"";
            if (!SpawnDetached(self, args, root))
            {
                // 兜底（非 Windows / CreateProcess 失败）：普通启动。注意子进程会**继承**我们的
                // stdout/stderr 句柄，所以调用方要是靠管道读到 EOF 判断结束，就会被拖住 —— 只在这条路上出现。
                var psi = new System.Diagnostics.ProcessStartInfo(self)
                {
                    Arguments = args, WorkingDirectory = root, UseShellExecute = false,
                    CreateNoWindow = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                };
                System.Diagnostics.Process.Start(psi);
            }
            for (int i = 0; i < 20 && !IsWatcherAlive(); i++) Thread.Sleep(150);
            if (IsWatcherAlive())
            {
                if (!quiet) Console.WriteLine(L("  ✅ 守候进程已启动（选中 Kristal 等外部章节时会自动接管）"));
                return true;
            }
            if (!quiet) Console.WriteLine(L("  [警告] 守候进程启动 3 秒后仍未就绪（看 Neutraled\\logs\\watch-external.log）"));
            return false;
        }
        catch (Exception ex)
        {
            if (!quiet) Console.WriteLine(L("  [警告] 启动守候进程失败: {0}", ex.Message));
            return false;
        }
    }

    /// <summary>--watch-autostart [on|off|status]；默认 status（只读，不乱改用户系统）。</summary>
    public static int Cli(string gameRoot, string? mode)
    {
        var m = (mode ?? "").Trim().ToLowerInvariant();
        switch (m)
        {
            case "":
            case "status":
                return Status(gameRoot);
            case "on":
            case "enable":
                return Enable(gameRoot);
            case "off":
            case "disable":
                return Disable(gameRoot);
            default:
                Console.WriteLine(L("  [错误] 未知参数: {0}（可用: on / off / status）", mode ?? ""));
                return 1;
        }
    }

    public static int Status(string gameRoot)
    {
        Console.WriteLine(L("===== 守候进程自启：状态 ====="));
        Console.WriteLine(L("  守候进程: {0}", IsWatcherAlive() ? L("正在运行") : L("没有运行")));
        var (rcTask, outTask) = RunSchtasks("/Query /TN \"" + TaskName + "\"");
        Console.WriteLine(L("  计划任务 {0}: {1}", TaskName, rcTask == 0 ? L("已注册（登录 / 解锁 / 每 1 分钟兜底）") : L("未注册")));
        if (rcTask == 0 && outTask.Length > 0)
        {
            foreach (var line in outTask.Split('\n'))
            {
                var t = line.Trim();
                if (t.StartsWith("状态") || t.StartsWith("Status"))
                {
                    Console.WriteLine(L("    {0}", t));
                    break;
                }
            }
        }
        var sv = StartupVbs();
        Console.WriteLine(L("  开机启动项: {0} → {1}", sv, File.Exists(sv) ? L("存在") : L("不存在")));
        var wv = WatcherVbs(gameRoot);
        Console.WriteLine(L("  启动器脚本: {0} → {1}", wv, File.Exists(wv) ? L("存在") : L("不存在")));
        if (!IsWatcherAlive())
            Console.WriteLine(L("  提示: 现在跑一次 --ensure-watcher 可以立刻把守候拉起来（不必等下次登录）。"));
        return 0;
    }

    public static int Enable(string gameRoot)
    {
        Console.WriteLine(L("===== 守候进程自启：开启 ====="));
        bool scriptOk = false, startupOk = false, taskOk = false;

        try
        {
            var vbs = WatcherVbs(gameRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(vbs)!);
            WriteScript(vbs, WatcherVbsBody(gameRoot));
            Console.WriteLine(L("  ✅ 启动器已写入: {0}", vbs));
            scriptOk = true;
        }
        catch (Exception ex) { Console.WriteLine(L("  [错误] 写启动器失败: {0}", ex.Message)); }

        try
        {
            var sv = StartupVbs();
            Directory.CreateDirectory(Path.GetDirectoryName(sv)!);
            WriteScript(sv, StartupVbsBody(gameRoot));
            Console.WriteLine(L("  ✅ 开机启动项已写入: {0}", sv));
            startupOk = true;
        }
        catch (Exception ex) { Console.WriteLine(L("  [错误] 写开机启动项失败: {0}", ex.Message)); }

        if (scriptOk)
        {
            var tmp = Path.Combine(Path.GetTempPath(), "ntl-watcher-task.xml");
            try
            {
                File.WriteAllText(tmp, TaskXml(gameRoot), new UnicodeEncoding(false, true));
                var (rc, outp) = RunSchtasks("/Create /TN \"" + TaskName + "\" /XML \"" + tmp + "\" /F");
                if (rc == 0)
                {
                    Console.WriteLine(L("  ✅ 计划任务 {0} 已注册（登录 / 解锁 / 每 1 分钟兜底；免管理员）", TaskName));
                    taskOk = true;
                }
                else
                {
                    Console.WriteLine(L("  [错误] 注册计划任务失败（退出码 {0}）: {1}", rc, outp.Trim()));
                    Console.WriteLine(L("         不影响使用：开机启动项已经能拉起守候，只是被误杀后要等下次登录。"));
                }
            }
            catch (Exception ex) { Console.WriteLine(L("  [错误] 注册计划任务失败: {0}", ex.Message)); }
            finally { try { File.Delete(tmp); } catch { } }
        }

        if (!IsWatcherAlive())
        {
            Console.WriteLine(L("  现在把守候拉起来:"));
            StartWatcher(gameRoot);
        }
        else Console.WriteLine(L("  守候进程已在运行。"));
        Console.WriteLine(L("  小结: 启动器 {0} / 开机项 {1} / 计划任务 {2}（三者任一可用就不会丢掉守候）",
            scriptOk ? L("OK") : L("失败"), startupOk ? L("OK") : L("失败"), taskOk ? L("OK") : L("失败")));
        return (scriptOk && (taskOk || startupOk)) ? 0 : 1;
    }

    public static int Disable(string gameRoot)
    {
        Console.WriteLine(L("===== 守候进程自启：关闭 ====="));
        var (rc, outp) = RunSchtasks("/Delete /TN \"" + TaskName + "\" /F");
        if (rc == 0) Console.WriteLine(L("  ✅ 计划任务 {0} 已删除", TaskName));
        else if (outp.Contains("找不到") || outp.Contains("cannot find", StringComparison.OrdinalIgnoreCase) || outp.Contains("ERROR: The system cannot find"))
            Console.WriteLine(L("  计划任务本来就没注册（跳过）"));
        else Console.WriteLine(L("  [警告] 删除计划任务失败: {0}", outp.Trim()));

        var sv = StartupVbs();
        try
        {
            if (File.Exists(sv)) { File.Delete(sv); Console.WriteLine(L("  ✅ 开机启动项已删除: {0}", sv)); }
            else Console.WriteLine(L("  开机启动项本来就不存在（跳过）"));
        }
        catch (Exception ex) { Console.WriteLine(L("  [警告] 删除开机启动项失败: {0}", ex.Message)); }

        Console.WriteLine(IsWatcherAlive()
            ? L("  注意：当前正在跑的守候进程**没有**被杀掉（它只影响下次登录/兜底）；要立刻停它请在任务管理器结束 ntl-builder.exe。")
            : L("  当前没有守候进程在跑。"));
        return 0;
    }

    /// <summary>有**启用中的外部章节**且守候没在跑 → 拉起来。启动游戏前调用；失败不阻断启动。</summary>
    public static void EnsureWatcherIfNeeded(string gameRoot)
    {
        try
        {
            if (IsWatcherAlive()) return;
            bool hasExternal = false;
            foreach (var e in Chapters.LoadExternalList(gameRoot))
                if (e.TryGetValue("exe", out var v) && v is string s && s.Length > 0) { hasExternal = true; break; }
            if (!hasExternal) return;      // 没装外部章节就别白跑一个常驻进程
            Console.WriteLine(L("  外部章节需要守候进程 → 正在启动..."));
            StartWatcher(gameRoot);
        }
        catch (Exception ex) { Console.WriteLine(L("  [警告] 守候进程自检失败: {0}", ex.Message)); }
    }

    // ───────────────────────── 脚本与计划任务内容 ─────────────────────────

    private static string WatcherVbsBody(string gameRoot)
    {
        var self = Environment.ProcessPath ?? Path.Combine(Paths.NeutraledRoot(gameRoot), "builder", "bin", "Release", "net9.0", "ntl-builder.exe");
        var root = Paths.NeutraledRoot(gameRoot);
        // ★ --game 必须是**游戏根**，不是 Neutraled 目录：这里曾经把 root 传下去（1.0.3 / 1.0.4 出厂件
        //   就带这个毛病），于是「登录 / 解锁 / 每分钟兜底」拉起的守候全程把 Neutraled 当游戏根 ——
        //   config / lang 读不到（日志整篇回退英文）、Kristal 中文回退路径拼成 ...\Neutraled\Kristal-main、
        //   chapters.json 判定失效、面板「重新部署」会往 ...\Neutraled\Neutraled\ 里部署，还留下垃圾日志目录。
        //   （外部章节本身仍能被拉起：游戏写的 launch-request.json 还有 %LOCALAPPDATA% 那条与根无关的路径；
        //     2026-10-03 实测垃圾日志里确实成功启动过冰封帷幕。）
        //   root 只留给 CurrentDirectory —— 守候的工作目录仍在 Neutraled 下，日志与相对路径照旧。
        var game = Path.GetFullPath(gameRoot);
        var sb = new StringBuilder();
        sb.Append("' Neutraled 守候进程启动器（由 ntl-builder --watch-autostart on 生成，可随时重新生成）\r\n");
        sb.Append("' 作用：无窗口、无弹窗地启动 --watch-external。选中 Kristal 等外部章节时，只有它在跑才能接管。\r\n");
        sb.Append("Option Explicit\r\n");
        sb.Append("Dim fso, sh, ntl, root, game\r\n");
        sb.Append("Set fso = CreateObject(\"Scripting.FileSystemObject\")\r\n");
        sb.Append("Set sh  = CreateObject(\"WScript.Shell\")\r\n");
        sb.Append("ntl = \"" + self + "\"\r\n");
        sb.Append("If Not fso.FileExists(ntl) Then WScript.Quit 0\r\n");
        sb.Append("root = \"" + root + "\"\r\n");
        sb.Append("game = \"" + game + "\"\r\n");
        sb.Append("sh.CurrentDirectory = root\r\n");
        sb.Append("sh.Run \"\"\"\" & ntl & \"\"\" --watch-external --game \"\"\" & game & \"\"\"\", 0, False\r\n");
        return sb.ToString();
    }

    private static string StartupVbsBody(string gameRoot)
    {
        var target = WatcherVbs(gameRoot);
        var sb = new StringBuilder();
        sb.Append("' Neutraled 开机自启蹦床（由 ntl-builder --watch-autostart on 生成）\r\n");
        sb.Append("' 只负责把 scripts\\watch-external.vbs 跑起来，真正的启动逻辑在那边（单一来源）。\r\n");
        sb.Append("Option Explicit\r\n");
        sb.Append("Dim fso, sh, target\r\n");
        sb.Append("Set fso = CreateObject(\"Scripting.FileSystemObject\")\r\n");
        sb.Append("Set sh  = CreateObject(\"WScript.Shell\")\r\n");
        sb.Append("target = \"" + target + "\"\r\n");
        sb.Append("If Not fso.FileExists(target) Then WScript.Quit 0\r\n");
        sb.Append("sh.Run \"wscript.exe //B //Nologo \"\"\" & target & \"\"\"\", 0, False\r\n");
        return sb.ToString();
    }

    /// <summary>写 .vbs：纯 ASCII 用无 BOM（WSH 最稳），含非 ASCII（例如中文用户名路径）改 UTF-16LE + BOM。</summary>
    private static void WriteScript(string path, string body)
    {
        bool ascii = true;
        foreach (var ch in body) if (ch > 0x7F) { ascii = false; break; }
        File.WriteAllText(path, body, ascii ? new UTF8Encoding(false) : new UnicodeEncoding(false, true));
    }

    private static string TaskXml(string gameRoot)
    {
        string user;
        try { user = System.Security.Principal.WindowsIdentity.GetCurrent().Name; }
        catch { user = Environment.UserDomainName + "\\" + Environment.UserName; }
        var vbs = WatcherVbs(gameRoot);
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n");
        sb.Append("<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n");
        sb.Append("  <RegistrationInfo>\r\n");
        sb.Append("    <Description>Neutraled 守候进程：登录 / 解锁 / 每分钟兜底拉起 --watch-external（选中 Kristal 等外部章节需要它在跑）</Description>\r\n");
        sb.Append("  </RegistrationInfo>\r\n");
        sb.Append("  <Triggers>\r\n");
        sb.Append("    <LogonTrigger>\r\n      <Enabled>true</Enabled>\r\n      <UserId>" + SecurityElement.Escape(user) + "</UserId>\r\n    </LogonTrigger>\r\n");
        sb.Append("    <SessionStateChangeTrigger>\r\n      <Enabled>true</Enabled>\r\n      <UserId>" + SecurityElement.Escape(user) + "</UserId>\r\n      <StateChange>SessionUnlock</StateChange>\r\n    </SessionStateChangeTrigger>\r\n");
        sb.Append("    <TimeTrigger>\r\n");
        sb.Append("      <Repetition>\r\n        <Interval>PT1M</Interval>\r\n        <StopAtDurationEnd>false</StopAtDurationEnd>\r\n      </Repetition>\r\n");
        sb.Append("      <StartBoundary>2020-01-01T00:00:00</StartBoundary>\r\n      <Enabled>true</Enabled>\r\n    </TimeTrigger>\r\n");
        sb.Append("  </Triggers>\r\n");
        sb.Append("  <Principals>\r\n    <Principal id=\"Author\">\r\n      <UserId>" + SecurityElement.Escape(user) + "</UserId>\r\n");
        sb.Append("      <LogonType>InteractiveToken</LogonType>\r\n      <RunLevel>LeastPrivilege</RunLevel>\r\n    </Principal>\r\n  </Principals>\r\n");
        sb.Append("  <Settings>\r\n");
        sb.Append("    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n");
        sb.Append("    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n");
        sb.Append("    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n");
        sb.Append("    <AllowHardTerminate>true</AllowHardTerminate>\r\n");
        sb.Append("    <StartWhenAvailable>true</StartWhenAvailable>\r\n");
        sb.Append("    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>\r\n");
        sb.Append("    <IdleSettings>\r\n      <StopOnIdleEnd>false</StopOnIdleEnd>\r\n      <RestartOnIdle>false</RestartOnIdle>\r\n    </IdleSettings>\r\n");
        sb.Append("    <AllowStartOnDemand>true</AllowStartOnDemand>\r\n    <Enabled>true</Enabled>\r\n    <Hidden>false</Hidden>\r\n");
        sb.Append("    <RunOnlyIfIdle>false</RunOnlyIfIdle>\r\n    <WakeToRun>false</WakeToRun>\r\n    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n    <Priority>7</Priority>\r\n");
        sb.Append("  </Settings>\r\n");
        sb.Append("  <Actions Context=\"Author\">\r\n    <Exec>\r\n      <Command>wscript.exe</Command>\r\n");
        sb.Append("      <Arguments>//B //Nologo \"" + SecurityElement.Escape(vbs) + "\"</Arguments>\r\n    </Exec>\r\n  </Actions>\r\n");
        sb.Append("</Task>\r\n");
        return sb.ToString();
    }

    // ───────────────────── 脱离式启动（不继承句柄） ─────────────────────
    //
    // ★ 踩过的坑：用 Process.Start 起守候时，子进程会继承父进程的 stdout/stderr 句柄。
    //   守候是常驻进程（默认活 12 小时），于是**父进程的调用方**永远等不到 EOF：
    //   实测在 PowerShell 里 `& ntl-builder --ensure-watcher ... *> log` 直接卡死整条流水线
    //   （GUI 的 RunBuilderAsync 读 stdout 到 EOF、部署脚本重定向输出，都会同样中招）。
    //   这里用 DETACHED_PROCESS + bInheritHandles=false 起进程：不继承任何句柄、没有控制台，
    //   子进程的标准句柄无效 ⇒ .NET 退化成丢弃写入（守候本来就在 GoSilent 里把输出改到日志文件）。
    private const uint DETACHED_PROCESS = 0x00000008;
    private const uint CREATE_NEW_PROCESS_GROUP = 0x00000200;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public IntPtr lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(string? lpApplicationName, string lpCommandLine,
        IntPtr lpProcessAttributes, IntPtr lpThreadAttributes, bool bInheritHandles, uint dwCreationFlags,
        IntPtr lpEnvironment, string? lpCurrentDirectory, ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    /// <summary>完全脱离地启动子进程（无控制台、不继承句柄）。成功返回 true。</summary>
    private static bool SpawnDetached(string exe, string args, string workDir)
    {
        if (!Platform.IsWindows) return false;
        try
        {
            var si = new STARTUPINFO();
            si.cb = Marshal.SizeOf<STARTUPINFO>();
            var cmd = "\"" + exe + "\" " + args;
            if (!CreateProcessW(null, cmd, IntPtr.Zero, IntPtr.Zero, false,
                    DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP, IntPtr.Zero, workDir, ref si, out var pi))
                return false;
            try { CloseHandle(pi.hThread); CloseHandle(pi.hProcess); } catch { }
            return true;
        }
        catch { return false; }
    }

    /// <summary>跑 schtasks 并抓输出（找不到命令时返回 -1 + 说明）。</summary>
    private static (int rc, string output) RunSchtasks(string args)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("schtasks.exe", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p == null) return (-1, L("无法启动 schtasks.exe"));
            var so = p.StandardOutput.ReadToEndAsync();
            var se = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(20000)) { try { p.Kill(true); } catch { } return (-1, L("schtasks.exe 超时")); }
            var outp = (so.Result ?? "") + (se.Result ?? "");
            return (p.ExitCode, outp);
        }
        catch (Exception ex) { return (-1, ex.Message); }
    }
}
