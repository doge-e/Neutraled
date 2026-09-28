using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>主题：一套命名配色（外加可选字体），供 GUI / Studio / 网页界面 / 游戏内控制台共用同一份数据。
/// 约定：
///   · 颜色统一写成 "#RRGGBB" 字符串 —— GUI 直接当 CSS 变量用，人也能一眼看懂；
///   · GML 侧不读本文件：它读 ApplyToGameConsole 生成的 console-theme.json（那里另给一份算好的 BGR 整数）；
///   · 字段必须挂 [JsonInclude]：Paths.Json 没开 IncludeFields，光有 public 字段不会参与序列化（只写属性）；
///   · 外部主题放 &lt;游戏根&gt;/Neutraled/themes/&lt;id&gt;.json，**以文件名为准**（文件里的 id 字段不参与定址）。</summary>
public sealed class Theme
{
    [JsonInclude, JsonPropertyName("id")] public string Id = "";
    [JsonInclude, JsonPropertyName("name")] public string Name = "";
    [JsonInclude, JsonPropertyName("author")] public string Author = "";
    [JsonInclude, JsonPropertyName("description")] public string Description = "";
    [JsonInclude, JsonPropertyName("colors")] public Dictionary<string, string> Colors = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>null = 用界面默认字体（GUI 侧读 config.json 的 theme_font）。</summary>
    [JsonInclude, JsonPropertyName("fontFamily")] public string? FontFamily;
    /// <summary>null = 用界面默认字号（GUI 侧读 config.json 的 theme_font_size）。</summary>
    [JsonInclude, JsonPropertyName("fontSize")] public int? FontSize;
    /// <summary>内置主题标记。**不写进主题文件**（落盘走 ToNode 手工构造，没有这个键），
    /// 但反射序列化必须带上它：WebUi 的 Obj() 走 SerializeToNode，网页端靠 builtIn 区分内置/外部。
    /// 反序列化后一律由 Load/ScanExternal 强制改回 false —— 外部文件写 "builtIn": true 冒充不了内置。</summary>
    [JsonInclude, JsonPropertyName("builtIn")] public bool BuiltIn;
}

/// <summary>主题管理：内置 4 套 + themes/*.json 扩展。
/// 输出行一律以 [主题] 开头（人可读、可直接 grep）。
/// 退出码约定（Use / Import）：0 = 成功，1 = 失败，2 = 参数非法（id 不合法）。</summary>
public static class Themes
{
    /// <summary>缺省主题 id（config.json 没有 theme 键、或键值查不到主题时用它）。</summary>
    public const string DefaultId = "dark";

    /// <summary>外部主题必须提供的颜色键（顺序即文档顺序，导出/落盘都按它排）。
    /// 语义与控制台绘制（api/ntl_console_draw.gml）的取色点一一对应：
    ///   bg 面板底 / panel 内容底 / fg 正文 / dim 次要文字 / border 分隔线
    ///   accent 标题与强调 / highlight "==" 高亮行 / selection 过滤中与选中项
    ///   ok 输入行与成功 / warn [警告] / error [错误]</summary>
    public static readonly string[] ColorKeys =
        { "bg", "panel", "fg", "dim", "border", "accent", "highlight", "selection", "ok", "warn", "error" };

    /// <summary>缩进版 JSON 选项：Paths.Json 没开 WriteIndented，而主题文件是给人看/给译者改的，必须缩进。</summary>
    private static readonly JsonSerializerOptions Indented = new(Paths.Json) { WriteIndented = true };

    /// <summary>外部主题目录（Neutraled/themes）。</summary>
    public static string Root(string gameRoot) => Paths.ThemesRoot(gameRoot);

    /// <summary>游戏内控制台读的配色文件（Neutraled/console-theme.json）。</summary>
    public static string ConsoleThemePath(string gameRoot) => Path.Combine(Paths.NeutraledRoot(gameRoot), "console-theme.json");

    /// <summary>外部主题文件路径。id 不合法时返回空串（**绝不拼接越界路径**）。</summary>
    public static string FileOf(string gameRoot, string id) => IsSafeId(id) ? Path.Combine(Root(gameRoot), id + ".json") : "";

