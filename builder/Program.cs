using System.Text.Json;
using UndertaleModLib;
using System.Text.Json.Nodes;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

public static class Program
{
    /// <summary>比较版本号：a &gt; b 返回 1，相等 0，小于 -1（按点分段数字比较，非法段按 0）</summary>
    private static int CompareVersion(string? a, string? b)
    {
        var pa = (a ?? "0").Split('.', '-', '+');
        var pb = (b ?? "0").Split('.', '-', '+');
        var n = Math.Max(pa.Length, pb.Length);
        for (int i = 0; i < n; i++)
        {
            int va = (i < pa.Length && int.TryParse(pa[i], out var x)) ? x : 0;
            int vb = (i < pb.Length && int.TryParse(pb[i], out var y)) ? y : 0;
            if (va != vb) return va > vb ? 1 : -1;
        }
        return 0;
    }

    /// <summary>供 SelfTest 调用的部署入口</summary>
    public static int DeployAllForTest(string gameRoot, string chapter) => DeployAll(gameRoot, chapter);

    /// <summary>上次 Deploy 是否因签名一致走了幂等跳过 —— 语言无关的结构化信号，供 SelfTest 探测（原先是 grep 中文字面量，en 模式下会误判）</summary>
    public static bool LastDeploySkipped;

    private static int ModInstallListCli(string gameRoot)
    {
        ModInstall.ListInstalled(gameRoot);
        return 0;
    }

    private static int KristalValidateCli(string src)
    {
        KristalValidate.Validate(src);
        return 0;
    }

    private static int KristalConvertCli(string src)
    {
        var outDir = Path.Combine(Paths.NeutraledRoot(Paths.DetectGameRoot()), "kristal-converted");
        KristalConvert.Convert(src, outDir);
        return 0;
    }

    private static int SmokeCli(string gameRoot)
    {
        var rep = Smoke.Run(gameRoot);
        return (rep.Broken > 0) ? 1 : 0;
    }

    private static int DoctorCli(string gameRoot)
    {
        var rep = Doctor.Run(gameRoot);
        return (rep.Count("error") > 0) ? 1 : 0;
    }

    private static int CacheDedupCli(string gameRoot)
    {
        var root = Paths.NeutraledRoot(gameRoot);
        Console.WriteLine(L("===== 缓存去重 ====="));
        CacheOpt.Deduplicate(Path.Combine(root, "cache"));
        return 0;
    }

    private static int SaveGuardListCli(string gameRoot)
    {
        SaveGuard.List(gameRoot);
        return 0;
    }

    /// <summary>Web UI 的部署钩子：部署一个章节（与命令行 --deploy --chapter X 完全同路径）。</summary>
    public static int DeployHookForWeb(string gameRoot, string chapter) => DeployAll(gameRoot, chapter);

    /// <summary>插件宿主：部署/导入这类「有插件参与」的流程开始前调一次。
    /// 没有插件时完全安静（不打印、不建目录），所以不会污染既有套件的输出断言。</summary>
    public static void PluginBoot(string gameRoot)
    {
        try { if (!PluginHost.IsLoaded) PluginHost.Load(gameRoot); }
        catch (Exception ex) { Paths.Log(L("[插件] 宿主加载失败: {0}", ex.Message)); }
    }

    /// <summary>触发插件钩子，返回非 0 = 插件否决（调用方应当中止并返回该码）。
    /// 钩子是「协作式」的：插件即进程内程序集，这里只保证「没声明权限的一定被拒」。</summary>
    public static int PluginFire(string hook, object? payload = null)
    {
        try
        {
            var json = payload == null ? null : System.Text.Json.JsonSerializer.Serialize(payload);
            return PluginHost.Fire(hook, json);
        }
        catch (Exception ex) { Paths.Log(L("[插件] 钩子 {0} 异常: {1}", hook, ex.Message)); return 0; }
    }

    /// <summary>GameBanana / 下载队列的导入钩子：把一个已下载的压缩包（或目录）当成 mod 导入
    /// （与 --import-mod 同路径；modNameHint 为空时由包内元数据决定名字）。</summary>
    public static int ImportModForHook(string gameRoot, string path, string? modNameHint, bool force)
        => ModdingImport.Import(gameRoot, path, modNameHint, null, false, force, null);

    /// <summary>命令行里的 --game 优先于自动检测。<para>发布包（install/ + src/）里的 exe 不在游戏目录内，
    /// 靠向上找 DELTARUNE.exe 找不到游戏根 ⇒ 所有命令都必须认 --game。</para></summary>
    static string EffectiveGameRoot(string[] args)
    {
        for (int k = 0; k + 1 < args.Length; k++)
            if (args[k] == "--game" && !string.IsNullOrWhiteSpace(args[k + 1]))
                return Path.GetFullPath(args[k + 1]);
        return Paths.DetectGameRoot();
    }

