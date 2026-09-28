namespace Neutraled.Studio;

public static class Program
{
    public static string GameRoot => DetectGameRoot();
    public static string NeutraledRoot => Path.Combine(GameRoot, "Neutraled");
    public static string LiveRoot => Path.Combine(NeutraledRoot, "live");
    public static string ModsRoot => Path.Combine(NeutraledRoot, "mods");
    public static string BuilderExe => Path.Combine(NeutraledRoot, "builder", "bin", "Release", "net9.0", "ntl-builder.exe");
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
    public static void Main(string[] args)
    {
        SelfTest = args.Any(a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase));
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }

    /// <summary>运行 builder 并实时回传输出。</summary>
    public static async Task<int> RunBuilderAsync(string args, Action<string>? onLine = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(BuilderExe)) { onLine?.Invoke(Localizer.T("[错误] 找不到 ntl-builder.exe")); return -1; }
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

    public static void LaunchGame()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            { FileName = "steam://rungameid/1671210", UseShellExecute = true });
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
