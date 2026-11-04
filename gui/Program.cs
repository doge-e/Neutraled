using System.Diagnostics;

namespace Neutraled.Gui;

/// <summary>Neutraled GUI 管理器入口。</summary>
public static class Program
{
    /// <summary>游戏根目录（从 exe 上溯探测，无硬编码）。</summary>
    public static string GameRoot => DetectGameRoot();

    public static string NeutraledRoot => Path.Combine(GameRoot, "Neutraled");

    public static string BuilderExe =>
        Path.Combine(NeutraledRoot, "builder", "bin", "Release", "net9.0", "ntl-builder.exe");

    public static string ModsRoot => Path.Combine(NeutraledRoot, "mods");

    public static string DetectGameRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 12 && dir != null; i++)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DELTARUNE.exe")) &&
                Directory.Exists(Path.Combine(dir.FullName, "chapter1_windows")))
                return dir.FullName;
            dir = dir.Parent;
        }
        dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 12 && dir != null; i++)
        {
            if (string.Equals(dir.Name, "Neutraled", StringComparison.OrdinalIgnoreCase))
                return dir.Parent?.FullName ?? dir.FullName;
            dir = dir.Parent;
        }
        return AppContext.BaseDirectory;
    }

    [STAThread]
    public static void Main(string[] args)
    {
        // 自检/应急入口（都不开窗口、不部署、不碰游戏目录）：
        //   ntl-gui.exe --print-deploy-args [--fast-deploy on|off]  → 打印三条部署路径拼出的命令行
        //   ntl-gui.exe --check-toolbox-layout [zh|en]              → 量工具箱对话框的控件边界/文字高度
        //   ntl-gui.exe --set-fast-deploy on|off                    → 直接改设置（等价于工具箱里勾选）
        // 自检命令必须用退出码表达结论（GUI-M09）：0=通过、1=发现问题、2=参数错误。
        if (args.Any(a => a.Equals("--check-toolbox-layout", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.ExitCode = CheckToolboxLayout(args);
            return;
        }

        if (args.Any(a => a.Equals("--print-deploy-args", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.ExitCode = PrintDeployArgs(args);
            return;
        }

        // 应急入口（等价于工具箱里勾选，GUI 打不开时也能改；不启动窗口）：
        // 用法: ntl-gui.exe --set-fast-deploy on|off
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i].Equals("--set-fast-deploy", StringComparison.OrdinalIgnoreCase))
            {
                try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }
                // GUI-L12：非法值不能静默当 off 写盘（那会把玩家明确的设置悄悄改掉）。
                if (!DeployOptions.TryParseFlag(args[i + 1], out var fast))
                {
                    Console.Error.WriteLine("[selfcheck] ERROR: --set-fast-deploy 只接受 on|off（也认 true/false、1/0、yes/no），收到: " + args[i + 1]);
                    Environment.ExitCode = 2;
                    return;
                }
                DeployOptions.Load(NeutraledRoot);
                DeployOptions.FastDeploy = fast;
                DeployOptions.Save(NeutraledRoot);
                Console.WriteLine("[selfcheck] saved fast-deploy = " + (DeployOptions.FastDeploy ? "on" : "off")
                    + " -> " + DeployOptions.SettingsPath(NeutraledRoot));
                return;
            }

        // 无窗口自检/自动化入口（C4 P1-12）：绝不创建窗口，只读文件或转发 builder。
        //   ntl-gui.exe --headless alerts|selftest|plan|deploy-state|lang-report [--json]
        if (Headless.IsHeadless(args))
        {
            Environment.ExitCode = Headless.Run(args);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }

    /// <summary>结构自检：构造工具箱对话框（不显示），量控件边界/文字高度并打印开关与代价文案。
    /// 用法: ntl-gui.exe --check-toolbox-layout [zh|en]（不带语言就沿用 config.json 的 lang）。</summary>
    private static int CheckToolboxLayout(string[] args)
    {
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }
        using var owner = new MainForm();          // 构造里会 Localizer.Load(config.json.lang)，所以语言在这之后再定
        foreach (var a in args)
            if (a is "zh" or "en") Localizer.Current = a;
        Console.WriteLine($"[layout] language = {Localizer.Current}");

        using var dlg = owner.BuildToolboxDialog();
        dlg.CreateControl();
        dlg.PerformLayout();
        Console.WriteLine($"[layout] client area = {dlg.ClientSize.Width} x {dlg.ClientSize.Height}");
        int bad = 0;
        foreach (Control c in dlg.Controls)
        {
            bool over = c.Right > dlg.ClientSize.Width || c.Bottom > dlg.ClientSize.Height;
            string extra = "";
            if (c is Label lb && lb.Text.Length > 0)
            {
                var need = TextRenderer.MeasureText(lb.Text, lb.Font,
                    new Size(Math.Max(1, lb.Width), int.MaxValue), TextFormatFlags.WordBreak);
                bool clipped = need.Height > lb.Height + 1;
                if (clipped) over = true;
                extra = $"  needH={need.Height,3} {(clipped ? "TEXT-CLIPPED" : "text-fits")} | " + lb.Text.Replace(Environment.NewLine, " ⏎ ");
            }
            // 判据不写死中文子串：直接与工具箱里那个开关的当前语言文本比对（改文案时两边同一个 key，不会失配）
            else if (c is CheckBox cb) extra = "  | " + cb.Text + (cb.Text == Localizer.T("部署加速档（跳过输入重定向）") ? $"  [Checked={cb.Checked}]" : "");
            if (over) bad++;
            Console.WriteLine($"[layout] {c.GetType().Name,-10} T={c.Top,4} H={c.Height,3} B={c.Bottom,4} W={c.Width,4} R={c.Right,4} {(over ? "OVERFLOW" : "ok")}{extra}");
        }
        Console.WriteLine(bad == 0 ? "[layout] RESULT: all controls fit" : $"[layout] RESULT: {bad} control(s) overflow");
        // GUI-M09：出口码就是结论（原先恒为 0，脚本/CI 无法据此判失败）。
        return bad == 0 ? 0 : 1;
    }

    /// <summary>自检：按当前设置（或用 --fast-deploy on/off 临时覆盖）打印三条部署命令行。</summary>
    private static int PrintDeployArgs(string[] args)
    {
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }
        DeployOptions.Load(NeutraledRoot);
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i].Equals("--fast-deploy", StringComparison.OrdinalIgnoreCase))
            {
                // GUI-L12：非法值报错退出，不静默当 off。
                if (!DeployOptions.TryParseFlag(args[i + 1], out var fast))
                {
                    Console.Error.WriteLine("[selfcheck] ERROR: --fast-deploy 只接受 on|off，收到: " + args[i + 1]);
                    return 2;
                }
                DeployOptions.FastDeploy = fast;
                break;
            }

        Console.WriteLine("[selfcheck] fast-deploy = " + (DeployOptions.FastDeploy ? "on" : "off"));
        Console.WriteLine("[selfcheck] settings    = " + DeployOptions.SettingsPath(NeutraledRoot));
        // 与 GUI 实际行为保持一致（UX-04）：【部署】= --launch <章节> --no-start（写部署会话、走缓存，只是不拉起 Steam）。
        Console.WriteLine("[selfcheck] deploy          : " + DeployOptions.AppendFastDeploy("--launch chapter1 --no-start"));
        Console.WriteLine("[selfcheck] deploy all      : " + DeployOptions.AppendFastDeploy("--launch all --no-start"));
        Console.WriteLine("[selfcheck] deploy + launch : " + DeployOptions.AppendFastDeploy("--launch chapter1"));
        return 0;
    }

    /// <summary>后台起一个"外部章节守候"进程（不阻塞 GUI）。
    /// 游戏里选中 Kristal 章节时会写 launch-request.json 并退出，守候进程看到请求就拉起 Kristal 引擎。
    /// 为什么必须这样：实测 GML 运行时没有任何启动进程的内置函数（execute_program 等都不存在）。</summary>
    public static void StartExternalWatcher()
    {
        try
        {
            if (!File.Exists(BuilderExe)) return;
            Process.Start(new ProcessStartInfo
            {
                FileName = BuilderExe,
                Arguments = "--watch-external",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch { }
    }

    /// <summary>运行 builder，实时回传输出行。</summary>
    public static async Task<int> RunBuilderAsync(string args, Action<string>? onLine = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(BuilderExe))
        {
            onLine?.Invoke(Localizer.T("[错误] 找不到 ntl-builder.exe: {0}", BuilderExe));
            return -1;
        }
        var psi = new ProcessStartInfo
        {
            FileName = BuilderExe,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            WorkingDirectory = NeutraledRoot
        };
        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => { if (e.Data != null) onLine?.Invoke(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) onLine?.Invoke(e.Data); };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        using var reg = ct.Register(() => { try { if (!p.HasExited) p.Kill(true); } catch { } });
        await p.WaitForExitAsync(ct);
        return p.ExitCode;
    }

    // C4 P1-13 / P1-6：这里原来有一个 LaunchGame()（裸 steam:// 启动，绕过 --launch 的部署与缓存），
    // 已无任何调用点 —— 留着只会让人误以为还能用，所以删除。
}
