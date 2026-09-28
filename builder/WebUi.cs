using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>零依赖本地网页界面（System.Net.HttpListener，Windows / Linux / macOS 通用）。
/// 设计口径：
///   · **只绑回环**：http://127.0.0.1:&lt;port&gt;/（被 URL ACL 拒绝时退到 localhost），被占用则端口 +1 重试；
///     每个请求再校验一次来源，非本机一律 403 —— 网页端没有登录态，绝不能暴露到局域网；
///   · **零业务逻辑**：配置档 / 快照 / 恢复点 / 下载队列 / 插件 / 主题 / 语言全部转调对应模块的公开 API，
///     与 CLI 走同一条代码路径（不会出现「网页改了但 CLI 不认」的分叉）；部署走 Captain 注入的 DeployHook；
///   · **不崩**：所有路由包在 try/catch 里，异常一律 500 + {"error":"…"}；静态资源缺失也不影响接口；
///   · 请求体上限 1 MB（本地面板没有大请求，设上限只为防误连/恶意占用内存）。
/// 实测教训（沿用仓库既有约定）：
///   · 打开浏览器走各平台自己的命令（cmd /c start / open / xdg-open），**失败静默** ——
///     打不开浏览器不算启动失败，URL 已经打在控制台上了；
///   · 破坏性操作（快照回切 / 恢复点恢复 / 部署 / 队列执行）由前端二次确认，服务端只做参数校验与转发；
///   · 来自 JSON / 查询串的 id（modId、version、配置档 id、恢复点 id、chapter）一律先过 SafeToken，
///     拒绝 .. 与路径分隔符，避免任何形式的路径越界。</summary>
public static class WebUi
{
    /// <summary>部署回调 (gameRoot, chapter) -&gt; exitCode；由 Program.cs 注入（--web 时指向既有部署流程）。
    /// 未注入时 /api/deploy 返回 501，其余接口照常可用。</summary>
    public static Func<string, string, int>? DeployHook { get; set; }

    /// <summary>服务是否在跑（Stop / POST /api/shutdown 之后变 false）。</summary>
    public static bool IsRunning { get; private set; }

    /// <summary>实际绑定的端口（端口冲突会自动 +1，Start 的返回值以此为准）。</summary>
    public static int Port { get; private set; }

    private const int DefaultPort = 7931;
    private const int MaxBody = 1024 * 1024;   // 1 MB
    private const int MaxPortTries = 6;        // 首次 + 最多 5 次递增重试
    private const int IdleDefaultMs = 120_000; // --auto-stop 的空闲上限：这么久没有请求就自己退出

    private static readonly object Gate = new();
    private static readonly object ConsoleGate = new();
    private static readonly ManualResetEventSlim StopEvent = new(false);

    private static HttpListener? _listener;
    private static string _gameRoot = "";
    private static string _webRoot = "";
    private static string _lastBindError = "";
    private static int _requestedPort = DefaultPort;
    private static volatile bool _stopping;
    private static long _lastActivity;         // 最近一次收到请求的时刻（TickCount64），空闲看门狗用它判断"还有人在用吗"

    /// <summary>已注册的全部 API 路由（PrintStatus 逐条打印，便于 grep 断言）。</summary>
    private static readonly string[] ApiRoutes =
    {
        "GET /api/status", "GET /api/mods", "POST /api/mods/toggle",
        "GET /api/profiles", "POST /api/profile/use", "POST /api/profile/save",
        "GET /api/snapshots", "POST /api/snapshot/use",
        "GET /api/restore", "POST /api/restore/apply",
        "GET /api/gb/search", "POST /api/gb/install",
        "GET /api/queue", "POST /api/queue/run",
        "GET /api/plugins",
        "GET /api/themes", "POST /api/theme/use",
        "GET /api/lang", "POST /api/lang/use", "GET /api/lang/coverage",
        "POST /api/deploy", "POST /api/shutdown"
    };

    // ================================================================= 生命周期

