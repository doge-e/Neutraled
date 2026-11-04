using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace NtlMapEdit;

public static class Program
{
    static string Nice(Type t)
    {
        if (!t.IsGenericType) return t.Name;
        return t.Name.Split('`')[0] + "<" + string.Join(",", t.GetGenericArguments().Select(Nice)) + ">";
    }

    static UndertaleData Load(string path)
    {
        using var fs = File.OpenRead(path);
        return UndertaleIO.Read(fs, (w, imp) => { }, m => { }, false);
    }

    private static string[] _argv = Array.Empty<string>();
    private static string _command = "";
    private const string UsageHint = "运行 ntl-mapedit help 查看全部命令。";

    /// <summary>参数/用法类错误（退出码固定 2，不写崩溃日志）。</summary>
    private sealed class CliError : Exception
    {
        public int Code { get; }
        public CliError(int code, string? message = null) : base(message ?? "") { Code = code; }
    }

    /// <summary>缺参守卫：打印用法并抛 CliError(2)。</summary>
    private static void Need(string usage, int minArgs)
    {
        if (_argv.Length < minArgs)
        {
            Console.Error.WriteLine("用法: ntl-mapedit " + usage);
            Console.Error.WriteLine("  " + (_command.Length > 0 ? _command : "命令") + " 参数不足（至少需要 " + (minArgs - 1) + " 个参数）。" + UsageHint);
            throw new CliError(2);
        }
    }

    /// <summary>多余参数守卫（maxArgs 含命令本身）：报出多余参数并抛 CliError(2)。</summary>
    private static void NoExtra(string usage, int maxArgs)
    {
        if (_argv.Length > maxArgs)
        {
            Console.Error.WriteLine("多余参数: " + string.Join(" ", _argv.Skip(maxArgs)));
            Console.Error.WriteLine("用法: ntl-mapedit " + usage);
            Console.Error.WriteLine("  " + UsageHint);
            throw new CliError(2);
        }
    }

    /// <summary>输出文件预处理：目标为目录则报用法错误；父目录为空（相对文件名）时按当前目录处理。</summary>
    private static string PrepareOutFile(string path)
    {
        var full = Path.GetFullPath(path);
        if (Directory.Exists(full)) throw new CliError(2, "[错误] 输出路径是目录，请指定文件名: " + full);
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        return full;
    }

    /// <summary>输出目录预处理：目标为文件时报用法错误；否则创建目录。</summary>
    private static string PrepareOutDir(string path)
    {
        var full = Path.GetFullPath(path);
        if (File.Exists(full)) throw new CliError(2, "[错误] 输出路径是文件，请指定目录名: " + full);
        Directory.CreateDirectory(full);
        return full;
    }

