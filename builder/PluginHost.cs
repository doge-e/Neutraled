using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json.Nodes;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>C# DLL 插件宿主（SPEC §3.5 的 PluginHost 半）。
///
/// 隔离模型：每个插件一份 **可卸载** 的 AssemblyLoadContext（isCollectible: true）+ AssemblyDependencyResolver。
/// 单插件加载/初始化/钩子抛异常 → 只把它自己记成失败，主程序与其它插件照常往下走
/// （插件是最不可控的第三方代码，这里绝不能让它把 builder 带崩）。
///
/// 三个实测教训，写在这里免得后来人再踩：
///   1) **宿主程序集必须解析回主程序那一份**。插件目录里常见「顺手拷一份 ntl-builder.exe」，
///      如果让解析器按目录解析，INeutraledPlugin 会出现**两份类型** → 强转静默失败（报「没有实现接口的类型」）。
///      所以 Resolving 里对宿主程序集名直接短路返回 typeof(PluginHost).Assembly。
///   2) **SetResult 每次调用前必须清空**。插件的 Reason 是「上一次调用写下的」这种串台，
///      在 return 非 0 时会把上一条原因当成这次的原因打印出去，排查时极易误判。
///   3) 钩子异常 ≠ 否决：异常记日志后**继续**下一个插件（否决只能由 return 非 0 表达）。</summary>
public static class PluginHost
{
    private sealed class Active
    {
        public string Id = "";
        public PluginManifest Manifest = new();
        public INeutraledPlugin Instance = null!;
        public PluginContext Ctx = null!;
        public AssemblyLoadContext? Alc;
    }

    private static readonly List<Active> _active = new();
    private static readonly List<string> _hookLog = new();
    private static readonly object _gate = new();
    private static bool _loaded;

    /// <summary>钩子调用日志上限（长跑不涨内存；只留最近这些条）。</summary>
    private const int HookLogLimit = 1000;

    /// <summary>宿主是否已加载完成（Load 跑过且没被 Unload）。**注意**：没有插件时也是 true —— 它描述的是宿主的意愿，不是插件数量。</summary>
    public static bool IsLoaded { get { lock (_gate) return _loaded; } }

    /// <summary>调试用钩子日志（--plugin-hooks 打印）。返回**快照**，调用方遍历时不会被钩子线程改到。</summary>
    public static List<string> HookLog { get { lock (_gate) return new List<string>(_hookLog); } }

    // ---------------------------------------------------------------- 加载 / 卸载

    /// <summary>加载全部启用插件。返回 0 = 全部就绪（含「一个插件都没有」）/ 1 = 有插件没加载成功。</summary>
    public static int Load(string gameRoot)
    {
        Unload();
        lock (_gate) _hookLog.Clear();   // 钩子日志按「本次加载」重置：--plugin-hooks 打印的就是这一次加载登记/触发的记录

        var entries = Plugins.List(gameRoot);
        if (entries.Count == 0) { lock (_gate) _loaded = true; return 0; }   // 没有插件：安静返回，不打任何噪音

        Paths.Log(L("===== 插件加载 ====="));
        var order = OrderByDeps(entries);
        var loadedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failed = 0;

        lock (_gate)
        {
            foreach (var e in order)
            {
                if (!string.Equals(e.State, PluginStates.Enabled, StringComparison.Ordinal))
                {
                    if (e.State == PluginStates.Broken)
                    {
                        failed++;
                        Paths.Log(L("  [跳过] {0}: 清单损坏（{1}）", e.Id, string.Join("；", e.Diagnostics)));
                    }
                    else if (e.State == PluginStates.Installed)
                    {
                        failed++;
                        Paths.Log(L("  [跳过] {0}: 缺少入口程序集", e.Id));
                    }
                    continue;   // disabled / local-only：用户自己的选择与手工目录，安静跳过
                }

                var missing = (e.Manifest?.Requires ?? new List<string>())
                    .Select(r => (r ?? "").Trim())
                    .Where(r => r.Length > 0 && !loadedIds.Contains(r))
                    .ToList();
                if (missing.Count > 0)
                {
                    failed++;
                    Paths.Log(L("[插件 {0}] 缺少依赖插件: {1}（跳过加载）", e.Id, string.Join(", ", missing)));
                    continue;
                }

                var clash = (e.Manifest?.Conflicts ?? new List<string>())
                    .Select(c => (c ?? "").Trim())
                    .Where(c => c.Length > 0 && loadedIds.Contains(c))
                    .ToList();
                if (clash.Count > 0)
                {
                    failed++;
                    Paths.Log(L("[插件 {0}] 与已加载插件冲突: {1}（跳过加载）", e.Id, string.Join(", ", clash)));
                    continue;
                }

                if (TryLoadOne(gameRoot, e, out var why)) loadedIds.Add(e.Id);
                else { failed++; Paths.Log(why); }
            }
            _loaded = true;
        }

        Paths.Log(L("  已加载 {0} 个插件", loadedIds.Count));
        if (failed > 0) Paths.Log(L("  有 {0} 个插件未加载（原因见上面各行）", failed));
        return failed == 0 ? 0 : 1;
    }

