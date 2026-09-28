using System.Text.Json;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>整脚本覆盖（mod 提供完整新版脚本内容）。</summary>
public sealed class ModPatch
{
    public string Target { get; set; } = "";
    public string File { get; set; } = "";
}

/// <summary>局部文本替换（反编译后子串替换，再重新编译）。</summary>
public sealed class ModFindReplace
{
    public string Target { get; set; } = "";
    public string Find { get; set; } = "";
    public string Replace { get; set; } = "";
}

/// <summary>mod 新建/补挂的一个游戏对象事件。</summary>
public sealed class ModObjectEvent
{
    /// <summary>事件类型名（Create/Destroy/Alarm/Step/BeginStep/EndStep/Draw/DrawGUI/Other/Collision/Keyboard/Mouse/KeyPress/...）。</summary>
    public string Type { get; set; } = "";
    /// <summary>事件子类型（GM 原始编号：Begin Step=1、End Step=2、Draw GUI=64、Pre-Draw=76 等）。</summary>
    public int Subtype { get; set; }
    /// <summary>GML 源文件（相对章节目录）。</summary>
    public string File { get; set; } = "";
}

/// <summary>mod 新增的游戏对象（含事件）。
/// ★ 为什么需要它：靠新对象当入口的 mod（如 DOJO）没法只靠「覆盖脚本」表达——
///   它们的逻辑挂在一个原版根本不存在的对象上，整包部署时是 data.win 里的新条目，
///   拆成差异层时既不是 patch（没有基线可覆盖）也不是脚本（不是函数）。</summary>
public sealed class ModObject
{
    public string Name { get; set; } = "";
    /// <summary>精灵名（按名解析，找不到就响亮警告；缺省不动）。</summary>
    public string? Sprite { get; set; }
    /// <summary>父对象名。</summary>
    public string? Parent { get; set; }
    /// <summary>遮罩精灵名。</summary>
    public string? Mask { get; set; }
    public bool Visible { get; set; } = true;
    public bool Solid { get; set; }
    public bool Persistent { get; set; }
    /// <summary>深度（缺省 0）。</summary>
    public int? Depth { get; set; }
    public List<ModObjectEvent> Events { get; set; } = new();
}

/// <summary>mod 新建房间的视图（只带**启用**的视图；未启用的视图引擎按默认处理）。</summary>
public sealed class ModRoomView
{
    public int Index { get; set; }
    public int PortX { get; set; }
    public int PortY { get; set; }
    public int PortWidth { get; set; } = 640;
    public int PortHeight { get; set; } = 480;
    public int ViewX { get; set; }
    public int ViewY { get; set; }
    public int ViewWidth { get; set; } = 640;
    public int ViewHeight { get; set; } = 480;
    public int SpeedX { get; set; } = -1;
    public int SpeedY { get; set; } = -1;
    public uint BorderX { get; set; }
    public uint BorderY { get; set; }
    /// <summary>跟随对象名（可空）。</summary>
    public string? Object { get; set; }
}

/// <summary>mod 新增的房间（基本属性 + 启用视图）。
/// ★ 为什么需要它：房间在指令级 diff 里**根本不出现**（它是 ROOM 段的资源，不是代码条目），
///   而 dojo 靠 room_goto(147) 进自己新建的房间 —— 只做「对象 + 事件」时房间表里没有第 148 个房间，
///   真机上入口直接断掉。这里只表达**空房间**（无实例/图块/层/创建代码/启用背景，如 room_dojo
///   完全靠 obj_dojo 自己 Draw）；有内容的房间需要完整的房间序列化，暂由报告指出，不静默丢。</summary>
public sealed class ModRoom
{
    public string Name { get; set; } = "";
    public uint Width { get; set; } = 640;
    public uint Height { get; set; } = 480;
    public uint Speed { get; set; } = 30;
    public bool Persistent { get; set; }
    /// <summary>RoomEntryFlags 的数值（EnableViews/ShowColor/IsGMS2/IsGMS2_3…）。</summary>
    public int Flags { get; set; }
    public long BackgroundColor { get; set; }
    public bool DrawBackgroundColor { get; set; } = true;
    public List<ModRoomView> Views { get; set; } = new();
}

/// <summary>mod.json 的 references 段。</summary>
public sealed class ModReferences
{
    public string? Source { get; set; }
    public string? Assets { get; set; }
    public List<string> Codes { get; set; } = new();
    public List<string> Override { get; set; } = new();
}