    /// <summary>主题 id 是否安全：只允许字母/数字/点/下划线/短横线，且不含 ".."。
    /// 主题 id 会直接当文件名，必须挡住 ../ 与非法字符（否则可从 JSON 里写到 Neutraled 之外）。</summary>
    public static bool IsSafeId(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        if (id.Length > 64) return false;
        if (id == "." || id == ".." || id.Contains("..")) return false;
        foreach (var c in id)
        {
            var ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
                     || c == '.' || c == '_' || c == '-' || (c > 127 && char.IsLetterOrDigit(c));
            if (!ok) return false;
        }
        return true;
    }

    /// <summary>内置主题（代码内定义，永远可用；外部同 id 文件会覆盖它）。</summary>
    private static List<Theme> BuiltIns() => new()
    {
        Make("dark", "深色（默认）", "Neutraled 默认深色配色（黑底蓝调，久看不累）", new()
        {
            ["bg"] = "#101014", ["panel"] = "#16161e", ["fg"] = "#e6e6e6", ["dim"] = "#8a8a96",
            ["border"] = "#2a2a38", ["accent"] = "#4fc3f7", ["highlight"] = "#ffd54f", ["selection"] = "#2f5fd0",
            ["ok"] = "#7ee081", ["warn"] = "#ffb74d", ["error"] = "#ff6b6b"
        }),
        Make("light", "浅色", "白底深字，适合白天或投影", new()
        {
            ["bg"] = "#f5f5f7", ["panel"] = "#ffffff", ["fg"] = "#1c1c22", ["dim"] = "#6b6b78",
            ["border"] = "#d0d0d8", ["accent"] = "#0b63c5", ["highlight"] = "#e6a700", ["selection"] = "#bcd7ff",
            ["ok"] = "#2e7d32", ["warn"] = "#b26a00", ["error"] = "#c62828"
        }),
        Make("high-contrast", "高对比度（无障碍）", "纯黑底纯白字，为低视力与色觉障碍用户准备", new()
        {
            ["bg"] = "#000000", ["panel"] = "#000000", ["fg"] = "#ffffff", ["dim"] = "#c0c0c0",
            ["border"] = "#ffffff", ["accent"] = "#00e5ff", ["highlight"] = "#ffff00", ["selection"] = "#0050d0",
            ["ok"] = "#00ff00", ["warn"] = "#ffaa00", ["error"] = "#ff0000"
        }),
        Make("classic-deltarune", "经典 DELTARUNE", "贴近 DELTARUNE 原作的纯黑底白字与紫蓝强调色", new()
        {
            ["bg"] = "#000000", ["panel"] = "#101010", ["fg"] = "#ffffff", ["dim"] = "#a0a0a0",
            ["border"] = "#555555", ["accent"] = "#c0a0ff", ["highlight"] = "#ffff80", ["selection"] = "#404080",
            ["ok"] = "#80ff80", ["warn"] = "#ffcc44", ["error"] = "#ff5555"
        })
    };

    private static Theme Make(string id, string name, string desc, Dictionary<string, string> colors) => new()
    {
        Id = id, Name = name, Author = "Neutraled", Description = desc, Colors = colors, BuiltIn = true
    };