    /// <summary>卸载全部插件（逐个 OnShutdown → 卸载可卸载上下文）。可重复调用。</summary>
    public static void Unload()
    {
        List<Active> snapshot;
        lock (_gate)
        {
            if (_active.Count == 0) { _loaded = false; return; }
            snapshot = new List<Active>(_active);
            _active.Clear();
            _loaded = false;
        }

        // 逆序关闭：后加载的插件可能依赖先加载的
        for (var i = snapshot.Count - 1; i >= 0; i--)
        {
            var a = snapshot[i];
            try { a.Instance.OnShutdown(a.Ctx); }
            catch (Exception ex) { Paths.Log(L("[插件 {0}] 关闭失败: {1}", a.Id, ex.Message)); }
        }
        foreach (var a in snapshot)
        {
            try { a.Alc?.Unload(); } catch (Exception ex) { Paths.Log(L("[插件 {0}] 卸载上下文失败: {1}", a.Id, ex.Message)); }
        }
    }

    /// <summary>加载单个插件（调用方必须已持有 _gate）。失败时 why 是一整行可直接打印的消息。</summary>
    private static bool TryLoadOne(string gameRoot, PluginEntry e, out string why)
    {
        why = "";
        var m = e.Manifest ?? new PluginManifest();
        var rel = (m.Entry ?? "").Trim().Replace('\\', '/');
        var entryPath = Path.GetFullPath(Path.Combine(e.Dir, rel.Replace('/', Path.DirectorySeparatorChar)));
        if (!Plugins.IsUnder(Path.GetFullPath(e.Dir), entryPath) || !File.Exists(entryPath))
        {
            why = L("[插件 {0}] 入口程序集不存在: {1}", e.Id, m.Entry);
            return false;
        }

        AssemblyLoadContext? alc = null;
        try
        {
            alc = new AssemblyLoadContext("ntl-plugin-" + e.Id + "-" + Guid.NewGuid().ToString("N")[..6], isCollectible: true);

            AssemblyDependencyResolver? resolver = null;
            try { resolver = new AssemblyDependencyResolver(entryPath); } catch { resolver = null; }

            var hostAsm = typeof(PluginHost).Assembly;
            var hostName = hostAsm.GetName().Name;
            alc.Resolving += (ctx, name) =>
            {
                // ★ 教训 1：宿主程序集一律复用主程序那一份，否则接口类型出现两份 → 强转失败
                if (string.Equals(name.Name, hostName, StringComparison.OrdinalIgnoreCase)) return hostAsm;
                var p = resolver?.ResolveAssemblyToPath(name);
                return p != null && File.Exists(p) ? ctx.LoadFromAssemblyPath(p) : null;
            };
            if (resolver != null)
            {
                alc.ResolvingUnmanagedDll += (_, name) =>
                {
                    var p = resolver.ResolveUnmanagedDllToPath(name);
                    return p != null && File.Exists(p) ? NativeLibrary.Load(p) : IntPtr.Zero;
                };
            }

            var asm = alc.LoadFromAssemblyPath(entryPath);
            var type = FindPluginType(asm, m.Type);
            if (type == null)
            {
                why = string.IsNullOrWhiteSpace(m.Type)
                    ? L("[插件 {0}] 找不到实现 INeutraledPlugin 的类型", e.Id)
                    : L("[插件 {0}] 找不到实现 INeutraledPlugin 的类型: {1}", e.Id, m.Type.Trim());
                TryUnload(alc, e.Id);
                return false;
            }
            if (Activator.CreateInstance(type) is not INeutraledPlugin inst)
            {
                why = L("[插件 {0}] 无法实例化 {1}（需要公开的无参构造函数）", e.Id, type.FullName ?? type.Name);
                TryUnload(alc, e.Id);
                return false;
            }

            var ctx = new PluginContext(gameRoot, e.Dir, m, e.Id);
            try { inst.OnInit(ctx); }
            catch (Exception ex)
            {
                why = L("[插件 {0}] 初始化失败: {1}", e.Id, ex.Message);
                TryUnload(alc, e.Id);
                return false;
            }

            _active.Add(new Active { Id = e.Id, Manifest = m, Instance = inst, Ctx = ctx, Alc = alc });
            Paths.Log(L("  [加载] {0} {1}（权限 {2} 项）", e.Id, string.IsNullOrWhiteSpace(m.Version) ? "-" : m.Version, m.Permissions.Count));
            RegisterHooks(e.Id, inst);
            return true;
        }
        catch (Exception ex)
        {
            why = L("[插件 {0}] 加载失败: {1}", e.Id, ex.Message);
            TryUnload(alc, e.Id);
            return false;
        }
    }