/// <summary>外部启动型章节：这个 mod 自带运行时（Kristal 引擎），Neutraled 不加载它的 data.win，
/// 而是在章节选择里选中它时**退出 DELTARUNE 并拉起那个程序**。
/// 这样插件、进度、通关全部是 Kristal 原生行为，不需要移植引擎。</summary>
public sealed class ModExternal
{
    /// <summary>要启动的可执行文件（通常是 love.exe）。</summary>
    [System.Text.Json.Serialization.JsonPropertyName("exe")] public string Exe { get; set; } = "";
    /// <summary>命令行参数（通常是 Kristal 引擎目录）。</summary>
    [System.Text.Json.Serialization.JsonPropertyName("args")] public string Args { get; set; } = "";
    /// <summary>工作目录。</summary>
    [System.Text.Json.Serialization.JsonPropertyName("cwd")] public string Cwd { get; set; } = "";
    /// <summary>mod 内保留的原项目副本（相对 mod 目录，如 "kristal"）——部署时装进引擎的 mods/。</summary>
    [System.Text.Json.Serialization.JsonPropertyName("project")] public string Project { get; set; } = "";
    /// <summary>目标引擎的 mods 目录（绝对路径）。</summary>
    [System.Text.Json.Serialization.JsonPropertyName("engine_mods")] public string EngineMods { get; set; } = "";
    /// <summary>装进引擎后使用的目录名。</summary>
    [System.Text.Json.Serialization.JsonPropertyName("mod_name")] public string ModName { get; set; } = "";
}

/// <summary>mod 作者自报的「适配程度」—— 声明得越充分，Neutraled 就能走越快的通道。
/// 全部可选；**缺省一律按最保守（完整）通道处理**。任何一条声明与 mod 实际内容不符时，
/// Neutraled 会忽略该声明并响亮警告（宁可慢，不可错）。</summary>
public sealed class ModAdapt
{
    /// <summary>"fast" = 等价于把下面几项全声明为 true（作者自担）；缺省 "safe"。</summary>
    [System.Text.Json.Serialization.JsonPropertyName("level")] public string? Level { get; set; }
    /// <summary>已知章节号（1-5 / 0=root）→ 导入时跳过逐章探测。</summary>
    [System.Text.Json.Serialization.JsonPropertyName("chapter")] public int? Chapter { get; set; }
    /// <summary>只改资源/覆盖文件，不带任何代码 patch → 跳过该 mod 的代码补丁扫描。</summary>
    [System.Text.Json.Serialization.JsonPropertyName("assets_only")] public bool AssetsOnly { get; set; }
    /// <summary>不声明任何函数 hook → 不参与 hook 收集。</summary>
    [System.Text.Json.Serialization.JsonPropertyName("no_hooks")] public bool NoHooks { get; set; }
    /// <summary>不依赖「控制台输入屏蔽」→ 当**所有**启用 mod 都这么声明时，可整体跳过输入重定向（这是部署里最贵的一步）。</summary>
    [System.Text.Json.Serialization.JsonPropertyName("no_console_input")] public bool NoConsoleInput { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("notes")] public string? Notes { get; set; }

    public bool IsFastLevel => string.Equals(Level, "fast", StringComparison.OrdinalIgnoreCase);

    /// <summary>把 level=fast 展开成逐项布尔。</summary>
    public void Normalize()
    {
        if (!IsFastLevel) return;
        AssetsOnly = NoHooks = NoConsoleInput = true;
    }

    /// <summary>人类可读的一行摘要（用于部署/冲突报告）。</summary>
    public string Describe()
    {
        var parts = new List<string>();
        if (Chapter.HasValue) parts.Add(L("章节") + Chapter.Value);
        if (AssetsOnly) parts.Add(L("仅资源"));
        if (NoHooks) parts.Add(L("无hook"));
        if (NoConsoleInput) parts.Add(L("不依赖控制台输入屏蔽"));
        return parts.Count == 0 ? L("无声明（走完整通道）") : string.Join(" / ", parts);
    }
}