    /// <summary>把颜色表归一成 OrdinalIgnoreCase（STJ 反序列化会新建字典，丢掉我们设的比较器）。
    /// 顺便把落盘的键原样保留：外部主题多写的颜色键不丢，导出时原样带回去。</summary>
    private static void Normalize(Theme t)
    {
        var copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in t.Colors) if (!string.IsNullOrWhiteSpace(kv.Key)) copy[kv.Key.Trim()] = kv.Value ?? "";
        t.Colors = copy;
    }

    /// <summary>读外部主题目录里的全部 *.json（损坏的打印警告后跳过，绝不让一个坏文件毁掉整个列表）。</summary>
    private static List<Theme> ScanExternal(string gameRoot)
    {
        var list = new List<Theme>();
        var root = Root(gameRoot);
        if (!Directory.Exists(root)) return list;
        foreach (var f in Directory.GetFiles(root, "*.json").OrderBy(x => x, StringComparer.Ordinal))
        {
            var id = Path.GetFileNameWithoutExtension(f);
            if (!IsSafeId(id)) { Console.WriteLine(L("[主题] 非法主题 id: {0}（只允许字母、数字、点、下划线与短横线）", id)); continue; }
            try
            {
                var t = JsonSerializer.Deserialize<Theme>(File.ReadAllText(f), Paths.Json);
                if (t == null) continue;
                t.Id = id;                 // 以文件名为准：文件里的 id 字段只当备注，防止两处不一致时定址漂移
                t.BuiltIn = false;
                Normalize(t);
                list.Add(t);
            }
            catch (Exception ex) { Console.WriteLine(L("[主题] 文件损坏，已跳过: {0} → {1}", f, ex.Message)); }
        }
        return list;
    }

    /// <summary>全部主题：内置（按定义顺序，dark 在前）+ 外部（按 id 排序）。同 id 时外部覆盖内置。</summary>
    public static List<Theme> List(string gameRoot)
    {
        var ext = ScanExternal(gameRoot);
        var byId = new Dictionary<string, Theme>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in ext) byId[t.Id] = t;
        var list = new List<Theme>();
        foreach (var b in BuiltIns()) if (!byId.ContainsKey(b.Id)) list.Add(b);
        list.AddRange(ext.OrderBy(t => t.Id, StringComparer.OrdinalIgnoreCase));
        return list;
    }

    /// <summary>取一个主题（外部优先，其次内置）；找不到或 id 不合法返回 null。</summary>
    public static Theme? Load(string gameRoot, string id)
    {
        if (!IsSafeId(id)) return null;
        var f = FileOf(gameRoot, id);
        if (f.Length > 0 && File.Exists(f))
        {
            try
            {
                var t = JsonSerializer.Deserialize<Theme>(File.ReadAllText(f), Paths.Json);
                if (t != null)
                {
                    t.Id = id;             // 以调用方给的文件名为准（见 ScanExternal 注释）
                    t.BuiltIn = false;
                    Normalize(t);
                    return t;
                }
            }
            catch { /* 坏文件当不存在：调用方会退回内置或报"找不到主题"，不至于抛异常打断 CLI */ }
        }
        foreach (var b in BuiltIns()) if (b.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) return b;
        return null;
    }

    /// <summary>当前主题 id：读 config.json 的 theme；缺失或指向不存在的主题时回退 "dark"（配置被手改坏也不会让界面没配色）。</summary>
    public static string ActiveId(string gameRoot)
    {
        var id = ConfigFile.GetString(gameRoot, "theme");
        if (string.IsNullOrWhiteSpace(id)) return DefaultId;
        id = id.Trim();
        return Load(gameRoot, id) != null ? id : DefaultId;
    }

    /// <summary>切换主题：写 config.json（theme / theme_font / theme_font_size）+ 生成 console-theme.json。
    /// 返回码：0 成功，1 失败（找不到主题/写不进去），2 id 非法。</summary>
    public static int Use(string gameRoot, string id)
    {
        if (!IsSafeId(id)) { Console.WriteLine(L("[主题] 非法主题 id: {0}（只允许字母、数字、点、下划线与短横线）", id)); return 2; }
        var t = Load(gameRoot, id);
        if (t == null) { Console.WriteLine(L("[主题] 找不到主题: {0}", id)); return 1; }
        // 一次读-改-写：GUI 读 config.json 就能立刻换配色，不用自己解析主题文件（保未知键）
        ConfigFile.SetMany(gameRoot, new Dictionary<string, JsonNode?>
        {
            ["theme"] = JsonValue.Create(t.Id),
            ["theme_font"] = string.IsNullOrWhiteSpace(t.FontFamily) ? null : JsonValue.Create(t.FontFamily),
            ["theme_font_size"] = t.FontSize.HasValue ? JsonValue.Create(t.FontSize.Value) : null
        });
        AppDomain.CurrentDomain.SetData("Neutraled.Theme", t.Id);   // 同进程内（WebUi/Studio）无需重读配置即可取到
        ApplyToGameConsole(gameRoot, t.Id);
        Console.WriteLine(L("[主题] 已切换到 {0}（{1}）", t.Id, L(t.Name)));
        return 0;
    }

    /// <summary>把主题序列化成单文件 JSON 文本（可直接发给别人，Import 能原样吃回来）。找不到主题返回空串。</summary>
    public static string Export(string gameRoot, string id)
    {
        var t = Load(gameRoot, id);
        if (t == null) { Console.WriteLine(L("[主题] 找不到主题: {0}", id)); return ""; }
        return ToNode(t).ToJsonString(Indented);
    }

    /// <summary>导入主题 JSON（校验 id 与全部必需颜色键后才落盘）。
    /// 已存在同名主题（含内置）时只有 overwrite=true 才覆盖。
    /// 返回码：0 成功，1 失败（解析错/缺键/已存在且没给 --force/写不进）。</summary>
    public static int Import(string gameRoot, string json, bool overwrite = false)
    {
        Theme? t;
        try { t = JsonSerializer.Deserialize<Theme>(json, Paths.Json); }
        catch (Exception ex) { Console.WriteLine(L("[主题] 导入失败：JSON 解析错误 → {0}", ex.Message)); return 1; }
        if (t == null) { Console.WriteLine(L("[主题] 导入失败：内容为空")); return 1; }
        Normalize(t);
        if (string.IsNullOrWhiteSpace(t.Id)) { Console.WriteLine(L("[主题] 导入失败：缺少 id 字段")); return 1; }
        if (!IsSafeId(t.Id)) { Console.WriteLine(L("[主题] 非法主题 id: {0}（只允许字母、数字、点、下划线与短横线）", t.Id)); return 1; }
        var missing = ColorKeys.Where(k => !t.Colors.ContainsKey(k) || string.IsNullOrWhiteSpace(t.Colors[k])).ToList();
        if (missing.Count > 0) { Console.WriteLine(L("[主题] 导入失败：缺少必需颜色键 {0}", string.Join(", ", missing))); return 1; }
        var unknown = t.Colors.Keys.Where(k => !ColorKeys.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList();
        if (unknown.Count > 0) Console.WriteLine(L("[主题] 忽略未知颜色键: {0}", string.Join(", ", unknown)));

        var path = FileOf(gameRoot, t.Id);
        var exists = File.Exists(path) || BuiltIns().Any(b => b.Id.Equals(t.Id, StringComparison.OrdinalIgnoreCase));
        if (exists && !overwrite) { Console.WriteLine(L("[主题] 已存在同名主题: {0}（加 --force 覆盖）", t.Id)); return 1; }
        if (!Write(gameRoot, path, t)) return 1;
        Console.WriteLine(L("[主题] 已导入: {0}（{1}）", t.Id, string.IsNullOrWhiteSpace(t.Name) ? t.Id : t.Name));
        return 0;
    }

    /// <summary>生成游戏内控制台配色文件（GML 侧在 ntl_console_draw 里读它换配色）。
    /// 除 "#RRGGBB" 字符串外再给一份 colors_bgr：GM 的颜色是 BGR 整数（make_colour_rgb(r,g,b) = b*65536+g*256+r），
    /// GML 没有现成的十六进制解析函数，算好给它是为了让游戏侧只做一次 variable_struct_get，不必手写解析。
    /// 返回落盘路径；找不到主题或写失败返回空串。</summary>
    public static string ApplyToGameConsole(string gameRoot, string id)
    {
        var t = Load(gameRoot, id);
        if (t == null) { Console.WriteLine(L("[主题] 找不到主题: {0}", id)); return ""; }
        var colors = new JsonObject();
        var bgr = new JsonObject();
        foreach (var k in ColorKeys)
        {
            var hex = t.Colors.TryGetValue(k, out var v) ? v : "";
            colors[k] = hex;
            var n = HexToBgr(hex);
            if (n >= 0) bgr[k] = n;
        }
        var obj = new JsonObject
        {
            ["schema"] = 1,
            ["id"] = t.Id,
            ["name"] = L(t.Name),        // 控制台标题栏要显示主题名，这里给的是当前语言的写法
            ["colors"] = colors,
            ["colors_bgr"] = bgr
        };
        if (!string.IsNullOrWhiteSpace(t.FontFamily)) obj["fontFamily"] = t.FontFamily;
        if (t.FontSize.HasValue) obj["fontSize"] = t.FontSize.Value;
        var path = ConsoleThemePath(gameRoot);
        try
        {
            Directory.CreateDirectory(Paths.NeutraledRoot(gameRoot));
            Paths.SafeWrite(path, obj.ToJsonString(Indented));
        }
        catch (Exception ex) { Console.WriteLine(L("[主题] 写出失败: {0}", ex.Message)); return ""; }
        Console.WriteLine(L("[主题] 已写出控制台配色: {0}", path));
        return path;
    }

    /// <summary>打印主题清单（--theme-list）。活动主题行首带 *。</summary>
    public static void PrintList(string gameRoot)
    {
        var themes = List(gameRoot);
        var active = ActiveId(gameRoot);
        Console.WriteLine(L("===== 主题 ====="));
        if (themes.Count == 0) { Console.WriteLine(L("  （无）")); return; }
        foreach (var t in themes)
        {
            var mark = t.Id.Equals(active, StringComparison.OrdinalIgnoreCase) ? "*" : " ";
            // 先算好再插值：洞内嵌字符串字面量虽然合法，但本项目无法本地编译，取保守写法
            var name = L(t.Name);
            var kind = L(t.BuiltIn ? "内置" : "外部");
            Console.WriteLine($"  {mark} {t.Id,-20} {name,-20} {kind}");
        }
        Console.WriteLine(L("  共 {0} 个主题（内置 {1}，外部 {2}）", themes.Count, themes.Count(x => x.BuiltIn), themes.Count(x => !x.BuiltIn)));
        Console.WriteLine(L("  活动主题: {0}", active));
        Console.WriteLine(L("  外部主题目录: {0}", Root(gameRoot)));
    }

    /// <summary>当前进程最近一次 Use 的主题 id（没有就返回 null；WebUi/Studio 同进程内免重读配置）。</summary>
    public static string? CurrentInProcess() => AppDomain.CurrentDomain.GetData("Neutraled.Theme") as string;

    /// <summary>主题 → 落盘用 JsonObject（必需键按 ColorKeys 顺序在前，额外键按原样跟在后面）。</summary>
    private static JsonObject ToNode(Theme t)
    {
        var obj = new JsonObject
        {
            ["schema"] = 1,
            ["id"] = t.Id,
            ["name"] = t.Name,
            ["author"] = t.Author,
            ["description"] = t.Description
        };
        var colors = new JsonObject();
        foreach (var k in ColorKeys) if (t.Colors.TryGetValue(k, out var v)) colors[k] = v;
        foreach (var kv in t.Colors) if (!ColorKeys.Contains(kv.Key, StringComparer.OrdinalIgnoreCase)) colors[kv.Key] = kv.Value;
        obj["colors"] = colors;
        if (!string.IsNullOrWhiteSpace(t.FontFamily)) obj["fontFamily"] = t.FontFamily;
        if (t.FontSize.HasValue) obj["fontSize"] = t.FontSize.Value;
        return obj;
    }

    private static bool Write(string gameRoot, string path, Theme t)
    {
        try
        {
            var full = Path.GetFullPath(path);
            // 前缀校验：无论 id 从哪来，只允许写进 themes/ 之内（IsSafeId 已挡一次，这里是第二道闸）
            var rootFull = Path.GetFullPath(Root(gameRoot)) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            { Console.WriteLine(L("[主题] 拒绝写入主题目录之外的路径: {0}", full)); return false; }
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            Paths.SafeWrite(full, ToNode(t).ToJsonString(Indented));
            return true;
        }
        catch (Exception ex) { Console.WriteLine(L("[主题] 写入失败: {0}", ex.Message)); return false; }
    }

    /// <summary>"#RRGGBB" / "#RGB" / "RRGGBB" → GameMaker 的 BGR 整数（0xBBGGRR）；非法返回 -1。
    /// ★ 别照搬 CSS 的 RGB 顺序：GM 的 make_colour_rgb(r,g,b) 返回的是 b*65536+g*256+r，写反了红蓝会互换。</summary>
    private static int HexToBgr(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return -1;
        var s = hex.Trim().TrimStart('#');
        if (s.Length == 3) s = new string(new[] { s[0], s[0], s[1], s[1], s[2], s[2] });
        if (s.Length != 6) return -1;
        if (!int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)) return -1;
        var r = (rgb >> 16) & 0xFF;
        var g = (rgb >> 8) & 0xFF;
        var b = rgb & 0xFF;
        return (b << 16) | (g << 8) | r;
    }
}