    private static void TryUnload(AssemblyLoadContext? alc, string id)
    {
        try { alc?.Unload(); }
        catch (Exception ex) { Paths.Log(L("[插件 {0}] 卸载上下文失败: {1}", id, ex.Message)); }
    }

    /// <summary>找入口类型：清单给了 type 就精确取（且必须实现接口），否则取第一个非抽象实现。</summary>
    private static Type? FindPluginType(Assembly asm, string? typeName)
    {
        if (!string.IsNullOrWhiteSpace(typeName))
        {
            var t = asm.GetType(typeName.Trim(), throwOnError: false, ignoreCase: false);
            return t != null && typeof(INeutraledPlugin).IsAssignableFrom(t) ? t : null;
        }
        foreach (var t in asm.GetTypes())
        {
            if (t.IsAbstract || t.IsInterface) continue;
            if (typeof(INeutraledPlugin).IsAssignableFrom(t)) return t;
        }
        return null;
    }

    /// <summary>依赖拓扑排序（Kahn 变体的 DFS 实现）；有环只警告，环内按目录顺序加载。</summary>
    private static List<PluginEntry> OrderByDeps(List<PluginEntry> entries)
    {
        var byId = new Dictionary<string, PluginEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries) byId[e.Id] = e;

        var result = new List<PluginEntry>();
        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);   // 1=正在排 2=已排出
        var cyclic = new List<string>();

        void Visit(PluginEntry e)
        {
            if (state.TryGetValue(e.Id, out var s))
            {
                if (s == 1) cyclic.Add(e.Id);   // 回到自己/祖先 = 有环
                return;
            }
            state[e.Id] = 1;
            foreach (var req in e.Manifest?.Requires ?? new List<string>())
            {
                var dep = (req ?? "").Trim();
                if (dep.Length > 0 && byId.TryGetValue(dep, out var d)) Visit(d);
            }
            state[e.Id] = 2;
            result.Add(e);
        }

        foreach (var e in entries) Visit(e);
        if (cyclic.Count > 0) Paths.Log(L("  依赖顺序有环，环内插件按目录顺序加载: {0}", string.Join(", ", cyclic)));
        return result;
    }

    // ---------------------------------------------------------------- 钩子

    /// <summary>触发钩子：按依赖/目录顺序调用；任一插件返回非 0 → 立刻中止并返回该值（原因由它 SetResult 写下）。
    /// 未知钩子、没有插件、宿主未加载 → 安静返回 0。</summary>
    public static int Fire(string hook, string? payloadJson = null)
    {
        if (string.IsNullOrWhiteSpace(hook)) return 0;
        if (!PluginHooks.IsKnown(hook)) return 0;      // 未知钩子不报错

        lock (_gate)
        {
            if (_active.Count == 0) return 0;
            foreach (var a in _active)
            {
                a.Ctx.Data = payloadJson;
                a.Ctx.ClearResult();                       // ★ 教训 2：清空上一次的原因，避免串台
                var sw = System.Diagnostics.Stopwatch.StartNew();
                int rc;
                try { rc = a.Instance.OnHook(hook, a.Ctx); }
                catch (Exception ex)
                {
                    sw.Stop();
                    // ★ 教训 3：异常 ≠ 否决 → 记录后继续（否决只能由 return 非 0 表达）
                    Paths.Log(L("[插件 {0}] 钩子异常: {1}", a.Id, ex.Message));
                    HookRow(hook, "ERR", a.Id, sw.ElapsedMilliseconds, ex.Message);
                    continue;
                }
                sw.Stop();
                HookRow(hook, rc.ToString(), a.Id, sw.ElapsedMilliseconds, rc == 0 ? "" : a.Ctx.ResultText);
                if (rc == 0) continue;

                var reason = (a.Ctx.ResultText ?? "").Trim();
                if (reason.Length == 0) Paths.Log(L("[插件 {0}] 否决了 {1}（返回 {2}）", a.Id, hook, rc));
                else Paths.Log(L("[插件 {0}] 否决了 {1}（返回 {2}）: {3}", a.Id, hook, rc, reason));
                return rc;
            }
            return 0;
        }
    }

    /// <summary>钩子日志一行（制表符分隔的**数据**，不进 i18n 表：--plugin-hooks 的机器判据靠它）。</summary>
    private static void HookRow(string hook, string rc, string id, long ms, string? note)
    {
        if (_hookLog.Count >= HookLogLimit) _hookLog.RemoveRange(0, _hookLog.Count - HookLogLimit + 1);
        _hookLog.Add((note ?? "").Length == 0
            ? $"{hook}\t{rc}\t{id}\t{ms}ms"
            : $"{hook}\t{rc}\t{id}\t{ms}ms\t{note}");
    }

    /// <summary>登记插件**声明**的钩子（每个插件一行；列格式与 HookRow 不同，机器判据不会混淆）：
    ///   register TAB &lt;插件 id&gt; TAB &lt;钩子名,逗号分隔&gt;
    /// 为什么需要它：HookLog 原本只有 Fire 才写，于是「插件加载成功」与「钩子清单为空」在
    /// --plugin-hooks 的输出里分不开（没有任何操作触发时永远是 0 行）。派生 PluginBase（或自己实现
    /// IHookDeclaringPlugin）的插件在这里留下自己的钩子清单；未声明的钩子依旧会被送进 OnHook。</summary>
    private static void RegisterHooks(string id, INeutraledPlugin inst)
    {
        if (inst is not IHookDeclaringPlugin dp) return;
        var hooks = new List<string>();
        foreach (var raw in dp.DeclaredHooks ?? Array.Empty<string>())
        {
            var h = (raw ?? "").Trim();
            if (!PluginHooks.IsKnown(h) || hooks.Contains(h, StringComparer.Ordinal)) continue;
            hooks.Add(h);
        }
        if (hooks.Count == 0) return;
        if (_hookLog.Count >= HookLogLimit) _hookLog.RemoveRange(0, _hookLog.Count - HookLogLimit + 1);
        _hookLog.Add("register\t" + id + "\t" + string.Join(",", hooks));
    }

    /// <summary>把插件计数打印出来（供 --plugin-load 之后的自检输出用；没有插件时安静）。</summary>
    public static void PrintStatus()
    {
        if (!IsLoaded) { Paths.Log(L("插件宿主尚未加载")); return; }
        List<string> ids;
        lock (_gate) ids = _active.Select(a => a.Id).ToList();
        Paths.Log(ids.Count == 0 ? L("没有已加载的插件") : L("已加载插件: {0}", string.Join(", ", ids)));
    }
}