/// <summary>一个 mod 章节内容（来自 mod.json）。Dir 指向章节目录。</summary>
public sealed class ModEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public bool Enabled { get; set; } = true;
    public string? Chapter { get; set; }
    public string? Main { get; set; }
    public string Dir { get; set; } = "";

    public List<ModPatch> Patches { get; set; } = new();
    public List<ModFindReplace> FindReplace { get; set; } = new();
    public List<string> PatchSkip { get; set; } = new();

    /// <summary>新增对象 + 事件（见 ModObject）。层由 --layer-from-base 自动生成。</summary>
    public List<ModObject> Objects { get; set; } = new();

    /// <summary>新增房间（见 ModRoom）。层由 --layer-from-base 自动生成；入口型 mod 的 room_goto 靠它落地。</summary>
    public List<ModRoom> Rooms { get; set; } = new();

    public string? RefSource { get; set; }
    public string? RefAssets { get; set; }
    public ModReferences? References { get; set; }

    /// <summary>作者自报的适配程度（可选）——见 ModAdapt。</summary>
    [System.Text.Json.Serialization.JsonPropertyName("ntl_adapt")] public ModAdapt? Adapt { get; set; }

    /// <summary>外部启动（Kristal 项目）——见 ModExternal。</summary>
    [System.Text.Json.Serialization.JsonPropertyName("kristal_external")] public ModExternal? KristalExternal { get; set; }

    /// <summary>加载顺序约束：在这些 mod（id）之后加载。</summary>
    public List<string> LoadAfter { get; set; } = new();

    /// <summary>加载顺序约束：在这些 mod（id）之前加载。</summary>
    public List<string> LoadBefore { get; set; } = new();

    /// <summary>章节声明原始数据（mod.json 的 chapters 段）。
    /// 支持两种写法：字符串 "Chapter:4:name" / "~Chapter:4:name"，
    /// 或结构化 { order, name, timeline, slug, mods }。</summary>
    [System.Text.Json.Serialization.JsonPropertyName("chapters")]
    public List<System.Text.Json.Nodes.JsonNode>? ChaptersRaw { get; set; }

    /// <summary>hooks 声明：[{ script, mode(pre/post/override), handler }]</summary>
    [System.Text.Json.Serialization.JsonPropertyName("hooks")]
    public List<System.Text.Json.Nodes.JsonNode>? HooksRaw { get; set; }

    /// <summary>该 mod 提供的接口（供依赖者使用），如 { "ns": "mymod", "functions": [...] }。</summary>
    public ModApi? Api { get; set; }

    /// <summary>依赖的其它 mod（id 列表）。</summary>
    public List<string> Dependencies { get; set; } = new();
}

/// <summary>mod 对外提供的接口描述。</summary>
public sealed class ModApi
{
    /// <summary>命名空间（代码中以此前缀引用，如 mymod.doThing(...)）。</summary>
    public string Ns { get; set; } = "";

    /// <summary>导出的函数：{ name, params, returns, desc }。</summary>
    public List<ModApiFunction> Functions { get; set; } = new();

    /// <summary>导出的常量：{ name, value, desc }。</summary>
    public List<ModApiConstant> Constants { get; set; } = new();
}

public sealed class ModApiFunction
{
    /// <summary>导出名（调用形式 &lt;ns&gt;.&lt;name&gt;(...)）。</summary>
    public string Name { get; set; } = "";

    /// <summary>实际脚本资源名（默认 &lt;ns&gt;_&lt;name&gt;，也可直接给 gml 文件名）。</summary>
    public string? Script { get; set; }

    public string Params { get; set; } = "";
    public string Returns { get; set; } = "";
    public string Desc { get; set; } = "";
}

public sealed class ModApiConstant
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public string Desc { get; set; } = "";
}

/// <summary>
/// mod 目录扫描。统一格式：
///   mods/&lt;mod_name&gt;/&lt;author_name&gt;/&lt;chapterN|root&gt;/mod.json + 资源
/// 兼容旧格式：mods/&lt;chapterN&gt;/&lt;mod&gt;/ 与 mods/&lt;mod&gt;/
/// </summary>
public static class Mods
{
    /// <summary>--no-cache：禁读扫描缓存（计时用）。</summary>
    public static bool NoCache = false;