    /// <summary>启动本地网页界面。
    /// 返回码：0 = 正常结束（含 POST /api/shutdown 与 Ctrl+C）；1 = 端口绑定失败（{port}..{port+5} 全占用或无权限）；
    ///         2 = 游戏根无效（目录不存在）；3 = 已在运行（重复调用）。
    /// autoStop=true 时：照常对外服务，但空闲超过 NTL_WEB_IDLE_MS（默认 120 秒）没有请求就自己停 ——
    /// 这样 CLI 的 --web --auto-stop 既真的能用（不是"启动即返回"然后进程随 Main 一起没），也不会永久挂着；
    /// false 时阻塞到 Ctrl+C 或 /api/shutdown。</summary>
    public static int Start(string gameRoot, int port = DefaultPort, bool openBrowser = true, bool autoStop = false)
    {
        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
        {
            Paths.Log("[Web] " + L("游戏根不存在: {0}", gameRoot ?? "(null)"));
            return 2;
        }

        lock (Gate)
        {
            if (IsRunning)
            {
                Paths.Log("[Web] " + L("网页界面已在运行: {0}", Url()));
                return 3;
            }

            _gameRoot = Path.GetFullPath(gameRoot);
            _requestedPort = port;
            _stopping = false;
            _lastBindError = "";
            StopEvent.Reset();
            _webRoot = LocateWebRoot(_gameRoot);

            HttpListener? listener = null;
            int bound = 0;
            for (int i = 0; i < MaxPortTries && listener == null; i++)
            {
                int tryPort = port + i;
                if (tryPort <= 0 || tryPort > 65535) break;
                // 每个端口先试 127.0.0.1（真正的只绑本机）；被 URL ACL 拒绝时退到 localhost（http.sys 对 localhost 更宽松）
                foreach (var host in new[] { "127.0.0.1", "localhost" })
                {
                    var cand = new HttpListener();
                    cand.Prefixes.Add("http://" + host + ":" + tryPort + "/");
                    try
                    {
                        cand.Start();
                        listener = cand;
                        bound = tryPort;
                        break;
                    }
                    catch (Exception ex)
                    {
                        _lastBindError = ex.Message;
                        try { cand.Close(); } catch { /* 已经废掉的候选，忽略关闭异常 */ }
                    }
                }
            }

            if (listener == null)
            {
                Paths.Log("[Web] " + L("无法绑定端口 {0}-{1}: {2}", port, port + MaxPortTries - 1, _lastBindError));
                return 1;
            }

            _listener = listener;
            Port = bound;
            IsRunning = true;
            if (bound != port) Paths.Log("[Web] " + L("端口 {0} 被占用，已改用 {1}", port, bound));
        }

        var url = Url();
        Paths.Log("[Web] " + L("网页界面已启动: {0}", url));
        Paths.Log("[Web] " + L("游戏根: {0}", _gameRoot));
        Paths.Log("[Web] " + L("静态目录: {0}", _webRoot.Length > 0 ? _webRoot : L("(未找到 web/，改用内置页面)")));
        if (openBrowser) OpenBrowser(url);

        // 线程始终是后台线程：真正的"守着进程"由下面的等待完成，线程本身不该把进程钉住。
        var accept = new Thread(AcceptLoop) { IsBackground = true, Name = "ntl-web-accept" };
        accept.Start();

        ConsoleCancelEventHandler onCancel = (s, e) => { e.Cancel = true; StopEvent.Set(); };
        Console.CancelKeyPress += onCancel;
        try
        {
            if (autoStop) WaitIdle(IdleMs());   // 自停模式：有请求就一直服务，空闲够久就退出
            else StopEvent.Wait();              // 常驻模式：等到 Ctrl+C 或 POST /api/shutdown
        }
        finally { Console.CancelKeyPress -= onCancel; }

        if (IsRunning) Stop();
        Paths.Log("[Web] " + L("网页界面已停止"));
        return 0;
    }

    /// <summary>空闲上限（毫秒）：默认 2 分钟，环境变量 NTL_WEB_IDLE_MS 可覆盖（自动化验证用）。</summary>
    private static int IdleMs()
    {
        var raw = Environment.GetEnvironmentVariable("NTL_WEB_IDLE_MS");
        if (int.TryParse(raw, out var ms) && ms >= 200 && ms <= 86_400_000) return ms;
        return IdleDefaultMs;
    }

    /// <summary>autoStop 的等待循环：每 200ms 看一次"最后活动时间"，空闲到点就自己停。</summary>
    private static void WaitIdle(int idleMs)
    {
        Touch();
        while (!StopEvent.Wait(200))
        {
            if (Environment.TickCount64 - Interlocked.Read(ref _lastActivity) < idleMs) continue;
            Paths.Log("[Web] " + L("空闲 {0} 秒无请求，网页界面自动关闭", Math.Max(1, idleMs / 1000)));
            break;
        }
    }

    /// <summary>记一次活动（任何请求都算），供空闲看门狗使用。</summary>
    private static void Touch() => Interlocked.Exchange(ref _lastActivity, Environment.TickCount64);

    /// <summary>停止服务。返回 0 = 已停止；1 = 本来就没在运行。</summary>
    public static int Stop()
    {
        lock (Gate)
        {
            StopEvent.Set();
            if (!IsRunning && _listener == null) return 1;
            _stopping = true;
            IsRunning = false;
            var l = _listener;
            _listener = null;
            try { l?.Stop(); } catch { /* 停止中的异常一律忽略 */ }
            try { l?.Close(); } catch { }
            return 0;
        }
    }