    /// <summary>房间名解析：精确匹配优先；单一模糊匹配给提示；多义/无命中抛 CliError(2) 并给候选项。</summary>
    private static UndertaleRoom ResolveRoom(IEnumerable<UndertaleRoom> rooms, string name)
    {
        var list = rooms.ToList();
        var exact = list.FirstOrDefault(r => string.Equals(GameData.Name(r.Name), name, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;
        var matches = list.Where(r => GameData.Name(r.Name).Contains(name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 0)
            throw new CliError(2, "[错误] 未找到房间: " + name + "（运行 ntl-mapedit rooms <data.win> 查看全部房间名）");
        if (matches.Count == 1)
        {
            Console.Error.WriteLine("[提示] 未精确匹配，使用模糊匹配: " + name + " -> " + GameData.Name(matches[0].Name));
            return matches[0];
        }
        throw new CliError(2, "[错误] 房间名不唯一: " + name + "，候选: " +
            string.Join(", ", matches.Take(12).Select(m => GameData.Name(m.Name))) + "（请使用完整房间名）");
    }

    /// <summary>自检套件输入守卫：源必须是存在的 data.win 文件（不是目录）。</summary>
    private static void CheckSuiteSource(string src, string label)
    {
        if (Directory.Exists(src)) throw new CliError(2, label + " FAIL: 源是目录，需要 data.win 文件: " + src);
        if (!File.Exists(src)) throw new CliError(2, label + " FAIL: 源 data.win 不存在: " + src);
    }

    /// <summary>自检套件 workDir 守卫：不得位于真实游戏根内；不得是已存在文件（两个套件共用一致口径）。</summary>
    private static void CheckSuiteWorkDir(string workDir, string label)
    {
        string full;
        try { full = Path.GetFullPath(workDir); }
        catch (Exception ex) { throw new CliError(2, label + " FAIL: 无效 workDir: " + ex.Message); }
        if (File.Exists(full)) throw new CliError(2, label + " FAIL: workDir 是文件而不是目录: " + full);
        var root = FileRole.FindGameRoot();
        if (root == null) return;
        var r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var f = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(f, r, StringComparison.OrdinalIgnoreCase) ||
            f.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new CliError(2, label + " FAIL: workDir 位于真实游戏根内，拒绝运行（fixture 只能放 tmp）: " + full);
    }

    /// <summary>把 common I/O 异常映射为 2（参数/环境错误），其余为 1（内部错误）。</summary>
    private static int ClassifyError(Exception ex)
    {
        if (ex is FileNotFoundException || ex is DirectoryNotFoundException ||
            ex is UnauthorizedAccessException || ex is IOException ||
            ex is System.Runtime.InteropServices.ExternalException) return 2;
        return 1;
    }

    private static string Describe(Exception ex) => ex switch
    {
        FileNotFoundException => "找不到文件（检查 data.win / 输出路径）: " + ex.Message,
        DirectoryNotFoundException => "找不到目录（检查输出路径的父目录）: " + ex.Message,
        UnauthorizedAccessException => "没有访问权限（文件只读、被占用或目标是目录）: " + ex.Message,
        IOException => "读写失败（可能不是有效 data.win，或目标不是可写文件）: " + ex.Message,
        System.Runtime.InteropServices.ExternalException => "图片保存失败（输出路径可能是目录或不可写）: " + ex.Message,
        _ => "未处理异常: " + ex.Message,
    };

    private static string? LogCrash(Exception? ex)
    {
        // 崩溃日志不能写死开发机路径：优先 %LOCALAPPDATA%\DRMapEditor\logs，失败再退临时目录；
        // 两处都失败就静默放弃（不能因为写日志再崩一次）。
        foreach (var dir in new[]
                 {
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DRMapEditor", "logs"),
                     Path.Combine(Path.GetTempPath(), "DRMapEditor"),
                 })
        {
            try
            {
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, "ntl-mapedit-crash.log");
                var sb = new System.Text.StringBuilder();
                sb.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " cmd=" + _command + " argv=" + string.Join(" ", _argv));
                sb.AppendLine("提示: Release 构建的堆栈行号仅供参考；请按命令名与异常类型定位（运行 ntl-mapedit help 查看用法）。");
                sb.AppendLine(ex?.ToString() ?? "(null)");
                sb.AppendLine();
                File.AppendAllText(file, sb.ToString());
                return file;
            }
            catch { }
        }
        return null;
    }

    /// <summary>打印各章节 data.win 是否存在（不需要加载，纯文件探测）。</summary>
    private static void PrintChapterTable()
    {
        string root = GameRootDir();
        int preferred = StartupSettings.ChapterNumber(StartupSettings.Load().DefaultChapter);
        Console.WriteLine("游戏根: " + root);
        for (int n = StartupSettings.MinChapter; n <= StartupSettings.MaxChapter; n++)
        {
            string live = StartupSettings.LivePath(root, n);
            string baseline = StartupSettings.BaselinePath(root, n);
            Console.WriteLine("  Chapter " + n + (preferred == n ? "  ← 默认" : ""));
            Console.WriteLine("    游戏文件(可保存): " + (File.Exists(live) ? "OK   " + live : "缺失 " + live));
            Console.WriteLine("    原版基线(只读)  : " + (File.Exists(baseline) ? "OK   " + baseline : "缺失"));
        }
    }

    /// <summary>查看启动设置全貌：默认章节、设置文件、最近打开、各章节可用性、解析出的启动目标。</summary>
    private static void PrintStartupState()
    {
        var st = StartupSettings.Load();
        Console.WriteLine("默认启动章节: " + StartupSettings.Describe(st.DefaultChapter) + "（" + st.DefaultChapter + "）");
        Console.WriteLine("设置文件: " + StartupSettings.DefaultFile + "（" + (File.Exists(StartupSettings.DefaultFile) ? "存在" : "尚未创建，按 auto 处理") + "）");
        Console.WriteLine("最近打开: " + (RecentStore.LastPath ?? "(无)"));
        Console.WriteLine();
        PrintChapterTable();
        var t = StartupSettings.ResolveTarget(GameRootDir(), RecentStore.LastPath);
        Console.WriteLine();
        Console.WriteLine("下次双击 / 无参数启动将打开: " + t.Path);
        Console.WriteLine("  来源: " + t.Source + (t.IsBaseline ? "（只读）" : ""));
        Console.WriteLine();
        Console.WriteLine("修改: ntl-mapedit default-chapter auto|chapter1..chapter5");
    }

    private static int StartGui(string? path)
    {
        Theme.HideConsole();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += (s, e) => { LogCrash(e.Exception); };
        AppDomain.CurrentDomain.UnhandledException += (s, e) => { LogCrash(e.ExceptionObject as Exception); };
        try
        {
            Application.Run(new EditorForm(path));
        }
        catch (Exception ex)
        {
            LogCrash(ex);
            throw;
        }
        return 0;
    }

    [STAThread]
    public static int Main(string[] args)
    {
        _argv = args ?? Array.Empty<string>();
        _command = _argv.Length > 0 ? _argv[0] : "";
        try
        {
            return Run(_argv);
        }
        catch (CliError ce)
        {
            if (!string.IsNullOrEmpty(ce.Message)) Console.Error.WriteLine(ce.Message);
            return ce.Code;
        }
        catch (Exception ex)
        {
            int code = ClassifyError(ex);
            Console.Error.WriteLine("[错误] " + Describe(ex) + "（命令: " + (_command.Length > 0 ? _command : "(无参数)") + "）");
            var logPath = LogCrash(ex);
            if (logPath != null)
                Console.Error.WriteLine("  崩溃日志: " + logPath + "（堆栈行号仅供参考，请按命令与异常类型定位）");
            return code;
        }
    }

    private static int Run(string[] args)
    {
        if (args.Length > 0 && args[0] == "gui")
        {
            if (args.Length > 2)
            {
                NoExtra("gui [data.win|chapterN]", 2);
            }
            if (args.Length > 1)
            {
                int wantChapter = StartupSettings.ChapterNumber(args[1]);
                if (wantChapter > 0)
                {
                    string root = GameRootDir();
                    string live = StartupSettings.LivePath(root, wantChapter);
                    if (File.Exists(live)) return StartGui(live);
                    string baseline = StartupSettings.BaselinePath(root, wantChapter);
                    if (File.Exists(baseline))
                    {
                        Console.WriteLine("[提示] chapter" + wantChapter + " 游戏文件不存在，改打开原版基线（只读）: " + baseline);
                        return StartGui(baseline);
                    }
                    throw new CliError(2, "[错误] chapter" + wantChapter + " 的数据文件不存在（已试: " + live + "、" + baseline + "）；运行 ntl-mapedit chapters 查看可用章节");
                }
                return StartGui(args[1]);
            }
            return StartGui(null);
        }
        if (args.Length == 0)
        {
            // 双击 exe（无参数）直接进 GUI；命令行帮助用 help/-h/--help。
            return StartGui(null);
        }
        if (args[0] == "help" || args[0] == "-h" || args[0] == "--help" || args[0] == "-?")
        {
            if (args.Length > 1)
            {
                NoExtra("help", 1);
            }
            Console.WriteLine("GUI / 查看:");
            Console.WriteLine("  ntl-mapedit                            - 打开地图编辑器（GUI，等同 gui）");
            Console.WriteLine("  ntl-mapedit gui [data.win]              - 打开地图编辑器（GUI）");
            Console.WriteLine("  ntl-mapedit gui chapterN                - 直接打开第 N 章游戏文件（N=1..5；缺 live 自动退回原版基线）");
            Console.WriteLine("  ntl-mapedit default-chapter [auto|chapterN] - 查看/设置默认启动章节（双击 exe 或无参数启动时打开哪个章节）");
            Console.WriteLine("  ntl-mapedit chapters                    - 列出各章节 data.win 是否存在（游戏文件 / 原版基线）");
            Console.WriteLine("  ntl-mapedit rooms <data.win>            - 列出全部房间（名字/宽高）");
            Console.WriteLine("  ntl-mapedit inspect <data.win> <room>   - 逐层 dump 房间结构（实例/瓦片）");
            Console.WriteLine("  ntl-mapedit room-stat <data.win> <room> - 房间汇总：尺寸/图层/实例/图块/背景/瓦片来源");
            Console.WriteLine("  ntl-mapedit room-inst <data.win> <room> - 实例清单：id/x/y/spr/scale/color");
            Console.WriteLine("  ntl-mapedit render <data.win> <room> [scale] [out.png] - 把房间渲染成 PNG");
            Console.WriteLine("  ntl-mapedit render-stats <data.win> <room> [scale] [out.json] - 渲染覆盖统计（逐层绘制数/不支持项/黑像素占比）");
            Console.WriteLine("  ntl-mapedit asset-clone-test <data.win> <room> - 复制房间深拷贝自检（Assets Sprite/NineSlice/原生 Tiles）");
            Console.WriteLine("  ntl-mapedit sprite <data.win> [filter]   - 列出精灵帧布局（空纹理帧会跳过并告警）");
            Console.WriteLine("  ntl-mapedit object <data.win> [filter]   - 列出游戏对象定义");
            Console.WriteLine("  ntl-mapedit probe-room <data.win> <room> - 实例创建顺序/引用一致性探测");
            Console.WriteLine("  ntl-mapedit texture <data.win> <outDir>  - 导出前 8 张纹理页为 PNG");
            Console.WriteLine("测试（离线，不碰游戏本体）:");
            Console.WriteLine("  ntl-mapedit document-test <src data.win> <dst data.win>  - 编辑模型全量测试（房间/图层/实例/瓦片/剪贴板/对齐）");
            Console.WriteLine("  ntl-mapedit mapedit-test <src data.win> <workDir>        - mer 写盘守卫自检（原版基线只读/live 可保存/sidecar/默认启动目标）");
            Console.WriteLine("  ntl-mapedit native-tiles-test <src data.win> <workDir> - 原生 LayerTilesData 批量绘制/撤销/保存/重载闭环");
            Console.WriteLine("  ntl-mapedit structure-test <src data.win> <dst data.win> - 房间结构复制/删除/排序往返测试");
            Console.WriteLine("  ntl-mapedit resource-browser-test <data.win>          - 资源浏览器只读自检（12 类资源可枚举 + 源文件不变）");
            Console.WriteLine("  ntl-mapedit stress-test <src data.win> <dst data.win>    - 大房间（room_town_north）绘制/保存/重载冒烟");
            Console.WriteLine("  ntl-mapedit vanilla-scan-test <data.win> [outJson]       - 原版交互/对话扫描自检（反编译+条件三态+lang 解析）");
            Console.WriteLine("  ntl-mapedit vanilla-scan <data.win> <object> [room] [x] [y] [outJson] - 扫描单个对象的原版交互/对白（只读；打印行摘要，可选导出 JSON 对账）");
            Console.WriteLine("  ntl-mapedit settings-test <workDir>                      - 默认启动章节设置自检（读写/坏文件自愈/解析优先级）");
            Console.WriteLine("  ntl-mapedit battle-test [data.win] [outDir]              - 战斗配置校验/sidecar 往返/生成 mod 的自检");
            Console.WriteLine("  ntl-mapedit battle-demo [data.win] [modsDir]             - 生成可直接部署的示例战斗 mod（默认 Neutraled/mods）");
            Console.WriteLine("  ntl-mapedit manifest-test [modsDir] [outJson]            - mods.json 整包清单扫描/校验/保存重读自检");
            Console.WriteLine("  ntl-mapedit edit-test | tile-test <src> <dst>            - 早期编辑/瓦片测试");
            Console.WriteLine("  ntl-mapedit save-roundtrip <src> <dst>                   - 经 EditorDocument 打开→不修改→Save，校验 SHA256 不变");
            Console.WriteLine("  ntl-mapedit roundtrip <src data.win> <dst data.win>      - data.win 读写往返");
            Console.WriteLine("其它:");
            Console.WriteLine("  ntl-mapedit types [filter] | io | delegates              - 反射 UndertaleModLib 类型/接口");
            Console.WriteLine("退出码: 0=成功  1=运行失败（业务判据未过）  2=用法/参数/路径错误");
            Console.WriteLine("提示: 相对输出路径按当前目录解析；输出文件父目录会自动创建；Release 堆栈行号仅供参考。");
            Console.WriteLine("崩溃日志: %LOCALAPPDATA%\\DRMapEditor\\logs\\ntl-mapedit-crash.log");
            return 0;
        }
        var asm = typeof(UndertaleData).Assembly;
        if (args[0] == "types")
        {
            NoExtra("types [filter]", 2);
            var filter = args.Length > 1 ? args[1] : "";
            foreach (var t in asm.GetTypes().Where(t => t.FullName != null && t.FullName.Contains(filter, StringComparison.OrdinalIgnoreCase)).OrderBy(t => t.FullName))
            {
                Console.WriteLine($"== {t.FullName} (base {t.BaseType?.Name})");
                foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                    Console.WriteLine($"   prop {Nice(p.PropertyType)} {p.Name}");
                foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Where(m => !m.IsSpecialName))
                    Console.WriteLine($"   meth {Nice(m.ReturnType)} {m.Name}({string.Join(", ", m.GetParameters().Select(p => Nice(p.ParameterType) + " " + p.Name))})");
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                    Console.WriteLine($"   fld {Nice(f.FieldType)} {f.Name}");
            }
            return 0;
        }
        if (args[0] == "io")
        {
            NoExtra("io", 1);
            var t = asm.GetType("UndertaleModLib.UndertaleIO")!;
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                Console.WriteLine($"meth {Nice(m.ReturnType)} {m.Name}({string.Join(", ", m.GetParameters().Select(p => Nice(p.ParameterType) + " " + p.Name))})");
            return 0;
        }
        if (args[0] == "delegates")
        {
            NoExtra("delegates", 1);
            foreach (var n in new[] { "UndertaleModLib.UndertaleReader+WarningHandlerDelegate", "UndertaleModLib.UndertaleReader+MessageHandlerDelegate" })
            {
                var t = asm.GetType(n); if (t == null) { Console.WriteLine("MISS " + n); continue; }
                Console.WriteLine("== " + n);
                foreach (var p in t.GetMethod("Invoke")!.GetParameters()) Console.WriteLine($"   param {Nice(p.ParameterType)} {p.Name}");
            }
            return 0;
        }
        if (args[0] == "rooms")
        {
            Need("rooms <data.win>", 2);
            NoExtra("rooms <data.win>", 2);
            var data = Load(args[1]);
            Console.WriteLine($"rooms={data.Rooms.Count} sprites={data.Sprites.Count} objects={data.GameObjects.Count} backgrounds={data.Backgrounds.Count} textures={data.EmbeddedTextures.Count}");
            for (int i = 0; i < data.Rooms.Count; i++)
            {
                var r = data.Rooms[i];
                Console.WriteLine($"[{i}] {r.Name?.Content}  {r.Width}x{r.Height} layers={r.Layers?.Count} objs={r.GameObjects?.Count} tiles={r.Tiles?.Count}");
            }
            return 0;
        }
        if (args[0] == "inspect")
        {
            Need("inspect <data.win> <room>", 3);
            NoExtra("inspect <data.win> <room>", 3);
            var data = Load(args[1]);
            var room = ResolveRoom(data.Rooms, args[2]);
            Console.WriteLine($"ROOM {room.Name?.Content} {room.Width}x{room.Height} speed={room.Speed} persistent={room.Persistent} flags={room.Flags} bgcolor=0x{room.BackgroundColor:X8} drawbg={room.DrawBackgroundColor}");
            Console.WriteLine($"layers={room.Layers?.Count} legacyObjs={room.GameObjects?.Count} legacyTiles={room.Tiles?.Count} bgs={room.Backgrounds?.Count} views={room.Views?.Count}");
            if (room.Layers != null)
            {
                foreach (var L in room.Layers)
                {
                    Console.WriteLine($"LAYER '{L.LayerName?.Content}' id={L.LayerId} type={L.LayerType} depth={L.LayerDepth} vis={L.IsVisible} off=({L.XOffset},{L.YOffset}) speed=({L.HSpeed},{L.VSpeed})");
                    if (L.InstancesData != null)
                    {
                        int n = L.InstancesData.Instances.Count;
                        Console.WriteLine($"   instances={n}");
                        foreach (var go in L.InstancesData.Instances.Take(6))
                            Console.WriteLine($"     obj={go.ObjectDefinition?.Name?.Content} id={go.InstanceID} pos=({go.X},{go.Y}) scale=({go.ScaleX},{go.ScaleY}) rot={go.Rotation} color=0x{go.Color:X8} img={go.ImageIndex} spr={go.ObjectDefinition?.Sprite?.Name?.Content}");
                    }
                    if (L.TilesData != null)
                    {
                        var td = L.TilesData;
                        Console.WriteLine($"   tiles={td.TilesX}x{td.TilesY} bg={td.Background?.Name?.Content}");
                        if (td.Background != null)
                        {
                            var bg = td.Background;
                            Console.WriteLine($"     bg tile={bg.GMS2TileWidth}x{bg.GMS2TileHeight} border=({bg.GMS2OutputBorderX},{bg.GMS2OutputBorderY}) cols={bg.GMS2TileColumns} itemsPerTile={bg.GMS2ItemsPerTileCount} tileCount={bg.GMS2TileCount} frameLen={bg.GMS2FrameLength} tileIds={bg.GMS2TileIds?.Count} texture={(bg.Texture == null ? "null" : bg.Texture.SourceWidth + "x" + bg.Texture.SourceHeight + " target " + bg.Texture.TargetWidth + "x" + bg.Texture.TargetHeight)}");
                        }
                        var ids = new List<uint>();
                        if (td.TileData != null) foreach (var row in td.TileData) if (row != null) ids.AddRange(row.Where(v => v != 0).Take(2000));
                        uint mn = ids.Count == 0 ? 0u : ids.Min(); uint mx = ids.Count == 0 ? 0u : ids.Max();
                        Console.WriteLine($"     nonzeroTiles={ids.Count} min=0x{mn:X8} max=0x{mx:X8} sample={string.Join(",", ids.Take(12).Select(v => "0x" + v.ToString("X8")))}");
                    }
                    if (L.BackgroundData != null)
                        Console.WriteLine($"   bgLayer sprite={L.BackgroundData.Sprite?.Name?.Content} visible={L.BackgroundData.Visible} fg={L.BackgroundData.Foreground} tiled=({L.BackgroundData.TiledHorizontally},{L.BackgroundData.TiledVertically}) stretch={L.BackgroundData.Stretch}");
                    if (L.AssetsData != null)
                    {
                        Console.WriteLine($"   assets legacyTiles={L.AssetsData.LegacyTiles?.Count} sprites={L.AssetsData.Sprites?.Count} seq={L.AssetsData.Sequences?.Count} nineSlices={L.AssetsData.NineSlices?.Count} particles={L.AssetsData.ParticleSystems?.Count} text={L.AssetsData.TextItems?.Count}");
                        var lts = L.AssetsData.LegacyTiles;
                        if (lts != null) foreach (var tile in lts.Take(3))
                        {
                            Console.WriteLine($"     TILE xy=({tile.X},{tile.Y}) size={tile.Width}x{tile.Height} src=({tile.SourceX},{tile.SourceY}) depth={tile.TileDepth} scale=({tile.ScaleX},{tile.ScaleY}) color=0x{tile.Color:X8}");
                            var bgDef = tile.BackgroundDefinition;
                            if (bgDef != null) Console.WriteLine($"       bg={bgDef.Name?.Content} bgGMS2 tile={bgDef.GMS2TileWidth}x{bgDef.GMS2TileHeight} cols={bgDef.GMS2TileColumns} border=({bgDef.GMS2OutputBorderX},{bgDef.GMS2OutputBorderY}) items={bgDef.GMS2ItemsPerTileCount} count={bgDef.GMS2TileCount} tex={(bgDef.Texture == null ? "null" : bgDef.Texture.SourceWidth + "x" + bgDef.Texture.SourceHeight)}");
                            if (tile.SpriteDefinition != null) Console.WriteLine($"       spr={tile.SpriteDefinition.Name?.Content} frames={tile.SpriteDefinition.Textures?.Count}");
                            if (tile.Tpag != null) Console.WriteLine($"       tpag src=({tile.Tpag.SourceX},{tile.Tpag.SourceY},{tile.Tpag.SourceWidth},{tile.Tpag.SourceHeight}) target=({tile.Tpag.TargetX},{tile.Tpag.TargetY},{tile.Tpag.TargetWidth},{tile.Tpag.TargetHeight}) page={tile.Tpag.TexturePage?.Name?.Content}");
                        }
                    }
                }
            }
            if (room.Backgrounds != null)
                foreach (var b in room.Backgrounds)
                    Console.WriteLine($"ROOMBG enabled={b.Enabled} fg={b.Foreground} def={b.BackgroundDefinition?.Name?.Content} x={b.X} y={b.Y} stretch={b.Stretch} tiledH={b.TiledHorizontally} tiledV={b.TiledVertically}");
            // sprite frame layout sample
            var anyInst = room.Layers?.Where(l => l.InstancesData != null).SelectMany(l => l.InstancesData.Instances).FirstOrDefault();
            var spr = anyInst?.ObjectDefinition?.Sprite;
            if (spr != null)
            {
                Console.WriteLine($"SPRITE {spr.Name?.Content} {spr.Width}x{spr.Height} origin=({spr.OriginX},{spr.OriginY}) frames={spr.Textures?.Count} margins=({spr.MarginLeft},{spr.MarginRight},{spr.MarginTop},{spr.MarginBottom})");
                int fi = 0;
                foreach (var f in spr.Textures.Take(3))
                {
                    var tp = f.Texture;
                    if (tp == null)
                    {
                        Console.WriteLine($"  frame{fi++}: (空纹理帧，已跳过)");
                        continue;
                    }
                    Console.WriteLine($"  frame{fi++}: src=({tp.SourceX},{tp.SourceY},{tp.SourceWidth},{tp.SourceHeight}) target=({tp.TargetX},{tp.TargetY},{tp.TargetWidth},{tp.TargetHeight}) bound=({tp.BoundingWidth},{tp.BoundingHeight}) page={tp.TexturePage?.Name?.Content} pageSize={tp.TexturePage?.TextureWidth}x{tp.TexturePage?.TextureHeight}");
                }
            }
            // texture page sample
            if (data.EmbeddedTextures.Count > 0)
            {
                var tp0 = data.EmbeddedTextures[0];
                Console.WriteLine($"TEXPAGE[0] {tp0.Name?.Content} {tp0.TextureWidth}x{tp0.TextureHeight} data={(tp0.TextureData == null ? "null" : tp0.TextureData.Width + "x" + tp0.TextureData.Height)}");
            }
            return 0;
        }
        if (args[0] == "sprite")
        {
            Need("sprite <data.win> [filter]", 2);
            NoExtra("sprite <data.win> [filter]", 3);
            var gd = new GameData(args[1]);
            var filter = args.Length > 2 ? args[2] : "";
            int emptyFrames = 0, emptySprites = 0;
            foreach (var s in gd.Data.Sprites.Where(s => GameData.Name(s.Name).Contains(filter, StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine($"SPR {GameData.Name(s.Name)} {s.Width}x{s.Height} origin=({s.OriginX},{s.OriginY}) frames={s.Textures?.Count} margins=({s.MarginLeft},{s.MarginRight},{s.MarginTop},{s.MarginBottom}) ");
                if (s.Textures == null) continue;
                int valid = 0;
                for (int fi = 0; fi < s.Textures.Count && fi < 32; fi++)
                {
                    var tp = s.Textures[fi]?.Texture;
                    if (tp == null)
                    {
                        Console.WriteLine($"   f{fi} (空纹理帧 Texture=null，已跳过)");
                        emptyFrames++;
                        continue;
                    }
                    valid++;
                    Console.WriteLine($"   f{fi} src=({tp.SourceX},{tp.SourceY},{tp.SourceWidth},{tp.SourceHeight}) target=({tp.TargetX},{tp.TargetY},{tp.TargetWidth},{tp.TargetHeight}) bound=({tp.BoundingWidth},{tp.BoundingHeight}) page={GameData.Name(tp.TexturePage?.Name)}");
                }
                if (s.Textures.Count > 0 && valid == 0) emptySprites++;
            }
            if (emptyFrames > 0)
            {
                Console.Error.WriteLine($"[警告] {emptyFrames} 个空纹理帧已跳过；{emptySprites} 个 sprite 无有效纹理帧（坏数据，建议修复/重导 data.win）");
                return emptySprites > 0 ? 1 : 0;
            }
            return 0;
        }
        if (args[0] == "object")
        {
            Need("object <data.win> [filter]", 2);
            NoExtra("object <data.win> [filter]", 3);
            var gd = new GameData(args[1]);
            var filter = args.Length > 2 ? args[2] : "";
            foreach (var o in gd.Data.GameObjects.Where(o => GameData.Name(o.Name).Contains(filter, StringComparison.OrdinalIgnoreCase)))
                Console.WriteLine($"OBJ {GameData.Name(o.Name)} sprite={GameData.Name(o.Sprite?.Name)} visible={o.Visible} solid={o.Solid} persistent={o.Persistent} depth={o.Depth} parent={GameData.Name(o.ParentId?.Name)}");
            return 0;
        }
        if (args[0] == "probe-room")
        {
            Need("probe-room <data.win> <room>", 3);
            NoExtra("probe-room <data.win> <room>", 3);
            var gd = new GameData(args[1]);
            var room = ResolveRoom(gd.Data.Rooms, args[2]);
            var layerInsts = room.Layers == null
                ? new List<UndertaleRoom.GameObject>()
                : room.Layers.Where(l => l.InstancesData != null).SelectMany(l => l.InstancesData.Instances).ToList();
            var creationOrder = room.InstanceCreationOrderIDs?.InstanceIDs;
            Console.WriteLine($"layerInstances={layerInsts.Count} legacyGameObjects={room.GameObjects?.Count} creationOrder={(creationOrder == null ? "(无 creationOrder)" : creationOrder.Count.ToString())}");
            if (room.GameObjects != null && layerInsts.Count > 0 && room.GameObjects.Count > 0)
            {
                var first = layerInsts[0];
                int idx = -1;
                for (int i = 0; i < room.GameObjects.Count; i++) if (ReferenceEquals(room.GameObjects[i], first)) { idx = i; break; }
                Console.WriteLine($"sameRef={(idx >= 0)} legacyIndex={idx} canCast={room.GameObjects[0].InstanceID}");
                Console.WriteLine("legacyIDs=" + string.Join(",", room.GameObjects.Select(g => g.InstanceID).Take(8)));
                Console.WriteLine("layerIDs=" + string.Join(",", layerInsts.Select(g => g.InstanceID).Take(8)));
                if (creationOrder != null && creationOrder.Count > 0)
                    Console.WriteLine("orderIDs=" + string.Join(",", creationOrder.Take(8)));
                else
                    Console.WriteLine("orderIDs=(无 creationOrder，GMS2 房间可省略该表)");
            }
            return 0;
        }
        if (args[0] == "edit-test")
        {
            Need("edit-test <src data.win> <dst data.win>", 3);
            NoExtra("edit-test <src data.win> <dst data.win>", 3);
            var src = args[1];
            var dst = args[2];
            var gd = new GameData(src);
            var room = gd.FindRoom("room_krisroom")!;
            var layer = RoomEdit.InstancesLayer(room)!;
            var obj = gd.Data.GameObjects.First(o => GameData.Name(o.Name) == "obj_markerB");
            int before = RoomEdit.AllInstances(room).Count();
            var added = RoomEdit.AddInstance(gd.Data, room, layer, obj, 120, 150);
            Console.WriteLine($"added id={added.InstanceID} at (120,150): {before} -> {RoomEdit.AllInstances(room).Count()}");
            dst = PrepareOutFile(dst);
            using (var fs = File.Create(dst)) UndertaleIO.Write(fs, gd.Data, m => { });
            Console.WriteLine($"written {dst} {new FileInfo(dst).Length} B (src {new FileInfo(src).Length} B)");
            var gd2 = new GameData(dst);
            var room2 = gd2.FindRoom("room_krisroom")!;
            var insts = RoomEdit.AllInstances(room2).ToList();
            var found = insts.FirstOrDefault(x => x.Inst.InstanceID == added.InstanceID);
            Console.WriteLine($"reloaded: instances={insts.Count} legacy={room2.GameObjects?.Count} foundId={found.Inst != null} pos=({found.Inst?.X},{found.Inst?.Y}) obj={GameData.Name(found.Inst?.ObjectDefinition?.Name)}");
            if (found.Inst != null)
            {
                RoomEdit.RemoveInstance(room2, found.Inst);
                var dst2 = dst + ".del.win";
                using (var fs = File.Create(dst2)) UndertaleIO.Write(fs, gd2.Data, m => { });
                var gd3 = new GameData(dst2);
                var room3 = gd3.FindRoom("room_krisroom")!;
                Console.WriteLine($"after delete write+reload: instances={RoomEdit.AllInstances(room3).Count()} legacy={room3.GameObjects?.Count} size={new FileInfo(dst2).Length} B");
            }
            return 0;
        }
        if (args[0] == "tile-test")
        {
            Need("tile-test <src data.win> <dst data.win>", 3);
            NoExtra("tile-test <src data.win> <dst data.win>", 3);
            var src = args[1];
            var dst = args[2];
            var gd = new GameData(src);
            var room = gd.FindRoom("room_krisroom")!;
            var layer = RoomEdit.TilesLayer(room)!;
            var srcTile = RoomEdit.AllTiles(room).First().Tile;
            int before = RoomEdit.AllTiles(room).Count();
            var added = RoomEdit.AddTile(gd.Data, room, layer, srcTile, 200, 190);
            Console.WriteLine($"added tile id={added.InstanceID} at (200,190): {before} -> {RoomEdit.AllTiles(room).Count()} tpag={(added.Tpag == null ? "NULL" : "ok")}");
            dst = PrepareOutFile(dst);
            using (var fs = File.Create(dst)) UndertaleIO.Write(fs, gd.Data, m => { });
            Console.WriteLine($"written {dst} {new FileInfo(dst).Length} B (src {new FileInfo(src).Length} B)");
            var gd2 = new GameData(dst);
            var room2 = gd2.FindRoom("room_krisroom")!;
            var tiles = RoomEdit.AllTiles(room2).ToList();
            Console.WriteLine($"reloaded: tiles={tiles.Count}");
            foreach (var (l2, t2) in tiles)
                Console.WriteLine($"  tile id={t2.InstanceID} xy=({t2.X},{t2.Y}) spr={GameData.Name(t2.SpriteDefinition?.Name)} tpag={(t2.Tpag == null ? "NULL" : "ok")}");
            var dst2 = dst + ".del.win";
            foreach (var (l3, t3) in tiles.Where(x => x.Tile.InstanceID == added.InstanceID).ToList())
                RoomEdit.RemoveTile(room2, t3);
            using (var fs = File.Create(dst2)) UndertaleIO.Write(fs, gd2.Data, m => { });
            var gd3 = new GameData(dst2);
            var room3 = gd3.FindRoom("room_krisroom")!;
            Console.WriteLine($"after delete write+reload: tiles={RoomEdit.AllTiles(room3).Count()} size={new FileInfo(dst2).Length} B");
            return 0;
        }


        if (args[0] == "document-test")
        {
            Need("document-test <src data.win> <dst data.win>", 3);
            NoExtra("document-test <src data.win> <dst data.win>", 3);
            var src = args[1];
            var dst = args[2];
            int fails = 0;
            void Check(bool ok, string label)
            {
                Console.WriteLine((ok ? "PASS " : "FAIL ") + label);
                if (!ok) fails++;
            }
            var gd = new GameData(src);
            using var doc = new EditorDocument(gd, src);
            int roomsChanged = 0;
            doc.RoomsChanged += () => roomsChanged++;
            var start = gd.Rooms.First(r => GameData.Name(r.Name) == "room_krisroom");
            doc.OpenRoom(start);
            Check(doc.RoomName() == "room_krisroom", "open room");

            // ---------------- clipboard / paste / duplicate / invert (room_krisroom)
            // room_krisroom 的层数/实例数各章不同（ch1 8/26、ch4 6/28、ch5 6/19 …），
            // 基线一律从当前 data.win 的实测值取，后续断言只用相对值，避免测试数据耦合。
            var everyInst = RoomEdit.AllInstances(doc.Room!).Select(t => t.Inst).ToList();
            int baseInstances = everyInst.Count;
            int baseLayers = doc.Room!.Layers?.Count ?? 0;
            Console.WriteLine($"baseline: room_krisroom layers={baseLayers} instances={baseInstances}");
            Check(baseInstances >= 2, $"clipboard: baseline instances {baseInstances} (need >= 2)");
            var srcA = everyInst[0];
            var srcB = everyInst[1];
            doc.PushUndo();   // snapshot before the property mutation below, so undo can restore it
            srcA.ScaleX = 1.5f;
            srcA.ScaleY = 0.5f;
            srcA.Rotation = 12f;
            srcA.Color = 0xFF00FF00;
            srcA.ImageIndex = 3;
            doc.SelectMany(new[] { srcA, srcB }, false);
            Check(doc.Selection.Count == 2, "clipboard: selected 2");
            Check(doc.CopySelection() == 2 && doc.ClipboardCount == 2, "clipboard: copy 2 entries");
            var pasted = doc.PasteClipboard(8, 8);
            Check(pasted.Count == 2, "clipboard: paste 2");
            Check(RoomEdit.AllInstances(doc.Room!).Count() == baseInstances + 2,
                $"clipboard: instances {baseInstances + 2} after paste");
            Check(pasted[0].X == srcA.X + 8 && pasted[0].Y == srcA.Y + 8, "clipboard: paste offset +8/+8");
            Check(pasted[0].InstanceID != srcA.InstanceID, "clipboard: fresh instance id");
            Check(Math.Abs(pasted[0].ScaleX - 1.5f) < 0.001f && Math.Abs(pasted[0].Rotation - 12f) < 0.001f &&
                  pasted[0].Color == 0xFF00FF00 && pasted[0].ImageIndex == 3, "clipboard: paste keeps properties");
            Check(doc.IsSelected(pasted[0]) && doc.IsSelected(pasted[1]), "clipboard: pasted selected");
            doc.InvertSelection();
            Check(doc.Selection.Count == baseInstances, $"invert: {baseInstances} selected");
            doc.SelectMany(new[] { srcA, srcB }, false);
            var dups = doc.DuplicateSelection(8, 8);
            Check(dups.Count == 2 && RoomEdit.AllInstances(doc.Room!).Count() == baseInstances + 4,
                $"duplicate: 2 more ({baseInstances + 4})");
            Check(doc.IsSelected(dups[0]), "duplicate: copies selected");
            doc.SelectMany(pasted, false);
            Check(doc.CutSelection() == 2 && RoomEdit.AllInstances(doc.Room!).Count() == baseInstances + 2,
                "cut: removed pasted 2");
            Check(doc.ClipboardCount == 2, "cut: clipboard kept 2");
            for (int u = 0; u < 4; u++) doc.Undo();
            Check(RoomEdit.AllInstances(doc.Room!).Count() == baseInstances, $"undo: back to {baseInstances} instances");
            var restored = RoomEdit.AllInstances(doc.Room!).Select(t => t.Inst).First(i => i.InstanceID == srcA.InstanceID);
            Check(Math.Abs(restored.ScaleX - 1f) < 0.001f && restored.Rotation == 0f, "undo: property mutation rolled back");
            doc.ClearSelection();

            // ---------------- layer properties: depth / offset / visibility / background (room_krisroom)
            var layers = doc.Room!.Layers!;
            Check(layers.Count == baseLayers, $"layer: baseline {baseLayers} layers");
            var bgLayer = layers.First(l => l.LayerType == UndertaleRoom.LayerType.Background && l.BackgroundData?.Sprite != null);
            int origDepth = bgLayer.LayerDepth;
            Check(doc.SetLayerDepth(bgLayer, origDepth - 50) && bgLayer.LayerDepth == origDepth - 50,
                "layer: depth applied");
            bool depthSorted = true;
            for (int i = 1; i < doc.Room!.Layers!.Count; i++)
                if (doc.Room.Layers[i].LayerDepth < doc.Room.Layers[i - 1].LayerDepth) depthSorted = false;
            Check(depthSorted, "layer: list re-sorted after depth change");
            doc.Undo();
            Check(bgLayer.LayerDepth == origDepth, "layer: depth undo restored");

            Check(doc.SetLayerOffset(bgLayer, 12f, -7f) && Math.Abs(bgLayer.XOffset - 12f) < 0.001f &&
                  Math.Abs(bgLayer.YOffset + 7f) < 0.001f, "layer: offset applied");
            doc.Undo();
            Check(Math.Abs(bgLayer.XOffset) < 0.001f && Math.Abs(bgLayer.YOffset) < 0.001f,
                "layer: offset undo restored");

            Check(doc.SetLayerVisible(bgLayer, false) && !bgLayer.IsVisible, "layer: visibility off");
            doc.Undo();
            Check(bgLayer.IsVisible, "layer: visibility undo restored");

            var bd = bgLayer.BackgroundData!;
            bool oldBgVisible = bd.Visible, oldTiledH = bd.TiledHorizontally, oldTiledV = bd.TiledVertically,
                oldStretch = bd.Stretch;
            uint oldColor = bd.Color;
            Check(doc.SetBackgroundProps(bgLayer, visible: false, tiledH: false, tiledV: false, stretch: true,
                      color: 0xFF804020) == true, "background: props applied");
            Check(bd.Visible == false && bd.TiledHorizontally == false && bd.TiledVertically == false &&
                  bd.Stretch && bd.Color == 0xFF804020, "background: props committed");
            doc.Undo();
            Check(bd.Visible == oldBgVisible && bd.TiledHorizontally == oldTiledH && bd.TiledVertically == oldTiledV &&
                  bd.Stretch == oldStretch && bd.Color == oldColor, "background: props undo restored");

            using (var render = RoomRender.Render(gd, doc.Room!))
            {
                bool anyPixel = false;
                for (int y = 0; y < render.Height && !anyPixel; y += 4)
                    for (int x = 0; x < render.Width && !anyPixel; x += 4)
                    {
                        var px = render.GetPixel(x, y);
                        if (px.R != 0 || px.G != 0 || px.B != 0) anyPixel = true;
                    }
                Check(render.Width == 320 && render.Height == 240 && anyPixel, "layer: render non-empty");
            }

            // ---------------- tile painting: continuous / rect / line / bucket (room_krisroom, 1 baseline tile)
            var baselineTiles = RoomEdit.AllTiles(doc.Room!).ToList();
            Check(baselineTiles.Count == 1, "tile: baseline 1 tile");
            var tileLayer = baselineTiles[0].Layer;
            var brush = RoomEdit.CloneTile(baselineTiles[0].Tile);
            var cell = RoomEdit.TileWorldSize(brush);
            int cw = Math.Max(1, (int)cell.Width), ch = Math.Max(1, (int)cell.Height);
            Check(cw > 1 && ch > 1, $"tile: world size {cw}x{ch}");
            Check(RoomEdit.SameTileSource(brush, baselineTiles[0].Tile), "tile: clone keeps source");

            var baseTile = baselineTiles[0].Tile;
            Check(ReferenceEquals(RoomEdit.TileAtPoint(tileLayer,
                      new PointF(baseTile.X + cell.Width / 2f, baseTile.Y + cell.Height / 2f)), baseTile),
                "tile: TileAtPoint finds baseline tile");
            Check(RoomEdit.TileAtPoint(tileLayer, new PointF(-500, -500)) == null, "tile: TileAtPoint empty returns null");

            // 找一个 6 列 × 3 行都空着的格子区域（room_krisroom 320x240 最多 6x4 格可用），
            // 这样矩形（3x2）与油漆桶（3x3）都能落在房间内 —— 房间外的区域 FloodRegion 会拒绝。
            Point origin = new(-1, -1);
            int roomW = (int)doc.Room!.Width, roomH = (int)doc.Room.Height;
            for (int y = 0; y + ch * 3 <= roomH && origin.X < 0; y += ch)
                for (int x = 0; x + cw * 6 <= roomW && origin.X < 0; x += cw)
                {
                    bool clear = true;
                    for (int ry = 0; ry < 3 && clear; ry++)
                        for (int rx = 0; rx < 6 && clear; rx++)
                            if (RoomEdit.TileAtPoint(tileLayer,
                                    new PointF(x + rx * cw + cw / 2f, y + ry * ch + ch / 2f)) != null) clear = false;
                    if (clear) origin = new Point(x, y);
                }
            Check(origin.X >= 0, $"tile: found 6x3 clear cell area in {roomW}x{roomH} (origin={origin})");
            var rectCells = RoomEdit.TileCellRect(origin, new Point(origin.X + cw * 2, origin.Y + ch), cell);
            Check(rectCells.Count == 6, "tile: rect 3x2 => 6 cells");
            Check(RoomEdit.TileCellLine(origin, new Point(origin.X + cw * 4, origin.Y), cell).Count == 5, "tile: line 5 cells");
            Check(RoomEdit.TileCellLine(origin, new Point(origin.X + cw * 2, origin.Y + ch * 2), cell).Count == 3,
                "tile: diagonal line 3 cells");
            Check(RoomEdit.SnapToTileCell(brush, new PointF(origin.X + 1, origin.Y + ch - 1)) == origin,
                "tile: snap to cell");

            doc.PushUndo();
            Check(RoomEdit.PaintTileCells(gd.Data, tileLayer, brush, rectCells) == 6, "tile: paint 6 new cells");
            Check(RoomEdit.AllTiles(doc.Room!).Count() == 7, "tile: room tiles 7 after paint");
            Check(RoomEdit.PaintTileCells(gd.Data, tileLayer, brush, rectCells) == 0, "tile: repaint same source = 0 changes");
            var brush2 = RoomEdit.CloneTile(brush);
            brush2.SourceX += (int)brush.Width;
            brush2.SourceY += (int)brush.Height;
            Check(RoomEdit.PaintTileCells(gd.Data, tileLayer, brush2, new[] { origin }) == 1,
                "tile: repaint different source = 1 change");
            Check(RoomEdit.SameTileSource(RoomEdit.TileAtPoint(tileLayer,
                      new PointF(origin.X + cell.Width / 2f, origin.Y + cell.Height / 2f)), brush2),
                "tile: different source overwrote in place, not stacked");
            Check(RoomEdit.EraseTileCells(tileLayer, new[] { origin }) == 1, "tile: erase one cell");
            doc.Undo();
            Check(RoomEdit.AllTiles(doc.Room!).Count() == 1, "tile: undo restores baseline tile count");

            // 油漆桶：先刷 3x3 同图源区域，再用 brush2 从中心整片替换
            var fillOrigin = new Point(origin.X + cw * 3, origin.Y);
            var blockCells = RoomEdit.TileCellRect(fillOrigin,
                new Point(fillOrigin.X + cw * 2, fillOrigin.Y + ch * 2), cell);
            doc.PushUndo();
            Check(RoomEdit.PaintTileCells(gd.Data, tileLayer, brush, blockCells) == 9, "tile: paint 3x3 block");
            var fillCenter = new PointF(fillOrigin.X + cell.Width * 1.5f, fillOrigin.Y + cell.Height * 1.5f);
            Check(RoomEdit.FloodFillTiles(gd.Data, doc.Room!, tileLayer, brush2, fillCenter) == 9,
                "tile: bucket replaces connected 9 cells");
            doc.Undo();
            Check(RoomEdit.AllTiles(doc.Room!).Count() == 1, "tile: undo after bucket restores 1 tile");

            doc.PushUndo();
            Check(RoomEdit.PaintTileCells(gd.Data, tileLayer, brush, blockCells) == 9, "tile: repaint block for erase test");
            Check(RoomEdit.FloodEraseTiles(doc.Room!, tileLayer, fillCenter) == 9, "tile: bucket erase removes connected 9");
            doc.Undo();
            Check(RoomEdit.AllTiles(doc.Room!).Count() == 1, "tile: undo after bucket erase restores 1 tile");

            // 空白区域油漆桶：房间 320x240 / 格子尺寸得出格数上限，填满后应恰好是该连通空白格数
            doc.PushUndo();
            PointF emptyOrigin = default;
            for (int y = 0; y < roomH && emptyOrigin == default; y += ch)
                for (int x = 0; x < roomW && emptyOrigin == default; x += cw)
                    if (RoomEdit.TileAtPoint(tileLayer,
                            new PointF(x + cw / 2f, y + ch / 2f)) == null) emptyOrigin = new PointF(x + cw / 2f, y + ch / 2f);
            bool emptyHere = RoomEdit.TileAtPoint(tileLayer, emptyOrigin) == null;
            int filled = emptyHere ? RoomEdit.FloodFillTiles(gd.Data, doc.Room!, tileLayer, brush, emptyOrigin) : -1;
            Check(!emptyHere || filled > 0, "tile: bucket fills empty connected area");
            if (filled > 0) Check(RoomEdit.AllTiles(doc.Room!).Count() > 9, "tile: empty fill added tiles");
            doc.Undo();
            Check(RoomEdit.AllTiles(doc.Room!).Count() == 1, "tile: undo after empty bucket restores 1 tile");
            doc.ClearSelection();

            // ---------------- tile selection / clipboard / move (room_krisroom)
            // 注意：undo 会整体重建图块对象（快照存的是 Clone），所以每次 Undo 之后必须重新取引用。
            UndertaleRoom.Tile BaseTile() => RoomEdit.AllTiles(doc.Room!).First().Tile;
            doc.ClearTileSelection();
            Check(doc.TileSelectionCount == 0, "tile sel: starts empty");
            Check(RoomEdit.TilesInRect(tileLayer, new RectangleF(BaseTile().X, BaseTile().Y, 1, 1)).Count == 1,
                "tile sel: TilesInRect finds baseline tile");
            Check(RoomEdit.TilesInRect(tileLayer, new RectangleF(0, 0, roomW, roomH)).Count == 1,
                "tile sel: TilesInRect whole room = 1");
            Check(RoomEdit.TilesInRect(tileLayer, new RectangleF(roomW + 10, roomH + 10, 16, 16)).Count == 0,
                "tile sel: TilesInRect outside = 0");

            doc.SetTileSelection(new[] { BaseTile() });
            Check(doc.TileSelectionCount == 1 && doc.Selection.Count == 0, "tile sel: selecting tile clears instance selection");
            doc.ToggleTileSelection(BaseTile());
            Check(doc.TileSelectionCount == 0, "tile sel: toggle removes");
            doc.ToggleTileSelection(BaseTile());
            Check(doc.TileSelectionCount == 1, "tile sel: toggle adds back");
            Check(doc.SelectAllTiles(tileLayer) == 1, "tile sel: select all = 1");
            Check(doc.CopyTileSelection() == 1 && doc.TileClipboardCount == 1, "tile sel: copy 1");
            Check(doc.TileClipboardCellSize.Width > 1 && doc.TileClipboardCellSize.Height > 1, "tile sel: clipboard cell size");
            Check(doc.TileSelection[0].X == BaseTile().X, "tile sel: select all keeps original coords");

            var anchor = new Point(BaseTile().X + cw, BaseTile().Y);
            var pastedTiles = doc.PasteTiles(tileLayer, anchor.X, anchor.Y);
            Check(pastedTiles.Count == 1, "tile sel: paste 1");
            Check(RoomEdit.AllTiles(doc.Room!).Count() == 2, "tile sel: room tiles 2 after paste");
            Check(doc.TileSelectionCount == 1 && ReferenceEquals(doc.TileSelection[0], pastedTiles[0]),
                "tile sel: pasted tile becomes selection");
            doc.Undo();
            Check(RoomEdit.AllTiles(doc.Room!).Count() == 1, "tile sel: undo paste restores 1");

            // 多块复制：基线 + 再粘一块 → 选中两块复制 → 粘贴得到两块
            doc.SetTileSelection(new[] { BaseTile() });
            doc.CopyTileSelection();
            var two = doc.PasteTiles(tileLayer, BaseTile().X + cw, BaseTile().Y);
            doc.SetTileSelection(new[] { BaseTile(), two[0] });
            Check(doc.CopyTileSelection() == 2 && doc.TileClipboardCount == 2, "tile sel: copy 2 tiles");
            var pair = doc.PasteTiles(tileLayer, BaseTile().X, BaseTile().Y + ch);
            Check(pair.Count == 2, "tile sel: paste 2 tiles");
            Check(RoomEdit.AllTiles(doc.Room!).Count() == 4, "tile sel: room tiles 4");
            doc.Undo();
            Check(RoomEdit.AllTiles(doc.Room!).Count() == 2, "tile sel: undo paste 2 restores 2");
            doc.Undo();
            Check(RoomEdit.AllTiles(doc.Room!).Count() == 1, "tile sel: undo paste 1 restores 1");

            // 删除 / 剪切
            doc.SetTileSelection(new[] { BaseTile() });
            Check(doc.DeleteTileSelection() == 1, "tile sel: delete selection 1");
            Check(RoomEdit.AllTiles(doc.Room!).Count() == 0 && doc.TileSelectionCount == 0, "tile sel: delete leaves 0");
            doc.Undo();
            Check(RoomEdit.AllTiles(doc.Room!).Count() == 1, "tile sel: undo delete restores 1");
            doc.SetTileSelection(new[] { BaseTile() });
            Check(doc.CutTileSelection() == 1, "tile sel: cut 1");
            Check(RoomEdit.AllTiles(doc.Room!).Count() == 0 && doc.TileClipboardCount == 1, "tile sel: cut removes and fills clipboard");
            Check(doc.PasteTiles(tileLayer, cw * 2, ch * 2).Count == 1, "tile sel: paste after cut");
            doc.Undo();
            doc.Undo();
            Check(RoomEdit.AllTiles(doc.Room!).Count() == 1, "tile sel: undo cut/paste restores 1");

            // 移动（拖动）等价：改坐标 + undo 还原
            doc.SetTileSelection(new[] { BaseTile() });
            doc.PushUndo();
            int bx = BaseTile().X, by = BaseTile().Y;
            BaseTile().X = bx + cw * 2;
            BaseTile().Y = by + ch;
            doc.Undo();
            var moved = BaseTile();
            Check(moved.X == bx && moved.Y == by, "tile sel: undo restores moved position");
            Check(doc.TileSelectionCount == 0, "tile sel: undo clears stale tile selection");

            // 与实例选择互斥
            doc.SetTileSelection(new[] { BaseTile() });
            var anyInst = RoomEdit.AllInstances(doc.Room!).First().Inst;
            doc.SelectOnly(anyInst);
            Check(doc.TileSelectionCount == 0 && doc.Selection.Count == 1, "tile sel: instance selection clears tile selection");
            doc.SelectOnly(null);
            Check(doc.TileSelectionCount == 0, "tile sel: cleared");

            // ---------------- 资源选择器：任意精灵整帧当瓦片笔刷 / 更换背景层精灵
            doc.ClearTileSelection();
            UndertaleSprite? brushSpr = null;
            foreach (var spr in gd.Sprites)
            {
                if (spr.Textures == null || spr.Textures.Count == 0) continue;
                if (spr.Textures[0]?.Texture == null) continue;
                if (GameData.Name(spr.Name) == "bg_myroom") { brushSpr = spr; break; }
                brushSpr ??= spr;
            }
            Check(brushSpr != null, "asset: found a sprite usable as tile brush");
            if (brushSpr != null)
            {
                var sprBrush = RoomEdit.MakeTileBrush(gd.Data, brushSpr);
                var sprSize = RoomEdit.TileWorldSize(sprBrush);
                Check(ReferenceEquals(sprBrush.SpriteDefinition, brushSpr), "asset: brush uses chosen sprite");
                Check(sprBrush.Tpag != null, "asset: brush has texture page item");
                Check(sprSize.Width >= 1 && sprSize.Height >= 1, "asset: brush cell size >= 1");
                Check(RoomEdit.SameTileSource(sprBrush, RoomEdit.CloneTile(sprBrush)), "asset: cloned brush keeps source");
                doc.PushUndo();
                int painted = RoomEdit.PaintTileCells(gd.Data, tileLayer, sprBrush,
                    new[] { new Point((int)(cw * 5), 0), new Point((int)(cw * 5) + (int)Math.Round(cell.Width), 0) });
                Check(painted == 2, "asset: paint 2 cells with custom sprite brush");
                Check(RoomEdit.AllTiles(doc.Room!).Count() == 3, "asset: room tiles 3 after custom paint");
                doc.Undo();
                Check(RoomEdit.AllTiles(doc.Room!).Count() == 1, "asset: undo custom paint restores 1");
            }

            // ---------------- 瓦片调色板：从精灵帧里切子区域当笔刷（大 tileset 只取一格）
            if (brushSpr != null)
            {
                var tp = brushSpr.Textures![0].Texture!;
                int fw = (int)tp.SourceWidth, fh = (int)tp.SourceHeight;
                int rw = Math.Min(16, fw - 2), rh = Math.Min(12, fh - 3);
                Check(rw >= 1 && rh >= 1, "region: sprite big enough to slice");
                if (rw >= 1 && rh >= 1)
                {
                    var rect = new Rectangle(2, 3, rw, rh);
                    var sub = RoomEdit.MakeTileBrush(gd.Data, brushSpr, rect);
                    Check(sub.SourceX == 2 && sub.SourceY == 3, "region brush: source offset applied");
                    Check((int)sub.Width == rw && (int)sub.Height == rh, "region brush: sub size applied");
                    Check(RoomEdit.SameTileSource(sub, RoomEdit.CloneTile(sub)), "region brush: clone keeps sub source");
                    var clamped = RoomEdit.MakeTileBrush(gd.Data, brushSpr, new Rectangle(fw + 5, fh + 7, 999, 999));
                    Check(clamped.SourceX == fw - 1 && clamped.SourceY == fh - 1, "region brush: origin clamped to frame");
                    Check(clamped.Width == 1 && clamped.Height == 1, "region brush: size clamped to 1");
                    doc.PushUndo();
                    int paintedSub = RoomEdit.PaintTileCells(gd.Data, tileLayer, sub, new[] { new Point((int)(cw * 6), 0) });
                    Check(paintedSub == 1, "region brush: paint 1 cell");
                    Check(RoomEdit.AllTiles(doc.Room!).Count() == 2, "region brush: room tiles 2");
                    doc.Undo();
                    Check(RoomEdit.AllTiles(doc.Room!).Count() == 1, "region brush: undo restores 1");
                }
            }

            // ---------------- 瓦片调色板网格：整帧按固定单元格切格（点格子选笔刷）
            {
                var grid = RoomEdit.TilePaletteGrid(200, 520, 16, 16);
                Check(grid.Width == 13 && grid.Height == 33, "palette: grid 13x33 for 200x520 / 16");
                var c00 = RoomEdit.TilePaletteCellRect(200, 520, 16, 16, 0, 0);
                Check(c00 == new Rectangle(0, 0, 16, 16), "palette: first cell rect");
                var lastCell = RoomEdit.TilePaletteCellRect(200, 520, 16, 16, 12, 32);
                Check(lastCell == new Rectangle(192, 512, 8, 8), "palette: last cell clipped to frame edge");
                Check(RoomEdit.TilePaletteCellRect(200, 520, 16, 16, 13, 0) == Rectangle.Empty, "palette: col out of range empty");
                Check(RoomEdit.TilePaletteCellRect(200, 520, 16, 16, 0, 33) == Rectangle.Empty, "palette: row out of range empty");
                Check(RoomEdit.TilePaletteCellRect(200, 520, 16, 16, -1, 0) == Rectangle.Empty, "palette: negative cell empty");
                Check(RoomEdit.TilePaletteCellAt(200, 520, 16, 16, 199, 519) == new Point(12, 32), "palette: hit test last cell");
                Check(RoomEdit.TilePaletteCellAt(200, 520, 16, 16, 16, 16) == new Point(1, 1), "palette: hit test cell boundary");
                Check(RoomEdit.TilePaletteCellAt(200, 520, 16, 16, 200, 0) == new Point(-1, -1), "palette: hit test outside frame");
                Check(RoomEdit.TilePaletteCellRect(200, 520, 0, 0, 3, 4) == new Rectangle(3, 4, 1, 1), "palette: cell size clamped to >=1");
                // 单元格比帧还大：首格被帧宽高裁齐（200x520 帧 + 512 格 → 200x512，不是 200x520）
                Check(RoomEdit.TilePaletteCellRect(200, 520, 512, 512, 0, 0) == new Rectangle(0, 0, 200, 512),
                      "palette: cell larger than frame width uses frame width");
                Check(RoomEdit.TilePaletteCellRect(100, 100, 512, 512, 0, 0) == new Rectangle(0, 0, 100, 100),
                      "palette: cell larger than frame uses frame");
                if (brushSpr != null)
                {
                    // 用笔刷精灵自己的帧尺寸算尾格，别假设帧一定是 200x520（bg_myroom 实测 320x240）
                    var palFrame = brushSpr.Textures![0].Texture!;
                    int pfw = (int)palFrame.SourceWidth, pfh = (int)palFrame.SourceHeight;
                    var palGrid = RoomEdit.TilePaletteGrid(pfw, pfh, 16, 16);
                    var palCell = RoomEdit.TilePaletteCellRect(pfw, pfh, 16, 16, palGrid.Width - 1, palGrid.Height - 1);
                    var palTile = RoomEdit.MakeTileBrush(gd.Data, brushSpr, palCell);
                    Check(palTile.SourceX == palCell.X && palTile.SourceY == palCell.Y, "palette: brush source from cell");
                    Check((int)palTile.Width == palCell.Width && (int)palTile.Height == palCell.Height,
                          "palette: brush size from clipped cell");
                }
            }

            // ---------------- 瓦片圆形/椭圆：格子集合纯函数（Loenn circle/ellipse 对齐）
            {
                var cs = new SizeF(16, 16);
                var dot = RoomEdit.TileCellCircle(new Point(0, 0), new Point(0, 0), cs);
                Check(dot.Count == 1 && dot[0] == new Point(0, 0), "circle: zero radius is a single cell");
                var c2 = RoomEdit.TileCellCircle(new Point(0, 0), new Point(32, 0), cs);
                Check(c2.Count == 13, "circle: radius 2 has 13 cells");
                var c2d = RoomEdit.TileCellCircle(new Point(0, 0), new Point(32, 32), cs);
                Check(c2d.Count == 13, "circle: diagonal drag keeps radius");
                var ell = RoomEdit.TileCellEllipse(new Point(0, 0), new Point(64, 32), cs);
                Check(ell.Count == 7, "ellipse: 5x3 bounding box has 7 cells");
                var ellR = RoomEdit.TileCellEllipse(new Point(64, 32), new Point(0, 0), cs);
                Check(ellR.Count == 7 && ellR.All(p => ell.Contains(p)), "ellipse: reversed drag same cells");
                var thinW = RoomEdit.TileCellEllipse(new Point(0, 0), new Point(0, 32), cs);
                Check(thinW.Count == 3 && thinW.All(p => p.X == 0), "ellipse: degenerate width is a line");
                var thinH = RoomEdit.TileCellEllipse(new Point(0, 0), new Point(32, 0), cs);
                Check(thinH.Count == 3 && thinH.All(p => p.Y == 0), "ellipse: degenerate height is a line");

                // 接线：圆形格子交给瓦片 API 要真写进房间，且能一步撤销
                var centerCell = new Point(cw * 3, ch * 2);
                var edgeCell = new Point(centerCell.X + cw, centerCell.Y);
                var circleCells = RoomEdit.TileCellCircle(centerCell, edgeCell, cell);
                Check(circleCells.Count == 5, "circle: radius 1 has 5 cells");
                doc.PushUndo();
                int changedCircle = RoomEdit.PaintTileCells(gd.Data, tileLayer, brush, circleCells);
                Check(changedCircle >= 1, "circle: paint cells into room");
                doc.Undo();
                Check(RoomEdit.AllTiles(doc.Room!).Count() == 1, "circle: undo restores 1");
            }

            // ---------------- 房间尺寸：走 SetRoomSize（撤销 / 脏标记 / 渲染缓存 / 夹取）
            {
                var sized = doc.Room!;
                uint oldW = sized.Width, oldH = sized.Height;
                var beforeRender = doc.RenderRoom(sized);
                Check(beforeRender != null && beforeRender.Width == oldW && beforeRender.Height == oldH,
                      "room size: render matches original size");
                Check(doc.SetRoomSize(oldW + 80, oldH + 40), "room size: applied");
                Check(sized.Width == oldW + 80 && sized.Height == oldH + 40, "room size: fields changed");
                var afterRender = doc.RenderRoom(sized);
                Check(afterRender != null && afterRender.Width == oldW + 80 && afterRender.Height == oldH + 40,
                      "room size: render follows new size");
                Check(doc.Dirty, "room size: marks dirty");
                Check(!doc.SetRoomSize(sized.Width, sized.Height), "room size: same size is a no-op");
                Check(doc.SetRoomSize(1, 1), "room size: out-of-range accepted then clamped");
                Check(sized.Width == 16 && sized.Height == 16, "room size: clamped to 16x16");
                var tinyRender = doc.RenderRoom(sized);
                Check(tinyRender != null && tinyRender.Width == 16 && tinyRender.Height == 16, "room size: render 16x16");
                doc.Undo();
                Check(sized.Width == oldW + 80 && sized.Height == oldH + 40, "room size: undo restores 416x280");
                doc.Undo();
                Check(sized.Width == oldW && sized.Height == oldH, "room size: undo restores original");
                var restoredRender = doc.RenderRoom(sized);
                Check(restoredRender != null && restoredRender.Width == oldW && restoredRender.Height == oldH,
                      "room size: render back to original");
            }

// ---------------- 实例对齐 / 分布（Loenn selection align/distribute；包围盒按图像帧×缩放）
            {
                List<UndertaleRoom.GameObject> TakeInst(int n) =>
                    RoomEdit.AllInstances(doc.Room!).Select(t => t.Inst).Take(n).ToList();
                List<RectangleF> BoxesOf(List<UndertaleRoom.GameObject> l) =>
                    l.Select(i => doc.InstanceBounds(i)).ToList();

                var s4 = TakeInst(4);
                doc.SelectMany(s4, false);
                var b4 = BoxesOf(s4);
                Check(b4.All(b => b.Width >= 1 && b.Height >= 1), "align: bounds positive");

                // 旋转参与包围盒（外接矩形）：长 32×宽 16 的实例转 90 度后宽高互换、中心不动
                {
                    var ri = s4[0];
                    float sx = ri.ScaleX, sy = ri.ScaleY, rr = ri.Rotation;
                    ri.ScaleX = 2f; ri.ScaleY = 1f; ri.Rotation = 0f;
                    var r0 = doc.InstanceBounds(ri);
                    ri.Rotation = 90f;
                    var r90 = doc.InstanceBounds(ri);
                    Check(Math.Abs(r90.Width - r0.Height) <= 1 && Math.Abs(r90.Height - r0.Width) <= 1,
                          "align: rotation-aware bounds (90deg swaps w/h)");
                    Check(Math.Abs((r0.Left + r0.Width / 2f) - (r90.Left + r90.Width / 2f)) <= 1 &&
                          Math.Abs((r0.Top + r0.Height / 2f) - (r90.Top + r90.Height / 2f)) <= 1,
                          "align: rotation keeps bounds center");
                    ri.ScaleX = sx; ri.ScaleY = sy; ri.Rotation = rr;
                }
                float leftTarget = b4.Min(b => b.Left);
                var xBefore = s4.ToDictionary(i => i.InstanceID, i => i.X);
                Check(doc.AlignSelection(EditorDocument.AlignMode.Left) > 0, "align: left moved");
                Check(BoxesOf(TakeInst(4)).All(b => Math.Abs(b.Left - leftTarget) <= 1), "align: left edges equal");
                doc.Undo();
                var xRestored = RoomEdit.AllInstances(doc.Room!).ToDictionary(t => t.Inst.InstanceID, t => t.Inst.X);
                Check(xBefore.All(kv => xRestored.TryGetValue(kv.Key, out int x) && x == kv.Value),
                      "align: undo restores positions");

                doc.SelectMany(TakeInst(3), false);
                var b3 = BoxesOf(TakeInst(3));
                float centerX = (b3.Min(b => b.Left) + b3.Max(b => b.Right)) / 2f;
                Check(doc.AlignSelection(EditorDocument.AlignMode.CenterX) > 0, "align: centerX moved");
                Check(BoxesOf(TakeInst(3)).All(b => Math.Abs(b.Left + b.Width / 2f - centerX) <= 1),
                      "align: centerX centered");
                doc.Undo();

                doc.SelectMany(TakeInst(2), false);
                float rightTarget = BoxesOf(TakeInst(2)).Max(b => b.Right);
                Check(doc.AlignSelection(EditorDocument.AlignMode.Right) > 0, "align: right moved");
                Check(BoxesOf(TakeInst(2)).All(b => Math.Abs(b.Right - rightTarget) <= 1), "align: right edges equal");
                doc.Undo();

                doc.SelectMany(TakeInst(3), false);
                float topTarget = BoxesOf(TakeInst(3)).Min(b => b.Top);
                Check(doc.AlignSelection(EditorDocument.AlignMode.Top) > 0, "align: top moved");
                Check(BoxesOf(TakeInst(3)).All(b => Math.Abs(b.Top - topTarget) <= 1), "align: tops equal");
                doc.Undo();

                doc.SelectMany(TakeInst(2), false);
                float bottomTarget = BoxesOf(TakeInst(2)).Max(b => b.Bottom);
                Check(doc.AlignSelection(EditorDocument.AlignMode.Bottom) > 0, "align: bottom moved");
                Check(BoxesOf(TakeInst(2)).All(b => Math.Abs(b.Bottom - bottomTarget) <= 1), "align: bottoms equal");
                doc.Undo();

                doc.SelectMany(TakeInst(2), false);
                var bcy = BoxesOf(TakeInst(2));
                float centerY = (bcy.Min(b => b.Top) + bcy.Max(b => b.Bottom)) / 2f;
                Check(doc.AlignSelection(EditorDocument.AlignMode.CenterY) > 0, "align: centerY moved");
                Check(BoxesOf(TakeInst(2)).All(b => Math.Abs(b.Top + b.Height / 2f - centerY) <= 1),
                      "align: centerY centered");
                doc.Undo();

                doc.SelectMany(TakeInst(3), false);
                var centersBefore = BoxesOf(TakeInst(3))
                    .Select(b => b.Left + b.Width / 2f).OrderBy(v => v).ToList();
                bool alreadyEven = centersBefore.Count == 3 &&
                                   Math.Abs((centersBefore[1] - centersBefore[0]) -
                                            (centersBefore[2] - centersBefore[1])) <= 2;
                int dxMoved = doc.AlignSelection(EditorDocument.AlignMode.DistributeX);
                Check(dxMoved > 0 || alreadyEven, "align: distributeX moved or already even");
                var cxAfter = BoxesOf(TakeInst(3))
                    .Select(b => b.Left + b.Width / 2f).OrderBy(v => v).ToList();
                Check(Math.Abs((cxAfter[1] - cxAfter[0]) - (cxAfter[2] - cxAfter[1])) <= 2,
                      "align: distributeX equal gaps");
                doc.Undo();

                doc.SelectMany(TakeInst(3), false);
                doc.AlignSelection(EditorDocument.AlignMode.DistributeY);
                doc.Undo();
                Check(RoomEdit.AllInstances(doc.Room!).Count() == baseInstances,
                    $"align: undo leaves {baseInstances} instances");
                doc.ClearSelection();
            }
            var bgSprLayer = doc.Room!.Layers?.FirstOrDefault(l => l.BackgroundData?.Sprite != null);
            Check(bgSprLayer != null, "asset: found background layer with sprite");
            if (bgSprLayer != null)
            {
                var origSpr = bgSprLayer.BackgroundData!.Sprite;
                UndertaleSprite? otherSpr = gd.Sprites.FirstOrDefault(s => !ReferenceEquals(s, origSpr) && s.Textures != null && s.Textures.Count > 0);
                Check(otherSpr != null, "asset: found another sprite for background");
                if (otherSpr != null)
                {
                    Check(doc.SetBackgroundSprite(bgSprLayer, otherSpr), "asset: background sprite updated");
                    Check(ReferenceEquals(bgSprLayer.BackgroundData!.Sprite, otherSpr), "asset: background sprite applied");
                    doc.Undo();
                    Check(ReferenceEquals(bgSprLayer.BackgroundData!.Sprite, origSpr), "asset: undo restores background sprite");
                }
            }

            int baseRooms = gd.Rooms.Count();
            // 新房间继承当前房间（withContent=true），期望值同样取实测模板，而不是写死 8/26。
            int inheritedLayers = doc.Room!.Layers?.Count ?? 0;
            int inheritedInstances = RoomEdit.AllInstances(doc.Room!).Count();

            var created = doc.NewRoom("ntl_doc_room", true);
            Check(created != null, "new room returned");
            Check(gd.Rooms.Count() == baseRooms + 1, "new room: count +1");
            Check(ReferenceEquals(doc.Room, created), "new room: became current");
            Check(doc.Room!.Layers?.Count == inheritedLayers, $"new room: layers {inheritedLayers} inherited");
            Check(RoomEdit.AllInstances(doc.Room!).Count() == inheritedInstances,
                $"new room: instances {inheritedInstances} inherited");
            Check(roomsChanged >= 1, "new room: RoomsChanged fired");

            Check(doc.RenameRoom("ntl_doc_renamed"), "rename room");
            Check(doc.RoomName() == "ntl_doc_renamed", "rename room: applied");
            Check(gd.FindRoom("ntl_doc_renamed") != null, "rename room: findable by new name");
            Check(gd.FindRoom("ntl_doc_room") == null, "rename room: old name gone");

            var blank = doc.NewRoom("ntl_doc_blank", false);
            Check(blank != null && RoomEdit.AllInstances(blank!).Count() == 0, "blank room: 0 instances");
            Check(doc.DeleteRoom(), "delete blank room");
            Check(gd.Rooms.Count() == baseRooms + 1, "delete blank: count");

            doc.OpenRoom(created);
            var dup = doc.DuplicateRoom(null);
            Check(dup != null && gd.Rooms.Count() == baseRooms + 2, "duplicate: count +2");
            Check(doc.RoomName(dup).EndsWith("_copy"), "duplicate: default name _copy");

            int layersBefore = doc.Room!.Layers!.Count;
            var nl = doc.AddLayer(false, -30);
            Check(nl != null && doc.Room.Layers.Count == layersBefore + 1, "layer add: count +1");
            Check(nl!.LayerDepth == -30, "layer add: depth -30");
            Check(doc.RenameLayer(nl, "ntl_doc_layer"), "layer rename");
            Check(GameData.Name(nl.LayerName) == "ntl_doc_layer", "layer rename: applied");
            int idx0 = doc.Room.Layers.IndexOf(nl);
            Check(doc.MoveLayer(nl, 1), "layer move down");
            Check(doc.Room.Layers.IndexOf(nl) > idx0, "layer move: index increased");
            Check(doc.MoveLayer(nl, -1), "layer move back up");
            Check(doc.RemoveLayer(nl) && doc.Room.Layers.Count == layersBefore, "layer remove");

            Check(doc.DeleteRoom(), "delete duplicate");
            Check(gd.Rooms.Count() == baseRooms + 1, "delete duplicate: count");
            Check(gd.FindRoom("ntl_doc_renamed") != null, "renamed room survived duplicate delete");
            doc.OpenRoom(created);
            Check(doc.DeleteRoom(), "delete renamed room");
            Check(gd.Rooms.Count() == baseRooms, "back to baseline rooms");

            dst = PrepareOutFile(dst);
            var saveResult = doc.Save(dst);
            Check(saveResult.Ok, "save dst");
            var gd2 = new GameData(dst);
            Check(gd2.Rooms.Count() == baseRooms, "saved reload: rooms");
            Check(gd2.FindRoom("ntl_doc_renamed") == null, "saved reload: no test rooms");
            gd2.Dispose();
            Console.WriteLine(fails == 0 ? "DOCUMENT-TEST PASS" : $"DOCUMENT-TEST FAIL ({fails})");
            return fails == 0 ? 0 : 1;
        }
        if (args[0] == "mapedit-test")
        {
            Need("mapedit-test <src data.win> <workDir>", 3);
            NoExtra("mapedit-test <src data.win> <workDir>", 3);
            Console.WriteLine("  src 只读；fixture 只建在 workDir 下，workDir 不得位于真实游戏根内。");
            CheckSuiteSource(args[1], "MAPEDIT-TEST");
            CheckSuiteWorkDir(args[2], "MAPEDIT-TEST");
            return MapeditSelfTest.Run(args[1], args[2]);
        }
        if (args[0] == "default-chapter")
        {
            NoExtra("default-chapter [auto|chapter1..chapter5]", 2);
            string sfile = StartupSettings.DefaultFile;
            if (args.Length > 1)
            {
                string raw = args[1].Trim();
                string lower = raw.ToLowerInvariant();
                bool valid = lower is "auto" or "default" or "last" || StartupSettings.NormalizeChapter(raw) != StartupSettings.AutoChapter;
                if (!valid)
                {
                    throw new CliError(2, "[错误] 无法识别章节: " + raw + "（可用: auto 或 chapter1..chapter5）");
                }
                var st = StartupSettings.Load(sfile);
                st.DefaultChapter = StartupSettings.NormalizeChapter(raw);
                st.Save(sfile);
                Console.WriteLine("默认启动章节已设为: " + StartupSettings.Describe(st.DefaultChapter) + "（" + st.DefaultChapter + "）");
                Console.WriteLine("设置文件: " + sfile);
                var resolved = StartupSettings.ResolveTarget(GameRootDir(), RecentStore.LastPath);
                Console.WriteLine("下次启动将打开: " + resolved.Path + "（来源: " + resolved.Source + (resolved.IsBaseline ? "，只读" : "") + "）");
                return 0;
            }
            PrintStartupState();
            return 0;
        }
        if (args[0] == "chapters")
        {
            NoExtra("chapters", 1);
            PrintChapterTable();
            var resolved = StartupSettings.ResolveTarget(GameRootDir(), RecentStore.LastPath);
            Console.WriteLine();
            Console.WriteLine("当前解析目标: " + resolved.Path + "（来源: " + resolved.Source + (resolved.IsBaseline ? "，只读" : "") + "）");
            return 0;
        }
        if (args[0] == "vanilla-scan-test")
        {
            Need("vanilla-scan-test <data.win> [outJson]", 2);
            if (args.Length > 3) NoExtra("vanilla-scan-test <data.win> [outJson]", 3);
            Console.WriteLine("  只读：反编译对象事件、抽 global.msg 对白行、做条件三态判定、解析 lang 文本。");
            CheckSuiteSource(args[1], "VANILLA-SCAN-TEST");
            return VanillaScanSelfTest.Run(args[1], args.Length > 2 ? args[2] : null);
        }
        if (args[0] == "vanilla-scan")
        {
            Need("vanilla-scan <data.win> <object> [room] [x] [y] [outJson]", 3);
            if (args.Length > 7) NoExtra("vanilla-scan <data.win> <object> [room] [x] [y] [outJson]", 7);
            CheckSuiteSource(args[1], "VANILLA-SCAN");
            using var gd = new GameData(args[1]);
            var obj = gd.Data.GameObjects.FirstOrDefault(o => o?.Name?.Content == args[2]);
            if (obj == null)
            {
                Console.WriteLine("对象不存在: " + args[2]);
                return 2;
            }
            string roomName = args.Length > 3 ? args[3] : "room_krisroom";
            var room = gd.Data.Rooms.FirstOrDefault(r => r?.Name?.Content == roomName);
            int roomIndex = room != null ? gd.Data.Rooms.IndexOf(room) : -1;
            float sx = float.NaN, sy = float.NaN;   // 不给坐标 = 未知（x/y 条件保持 ?）
            if (args.Length > 4 && !float.TryParse(args[4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out sx))
                throw new CliError(2, "x 坐标不是数字: " + args[4]);
            if (args.Length > 5 && !float.TryParse(args[5], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out sy))
                throw new CliError(2, "y 坐标不是数字: " + args[5]);
            var vc = new VanillaCode(gd.Data, Path.GetFullPath(args[1]));
            var info = vc.ScanObject(obj, roomIndex, sx, sy);
            string coordText = (float.IsNaN(sx) || float.IsNaN(sy)) ? "坐标未知" : $"{sx},{sy}";
            Console.WriteLine($"VANILLA-SCAN {args[2]} @ {roomName}[{roomIndex}] ({coordText})");
            Console.WriteLine("  " + vc.LangStatus);
            Console.WriteLine($"  事件={info.Events.Count} 有对白事件={info.DialogueEventCount} 对白行={info.DialogueLineCount}（可阅读 {info.DisplayableLineCount}；运行期文本/控制码 {info.RuntimeTextLineCount}） 截断={(info.Events.Any(e => e.Truncated) ? "是" : "否")} 继承行={info.InheritedDialogueLineCount}");
            Console.WriteLine("  父对象: " + (info.ParentChain.Count > 0 ? string.Join(" → ", info.ParentChain) : "(无)"));
            if (info.InteractionEvent != null)
                Console.WriteLine($"  交互事件: {info.InteractionEvent.Name}{(info.InteractionEvent.Inherited ? "（继承自 " + info.InteractionEvent.OwnerObject + "）" : "")} 对白行={info.InteractionEvent.Lines.Count}");
            int mscLines = info.Events.SelectMany(e => e.Lines).Count(l => l.MscNo > 0);
            int argLines = info.Events.SelectMany(e => e.Lines).Count(l => l.RequiresArgs);
            int unknownTruth = info.Events.SelectMany(e => e.Lines).Count(l => l.Truth == TriTruth.Unknown);
            Console.WriteLine($"  msc 行={mscLines} 含参数行={argLines} 未知条件行={unknownTruth}");
            foreach (var l in info.Events.SelectMany(e => e.Lines).Where(l => l.Truth != TriTruth.False).Take(30))
            {
                var shown = l.DisplayText;
                var txt = shown.Length > 60 ? shown[..60] + "…" : shown;
                Console.WriteLine($"  [{l.EventName}] msg[{l.MsgIndex}] msc={l.MscNo} truth={l.Truth} cond={(l.Condition.Length > 70 ? l.Condition[..70] + "…" : l.Condition)} :: {txt}");
            }
            if (args.Length > 6)
            {
                var outPath = Path.GetFullPath(args[6]);
                var json = VanillaScanSelfTest.BuildScanJson(vc, info, args[1], roomIndex, sx, sy, roomName);
                var dir = Path.GetDirectoryName(outPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(outPath, json + Environment.NewLine, new UTF8Encoding(false));
                Console.WriteLine("  JSON: " + outPath + " (" + json.Length + " 字符)");
            }
            return 0;
        }
        if (args[0] == "settings-test")
        {
            Need("settings-test <workDir>", 2);
            NoExtra("settings-test <workDir>", 2);
            CheckSuiteWorkDir(args[1], "SETTINGS-TEST");
            return StartupSettingsSelfTest.Run(args[1]);
        }
        if (args[0] == "native-tiles-test")
        {
            Need("native-tiles-test <src data.win> <workDir>", 3);
            NoExtra("native-tiles-test <src data.win> <workDir>", 3);
            Console.WriteLine("  src 只读；fixture 只建在 workDir 下，workDir 不得位于真实游戏根内。");
            CheckSuiteSource(args[1], "NATIVE-TILES-TEST");
            CheckSuiteWorkDir(args[2], "NATIVE-TILES-TEST");
            return NativeTilesSelfTest.Run(args[1], args[2]);
        }
        if (args[0] == "resource-browser-test")
        {
            Need("resource-browser-test <data.win>", 2);
            NoExtra("resource-browser-test <data.win>", 2);
            Console.WriteLine("  只读：验证资源浏览器能列出全部资源类别（房间/对象/精灵/背景/字体/音效/脚本代码/着色器/序列/时间线/纹理页/路径）。");
            CheckSuiteSource(args[1], "RESOURCE-BROWSER-TEST");
            return ResourceBrowserSelfTest.Run(args[1]);
        }
        if (args[0] == "room-stat")
        {
            Need("room-stat <data.win> <room>", 3);
            NoExtra("room-stat <data.win> <room>", 3);
            var gd = new GameData(args[1]);
            var room = gd.FindRoom(args[2]);
            if (room == null)
            {
                Console.WriteLine($"ROOM-STAT FAIL: room '{args[2]}' not found");
                gd.Dispose();
                throw new CliError(2);
            }
            int legacyTileCount = RoomEdit.AllTiles(room).Count();
            int nativeTileCount = room.Layers?.Sum(l => NativeTiles.CountNonZero(l)) ?? 0;
            Console.WriteLine($"room {GameData.Name(room.Name)} size={room.Width}x{room.Height} layers={room.Layers?.Count ?? 0} " +
                              $"instances={RoomEdit.AllInstances(room).Count()} tiles={legacyTileCount + nativeTileCount} " +
                              $"legacyTiles={legacyTileCount} nativeTiles={nativeTileCount}");
            var instColors = RoomEdit.AllInstances(room)
                .Where(t => t.Inst.Color != 0xFFFFFFFF)
                .GroupBy(t => t.Inst.Color)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .Select(g => $"#{g.Key & 0xFFFFFF:X6}:{g.Count()}")
                .ToList();
            if (instColors.Count > 0) Console.WriteLine($"  instColor[{string.Join(",", instColors)}]");
            if (room.Layers != null)
            {
                for (int i = 0; i < room.Layers.Count; i++)
                {
                    var l = room.Layers[i];
                    var bg = l.BackgroundData;
                    string extra = bg == null
                        ? ""
                        : $" bg[sprite={GameData.Name(bg.Sprite?.Name)} visible={bg.Visible} tiledH={bg.TiledHorizontally} " +
                          $"tiledV={bg.TiledVertically} stretch={bg.Stretch} color=0x{bg.Color:X8}]";
                    string tileInfo = "";
                    var legacyTiles = l.AssetsData?.LegacyTiles;
                    if (legacyTiles != null && legacyTiles.Count > 0)
                    {
                        var groups = legacyTiles
                            .GroupBy(t => GameData.Name(t.ObjectDefinition?.Name))
                            .OrderByDescending(g => g.Count())
                            .ThenBy(g => g.Key)
                            .Select(g => g.Key + ":" + g.Count());
                        tileInfo = $" tiles={legacyTiles.Count} src[{string.Join(",", groups)}]";
                        var ft = legacyTiles[^1];
                        tileInfo += $" last[w={ft.Width} h={ft.Height} sx={ft.SourceX} sy={ft.SourceY} " +
                                    $"color=0x{ft.Color:X8} scale={ft.ScaleX}x{ft.ScaleY}]";
                    }
                    var native = l.TilesData;
                    if (native != null)
                    {
                        var nbg = native.Background;
                        tileInfo += $" nativeTiles={NativeTiles.CountNonZero(l)} native[bg={GameData.Name(nbg?.Name)} " +
                                    $"grid={native.TilesX}x{native.TilesY} tile={nbg?.GMS2TileWidth}x{nbg?.GMS2TileHeight} " +
                                    $"rows={native.TileData?.Length ?? 0}]";
                    }
                    Console.WriteLine($"  [{i}] {GameData.Name(l.LayerName)} type={l.LayerType} depth={l.LayerDepth} " +
                                      $"offset=({l.XOffset},{l.YOffset}) visible={l.IsVisible}{extra}{tileInfo}");
                }
            }
            gd.Dispose();
            return 0;
        }
        if (args[0] == "room-inst")
        {
            Need("room-inst <data.win> <room>", 3);
            NoExtra("room-inst <data.win> <room>", 3);
            var gd = new GameData(args[1]);
            var room = gd.FindRoom(args[2]);
            if (room == null)
            {
                Console.WriteLine($"ROOM-INST FAIL: room '{args[2]}' not found");
                gd.Dispose();
                throw new CliError(2);
            }
            var list = RoomEdit.AllInstances(room).ToList();
            Console.WriteLine($"room {GameData.Name(room.Name)} instances={list.Count}");
            foreach (var (_, inst) in list)
                Console.WriteLine($"  inst id={inst.InstanceID} x={inst.X} y={inst.Y} " +
                                  $"spr={GameData.Name(inst.ObjectDefinition?.Name)} " +
                                  $"scale={inst.ScaleX}x{inst.ScaleY} color=0x{inst.Color:X8}");
            gd.Dispose();
            return 0;
        }
        if (args[0] == "stress-test")
        {
            Need("stress-test <src data.win> <dst data.win> [room]", 3);
            NoExtra("stress-test <src data.win> <dst data.win> [room]", 4);
            var src = args[1];
            var dst = PrepareOutFile(args[2]);
            string roomName = args.Length > 3 ? args[3] : "room_town_north";
            int fails = 0;
            void Check(bool ok, string label)
            {
                Console.WriteLine((ok ? "PASS " : "FAIL ") + label);
                if (!ok) fails++;
            }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var gd = new GameData(src);
            var room = gd.FindRoom(roomName);
            if (room == null)
            {
                Console.WriteLine("STRESS-TEST FAIL: room '" + roomName + "' not found（可用 rooms 命令查看房间名）");
                gd.Dispose();
                return 1;
            }
            int layerCount = room.Layers?.Count ?? 0;
            int inst0 = RoomEdit.AllInstances(room).Count();
            int tiles0 = RoomEdit.AllTiles(room).Count();
            Console.WriteLine($"baseline: {GameData.Name(room.Name)} {room.Width}x{room.Height} layers={layerCount} instances={inst0} tiles={tiles0}");

            sw.Restart();
            using (var bmp = RoomRender.Render(gd, room))
                Check(bmp != null && bmp.Width >= (int)room.Width && bmp.Height >= (int)room.Height,
                      $"render {bmp?.Width}x{bmp?.Height} covers room {room.Width}x{room.Height}");
            Console.WriteLine($"render ms={sw.ElapsedMilliseconds}");

            var layer = RoomEdit.TilesLayer(room);
            var seed = RoomEdit.AllTiles(room).Select(t => t.Tile).FirstOrDefault();
            bool canPaint = layer != null && seed != null;
            if (canPaint) Console.WriteLine("PASS found tile layer and seed tile");
            else Console.WriteLine("SKIP 房间没有 legacy 瓦片层/种子瓦片，跳过瓦片绘制与擦除检查（按 SKIP 计，不计失败）");
            UndertaleRoom.Tile? brush = canPaint ? RoomEdit.CloneTile(seed!) : null;
            int tiles1 = tiles0, tiles2 = tiles0, painted = 0, erased = 0;
            if (canPaint)
            {
                var cell = RoomEdit.TileWorldSize(brush!);
                int cw = Math.Max(1, (int)Math.Ceiling(cell.Width));
                int ch = Math.Max(1, (int)Math.Ceiling(cell.Height));
                int cols = Math.Min(200, Math.Max(1, (int)room.Width / cw));
                int rows = Math.Min(30, Math.Max(1, (int)room.Height / ch));
                var cells = new List<Point>();
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                        cells.Add(new Point(c, r));
                sw.Restart();
                painted = RoomEdit.PaintTileCells(gd.Data, layer!, brush!, cells);
                sw.Stop();
                tiles1 = RoomEdit.AllTiles(room).Count();
                Console.WriteLine($"paint ms={sw.ElapsedMilliseconds} cells={cells.Count} changed={painted} tiles={tiles1}");
                Check(painted > 0, "paint changed tiles");
                Check(tiles1 > tiles0, $"paint grew tile count ({tiles0} -> {tiles1})");

                sw.Restart();
                erased = RoomEdit.EraseTileCells(layer!, cells.Take(cells.Count / 2).ToList());
                sw.Stop();
                tiles2 = RoomEdit.AllTiles(room).Count();
                Console.WriteLine($"erase ms={sw.ElapsedMilliseconds} changed={erased} tiles={tiles2}");
                Check(erased > 0, "erase changed tiles");
                Check(tiles2 < tiles1, $"erase shrank tile count ({tiles1} -> {tiles2})");
            }

            sw.Restart();
            using (var bmp2 = RoomRender.Render(gd, room)) Check(bmp2.Width >= (int)room.Width, "render after edits");
            Console.WriteLine($"render-after-ms={sw.ElapsedMilliseconds}");

            sw.Restart();
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            using (var fs = File.Create(dst)) UndertaleIO.Write(fs, gd.Data, m => { });
            sw.Stop();
            long size = new FileInfo(dst).Length;
            Console.WriteLine($"save ms={sw.ElapsedMilliseconds} bytes={size}");

            sw.Restart();
            var gd2 = new GameData(dst);
            var room2 = gd2.FindRoom(roomName)!;
            int tiles3 = RoomEdit.AllTiles(room2).Count();
            int inst2 = RoomEdit.AllInstances(room2).Count();
            Console.WriteLine($"reload ms={sw.ElapsedMilliseconds} tiles={tiles3} instances={inst2}");
            Check(tiles3 == tiles2, $"reload tiles {tiles3} == {tiles2}");
            Check(inst2 == inst0, $"reload instances {inst2} == {inst0}");
            Check((room2.Layers?.Count ?? 0) == layerCount, "reload layer count");
            Check(gd2.Data.Rooms.Count == gd.Data.Rooms.Count, "reload room count");
            if (canPaint)
            {
                int sameSize = RoomEdit.AllTiles(room2).Count(t => t.Tile.Width == brush!.Width && t.Tile.Height == brush!.Height);
                Check(sameSize >= painted / 2, $"painted tiles keep source size ({sameSize} >= {painted / 2})");
            }
            gd2.Dispose();
            gd.Dispose();
            Console.WriteLine(fails == 0 ? "STRESS-TEST PASS" : $"STRESS-TEST FAIL ({fails})");
            return fails == 0 ? 0 : 1;
        }
        if (args[0] == "battle-demo")
        {
            // 生成一份可部署的示例战斗 mod（默认输出到当前目录，绝不默认写游戏根）。
            NoExtra("battle-demo [data.win] [modsDir] [leaf]", 4);
            string? data = args.Length > 1 ? args[1] : DefaultBattleDataWin();
            if (args.Length > 1 && !File.Exists(args[1]))
            {
                Console.Error.WriteLine("[battle-demo] data.win 不存在: " + args[1] + "（不传参数会自动查找原版 backup）");
                return 1;
            }
            string modsDir = args.Length > 2 ? args[2] : Path.Combine(Directory.GetCurrentDirectory(), "battle-demo-mods");
            string leaf = args.Length > 3 ? args[3] : "chapter1";
            GameData? gd = null;
            try
            {
                if (data != null && File.Exists(data)) gd = new GameData(data);
                var project = BattleSelfTest.BuildProject(gd, out _);
                project.File.Mod.Leaf = leaf; // 生成到指定章节产物（默认 chapter1；真机验证可用已有存档的 chapter2）
                var issues = project.Validate(gd);
                foreach (var issue in issues.Take(10)) Console.WriteLine((issue.IsError ? "ERROR " : "WARN  ") + issue.Message);
                if (issues.Any(i => i.IsError)) return 1;
                var result = BattleGenerator.Generate(project, modsDir, gd);
                Console.WriteLine($"generated {result.ModDir}");
                Console.WriteLine($"  gml={result.GmlFiles} objects={result.ObjectFiles} data={result.DataFiles}");
                return 0;
            }
            finally { gd?.Dispose(); }
        }
        if (args[0] == "battle-test")
        {
            // 战斗编辑器离线自检：配置校验 -> sidecar 往返 -> 生成 mod -> 产物结构断言。
            NoExtra("battle-test [data.win] [outDir]", 3);
            string? data;
            if (args.Length > 1)
            {
                if (!File.Exists(args[1]))
                {
                    Console.Error.WriteLine("[battle-test] data.win 不存在: " + args[1] + "（不传参数会自动查找原版 backup）");
                    return 1;
                }
                data = args[1];
            }
            else data = DefaultBattleDataWin();
            string? outDir = args.Length > 2 ? args[2] : null;
            return BattleSelfTest.Run(data, outDir);
        }
        if (args[0] == "manifest-test")
        {
            // mods.json 整包清单：扫描 -> 校验 -> 保存/重读 -> 重复 id 检出（默认输出到当前目录，不写游戏根）。
            NoExtra("manifest-test [modsDir] [outJson]", 3);
            string modsDir = args.Length > 1 ? args[1] : Path.Combine(GameRootDir(), "Neutraled", "mods");
            string outJson = args.Length > 2 ? args[2] : Path.Combine(Directory.GetCurrentDirectory(), "manifest-test.json");
            int fails = 0;
            void Check(bool ok, string label)
            {
                Console.WriteLine((ok ? "PASS " : "FAIL ") + label);
                if (!ok) fails++;
            }
            var scanned = ModsManifestDialog.ScanMods(modsDir);
            Check(scanned.Mods.Count > 0, $"scan: 从 {modsDir} 扫描到 {scanned.Mods.Count} 个 mod");
            var ids = scanned.Mods.Select(m => m.Id).ToList();
            Check(ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() == ids.Count, "scan: 扫描结果 id 唯一");
            var errors = ModsManifestDialog.Validate(scanned);
            Check(errors.Count == 0, $"validate: 真实 mods 目录清单无错误（{errors.Count}）");
            try
            {
                ModsManifestDialog.SaveTo(scanned, outJson);
                Check(File.Exists(outJson), "save: 清单已写盘");
                var reload = ModsManifestDialog.LoadFromFile(outJson);
                Check(reload.Mods.Count == scanned.Mods.Count, $"reload: 条目数一致（{reload.Mods.Count}/{scanned.Mods.Count}）");
                Check(reload.Mods.Any(m => !string.IsNullOrWhiteSpace(m.Id) && m.Dependencies != null), "reload: 依赖列表不为 null");
                ModsManifestDialog.SaveTo(scanned, outJson);
                Check(File.Exists(outJson + ".bak"), "save: 二次保存生成 .bak 备份");
                var dup = new ModsManifestDialog.ManifestFile
                {
                    Mods =
                    {
                        new ModsManifestDialog.ManifestMod { Id = "dup.mod" },
                        new ModsManifestDialog.ManifestMod { Id = "dup.mod" },
                        new ModsManifestDialog.ManifestMod { Id = "" },
                    },
                };
                var dupErrors = ModsManifestDialog.Validate(dup);
                Check(dupErrors.Count >= 2, $"validate: 重复 id / 缺 id 被检出（{dupErrors.Count} 条）");
            }
            finally
            {
                try { if (File.Exists(outJson)) File.Delete(outJson); } catch { }
                try { if (File.Exists(outJson + ".bak")) File.Delete(outJson + ".bak"); } catch { }
            }
            Console.WriteLine(fails == 0 ? "MANIFEST-TEST PASS" : $"MANIFEST-TEST FAIL ({fails})");
            return fails == 0 ? 0 : 1;
        }
        if (args[0] == "structure-test")
        {
            Need("structure-test <src data.win> <dst data.win>", 3);
            NoExtra("structure-test <src data.win> <dst data.win>", 3);
            var src = args[1];
            var dst = args[2];
            int fails = 0;
            void Check(bool ok, string label)
            {
                Console.WriteLine((ok ? "PASS " : "FAIL ") + label);
                if (!ok) fails++;
            }
            static void WriteFile(GameData g, string path)
            {
                path = PrepareOutFile(path);
                using var fs = File.Create(path);
                UndertaleIO.Write(fs, g.Data, m => { });
            }
            static string LName(UndertaleRoom.Layer l) => GameData.Name(l.LayerName);

            var gd = new GameData(src);
            var room = gd.FindRoom("room_krisroom")!;
            int baseRooms = gd.Data.Rooms.Count;
            int baseOrder = gd.Data.GeneralInfo?.RoomOrder?.Count ?? -1;
            int baseLayers = room.Layers!.Count;
            int baseInst = RoomEdit.AllInstances(room).Count();
            int baseTiles = RoomEdit.AllTiles(room).Count();
            Console.WriteLine($"baseline: rooms={baseRooms} order={baseOrder} layers={baseLayers} instances={baseInst} tiles={baseTiles}");

            // ---------------- 1) duplicate room
            var dup = RoomEdit.DuplicateRoom(gd.Data, room);
            string dupName = GameData.Name(dup.Name);
            Console.WriteLine($"duplicated -> {dupName}");
            Check(gd.Data.Rooms.Count == baseRooms + 1, $"duplicate: Rooms {gd.Data.Rooms.Count} == {baseRooms + 1}");
            Check(baseOrder < 0 || gd.Data.GeneralInfo!.RoomOrder!.Count == baseOrder + 1, "duplicate: RoomOrder +1");
            Check(dup.Layers!.Count == baseLayers, $"duplicate: layers {dup.Layers!.Count} == {baseLayers}");
            Check(RoomEdit.AllInstances(dup).Count() == baseInst, $"duplicate: instances {RoomEdit.AllInstances(dup).Count()} == {baseInst}");
            Check(RoomEdit.AllTiles(dup).Count() == baseTiles, $"duplicate: tiles {RoomEdit.AllTiles(dup).Count()} == {baseTiles}");
            Check(dup.GameObjects!.Count == baseInst, $"duplicate: flat GameObjects {dup.GameObjects!.Count} == {baseInst}");
            bool layersSame = true;
            for (int i = 0; i < baseLayers; i++)
            {
                var a = room.Layers![i];
                var b = dup.Layers![i];
                if (LName(a) != LName(b) || a.LayerType != b.LayerType || a.LayerDepth != b.LayerDepth) layersSame = false;
            }
            Check(layersSame, "duplicate: layer names/types/depths match");
            var origIds = new HashSet<uint>(RoomEdit.AllInstances(room).Select(x => x.Inst.InstanceID));
            Check(RoomEdit.AllInstances(dup).All(x => !origIds.Contains(x.Inst.InstanceID)), "duplicate: instance ids disjoint from original");
            var origTileIds = new HashSet<uint>(RoomEdit.AllTiles(room).Select(x => x.Tile.InstanceID));
            Check(RoomEdit.AllTiles(dup).All(x => !origTileIds.Contains(x.Tile.InstanceID)), "duplicate: tile ids disjoint from original");
            WriteFile(gd, dst);
            Console.WriteLine($"written {dst} {new FileInfo(dst).Length} B (src {new FileInfo(src).Length} B)");

            // ---------------- 2) reload + delete duplicate
            var gd2 = new GameData(dst);
            var dup2 = gd2.FindRoom(dupName);
            Check(dup2 != null, "reload: duplicated room present");
            if (dup2 != null)
            {
                Check(dup2.Layers!.Count == baseLayers, $"reload: layers {dup2.Layers!.Count} == {baseLayers}");
                Check(RoomEdit.AllInstances(dup2).Count() == baseInst, $"reload: instances {RoomEdit.AllInstances(dup2).Count()} == {baseInst}");
                Check(RoomEdit.AllTiles(dup2).Count() == baseTiles, $"reload: tiles {RoomEdit.AllTiles(dup2).Count()} == {baseTiles}");
                Check(dup2.CheckLayersDepthOrder(), "reload: layers sorted by depth");
                var orig2 = gd2.FindRoom("room_krisroom")!;
                Check(RoomEdit.AllInstances(orig2).Count() == baseInst, "reload: original room instances intact");
                Check(RoomEdit.DeleteRoom(gd2.Data, dup2), "delete: duplicate removed");
                WriteFile(gd2, dst + ".del.win");
                var gd3 = new GameData(dst + ".del.win");
                Check(gd3.Data.Rooms.Count == baseRooms, $"delete reload: rooms {gd3.Data.Rooms.Count} == {baseRooms}");
                Check(gd3.FindRoom(dupName) == null, "delete reload: duplicate gone");
                Check(baseOrder < 0 || gd3.Data.GeneralInfo!.RoomOrder!.Count == baseOrder, "delete reload: RoomOrder back");

                // ---------------- 3) blank room + layer management
                var tmpl = gd3.FindRoom("room_krisroom")!;
                var blank = RoomEdit.CreateBlankRoom(gd3.Data, tmpl);
                string blankName = GameData.Name(blank.Name);
                Console.WriteLine($"blank room -> {blankName}");
                Check(blank.Layers!.Count == baseLayers, $"blank: layers {blank.Layers!.Count} == {baseLayers}");
                Check(RoomEdit.AllInstances(blank).Count() == 0, "blank: 0 instances");
                Check(RoomEdit.AllTiles(blank).Count() == 0, "blank: 0 tiles");
                Check(blank.GameObjects!.Count == 0, "blank: 0 flat GameObjects");
                var instLayer = RoomEdit.AddInstanceLayer(gd3.Data, blank, "Test_Instances", -30);
                var tileLayer = RoomEdit.AddTileLayer(gd3.Data, blank, "Test_Tiles", 14999);
                Check(blank.Layers!.Count == baseLayers + 2, $"layer add: {blank.Layers!.Count} == {baseLayers + 2}");
                Check(blank.Layers.Contains(instLayer) && blank.Layers.Contains(tileLayer), "layer add: both registered");
                Check(blank.CheckLayersDepthOrder(), "layer add: order sorted");
                Check(blank.Layers![0] == instLayer, "layer add: depth -30 is front-most");
                RoomEdit.RenameLayer(gd3.Data, instLayer, "Renamed_Instances");
                string blankName2 = blankName + "_r";
                RoomEdit.RenameRoom(gd3.Data, blank, blankName2);
                Check(blank.Layers![0] == instLayer && LName(instLayer) == "Renamed_Instances", "layer rename: applied");
                var firstLayer = blank.Layers![0];
                Check(RoomEdit.MoveLayer(blank, firstLayer, +1), "layer move: moved down one");
                Check(blank.Layers![1] == firstLayer, "layer move: now at index 1");
                Check(blank.CheckLayersDepthOrder(), "layer move: order still sorted");
                Check(RoomEdit.MoveLayer(blank, firstLayer, -1), "layer move: moved back up");
                Check(blank.Layers![0] == firstLayer, "layer move: back at index 0");
                int beforeRemove = blank.Layers.Count;
                Check(RoomEdit.RemoveLayer(blank, tileLayer), "layer remove: tile layer removed");
                Check(blank.Layers.Count == beforeRemove - 1 && !blank.Layers.Contains(tileLayer), "layer remove: count back");
                WriteFile(gd3, dst + ".struct.win");
                Console.WriteLine($"written {dst}.struct.win {new FileInfo(dst + ".struct.win").Length} B");
                var gd4 = new GameData(dst + ".struct.win");
                var b4 = gd4.FindRoom(blankName2);
                Check(b4 != null, "struct reload: blank room present");
                if (b4 != null)
                {
                    Check(b4.Layers!.Count == baseLayers + 1, $"struct reload: layers {b4.Layers!.Count} == {baseLayers + 1}");
                    Check(b4.Layers.Any(l => LName(l) == "Renamed_Instances"), "struct reload: renamed layer present");
                    Check(b4.Layers.All(l => LName(l) != "Test_Tiles"), "struct reload: removed layer gone");
                    Check(b4.Layers[0] != null && LName(b4.Layers[0]) == "Renamed_Instances", "struct reload: layer order preserved");
                    Check(b4.CheckLayersDepthOrder(), "struct reload: layer order valid");
                    Check(RoomEdit.AllInstances(b4).Count() == 0, "struct reload: still empty");
                    Check(RoomEdit.DeleteRoom(gd4.Data, b4), "delete: blank room removed");
                    WriteFile(gd4, dst + ".struct.del.win");
                    var gd5 = new GameData(dst + ".struct.del.win");
                    Check(gd5.Data.Rooms.Count == baseRooms, $"final: rooms {gd5.Data.Rooms.Count} == {baseRooms}");
                    Check(gd5.FindRoom(blankName2) == null, "final: blank room gone");
                }
            }
            Console.WriteLine(fails == 0 ? "STRUCTURE-TEST PASS" : $"STRUCTURE-TEST FAIL ({fails} failed)");
            return fails == 0 ? 0 : 1;
        }
        if (args[0] == "render")
        {
            Need("render <data.win> <room> [scale] [out.png]", 3);
            NoExtra("render <data.win> <room> [scale] [out.png]", 5);
            var gd = new GameData(args[1]);
            var room = ResolveRoom(gd.Data.Rooms, args[2]);
            int scale = 1;
            if (args.Length > 3)
            {
                if (!int.TryParse(args[3], out scale))
                {
                    Console.Error.WriteLine("[错误] scale 需要整数（例如 1 / 2 / 4）：" + args[3]);
                    return 2;
                }
                int clamped = Math.Clamp(scale, 1, 8);
                if (clamped != scale)
                    Console.Error.WriteLine("[提示] scale " + scale + " 超出范围 1..8，已按 " + clamped + " 渲染");
                scale = clamped;
            }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var bmp = RoomRender.Render(gd, room, scale);
            string outPath;
            if (args.Length > 4)
            {
                outPath = args[4];
                var outFull = Path.GetFullPath(outPath);
                if (Directory.Exists(outFull))
                    outPath = Path.Combine(outFull, "render-" + GameData.Name(room.Name) + ".png");
            }
            else
            {
                outPath = Path.Combine(Directory.GetCurrentDirectory(), "render-" + GameData.Name(room.Name) + ".png");
            }
            outPath = PrepareOutFile(outPath);
            bmp.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
            Console.WriteLine($"rendered {GameData.Name(room.Name)} {room.Width}x{room.Height} scale={scale} -> {outPath} ({new FileInfo(outPath).Length} B) in {sw.ElapsedMilliseconds} ms, layers={room.Layers?.Count}");
            return 0;
        }
        if (args[0] == "asset-clone-test")
        {
            Need("asset-clone-test <data.win> <room>", 3);
            NoExtra("asset-clone-test <data.win> <room>", 3);
            var gd = new GameData(args[1]);
            var room = ResolveRoom(gd.Data.Rooms, args[2]);
            try
            {
                int CountAssets(UndertaleRoom.Layer l, string kind) => kind switch
                {
                    "sprite" => l.AssetsData?.Sprites?.Count ?? 0,
                    "nine" => l.AssetsData?.NineSlices?.Count ?? 0,
                    "legacy" => l.AssetsData?.LegacyTiles?.Count ?? 0,
                    "seq" => l.AssetsData?.Sequences?.Count ?? 0,
                    "particles" => l.AssetsData?.ParticleSystems?.Count ?? 0,
                    "text" => l.AssetsData?.TextItems?.Count ?? 0,
                    _ => 0,
                };
                var srcLayers = room.Layers?.ToList() ?? new List<UndertaleRoom.Layer>();
                var srcCounts = srcLayers.Select(l => (Sprite: CountAssets(l, "sprite"), Nine: CountAssets(l, "nine"),
                    Legacy: CountAssets(l, "legacy"), Seq: CountAssets(l, "seq"), Particles: CountAssets(l, "particles"), Text: CountAssets(l, "text"),
                    Native: NativeTiles.CountNonZero(l))).ToList();
                var copy = RoomEdit.DuplicateRoom(gd.Data, room, "zz_asset_clone_test");
                int fails = 0;
                void Check(bool ok, string label)
                {
                    Console.WriteLine((ok ? "PASS " : "FAIL ") + label);
                    if (!ok) fails++;
                }
                var copyLayers = copy.Layers?.ToList() ?? new List<UndertaleRoom.Layer>();
                Check(copyLayers.Count == srcLayers.Count, $"layers {copyLayers.Count} == {srcLayers.Count}");
                for (int i = 0; i < Math.Min(copyLayers.Count, srcLayers.Count); i++)
                {
                    var a = srcLayers[i];
                    var b = copyLayers[i];
                    var expected = srcCounts[i];
                    var actual = (Sprite: CountAssets(b, "sprite"), Nine: CountAssets(b, "nine"), Legacy: CountAssets(b, "legacy"),
                        Seq: CountAssets(b, "seq"), Particles: CountAssets(b, "particles"), Text: CountAssets(b, "text"),
                        Native: NativeTiles.CountNonZero(b));
                    var name = GameData.Name(a.LayerName);
                    Check(expected == actual, $"{name}: counts {expected} == {actual}");
                    if (a.AssetsData?.Sprites != null && b.AssetsData?.Sprites != null)
                    {
                        for (int j = 0; j < Math.Min(a.AssetsData.Sprites.Count, b.AssetsData.Sprites.Count); j++)
                        {
                            var x = a.AssetsData.Sprites[j];
                            var y = b.AssetsData.Sprites[j];
                            Check(!ReferenceEquals(x, y) && ReferenceEquals(x.Sprite, y.Sprite) &&
                                  x.X == y.X && x.Y == y.Y && x.ScaleX == y.ScaleX && x.ScaleY == y.ScaleY &&
                                  x.Rotation == y.Rotation && x.Color == y.Color && x.FrameIndex == y.FrameIndex,
                                $"{name}: sprite[{j}] deep-copied {GameData.Name(x.Name)}");
                        }
                    }
                    if (a.TilesData?.TileData != null && b.TilesData?.TileData != null)
                    {
                        bool nativeSame = !ReferenceEquals(a.TilesData.TileData, b.TilesData.TileData);
                        bool nativeValues = a.TilesData.TileData.Length == b.TilesData.TileData.Length;
                        if (nativeValues)
                        {
                            for (int y = 0; y < a.TilesData.TileData.Length && nativeValues; y++)
                            {
                                if (!ReferenceEquals(a.TilesData.TileData[y], b.TilesData.TileData[y]) &&
                                    a.TilesData.TileData[y].SequenceEqual(b.TilesData.TileData[y])) continue;
                                nativeValues = false;
                            }
                        }
                        Check(nativeSame && nativeValues, $"{name}: native TileData deep-copied ({expected.Native} non-zero)");
                    }
                }
                Console.WriteLine($"ASSET-CLONE-TEST {(fails == 0 ? "PASS" : "FAIL (" + fails + ")")} room={GameData.Name(room.Name)} layers={copyLayers.Count} sprites={srcCounts.Sum(c => c.Sprite)} nine={srcCounts.Sum(c => c.Nine)} nativeTiles={srcCounts.Sum(c => c.Native)}");
                return fails == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine("ASSET-CLONE-TEST FAIL: " + ex);
                return 1;
            }
        }
        if (args[0] == "render-stats")
        {
            Need("render-stats <data.win> <room> [scale] [out.json]", 3);
            NoExtra("render-stats <data.win> <room> [scale] [out.json]", 5);
            var gd = new GameData(args[1]);
            var room = ResolveRoom(gd.Data.Rooms, args[2]);
            int scale = 1;
            if (args.Length > 3 && !int.TryParse(args[3], out scale))
            {
                Console.Error.WriteLine("[错误] scale 需要整数（例如 1 / 2 / 4）：" + args[3]);
                return 2;
            }
            scale = Math.Clamp(scale, 1, 8);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var stats = RenderStats.Run(gd, room, scale);
            Console.WriteLine(RenderStats.Format(stats));
            Console.WriteLine($"render-stats done in {sw.ElapsedMilliseconds} ms");
            if (args.Length > 4)
            {
                var outJson = PrepareOutFile(args[4]);
                File.WriteAllText(outJson, System.Text.Json.JsonSerializer.Serialize(stats, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), System.Text.Encoding.UTF8);
                Console.WriteLine("json -> " + outJson);
            }
            return 0;
        }
        if (args[0] == "save-roundtrip")
        {
            Need("save-roundtrip <src data.win> <dst data.win>", 3);
            NoExtra("save-roundtrip <src data.win> <dst data.win>", 3);
            var srcPath = args[1];
            var dstPath = PrepareOutFile(args[2]);
            var gd = new GameData(srcPath);
            using var doc = new EditorDocument(gd, srcPath);
            var saved = doc.Save(dstPath);
            if (!saved.Ok)
            {
                Console.Error.WriteLine("SAVE-ROUNDTRIP FAIL: " + saved.Error);
                return 1;
            }
            static string Hash(string p)
            {
                using var fs = File.OpenRead(p);
                return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fs));
            }
            string hs = Hash(srcPath), hd = Hash(dstPath);
            bool same = string.Equals(hs, hd, StringComparison.OrdinalIgnoreCase);
            Console.WriteLine($"SAVE-ROUNDTRIP src={hs} dst={hd} bytes {new FileInfo(srcPath).Length}/{new FileInfo(dstPath).Length} match={same}");
            return same ? 0 : 1;
        }
        if (args[0] == "roundtrip")
        {
            Need("roundtrip <src data.win> <dst data.win>", 3);
            NoExtra("roundtrip <src data.win> <dst data.win>", 3);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var data = Load(args[1]);
            Console.WriteLine($"loaded {sw.ElapsedMilliseconds} ms");
            var outPath = PrepareOutFile(args[2]);
            using (var fs = File.Create(outPath)) UndertaleIO.Write(fs, data, m => { });
            Console.WriteLine($"written {outPath} {new FileInfo(outPath).Length} B in {sw.ElapsedMilliseconds} ms (src {new FileInfo(args[1]).Length} B)");
            var sw2 = System.Diagnostics.Stopwatch.StartNew();
            var data2 = Load(outPath);
            Console.WriteLine($"reloaded in {sw2.ElapsedMilliseconds} ms: rooms={data2.Rooms.Count} sprites={data2.Sprites.Count} objects={data2.GameObjects.Count}");
            for (int i = 0; i < Math.Min(5, data2.Rooms.Count); i++)
            {
                var r1 = data.Rooms[i]; var r2 = data2.Rooms[i];
                Console.WriteLine($"  room[{i}] {r2.Name?.Content} {r2.Width}x{r2.Height} layers={r2.Layers?.Count} (orig layers={r1.Layers?.Count})");
            }
            return 0;
        }
        if (args[0] == "texture")
        {
            Need("texture <data.win> <outDir>", 3);
            NoExtra("texture <data.win> <outDir>", 3);
            var data = Load(args[1]);
            var outDir = PrepareOutDir(args[2]);
            foreach (var (t, i) in data.EmbeddedTextures.Select((t, i) => (t, i)).Take(8))
            {
                var img = t.TextureData?.Image;
                Console.WriteLine($"tex[{i}] {t.Name?.Content} {t.TextureWidth}x{t.TextureHeight} texData={t.TextureData?.Width}x{t.TextureData?.Height} fmt={img?.Format}");
                if (img != null)
                {
                    var fn = Path.Combine(outDir, $"tex{i}.png");
                    using (var fs = File.Create(fn)) img.SavePng(fs);
                    Console.WriteLine("   saved " + fn + " " + new FileInfo(fn).Length + " B");
                }
            }
            return 0;
        }
        Console.Error.WriteLine("[错误] 未知命令: " + args[0]);
        Console.Error.WriteLine("  " + UsageHint);
        return 2;
    }

    /// <summary>游戏根：优先环境变量，其次常见安装位置，最后退当前目录（CLI 生成 demo 用）。</summary>
    private static string GameRootDir()
    {
        var env = Environment.GetEnvironmentVariable("DELTARUNE_DIR");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(Path.Combine(env!, "DELTARUNE.exe"))) return env!;
        foreach (var c in new[] { @"E:\steam\steamapps\common\DELTARUNE", @"C:\Program Files (x86)\Steam\steamapps\common\DELTARUNE", @"D:\SteamLibrary\steamapps\common\DELTARUNE" })
            if (File.Exists(Path.Combine(c, "DELTARUNE.exe"))) return c;
        return Directory.GetCurrentDirectory();
    }

    /// <summary>battle-test 默认用原版 ch1 备份（只读），找不到就跳过资源名校验。</summary>
    private static string? DefaultBattleDataWin()
    {
        var candidates = new List<string>();
        var env = Environment.GetEnvironmentVariable("DELTARUNE_DIR");
        if (!string.IsNullOrWhiteSpace(env)) candidates.Add(env!);
        candidates.Add(@"E:\steam\steamapps\common\DELTARUNE");
        candidates.Add(@"C:\Program Files (x86)\Steam\steamapps\common\DELTARUNE");
        candidates.Add(@"D:\SteamLibrary\steamapps\common\DELTARUNE");
        var root = GameRootDir();
        if (!candidates.Contains(root, StringComparer.OrdinalIgnoreCase)) candidates.Add(root);
        foreach (var c in candidates)
        {
            if (string.IsNullOrWhiteSpace(c)) continue;
            var p = Path.Combine(c, "backup", "chapter1_windows", "data.win");
            if (File.Exists(p)) return p;
        }
        return null;
    }
}