    private static readonly Dictionary<string, List<ModEntry>> _scanCache = new();

    
    /// <summary>mods 树的**目录级指纹**：所有目录的 (相对路径, mtime) + 所有 mod.json 的 (大小, mtime)。
    /// 为什么够用：ScanMods 只产出「有哪些 mod、每个 mod 有哪些文件」这些**清单**，
    /// 文件内容变了不影响清单；而**增删文件会改父目录的 mtime**（Windows 语义），所以目录指纹能覆盖。
    /// 代价：只 stat 目录，不枚举文件、不解析 JSON —— 实测把 7.3s 的扫描降到百毫秒级。</summary>
    private static string ScanToken(string modsRoot)
    {
        var sb = new System.Text.StringBuilder(4096);
        try
        {
            var dirs = new List<string> { modsRoot };
            dirs.AddRange(Directory.GetDirectories(modsRoot, "*", SearchOption.AllDirectories));
            dirs.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (var d in dirs)
            {
                try { sb.Append(Path.GetRelativePath(modsRoot, d)).Append('=').Append(Directory.GetLastWriteTimeUtc(d).Ticks).Append(';'); }
                catch { }
            }
            var jsons = Directory.GetFiles(modsRoot, "mod.json", SearchOption.AllDirectories);
            Array.Sort(jsons, StringComparer.OrdinalIgnoreCase);
            foreach (var j in jsons)
            {
                try { var fi = new FileInfo(j); sb.Append(Path.GetRelativePath(modsRoot, j)).Append('=').Append(fi.Length).Append('@').Append(fi.LastWriteTimeUtc.Ticks).Append(';'); }
                catch { }
            }
        }
        catch { }
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes)[..24];
    }

    /// <summary>扫描结果磁盘缓存（键：参数组合；值：指纹 + 结果）。指纹不一致就重扫。</summary>
    private sealed class ScanCacheEntry
    {
        public string Token { get; set; } = "";
        public List<ModEntry> Mods { get; set; } = new();
    }

    private static string ScanCachePath(string modsRoot, string chapter, bool includeDisabled, bool allChapters)
    {
        var key = modsRoot + "|" + chapter + "|" + includeDisabled + "|" + allChapters;
        var h = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)))[..16];
        var root = Path.Combine(modsRoot, "..", "cache");
        return Path.GetFullPath(Path.Combine(root, "modscan-" + h + ".json"));
    }

    private static List<ModEntry>? ScanCacheLoad(string path, string token)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var e = System.Text.Json.JsonSerializer.Deserialize<ScanCacheEntry>(File.ReadAllText(path), Paths.Json);
            if (e == null || e.Token != token || e.Mods.Count == 0) return null;
            return e.Mods;
        }
        catch { return null; }
    }

    private static void ScanCacheSave(string path, string token, List<ModEntry> mods)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            Paths.SafeWrite(path, System.Text.Json.JsonSerializer.Serialize(new ScanCacheEntry { Token = token, Mods = mods }, Paths.Json));
        }
        catch { }
    }