    /// <summary>--web-status 的人可读输出（状态 / 地址 / 游戏根 / 静态目录 / 路由清单）。</summary>
    public static void PrintStatus()
    {
        Paths.Log("[Web] " + L("状态: {0}", IsRunning ? L("运行中") : L("未运行")));
        if (IsRunning)
        {
            Paths.Log("[Web] " + L("地址: {0}", Url()));
            Paths.Log("[Web] " + L("游戏根: {0}", _gameRoot));
            Paths.Log("[Web] " + L("静态目录: {0}", _webRoot.Length > 0 ? _webRoot : L("(未找到 web/，改用内置页面)")));
        }
        else
        {
            Paths.Log("[Web] " + L("默认端口: {0}", DefaultPort));
            Paths.Log("[Web] " + L("用法: 由主程序把 WebUi.Start(gameRoot, port, openBrowser, autoStop) 接进 CLI"));
        }
        Paths.Log("[Web] " + L("路由: {0} 个", ApiRoutes.Length));
        foreach (var r in ApiRoutes) Paths.Log("[Web]   " + r);
    }

    private static string Url() => "http://127.0.0.1:" + (IsRunning ? Port : _requestedPort) + "/";

    // ================================================================= 接受循环

    private static void AcceptLoop()
    {
        var l = _listener;
        if (l == null) return;
        while (!_stopping)
        {
            HttpListenerContext ctx;
            try { ctx = l.GetContext(); }
            catch { break; }   // Stop/Close 会让 GetContext 抛异常，这就是退出信号
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { HandleAsync(ctx).GetAwaiter().GetResult(); }
                catch (Exception ex) { Fail(ctx, 500, L("服务器内部错误: {0}", ex.Message)); }
            });
        }
    }

    // ================================================================= 路由

    private static async Task HandleAsync(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var path = req.Url?.AbsolutePath ?? "/";
        var method = (req.HttpMethod ?? "GET").ToUpperInvariant();
        Touch();   // 空闲看门狗：任何请求都算"有人在用"
        // HEAD 复用 GET 的路由，只是不写正文：HttpListener 对 HEAD 写正文会抛异常并把连接重置（RST）
        if (method == "HEAD") method = "GET";

        // 只允许本机：前缀本身已是回环地址，这里再挡一次（双保险，防止未来有人把前缀放宽）
        if (!req.IsLocal || !IsLoopback(req.RemoteEndPoint))
        {
            Fail(ctx, 403, L("只允许本机访问"));
            return;
        }

        // 静态文件（web/index.html、app.js、style.css 以及其它随包资源）
        if (method == "GET" && !path.StartsWith("/api/", StringComparison.Ordinal))
        {
            if (path == "/" || path.Equals("/index.html", StringComparison.OrdinalIgnoreCase)) { ServeIndex(ctx); return; }
            if (ServeStatic(ctx, path)) return;
            Fail(ctx, 404, L("找不到资源: {0}", path));
            return;
        }

        try
        {
            // POST 体必须在写响应前读完（HttpListener 对未读完的请求会重置连接）
            JsonObject body = method == "POST" ? ReadBody(req) : new JsonObject();

            switch (method + " " + path)
            {
                case "POST /api/shutdown":
                    Json(ctx, 200, new JsonObject { ["ok"] = true, ["message"] = L("正在关闭") });
                    StopEvent.Set();
                    return;

                case "GET /api/status":
                {
                    var mods = ScanAll();
                    var profIds = new JsonArray();
                    foreach (var p in Profiles.List(_gameRoot)) profIds.Add(Str(Obj(p), "id", "Id"));
                    Json(ctx, 200, new JsonObject
                    {
                        ["gameRoot"] = _gameRoot,
                        ["platform"] = Platform.Name,
                        ["chapterSuffix"] = Platform.ChapterSuffix(_gameRoot),
                        ["mods"] = mods.Count,
                        ["enabled"] = mods.Count(x => x.Enabled),
                        ["profiles"] = profIds,
                        ["active_profile"] = Profiles.ActiveId(_gameRoot),
                        ["lang"] = CurrentLang(),
                        ["theme"] = Themes.ActiveId(_gameRoot),
                        ["version"] = Paths.ApiVersion(),
                        ["port"] = Port,
                        ["from"] = "web"
                    });
                    return;
                }

                case "GET /api/mods":
                {
                    var arr = new JsonArray();
                    foreach (var m in ScanAll())
                        arr.Add(new JsonObject
                        {
                            ["id"] = m.Id,
                            ["name"] = m.Name,
                            ["author"] = m.Author,
                            ["version"] = m.Version,
                            ["enabled"] = m.Enabled,
                            ["chapter"] = m.Chapter ?? "",
                            ["dir"] = m.Dir
                        });
                    Json(ctx, 200, arr);
                    return;
                }

                case "POST /api/mods/toggle":
                {
                    var id = Str(body, "id", "modId");
                    if (!SafeToken(id)) throw new WebBadRequest(L("mod id 非法: {0}", id));
                    if (Pick(body, "enabled", "Enabled") == null) throw new WebBadRequest(L("缺少 enabled 参数"));
                    bool want = Flag(body, "enabled", "Enabled");

                    var hit = ScanAll().FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
                    if (hit == null) throw new WebBadRequest(L("找不到 mod: {0}", id));

                    Profiles.RewriteModEnabled(_gameRoot, hit, want);
                    Json(ctx, 200, new JsonObject { ["ok"] = true, ["id"] = hit.Id, ["enabled"] = want });
                    return;
                }

                case "GET /api/profiles":
                {
                    var active = Profiles.ActiveId(_gameRoot);
                    var arr = new JsonArray();
                    foreach (var p in Profiles.List(_gameRoot))
                    {
                        var o = Obj(p);
                        var id = Str(o, "id", "Id");
                        arr.Add(new JsonObject
                        {
                            ["id"] = id,
                            ["name"] = Str(o, "name", "Name"),
                            ["description"] = Str(o, "description", "Description"),
                            ["enabled"] = Pick(o, "enabled", "Enabled") ?? new JsonArray(),
                            ["chapters"] = Pick(o, "chapters", "Chapters") ?? new JsonArray(),
                            ["active"] = string.Equals(id, active, StringComparison.OrdinalIgnoreCase)
                        });
                    }
                    Json(ctx, 200, arr);
                    return;
                }

                case "POST /api/profile/use":
                {
                    var id = Str(body, "id");
                    if (!SafeToken(id)) throw new WebBadRequest(L("配置档 id 非法: {0}", id));
                    int changed = Profiles.Apply(_gameRoot, id);
                    Json(ctx, 200, new JsonObject { ["ok"] = true, ["id"] = id, ["changed"] = changed });
                    return;
                }

                case "POST /api/profile/save":
                {
                    var id = Str(body, "id");
                    if (!SafeToken(id)) throw new WebBadRequest(L("配置档 id 非法: {0}", id));
                    var name = Str(body, "name");
                    if (Profiles.Load(_gameRoot, id) == null)
                        Profiles.Create(_gameRoot, id, string.IsNullOrWhiteSpace(name) ? id : name);
                    int n = Profiles.CaptureFromLive(_gameRoot, id);
                    Json(ctx, 200, new JsonObject { ["ok"] = true, ["id"] = id, ["captured"] = n });
                    return;
                }

                case "GET /api/snapshots":
                {
                    var modId = (req.QueryString["modId"] ?? "").Trim();
                    if (modId.Length > 0 && !SafeToken(modId)) throw new WebBadRequest(L("mod id 非法: {0}", modId));
                    var arr = new JsonArray();
                    foreach (var s in Snapshots.List(_gameRoot, modId.Length > 0 ? modId : null))
                    {
                        var o = Obj(s);
                        arr.Add(new JsonObject
                        {
                            ["modId"] = Str(o, "modId", "ModId"),
                            ["version"] = Str(o, "version", "Version"),
                            ["name"] = Str(o, "name", "Name"),
                            ["author"] = Str(o, "author", "Author"),
                            ["created"] = Str(o, "created", "Created"),
                            ["source"] = Str(o, "source", "Source"),
                            ["files"] = Long(o, "files", "Files"),
                            ["bytes"] = Long(o, "bytes", "Bytes"),
                            ["note"] = Str(o, "note", "Note")
                        });
                    }
                    Json(ctx, 200, arr);
                    return;
                }

                case "POST /api/snapshot/use":
                {
                    var modId = Str(body, "modId", "id");
                    var version = Str(body, "version");
                    if (!SafeToken(modId)) throw new WebBadRequest(L("mod id 非法: {0}", modId));
                    if (!SafeToken(version)) throw new WebBadRequest(L("快照版本非法: {0}", version));
                    bool force = Flag(body, "force", "Force");
                    int n = Snapshots.Use(_gameRoot, modId, version, force);
                    Json(ctx, 200, new JsonObject { ["ok"] = true, ["modId"] = modId, ["version"] = version, ["restored"] = n });
                    return;
                }

                case "GET /api/restore":
                {
                    var arr = new JsonArray();
                    foreach (var p in RestorePoints.List(_gameRoot))
                    {
                        var o = Obj(p);
                        arr.Add(new JsonObject
                        {
                            ["id"] = Str(o, "id", "Id"),
                            ["name"] = Str(o, "name", "Name"),
                            ["created"] = Str(o, "created", "Created"),
                            ["from"] = Str(o, "from", "From"),
                            ["profileId"] = Str(o, "profileId", "ProfileId"),
                            ["chapters"] = Pick(o, "chapters", "Chapters") ?? new JsonArray(),
                            ["mods"] = Pick(o, "mods", "Mods") ?? new JsonArray(),
                            ["bytes"] = Long(o, "bytes", "Bytes"),
                            ["hasData"] = Flag(o, "hasData", "HasData"),
                            ["gameVersion"] = Str(o, "gameVersion", "GameVersion")
                        });
                    }
                    Json(ctx, 200, arr);
                    return;
                }

                case "POST /api/restore/apply":
                {
                    var id = Str(body, "id", "pointId");
                    if (!SafeToken(id)) throw new WebBadRequest(L("恢复点 id 非法: {0}", id));
                    bool force = Flag(body, "force", "Force");
                    int n = RestorePoints.Apply(_gameRoot, id, force);
                    Json(ctx, 200, new JsonObject { ["ok"] = true, ["id"] = id, ["applied"] = n });
                    return;
                }

                case "GET /api/gb/search":
                {
                    var q = (req.QueryString["q"] ?? "").Trim();
                    if (q.Length == 0) throw new WebBadRequest(L("缺少查询词 q"));
                    int perPage = 15;
                    if (int.TryParse(req.QueryString["perPage"], out var pp)) perPage = Math.Clamp(pp, 1, 50);

                    var hits = await GameBanana.SearchAsync(q, perPage);
                    int filtered = Blacklist.FilterOut(_gameRoot, hits);

                    var arr = new JsonArray();
                    foreach (var h in hits)
                    {
                        var o = Obj(h);
                        arr.Add(new JsonObject
                        {
                            ["id"] = Long(o, "id", "Id"),
                            ["name"] = Str(o, "name", "Name"),
                            ["model"] = Str(o, "model", "Model"),
                            ["author"] = Str(o, "author", "Author"),
                            ["profileUrl"] = Str(o, "profileUrl", "ProfileUrl"),
                            ["description"] = Str(o, "description", "Description"),
                            ["downloadCount"] = Long(o, "downloadCount", "DownloadCount"),
                            ["likeCount"] = Long(o, "likeCount", "LikeCount"),
                            ["updated"] = Str(o, "updated", "Updated")
                        });
                    }
                    Json(ctx, 200, new JsonObject
                    {
                        ["ok"] = true,
                        ["query"] = q,
                        ["count"] = arr.Count,
                        ["filtered"] = filtered,
                        ["results"] = arr
                    });
                    return;
                }

                case "POST /api/gb/install":
                {
                    var modIdText = Str(body, "modId", "id");
                    if (!int.TryParse(modIdText, out var modId) || modId <= 0)
                        throw new WebBadRequest(L("modId 非法: {0}", modIdText));
                    var type = Str(body, "type");
                    var chapter = Str(body, "chapter");
                    if (chapter.Length > 0 && !SafeToken(chapter)) throw new WebBadRequest(L("章节名非法: {0}", chapter));
                    int? fileId = int.TryParse(Str(body, "fileId"), out var fid) && fid > 0 ? fid : null;
                    bool force = Flag(body, "force", "Force");

                    int code = await GbBrowse.Install(_gameRoot, modId,
                        type.Length > 0 ? type : null,
                        fileId,
                        chapter.Length > 0 ? chapter : null,
                        force);
                    Json(ctx, 200, new JsonObject { ["ok"] = code == 0, ["modId"] = modId, ["code"] = code });
                    return;
                }

                case "GET /api/queue":
                {
                    var arr = new JsonArray();
                    foreach (var it in DownloadQueue.List(_gameRoot))
                    {
                        var o = Obj(it);
                        arr.Add(new JsonObject
                        {
                            ["id"] = Str(o, "id", "Id"),
                            ["modId"] = Long(o, "modId", "ModId"),
                            ["modName"] = Str(o, "modName", "ModName"),
                            ["fileName"] = Str(o, "fileName", "FileName"),
                            ["state"] = Str(o, "state", "State"),
                            ["error"] = Str(o, "error", "Error"),
                            ["added"] = Str(o, "added", "Added"),
                            ["updated"] = Str(o, "updated", "Updated"),
                            ["size"] = Long(o, "size", "Size"),
                            ["got"] = Long(o, "got", "Got"),
                            ["url"] = Str(o, "url", "Url"),
                            ["localPath"] = Str(o, "localPath", "LocalPath")
                        });
                    }
                    Json(ctx, 200, arr);
                    return;
                }

                case "POST /api/queue/run":
                {
                    bool autoInstall = Pick(body, "autoInstall") == null || Flag(body, "autoInstall");
                    bool deleteAfter = Flag(body, "deleteAfter");
                    int n = DownloadQueue.Run(_gameRoot, autoInstall, deleteAfter);
                    Json(ctx, 200, new JsonObject { ["ok"] = true, ["processed"] = n });
                    return;
                }

                case "GET /api/plugins":
                {
                    var arr = new JsonArray();
                    foreach (var p in Plugins.List(_gameRoot))
                    {
                        var o = Obj(p);
                        var manifest = Pick(o, "manifest", "Manifest") as JsonObject;
                        arr.Add(new JsonObject
                        {
                            ["id"] = Str(o, "id", "Id"),
                            ["state"] = Str(o, "state", "State"),
                            ["dir"] = Str(o, "dir", "Dir"),
                            ["diagnostics"] = Pick(o, "diagnostics", "Diagnostics") ?? new JsonArray(),
                            ["name"] = manifest != null ? Str(manifest, "name", "Name") : "",
                            ["version"] = manifest != null ? Str(manifest, "version", "Version") : "",
                            ["author"] = manifest != null ? Str(manifest, "author", "Author") : "",
                            ["description"] = manifest != null ? Str(manifest, "description", "Description") : "",
                            ["permissions"] = (manifest != null ? Pick(manifest, "permissions", "Permissions") : null) ?? new JsonArray()
                        });
                    }
                    Json(ctx, 200, arr);
                    return;
                }

                case "GET /api/themes":
                {
                    var arr = new JsonArray();
                    foreach (var t in Themes.List(_gameRoot))
                    {
                        var o = Obj(t);
                        arr.Add(new JsonObject
                        {
                            ["id"] = Str(o, "id", "Id"),
                            ["name"] = Str(o, "name", "Name"),
                            ["author"] = Str(o, "author", "Author"),
                            ["description"] = Str(o, "description", "Description"),
                            ["builtIn"] = Flag(o, "builtIn", "BuiltIn"),
                            ["colors"] = Pick(o, "colors", "Colors") ?? new JsonObject(),
                            ["fontFamily"] = Str(o, "fontFamily", "FontFamily"),
                            ["fontSize"] = Long(o, "fontSize", "FontSize")
                        });
                    }
                    Json(ctx, 200, new JsonObject
                    {
                        ["ok"] = true,
                        ["active"] = Themes.ActiveId(_gameRoot),
                        ["themes"] = arr
                    });
                    return;
                }

                case "POST /api/theme/use":
                {
                    var id = Str(body, "id");
                    if (!SafeToken(id)) throw new WebBadRequest(L("主题 id 非法: {0}", id));
                    int code = Themes.Use(_gameRoot, id);
                    Json(ctx, 200, new JsonObject { ["ok"] = code == 0, ["id"] = id, ["code"] = code });
                    return;
                }

                case "GET /api/lang":
                {
                    var arr = new JsonArray();
                    foreach (var code in LangPacks.Available(_gameRoot)) arr.Add(code);
                    var builtIn = new JsonArray();
                    foreach (var code in LangPacks.BuiltIn) builtIn.Add(code);
                    Json(ctx, 200, new JsonObject
                    {
                        ["ok"] = true,
                        ["active"] = CurrentLang(),
                        ["available"] = arr,
                        ["builtIn"] = builtIn
                    });
                    return;
                }

                case "POST /api/lang/use":
                {
                    var code = Str(body, "code");
                    if (!SafeToken(code)) throw new WebBadRequest(L("语言代码非法: {0}", code));
                    ConfigFile.Set(_gameRoot, "lang", code);
                    LangPacks.LoadExternal(_gameRoot, code);   // 外部语言包先灌表再切，避免切完立刻取不到译文
                    Lang.SetCode(code);
                    Json(ctx, 200, new JsonObject { ["ok"] = true, ["code"] = code });
                    return;
                }

                case "GET /api/lang/coverage":
                {
                    var code = (req.QueryString["code"] ?? "").Trim();
                    if (code.Length == 0) code = CurrentLang();
                    if (!SafeToken(code)) throw new WebBadRequest(L("语言代码非法: {0}", code));
                    int result;
                    var report = CaptureConsole(() => LangPacks.Coverage(_gameRoot, code), out result);
                    Json(ctx, 200, new JsonObject
                    {
                        ["ok"] = true,
                        ["code"] = code,
                        ["result"] = result,
                        ["report"] = report.Trim()
                    });
                    return;
                }

                case "POST /api/deploy":
                {
                    var hook = DeployHook;
                    if (hook == null)
                    {
                        Fail(ctx, 501, L("部署钩子未注入：请由主程序设置 WebUi.DeployHook"));
                        return;
                    }
                    var chapter = Str(body, "chapter");
                    if (chapter.Length == 0) chapter = "root";
                    if (!SafeToken(chapter)) throw new WebBadRequest(L("章节名非法: {0}", chapter));
                    int code = hook(_gameRoot, chapter);
                    Json(ctx, 200, new JsonObject { ["ok"] = code == 0, ["chapter"] = chapter, ["code"] = code });
                    return;
                }
            }

            Fail(ctx, 404, L("未知路由: {0} {1}", method, path));
        }
        catch (WebBadRequest ex)
        {
            Fail(ctx, 400, ex.Message);
        }
        catch (Exception ex)
        {
            Fail(ctx, 500, L("处理 {0} 失败: {1}", path, ex.Message));
        }
    }

    // ================================================================= 静态文件

    /// <summary>找 web 目录：① &lt;exe目录&gt;/web（发布包的独立布局）② &lt;Neutraled&gt;/web（仓库布局）。</summary>
    private static string LocateWebRoot(string gameRoot)
    {
        foreach (var cand in new[]
                 {
                     Path.Combine(AppContext.BaseDirectory, "web"),
                     Path.Combine(Paths.NeutraledRoot(gameRoot), "web")
                 })
        {
            try { if (File.Exists(Path.Combine(cand, "index.html"))) return Path.GetFullPath(cand); }
            catch { /* 路径非法就继续找下一个 */ }
        }
        return "";
    }

    private static void ServeIndex(HttpListenerContext ctx)
    {
        if (_webRoot.Length > 0 && ServeStatic(ctx, "/index.html")) return;
        WriteText(ctx, 200, "text/html; charset=utf-8", FallbackPage());
    }

    /// <summary>静态资源缺失时的兜底页面（服务本身照常可用，只是没有面板）。</summary>
    private static string FallbackPage()
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"zh\"><head><meta charset=\"utf-8\"><title>Neutraled</title></head>");
        sb.Append("<body style=\"font-family:system-ui;background:#14161a;color:#e8e8ea;padding:2rem;line-height:1.7\">");
        sb.Append("<h1>Neutraled</h1>");
        sb.Append("<p>").Append(L("未找到 web/index.html：网页界面资源缺失，接口仍在运行。请确认发布包里包含 web/ 目录（index.html / app.js / style.css），或把它们放进 Neutraled/web/ 下。")).Append("</p>");
        sb.Append("<p>").Append(L("可用接口: {0}", "/api/status")).Append("</p>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static bool ServeStatic(HttpListenerContext ctx, string path)
    {
        if (_webRoot.Length == 0) return false;
        var rel = path.TrimStart('/');
        if (rel.Length == 0) rel = "index.html";
        if (rel.Contains("..")) return false;
        string full;
        try { full = Path.GetFullPath(Path.Combine(_webRoot, rel.Replace('/', Path.DirectorySeparatorChar))); }
        catch { return false; }
        // 前缀校验：解析后的绝对路径必须仍在 web/ 目录内，绝不越界读文件
        if (!full.StartsWith(_webRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return false;
        if (!File.Exists(full)) return false;
        try
        {
            var bytes = File.ReadAllBytes(full);
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = MimeOf(full);
            ctx.Response.Headers["Cache-Control"] = "no-store";   // 开发期改 app.js 刷新即生效
            ctx.Response.ContentLength64 = bytes.Length;
            if (!HeadOnly(ctx)) ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.Close();
            return true;
        }
        catch { return false; }
    }

    private static string MimeOf(string file)
    {
        switch (Path.GetExtension(file).ToLowerInvariant())
        {
            case ".html":
            case ".htm": return "text/html; charset=utf-8";
            case ".js":
            case ".mjs": return "text/javascript; charset=utf-8";
            case ".css": return "text/css; charset=utf-8";
            case ".json": return "application/json; charset=utf-8";
            case ".svg": return "image/svg+xml";
            case ".png": return "image/png";
            case ".jpg":
            case ".jpeg": return "image/jpeg";
            case ".gif": return "image/gif";
            case ".ico": return "image/x-icon";
            case ".woff2": return "font/woff2";
            case ".woff": return "font/woff";
            case ".txt":
            case ".md": return "text/plain; charset=utf-8";
            default: return "application/octet-stream";
        }
    }

    // ================================================================= 浏览器

    /// <summary>用各平台自己的方式打开默认浏览器；失败静默（打不开浏览器不能算启动失败）。</summary>
    private static void OpenBrowser(string url)
    {
        try
        {
            var psi = new ProcessStartInfo { UseShellExecute = false, CreateNoWindow = true };
            if (Platform.IsWindows)
            {
                psi.FileName = "cmd";
                psi.ArgumentList.Add("/c");
                psi.ArgumentList.Add("start");
                psi.ArgumentList.Add("");    // start 的第一个参数是窗口标题，留空
                psi.ArgumentList.Add(url);
            }
            else if (Platform.IsMacOs)
            {
                psi.FileName = "open";
                psi.ArgumentList.Add(url);
            }
            else
            {
                psi.FileName = "xdg-open";
                psi.ArgumentList.Add(url);
            }
            Process.Start(psi);
        }
        catch { /* 静默：控制台已经打印 URL，用户可手动打开 */ }
    }

    // ================================================================= 小工具

    private sealed class WebBadRequest : Exception
    {
        public WebBadRequest(string message) : base(message) { }
    }

    private static bool IsLoopback(IPEndPoint? ep)
    {
        try { return ep != null && IPAddress.IsLoopback(ep.Address); }
        catch { return false; }
    }

    /// <summary>路径 / 标识安全校验：拒绝空、过长、.. 与路径分隔符（所有来自 JSON 与查询串的参数都要过这关）。</summary>
    private static bool SafeToken(string s)
    {
        if (string.IsNullOrWhiteSpace(s) || s.Length > 96) return false;
        if (s.Contains("..") || s.Contains('/') || s.Contains('\\')) return false;
        if (s.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        return true;
    }

    private static string CurrentLang()
    {
        var c = ConfigFile.GetString(_gameRoot, "lang");
        return string.IsNullOrWhiteSpace(c) ? Lang.Current : c!;
    }

    private static List<ModEntry> ScanAll() =>
        Mods.ScanMods(Paths.ModsRoot(_gameRoot), "root", includeDisabled: true, allChapters: true);

    /// <summary>读 POST 体（上限 1 MB）；超限/非 JSON 对象一律 WebBadRequest（→ 400）。</summary>
    private static JsonObject ReadBody(HttpListenerRequest req)
    {
        if (!req.HasEntityBody) return new JsonObject();
        if (req.ContentLength64 > MaxBody) throw new WebBadRequest(L("请求体超过 1 MB 上限"));
        using var ms = new MemoryStream();
        var buf = new byte[8192];
        int n;
        while ((n = req.InputStream.Read(buf, 0, buf.Length)) > 0)
        {
            if (ms.Length + n > MaxBody) throw new WebBadRequest(L("请求体超过 1 MB 上限"));
            ms.Write(buf, 0, n);
        }
        var text = Encoding.UTF8.GetString(ms.ToArray()).Trim();
        if (text.Length == 0) return new JsonObject();
        JsonNode? node;
        try { node = JsonNode.Parse(text); }
        catch (Exception ex) { throw new WebBadRequest(L("请求体不是合法 JSON: {0}", ex.Message)); }
        return node as JsonObject ?? throw new WebBadRequest(L("请求体不是 JSON 对象"));
    }

    private static void Json(HttpListenerContext ctx, int status, JsonNode body)
        => WriteText(ctx, status, "application/json; charset=utf-8", body.ToJsonString(Paths.Json));

    private static void WriteText(HttpListenerContext ctx, int status, string contentType, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = contentType;
        ctx.Response.Headers["Cache-Control"] = "no-store";
        ctx.Response.ContentLength64 = bytes.Length;
        if (!HeadOnly(ctx)) ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        ctx.Response.Close();
    }

    /// <summary>HEAD 请求：只发头，不发正文。</summary>
    private static bool HeadOnly(HttpListenerContext ctx)
        => string.Equals(ctx.Request.HttpMethod, "HEAD", StringComparison.OrdinalIgnoreCase);

    private static void Fail(HttpListenerContext ctx, int status, string message)
    {
        try { Json(ctx, status, new JsonObject { ["error"] = message }); }
        catch { try { ctx.Response.Abort(); } catch { } }
    }

    /// <summary>把 DTO 转成 JsonObject（用仓库统一的序列化选项，非 ASCII 不转义）。</summary>
    private static JsonObject Obj(object? dto)
    {
        if (dto == null) return new JsonObject();
        try { return JsonSerializer.SerializeToNode(dto, Paths.Json) as JsonObject ?? new JsonObject(); }
        catch { return new JsonObject(); }
    }

    /// <summary>按名字取值（先精确后忽略大小写）。
    /// 为什么容忍两种命名：并行实现的 DTO 可能叫 Id 也可能叫 id —— 序列化后的键名不完全可控，
    /// 这里统一兼容，网页端永远拿到 SPEC §3.7 规定的小写键。</summary>
    private static JsonNode? Pick(JsonObject o, params string[] names)
    {
        foreach (var n in names)
        {
            if (o.TryGetPropertyValue(n, out var v)) return v?.DeepClone();
            foreach (var kv in o)
                if (string.Equals(kv.Key, n, StringComparison.OrdinalIgnoreCase)) return kv.Value?.DeepClone();
        }
        return null;
    }

    private static string Str(JsonObject o, params string[] names) => Pick(o, names)?.ToString() ?? "";

    private static long Long(JsonObject o, params string[] names)
    {
        var n = Pick(o, names);
        if (n == null) return 0;
        try { return n.GetValue<long>(); } catch { /* 可能是字符串，下面再试一次 */ }
        return long.TryParse(n.ToString(), out var v) ? v : 0;
    }

    private static bool Flag(JsonObject o, params string[] names)
    {
        var n = Pick(o, names);
        if (n == null) return false;
        try { return n.GetValue<bool>(); } catch { /* 字符串形式兜底 */ }
        var s = n.ToString();
        return s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1";
    }

    /// <summary>临时接管 Console 输出，捕获「打印式」模块的返回值（LangPacks.Coverage 的契约就是打印）。
    /// Console.SetOut 是进程级全局状态 —— 必须加锁且 try/finally 还原，否则别的线程输出会丢。</summary>
    private static string CaptureConsole(Func<int> action, out int result)
    {
        lock (ConsoleGate)
        {
            var old = Console.Out;
            var sw = new StringWriter();
            try
            {
                Console.SetOut(sw);
                result = action();
            }
            finally { Console.SetOut(old); }
            return sw.ToString();
        }
    }
}