/// <summary>IPluginContext 的宿主实现。敏感能力（配置读写）在**调用点**做权限真检查：
/// 未声明 → 打印 [插件 &lt;id&gt;] 缺少权限 &lt;perm&gt;，已拒绝，然后拒绝服务（读取返回 null，写入不落盘）。</summary>
internal sealed class PluginContext : IPluginContext
{
    private readonly string _gameRoot;

    public PluginContext(string gameRoot, string pluginDir, PluginManifest manifest, string id)
    {
        _gameRoot = gameRoot;
        PluginDir = pluginDir;
        Manifest = manifest;
        Id = id;
    }

    public string Id { get; }
    public PluginManifest Manifest { get; }

    public string GameRoot => _gameRoot;
    public string NeutraledRoot => Paths.NeutraledRoot(_gameRoot);
    public string PluginDir { get; }
    public IReadOnlyList<string> Permissions => Manifest.Permissions;
    public string Language => Lang.Current;

    /// <summary>当前钩子负载（JSON 文本）。OnInit/OnShutdown 期间为 null。</summary>
    public string? Data { get; internal set; }

    /// <summary>插件最后一次 SetResult 写下的原因（Fire 在它否决时打印）。</summary>
    public string ResultText { get; private set; } = "";

    public void SetResult(string json) => ResultText = json ?? "";