public static List<ModEntry> ScanMods(string modsRoot, string chapter, bool includeDisabled = false, bool allChapters = false)
    {
        // ★ 记忆化：一次部署里 ScanMods 会被调用 3~4 次（依赖分析/注册表/签名），
        //   每次都遍历整棵 mods 树并解析所有 mod.json。参数相同 → 结果必然相同，缓存即可。
        var _ck = modsRoot + "|" + chapter + "|" + includeDisabled + "|" + allChapters;
        if (!NoCache) lock (_scanCache) { if (_scanCache.TryGetValue(_ck, out var _hit)) return new List<ModEntry>(_hit); }
        // ★ 磁盘缓存：目录指纹没变就直接复用上次的扫描结果（实测扫描 7.3s → 百毫秒级）
        var _token = ScanToken(modsRoot);
        var _cachePath = ScanCachePath(modsRoot, chapter, includeDisabled, allChapters);
        var _cached = NoCache ? null : ScanCacheLoad(_cachePath, _token);
        if (_cached != null)
        {
            lock (_scanCache) { _scanCache[_ck] = new List<ModEntry>(_cached); }
            Paths.Log(L("  [扫描] mods 清单命中磁盘缓存（{0} 个）", _cached.Count));
            return _cached;
        }

        var result = new List<ModEntry>();
        if (!Directory.Exists(modsRoot)) return result;

        var unpackRoot = Path.Combine(Path.GetDirectoryName(modsRoot)!, ".unpacked");
        var candidates = new List<(string dir, string chapter, string modName, string author)>();

        // 0) 打包形式的 mod（*.ntlmod，zip 内容）→ 解压到 .unpacked 后参与扫描
        foreach (var f in Directory.GetFiles(modsRoot, "*.ntlmod", SearchOption.AllDirectories))
        {
            var chFromDir = Path.GetFileName(Path.GetDirectoryName(f)!) ?? "";
            var ch = IsChapterFolder(chFromDir) ? chFromDir : (File.Exists(Path.Combine(Path.GetDirectoryName(f)!, "mod.json")) ? "" : "");
            if (ch == "")
            {
                // 允许 <chapter>.ntlmod 命名，或包名里带 -chapterN 后缀（--pack 的默认命名）
                var baseName = Path.GetFileNameWithoutExtension(f);
                if (IsChapterFolder(baseName)) ch = baseName;
                else
                {
                    // ⚠ 2026-09-26 修：这里以前写作 chapterd（\d 被写成 d），于是 --pack 的默认名
                    //   X-chapter4.ntlmod 永远匹配不上 → ch = "" → effChapter 为空 → Mods.cs:357 的
                    //   「空值不跳过任何章节」把它当成通配符，注入 root + chapter1..5（实为全章节注入）。
                    var m = System.Text.RegularExpressions.Regex.Match(baseName,
                        @"(?:^|[_-])(chapter\d|root)(?:$|[_-])",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (m.Success) ch = m.Groups[1].Value.ToLowerInvariant();
                }
            }
            try
            {
                var unpacked = UnpackIfNeeded(f, unpackRoot);
                var name = Path.GetFileName(Path.GetDirectoryName(f)!);
                if (string.IsNullOrEmpty(name) || IsChapterFolder(name)) name = Path.GetFileNameWithoutExtension(f);
                candidates.Add((unpacked, ch, name, "packed"));
                Paths.Log(L("  [打包 mod] {0} -> {1}", Path.GetFileName(f), unpacked));
            }
            catch (Exception ex) { Paths.Log(L("  [警告] 解包失败 {0}: {1}", f, ex.Message)); }
        }

        foreach (var modDir in Directory.GetDirectories(modsRoot))
        {
            var modName = Path.GetFileName(modDir);
            if (IsChapterFolder(modName))
            {
                // 旧格式 A: mods/<chapter>/<mod>/
                foreach (var sub in Directory.GetDirectories(modDir))
                    candidates.Add((sub, modName, Path.GetFileName(sub), ""));
                continue;
            }

            // 新格式: mods/<mod_name>/<author>/<chapter>/
            var subdirs = Directory.GetDirectories(modDir);
            bool matchedNew = false;
            foreach (var authorDir in subdirs)
            {
                var author = Path.GetFileName(authorDir);
                foreach (var chDir in Directory.GetDirectories(authorDir))
                {
                    var chName = Path.GetFileName(chDir);
                    if (!IsChapterFolder(chName)) continue;
                    candidates.Add((chDir, chName, modName, author));
                    matchedNew = true;
                }
            }
            if (matchedNew) continue;

            // 旧格式 B: mods/<mod>/
            if (File.Exists(Path.Combine(modDir, "mod.json")))
                candidates.Add((modDir, "", modName, ""));
        }

        foreach (var (dir, dirChapter, modName, author) in candidates)
        {
            var mj = Path.Combine(dir, "mod.json");
            if (!File.Exists(mj)) continue;
            ModEntry? m;
            try { m = JsonSerializer.Deserialize<ModEntry>(File.ReadAllText(mj), Paths.Json); }
            catch (Exception ex) { Paths.Log(L("  [警告] mod.json 解析失败: {0}: {1}", dir, ex.Message)); continue; }
            if (m == null) continue;

            m.Dir = dir;
            var effChapter = dirChapter != "" ? dirChapter : m.Chapter;
            m.Chapter = effChapter;

            if (string.IsNullOrEmpty(m.Name)) m.Name = modName;
            if (string.IsNullOrEmpty(m.Author)) m.Author = author;
            if (string.IsNullOrEmpty(m.Id))
                m.Id = author != "" ? $"{Sanitize(modName)}.{Sanitize(author)}" : Sanitize(modName);

            if (m.References != null)
            {
                m.RefSource ??= m.References.Source;
                m.RefAssets ??= m.References.Assets;
            }

            if (!includeDisabled && !m.Enabled) continue;
            if (!allChapters && !string.IsNullOrEmpty(effChapter) &&
                !string.Equals(effChapter, chapter, StringComparison.OrdinalIgnoreCase))
                continue;

            result.Add(m);
        }

        // 去重：同一 mod（Id + 章节）只保留第一个，重复的跳过并提示
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deduped = new List<ModEntry>();
        foreach (var m in result)
        {
            var key = m.Id + "|" + (m.Chapter ?? "");
            if (seen.Add(key)) deduped.Add(m);
            else Paths.Log(L("  [去重] {0} 已存在（{1}），跳过重复项", m.Id, m.Dir));
        }
        lock (_scanCache) { _scanCache[_ck] = new List<ModEntry>(deduped); }
        ScanCacheSave(_cachePath, _token, deduped);
        return deduped;
    }

    /// <summary>解包 .ntlmod（仅当内容变化时重新解压）。</summary>
    private static string UnpackIfNeeded(string ntlmodPath, string unpackRoot)
    {
        var name = Path.GetFileNameWithoutExtension(ntlmodPath);
        var target = Path.Combine(unpackRoot, Sanitize(name));
        var stampFile = target + ".stamp";
        var info = new FileInfo(ntlmodPath);
        var stamp = $"{info.Length}-{info.LastWriteTimeUtc.Ticks}";

        if (Directory.Exists(target) && File.Exists(stampFile) && File.ReadAllText(stampFile) == stamp)
            return target;

        if (Directory.Exists(target)) Directory.Delete(target, true);
        Directory.CreateDirectory(target);
        System.IO.Compression.ZipFile.ExtractToDirectory(ntlmodPath, target, true);
        File.WriteAllText(stampFile, stamp);
        return target;
    }

    /// <summary>按 load_after / load_before 拓扑排序（稳定；资源型基底最先；检测环）。</summary>
    public static List<ModEntry> SortByDependencies(List<ModEntry> mods)
    {
        if (mods.Count <= 1) return mods;

        var byId = new Dictionary<string, ModEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in mods) byId[m.Id] = m;

        var edges = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in mods) edges[m.Id] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var m in mods)
        {
            foreach (var dep in m.LoadAfter)
                if (byId.ContainsKey(dep) && !dep.Equals(m.Id, StringComparison.OrdinalIgnoreCase))
                    edges[dep].Add(m.Id);
            foreach (var dep in m.LoadBefore)
                if (byId.ContainsKey(dep) && !dep.Equals(m.Id, StringComparison.OrdinalIgnoreCase))
                    edges[m.Id].Add(dep);
        }

        // 资源型基底最先加载（其它 mod 的注入基础）。
        // 注意：一节只能有一个 data.win 基底（Deploy 取第一个），所以这里**只对第一个** inherit mod 建边；
        // 若对每个 inherit mod 都建边，4 个基底就会构成完全图 → 拓扑排序必然报"循环依赖"（伪告警）。
        var firstInherit = mods.FirstOrDefault(m => string.Equals(m.RefAssets, "inherit", StringComparison.OrdinalIgnoreCase));
        if (firstInherit != null)
            foreach (var other in mods.Where(x => !ReferenceEquals(x, firstInherit)))
                edges[firstInherit.Id].Add(other.Id);

        var indeg = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in mods) indeg[m.Id] = 0;
        foreach (var kv in edges)
            foreach (var to in kv.Value)
                if (indeg.ContainsKey(to)) indeg[to]++;

        var order = new List<ModEntry>();
        var remaining = new List<ModEntry>(mods);

        while (remaining.Count > 0)
        {
            var pick = remaining.FirstOrDefault(m => indeg[m.Id] == 0);
            if (pick == null)
            {
                Paths.Log(L("  [警告] 循环依赖，剩余按原顺序: ") + string.Join(", ", remaining.Select(r => r.Id)));
                order.AddRange(remaining);
                break;
            }
            order.Add(pick);
            remaining.Remove(pick);
            foreach (var to in edges[pick.Id])
                if (indeg.ContainsKey(to)) indeg[to]--;
        }

        Paths.Log(L("  加载顺序: ") + string.Join(" -> ", order.Select(m => m.Id)));
        return order;
    }

    public static bool IsChapterFolder(string name) =>
        name.Equals("root", StringComparison.OrdinalIgnoreCase) ||
        (name.StartsWith("chapter", StringComparison.OrdinalIgnoreCase) && name.Length > 7);

    public static string Sanitize(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in s) sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_');
        return sb.ToString().Trim('_');
    }
}
