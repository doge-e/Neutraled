using System.Text.Json;

namespace Neutraled.Studio;

public static class Program
{
    public static string GameRoot => DetectGameRoot();
    public static string NeutraledRoot => Path.Combine(GameRoot, "Neutraled");
    public static string LiveRoot => Path.Combine(NeutraledRoot, "live");
    public static string ModsRoot => Path.Combine(NeutraledRoot, "mods");
    /// <summary>v1.1.0：Studio 并进 ntl-gui.exe，所以"再拉起 builder"= 再拉起自己（--deploy / --launch）。
    /// 保留 BuilderExe 这个名字（--headless selftest 的 JSON 字段也从 1.0.x 就叫这个，别让脚本失配）。</summary>
    public static string SelfExe => Path.Combine(AppContext.BaseDirectory, "ntl-gui.exe");
    public static string BuilderExe => Environment.ProcessPath ?? SelfExe;
    public static string ApiRegistry => Path.Combine(NeutraledRoot, "api-registry.json");

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

    /// <summary>自检模式（--selftest）：启动后自动触发补全等交互，便于无人值守验证。</summary>
    public static bool SelfTest { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        // UX-29：--headless 只跑可脚本化检查，不创建窗口（selftest / plan）。
        if (args.Any(a => a.Equals("--headless", StringComparison.OrdinalIgnoreCase)))
            return RunHeadless(args);
        SelfTest = args.Any(a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase));
        // v1.1.0：Studio 是库（不再产出 exe），WinForms 源生成器不会生成 ApplicationConfiguration，
        // 这里显式做同样的初始化（等价于 ApplicationConfiguration.Initialize() 的默认行为）。
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.Run(new MainForm());
        return 0;
    }

    /// <summary>Studio 的无窗口入口：--headless selftest 检查环境；--headless plan [--chapter X] 打印部署参数。</summary>
    private static int RunHeadless(string[] args)
    {
        var tail = args.SkipWhile(a => !a.Equals("--headless", StringComparison.OrdinalIgnoreCase)).Skip(1).ToArray();
        var action = tail.FirstOrDefault(a => !a.StartsWith("-", StringComparison.Ordinal))?.ToLowerInvariant() ?? "selftest";
        return action switch
        {
            "selftest" => HeadlessSelfTest(),
            "plan" => HeadlessPlan(tail),
            _ => HeadlessUsage(action)
        };
    }

    private static string HeadlessOption(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return "";
    }

    private static int HeadlessUsage(string action)
    {
        Console.Error.WriteLine("ntl-gui.exe studio --headless selftest | --headless plan [--chapter root|chapter1..chapter5|all]");
        Console.Error.WriteLine("unknown headless action: " + action);
        return 2;
    }

    private static int HeadlessSelfTest()
    {
        var checks = new List<(string Name, bool Ok, string Value)>
        {
            ("gameRoot", Directory.Exists(GameRoot), GameRoot),
            ("neutraledRoot", Directory.Exists(NeutraledRoot), NeutraledRoot),
            ("liveRoot", Directory.Exists(LiveRoot), LiveRoot),
            ("modsRoot", Directory.Exists(ModsRoot), ModsRoot),
            ("builderExe", File.Exists(BuilderExe), BuilderExe),
            ("apiRegistry", File.Exists(ApiRegistry), ApiRegistry)
        };
        bool ok = true;
        foreach (var c in checks) ok &= c.Ok;
        var result = new
        {
            ok,
            action = "selftest",
            gameRoot = GameRoot,
            neutraledRoot = NeutraledRoot,
            builderExe = BuilderExe,
            checks = checks.Select(c => new { c.Name, c.Ok, c.Value })
        };
        Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = false }));
        return ok ? 0 : 1;
    }

    private static int HeadlessPlan(string[] args)
    {
        var chapter = HeadlessOption(args, "--chapter");
        if (chapter.Length == 0) chapter = "root";
        var valid = chapter is "root" or "all" or "chapter1" or "chapter2" or "chapter3" or "chapter4" or "chapter5";   // ntl:chapter4-ok 显式章节白名单
        var result = new
        {
            ok = valid,
            action = "plan",
            chapter,
            args = "--deploy --chapter " + chapter,
            error = valid ? "" : "invalid chapter: " + chapter
        };
        Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = false }));
        return valid ? 0 : 2;
    }

    /// <summary>运行 builder 并实时回传输出。</summary>
    public static async Task<int> RunBuilderAsync(string args, Action<string>? onLine = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(BuilderExe)) { onLine?.Invoke(Localizer.T("[错误] 找不到 ntl-gui.exe")); return -1; }
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = BuilderExe, Arguments = args,
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            WorkingDirectory = NeutraledRoot
        };
        using var p = new System.Diagnostics.Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => { if (e.Data != null) onLine?.Invoke(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) onLine?.Invoke(e.Data); };
        p.Start();
        p.BeginOutputReadLine(); p.BeginErrorReadLine();
        using var reg = ct.Register(() => { try { if (!p.HasExited) p.Kill(true); } catch { } });
        await p.WaitForExitAsync(ct);
        return p.ExitCode;
    }

    /// <summary>C4 P1-6：启动游戏统一走 builder --launch（部署/缓存/会话/自愈一条链），不再裸 steam://。
    ///   bare --launch = root；需要章节请用 ntl-gui、CLI，或 Studio 工具栏的章节选择（该选择只用于部署）。</summary>
    public static async Task LaunchGameAsync(Action<string>? onLine = null)
    {
        try
        {
            var rc = await RunBuilderAsync("--launch", onLine);
            if (rc != 0)
                MessageBox.Show(Localizer.T("启动失败（退出码 {0}）；详见日志", rc), "Neutraled Studio");
        }
        catch (Exception ex) { MessageBox.Show(Localizer.T("启动游戏失败: {0}", ex.Message), "Neutraled Studio"); }
    }

    /// <summary>请求游戏热重载 live 脚本（写标志文件，游戏内检测后执行）。</summary>
    public static void RequestHotReload()
    {
        try
        {
            Directory.CreateDirectory(NeutraledRoot);
            File.WriteAllText(Path.Combine(NeutraledRoot, "reload.flag"),
                DateTime.Now.ToString("O"));
        }
        catch { }
    }

    /// <summary>读取游戏运行时日志尾部。</summary>
    public static string ReadGameLog(int maxLines = 400)
    {
        try
        {
            var p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DELTARUNE", "Neutraled", "dr-api.log");
            if (!File.Exists(p)) return "";
            var lines = File.ReadAllLines(p);
            return string.Join(Environment.NewLine, lines.TakeLast(maxLines));
        }
        catch { return ""; }
    }
}
