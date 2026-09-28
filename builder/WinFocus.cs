using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>
/// 把窗口强行拉到前台（守候进程是后台进程，直接 SetForegroundWindow **会被 Windows 拒绝**）。
///
/// 实测（用户反馈「窗口聚焦没生效」）：原来的实现只调了一次 SetForegroundWindow，
/// 而它返回 false 也没人看 —— 于是日志写着「已把焦点交给外部引擎窗口」，焦点其实还在那个
/// **已被隐藏的 DELTARUNE 窗口**上，玩家在 Kristal 里按 F2 当然没反应。
///
/// 这里用业界通行的组合拳，每步都用 GetForegroundWindow() 复核：
///   ① AttachThreadInput 把本线程挂到「当前前台线程」和「目标窗口线程」上（绕开前台锁）
///   ② BringWindowToTop + SetForegroundWindow + SetFocus
///   ③ ALT 键轻敲（让系统把本进程视作刚产生输入）
///   ④ SwitchToThisWindow 兜底（未公开但一直存在的 API）
///   ⑤ 置顶再取消置顶 + SetForegroundWindow 兜底
/// </summary>
internal static class WinFocus
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr h);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] private static extern void SwitchToThisWindow(IntPtr h, bool fUnknown);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    [DllImport("user32.dll")] private static extern uint MapVirtualKeyW(uint code, uint mapType);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    private delegate bool EnumProc(IntPtr h, IntPtr l);

    private const int SW_HIDE = 0, SW_SHOW = 5, SW_RESTORE = 9;
    private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private static readonly IntPtr HWND_TOPMOST = new(-1), HWND_NOTOPMOST = new(-2);
    private const byte VK_MENU = 0x12, VK_F2 = 0x71;

    /// <summary>非 Windows：整个类退化为空实现。窗口/前台/按键消息都是 Win32 概念，Linux 与 macOS 上
    /// 没有等价 API；更关键的是 DllNotFoundException 是**调用期**异常，内层 try 不一定兜得住，
    /// 所以每个公开入口第一行判一次，直接返回安全默认值（内部私有 helper 只在 Windows 分支被调用）。</summary>
    private static bool Off => !Platform.IsWindows;

    public static IntPtr Foreground() { if (Off) return IntPtr.Zero; try { return GetForegroundWindow(); } catch { return IntPtr.Zero; } }

    public static int ForegroundPid() => PidOf(Foreground());

    /// <summary>持续把目标进程的窗口顶到前台，直到成功或超时。
    /// ⚠ 必须"持续"：Kristal 加载完成后会**重建窗口**（改标题/图标），
    ///   早期抢到的那个句柄随即失效 —— 一次性抢焦点必然白抢（实测：抢完前台又变回别的窗口）。
    /// ⚠ 判定要按 **pid**，不能按句柄：句柄会变，pid 不会。</summary>
    public static bool KeepForeground(int pid, int timeoutMs = 20000)
    {
        if (Off) return false;
        var sw = Stopwatch.StartNew();
        IntPtr last = IntPtr.Zero;
        int stable = 0;
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var h = FindWindow(pid, true);
            if (h != IntPtr.Zero)
            {
                if (h != last) { last = h; stable = 0; }
                else stable++;
                TryAttach(h);
                if (Foreground() == h && stable >= 2) return true;
            }
            Thread.Sleep(400);
        }
        return ForegroundPid() == pid;
    }

    public static string Title(IntPtr h)
    {
        if (h == IntPtr.Zero) return L("(无)");
        var sb = new StringBuilder(256);
        GetWindowTextW(h, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string ClassName(IntPtr h)
    {
        if (h == IntPtr.Zero) return "";
        var sb = new StringBuilder(128);
        GetClassNameW(h, sb, sb.Capacity);
        return sb.ToString();
    }

    public static int PidOf(IntPtr h)
    {
        if (h == IntPtr.Zero) return 0;
        GetWindowThreadProcessId(h, out uint pid);
        return (int)pid;
    }

    public static string Describe(IntPtr h)
    {
        if (h == IntPtr.Zero) return L("(无前台窗口)");
        return L("hwnd=0x{0:X} pid={1} class={2} 标题=[{3}]", h.ToInt64(), PidOf(h), ClassName(h), Title(h));
    }

    /// <summary>枚举某个进程的顶层窗口。⚠ 不能用 Process.MainWindowHandle：窗口隐藏后它是 0（踩过）。</summary>
    public static IntPtr FindWindow(int pid, bool requireVisible)
    {
        if (Off) return IntPtr.Zero;
        IntPtr found = IntPtr.Zero;
        try
        {
            EnumWindows((h, l) =>
            {
                GetWindowThreadProcessId(h, out uint p);
                if ((int)p != pid) return true;
                if (GetWindowTextW(h, new StringBuilder(4), 4) == 0) { /* 标题长度单独判断 */ }
                var sb = new StringBuilder(256);
                GetWindowTextW(h, sb, sb.Capacity);
                if (sb.Length == 0) return true;                       // 无标题的多是隐藏辅助窗口
                if (requireVisible && !IsWindowVisible(h)) return true;
                found = h;
                return false;
            }, IntPtr.Zero);
        }
        catch { }
        return found;
    }

    /// <summary>等目标进程出现可见顶层窗口。</summary>
    public static IntPtr WaitWindow(Process proc, int timeoutMs)
    {
        if (Off) return IntPtr.Zero;
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            try
            {
                proc.Refresh();
                if (proc.HasExited) return IntPtr.Zero;
                var h = FindWindow(proc.Id, true);
                if (h != IntPtr.Zero) return h;
            }
            catch { }
            Thread.Sleep(120);
        }
        return IntPtr.Zero;
    }

    private static void AltNudge()
    {
        try
        {
            keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
            keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        catch { }
    }

    /// <summary>把 h 拉到前台；返回是否真的成为前台窗口。
    /// 后台进程抢前台会被 Windows 拦（前台锁），所以这里把几套"组合拳"都试一遍，每步都复核。</summary>
    public static bool ForceForeground(IntPtr h)
    {
        if (Off || h == IntPtr.Zero) return false;
        for (int round = 0; round < 4; round++)
        {
            if (TryAttach(h)) return true;
            if (TryMinRestore(h)) return true;
            if (TryTopmost(h)) return true;
            try { SwitchToThisWindow(h, true); } catch { }
            if (Settled(h)) return true;
            Thread.Sleep(250);
        }
        return false;
    }

    /// <summary>① 挂到前台线程 + 目标线程的输入队列上（绕过前台锁）——最常见有效的一招。
    /// ⚠ ALT 轻敲必须放在 AttachThreadInput **之后**：这样"最后一个输入事件"才算到我们头上。</summary>
    private static bool TryAttach(IntPtr h)
    {
        try
        {
            uint myTid = GetCurrentThreadId();
            IntPtr fg = GetForegroundWindow();
            uint fgTid = fg == IntPtr.Zero ? 0 : GetWindowThreadProcessId(fg, out _);
            uint tgTid = GetWindowThreadProcessId(h, out _);

            bool a1 = fgTid != 0 && fgTid != myTid && AttachThreadInput(myTid, fgTid, true);
            bool a2 = tgTid != 0 && tgTid != myTid && AttachThreadInput(myTid, tgTid, true);
            try
            {
                ShowWindow(h, IsIconic(h) ? SW_RESTORE : SW_SHOW);
                AltNudge();
                BringWindowToTop(h);
                SetForegroundWindow(h);
                SetFocus(h);
            }
            finally
            {
                if (a2) AttachThreadInput(myTid, tgTid, false);
                if (a1) AttachThreadInput(myTid, fgTid, false);
            }
            return Settled(h);
        }
        catch { return false; }
    }

    /// <summary>② 最小化再还原：Windows 对"还原"的窗口通常会直接给前台（很土但很有效）。</summary>
    private static bool TryMinRestore(IntPtr h)
    {
        try
        {
            if (!IsIconic(h))
            {
                ShowWindow(h, 6);            // SW_MINIMIZE
                Thread.Sleep(220);
            }
            ShowWindow(h, SW_RESTORE);
            SetForegroundWindow(h);
            return Settled(h);
        }
        catch { return false; }
    }

    /// <summary>③ 置顶再取消置顶（至少保证它露在最上面）。</summary>
    private static bool TryTopmost(IntPtr h)
    {
        try
        {
            SetWindowPos(h, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
            SetForegroundWindow(h);
            SetWindowPos(h, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
            return Settled(h);
        }
        catch { return false; }
    }

    /// <summary>直接给窗口发按键消息（不依赖焦点）—— 自检用，也用于兜底验证。</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr PostMessageW(IntPtr h, uint msg, IntPtr w, IntPtr l);
    private const uint WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;

    private const uint WM_CHAR = 0x0102;

    /// <summary>按「文本输入」通道发字符（WM_CHAR → SDL_TEXTINPUT → love.textinput）。</summary>
    public static void PostText(IntPtr h, string s)
    {
        if (Off) return;
        foreach (var ch in s)
        {
            PostMessageW(h, WM_CHAR, (IntPtr)ch, (IntPtr)1);
            Thread.Sleep(45);
        }
    }

    /// <summary>按「键盘」通道发字符（WM_KEYDOWN → love.keypressed）—— 模拟中文输入法开着时的情形。</summary>
    public static void PostKeyString(IntPtr h, string s)
    {
        foreach (var ch in s)
        {
            byte vk = ch == ' ' ? (byte)0x20 : (byte)char.ToUpperInvariant(ch);
            PostKey(h, vk);
            Thread.Sleep(45);
        }
    }

    public static void PostEnter(IntPtr h) { PostKey(h, 0x0D); }

    public static void PostKey(IntPtr h, byte vk)
    {
        if (Off) return;
        byte scan = (byte)MapVirtualKeyW(vk, 0);
        IntPtr l = (IntPtr)(1 | (scan << 16));
        PostMessageW(h, WM_KEYDOWN, (IntPtr)vk, l);
        Thread.Sleep(60);
        PostMessageW(h, WM_KEYUP, (IntPtr)vk, l | unchecked((IntPtr)(1 << 30)) | unchecked((IntPtr)(1 << 31)));
    }

    private static bool Settled(IntPtr h)
    {
        for (int i = 0; i < 6; i++)
        {
            Thread.Sleep(45);
            if (GetForegroundWindow() == h) return true;
        }
        return false;
    }

    /// <summary>朝当前前台窗口敲一个键（用扫描码，SDL/LÖVE 才认）。</summary>
    public static void SendKey(byte vk)
    {
        if (Off) return;
        byte scan = (byte)MapVirtualKeyW(vk, 0);
        keybd_event(vk, scan, 0, UIntPtr.Zero);
        Thread.Sleep(40);
        keybd_event(vk, scan, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    public static void Show(IntPtr h, bool visible)
    {
        try { if (h != IntPtr.Zero) ShowWindow(h, visible ? SW_SHOW : SW_HIDE); } catch { }
    }

    public static bool IsVisible(IntPtr h)
    {
        try { return h != IntPtr.Zero && IsWindowVisible(h); } catch { return false; }
    }

    // ---------------------------------------------------------------- 自检

    /// <summary>--focus-test：拉起一个程序，验证「后台进程把焦点抢过来」这套组合拳真的有效。</summary>
    public static int FocusTest(string exe, bool sendF2, bool keep, string? viaChar = null, string? viaKeys = null)
    {
        if (Off) { Console.WriteLine(L("  [错误] 窗口抢焦点只在 Windows 上可用（当前 {0}）", Platform.Name)); return 3; }
        exe = Path.GetFullPath(exe);
        if (!File.Exists(exe)) { Console.WriteLine(L("  [错误] 找不到 {0}", exe)); return 1; }
        Console.WriteLine(L("===== 焦点抢占自检 ====="));
        Console.WriteLine(L("  测试前前台: {0}", Describe(Foreground())));
        var proc = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! })!;
        var h0 = WaitWindow(proc, 20000);
        if (h0 == IntPtr.Zero) { Console.WriteLine(L("  [错误] 20 秒内没等到窗口")); if (!keep) try { proc.Kill(); } catch { } return 1; }
        Console.WriteLine(L("  目标窗口:   {0}", Describe(h0)));
        bool ok = KeepForeground(proc.Id, 20000);
        var hNow = FindWindow(proc.Id, true);
        Console.WriteLine(L("  抢焦点结果: {0}   （pid={1}）", (ok ? L("成功 ✓") : L("失败 ✗")), proc.Id));
        Console.WriteLine(L("  测试后前台: {0}", Describe(Foreground())));
        Console.WriteLine(L("  与目标一致: {0}   当前窗口={1}", (ForegroundPid() == proc.Id ? L("是 ✓") : L("否 ✗")), Describe(hNow)));

        if (sendF2)
        {
            Console.WriteLine(L("  等引擎启动完成后敲 F2（真键盘，和玩家按的一样）…"));
            Thread.Sleep(6000);
            var h = FindWindow(proc.Id, true);
            if (h == IntPtr.Zero) { Console.WriteLine(L("  [错误] 找不到窗口")); }
            else
            {
                if (Foreground() != h) TryAttach(h);
                Console.WriteLine(L("  发送时前台: {0}", Describe(Foreground())));
                SendKey(VK_F2);
                Thread.Sleep(1500);
                if (!string.IsNullOrEmpty(viaChar))
                {
                    Console.WriteLine(L("  用【文本通道】输入 \"{0}\" + 回车 …", viaChar));
                    PostText(h, viaChar!); Thread.Sleep(200); PostEnter(h); Thread.Sleep(1500);
                }
                if (!string.IsNullOrEmpty(viaKeys))
                {
                    Console.WriteLine(L("  用【键盘通道】输入 \"{0}\" + 回车（模拟输入法开着）…", viaKeys));
                    PostKeyString(h, viaKeys!); Thread.Sleep(200); PostEnter(h); Thread.Sleep(1500);
                }
            }
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var cands = new[] { Path.Combine(appData, "kristal", "ntlconsole.log"),
                                Path.Combine(appData, "LOVE", "kristal", "ntlconsole.log") };
            var logPath = cands.FirstOrDefault(File.Exists);
            if (logPath != null)
            {
                var lines = File.ReadAllLines(logPath, Encoding.UTF8);
                Console.WriteLine(L("  控制台日志 {0}（最后 {1} 行）:", logPath, Math.Min(6, lines.Length)));
                foreach (var l in lines.Skip(Math.Max(0, lines.Length - 6))) Console.WriteLine("      " + l);
            }
            else Console.WriteLine(L("  [提示] 没写出控制台日志（找过 {0}）—— 说明钩子没生效", string.Join(" / ", cands)));
        }
        if (!keep)
        {
            try { proc.Kill(); } catch { }
            Console.WriteLine(L("  已结束测试进程（--keep 可保留）"));
        }
        else Console.WriteLine(L("  进程保留中 pid={0}", proc.Id));
        return ok ? 0 : 2;
    }
}