    /// <summary>每次钩子调用前清空（避免上一轮的原因串台）。</summary>
    internal void ClearResult() => ResultText = "";

    public void Log(string message) => Paths.Log(L("[插件 {0}]", Id) + " " + (message ?? ""));

    public void Warn(string message) => Paths.Log(L("[插件 {0}] [警告] {1}", Id, message ?? ""));

    /// <summary>权限自查（插件在 OnInit 里也该先查一遍）。支持 "*" 表示全权；大小写不敏感。</summary>
    public bool HasPermission(string perm)
    {
        if (string.IsNullOrWhiteSpace(perm)) return true;
        var p = perm.Trim();
        foreach (var d in Permissions)
        {
            var t = (d ?? "").Trim();
            if (t.Length == 0) continue;
            if (t == "*") return true;
            if (string.Equals(t, p, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>读配置：需要 files.read（声明了 files.write 也放行 —— 写能覆盖读）。未声明 → 拒绝并打印。</summary>
    public string? ReadConfig(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        if (!HasPermission(PluginPermissions.FilesRead) && !HasPermission(PluginPermissions.FilesWrite))
        {
            Deny(PluginPermissions.FilesRead);
            return null;
        }
        try { return ConfigFile.GetString(_gameRoot, key.Trim()); }
        catch (Exception ex) { Warn(L("读配置失败: {0}", ex.Message)); return null; }
    }

    /// <summary>写配置：需要 files.write。未声明 → 拒绝并打印（value=null 表示删键）。</summary>
    public void WriteConfig(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        if (!HasPermission(PluginPermissions.FilesWrite)) { Deny(PluginPermissions.FilesWrite); return; }
        try { ConfigFile.Set(_gameRoot, key.Trim(), value == null ? null : JsonValue.Create(value)); }
        catch (Exception ex) { Warn(L("写配置失败: {0}", ex.Message)); }
    }

    private void Deny(string perm) => Paths.Log(L("[插件 {0}] 缺少权限 {1}，已拒绝", Id, perm));
}