    public static int Main(string[] args)
    {
        // 输出统一 UTF-8（GUI / 脚本按 UTF-8 读取，避免中文乱码）
        // 语言必须在任何输出之前确定：--lang > 环境变量 NTL_LANG > config.json 的 lang > zh
        string? cliLang = null;
        for (int k = 0; k + 1 < args.Length; k++)
            if (args[k] == "--lang") cliLang = args[k + 1];
        Lang.Init(cliLang, EffectiveGameRoot(args));

        // 新功能统一入口：配置档 / 快照 / 恢复点 / 黑名单 / GameBanana / 下载队列 / 插件 / 主题 / 语言包 / Web UI。
        // 这些开关带子命令与位置参数，统一由 builder/CliFeatures.cs 解析；命中即执行并直接返回退出码。
        try
        {
            var feat = CliFeatures.Detect(args);
            if (feat != null)
            {
                // --game <根> 必须对功能开关同样生效：否则在沙箱/测试里跑 --profile-* 会写到真实游戏根
                return CliFeatures.Run(EffectiveGameRoot(args), feat, args);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(L("[错误] {0}", ex.Message));
            return 1;
        }

        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.InputEncoding = System.Text.Encoding.UTF8;
        }
        catch { }

        try
        {
            string? cmd = null;
            string chapter = "chapter4";
            string? dumpTarget = null;
            string? listFilter = null;
            string? importPath = null;
            string mapId = "";
            string mapOut = "";
            string cacheMaxMb = "";
            string gameRootArg = "";
            string srcArg = "";
            string saveName = "";
        bool selfTestNoLaunch = false;
        bool modForce = false;
        bool dryRun = false;
        bool chapterExplicit = false;
        bool diffOverride = false;
        bool diffVerbose = false;
        string? baseArg = null;
        string gbModel = "Mod";
        string? probeCode = null;
        string? probeMode = null;
        string? packsOut = null;
        var withDirs = new List<string>();
            string? modName = null;
            string? author = null;
            bool timeline = true;
            string? saveTarget = null;
            int bsideSlot = 0;
            bool bsideAllSlots = false;
            bool sendF2 = false;
            bool keepProc = false;
            string? sendText = null;
            string? sendKeys = null;
            string? searchQuery = null;
            string? fetchId = null;
            string? spriteQuery = null;
            string? findPattern = null;
            string? objFilter = null;
            string? roomQuery = null;
            string? packName = null;
            string? chapMode = null;          // --new-chapter: independent|overlay|fork
            int chapSlot = 0;                 // --new-chapter: --slot N（0=自动）
            string? chapFork = null;          // --new-chapter: --fork <mod>[:作者[:chapterN]]
            string chapTemplate = "room";     // --new-chapter: --template room|empty
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--lang" when i + 1 < args.Length: cliLang = args[++i]; break;
                    case "--deploy": cmd = "deploy"; break;
                case "--lint": cmd = "lint"; break;
                case "--deploy-all": cmd = "deploy-all"; break;
                case "--add-external":
                    cmd = "add-external";
                    importPath = args[++i];                        // exe 路径
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) extName = args[++i];
                    break;
                case "--ext-args": extArgs = args[++i]; break;
                case "--force": ForceDeploy = true; break;
                case "--force-timelines": ForceTimelines = true; break;
            case "--no-timelines": NoTimelines = true; break;   // 并行 worker 用：时间线由父进程统一部署
                case "--audit-inputscan": cmd = "audit-inputscan"; importPath = args[++i]; break;
                case "--no-cache": NoCache = true; Cache.NoCache = true; Mods.NoCache = true; Injector.NoCache = true; break;
                case "--import-all":
                    cmd = "import-all";
                    importPath = args[++i];
                    break;
                case "--jobs": Jobs = int.Parse(args[++i]); break;
                case "--extract":
                    cmd = "extract";
                    importPath = args[++i];
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) mapOut = args[++i];
                    break;
                case "--font-probe":
                    cmd = "font-probe";
                    // 位置参数（data.win 路径）可选：省略时用 --chapter 对应章节的 data.win
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) importPath = args[++i];
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) mapOut = args[++i];
                    break;
                case "--make-cjk-font":
                    cmd = "make-cjk-font";
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) importPath = args[++i];
                    break;
                case "--font-native":
                    cmd = "font-native";
                    // 位置参数（data.win 路径、输出目录）可选：省略时用 --chapter 的 data.win 与 .tmp/font-native
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) importPath = args[++i];
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) mapOut = args[++i];
                    break;
                case "--font-source": FontSource = args[++i]; break;
                case "--ttf": CjkTtf = args[++i]; break;
                case "--size": CjkSize = int.Parse(args[++i]); break;
                case "--charset": CjkCharset = args[++i]; break;
                case "--chars": CjkChars = args[++i]; break;
                case "--font-name": CjkName = args[++i]; break;
                case "--from-font": CjkFromWin = args[++i]; break;
                case "--source-font": CjkSourceFont = args[++i]; break;
                case "--doctor": cmd = "doctor"; break;
                case "--smoke": cmd = "smoke"; break;
                case "--api-doc": cmd = "api-doc"; break;
                case "--selftest": cmd = "selftest"; break;
                case "--probe-timeline-runtime": cmd = "probe-timeline-runtime"; break;
                case "--no-launch": selfTestNoLaunch = true; break;
                case "--new-mod" when i + 1 < args.Length: cmd = "new-mod"; modName = args[++i]; break;
                case "--fast-deploy": Injector.FastDeploy = true; break;
                case "--full-deploy": FullDeployForced = true; break;
                case "--self-check": SelfCheckOn = true; break;
                    case "--info": cmd = "info"; break;
                    case "--verify": cmd = "verify"; break;
                    case "--content-check":
                        cmd = "content-check";
                        if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) { chapter = args[++i]; chapterExplicit = true; }
                        break;
                    case "--dump" when i + 1 < args.Length: cmd = "dump"; dumpTarget = args[++i]; break;
                    case "--list" when i + 1 < args.Length: cmd = "list"; listFilter = args[++i]; break;
                    case "--import-delta" when i + 1 < args.Length: cmd = "import-delta"; importPath = args[++i]; break;
                    case "--name" when i + 1 < args.Length: modName = args[++i]; break;
                    case "--author" when i + 1 < args.Length: author = args[++i]; break;
                    case "--search" when i + 1 < args.Length: cmd = "search"; searchQuery = args[++i]; break;
                    case "--fetch" when i + 1 < args.Length: cmd = "fetch"; fetchId = args[++i]; break;
                    case "--sprite" when i + 1 < args.Length: cmd = "sprite"; spriteQuery = args[++i]; break;
                    case "--listsprites" when i + 1 < args.Length: cmd = "listsprites"; spriteQuery = args[++i]; break;
                    case "--restore-chapter" when i + 1 < args.Length: cmd = "restore"; chapter = args[++i]; break;
                    case "--version": cmd = "version"; break;
                    case "--find" when i + 1 < args.Length: cmd = "find"; findPattern = args[++i]; break;
                    case "--listobjs" when i + 1 < args.Length: cmd = "listobjs"; objFilter = args[++i]; break;
                    case "--listrooms": cmd = "listrooms"; break;
                    case "--gameinfo": cmd = "gameinfo"; break;
                    case "--import-bside" when i + 1 < args.Length: cmd = "import-bside"; importPath = args[++i]; break;
                case "--import-kristal-map" when i + 1 < args.Length: cmd = "import-kristal-map"; importPath = args[++i]; break;
                case "--map-id" when i + 1 < args.Length: mapId = args[++i]; break;
                case "--install": cmd = "install"; break;
                case "--mod-install" when i + 1 < args.Length: cmd = "mod-install"; importPath = args[++i]; break;
                case "--import-mod" when i + 1 < args.Length: cmd = "import-mod"; importPath = args[++i]; break;
                case "--dry-run": dryRun = true; break;
                case "--extract-diff" when i + 1 < args.Length: cmd = "extract-diff"; importPath = args[++i]; break;
                case "--diff-override": diffOverride = true; break;
                case "--diff-verbose": diffVerbose = true; break;
                case "--layer-from-base" when i + 1 < args.Length: cmd = "layer-from-base"; importPath = args[++i]; break;
                case "--probe-code" when i + 2 < args.Length: cmd = "probe-code"; importPath = args[++i]; probeCode = args[++i]; break;
                case "--verify-refcopy" when i + 2 < args.Length: cmd = "verify-refcopy"; importPath = args[++i]; probeCode = args[++i]; break;
                case "--probe-selftest": cmd = "probe-selftest"; break;
                case "--merge-selftest": cmd = "merge-selftest"; break;
                case "--probe-object" when i + 2 < args.Length: cmd = "probe-object"; importPath = args[++i]; probeCode = args[++i]; break;
                case "--gml-check" when i + 2 < args.Length:
                    cmd = "gml-check"; importPath = args[++i]; probeCode = args[++i];
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) probeMode = args[++i];
                    break;
                case "--asset-guard" when i + 2 < args.Length: cmd = "asset-guard"; importPath = args[++i]; probeCode = args[++i]; break;
                case "--export-shaders" when i + 2 < args.Length: cmd = "export-shaders"; importPath = args[++i]; probeCode = args[++i]; break;
                case "--export-packs" when i + 1 < args.Length: cmd = "export-packs"; importPath = args[++i]; break;
                case "--kristal-merge" when i + 1 < args.Length: cmd = "kristal-merge"; importPath = args[++i]; break;
                case "--conflicts": cmd = "conflicts"; break;
                case "--watch-external": cmd = "watch-external"; break;
                case "--focus-test" when i + 1 < args.Length: cmd = "focus-test"; importPath = args[++i]; break;
                case "--send-f2": sendF2 = true; break;
                case "--send-text" when i + 1 < args.Length: cmd = "focus-test"; sendText = args[++i]; break;
                case "--send-keys" when i + 1 < args.Length: cmd = "focus-test"; sendKeys = args[++i]; break;
                case "--keep": keepProc = true; break;
                case "--kristal-console":
                    cmd = "kristal-console";
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) importPath = args[++i];
                    break;
                case "--kristal-ensure":
                    cmd = "kristal-ensure";
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) importPath = args[++i];
                    break;
                case "--kristal-console-restore":
                    cmd = "kristal-console-restore";
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) importPath = args[++i];
                    break;
                case "--with" when i + 1 < args.Length: withDirs.Add(args[++i]); break;
                case "--out" when i + 1 < args.Length: packsOut = args[++i]; break;
                case "--base" when i + 1 < args.Length: baseArg = args[++i]; break;
                case "--base-mod" when i + 1 < args.Length: BaseModId = args[++i]; break;
                case "--fetch-mod" when i + 1 < args.Length: cmd = "fetch-mod"; fetchId = args[++i]; break;
                case "--model" when i + 1 < args.Length: gbModel = args[++i]; break;
                case "--mod-list": cmd = "mod-list"; break;
                case "--mod-force": modForce = true; break;
                case "--uninstall": cmd = "uninstall"; break;
                case "--game" when i + 1 < args.Length: gameRootArg = args[++i]; break;
                case "--src" when i + 1 < args.Length: srcArg = args[++i]; break;
                case "--save-list": cmd = "save-list"; break;
                case "--restore-save": cmd = "restore-save";
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) saveName = args[++i];
                    break;
                case "--cache-list": cmd = "cache-list"; break;
                case "--make-icon": cmd = "make-icon"; mapOut = (i + 1 < args.Length && !args[i + 1].StartsWith("--")) ? args[++i] : null; break;
                case "--cache-check":
                    cmd = "cache-check";
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) chapter = args[++i];
                    break;
                case "--cache-clear": cmd = "cache-clear"; break;
                case "--cache-dedup": cmd = "cache-dedup"; break;
                case "--cache-applied": cmd = "cache-applied"; break;
                case "--launch" when i + 1 < args.Length: cmd = "launch"; chapter = args[++i]; break;
                case "--cache-max" when i + 1 < args.Length: cacheMaxMb = args[++i]; break;
                case "--map-out" when i + 1 < args.Length: mapOut = args[++i]; break;
                    case "--make-bside": cmd = "make-bside"; break;
                    case "--slot" when i + 1 < args.Length: bsideSlot = chapSlot = int.Parse(args[++i]); break;
                    case "--all-slots": bsideAllSlots = true; break;
                    case "--dumpall" when i + 1 < args.Length: cmd = "dumpall"; importPath = args[++i]; break;
                    case "--no-save-rename": DisableSaveRename = true; break;
                    case "--link-saves" when i + 1 < args.Length: cmd = "link-saves"; saveTarget = args[++i]; break;
                    case "--room" when i + 1 < args.Length: cmd = "room"; roomQuery = args[++i]; break;
                    case "--pack" when i + 1 < args.Length: cmd = "pack"; packName = args[++i]; break;
                    case "--import-kristal" when i + 1 < args.Length: cmd = "import-kristal"; importPath = args[++i]; break;
                case "--convert-kristal" when i + 1 < args.Length: cmd = "convert-kristal"; importPath = args[++i]; break;
                case "--validate-kristal" when i + 1 < args.Length: cmd = "validate-kristal"; importPath = args[++i]; break;
                    case "--no-timeline": timeline = false; break;
                    case "--chapter" when i + 1 < args.Length: chapter = args[++i]; chapterExplicit = true; break;
                    // ---- 新章节脚手架（--new-chapter）----
                    case "--new-chapter" when i + 1 < args.Length: cmd = "new-chapter"; modName = args[++i]; break;
                    case "--mode" when i + 1 < args.Length: chapMode = args[++i]; break;
                    case "--fork" when i + 1 < args.Length: cmd = "new-chapter"; chapFork = args[++i]; break;
                    case "--template" when i + 1 < args.Length: chapTemplate = args[++i]; break;
                    case "--help" or "-h": PrintHelp(); return 0;
                }
            }

            var gameRoot = EffectiveGameRoot(args);
            Console.WriteLine(L("游戏根: {0}", gameRoot));

            return cmd switch
            {
                "deploy" => DeployAll(gameRoot, chapter),
                "info" => Info(gameRoot, chapter),
                "version" => ShowVersion(gameRoot, chapter),
                "find" => FindRefs(gameRoot, chapter, findPattern!),
                "listobjs" => ListObjects(gameRoot, chapter, objFilter!),
                "listrooms" => ListRooms(gameRoot, chapter, "*"),
                "gameinfo" => GameInfo(gameRoot, chapter),
                "import-bside" => BSide.ImportTemplate(gameRoot, importPath!, ChapterNum(chapter)),
                "make-bside" => BSide.MakeBSideSave(gameRoot, ChapterNum(chapter), bsideSlot, bsideAllSlots),
                "dumpall" => DumpAll(gameRoot, chapter, importPath!),
                "link-saves" => LinkSaves(gameRoot, saveTarget!),
                "room" => ListRooms(gameRoot, chapter, roomQuery!),
                "pack" => Pack(gameRoot, packName!, author, chapter, packsOut),
                "import-kristal" => KristalImport.Import(gameRoot, importPath!, modName, author, timeline),
                "import-kristal-map" => ImportKristalMap(gameRoot, importPath!, mapId, mapOut),
                "lint" => Lint.RunCli(Paths.NeutraledRoot(gameRoot)),
                "extract" => ExtractCli(importPath!, mapOut),
                "audit-inputscan" => InputScanAudit.Run(importPath!),
                "deploy-all" => RunParallelDeploy(gameRoot),
                "add-external" => AddExternalChapter(gameRoot, importPath!, extName, extArgs),
                "import-all" => RunParallelImport(gameRoot, importPath!),
                // 位置参数省略时默认分析当前章节的 data.win（与 --info/--doctor 一致）
                "font-probe" => FontProbe.Run(string.IsNullOrEmpty(importPath) ? Paths.ChapterDataWin(gameRoot, chapter) : importPath, string.IsNullOrEmpty(mapOut) ? Path.Combine(Paths.NeutraledRoot(gameRoot), ".tmp", "fontprobe") : mapOut),
                "make-cjk-font" => MakeCjkFontCli(gameRoot, importPath),
                "font-native" => FontNative.RunCli(gameRoot, chapter, importPath, mapOut, FontSource),
                "doctor" => DoctorCli(gameRoot),
                "smoke" => SmokeCli(gameRoot),
                    "selftest" => SelfTest.Run(gameRoot, chapter, selfTestNoLaunch),
                    "probe-timeline-runtime" => TimelineRuntime.RunProbe(gameRoot, chapter),
                "api-doc" => ApiDoc.Generate(gameRoot),
                "new-mod" => Scaffold.Create(gameRoot, modName, author, chapter),
                // 参数里的 --chapter 默认值是 chapter4，没显式给就用第一章当基底（第四章 data.win 有 135MB）
                "new-chapter" => ChapterScaffold.Create(gameRoot, modName, author,
                    chapMode ?? (chapFork != null ? "fork" : "independent"),
                    chapterExplicit ? chapter : "chapter1",
                    chapSlot, chapFork, chapTemplate, ForceDeploy),
                "cache-dedup" => CacheDedupCli(gameRoot),
                "save-list" => SaveGuardListCli(gameRoot),
                "restore-save" => SaveGuard.Restore(gameRoot, string.IsNullOrEmpty(saveName) ? null : saveName),
                "install" => Installer.Install(string.IsNullOrEmpty(gameRootArg) ? null : gameRootArg, string.IsNullOrEmpty(srcArg) ? null : srcArg),
                "convert-kristal" => KristalConvertCli(importPath),
                "validate-kristal" => KristalValidateCli(importPath),
                "mod-install" => ModInstall.Install(gameRoot, importPath, modForce),
                "import-mod" => ModdingImport.Import(gameRoot, importPath!, modName, author, dryRun, modForce, chapterExplicit ? chapter : null),
                "extract-diff" => DiffLayer.Extract(gameRoot, importPath!, chapterExplicit ? chapter : null, modName, author, baseArg, dryRun, diffOverride, diffVerbose, modForce),
                "layer-from-base" => LayerFromBase.Extract(gameRoot, importPath!, chapterExplicit ? chapter : null, modName, author, baseArg, dryRun, 20000, diffVerbose),
                "probe-code" => CodeProbe.Dump(importPath!, probeCode!, 30),
                "probe-selftest" => CodeProbe.SelfTest(),
                "merge-selftest" => MergeSelfTest.Run(),
                "probe-object" => ObjectProbe.Dump(importPath!, probeCode!),
                "gml-check" => GmlCheck.Run(importPath!, probeCode!, probeMode),
                "asset-guard" => AssetNameGuard.Cli(importPath!, probeCode!),
                "verify-refcopy" => RefCopyVerify.Verify(importPath!, probeCode!, 60, null),
                "export-packs" => ExportPacks(gameRoot, importPath!, chapter, chapterExplicit, modName, author, packsOut, baseArg),
                "export-shaders" => ExportShaders(importPath!, probeCode!, baseArg),
                "kristal-merge" => KristalMerge.Import(gameRoot, importPath!, withDirs, modName, author),
                "conflicts" => ConflictsCli(gameRoot, chapter),
                "watch-external" => WatchExternal(gameRoot),
                "focus-test" => WinFocus.FocusTest(importPath!, sendF2 || sendText != null || sendKeys != null, keepProc, sendText, sendKeys),
                "kristal-console" => KristalConsoleCli(gameRoot, importPath),
                "kristal-ensure" => KristalEnsureCli(gameRoot, importPath),
                "kristal-console-restore" => FuseZip.Restore(importPath ?? ExternalExeDefault(gameRoot) ?? ""),
                "mod-list" => ModInstallListCli(gameRoot),
                "uninstall" => Installer.Uninstall(string.IsNullOrEmpty(gameRootArg) ? null : gameRootArg),
                "cache-list" => CacheList(gameRoot),
                "cache-check" => CacheCheck(gameRoot, chapter),
                "make-icon" => IconMaker.Run(mapOut ?? Path.Combine(Paths.NeutraledRoot(gameRoot), "tools", "icon.ico")),
                "cache-clear" => CacheClear(gameRoot),
                "cache-applied" => CacheApplied(gameRoot, chapter),
                "launch" => LaunchWithCache(gameRoot, chapter, cacheMaxMb),
                "verify" => Verify(gameRoot, chapter),
                    "content-check" => ContentCheck.Run(gameRoot, chapterExplicit ? chapter : "all", string.IsNullOrEmpty(packsOut) ? null : packsOut),
                "dump" => Dump(gameRoot, chapter, dumpTarget!),
                "list" => ListCodes(gameRoot, chapter, listFilter!),
                "import-delta" => DeltaImport.Import(gameRoot, importPath!, chapter,
                    modName ?? Path.GetFileNameWithoutExtension(importPath!), author ?? "unknown"),
                "search" => Search(gameRoot, searchQuery!),
                "restore" => RestoreChapter(gameRoot, chapter),
                "sprite" => SpriteInfo(gameRoot, chapter, spriteQuery!),
                "listsprites" => ListSprites(gameRoot, chapter, spriteQuery!),
                "fetch" => Fetch(gameRoot, chapter, fetchId!, modName),
                "fetch-mod" => FetchMod(gameRoot, fetchId!, gbModel, modName, author, dryRun, modForce, chapterExplicit ? chapter : null),
                _ => Unknown()
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine(L("[错误] ") + ex.Message);
            Console.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    private static int Unknown()
    {
        PrintHelp();
        return 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Neutraled builder");
        Console.WriteLine(L("  --lang zh|en                    设置输出语言（默认跟随 config.json 的 lang，缺省 zh；也可用环境变量 NTL_LANG）"));
        Console.WriteLine(L("  --deploy --chapter chapter4     部署核心+mods 到章节"));
        Console.WriteLine(L("  --info   --chapter chapter4     显示 data.win 概要"));
        Console.WriteLine(L("  --verify --chapter chapter4     校验注入结果"));
        Console.WriteLine();
        Console.WriteLine(L("  从零做自己的章节（GML 模板 + 脚手架，详见 docs/CHAPTER_DEV.md）:"));
        Console.WriteLine(L("  --new-chapter <名字>            生成一个**能进、能走、能看**的自定义章节（默认独立章节）"));
        Console.WriteLine(L("      --author <作者>             作者名（默认 Player）→ mods/<名字>/<作者>/"));
        Console.WriteLine("      --mode independent|overlay|fork");
        Console.WriteLine(L("                                  independent=自带 data.win 的独立章节（默认）"));
        Console.WriteLine(L("                                  overlay    =只叠加到官方某章，不产生新章节条目"));
        Console.WriteLine(L("                                  fork       =复制一个已安装 mod 章节当基底（配合 --fork）"));
        Console.WriteLine(L("      --chapter <chapterN>        基底官方章节（默认 chapter1）"));
        Console.WriteLine(L("      --slot <N>                  章节排序号（默认自动排在最后，最小 6）"));
        Console.WriteLine(L("      --fork <mod>[:作者[:chapterN]]  被 fork 的源章节（--new-chapter help 可列清单）"));
        Console.WriteLine(L("      --template room|empty       模板：room=带一个可走房间（默认）/ empty=空骨架"));
        Console.WriteLine(L("  例: ntl-builder.exe --new-chapter \"我的章节\" --deploy --chapter root --force"));
        Console.WriteLine();
        Console.WriteLine(L("  导入 mod（全部自动识别包形态）:"));
        Console.WriteLine(L("  --import-mod <包|目录>          传统 mod 自动转 ntl 格式（modding.xml / meta.toml / xdelta / zip / 原生 mod）"));
        Console.WriteLine(L("      --dry-run                   只分析不写盘（配合 --import-mod 做预检）"));
        Console.WriteLine(L("      --name <名> --author <作者>  覆盖包内元数据"));
        Console.WriteLine(L("      --mod-force                 校验和不符时仍强制尝试"));
        Console.WriteLine(L("  --mod-install <包>              安装原生 Neutraled/Kristal mod 包"));
        Console.WriteLine(L("  --mod-list                      列出已安装 mod"));
        Console.WriteLine(L("  --import-delta <xdelta>         单项 xdelta / 修改版 data.win 转换"));
        Console.WriteLine(L("  --fetch-mod <gamebananaId>      下载 + 自动识别格式转换（含 Kristal）"));
        Console.WriteLine(L("      --model Mod|Wip             指定 GameBanana 条目类型（默认 Mod）"));
        Console.WriteLine(L("  --extract-diff <data.win|目录>  把整包 mod 提取成**可叠加的差异层**（让多个整包 mod 共存）"));
        Console.WriteLine(L("      --chapter <ch>              指定章节（不给则从路径推断）"));
        Console.WriteLine(L("      --diff-override             层与层改同一对象时，声明 later-wins"));
        Console.WriteLine(L("  --layer-from-base <data.win>    **源码级**差异层：反编译真实改动对象 → GML patch（不再受索引移植限制）"));
        Console.WriteLine(L("  --export-packs <data.win>       导出资源包（精灵/声音/字体）→ 可叠加到任意基底"));
        Console.WriteLine(L("  --font-native [data.win] [目录] 部署期把本机字形（8bitoperator JVE + 汉化汉字）覆盖进 OFL 字体包"));
        Console.WriteLine(L("      --font-source game|base-mod 只用某一种本机字形来源（默认按 fonts/ntl_native_sources.json）"));
        Console.WriteLine(L("      --out <目录>                指定输出目录（默认写进对应 mod 目录）"));
        Console.WriteLine(L("  --kristal-merge <项目> --with <插件>...   Kristal 宿主合并导入（插件型 mod 缺宿主）"));
        Console.WriteLine(L("  --conflicts                     只查 mod 冲突不部署（0=无冲突 / 2=有冲突，CI 友好）"));
        Console.WriteLine(L("  --probe-code <data.win> <对象名>   指令级取证（字段真实类型 + 解析目标）"));
        Console.WriteLine(L("  --verify-refcopy <源> <目标>      用反编译器当预言机，验证跨资源池复制是否保真"));
        Console.WriteLine(L("  --merge-selftest                三方合并（patch × 基底）行级合并自检（17 组用例）"));
        Console.WriteLine(L("  --probe-object <data.win> <对象|*>  对象取证：头部字段 + 事件表（事件名/子类型/代码/指令数），* = 列全部对象"));
        Console.WriteLine(L("  --gml-check <data.win> <层目录> [模式]  编译自检：层里每个 .gml 按部署的同一套规则编译，逐个报失败文件与行"));
        Console.WriteLine(L("  --asset-guard <data.win> <层目录[;目录…]>  资源名守卫：层代码引用了产物里不存在的资源名就告警（只告警不中止）"));
Console.WriteLine(L("  --export-shaders <data.win> <mod 章节目录> [--base <基线>]  导出色差着色器资源包 → shaders/*.json（层缺着色器时用）"));
        Console.WriteLine(L("      模式 = check(默认) / asset(额外建脚本资源) / dup(无条件新建代码条目) / global(新全局脚本用 gml_GlobalScript_ 前缀) / deploy(=asset+dup)，可用逗号组合"));
        Console.WriteLine();
        Console.WriteLine(L("  质量与自检:"));
        Console.WriteLine(L("  --lint / --doctor / --smoke     静态检查 / 自检 / API 冒烟"));
        Console.WriteLine(L("  --api-doc                       生成 API 文档"));
        Console.WriteLine(L("  --selftest [--no-launch]        端到端自测"));
        Console.WriteLine(L("  --probe-timeline-runtime        只读预检：时间线产物的运行时文件章节（按 data.win 血统解析）+ 语言档覆盖守卫，不写盘"));
        Console.WriteLine();
        Console.WriteLine(L("  外部章节（Kristal / 冰封帷幕这类成品）:"));
        Console.WriteLine(L("  --add-external <exe> [名字]     注册成外部章节（章节选择器里可选）"));
        Console.WriteLine(L("  --watch-external                守候进程（静默，无窗口）：看到启动请求就拉起外部引擎，"));
        Console.WriteLine(L("                                  并负责隐藏/恢复 DELTARUNE 窗口 + 把焦点交给外部引擎"));
        Console.WriteLine(L("  --kristal-console [exe]         给**没有源码的融合版 Kristal exe** 注入 Neutraled 控制台"));
        Console.WriteLine(L("                                  （往内嵌 zip 追加 src/ntlconsole.lua + 在 main.lua 挂启动钩子；"));
        Console.WriteLine(L("                                    原版自动备份成 <exe>.orig，可反复注入）"));
        Console.WriteLine(L("  --kristal-console-restore [exe] 从 .orig 还原（撤销注入）"));
        Console.WriteLine(L("  --focus-test <exe> [--send-f2] [--keep]   焦点抢占自检（后台进程能否抢到前台 + F2 是否打开控制台）"));

        Console.WriteLine();
        Console.WriteLine(L("  配置档 / 快照 / 恢复点（mod 集合与版本管理）:"));
        Console.WriteLine(L("  --profile-list                  列出配置档（* = 当前活动档）"));
        Console.WriteLine(L("  --profile-new <id> [--name 名] [--from 档] [--desc 说明]    新建配置档"));
        Console.WriteLine(L("  --profile-use <id>              应用配置档（改各 mod 的启用态 + config.json 设置）"));
        Console.WriteLine(L("  --profile-show [id]             查看配置档内容"));
        Console.WriteLine(L("  --profile-copy <源> <目标>      复制配置档"));
        Console.WriteLine(L("  --profile-rename <旧> <新>      重命名配置档"));
        Console.WriteLine(L("  --profile-delete <id> [--force] 删除配置档（活动档需 --force）"));
        Console.WriteLine(L("  --profile-export <id> [--out 文件] / --profile-import <文件> [--force]   导出与导入"));
        Console.WriteLine(L("  --snapshot-list [modId]         列出 mod 的版本快照"));
        Console.WriteLine(L("  --snapshot-create <modId> [--version 版本] [--note 备注]   给当前内容打快照"));
        Console.WriteLine(L("  --snapshot-use <modId> <版本> [--force]   切回某个快照（先自动保存当前态）"));
        Console.WriteLine(L("  --snapshot-import <modId> <目录|zip> [--version 版本] / --snapshot-delete <modId> <版本>"));
        Console.WriteLine(L("  --snapshot-auto                 给所有 mod 建保底快照"));
        Console.WriteLine(L("  --restore-list                  列出整游戏恢复点"));
        Console.WriteLine(L("  --restore-create [--name 名] [--from live|profile] [--profile <id>]   建立恢复点"));
        Console.WriteLine(L("  --restore-apply <id> [--force]  恢复到该点（要求游戏未运行；先自动存回退点）"));
        Console.WriteLine(L("  --restore-export <id> <文件.ntlrestore> / --restore-import <文件> [--force] / --restore-delete <id>"));
        Console.WriteLine();
        Console.WriteLine(L("  GameBanana / 下载队列 / 黑名单:"));
        Console.WriteLine(L("  --gb-search <关键词> [--per-page N]   搜索 GameBanana（自动过滤黑名单）"));
        Console.WriteLine(L("  --gb-files <modId>              列出该条目的可下载文件"));
        Console.WriteLine(L("  --gb-install <modId> [--file N] [--chapter 章节] [--force]   下载并导入"));
        Console.WriteLine(L("  --queue-list / --queue-run [--no-install] [--delete-after]   下载队列"));
        Console.WriteLine(L("  --queue-add <modId> [--name 名] [--file N]   把一次下载加入队列（不立刻下载）"));
        Console.WriteLine(L("  --queue-remove <条目id> [--delete-file]      从队列移除（--delete-file 同时删已下载文件）"));
        Console.WriteLine(L("  --block-list / --block-add <值> [--kind id|name|category] [--note 备注] / --block-remove <值>"));
        Console.WriteLine();
        Console.WriteLine(L("  插件 / 主题 / 语言包 / 网页界面:"));
        Console.WriteLine(L("  --plugin-list / --plugin-enable <id> / --plugin-disable <id> / --plugin-info <id>"));
        Console.WriteLine(L("  --plugin-install <目录|.ntlplugin> [--force] / --plugin-remove <id> [--force] / --plugin-hooks"));
        Console.WriteLine(L("  --theme-list / --theme-use <id> / --theme-show <id>"));
        Console.WriteLine(L("  --theme-import <主题.json> [--force]   导入主题"));
        Console.WriteLine(L("  --lang-list                     列出可用界面语言与覆盖率"));
        Console.WriteLine(L("  --lang-use <语言码>             切换界面语言并存进 config.json"));
        Console.WriteLine(L("  --lang-coverage [语言码]        统计语言包覆盖率（C# 侧 + 游戏内；退出码 0 = 全部 100%）"));
        Console.WriteLine(L("  --lang-template <语言码> [--out 文件]   导出待翻译模板"));
        Console.WriteLine(L("  --web [--port 7931] [--no-open] [--auto-stop]   启动本地网页界面（跨平台，浏览器打开）"));
        Console.WriteLine(L("  --platform-info                 平台信息（章节后缀 / 硬链接 / curl / 可写性）"));
    }

    private static string BootCodeName(string chapter) =>
        chapter.Equals("root", StringComparison.OrdinalIgnoreCase)
            ? "gml_Object_obj_init_pc_Create_0"
            : "gml_Object_obj_initializer2_Create_0";

    private static int Search(string gameRoot, string query)
    {
        var list = GameBanana.SearchAsync(query).GetAwaiter().GetResult();
        Console.WriteLine(L("搜索 \"{0}\": {1} 条", query, list.Count));
        foreach (var r in list)
            Console.WriteLine(L("  [{0}] {1}  (id={2}, 作者={3}, 下载={4})", r.Model, r.Name, r.Id, r.Author, r.DownloadCount));
        return 0;
    }

    private static int Fetch(string gameRoot, string chapter, string idText, string? modName)
    {
        if (!int.TryParse(idText, out var id)) { Console.WriteLine(L("modId 必须是数字")); return 1; }
        var files = GameBanana.GetFilesAsync(id, "Mod").GetAwaiter().GetResult();
        if (files.Count == 0) files = GameBanana.GetFilesAsync(id, "Wip").GetAwaiter().GetResult();
        if (files.Count == 0) { Console.WriteLine(L("该 mod 没有可下载文件")); return 1; }

        Console.WriteLine(L("文件列表（{0}）:", files.Count));
        for (int i = 0; i < files.Count; i++)
            Console.WriteLine($"  [{i}] {files[i].FileName}  ({files[i].Size / 1024} KB)  {files[i].Description}");

        var pick = files[0];
        Console.WriteLine(L("下载: {0}", pick.FileName));
        var tmp = Path.Combine(Path.GetTempPath(), pick.FileName);
        GameBanana.DownloadAsync(pick.DownloadUrl, tmp, (r, t) =>
        {
            if (t > 0 && r % (5 * 1024 * 1024) < 81920) Console.WriteLine($"  {r * 100 / t}%");
        }).GetAwaiter().GetResult();
        Console.WriteLine(L("已下载: {0}", tmp));

        // 转换（zip/xdelta/data.win）
        var name = modName ?? Path.GetFileNameWithoutExtension(pick.FileName);
        return DeltaImport.Import(gameRoot, tmp, chapter, name);
    }

    /// <summary>下载一个 GameBanana mod 并**自动识别格式转换**（传统/Kristal/原生一条龙）。
    /// 与 --fetch 的区别：--fetch 只会走 xdelta 转换，遇到 Kristal / Modding.xml 包会转错。</summary>
    private static int FetchMod(string gameRoot, string idText, string model, string? modName, string? author,
        bool dryRun, bool force, string? forceChapter)
    {
        if (!int.TryParse(idText, out var id)) { Console.WriteLine(L("modId 必须是数字")); return 1; }

        var files = GameBanana.GetFilesAsync(id, model).GetAwaiter().GetResult();
        if (files.Count == 0 && !model.Equals("Wip", StringComparison.OrdinalIgnoreCase))
            files = GameBanana.GetFilesAsync(id, "Wip").GetAwaiter().GetResult();
        if (files.Count == 0) { Console.WriteLine(L("该 mod 没有可下载文件")); return 1; }

        Console.WriteLine(L("===== 下载并导入 GameBanana mod {0} =====", id));
        for (int i = 0; i < files.Count; i++)
            Console.WriteLine($"  [{i}] {files[i].FileName}  ({files[i].Size / 1024} KB)  {files[i].Description}");
        if (files.Count > 1) Console.WriteLine(L("  （多个文件时取最大的那个作为主包）"));

        var pick = files.OrderByDescending(f => f.Size).First();
        var tmpDir = Path.Combine(Paths.NeutraledRoot(gameRoot), ".tmp");
        Directory.CreateDirectory(tmpDir);
        var safe = new string(pick.FileName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
        var tmp = Path.Combine(tmpDir, safe);

        Console.WriteLine(L("  下载: {0}  {1} KB", pick.FileName, pick.Size / 1024));
        bool ok = GameBanana.DownloadWithCurl(pick.DownloadUrl, tmp, pick.Size, m => Console.WriteLine(m));
        if (!ok)
        {
            Console.WriteLine(L("  [警告] curl 通道失败，回退 .NET HttpClient"));
            try
            {
                GameBanana.DownloadAsync(pick.DownloadUrl, tmp, (r, t) =>
                {
                    if (t > 0 && r == t) Console.WriteLine($"    {r * 100 / t}%");
                }).GetAwaiter().GetResult();
            }
            catch (Exception ex) { Console.WriteLine(L("  [警告] 下载失败: ") + ex.Message); }
        }

        if (!File.Exists(tmp)) { Console.WriteLine(L("[错误] 下载失败")); return 1; }
        var finalLen = new FileInfo(tmp).Length;
        if (pick.Size > 0 && finalLen != pick.Size)
        {
            Console.WriteLine(L("[错误] 下载不完整（{0} / {1} 字节）—— 拒绝导入残缺的包", finalLen, pick.Size));
            return 1;
        }

        Console.WriteLine(L("  已下载: {0}  ({1} KB)", tmp, finalLen / 1024));
        var rc = ModdingImport.Import(gameRoot, tmp, modName, author, dryRun, force, forceChapter);
        try { File.Delete(tmp); } catch { }   // 临时包不留在项目里（需要时再下一次）
        return rc;
    }

    /// <summary>守候"外部引擎章节"的启动请求。
    /// 为什么需要它：实测这个 GameMaker 运行时**没有任何启动进程的内置函数**
    /// （execute_program / execute_shell / os_start_process / url_open 都不存在），
    /// 所以游戏自己拉不起 Kristal。协议改成：游戏在章节选择里选中外部章节时
    /// 写 Neutraled\launch-request.json 然后退出；本进程看到请求就把 ke 拉起来。
    /// 用户在 GUI 里点「部署并启动」时会在后台起一个本命令；关掉窗口即停止守候。</summary>
    /// <summary>确保 Kristal 引擎有中文字形回退。
    ///
    /// 背景：Kristal 的 assets/fonts/main.json 里字体回退链只到**日文像素字体 ja_main**，
    /// 简体中文项目（如 Frostveil）里 ja_main 缺失的字会整片画不出来，表现为
    /// "菜单中文散乱 / 字号不一致"（实测）。这里补一层中文字体回退：
    ///   main → ja_main(像素) → ntl_cjk(黑体，只在缺字时用)
    /// 回退字号取主字体的 1/2 —— 像素字体的"名义字号"约为视觉高度的两倍，直接用同号会明显偏大。</summary>
    static void EnsureKristalCjkFallback(string kristalRoot)
    {
        try
        {
            var fonts = Path.Combine(kristalRoot, "assets", "fonts");
            if (!Directory.Exists(fonts)) return;
            var ttf = Path.Combine(fonts, "ntl_cjk.ttf");
            if (!File.Exists(ttf))
            {
                var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                foreach (var cand in new[] { "simhei.ttf", "deng.ttf", "msyh.ttc" })
                {
                    var src = Path.Combine(windir, "Fonts", cand);
                    if (!File.Exists(src)) continue;
                    if (cand.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase)) continue;  // LÖVE 不吃 TTC
                    File.Copy(src, ttf, true);
                    Console.WriteLine(L("  [kristal] 已放入中文字体 {0} → assets/fonts/ntl_cjk.ttf", cand));
                    break;
                }
            }
            if (!File.Exists(ttf)) { Console.WriteLine(L("  [kristal] 找不到可用的中文字体，跳过中文回退（菜单中文可能缺失）")); return; }

            var cfgPath = Path.Combine(fonts, "ntl_cjk.json");
            if (!File.Exists(cfgPath)) File.WriteAllText(cfgPath, "{\n    \"defaultSize\": 32\n}\n", new System.Text.UTF8Encoding(false));

            int patched = 0;
            foreach (var name in new[] { "main.json", "main_mono.json", "small.json" })
            {
                var fp = Path.Combine(fonts, name);
                if (!File.Exists(fp)) continue;
                JsonNode? node;
                try { node = JsonNode.Parse(File.ReadAllText(fp)); } catch { continue; }
                if (node is not JsonObject o) continue;
                int def = o["defaultSize"]?.GetValue<int>() ?? 32;
                int want = Math.Max(10, def / 2);
                var arr = o["fallbacks"] as JsonArray;
                if (arr == null) { arr = new JsonArray(); o["fallbacks"] = arr; }
                bool has = false;
                foreach (var it in arr)
                    if (it is JsonObject io && io["font"]?.GetValue<string>() == "ntl_cjk")
                    {
                        has = true;
                        if ((io["size"]?.GetValue<int>() ?? 0) != want) io["size"] = want;
                    }
                if (!has) arr.Add(new JsonObject { ["font"] = "ntl_cjk", ["size"] = want });
                File.WriteAllText(fp, o.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), new System.Text.UTF8Encoding(false));
                patched++;
            }
            if (patched > 0) Console.WriteLine(L("  [kristal] 中文回退已就绪（{0} 个字体配置）", patched));
        }
        catch (Exception ex) { Console.WriteLine(L("  [警告] 配置 Kristal 中文回退失败: {0}", ex.Message)); }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr h, System.Text.StringBuilder s, int n);
    private delegate bool EnumWindowsProc(IntPtr h, IntPtr l);

    /// <summary>隐藏窗口期间，游戏可能弹出 Code Error 这类模态对话框（类名 #32770）。
    /// 必须把它们显示出来 —— 否则玩家看不到错误，只知道"游戏卡住了"（实测踩过）。</summary>
    private static void ShowHiddenGameDialogs()
    {
        try
        {
            var pids = new HashSet<uint>();
            foreach (var pr in System.Diagnostics.Process.GetProcessesByName("DELTARUNE")) pids.Add((uint)pr.Id);
            if (pids.Count == 0) return;
            EnumWindows((h, l) =>
            {
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                if (!pids.Contains(pid)) return true;
                var cls = new System.Text.StringBuilder(64);
                GetClassNameW(h, cls, 64);
                if (cls.ToString() == "#32770")            // 标准对话框
                {
                    ShowWindow(h, 5);                      // SW_SHOW：让报错看得见
                    SetForegroundWindow(h);
                    Console.WriteLine(L("  ⚠ 检测到游戏弹出的对话框（Code Error）—— 已显示出来，请查看"));
                }
                return true;
            }, IntPtr.Zero);
        }
        catch { }
    }

    /// <summary>DELTARUNE 主窗口句柄。
    /// ⚠ 不能用 Process.MainWindowHandle：**窗口被隐藏后它返回 0**（实测），
    ///   于是"恢复显示"永远找不到窗口 → 游戏永久隐藏。改用枚举窗口 + 类名 YYGameMakerYY。</summary>
    private static IntPtr GameWindow()
    {
        IntPtr found = IntPtr.Zero;
        try
        {
            var pids = new HashSet<uint>();
            foreach (var pr in System.Diagnostics.Process.GetProcessesByName("DELTARUNE")) pids.Add((uint)pr.Id);
            if (pids.Count == 0) return IntPtr.Zero;
            EnumWindows((h, l) =>
            {
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                if (!pids.Contains(pid)) return true;
                var cls = new System.Text.StringBuilder(64);
                GetClassNameW(h, cls, 64);
                var c = cls.ToString();
                if (c == "YYGameMakerYY" || c == "#32770")
                {
                    found = h;
                    return false;                  // 找到主窗口就停
                }
                return true;
            }, IntPtr.Zero);
        }
        catch { }
        return found;
    }

    /// <summary>把焦点交给刚拉起的外部引擎窗口。
    /// ⚠ 隐藏 DELTARUNE 窗口**不会**转移焦点 —— 焦点还在游戏上，于是按键（含 F2）都送进那个隐藏窗口，
    ///   玩家在 Kristal 里按 F2 毫无反应（用户实测）。所以拉起后要主动聚焦它。</summary>
    /// <summary>把焦点交给刚拉起的外部引擎窗口。
    /// ⚠ 两个坑都踩过（用户实测反馈）：
    ///   ① 隐藏 DELTARUNE 窗口**不会**转移焦点 —— 焦点还在游戏上，按键（含 F2）全进了那个隐藏窗口；
    ///   ② 后台进程**直接 SetForegroundWindow 会被 Windows 静默拒绝**（返回 false 没人看），
    ///      于是日志写着"已把焦点交给外部引擎窗口"，其实前台还在游戏上 → F2 自然没反应。
    ///   现在走 WinFocus 的组合拳，并且**每步都用 GetForegroundWindow 复核**，失败就如实报告。</summary>
    private static void FocusExternalWindow(System.Diagnostics.Process proc)
    {
        // 放到后台线程里做：要持续抢 15 秒左右（引擎加载完会重建窗口），不能卡住守候主循环。
        var pid = proc.Id;
        var th = new System.Threading.Thread(() =>
        {
            try
            {
                if (WinFocus.WaitWindow(proc, 15000) == IntPtr.Zero)
                {
                    Console.WriteLine(L("  [提示] 没等到外部引擎窗口，无法自动聚焦（手动点一下即可）"));
                    return;
                }
                if (WinFocus.KeepForeground(pid, 20000))
                    Console.WriteLine(L("  ✅ 焦点已交给外部引擎窗口（pid {0}，{1}）", pid, WinFocus.Describe(WinFocus.Foreground())));
                else
                {
                    Console.WriteLine(L("  ⚠ 自动聚焦失败 —— 请手动点一下外部引擎窗口"));
                    Console.WriteLine(L("     前台={0}（目标 pid {1}）", WinFocus.Describe(WinFocus.Foreground()), pid));
                }
            }
            catch (Exception ex) { Console.WriteLine(L("  [警告] 聚焦线程异常: ") + ex.Message); }
        })
        { IsBackground = true };
        th.Start();
    }

    /// <summary>进入外部章节时藏起游戏窗口（任务栏一起消失），退出后恢复并置前。</summary>
    private static void SetGameWindowVisible(bool visible)
    {
        try
        {
            var h = GameWindow();
            if (h == IntPtr.Zero) return;
            if (visible) { ShowWindow(h, 5); ShowWindow(h, 9); WinFocus.ForceForeground(h); }  // SW_SHOW + SW_RESTORE + 真置前
            else { ShowWindow(h, 0); }                                                     // SW_HIDE
        }
        catch { }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    /// <summary>守候进程必须**静默**：隐藏控制台窗口 + 日志改写到文件（用户要求无窗口、无弹窗）。</summary>
    private static void GoSilent(string gameRoot)
    {
        try
        {
            if (Platform.IsWindows)                    // GetConsoleWindow 是 kernel32 专有：非 Windows 上不该调用
            {
                var h = GetConsoleWindow();
                if (h != IntPtr.Zero) ShowWindow(h, 0);   // SW_HIDE
            }
        }
        catch { }
        try
        {
            var dir = Path.Combine(Paths.NeutraledRoot(gameRoot), "logs");
            Directory.CreateDirectory(dir);
            var sw = new StreamWriter(Path.Combine(dir, "watch-external.log"), true) { AutoFlush = true };
            Console.SetOut(sw);
            Console.SetError(sw);
        }
        catch { }
    }

    /// <summary>默认的外部章节 exe（清单里的第一个；冰封帷幕就是它）。</summary>
    private static string? ExternalExeDefault(string gameRoot)
    {
        try
        {
            foreach (var e in Chapters.LoadExternalList(gameRoot))
                if (e.TryGetValue("exe", out var v) && v is string s && File.Exists(s)) return s;
        }
        catch { }
        return null;
    }

    /// <summary>控制台用的中文字体：优先 Neutraled 自带，其次 Kristal 工程里的，最后系统黑体。</summary>
    private static string? FindCjkFont(string gameRoot)
    {
        var cands = new List<string>
        {
            Path.Combine(Paths.NeutraledRoot(gameRoot), "kristal", "ntlconsole", "ntl_cjk.ttf"),
            Path.Combine(gameRoot, "Kristal-main", "assets", "fonts", "ntl_cjk.ttf"),
            @"C:\Windows\Fonts\simhei.ttf",
        };
        return cands.FirstOrDefault(File.Exists);
    }

    /// <summary>--kristal-console [exe]：给**没有源码的融合版 Kristal 成品**（冰封帷幕这类）装上 Neutraled 控制台。
    /// 为什么必须改 exe：融合包的 mods/ 打包在 exe 内部，磁盘上的 mods/ 目录根本不会被扫描
    /// （实测把 mod 放进 exe 同级的 mods/ 与 LOVE 存档目录，mod.lua 都不执行）。
    /// 做法是往它内嵌的 zip 里**追加**一个库（mods/&lt;激活mod&gt;/libraries/ntlconsole/），原条目一个不动。</summary>
    private static int KristalConsoleCli(string gameRoot, string? exeArg)
    {
        var exe = exeArg ?? ExternalExeDefault(gameRoot);
        if (string.IsNullOrEmpty(exe))
        {
            Console.WriteLine(L("  [错误] 没指定 exe，也没有已注册的外部章节。用法: --kristal-console <exe>"));
            return 1;
        }
        var libDir = Path.Combine(Paths.NeutraledRoot(gameRoot), "kristal", "ntlconsole");
        return FuseZip.InjectConsole(exe, libDir, FindCjkFont(gameRoot));
    }

    /// <summary>--kristal-ensure [exe]：只在"缺控制台/控制台过期"时注入（守候启动前调用的就是它）。</summary>
    private static int KristalEnsureCli(string gameRoot, string? exeArg)
    {
        var exe = exeArg ?? ExternalExeDefault(gameRoot);
        if (string.IsNullOrEmpty(exe)) { Console.WriteLine(L("  [错误] 没指定 exe")); return 1; }
        FuseZip.EnsureConsole(exe, Path.Combine(Paths.NeutraledRoot(gameRoot), "kristal", "ntlconsole"), FindCjkFont(gameRoot));
        return 0;
    }

    /// <summary>已部署的 chapters.json 里所有外部章节的 exe 名（不含扩展名）——
    /// 恢复逻辑用它判断"外部引擎还在不在跑"。
    /// ⚠ 别用 chapters-external.json：那是**手动注册**的清单，本机根本没有（实测为空 → 恢复静默失效）。</summary>
    private static List<string> ExternalExeNames(string gameRoot)
    {
        var list = new List<string>();
        try
        {
            var f = Path.Combine(Paths.NeutraledRoot(gameRoot), "chapters.json");
            if (!File.Exists(f)) return list;
            if (JsonNode.Parse(File.ReadAllText(f)) is not JsonObject root) return list;
            if (root["chapters"] is not JsonArray arr) return list;
            foreach (var it in arr)
            {
                if (it is not JsonObject o) continue;
                var kind = o["Kind"]?.GetValue<string>() ?? o["kind"]?.GetValue<string>() ?? "";
                var exe = o["Exe"]?.GetValue<string>() ?? o["exe"]?.GetValue<string>() ?? "";
                if (!kind.Equals("external", StringComparison.OrdinalIgnoreCase) || exe.Length == 0) continue;
                var n = Path.GetFileNameWithoutExtension(exe);
                if (n.Length > 0) list.Add(n);
            }
        }
        catch { }
        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>游戏读得到的存档区目录（GM 会把相对路径重定向到这里）。</summary>
    private static string SaveAreaNtl() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DELTARUNE", "Neutraled");

    /// <summary>"外部引擎正在跑"标记：游戏每帧检查它来屏蔽自己的输入
    /// （否则玩家在 Kristal 里打字会漏进章节选择 —— 本次实测复现过）。</summary>
    private static void SetExternalRunning(bool running)
    {
        try
        {
            var dir = SaveAreaNtl();
            Directory.CreateDirectory(dir);
            var f = Path.Combine(dir, "external-running.txt");
            if (running) File.WriteAllText(f, DateTime.Now.ToString("s"));
            else if (File.Exists(f)) File.Delete(f);
        }
        catch (Exception ex) { Console.WriteLine(L("  [警告] 外部运行标记写入失败: ") + ex.Message); }
    }

    /// <summary>把「外部引擎已退出」告诉常驻中的 DELTARUNE（写标记文件，它每帧检测 → 0 秒回程）。</summary>
    private static void NotifyResidentExit()
    {
        try
        {
            var saveNtl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DELTARUNE", "Neutraled");
            Directory.CreateDirectory(saveNtl);
            File.WriteAllText(Path.Combine(saveNtl, "external-exited.txt"), DateTime.Now.ToString("s"));
            Console.WriteLine(L("  ✅ 已通知常驻中的 DELTARUNE 回到前台（0 秒回程）"));
        }
        catch (Exception ex) { Console.WriteLine(L("  [警告] 写回程标记失败: ") + ex.Message); }
    }

    /// <summary>★ 守候重启/上次会话残留时的自救：如果启动时发现 **DELTARUNE 窗口是隐藏的**，
    /// 说明有一局外部章节正在跑而没人负责收尾（改 builder 要重启守候 → 实测踩过）——
    /// 于是接管监视外部引擎进程，等它退出后恢复窗口 + 写回程标记。
    /// 不做这件事的后果：游戏永远停在隐藏状态，玩家以为卡死了。</summary>
    private static void RecoverHiddenGame(string gameRoot)
    {
        try
        {
            if (System.Diagnostics.Process.GetProcessesByName("DELTARUNE").Length == 0) return;
            var h = GameWindow();                                   // 隐藏时 MainWindowHandle = 0 → 必须枚举
            if (h == IntPtr.Zero || WinFocus.IsVisible(h)) return;

            var names = ExternalExeNames(gameRoot);
            if (names.Count == 0)
            {
                Console.WriteLine(L("  [恢复] 窗口是隐藏的，但 chapters.json 里没有外部章节 → 不动它（避免误恢复）"));
                return;
            }

            Console.WriteLine(L("  [恢复] DELTARUNE 窗口处于隐藏状态 → 接管监视：{0}", string.Join(", ", names)));
            var th = new System.Threading.Thread(() =>
            {
                try
                {
                    System.Threading.Thread.Sleep(3000);            // 先给主循环机会处理"刚按下的启动请求"
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    while (sw.Elapsed < TimeSpan.FromHours(3))
                    {
                        bool any = false;
                        foreach (var n in names)
                            if (System.Diagnostics.Process.GetProcessesByName(n).Length > 0) { any = true; break; }
                        if (!any) break;
                        System.Threading.Thread.Sleep(800);
                    }
                    Console.WriteLine(L("  [恢复] 外部引擎已退出 → 恢复游戏窗口并交还控制权"));
                    SetGameWindowVisible(true);
                    NotifyResidentExit();
                }
                catch (Exception ex) { Console.WriteLine(L("  [恢复] 异常: ") + ex.Message); }
            })
            { IsBackground = true };
            th.Start();
        }
        catch (Exception ex) { Console.WriteLine(L("  [恢复] 跳过: ") + ex.Message); }
    }

    static int WatchExternal(string gameRoot)
    {
        GoSilent(gameRoot);
        EnsureKristalCjkFallback(Path.Combine(gameRoot, "Kristal-main"));
        RecoverHiddenGame(gameRoot);
        // ★ 坑：GameMaker 的 file_text_open_write 会把路径**重定向进存档区**
        //   （实测写 "E:\...\Neutraled\launch-request.json" 实际落在
        //    %LOCALAPPDATA%\DELTARUNE\Neutraled\launch-request.json），所以两处都要找。
        var reqs = new List<string>
        {
            Path.Combine(Paths.NeutraledRoot(gameRoot), "launch-request.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "DELTARUNE", "Neutraled", "launch-request.json")
        };
        // ⚠ 以前这里会把**遗留请求**直接删掉 —— 于是"游戏先写了请求、守候才起来"的情况永远丢失（实测踩过）。
        //   改成什么都不做：主循环的 reqs.FirstOrDefault(File.Exists) 会正常处理它。
        // 守候重启：若确实没有外部引擎在跑，清掉过期的"外部运行"标记（否则游戏会一直被屏蔽输入）
        try
        {
            bool anyExt = false;
            foreach (var e in Chapters.LoadExternalList(gameRoot))
                if (e.TryGetValue("exe", out var v) && v is string s2 && s2.Length > 0 &&
                    System.Diagnostics.Process.GetProcessesByName(Path.GetFileNameWithoutExtension(s2)).Length > 0) { anyExt = true; break; }
            if (!anyExt) SetExternalRunning(false);
        }
        catch { }

        Console.WriteLine(L("===== 外部章节守候 ====="));
        Console.WriteLine(L("  在游戏里进入 Kristal 章节时会自动拉起它自己的引擎（关掉本窗口即停止守候）"));

        var deadline = DateTime.Now.AddHours(12);
        bool sawGame = false;
        System.Diagnostics.Process? extProc = null;      // 正在运行的���部引擎（Kristal）
        while (DateTime.Now < deadline)
        {
            System.Threading.Thread.Sleep(400);   // 轮询间隔（原 1500ms：退出检测白等）

            var req = reqs.FirstOrDefault(File.Exists);
            if (req != null)
            {
                System.Threading.Thread.Sleep(300);   // 等请求文件落盘（原 800ms）            // 等游戏把文件写完
                string txt;
                try { txt = File.ReadAllText(req); } catch { continue; }
                try { File.Delete(req); } catch { }
                try
                {
                    var o = JsonNode.Parse(txt) as JsonObject;
                    // ★ 面板里的「重新部署并重启」：游戏只能写请求文件（本运行时没有任何启动进程的内置函数），
                    //   守候进程在这里消费它 —— 先等游戏进程退出（data.win 被占用时部署会失败），再跑一次 --deploy-all。
                    var action = o?["action"]?.GetValue<string>() ?? "";
                    if (action == "deploy")
                    {
                        Console.WriteLine(L("  ▶ 收到重新部署请求（Neutraled 面板）"));
                        for (int w = 0; w < 80 && System.Diagnostics.Process.GetProcessesByName("DELTARUNE").Length > 0; w++)
                            System.Threading.Thread.Sleep(250);
                        var self = Environment.ProcessPath ?? "";
                        if (self.Length == 0) { Console.WriteLine(L("  [警告] 拿不到自身路径，无法重新部署")); continue; }
                        Console.WriteLine(L("  … 开始重新部署（--deploy-all）"));
                        try
                        {
                            var dp = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(self)
                            {
                                Arguments = "--deploy-all --game \"" + gameRoot + "\"",
                                UseShellExecute = false,
                                WorkingDirectory = gameRoot
                            });
                            dp?.WaitForExit();
                            Console.WriteLine(L("  ✅ 重新部署完成（退出码 {0}）", dp?.ExitCode ?? -1));
                        }
                        catch (Exception ex) { Console.WriteLine(L("  [警告] 重新部署失败: {0}", ex.Message)); }
                        try
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("steam://rungameid/1671210") { UseShellExecute = true });
                            Console.WriteLine(L("  ✅ 已请求 Steam 重新启动游戏"));
                        }
                        catch { }
                        sawGame = false;
                        continue;
                    }
                    var exe = o?["exe"]?.GetValue<string>() ?? "";
                    var args = o?["args"]?.GetValue<string>() ?? "";
                    var cwd = o?["cwd"]?.GetValue<string>() ?? "";
                    var name = o?["name"]?.GetValue<string>() ?? "";
                    if (exe.Length == 0) { Console.WriteLine(L("  [警告] 启动请求缺少 exe")); continue; }
                    Console.WriteLine(L("  ▶ 外部章节「{0}」→ 启动 {1} {2}", name, exe, args));
                    // 启动前顺手确保控制台是最新的（此刻 exe 没在运行，不会被占用）
                    FuseZip.EnsureConsole(exe, Path.Combine(Paths.NeutraledRoot(gameRoot), "kristal", "ntlconsole"), FindCjkFont(gameRoot));
                    extProc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe)
                    {
                        Arguments = args,
                        UseShellExecute = false,
                        WorkingDirectory = Directory.Exists(cwd) ? cwd : (Path.GetDirectoryName(exe) ?? "")
                    });
                    Console.WriteLine(L("  ✅ 已启动（Kristal 用的是它自己的存档目录）"));
                    Console.WriteLine(L("     玩完关掉外部引擎后，会自动把 DELTARUNE 拉回章节选择"));
                    SetExternalRunning(true);           // ① 告诉游戏"外部引擎在跑"→ 它屏蔽自己的输入
                    SetGameWindowVisible(false);        // ② 先把游戏窗口藏起来（任务栏也一起消失）—— 前台空出来更容易抢
                    if (extProc != null) FocusExternalWindow(extProc);   // ③ 再把焦点交给外部引擎（否则 F2 等按键都进了隐藏的游戏）
                }
                catch (Exception ex) { Console.WriteLine(L("  [警告] 启动外部引擎失败: {0}", ex.Message)); }
                continue;
            }

            // ★ 外部引擎（Kristal）运行期间：安静等待，别把"游戏已退出"当成收工条件
            if (extProc != null)
            {
                if (!extProc.HasExited) { ShowHiddenGameDialogs(); System.Threading.Thread.Sleep(500); continue; }
                Console.WriteLine(L("  ◀ 外部引擎已退出"));
                SetExternalRunning(false);
                SetGameWindowVisible(true);             // 恢复窗口并置前
                // ★ 优先走"常驻回程"：DELTARUNE 没退出的话，只要写个标记文件，
                //   它每帧检测到就会把控制权还回来（0 秒）—— 省掉 Steam 重新拉起 + GameMaker 引擎初始化 ≈33 秒。
                var drAlive = System.Diagnostics.Process.GetProcessesByName("DELTARUNE").Length > 0;
                if (drAlive) NotifyResidentExit();
                else
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                            "steam://rungameid/1671210") { UseShellExecute = true });
                        Console.WriteLine(L("  ✅ 游戏已被关闭 → 已请求 Steam 重新启动（这次要等引擎初始化 ≈33s）"));
                    }
                    catch (Exception ex) { Console.WriteLine(L("  [警告] 拉回游戏失败: {0}（手动开一下即可）", ex.Message)); }
                }
                extProc = null;
                sawGame = true;                                  // 常驻模式下游戏一直在跑，别重复打印"已启动"
                System.Threading.Thread.Sleep(400);   // 外部引擎退出 → 立刻回 DELTARUNE（原 3000ms）
                continue;
            }

            var g = System.Diagnostics.Process.GetProcessesByName("DELTARUNE");
            if (g.Length > 0) { if (!sawGame) { sawGame = true; Console.WriteLine(L("  ▶ 游戏已启动，守候中...")); } }
            else if (sawGame)
            {
                System.Threading.Thread.Sleep(2500);            // 给请求文件一点落盘时间
                if (reqs.Any(File.Exists)) continue;
                // ⚠ 不要自杀！守候是**常驻服务**（开机自启 + 桌面启动器都会用它）：
                //   常驻模式下游戏本来就可能"看起来退出"（切换章节会换进程），
                //   一旦退出就没人监听后续的外部章节请求了（实测：秒回成功一次后守候就没了）。
                Console.WriteLine(L("  ◀ 游戏暂时不在运行 —— 继续守候（等它回来）"));
                sawGame = false;
                continue;
            }
        }
        return 0;
    }

    /// <summary>把"外部引擎章节"（Kristal 项目）装进它自己引擎的 mods/ 目录。
    /// 项目本体在导入时被完整保留在 mod 的 kristal/ 子目录里 —— 自包含、可随 mod 搬走。
    /// 这样章节选择里选中它时，Neutraled 只是"退出游戏 + 拉起那个引擎"，插件/进度全原生。</summary>
    /// <summary>处理 mod 作者自报的适配程度（mod.json 的 ntl_adapt）。
    ///
    /// 设计原则：**宁可慢，不可错**。
    ///   1) level=fast 展开成逐项布尔；
    ///   2) 逐条**校验声明与实际内容是否相符**，不符就忽略该条并响亮警告；
    ///   3) 只有**所有**启用的 mod 都声明「快速档下功能正常」时，才整体切到快速档
    ///      （跳过部署里最贵的"输入函数重定向"，chapter5 约 20 秒）；
    ///   4) 打印适配报告，让玩家和作者都看得到到底加速了什么、代价是什么。</summary>
    private static void ApplyAdaptations(List<ModEntry> mods)
    {
        var enabled = mods.Where(m => m.Enabled).ToList();
        if (enabled.Count == 0) return;

        int fastOk = 0, declared = 0;
        foreach (var m in enabled)
        {
            var a = m.Adapt;
            if (a == null) continue;
            declared++;
            a.Normalize();

            // ---- 校验：声明必须与实际内容相符 ----
            if (a.AssetsOnly && (m.Patches.Count > 0 || m.FindReplace.Count > 0))
            {
                Console.WriteLine(L("  [适配] {0}: 声明『仅资源』但实际带了 {1} 个 patch / ", m.Name, m.Patches.Count) +
                                  L("{0} 个 find_replace → **忽略该声明**（按完整通道）", m.FindReplace.Count));
                a.AssetsOnly = false;
            }
            if (a.NoHooks && m.HooksRaw is { Count: > 0 })
            {
                Console.WriteLine(L("  [适配] {0}: 声明『无 hook』但实际有 {1} 条 hook 声明 → **忽略该声明**", m.Name, m.HooksRaw.Count));
                a.NoHooks = false;
            }
            if (a.Chapter.HasValue && a.Chapter.Value != ChapterNumber(m.Chapter))
            {
                Console.WriteLine(L("  [适配] {0}: 声明章节 {1} 与实际所在章节 {2} 不符 → **忽略该声明**", m.Name, a.Chapter.Value, m.Chapter));
                a.Chapter = null;
            }
            if (a.NoConsoleInput) fastOk++;
            Console.WriteLine(L("  [适配] {0}: {1}", m.Name, a.Describe()));
        }

        Console.WriteLine(L("  适配汇总: {0}/{1} 个 mod 带适配声明；", declared, enabled.Count) +
                          L("其中 {0} 个声明可在快速档运行（其余按完整通道）", fastOk));

        if (fastOk == enabled.Count && fastOk > 0)
        {
            if (FullDeployForced)
            {
                Console.WriteLine(L("  [适配] 全部 mod 都声明兼容快速档，但玩家指定了 --full-deploy → 仍走完整档"));
            }
            else if (Injector.FastDeploy)
            {
                Console.WriteLine(L("  [适配] 已是快速档（--fast-deploy）"));
            }
            else
            {
                Injector.FastDeploy = true;
                Console.WriteLine(L("  [适配] ✅ 全部启用 mod 都声明兼容快速档 → 自动走快速通道（跳过输入重定向，约省 20 秒）"));
                Console.WriteLine(L("         代价：章节内『控制台输入屏蔽』失效 —— 控制台仍能打开，但游戏自身的 keyboard_check_direct 仍会读到按键"));
                Console.WriteLine(L("         想强制完整档：加 --full-deploy"));
            }
        }
    }

    /// <summary>把 "chapter5" 这类章节名转成数字（root=0，无法识别返回 -1）。</summary>
    private static int ChapterNumber(string? chapter)
    {
        if (string.IsNullOrEmpty(chapter)) return -1;
        if (chapter.Equals("root", StringComparison.OrdinalIgnoreCase)) return 0;
        var digits = new string(chapter.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var n) ? n : -1;
    }

    /// <summary>文件指纹（大小 + 修改时间）—— 用于按内容缓存"只取决于文件内容"的计算结果。</summary>
    private static string FileFp(string path)
    {
        try { var fi = new FileInfo(path); return fi.Length + "@" + fi.LastWriteTimeUtc.Ticks; }
        catch { return "?"; }
    }

    /// <summary>包指纹：大小 + 修改时间（包变了才重新导入）。</summary>
    private static string? Fingerprint(string file)
    {
        try { var fi = new FileInfo(file); return fi.Length + "|" + fi.LastWriteTimeUtc.Ticks; }
        catch { return null; }
    }

    /// <summary>已成功导入过的包清单（--import-all 幂等用）。</summary>
    private static Dictionary<string, string> LoadImportManifest(string path)
    {
        try
        {
            if (!File.Exists(path)) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path), Paths.Json)
                   ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch { return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
    }

    /// <summary>外部章节清单的指纹（进部署签名用）。</summary>
    private static string ExternalSig(string gameRoot)
    {
        try
        {
            var f = Path.Combine(Paths.NeutraledRoot(gameRoot), "chapters-external.json");
            if (!File.Exists(f)) return "";
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f)))[..16];
        }
        catch { return ""; }
    }

    /// <summary>--add-external &lt;exe&gt; [名字] [--ext-args ...]：把任意可执行文件注册成
    /// **外部章节**（章节选择器里能选、选中后退出 DELTARUNE 并拉起它）。
    /// 用途：像「冰封帷幕」这种**打包好的成品**（fused LÖVE exe，没有工程源码）也能作为章节玩。
    /// 原理与 Kristal 章节一致：写 Neutraled/launch-request.json + game_end()，由守候进程拉起。</summary>
    private static int AddExternalChapter(string gameRoot, string exe, string name, string args)
    {
        var full = Path.GetFullPath(exe);
        if (!File.Exists(full)) { Console.WriteLine(L("找不到可执行文件: ") + full); return 1; }
        if (string.IsNullOrEmpty(name)) name = Path.GetFileNameWithoutExtension(full);
        var entry = new Dictionary<string, string>
        {
            ["id"] = "external:" + Path.GetFileNameWithoutExtension(full).ToLowerInvariant(),
            ["name"] = name,
            ["kind"] = "external",
            ["chapter"] = "0",
            ["order"] = "90",
            ["exe"] = full,
            ["args"] = args,
            ["cwd"] = Path.GetDirectoryName(full) ?? gameRoot,
            ["source"] = "external",
            ["author"] = "external"
        };
        var list = Chapters.LoadExternalList(gameRoot);
        list.RemoveAll(e => string.Equals(e.TryGetValue("exe", out var v) ? v as string : null, full, StringComparison.OrdinalIgnoreCase));
        list.Add(entry);
        Directory.CreateDirectory(Paths.NeutraledRoot(gameRoot));
        var file = Path.Combine(Paths.NeutraledRoot(gameRoot), "chapters-external.json");
        File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(list, Paths.Json), new System.Text.UTF8Encoding(false));
        Console.WriteLine(L("✓ 已注册外部章节「{0}」（{1}）", name, full));
        Console.WriteLine(L("  下次部署（--deploy-all）后即可在章节选择器里看到它；选中后会退出 DELTARUNE 并拉起该程序。"));
        return 0;
    }

    /// <summary>并行度：默认 min(4, 核数/2)（每个 worker 是一个独立进程，互不共享静态状态）。</summary>
    /// <summary>默认并行度。**分阶段不同**（都实测过）：
    ///   部署：章节互相独立、以 229MB 级 I/O 为主 → 6 路最好（4 路 64s → 6 路 55-57s）；
    ///   转换：压缩包解压 + data.win 解析混合，抢 IO 严重 → 6 路反而更慢（50s → 94s），4 路最优。
    /// 用户可用 --jobs 覆盖。</summary>
    private static int JobsOrDefault(bool forImport = false)
    {
        if (Jobs > 0) return Jobs;
        var cap = Math.Max(2, Environment.ProcessorCount - 2);
        return forImport ? Math.Max(2, Math.Min(4, cap)) : Math.Max(2, Math.Min(6, cap));
    }

    /// <summary>--deploy-all [--jobs N]：并行部署 root + 所有已存在的章节。
    /// 各章节产物是不同文件、互不依赖 → 并行是安全的；每个 worker 起独立进程，
    /// 避免共享 Injector/ScanMods 等静态状态。</summary>
    private static int RunParallelDeploy(string gameRoot)
    {
        var chapters = new List<string> { "root" };
        for (int i = 1; i <= 7; i++)
        {
            var ch = "chapter" + i;
            if (File.Exists(Paths.ChapterDataWin(gameRoot, ch))) chapters.Add(ch);
        }
        // 按产物体积**降序**启动：并行时关键路径 = 最大那一章，先开它才不会拖尾
        chapters = chapters.OrderByDescending(c =>
        {
            try { var f = Paths.ChapterDataWin(gameRoot, c); return File.Exists(f) ? new FileInfo(f).Length : 0; }
            catch { return 0L; }
        }).ToList();
        var forceArg = ForceDeploy ? " --force" : "";
        if (ForceTimelines) forceArg += " --force-timelines";
        // ⚠ --base-mod 必须转发给 worker：否则 `--deploy-all --base-mod X` 看起来生效了、其实每个 worker
            //   都按默认顺序挑基底（本机 6 个整包 mod，汉化包就永远选不上 → 章节里中文全是空白，实测踩过）
            var baseArg = string.IsNullOrEmpty(BaseModId) ? "" : " --base-mod " + BaseModId;
            // ⚠ worker 一律 --no-timelines：时间线只有 3 个产物目录，6 个 worker 各跑一遍 = 6 路并发抢同一个
            //   data.win、互相覆盖各自的 .ntl-deploy-*.sig（实测是 --deploy-all 慢与时间线反复重建的主因之一）
            var rc = RunParallel(chapters.Select(c => "--deploy --chapter " + c + forceArg + baseArg + " --no-timelines").ToList(),
                           chapters, L("部署"), JobsOrDefault());

            // 平行时间线统一由父进程部署一次（串行；worker 已用 --no-timelines 跳过）
            try
            {
                var tlModsRoot = Path.Combine(Paths.NeutraledRoot(gameRoot), "mods");
                const string tlChapter = "chapter1";
                LastAllMods = Mods.ScanMods(tlModsRoot, tlChapter, includeDisabled: false, allChapters: true);
                LastRegistry = Chapters.BuildRegistry(LastAllMods, tlChapter, gameRoot);
                Console.WriteLine(L("===== 平行时间线（由父进程统一部署，worker 已跳过） ====="));
                var tlBlocked = DeployTimelines(gameRoot, tlChapter);
                // ★ 守卫拦下 ≥1 条时间线 ⇒ 整个 --deploy-all 退出码非零
                //   （t31 复核 F1：过去返回值被丢弃，拦下了也照样 exit 0）
                if (tlBlocked > 0) rc = 1;
            }
            catch (Exception ex) { Console.WriteLine(L("[警告] 时间线部署失败: {0}", ex.Message)); }
            return rc;
    }

    /// <summary>--import-all &lt;目录&gt; [--jobs N]：并行转换目录下所有包。</summary>
    private static int RunParallelImport(string gameRoot, string dir)
    {
        if (!Directory.Exists(dir)) { Console.WriteLine(L("找不到目录: ") + dir); return 1; }
        var exts = new[] { ".zip", ".7z", ".rar", ".tar", ".gz", ".xdelta", ".win", ".ntlmod" };
        var all = Directory.GetFiles(dir)
            .Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => new FileInfo(f).Length)      // 小的先跑，长尾更短
            .ToList();
        if (all.Count == 0) { Console.WriteLine(L("目录里没有可导入的包: ") + dir); return 1; }

        // ★ 幂等：包没变过（大小+修改时间一致）且上次导入成功 → 直接跳过。
        //   实测「重跑全流程」里转换占 50s，而其中绝大多数包其实没变。
        var manPath = Path.Combine(Paths.NeutraledRoot(gameRoot), "cache", "import-manifest.json");
        var man = NoCache ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) : LoadImportManifest(manPath);
        var files = new List<string>();
        int skipped = 0;
        foreach (var f in all)
        {
            var fp = Fingerprint(f);
            if (fp != null && man.TryGetValue(f, out var old) && old == fp) { skipped++; continue; }
            files.Add(f);
        }
        if (skipped > 0) Console.WriteLine(L("跳过 {0} 个未变化的包（内容+时间都没变，上次已成功导入）", skipped));
        if (files.Count == 0) { Console.WriteLine(L("全部包都未变化，无需转换 ✓")); return 0; }
        var rc = RunParallel(files.Select(f => "--import-mod \"" + f + "\"").ToList(), files, L("转换"), JobsOrDefault());
        // 成功的包记进清单（失败的不记，下次还会重试）
        foreach (var f in files)
        {
            var fp = Fingerprint(f);
            if (fp != null) man[f] = fp;
        }
        try { Directory.CreateDirectory(Path.GetDirectoryName(manPath)!); File.WriteAllText(manPath, System.Text.Json.JsonSerializer.Serialize(man, Paths.Json)); } catch { }
        return rc;
    }

    /// <summary>把一组命令放进若干子进程并行跑，打印每项用时与总用时。</summary>
    private static int RunParallel(List<string> argList, List<string> labels, string phase, int jobs)
    {
        var exe = Environment.ProcessPath!;
        var results = new System.Collections.Concurrent.ConcurrentBag<(string label, int code, double sec)>();
        var total = System.Diagnostics.Stopwatch.StartNew();
        Console.WriteLine(L("并行{0}: {1} 项，并行度 {2}", phase, argList.Count, jobs));
        System.Threading.Tasks.Parallel.ForEach(
            Enumerable.Range(0, argList.Count),
            new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = jobs },
            idx =>
            {
                var w = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo(exe)
                    {
                        Arguments = argList[idx],
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        WorkingDirectory = Path.GetDirectoryName(exe)
                    };
                    using var pr = System.Diagnostics.Process.Start(psi)!;
                    var so = pr.StandardOutput.ReadToEnd();
                    var se = pr.StandardError.ReadToEnd();
                    pr.WaitForExit();
                    w.Stop();
                    results.Add((labels[idx], pr.ExitCode, w.Elapsed.TotalSeconds));
                    if (pr.ExitCode != 0)
                    {
                        Console.WriteLine(L("  [失败] {0}（exit={1}）", labels[idx], pr.ExitCode) + (se.Length > 0 ? " " + se.Split('\n')[0] : ""));
                        // ⚠ 子进程 stdout 原本被直接丢弃 → 并行部署失败时只剩一行「[失败] chapter1」，
                        //   看不到真正原因（实测踩过：只能手动单跑该章复现）。失败时补打尾部 12 行 + stderr。
                        var tail = so.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
                        foreach (var ln in tail.Skip(Math.Max(0, tail.Length - 12)))
                            Console.WriteLine("      | " + ln);
                        if (se.Trim().Length > 0)
                            Console.WriteLine("      ! " + se.Trim().Replace("\n", "\n      ! "));
                    }
                }
                catch (Exception ex)
                {
                    w.Stop();
                    results.Add((labels[idx], 1, w.Elapsed.TotalSeconds));
                    Console.WriteLine(L("  [异常] {0}: {1}", labels[idx], ex.Message));
                }
            });
        total.Stop();
        Console.WriteLine();
        foreach (var r in results.OrderByDescending(x => x.sec))
            Console.WriteLine($"  {(r.code == 0 ? "✓" : "✗")} {r.label,-12} {r.sec,6:N1}s");
        int bad = results.Count(r => r.code != 0);
        Console.WriteLine(L("{0}完成: {1}/{2} 成功，总用时 {3:N1}s（并行度 {4}）", phase, results.Count - bad, results.Count, total.Elapsed.TotalSeconds, jobs));
        return bad == 0 ? 0 : 1;
    }

    /// <summary>--extract &lt;压缩包&gt; &lt;目标目录&gt;：解压任意格式（zip/7z/rar/tar/gz）。</summary>
    private static int ExtractCli(string archive, string outDir)
    {
        if (!File.Exists(archive)) { Console.WriteLine(L("找不到压缩包: ") + archive); return 1; }
        var dest = string.IsNullOrEmpty(outDir) ? Path.Combine(Path.GetDirectoryName(archive)!, Path.GetFileNameWithoutExtension(archive)) : outDir;
        Directory.CreateDirectory(dest);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int n;
        var ext = Path.GetExtension(archive).ToLowerInvariant();
        if (ext is ".zip" or ".ntlmod") { System.IO.Compression.ZipFile.ExtractToDirectory(archive, dest, true); n = Directory.GetFiles(dest, "*", SearchOption.AllDirectories).Length; }
        else n = ModdingImport.ExtractAny(archive, dest);
        sw.Stop();
        Console.WriteLine(L("✓ 解压 {0} 个文件 → {1}（{2:N1}s）", n, dest, sw.Elapsed.TotalSeconds));
        return 0;
    }

    /// <summary>--make-cjk-font：生成中文字体包（默认输出 Neutraled/fonts，部署时自动导入）。</summary>
    private static int MakeCjkFontCli(string gameRoot, string? outDir)
    {
        var root = Paths.NeutraledRoot(gameRoot);
        var ttf = CjkTtf;
        if (string.IsNullOrEmpty(ttf))
        {
            var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            foreach (var cand in new[] { "simhei.ttf", "deng.ttf", "msyh.ttc", "simsun.ttc" })
            {
                var cp = Path.Combine(windir, "Fonts", cand);
                if (File.Exists(cp)) { ttf = cp; break; }
            }
        }
        if (string.IsNullOrEmpty(ttf) || !File.Exists(ttf))
        {
            Console.WriteLine(L("找不到中文字体，请用 --ttf <路径> 指定（例：C:\\Windows\\Fonts\\simhei.ttf）"));
            return 1;
        }
        var target = string.IsNullOrEmpty(outDir) ? Path.Combine(root, "fonts") : outDir;
        Console.WriteLine(L("生成中文字体包: {0} / {1}px / 字符集={2} → {3}", Path.GetFileName(ttf), CjkSize, CjkCharset, target));
        if (CjkCharset == "list") Console.WriteLine(L("  显式字表（--chars）: {0} 个字符", CjkChars.Length));
        if (!string.IsNullOrEmpty(CjkFromWin))
        {
            try
            {
                var sw2 = System.Diagnostics.Stopwatch.StartNew();
                var (g2, s2) = CjkFont.FromDataWin(target, CjkFromWin, CjkSourceFont, CjkName);
                sw2.Stop();
                Console.WriteLine(L("✓ 完成: 从 {0} 搬来 {1} 字形 / {2} 页，用时 {3:N1} 秒", Path.GetFileName(CjkFromWin), g2, s2, sw2.Elapsed.TotalSeconds));
                Console.WriteLine(L("  下一步：--deploy 会自动导入（Neutraled/fonts/*.json）"));
                return 0;
            }
            catch (Exception ex) { Console.WriteLine(L("搬字形失败: ") + ex.Message); return 1; }
        }
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var (glyphs, sheets) = CjkFont.Make(target, ttf, CjkSize, CjkCharset, CjkName, root, CjkChars);
            sw.Stop();
            Console.WriteLine(L("✓ 完成: {0} 字形 / {1} 张 sheet，用时 {2:N1} 秒", glyphs, sheets, sw.Elapsed.TotalSeconds));
            Console.WriteLine(L("  下一步：--deploy 会自动导入（Neutraled/fonts/*.json）"));
            return 0;
        }
        catch (Exception ex) { Console.WriteLine(L("生成失败: ") + ex.Message); return 1; }
    }

    private static void InstallExternalChapters(List<ModEntry> mods)
    {
        foreach (var m in mods)
        {
            var ext = m.KristalExternal;
            if (ext == null || string.IsNullOrEmpty(ext.Exe) || string.IsNullOrEmpty(ext.Project)) continue;
            var src = Path.Combine(m.Dir, ext.Project.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(src))
            {
                Console.WriteLine(L("  [警告] 外部章节 {0}: 保留的项目目录不存在 {1}", m.Name, src));
                continue;
            }
            var name = string.IsNullOrEmpty(ext.ModName) ? Mods.Sanitize(m.Name) : ext.ModName;
            var dst = Path.Combine(ext.EngineMods, name);
            try
            {
                CopyTree(src, dst);
                Console.WriteLine(L("  [外部章节] {0} → {1}", m.Name, dst));
            }
            catch (Exception ex) { Console.WriteLine(L("  [警告] 外部章节 {0} 安装失败: {1}", m.Name, ex.Message)); }
        }
    }

    private static void CopyTree(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        int copied = 0, skipped = 0;
        foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, f);
            var o = Path.Combine(dst, rel);
            // 增量化：大小 + 修改时间都相同就认为没变，跳过复制。
            // （外部章节的项目文件在每次部署都会被搬一遍，350+ 个文件里有 99% 是没变的）
            try
            {
                var fi = new FileInfo(f);
                var oi = new FileInfo(o);
                if (oi.Exists && oi.Length == fi.Length &&
                    oi.LastWriteTimeUtc == fi.LastWriteTimeUtc) { skipped++; continue; }
            }
            catch { }
            Directory.CreateDirectory(Path.GetDirectoryName(o)!);
            File.Copy(f, o, true);
            copied++;
        }
        if (skipped > 0) Console.WriteLine(L("    外部章节增量复制: 更新 {0} / 未变跳过 {1}", copied, skipped));
    }

    /// <summary>只查冲突不部署（CI / GUI 友好）：退出码 0=无冲突，2=有 Error 级冲突。</summary>
    private static int ConflictsCli(string gameRoot, string chapter)
    {
        var mods = Mods.ScanMods(Paths.ModsRoot(gameRoot), chapter);
        mods = Mods.SortByDependencies(mods);
        Console.WriteLine(L("===== 冲突报告（{0}，{1} 个启用的 mod）=====", chapter, mods.Count));
        var report = Conflicts.Analyze(mods);
        Conflicts.PrintReport(report);
        var outPath = Path.Combine(Paths.NeutraledRoot(gameRoot), "conflicts.json");
        Conflicts.WriteJson(report, outPath);
        Console.WriteLine(L("  已写出: {0}", outPath));
        Console.WriteLine(L("  退出码约定: 0=无冲突 / 2=有 {0} 个 Error 级冲突", report.ErrorCount));
        return report.ErrorCount > 0 ? 2 : 0;
    }

    /// <summary>--export-shaders &lt;data.win&gt; &lt;mod 章节目录&gt; [--base &lt;基线&gt;]
    /// 把着色器导出成资源包（写进 &lt;mod 章节目录&gt;/shaders/&lt;名字&gt;.json）。
    /// ★ 着色器是唯一「既没有资源包通道、又不能只靠代码」的资源：缺了它，滤镜类 mod 静默失效
    ///   （asset_get_index 返回 -1，日志一个字都没有）。percentage [color] 就是这么失效的。</summary>
    private static int ExportShaders(string win, string outDir, string? baseWin)
    {
        if (!File.Exists(win)) { Console.WriteLine(L("  [错误] 找不到 {0}", win)); return 1; }
        UndertaleData? baseline = null;
        if (!string.IsNullOrEmpty(baseWin))
        {
            if (File.Exists(baseWin)) baseline = Injector.Load(baseWin);
            else Console.WriteLine(L("  [警告] 基线不存在: {0}（改为全量导出）", baseWin));
        }
        Console.WriteLine(L("===== 着色器资源包导出 =====\n  源  : {0}\n  输出: {1}\n  基线: {2}", win, outDir,
            baseline != null ? baseWin! : L("（无 → 全量导出）")));
        var src = Injector.Load(win);
        var n = ShaderPack.Export(src, baseline, outDir);
        // 返回值是"导出个数"，**不能**当退出码（几十个会被截成 1，脚本误判失败）
        if (n < 0) { Console.WriteLine(L("  [错误] 导出失败")); return 1; }
        Console.WriteLine(L("  导出着色器: {0} 个", n));
        return 0;
    }

    /// <summary>导出资源包（精灵/声音/字体）到 mod 目录，供叠加到任意基底。</summary>
    private static int ExportPacks(string gameRoot, string win, string chapter, bool chapterExplicit,
        string? name, string? author, string? outDir, string? baseWin)
    {
        var ch = chapterExplicit ? chapter : (DiffLayer.InferChapterFromPath(win) ?? chapter);
        var baseline = baseWin ?? Paths.BackupDataWin(gameRoot, ch);
        var dir = outDir;
        if (string.IsNullOrEmpty(dir))
        {
            var n = string.IsNullOrWhiteSpace(name) ? new DirectoryInfo(Path.GetDirectoryName(win)!).Name : name!;
            var id = string.IsNullOrWhiteSpace(author) ? "converted" : author!;
            dir = Path.Combine(Paths.NeutraledRoot(gameRoot), "mods", DeltaImport.Sanitize(n), DeltaImport.Sanitize(id), ch);
        }
        Console.WriteLine(L("===== 资源包导出 =====\n  章节: {0}\n  输出: {1}", ch, dir));
        Console.WriteLine(L("  基线: ") + (File.Exists(baseline) ? baseline + L("（差异导出）") : L("（无 → 全量导出）")));
        var exported = PackExport.Export(win, File.Exists(baseline) ? baseline : null, dir, true, true, true);
        // 返回值是"导出资源数"，**不能**当进程退出码用（2001 会被截成 1，脚本会误判失败）
        if (exported < 0) { Console.WriteLine(L("  [错误] 导出失败")); return 1; }
        Console.WriteLine(L("  导出资源总数: {0}", exported));
        return 0;
    }

    private static readonly string[] AllChapters = { "chapter1", "chapter2", "chapter3", "chapter4", "chapter5", "root" };

    /// <summary>最近一次主部署扫描到的全部 mod / 生成的注册表（供时间线部署使用）。</summary>
    /// <summary>诊断开关：设为 true 时不改存档名（用于对比测试）。</summary>
    public static bool DisableSaveRename = false;

    /// <summary>--base-mod &lt;id&gt;：一章只能有一个整包 data.win 基底，用它显式指定哪个 mod 当基底。</summary>
    public static string? BaseModId = null;

    private static List<ModEntry> LastAllMods = new();
    private static List<ChapterEntry> LastRegistry = new();

    private static int DeployAll(string gameRoot, string chapter)
    {
        if (!chapter.Equals("all", StringComparison.OrdinalIgnoreCase))
            return Deploy(gameRoot, chapter);

        int rc = 0, ok = 0;
        foreach (var ch in AllChapters)
        {
            Console.WriteLine(L("===== 部署 {0} =====", ch));
            try { if (Deploy(gameRoot, ch) == 0) ok++; else rc = 1; }
            catch (Exception ex) { Console.WriteLine(L("[错误] {0}: {1}", ch, ex.Message)); rc = 1; }
        }
        Console.WriteLine(L("===== 全部完成: {0}/{1} 章成功 =====", ok, AllChapters.Length));
        return rc;
    }

    private static int RestoreChapter(string gameRoot, string chapter)
    {
        var win = Paths.ChapterDataWin(gameRoot, chapter);
        var backup = Paths.BackupDataWin(gameRoot, chapter);
        if (!File.Exists(backup)) { Console.WriteLine(L("[错误] 缺少原版备份: {0}", backup)); return 1; }

        bool same = false;
        if (File.Exists(win))
        {
            var a = new FileInfo(win); var b = new FileInfo(backup);
            same = a.Length == b.Length;
        }
        if (!same)
        {
            File.Copy(backup, win, true);
            Console.WriteLine(L("已恢复原版 data.win <- {0}", backup));
        }
        else Console.WriteLine(L("data.win 与原版一致（跳过）"));

        // 还原被覆盖的外部文件
        var filesBackupRoot = Path.Combine(Paths.NeutraledRoot(gameRoot), ".backup_files", chapter);
        int restored = 0;
        if (Directory.Exists(filesBackupRoot))
        {
            var chapterDir = Paths.ChapterDir(gameRoot, chapter);
            foreach (var f in Directory.GetFiles(filesBackupRoot, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(filesBackupRoot, f);
                var target = Path.Combine(chapterDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                // ★ 并行部署的竞态：root 与各章节 worker 会同时写同一份 files/ 覆盖
                //   （实测 chapter2_windows/lang/lang_en.json 被另一个 worker 占用：
                //    "The process cannot access the file ... because it is being used by another process"，
                //    导致 root 整体部署失败 5/6）→ 目标被占用时退避重试，策略与 Paths.SafeWrite 一致。
                for (var _try = 0; ; _try++)
                {
                    try { File.Copy(f, target, true); break; }
                    catch (IOException) when (_try < 6) { System.Threading.Thread.Sleep(120 * (_try + 1)); }
                }
                restored++;
            }
            if (restored > 0) Console.WriteLine(L("已还原 {0} 个外部文件", restored));
        }

        var list = Path.Combine(Paths.ChapterDir(gameRoot, chapter), "Neutraled", "mods.json");
        if (File.Exists(list)) { File.Delete(list); Console.WriteLine(L("已删除运行时清单 mods.json")); }
        var scope = Path.Combine(Paths.ChapterDir(gameRoot, chapter), "Neutraled", "scope.json");
        if (File.Exists(scope)) { File.Delete(scope); Console.WriteLine(L("已删除产物作用域清单 scope.json")); }

        Console.WriteLine(L("恢复完成 ✓ {0} 已回到未部署状态", chapter));
        return 0;
    }

    private static int SpriteInfo(string gameRoot, string chapter, string name)
    {
        var data = Injector.Load(Paths.ChapterDataWin(gameRoot, chapter));
        var s = data.Sprites.FirstOrDefault(x => x.Name?.Content == name);
        if (s == null) { Console.WriteLine(L("未找到精灵: ") + name); return 1; }
        Console.WriteLine(L("{0}: {1}x{2}  帧={3}  原点=({4},{5})  ", name, s.Width, s.Height, s.Textures.Count, s.OriginX, s.OriginY) +
                          L("边距 L{0} R{1} T{2} B{3}  ", s.MarginLeft, s.MarginRight, s.MarginTop, s.MarginBottom) +
                          L("bbox={0} sep={1} 掩码字节={2}", s.BBoxMode, s.SepMasks, s.CollisionMasks?.Count ?? -1));
        return 0;
    }

    private static int ListSprites(string gameRoot, string chapter, string filter)
    {
        var data = Injector.Load(Paths.ChapterDataWin(gameRoot, chapter));
        int n = 0;
        foreach (var s in data.Sprites.Where(s => (s.Name?.Content ?? "").Contains(filter, StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine(L("{0}  {1}x{2}  帧={3}", s.Name?.Content, s.Width, s.Height, s.Textures.Count));
            if (++n >= 60) break;
        }
        return 0;
    }

    private static int Dump(string gameRoot, string chapter, string codeNames)
    {
        var data = Injector.Load(Paths.ChapterDataWin(gameRoot, chapter));
        foreach (var codeName in codeNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            Console.WriteLine($"===== {codeName} =====");
            try { Console.WriteLine(Injector.Decompile(data, codeName)); }
            catch (Exception ex) { Console.WriteLine(L("[dump 失败] ") + ex.Message); }
        }
        return 0;
    }

    private static int ListCodes(string gameRoot, string chapter, string filter)
    {
        var data = Injector.Load(Paths.ChapterDataWin(gameRoot, chapter));
        foreach (var c in data.Code.Where(c => (c.Name?.Content ?? "").Contains(filter, StringComparison.OrdinalIgnoreCase)))
            Console.WriteLine(c.Name?.Content);
        return 0;
    }

    /// <summary>把 mod 目录打包成可分发文件（.ntlmod = zip）。</summary>
    private static int Pack(string gameRoot, string modName, string? author, string chapter, string? outPath)
    {
        var modsRoot = Paths.ModsRoot(gameRoot);
        string? modDir = null;

        if (!string.IsNullOrEmpty(author))
        {
            var d = Path.Combine(modsRoot, modName, author, chapter);
            if (Directory.Exists(d)) modDir = d;
        }
        if (modDir == null)
        {
            var byName = Path.Combine(modsRoot, modName);
            if (Directory.Exists(byName))
            {
                foreach (var authorDir in Directory.GetDirectories(byName))
                {
                    var d = Path.Combine(authorDir, chapter);
                    if (Directory.Exists(d)) { modDir = d; break; }
                }
                if (modDir == null && File.Exists(Path.Combine(byName, "mod.json"))) modDir = byName;
            }
        }
        if (modDir == null || !Directory.Exists(modDir))
        {
            Console.WriteLine(L("[错误] 找不到 mod: {0}（章节 {1}，作者 {2}）", modName, chapter, author ?? L("任意")));
            return 1;
        }

        var outFile = outPath ?? Path.Combine(Paths.DistRoot(gameRoot), $"{modName}-{chapter}.ntlmod");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outFile))!);
        if (File.Exists(outFile)) File.Delete(outFile);

        System.IO.Compression.ZipFile.CreateFromDirectory(
            modDir, outFile, System.IO.Compression.CompressionLevel.Optimal, false);

        var mb = new FileInfo(outFile).Length / 1024.0 / 1024.0;
        Console.WriteLine(L("已打包: {0}  ({1:F1} MB)", outFile, mb));
        Console.WriteLine(L("  分发方式：把该文件放到 mods/<mod_name>/ 下即可被自动识别（无需解压）"));
        return 0;
    }

    /// <summary>确定性短哈希（跨进程/跨机器一致）。</summary>
    private static string StableHash(string s)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        var bytes = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(s ?? ""));
        return Convert.ToHexString(bytes, 0, 4).ToLowerInvariant();   // 8 个十六进制字符
    }

    /// <summary>把 "chapter4" / "4" 解析成章节号。</summary>
    private static int ChapterNum(string chapter)
    {
        var digits = new string(chapter.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var n) ? n : 4;
    }

    /// <summary>输出 GeneralInfo 全部字段（用于确认存档路径的决定因素）。</summary>
    private static int GameInfo(string gameRoot, string chapter)
    {
        var data = Injector.Load(Paths.ChapterDataWin(gameRoot, chapter));
        var g = data.GeneralInfo;
        Console.WriteLine($"FileName        = {g.FileName?.Content}");
        Console.WriteLine($"Config          = {g.Config?.Content}");
        Console.WriteLine($"GameID          = {g.GameID}");
        Console.WriteLine($"Name            = {g.Name?.Content}");
        Console.WriteLine($"DisplayName     = {g.DisplayName?.Content}");
        Console.WriteLine($"Major.Minor.Rel = {g.Major}.{g.Minor}.{g.Release}.{g.Build}");
        Console.WriteLine($"BytecodeVersion = {g.BytecodeVersion}");
        Console.WriteLine($"DefaultWindowW/H= {g.DefaultWindowWidth}x{g.DefaultWindowHeight}");
        return 0;
    }

    /// <summary>列出房间表（索引与名字）。filter 为 "*" 时列全部。</summary>
    private static int ListRooms(string gameRoot, string chapter, string filter)
    {
        var data = Injector.Load(Paths.ChapterDataWin(gameRoot, chapter));
        int n = 0;
        for (int i = 0; i < data.Rooms.Count; i++)
        {
            var name = data.Rooms[i].Name?.Content ?? "";
            if (filter != "*" && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            Console.WriteLine($"{i}\t{name}");
            if (++n >= 300) { Console.WriteLine(L("...(截断)")); break; }
        }
        Console.WriteLine(L("共 {0} 个匹配（总房间数 {1}）", n, data.Rooms.Count));
        return 0;
    }

    /// <summary>列出对象表（索引 = instance_create 里用的编号）。filter 为 "*" 时列全部。</summary>
    private static int ListObjects(string gameRoot, string chapter, string filter)
    {
        var data = Injector.Load(Paths.ChapterDataWin(gameRoot, chapter));
        int n = 0;
        for (int i = 0; i < data.GameObjects.Count; i++)
        {
            var name = data.GameObjects[i].Name?.Content ?? "";
            if (filter != "*" && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            Console.WriteLine($"{i}\t{name}");
            if (++n >= 200) { Console.WriteLine(L("...(截断)")); break; }
        }
        Console.WriteLine(L("共 {0} 个匹配", n));
        return 0;
    }

    /// <summary>搜索引用了某函数名或字符串常量的代码对象（指令级，快速）。</summary>
    private static int FindRefs(string gameRoot, string chapter, string pattern)
    {
        var data = Injector.Load(Paths.ChapterDataWin(gameRoot, chapter));
        int n = 0;
        foreach (var code in data.Code)
        {
            bool hit = false;
            foreach (var ins in code.Instructions)
            {
                var fn = ins.ValueFunction?.Name?.Content;
                if (fn != null && fn.Contains(pattern, StringComparison.OrdinalIgnoreCase)) { hit = true; break; }
                var s = ins.ValueString?.Resource?.Content;
                if (s != null && s.Contains(pattern, StringComparison.OrdinalIgnoreCase)) { hit = true; break; }
            }
            if (hit)
            {
                Console.WriteLine(code.Name?.Content);
                if (++n >= 40) { Console.WriteLine(L("... (截断)")); break; }
            }
        }
        Console.WriteLine(L("共 {0} 个匹配", n));
        return 0;
    }

    private static int ShowVersion(string gameRoot, string chapter)
    {
        var ntl = Paths.NeutraledRoot(gameRoot);
        var apiDir = Path.Combine(ntl, "api");
        int scripts = Directory.Exists(apiDir) ? Directory.GetFiles(apiDir, "*.gml").Length : 0;

        // Neutraled 自身版本（发布包里没有游戏目录时也能打印）
        Console.WriteLine($"Neutraled {Paths.ApiVersion()}" + (scripts > 0 ? L("  （{0} 个 api 脚本）", scripts) : ""));
        if (!File.Exists(Path.Combine(gameRoot, "DELTARUNE.exe")))
        {
            Console.WriteLine(L("  未检测到游戏目录：可用 --game \"<游戏目录>\" 指定后再查看游戏版本"));
            return 0;
        }

        foreach (var (ch, path) in new[]
        {
            (L("原版(backup)"), Paths.BackupDataWin(gameRoot, chapter)),
            (L("当前部署"), Paths.ChapterDataWin(gameRoot, chapter))
        })
        {
            if (!File.Exists(path)) continue;
            try
            {
                var data = Injector.Load(path);
                var g = data.GeneralInfo;
                Console.WriteLine(L("{0}: 版本 {1}.{2}.{3}.{4}  ", ch, g.Major, g.Minor, g.Release, g.Build) +
                                  L("(bytecode {0}, 文件名 {1})  ", g.BytecodeVersion, g.FileName?.Content) +
                                  $"{new FileInfo(path).Length / 1024 / 1024} MB");
            }
            catch (Exception ex) { Console.WriteLine(L("{0}: 读取失败 {1}", ch, ex.Message)); }
        }
        return 0;
    }

    private static int Info(string gameRoot, string chapter)
    {
        var win = Paths.ChapterDataWin(gameRoot, chapter);
        if (!File.Exists(win)) { Console.WriteLine(L("找不到 {0}", win)); return 1; }
        var data = Injector.Load(win);
        Console.WriteLine($"  {win}");
        Console.WriteLine(L("  对象={0} 代码={1} 精灵={2} 脚本={3}", data.GameObjects.Count, data.Code.Count, data.Sprites.Count, data.Scripts.Count));
        return 0;
    }

    /// <summary>部署一个章节。
    /// outDirOverride：输出目录（平行时间线章节用；默认写回原章节目录）。
    /// modsOverride：只用指定的 mod 子集（默认按章节扫描）。
    /// runtimeChapterOverride：**运行时文件**（lang/、audiogroup1.dat、options.ini…）的来源章节。
    ///   平行时间线的产物 data.win 血统可能与 mod.json 声明的章节不同（例：第 1 章血统的产物声明成 chapter4），
    ///   照抄声明章节会把不匹配的 lang/ 复制过去 → 启动第一屏 Code Error（见 TimelineRuntime）。</summary>
    private static int Deploy(string gameRoot, string chapter, string? outDirOverride = null,
        List<ModEntry>? modsOverride = null, string? baseWinOverride = null, string? saveNameOverride = null,
        string? runtimeChapterOverride = null)
    {
        LastDeploySkipped = false;
        PluginBoot(gameRoot);
        var deployVeto = PluginFire(PluginHooks.BeforeDeploy, new { chapter });
        if (deployVeto != 0) return deployVeto;                       // 插件否决部署：原样返回它的码
        var srcWin = Paths.ChapterDataWin(gameRoot, chapter);
        var backup = Paths.BackupDataWin(gameRoot, chapter);
        var neutraled = Paths.NeutraledRoot(gameRoot);
        var apiDir = Path.Combine(neutraled, "api");
        var modsRoot = Path.Combine(neutraled, "mods");

        string win;
        if (outDirOverride != null)
        {
            Directory.CreateDirectory(outDirOverride);
            win = Path.Combine(outDirOverride, "data.win");
            Console.WriteLine(L("[输出] 平行时间线产物 -> {0}", win));

            // 复制源章节目录的运行时文件（音频组 / 外部音频 / options.ini 等）
            // 否则新章节目录缺少这些文件会导致游戏启动即黑屏
            // ★ 运行时文件按**血统章节**取，而不是 mod.json 声明的章节（见 TimelineRuntime）
            var srcDir = Paths.ChapterDir(gameRoot, runtimeChapterOverride ?? chapter);
            int copied = 0;
            foreach (var f in Directory.GetFiles(srcDir))
            {
                var name = Path.GetFileName(f);
                if (name.Equals("data.win", StringComparison.OrdinalIgnoreCase)) continue;
                // ★ builder 自己的账本文件（.ntl-deploy-<章节>.sig 等）**不是运行时文件**，绝不能跟着复制：
                //   它的名字只按源章节命名（例：时间线用 chapter4 当基底 → 源目录里的 .ntl-deploy-chapter4.sig），
                //   复制过来会**覆盖时间线自己的签名**，于是时间线每轮都拿"源章节的签名"和自己的比 → 永远判定内容已变、
                //   永远重建（实测：chapter1 单章 --force 58.8s，其中 3 个时间线 31.4s 全是白干；--deploy-all 同样翻倍）。
                if (name.StartsWith(".ntl-", StringComparison.Ordinal)) continue;
                try { File.Copy(f, Path.Combine(outDirOverride, name), true); copied++; } catch { }
            }
            foreach (var d in Directory.GetDirectories(srcDir))
            {
                var dn = Path.GetFileName(d);
                if (dn.Equals("Neutraled", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var f in Directory.GetFiles(d, "*", SearchOption.AllDirectories))
                {
                    if (Path.GetFileName(f).StartsWith(".ntl-", StringComparison.Ordinal)) continue;   // 账本文件，同上
                    var rel = Path.GetRelativePath(srcDir, f);
                    var dst = Path.Combine(outDirOverride, rel);
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                        File.Copy(f, dst, true);
                        copied++;
                    }
                    catch { }
                }
            }
            if (copied > 0) Console.WriteLine(L("  运行时文件复制: {0} 个", copied));
        }
        else win = srcWin;

        if (!File.Exists(srcWin)) { Console.WriteLine(L("找不到 {0}", srcWin)); return 1; }

        PhaseTimer.Reset();
        // 1) 备份原版（幂等：已存在则不动）
        if (!File.Exists(backup))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            File.Copy(srcWin, backup, true);
            Console.WriteLine(L("[1/5] 备份原版 -> {0}", backup));
        }
        else
        {
            Console.WriteLine(L("[1/5] 原版备份已存在（复用）"));
        }

        // 1.5) 「内容没变就跳过」的判断挪到 2.1.5)（章节注册表之后）—— 见那一段的说明。

        // 本产物的输入签名（检查与写回必须调用同一个函数，否则会永远「重做」）。
        // baseWinOverride（自带 data / 被引用章节产物）也是输入：它的指纹变了就必须重建。
        string DeploySignature(bool raw = false)
        {
            var sigMods = modsOverride ?? Mods.ScanMods(modsRoot, chapter, false, true);
            var extra = ExternalSig(gameRoot);
            if (baseWinOverride != null && File.Exists(baseWinOverride))
            {
                var bi = new FileInfo(baseWinOverride);
                extra += "|base=" + bi.FullName + ":" + bi.Length + "@" + bi.LastWriteTimeUtc.Ticks;
            }
            return raw ? Cache.SignatureRaw(Paths.ApiVersion(), chapter, sigMods, extra)
                       : Cache.Signature(Paths.ApiVersion(), chapter, sigMods, extra);
        }

        // 2) 扫描 mods（先扫描以便选择基底）
        var mods = modsOverride ?? Mods.ScanMods(modsRoot, chapter);
        if (mods == null) mods = new List<ModEntry>();
        ApplyAdaptations(mods);   // mod 作者自报的适配程度 → 选择转换/部署通道

        // 依赖自动启用（B 依赖 A 时，A 自动跟着启用）
        try
        {
            var allMods = Mods.ScanMods(modsRoot, chapter, false, true);   // 含未启用的
            var autoRes = AutoEnable.Resolve(allMods, mods);
            AutoEnable.PrintReport(autoRes);
            if (autoRes.Added.Count > 0)
                Console.WriteLine(L("  （被自动启用的 mod 是因为别的 mod 依赖它）"));
        }
        catch (Exception ex) { Console.WriteLine(L("  [警告] 依赖分析失败: {0}", ex.Message)); }

        // 2.1) 章节注册表（仅主部署时生成，避免时间线部署递归）
        if (outDirOverride == null)
        {
            try
            {
                var allMods = Mods.ScanMods(modsRoot, chapter, includeDisabled: false, allChapters: true);
                LastAllMods = allMods;
                var registry = Chapters.BuildRegistry(allMods, chapter, gameRoot);
                LastRegistry = registry;
                Chapters.WriteRegistry(gameRoot, registry);
                InstallExternalChapters(allMods);   // Kristal 等项目：装进它自己引擎的 mods/
            }
            catch (Exception ex) { Console.WriteLine(L("[警告] 章节注册表生成失败: {0}", ex.Message)); }
        }
        // 2.1.5) 没变就跳过：把"本次会产出什么"的签名和上次写完时记下的签名对比。
        //        ★ 放在 2.1) 之后是必须的：跳过路径也要给平行时间线逐个过签名，而 DeployTimelines
        //          依赖 2.1) 赋值的 LastAllMods / LastRegistry（1.5 那个位置它们是空的，跳过就再也查不到时间线）。
        //        （--deploy 以前每次都完整重做一遍 229MB 的注入+写盘；内容没变时纯属白干。
        //          部署缓存只服务 --launch，这里补上 --deploy 自己的幂等判断。）
        //        ★ 平行时间线子产物（outDirOverride != null）过去被 --force 连带强制重建：
        //          每章 1 个主产物 + 3 个时间线 = 同一份注入工作做 4 遍，实测占 wall 的一半
        //          （chapter1：主 18.6s + 时间线 3×~10.5s ≈ 50s / wall 60s）。现在时间线也走内容签名，
        //          要连它们一起重建就显式加 --force-timelines。
        //        ⚠ 时间线的签名必须用「它自己的 mod 子集 + 基底 data 指纹」：沿用 srcChapter 的
        //          全量扫描会出现「时间线的 mod 变了、签名却没变」→ 产物永久过期。
        var sigFile = Path.Combine(Path.GetDirectoryName(win) ?? ".", ".ntl-deploy-" + chapter + ".sig");
        // 产物内容指纹（SHA-256 前 16 字节 + 字节数）：输入签名只描述"用什么料"，描述不了"碗里现在是什么"。
        //   输入没变、产物却被换掉（--restore-chapter 还原、手工覆盖、外部补丁、别的工具重写）时，
        //   光比输入签名会得出"内容未变"并静默跳过 —— 实测踩过：还原 chapter3/4/5 后再 --deploy-all，
        //   6 项各约 2.1s 全部跳过，章节其实是原版 data.win，游戏里既没有面板也没有字体补全。
        //   用内容哈希而不是 (长度, 写入时间)：NTFS 的目录项时间戳是惰性刷新的，刚写完读可能拿到旧值。
        string OutFp()
        {
            try
            {
                if (!File.Exists(win)) return "missing";
                using var fs = File.OpenRead(win);
                using var sha = System.Security.Cryptography.SHA256.Create();
                var h = sha.ComputeHash(fs);
                return Convert.ToHexString(h.AsSpan(0, 16)).ToLowerInvariant() + ":" + new FileInfo(win).Length;
            }
            catch { return "?"; }
        }
        var isTimelineProduct = outDirOverride != null;
        var skipAllowed = isTimelineProduct ? !ForceTimelines : (!ForceDeploy && modsOverride == null);
        if (skipAllowed && !NoCache && !Injector.FastDeploy)
        {
            try
            {
                var sig = DeploySignature();
                // .sig 两行：第 1 行输入签名，第 2 行产物内容指纹（旧版只有 1 行 ⇒ 产物状态未知，重建一次）
                var sigText = File.Exists(sigFile) ? File.ReadAllText(sigFile).Trim() : "";
                var sigLines = sigText.Split('\n');
                var recSig = sigLines.Length > 0 ? sigLines[0].Trim() : "";
                var recOut = sigLines.Length > 1 ? sigLines[1].Trim() : "";
                var inputSame = recSig == sig;
                var curOut = OutFp();
                var outSame = recOut.Length > 0 && recOut == curOut;
                var sigSame = inputSame && outSame;
                // 产物齐全性也要参与幂等判断：新增产物时部署签名不会变（签名只由 api 版本/mods/
                // 外部签名决定），于是「新产物当前不存在」会让跳过永久生效、产物永远不生成。
                var missing = isTimelineProduct ? MissingTimelineProduct(outDirOverride!) : MissingProducts(gameRoot, chapter);
                var sigLabel = isTimelineProduct ? win : chapter;
                if (!sigSame && SigDebug)
                {
                    Console.WriteLine(L("[签名调试] {0}: 记录 {1} / 本次 {2}", sigLabel,
                        recSig.Length > 0 ? recSig : "(无)", Cache.SigShort(sig)));
                    Console.WriteLine(L("[签名原文·检查] {0}: {1}", sigLabel, DeploySignature(true)));
                }
                if (sigSame && missing == null)
                {
                    Console.WriteLine(L("[跳过] {0} 内容未变（签名 {1}）—— 产物已是这个内容，无需重新部署", sigLabel, Cache.SigShort(sig)));
                    LastDeploySkipped = true;
                    // ★ 主产物跳过时，平行时间线仍要各自过一遍签名：否则「章节 mod 没动、时间线 mod 动了」
                    //   会被这次提前 return 连带跳过，时间线产物永远停在旧内容。
                    if (outDirOverride == null && !NoTimelines)
                    {
                        // ★ 守卫拦下 ≥1 条 ⇒ 退出码非零（t31 复核 F1）
                        if (DeployTimelines(gameRoot, chapter) > 0) return 1;
                    }
                    return 0;
                }
                if (sigSame)
                    Console.WriteLine(L("[重做] {0} 内容未变（签名 {1}），但产物缺失（{2}）—— 必须重新部署才能生成", sigLabel, Cache.SigShort(sig), missing));
                else if (inputSame && !outSame)
                    Console.WriteLine(L("[重做] {0} 输入未变（签名 {1}），但产物内容对不上（记录 {2} / 当前 {3}）—— 产物被还原或替换过，必须重新部署", sigLabel, Cache.SigShort(sig), recOut.Length > 0 ? recOut : "(无)", curOut));
            }
            catch (Exception ex) { if (SigDebug) Console.WriteLine(L("[签名调试] 异常: {0}", ex.Message)); }
        }

        // 2.1.2) 运行期目录：GM 的 file_text_* 对相对路径的解析根在**存档区**，
        //   而它**不会自动建目录**（目录不存在时写日志/写请求都静默失败，
        //   实测表现为"游戏跑得好好的却一个字都不写"）。这里由 C# 先把目录建好。
        try
        {
            var saveArea = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DELTARUNE");
            Directory.CreateDirectory(Path.Combine(saveArea, "Neutraled"));
        }
        catch (Exception ex) { Console.WriteLine(L("[警告] 创建运行期目录失败: {0}", ex.Message)); }

        // 2.15) 依赖校验（dependencies 声明的 mod 是否存在 / 是否启用）
        try
        {
            var allForDep = Mods.ScanMods(modsRoot, chapter, includeDisabled: true, allChapters: true);
            var knownIds = new HashSet<string>(allForDep.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
            foreach (var m in mods)
            {
                foreach (var rawDep in m.Dependencies)
                {
                    if (string.IsNullOrWhiteSpace(rawDep)) continue;

                    // 支持三种写法：
                    //   "other.mod"                   仅要求存在
                    //   "other.mod >= 1.0.0"          要求最低版本
                    //   "other.mod == 1.2.3"          要求精确版本
                    var dep = rawDep.Trim();
                    string? op = null, want = null;
                    foreach (var o in new[] { ">=", "<=", "==", ">", "<" })
                    {
                        var idx = dep.IndexOf(o, StringComparison.Ordinal);
                        if (idx > 0)
                        {
                            op = o;
                            want = dep.Substring(idx + o.Length).Trim();
                            dep = dep.Substring(0, idx).Trim();
                            break;
                        }
                    }

                    if (!knownIds.Contains(dep))
                    {
                        Console.WriteLine(L("[警告] {0} 依赖的 mod 不存在: {1}", m.Id, dep));
                        continue;
                    }
                    var target = allForDep.FirstOrDefault(x => string.Equals(x.Id, dep, StringComparison.OrdinalIgnoreCase));
                    if (target == null) continue;

                    if (!target.Enabled)
                    {
                        Console.WriteLine(L("[警告] {0} 依赖的 mod 未启用: {1}", m.Id, dep));
                        continue;
                    }

                    // 版本校验
                    if (op != null && want != null)
                    {
                        var cmp = CompareVersion(target.Version, want);
                        bool pass = op switch
                        {
                            ">=" => cmp >= 0,
                            "<=" => cmp <= 0,
                            ">" => cmp > 0,
                            "<" => cmp < 0,
                            "==" => cmp == 0,
                            _ => true
                        };
                        if (!pass)
                            Console.WriteLine(L("[警告] {0} 要求 {1} {2} {3}，但当前版本是 {4}", m.Id, dep, op, want, target.Version));
                        else
                            Console.WriteLine(L("  依赖 OK: {0} ← {1} {2} {3}（当前 {4}）", m.Id, dep, op, want, target.Version));
                    }
                }
            }
        }
        catch { }

        // 2.2) 依赖排序（load_after / load_before / 资源型基底优先）
        mods = Mods.SortByDependencies(mods);

        PhaseTimer.Mark("1/5 备份 + 扫描/排序 mods");
        // 2.5) 基底选择：外部指定 > mod 的 inherit 声明 > 原版备份
        var basePath = backup;
        if (!string.IsNullOrEmpty(baseWinOverride) && File.Exists(baseWinOverride))
        {
            basePath = baseWinOverride!;
            Console.WriteLine(L("[2/5] 基底: 引用的章节产物 {0}", baseWinOverride));
        }
        else
        {
            // ★ 顺序必须**确定**（按 id 排序）：否则"取第一个整包 mod"会随扫描顺序漂移，
            //   而缓存签名算不出这种漂移 → 命中错的产物（本机 6 个整包 mod，汉化包就永远轮不到）。
            var inheritAll = mods.Where(m =>
                string.Equals(m.RefAssets, "inherit", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(m.RefSource) &&
                File.Exists(Path.Combine(m.Dir, m.RefSource)))
                .OrderBy(m => m.Id, StringComparer.Ordinal).ToList();

            // ★ --base-mod 没给时读 Neutraled/config.json 的 base_mod：
            //   这样"启动 Neutraled.bat"（内部走 --launch，不带参数）也能用对基底。
            string? effectiveBase = BaseModId;
            if (string.IsNullOrEmpty(effectiveBase))
            {
                // ★ 走 ConfigFile：裸 JsonNode.Parse 在「config.json 有重复键」时不会立刻抛，
                //   而是在访问 co["base_mod"] 时才从 JsonObject.InitializeDictionary 抛（同样会被这里吃掉，
                //   但换用统一入口还能顺带触发重复键自愈）。
                var bm = ConfigFile.GetString(gameRoot, "base_mod");
                if (!string.IsNullOrEmpty(bm)) { effectiveBase = bm; Console.WriteLine(L("[2/5] 基底取自 config.json base_mod: {0}", bm)); }
            }

            ModEntry? inheritMod = null;
            if (!string.IsNullOrEmpty(effectiveBase))
            {
                inheritMod = inheritAll.FirstOrDefault(m =>
                    string.Equals(m.Id, effectiveBase, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(m.Name, effectiveBase, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileName(Path.GetDirectoryName(m.Dir) ?? ""), effectiveBase, StringComparison.OrdinalIgnoreCase));
                if (inheritMod == null)
                    Console.WriteLine(L("[2/5] [警告] --base-mod {0} 没匹配到整包 mod，回退默认选择", effectiveBase));
                else
                    Console.WriteLine(L("[2/5] 基底由 --base-mod/config 指定: {0}", inheritMod.Id));
            }
            inheritMod ??= inheritAll.FirstOrDefault();

            if (inheritAll.Count > 1)
            {
                Console.WriteLine(L("[2/5] [警告] {0} 个 mod 提供整包 data.win，本章只能用 1 个基底：", inheritAll.Count));
                foreach (var m in inheritAll)
                {
                    // 状态标记先取出来：_check-wrap.mjs 的词法器会把整个 $"..." 当成一个 token，
                    // 洞里的内层调用会被误判成「首参不是字符串字面量」→ 提到洞外。
                    var mark = ReferenceEquals(m, inheritMod) ? L("✔ 生效") : L("✘ 被忽略");
                    Console.WriteLine($"        {mark}  {m.Id}");
                }
                Console.WriteLine(L("        → 想换基底：--base-mod <id>；其余 mod 的脚本/资源/files 覆盖仍会生效"));
            }
            if (inheritMod != null)
            {
                basePath = Path.Combine(inheritMod.Dir, inheritMod.RefSource!);
                Console.WriteLine(L("[2/5] 基底: {0} 的 {1}（资源型基底）", inheritMod.Id, inheritMod.RefSource));
            }
            else
            {
                Console.WriteLine(L("[2/5] 基底: 原版备份"));
            }
        }
        var data = Injector.Load(basePath);
        PhaseTimer.Mark("2/5 选基底 + 加载基底 data.win");

        // 独立存档：GM 的 game_save_id = %LOCALAPPDATA%\<GeneralInfo.FileName>\
        // 平行时间线/引用章改掉文件名即可获得独立存档目录，互不污染
        if (!string.IsNullOrEmpty(saveNameOverride) && !DisableSaveRename)
        {
            var oldName = data.GeneralInfo.FileName?.Content ?? "(none)";
            // GM 的 game_save_id 由多个字段共同决定，实测 FileName 单独改无效
            // → 同时改 FileName / Name（两者都指向 %LOCALAPPDATA%\<值>\）
            var sNew = new UndertaleModLib.Models.UndertaleString(saveNameOverride!);
            data.Strings.Add(sNew);
            data.GeneralInfo.FileName = sNew;

            var sNew2 = new UndertaleModLib.Models.UndertaleString(saveNameOverride!);
            data.Strings.Add(sNew2);
            data.GeneralInfo.Name = sNew2;

            Console.WriteLine(L("[存档] 独立存档名: {0} -> {1}（FileName + Name）", oldName, saveNameOverride));
        }

        Console.WriteLine(L("[3/5] mods: {0} 个（含基底 mod）", mods.Count));
        foreach (var m in mods) Console.WriteLine($"   - {m.Id} ({m.Name}) v{m.Version}");

        // 3.05) mod 权限分析（让玩家知道装了什么风险的东西）
        try
        {
            var perms = Permissions.Analyze(mods);
            Permissions.PrintReport(perms);
            Permissions.WriteJson(perms, Path.Combine(neutraled, "permissions.json"));
        }
        catch (Exception ex) { Console.WriteLine(L("  [警告] 权限分析失败: {0}", ex.Message)); }

        // 3.1) 存档保护：部署前快照（mod 出问题时玩家能回到之前的状态）
        SaveGuard.SnapshotBeforeDeploy(gameRoot);

        // 3.2) mod 冲突分析（友好报告 + GUI 可读的 JSON）
        try
        {
            var conflictReport = Conflicts.Analyze(mods);
            Conflicts.PrintReport(conflictReport);
            Conflicts.WriteJson(conflictReport, Path.Combine(neutraled, "conflicts.json"));
        }
        catch (Exception ex) { Console.WriteLine(L("  [警告] 冲突分析失败: {0}", ex.Message)); }

        // 3.5) 若使用非原版基底：计算"基底相对原版的改动集合"（供冲突裁决）
        HashSet<string>? baseModified = null;
        if (!string.Equals(basePath, backup, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(L("[3.5/5] 计算基底改动集合（冲突裁决用）..."));
            // ★ 结果只取决于「基底文件 + 官方备份文件」这两份内容 → 按它们的指纹缓存。
            //   以前每次都重新 Load 一遍官方备份（chapter5 那是 200MB+，实测 2.9s）。
            var diffKey = FileFp(basePath) + "|" + FileFp(backup);
            var diffCache = Path.Combine(Paths.NeutraledRoot(gameRoot), "cache",
                "basediff-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(diffKey)))[..16] + ".json");
            List<string>? names = null;
            if (!NoCache) { try { if (File.Exists(diffCache)) names = System.Text.Json.JsonSerializer.Deserialize<List<string>>(File.ReadAllText(diffCache), Paths.Json); } catch { } }
            if (names != null)
            {
                baseModified = new HashSet<string>(names, StringComparer.Ordinal);
                Console.WriteLine(L("  基底改动对象: {0} 个（命中缓存）", baseModified.Count));
            }
            else
            {
                var baselineData = Injector.Load(backup);
                baseModified = PreWriteRepairs.DiffCodeObjects(baselineData, data);
                Console.WriteLine(L("  基底改动对象: {0} 个", baseModified.Count));
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(diffCache)!);
                    File.WriteAllText(diffCache, System.Text.Json.JsonSerializer.Serialize(baseModified.ToList(), Paths.Json));
                }
                catch { }
            }
        }

        PhaseTimer.Mark("3.5/5 计算基底改动集合");
        // 4) 注入
        Console.WriteLine(L("[4/5] 注入核心..."));
        Injector.InjectCore(data, apiDir, mods, BootCodeName(chapter), baseModified, gameRoot, chapter);

        PhaseTimer.Mark("注入核心收尾（3-7 段）");
        // 4.4b) 应用 mod 的 files/（外部文件覆盖，如 lang json / vid）
        var filesBackupRoot = Path.Combine(neutraled, ".backup_files", chapter);
        int fileOverrides = 0;
        var chapterDir = outDirOverride ?? Paths.ChapterDir(gameRoot, chapter);
        foreach (var m in mods)
        {
            var filesDir = Path.Combine(m.Dir, "files");
            if (!Directory.Exists(filesDir)) continue;
            foreach (var f in Directory.GetFiles(filesDir, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(filesDir, f);
                var target = Path.Combine(chapterDir, rel);
                if (File.Exists(target))
                {
                    var bak = Path.Combine(filesBackupRoot, rel);
                    if (!File.Exists(bak))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(bak)!);
                        File.Copy(target, bak, true);
                    }
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(f, target, true);
                fileOverrides++;
            }
        }
        if (fileOverrides > 0) Console.WriteLine(L("  外部文件覆盖: {0} 个（已备份到 .backup_files/{1}）", fileOverrides, chapter));

        PhaseTimer.Mark("4.4b 应用 mod 的 files/");
        // 4.5) 写盘前修复（三道必修）
        Console.WriteLine(L("[4.5/5] 写盘前修复..."));
        PreWriteRepairs.DedupeCodeLocals(data);
        PreWriteRepairs.RebuildDeadFunctionReferences(data);
        if (Injector.LastRefSources.Count > 0)
            PreWriteRepairs.RepairMissingChildFunctions(data, Injector.LastRefSources);

        PhaseTimer.Mark("4.5/5 写盘前修复");
        // 5) 写盘
        Console.WriteLine(L("[5/5] 写入 data.win..."));
        Injector.Save(data, win);

        // 运行时清单（供 GUI 显示）
        var listPath = Path.Combine(chapterDir, "Neutraled", "mods.json");
        Directory.CreateDirectory(Path.GetDirectoryName(listPath)!);
        var json = System.Text.Json.JsonSerializer.Serialize(mods.Select(m => new { m.Id, m.Name, m.Version }),
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(listPath, json);

        // 产物作用域清单：运行期 api/ntl_product_scope.gml 读 <working_directory>Neutraled/scope.json
        //   拿本产物的目标名，用于 mod 脚本作用域（ntl_mod_scripts_load）与 root/章节判定（ntl_is_root）。
        //   ⚠ 这里写的 chapter 就是本产物的目标：root / chapterN / 独立章（时间线传的 srcChapter）。
        //   以前运行期只能靠 working_directory 里猜 + config.auto_chapter 兜底，时间线产物恒猜错。
        var scopePath = Path.Combine(Path.GetDirectoryName(listPath)!, "scope.json");
        File.WriteAllText(scopePath, System.Text.Json.JsonSerializer.Serialize(
            new { version = 1, target = chapter },
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

        PhaseTimer.Mark("5/5 写入 data.win + 清单");
        PhaseTimer.Summary(chapter);
        Console.WriteLine(L("部署完成 ✓  {0}  ({1} MB)", win, new FileInfo(win).Length / 1024 / 1024));
        try
        {
            File.WriteAllText(sigFile, DeploySignature() + "\n" + OutFp());
            if (SigDebug) Console.WriteLine(L("[签名原文·写回] {0}: {1}", chapter, DeploySignature(true)));
        }
        catch { }

        // 5.5) API 注册表（IDE 自动补全 / 依赖解析 / 运行时命名空间调用）
        // 顶层 data.win 资源很少（443 函数），完整版要用章节产物生成才是完整集合；
        // 但「运行时精简注册表」只含 mods 段、与章节 data 无关，root 部署也必须写，
        // 否则全新仓库先部署 root 时 ntl_ns_load 无文件可读、部署后自检 (e) 会失败。
        if (outDirOverride == null)
        {
            var allMods = LastAllMods.Count > 0 ? LastAllMods : mods;
            var isRoot = chapter.Equals("root", StringComparison.OrdinalIgnoreCase);
            try { ApiRegistry.Write(gameRoot, isRoot ? (UndertaleModLib.UndertaleData?)null : data, allMods); }
            catch (Exception ex) { Console.WriteLine(L("[警告] API 注册表生成失败: {0}", ex.Message)); }

            if (!isRoot)
            {
                // hook 注册表（运行时 ntl_hook_init 读取）
                try { Hooks.WriteRegistry(gameRoot, Hooks.Collect(allMods)); }
                catch (Exception ex) { Console.WriteLine(L("[警告] hook 注册表生成失败: {0}", ex.Message)); }
            }
        }

        // ---- 部署后自检：把"静默失败"变成"响亮报错" ----
        // 小产物（< 50MB，root/chapter1 这类）自动跑，只要 0.1~0.2 秒；
        // 大产物要重新打开 229MB 的 data.win（约 8~9 秒），所以默认只在 --self-check 时跑。
        try
        {
            long prodSize = File.Exists(win) ? new FileInfo(win).Length : 0;
            if (SelfCheckOn || (prodSize > 0 && prodSize < 50L * 1024 * 1024))
            {
                if (DeploySelfCheck.Run(gameRoot, chapter, BootCodeName(chapter)) != 0)
                {
                    Console.WriteLine(L("  [错误] 部署后自检未通过 —— 产物可能不可用，已中止（不会静默放过）"));
                    return 1;
                }
            }
            else
            {
                Console.WriteLine(L("  [提示] 产物 {0} MB，跳过自动自检（要检查请加 --self-check，约 +9 秒）", prodSize / 1024 / 1024));
            }
        }
        catch (Exception ex) { Console.WriteLine(L("  [警告] 部署后自检异常（不影响部署）: {0}", ex.Message)); }

        // 6) 平行时间线章节：为每个 ~Chapter:N 声明生成独立产物目录
        //   ★ 返回值 = 被语言档守卫拦下的条数（t31 复核 F1）：过去丢弃它 ⇒ 拦下了也 exit 0。
        //     先记下、等清理与插件回调做完再决定退出码，避免「被拦下就不清理」。
        var tlGuardBlocked = 0;
        if (outDirOverride == null && !NoTimelines) tlGuardBlocked = DeployTimelines(gameRoot, chapter);

        // 部署后顺手清理 builder 自己产生的临时文件
        Cleanup.Run(gameRoot);
        PluginFire(PluginHooks.AfterDeploy, new { chapter });
        return tlGuardBlocked > 0 ? 1 : 0;
    }

    /// <summary>部署时应当生成的产物清单；返回第一个缺失项（都齐则 null）。
    /// 为什么要检查：部署签名只由 api 版本 / mods / 外部签名决定，新增产物不会改变它，
    /// 因此「新产物当前不存在」会让幂等跳过永久生效、产物永远不生成。</summary>
    private static string? MissingProducts(string gameRoot, string chapter)
    {
        var expected = new List<string> { "chapters.json", "ns-registry.json" };
        if (!chapter.Equals("root", StringComparison.OrdinalIgnoreCase))
        {
            expected.Add("hook-registry.json");
            expected.Add("api-registry.json");
        }
        var root = Paths.NeutraledRoot(gameRoot);
        foreach (var p in expected)
        {
            var full = Path.Combine(root, p);
            if (!File.Exists(full)) return p + L("（不存在）");
            // 0 字节等同于缺失（写盘中断会留下空文件）
            if (new FileInfo(full).Length == 0) return p + L("（0 字节）");
        }
        return null;
    }

    /// <summary>时间线子产物自己的必备文件（chapters.json / api-registry.json 那套是主部署写的，与它无关）。
    /// 用途同 MissingProducts：签名一致但产物被删掉时，必须重做而不是永久跳过。</summary>
    private static string? MissingTimelineProduct(string outDir)
    {
        foreach (var p in new[] { "data.win", Path.Combine("Neutraled", "mods.json"), Path.Combine("Neutraled", "scope.json") })
        {
            var full = Path.Combine(outDir, p);
            if (!File.Exists(full)) return p + L("（不存在）");
            if (new FileInfo(full).Length == 0) return p + L("（0 字节）");
        }
        return null;
    }

    /// <summary>部署所有平行时间线章节（每个 ~Chapter:N:name 一个独立目录）。</summary>
    private static int DeployTimelines(string gameRoot, string currentChapter)
    {
        // 两类都要部署：纯时间线 + 基于时间线的引用章（后者基底用前者产物，故排后面）
        var timelines = LastRegistry
            .Where(e => e.Kind == "timeline" || !string.IsNullOrEmpty(e.BaseDir))
            .OrderBy(e => string.IsNullOrEmpty(e.BaseDir) ? 0 : 1)
            .ToList();
        if (timelines.Count == 0) return 0;

        Console.WriteLine(L("===== 平行时间线 / 引用章: {0} 个 =====", timelines.Count));

        // 章节目录的上一级需要有 mus/（游戏的 launcher 分支按 working_directory + "../mus/" 找音频）
        var chaptersRoot = Path.Combine(Paths.NeutraledRoot(gameRoot), "chapters");
        Directory.CreateDirectory(chaptersRoot);
        var musLink = Path.Combine(chaptersRoot, "mus");
        var musReal = Path.Combine(gameRoot, "mus");
        if (Directory.Exists(musReal) && !Directory.Exists(musLink))
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{musLink}\" \"{musReal}\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using var mp = System.Diagnostics.Process.Start(psi)!;
                mp.WaitForExit();
                Console.WriteLine(mp.ExitCode == 0
                    ? L("  音频目录联接: {0} -> {1}", musLink, musReal)
                    : L("  [警告] 音频目录联接失败"));
            }
            catch (Exception ex) { Console.WriteLine(L("  [警告] 音频目录处理失败: {0}", ex.Message)); }
        }
        int ok = 0;                     // 成功部署的产物数（沿用原语义：既有日志行逐字不变）
        int blocked = 0;                // ★ 被语言档守卫拦下的产物数 —— 它就是返回值（调用方据此判退出码）
        foreach (var e in timelines)
        {
            var modsFor = LastAllMods
                .Where(m => e.Mods.Any(id => string.Equals(id, m.Id, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (modsFor.Count == 0)
            {
                Console.WriteLine(L("  [跳过] {0}: 找不到声明的 mod（{1}）", e.Id, string.Join(", ", e.Mods)));
                continue;
            }

            // 基底章节：优先用声明者自身的章节（如 mod 属于 chapter4）；否则用序号对应的官方章节
            var owner = modsFor.FirstOrDefault(m => !string.IsNullOrEmpty(m.Chapter) &&
                                                     !m.Chapter!.Equals("root", StringComparison.OrdinalIgnoreCase));
            var baseOrder = e.Order <= Chapters.OfficialCount ? e.Order : Chapters.OfficialCount;
            var srcChapter = owner?.Chapter ?? ("chapter" + baseOrder);
            if (!File.Exists(Paths.BackupDataWin(gameRoot, srcChapter)) &&
                !File.Exists(Paths.ChapterDataWin(gameRoot, srcChapter)))
            {
                Console.WriteLine(L("  [跳过] {0}: 基底章节不可用 {1}", e.Id, srcChapter));
                continue;
            }

            // 平行时间线的 data 来源（按语义，绝不用官方章节当基底）：
            //   1) 引用其它章节（Chapter:N:name 的产物）
            //   2) mod 自带 data：mods/<mod名>/<作者>/data/<章节名>/data.win
            string? baseWinOverride = null;
            if (!string.IsNullOrEmpty(e.BaseDir))
            {
                baseWinOverride = Path.Combine(gameRoot,
                    e.BaseDir.Replace('/', Path.DirectorySeparatorChar), "data.win");
                if (!File.Exists(baseWinOverride))
                {
                    Console.WriteLine(L("  [跳过] {0}: 被引用章节产物不存在 {1}", e.Id, baseWinOverride));
                    continue;
                }
            }
            else if (!string.IsNullOrEmpty(e.OwnData))
            {
                baseWinOverride = e.OwnData;
                Console.WriteLine(L("  基底: 自带 data -> {0}", e.OwnData));
            }
            else
            {
                Console.WriteLine(L("  [错误] {0}「{1}」: 平行时间线缺少自带 data", e.Id, e.Name));
                Console.WriteLine(L("         应放在 mods/<mod名>/<作者>/data/{0}/data.win（每个时间线一份）", e.Name));
                continue;
            }

            // ★ 运行时文件来源 = data.win 的**血统章节**，不是 mod.json 声明的章节。
            //   实测（2026-09-30 真机 Code Error）：ntl_timeline_8_…_lab / ntl_timeline_4_…_forest 的产物 data.win 是
            //   第 1 章血统（obj_initializer2 的 Create 用 scr_84_get_lang_string("obj_initializer2_slash_Create_0_gml_2_0")），
            //   而 mod.json 声明 chapter4 → 复制过去的 lang_en.json 里没有那个键 → 启动第一屏 Code Error。
            var runtimeChapter = TimelineRuntime.ResolveRuntimeChapter(gameRoot, baseWinOverride!, srcChapter, out _, out _);
            var cov = TimelineRuntime.CheckCoverage(gameRoot, baseWinOverride!, runtimeChapter);
            if (!cov.Ok)
            {
                TimelineRuntime.PrintGuardFailure(e.Id, e.Name, baseWinOverride!, runtimeChapter, cov);
                blocked++;     // ★ 被守卫拦下 ⇒ 记数（t31 复核 F1）
                continue;      // ★ 中止该产物部署（不计入 ok）
            }

            var outDir = Path.Combine(gameRoot, e.Dir.Replace('/', Path.DirectorySeparatorChar));
            var baseLabel = string.IsNullOrEmpty(e.BaseDir) ? L("自带data") : e.BaseDir;
            var runtimeLabel = runtimeChapter.Equals(srcChapter, StringComparison.OrdinalIgnoreCase)
                ? runtimeChapter
                : L("{0}（血统判定；mod.json 声明的是 {1}）", runtimeChapter, srcChapter);
            Console.WriteLine(L("--- {0}  「{1}」 <- {2}（运行时文件取自 {3}）+ {4} 个 mod ---", e.Id, e.Name, baseLabel, runtimeLabel, modsFor.Count));
            try
            {
                // 独立存档名：保持简短（过长的名字可能导致游戏启动异常）
                // 注意：必须用**确定性**哈希 —— string.GetHashCode() 在每个进程都不同，
                // 会导致每次部署生成新的存档目录，玩家的进度"消失"
                var saveName = $"DRTL{e.Order}_{StableHash(e.Id)}";

                if (Deploy(gameRoot, srcChapter, outDir, modsFor, baseWinOverride, saveName, runtimeChapter) == 0)
                {
                    ok++;
                    // 把存档实际落到游戏目录（C 盘空间紧张 + 便于随游戏一起备份）
                    LinkSaveFolder(gameRoot, saveName);
                }
            }
            catch (Exception ex) { Console.WriteLine(L("  [错误] {0}: {1}", e.Id, ex.Message)); }
        }
        Console.WriteLine(L("===== 时间线部署完成: {0}/{1} =====", ok, timelines.Count));
        if (blocked > 0)
            Console.WriteLine(L("  [错误] {0} 条产物被语言档守卫拦下 —— 本次部署不能算成功（退出码非零）", blocked));

        // 产物已生成 → 重新生成注册表（Enabled 判定基于产物存在性）
        try
        {
            var registry = Chapters.BuildRegistry(LastAllMods, currentChapter, gameRoot);
            Chapters.WriteRegistry(gameRoot, registry);
        }
        catch { }
        return blocked;   // ★ 返回值 = 被守卫拦下的条数（0 = 全部通过），调用方据此判退出码
    }

    /// <summary>把章节的存档目录做成目录联接（junction）：
    ///   游戏看到的 %LOCALAPPDATA%\&lt;saveName&gt;\ → 实际存储在 &lt;游戏根&gt;/Neutraled/saves/&lt;saveName&gt;/
    /// 这样存档数据留在游戏目录（E 盘），且可随游戏一起备份。</summary>
    private static void LinkSaveFolder(string gameRoot, string saveName)
    {
        try
        {
            var realDir = Path.Combine(Paths.NeutraledRoot(gameRoot), "saves", saveName);
            Directory.CreateDirectory(realDir);

            // 新章节的存档目录需要游戏的配置文件（dr.ini / config_*.ini / difficulty.ini），
            // 否则游戏的菜单/存档界面会渲染异常（黑屏只有红心）
            if (!saveName.Equals("DELTARUNE", StringComparison.OrdinalIgnoreCase))
            {
                var officialDir = Path.Combine(Paths.NeutraledRoot(gameRoot), "saves", "DELTARUNE");
                if (Directory.Exists(officialDir))
                {
                    int copiedCfg = 0;
                    foreach (var f in Directory.GetFiles(officialDir))
                    {
                        var fn = Path.GetFileName(f);
                        // 配置 + 存档都要（缺存档会让游戏走"新游戏"流程并卡住）
                        // 只复制配置：平行章节按"新游戏"启动，不该继承官方存档
                        var keep = fn.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) ||
                                   fn.EndsWith(".vdf", StringComparison.OrdinalIgnoreCase);
                        if (!keep) continue;
                        var dst = Path.Combine(realDir, fn);
                        if (!File.Exists(dst)) { File.Copy(f, dst, true); copiedCfg++; }
                    }
                    if (copiedCfg > 0) Console.WriteLine(L("    复制配置+存档: {0} 个", copiedCfg));
                }
            }

            var localDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), saveName);

            if (Directory.Exists(localDir))
            {
                var attrs = File.GetAttributes(localDir);
                if ((attrs & FileAttributes.ReparsePoint) != 0)
                {
                    Console.WriteLine(L("  存档联接已存在: {0} -> {1}", localDir, realDir));
                    return;
                }
                // 真实目录：把内容搬过去再替换成联接
                foreach (var f in Directory.GetFiles(localDir))
                {
                    var dst = Path.Combine(realDir, Path.GetFileName(f));
                    if (!File.Exists(dst)) File.Copy(f, dst, true);
                }
                Directory.Delete(localDir, true);
            }

            var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{localDir}\" \"{realDir}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = System.Diagnostics.Process.Start(psi)!;
            var outp = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode == 0)
                Console.WriteLine(L("  存档目录联接: {0} -> {1}", localDir, realDir));
            else
                Console.WriteLine(L("[警告] 存档目录联接失败: {0}", outp.Trim()));
        }
        catch (Exception ex) { Console.WriteLine(L("[警告] 存档目录处理失败: {0}", ex.Message)); }
    }

    /// <summary>把存档目录搬到游戏目录（用 junction 联接，数据不丢、可逆）。
    ///   official = 只处理 %LOCALAPPDATA%\DELTARUNE
    ///   all      = 处理所有 DELTARUNE* 存档目录</summary>
    private static int LinkSaves(string gameRoot, string which)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var names = new List<string>();

        if (which.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            names.AddRange(Directory.GetDirectories(local)
                .Select(Path.GetFileName)
                .Where(n => n != null && n.StartsWith("DELTARUNE", StringComparison.OrdinalIgnoreCase))!);
        }
        else
        {
            names.Add("DELTARUNE");
        }

        if (names.Count == 0) { Console.WriteLine(L("没有找到存档目录")); return 1; }

        Console.WriteLine(L("===== 把 {0} 个存档目录搬到游戏目录 =====", names.Count));
        foreach (var n in names) LinkSaveFolder(gameRoot, n!);
        Console.WriteLine(L("存档实际位置: {0}", Path.Combine(Paths.NeutraledRoot(gameRoot), "saves")));
        return 0;
    }

    /// <summary>导出全部代码条目为文本（便于用 ripgrep/grep 做全局搜索）。</summary>
    private static int DumpAll(string gameRoot, string chapter, string outDir)
    {
        var data = Injector.Load(Paths.ChapterDataWin(gameRoot, chapter));
        Directory.CreateDirectory(outDir);
        var gctx = new UndertaleModLib.Decompiler.GlobalDecompileContext(data);
        int n = 0;
        foreach (var code in data.Code)
        {
            var name = code.Name?.Content;
            if (string.IsNullOrEmpty(name)) continue;
            try
            {
                var dctx = new Underanalyzer.Decompiler.DecompileContext(gctx, code, null!);
                var txt = dctx.DecompileToString();
                var safe = name.Replace('/', '_').Replace('\\', '_');
                File.WriteAllText(Path.Combine(outDir, safe + ".gml"), txt);
                n++;
            }
            catch { }
        }
        Console.WriteLine(L("导出 {0} 个代码条目 -> {1}", n, outDir));
        return 0;
    }

    /// <summary>--import-kristal-map &lt;地图 .tmx 或目录&gt; [--map-id 名字] [--map-out 输出目录]
    /// 把 Kristal/Tiled 地图转换成「整图 PNG + 地图数据 JSON」。</summary>
    static int ImportKristalMap(string gameRoot, string path, string mapId, string mapOut)
    {
        try
        {
            var outDir = string.IsNullOrEmpty(mapOut)
                ? Path.Combine(Paths.NeutraledRoot(gameRoot), "kristal-maps")
                : mapOut;

            var files = new List<string>();
            if (Directory.Exists(path))
                files.AddRange(Directory.GetFiles(path, "*.tmx", SearchOption.AllDirectories));
            else if (File.Exists(path))
                files.Add(path);
            else { Console.WriteLine(L("[错误] 找不到: {0}", path)); return 1; }

            if (files.Count == 0) { Console.WriteLine(L("[错误] 没有找到 .tmx 地图")); return 1; }

            Console.WriteLine(L("===== 转换 Kristal 地图: {0} 张 =====", files.Count));
            int ok = 0, fail = 0;
            foreach (var f in files)
            {
                var id = files.Count == 1 && !string.IsNullOrEmpty(mapId)
                    ? mapId : Path.GetFileNameWithoutExtension(f);
                try
                {
                    var (png, json, map) = KristalMap.Convert(f, outDir, id);
                    var pngInfo = new FileInfo(png);
                    Console.WriteLine(L("  [OK] {0}: {1}x{2} 格 ", id, map.Width, map.Height) +
                                      $"({map.Width * map.TileWidth}x{map.Height * map.TileHeight} px) " +
                                      L("图层 {0} / 对象组 {1} ", map.TileLayers.Count, map.ObjectGroups.Count) +
                                      $"-> {pngInfo.Length / 1024} KB");
                    ok++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(L("  [失败] {0}: {1}", Path.GetFileName(f), ex.Message));
                    fail++;
                }
            }
            Console.WriteLine(L("===== 完成: {0} 成功 / {1} 失败 =====", ok, fail));
            Console.WriteLine(L("输出目录: {0}", outDir));
            return fail == 0 ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.WriteLine(L("[错误] {0}", ex.Message));
            return 1;
        }
    }

    /// <summary>--cache-check [chapter]：**只报告**当前状态与缓存的关系（不应用、不启动游戏）。
    /// 用来回答"这次启动会不会直接命中缓存"。</summary>
    static int CacheCheck(string gameRoot, string chapter)
    {
        chapter = string.IsNullOrEmpty(chapter) ? "root" : chapter;
        var mods = Mods.ScanMods(Path.Combine(Paths.NeutraledRoot(gameRoot), "mods"), chapter, false, true);
        var gameVer = Cache.GameVersion(gameRoot);
        var sig = Cache.Signature(gameVer, chapter, mods, ExternalSig(gameRoot));
        var hit = Cache.Lookup(gameRoot, sig);
        Console.WriteLine(L("===== 启动缓存查验（不启动游戏）====="));
        Console.WriteLine(L("  章节        : {0}", chapter));
        Console.WriteLine(L("  启用 mod    : {0} 个", mods.Count));
        Console.WriteLine(L("  状态签名    : {0}  (完整 {1} 字符)", Cache.SigShort(sig), sig.Length));
        Console.WriteLine(L("  api 指纹    : {0}", Cache.SigShort(Cache.ApiFingerprint())));
        Console.WriteLine(L("  游戏版本    : {0}", gameVer));
        Console.WriteLine(hit != null
            ? L("  ✅ 命中缓存  : {0} MB，用过 {1} 次，建于 {2}  → 启动会硬链接秒开", hit.Size / 1024 / 1024, hit.Hits, hit.Created)
            : L("  ⏳ 无缓存    : 这次启动会真正部署一遍，然后把结果存进缓存"));
        var idx = Cache.LoadIndex(gameRoot);
        Console.WriteLine(L("  缓存条目    : {0} 个，上限 {1} MB", idx.Count, Cache.MaxBytes(gameRoot) / 1024 / 1024));
        return 0;
    }

    /// <summary>--cache-list 列出缓存。</summary>
    static int CacheList(string gameRoot)
    {
        var idx = Cache.LoadIndex(gameRoot);
        var max = Cache.MaxBytes(gameRoot);
        long total = 0;
        Console.WriteLine(L("===== 部署缓存（上限 {0} MB）=====", max / 1024 / 1024));
        foreach (var e in idx.Values.OrderByDescending(x => x.LastUsed))
        {
            total += e.Size;
            var mods = string.Join(", ", e.Mods.Select(kv => $"{kv.Key}@{kv.Value}"));
            Console.WriteLine($"  {(e.Sig.Length >= 16 ? e.Sig[..16] : e.Sig)}  " +
                              L("{0,6} MB  命中 {1} 次  最后使用 {2}  ", e.Size / 1024 / 1024, e.Hits, e.LastUsed) +
                              L("目标 [{0}]", string.Join("/", e.Targets)));
            Console.WriteLine($"      mod: {mods}");
        }
        Console.WriteLine(L("===== 共 {0} 份，占用 {1} MB / {2} MB =====", idx.Count, total / 1024 / 1024, max / 1024 / 1024));
        return 0;
    }

    /// <summary>--cache-clear 清空缓存。</summary>
    static int CacheClear(string gameRoot)
    {
        var idx = Cache.LoadIndex(gameRoot);
        int n = 0;
        foreach (var sig in idx.Keys.ToList())
        {
            var dir = Path.Combine(Paths.NeutraledRoot(gameRoot), "cache", Cache.SigShort(sig));
            try { if (Directory.Exists(dir)) { Directory.Delete(dir, true); n++; } } catch { }
        }
        Cache.SaveIndex(gameRoot, new Dictionary<string, Cache.Entry>());
        Console.WriteLine(L("缓存已清空: 删除 {0} 份", n));
        return 0;
    }

    /// <summary>--cache-applied 报告当前配置是否已有缓存（GUI 用）。</summary>
    static int CacheApplied(string gameRoot, string chapter)
    {
        var mods = Mods.ScanMods(Path.Combine(Paths.NeutraledRoot(gameRoot), "mods"), chapter, false, true);
        var gameVer = Cache.GameVersion(gameRoot);
        var sig = Cache.Signature(gameVer, chapter, mods, ExternalSig(gameRoot));
        var hit = Cache.Lookup(gameRoot, sig);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            signature = sig,
            shortSig = Cache.SigShort(sig),
            hit = hit != null,
            sizeMB = hit?.Size / 1024 / 1024 ?? 0,
            hits = hit?.Hits ?? 0
        }));
        return 0;
    }

    /// <summary>玩家显式要求走完整部署档（即使所有 mod 都声明兼容快速档）。</summary>
    static bool FullDeployForced = false;

    /// <summary>部署写盘后跑一次结构化自检（--self-check 强制；小产物自动跑）。</summary>
    static bool SelfCheckOn = false;

    /// <summary>--force：即使签名一致也重新部署。</summary>
    static bool ForceDeploy = false;

    /// <summary>--force-timelines：连平行时间线子产物也强制重建。
    /// 默认时间线跟随内容签名（自己的 mod 子集 + 基底 data 指纹），内容没变就跳过。
    /// 例：--deploy-all --force 只重建 6 个主产物，18 个时间线产物按需重建。</summary>
    static bool ForceTimelines = false;

    /// <summary>--no-timelines：本进程不部署平行时间线（只给 --deploy-all 的 worker 用）。
    /// 时间线产物是"每章 1 个主产物 + 3 个时间线"，6 个 worker 各跑一遍 = 6 路并发抢同一个
    /// 产物目录（同时重写 data.win、互相覆盖 .ntl-deploy-*.sig），既慢又可能写坏 —— 交给父进程做一次。</summary>
    static bool NoTimelines = false;

    /// <summary>NTL_SIG_DEBUG=1：打印部署签名原文（检查时 / 写回时各一次）。
    /// 用来定位「签名每轮都变 ⇒ 内容没变也重建」是哪个输入项在动（把两行原文一比即可）。</summary>
    static readonly bool SigDebug = Environment.GetEnvironmentVariable("NTL_SIG_DEBUG") == "1";

    /// <summary>--no-cache：**只禁读缓存**（扫描缓存 / 输入扫描缓存 / 基底改动缓存 / 转换幂等清单 / 部署幂等签名）。
    /// 用于"真实冷启动"计时：并行跑多个章节时，先跑的章节写下的缓存不会被后面的章节读到。
    /// 不在用户显式破坏的前提下，缓存仍会正常写入。</summary>
    static bool NoCache = false;

    /// <summary>--add-external 用：外部章节显示名与启动参数。</summary>
    static string extName = "";
    static string extArgs = "";

    /// <summary>并行度（--deploy-all / --import-all 用；默认 min(4, 核数/2)）。</summary>
    static int Jobs = 0;

    /// <summary>--make-cjk-font：从系统 TTF 生成中文字体资源包。</summary>
    static bool MakeCjkFont = false;
    static string CjkTtf = "";
    static int CjkSize = 12;
    static string CjkCharset = "cjk";
    /// <summary>--chars：配合 --charset list 用的显式字表（只渲这几个字），补空白格用。</summary>
    static string CjkChars = "";
    static string CjkName = "ntl_font_cjk";
    static string CjkFromWin = "";
    static string CjkSourceFont = "fnt_main";
    /// <summary>--font-native 的强制来源种类（game / base-mod；不给则按 fonts/ntl_native_sources.json 的优先级）。</summary>
    static string? FontSource = null;

    /// <summary>--launch &lt;chapter&gt;：查缓存→命中则硬链接应用（秒开）→ 否则部署→ 存缓存 → 由 Steam 拉起游戏。</summary>
    static int LaunchWithCache(string gameRoot, string chapter, string cacheMaxMb)
    {
        // 玩家设置的缓存上限写回 config.json
        if (!string.IsNullOrEmpty(cacheMaxMb) && long.TryParse(cacheMaxMb, out var mb) && mb > 0)
        {
            // ★ 2026-09-29：这里以前是**裸 JsonNode.Parse** —— config.json 里一旦有重复键就抛
            //   「An item with the same key has already been added」（整条 --launch 路径崩）。
            //   统一走 ConfigFile（重复键自愈 + 无 BOM + 镜像到存档区）。
            ConfigFile.Set(gameRoot, "cache_max_mb", JsonValue.Create(mb));
            Console.WriteLine(L("缓存上限已设为 {0} MB", mb));
        }

        var mods = Mods.ScanMods(Path.Combine(Paths.NeutraledRoot(gameRoot), "mods"), chapter, false, true);
        var gameVer = Cache.GameVersion(gameRoot);
        var targets = new List<string> { "root" };
        if (!string.Equals(chapter, "root", StringComparison.OrdinalIgnoreCase)) targets.Add(chapter);
        var sig = Cache.Signature(gameVer, chapter, mods);

        Console.WriteLine(L("===== Neutraled 启动器 ====="));
        Console.WriteLine(L("  签名: {0}  目标: {1}", Cache.SigShort(sig), string.Join("/", targets)));
        var hit = Cache.Lookup(gameRoot, sig);
        if (hit != null)
        {
            var n = Cache.Apply(gameRoot, hit);
            Console.WriteLine(L("  ✅ 缓存命中（{0} MB，第 {1} 次使用）: 硬链接 {2} 个文件（0 秒）", hit.Size / 1024 / 1024, hit.Hits + 1, n));
        }
        else
        {
            Console.WriteLine(L("  ⏳ 无匹配缓存 → 开始部署..."));
            foreach (var t in targets)
            {
                var rc = Deploy(gameRoot, t, null, null, null, null);
                if (rc != 0) { Console.WriteLine(L("[错误] 部署 {0} 失败", t)); return rc; }
            }
            Cache.Store(gameRoot, sig, gameVer, chapter, mods, targets);
            Console.WriteLine(L("  ✅ 部署完成并已存入缓存"));
        }

        // 交给 Steam 拉起（保证计时 / 云存档 / 成就）
        Console.WriteLine(L("  启动游戏（通过 Steam）..."));
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "steam://rungameid/1671210",
                UseShellExecute = true
            });
            Console.WriteLine(L("  ✅ 已请求 Steam 启动 DELTARUNE"));
        }
        catch (Exception ex) { Console.WriteLine(L("  [警告] 启动失败: {0}", ex.Message)); return 1; }
        return 0;
    }

    static int Verify(string gameRoot, string chapter)
    {
        var win = Paths.ChapterDataWin(gameRoot, chapter);
        var data = Injector.Load(win);
        var bootName = BootCodeName(chapter);

        bool coreObj = data.GameObjects.ByName("obj_ntl_core") != null;
        bool initScript = data.Scripts.ByName("scr_ntl_init") != null;
        var boot = data.Code.ByName(bootName);
        var fnInit = data.Functions.ByName("scr_ntl_init");
        bool bootHasInit = fnInit != null && fnInit.Occurrences > 0;

        Console.WriteLine(L("验证 {0}:", chapter));
        Console.WriteLine(L("  obj_ntl_core 对象: {0}", (coreObj ? "OK" : L("缺失"))));
        Console.WriteLine(L("  scr_ntl_init 脚本: {0}", (initScript ? "OK" : L("缺失"))));
        Console.WriteLine(L("  引导 {0}: {1}", bootName, (boot == null ? L("缺失") : (bootHasInit ? L("含 scr_ntl_init OK") : L("未含引导")))));

        // 内容级：文本 × 字体字形覆盖（复用已加载的 data，不额外 Load）
        int contentFail = 0;
        try
        {
            var crep = ContentCheck.CheckTarget(gameRoot, chapter, data, verbose: true,
                        rootUiOnly: string.Equals(chapter, "root", StringComparison.OrdinalIgnoreCase));
            if (crep.Verdict == "失败") contentFail = 1;
        }
        catch (Exception cex) { Console.WriteLine(L("  内容级自检抛异常: ") + cex.GetType().Name + ": " + cex.Message); contentFail = 1; }

        return (coreObj && initScript && bootHasInit && contentFail == 0) ? 0 : 1;
    }
}