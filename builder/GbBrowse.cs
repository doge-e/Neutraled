using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>
/// GameBanana 浏览 / 一键安装（feat-gb）：搜索 → 看文件表 → 下载 → 交给导入器。
/// HTTP 细节全部复用 GameBanana.cs（SearchAsync / GetFilesAsync / DownloadAsync / DownloadWithCurl），
/// 本文件不自己建 HttpClient、不解析任何 HTTP 响应。
///
/// 实测教训（2026-09，本机 Windows）：
///   · HttpClient 直连 gamebanana 的 CDN 常在几十 MB 后断流，curl 走系统代理更稳，所以下载顺序固定为
///     「curl 优先 → DownloadAsync 兜底」；DownloadWithCurl 内部已自带「非 Windows 直接 false」，
///     这里再用 Platform.IsWindows 挡一次，省掉一次无意义的线程与重试。
///   · DownloadWithCurl 没有进度回调，只能自己在等待期间轮询目标文件体积来补进度（见 CurlWithProgress），
///     否则 curl 分支下几百 MB 全程无输出，用户会以为卡死。
///   · DownloadUrl 的体积字段偶尔为 0；expectedSize 小于等于 0 时一律不做体积校验（GameBanana.DownloadWithCurl
///     里那种「体积未知就当成功」的判定，在回退分支上要挡掉，否则半截文件会被当成下载完成）。
///   · 「导入」的实现在 Program.cs，本文件不能引用 Program，所以留一个 Func 插口 ImportHook：
///     没注入时只提示并返回 2，绝不自作主张解压或搬文件。
///   · 所有来自参数/API/JSON 的文件名都是不可信输入：一律过 SafeFileName 砍掉目录部分与非法字符，
///     否则一个 ../ 就能让下载写到 dl 目录外。
/// </summary>
public static class GbBrowse
{
    /// <summary>导入器插口：由 Program.cs 启动时注入，指向「把下载好的安装包装进 mods」的实现。
    /// 参数依次为 (gameRoot, filePath, chapter, force)，返回码 0 = 成功、非 0 = 失败（原样透传给 CLI）。
    /// 保持可空：没注入时 Install 打印提示并返回 2，而不是让本文件编译期就依赖 Program。</summary>
    public static Func<string, string, string?, bool, int>? ImportHook;

    /// <summary>搜索 GameBanana 并打印可 grep 的一行一条列表。
    /// 返回 0 = 成功（0 条结果也算成功），1 = 网络/解析失败，3 = 参数非法。</summary>
    public static async Task<int> Search(string gameRoot, string query, int perPage = 15)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            Paths.Log(L("[GB] 搜索关键字为空"));
            return 3;
        }
        if (perPage <= 0) perPage = 15;
        try
        {
            Paths.Log(L("[GB] 搜索「{0}」（每页 {1} 条）", query, perPage));
            var results = await GameBanana.SearchAsync(query, perPage);
            // 黑名单由 Blacklist.cs（feat-bl）负责：命中项就地剔除，返回被滤掉的条数
            var blocked = Blacklist.FilterOut(gameRoot, results);
            if (blocked > 0) Paths.Log(L("[GB] 黑名单已过滤 {0} 条", blocked));
            if (results.Count == 0)
            {
                Paths.Log(L("[GB] 没有搜索结果"));
                return 0;
            }
            Paths.Log(L("[GB] 序号 | id | 名称 | 作者 | 下载量 | 更新时间"));
            for (var i = 0; i < results.Count; i++)
            {
                var r = results[i];
                Paths.Log(L("[GB] {0} | {1} | {2} | {3} | {4} | {5}",
                    i + 1, r.Id, OneLine(r.Name), OneLine(r.Author), r.DownloadCount, OneLine(r.Updated)));
            }
            Paths.Log(L("[GB] 共 {0} 条结果", results.Count));
            return 0;
        }
        catch (Exception ex)
        {
            Paths.Log(L("[GB] 搜索失败: {0}", ex.Message));
            return 1;
        }
    }

    /// <summary>列出某个 mod 的文件表（想指定 fileId 装某个文件前先看这个）。
    /// 返回 0 = 成功，1 = 网络/解析失败，3 = modId 或 type 非法。</summary>
    public static async Task<int> Files(string gameRoot, int modId, string type = "Mod")
    {
        if (modId <= 0)
        {
            Paths.Log(L("[GB] mod id 非法：{0}", modId));
            return 3;
        }
        var t = string.IsNullOrWhiteSpace(type) ? "Mod" : type.Trim();
        if (!IsValidType(t))
        {
            Paths.Log(L("[GB] 类型名非法：{0}（只允许字母，最长 16 位）", t));
            return 3;
        }
        try
        {
            var files = await GameBanana.GetFilesAsync(modId, t);
            if (files.Count == 0)
            {
                Paths.Log(L("[GB] mod {0} 没有文件", modId));
                return 0;
            }
            Paths.Log(L("[GB] mod {0}（类型 {1}）共 {2} 个文件", modId, t, files.Count));
            Paths.Log(L("[GB] 序号 | 文件 id | 文件名 | 体积 | 说明"));
            for (var i = 0; i < files.Count; i++)
            {
                var f = files[i];
                Paths.Log(L("[GB] {0} | {1} | {2} | {3} KB | {4}",
                    i + 1, f.Id, OneLine(f.FileName), f.Size / 1024, OneLine(f.Description)));
            }
            return 0;
        }
        catch (Exception ex)
        {
            Paths.Log(L("[GB] 取文件表失败: {0}", ex.Message));
            return 1;
        }
    }

    /// <summary>一键安装：取文件表 → 选目标文件（fileId 指定，否则第一个）→ 下载到 Neutraled/dl →
    /// 调 ImportHook(gameRoot, filePath, chapter, force) 导入。
    /// 返回 0 = 安装成功，1 = 网络/IO 失败，2 = 没注入 ImportHook（文件已下好但没装），
    /// 3 = 参数非法或指定文件不存在，其它 = ImportHook 自己的失败码（原样透传）。</summary>
    public static async Task<int> Install(string gameRoot, int modId, string? type = null, int? fileId = null, string? chapter = null, bool force = false)
    {
        if (modId <= 0)
        {
            Paths.Log(L("[GB] mod id 非法：{0}", modId));
            return 3;
        }
        if (fileId is <= 0)
        {
            Paths.Log(L("[GB] 文件 id 非法：{0}", fileId));
            return 3;
        }
        var t = string.IsNullOrWhiteSpace(type) ? "Mod" : type!.Trim();
        if (!IsValidType(t))
        {
            Paths.Log(L("[GB] 类型名非法：{0}（只允许字母，最长 16 位）", t));
            return 3;
        }
        try
        {
            Paths.Log(L("[GB] 一键安装：mod {0}（类型 {1}）", modId, t));
            if (!string.IsNullOrWhiteSpace(chapter)) Paths.Log(L("[GB] 目标章节：{0}", chapter));
            var files = await GameBanana.GetFilesAsync(modId, t);
            if (files.Count == 0)
            {
                Paths.Log(L("[GB] mod {0} 没有可下载的文件", modId));
                return 3;
            }
            GbFile? pick = null;
            if (fileId is > 0)
            {
                foreach (var f in files) if (f.Id == fileId.Value) { pick = f; break; }
                if (pick == null)
                {
                    Paths.Log(L("[GB] mod {0} 的文件表里没有文件 id {1}", modId, fileId.Value));
                    Paths.Log(L("[GB] 可选的文件 id：{0}", string.Join(", ", files.Select(f => f.Id))));
                    return 3;
                }
            }
            else
            {
                pick = files[0];
            }
            if (string.IsNullOrWhiteSpace(pick.DownloadUrl))
            {
                Paths.Log(L("[GB] 文件 {0} 没有下载地址", pick.Id));
                return 3;
            }
            Paths.Log(L("[GB] 选中文件：{0}（id {1}，{2} KB）", OneLine(pick.FileName), pick.Id, pick.Size / 1024));
            var outPath = DownloadPathFor(gameRoot, modId, pick.Id, pick.FileName);
            var got = await FetchAsync(pick.DownloadUrl, outPath, pick.Size);
            if (got < 0) return 1;
            if (ImportHook == null)
            {
                Paths.Log(L("[GB] 未注入导入器（ImportHook），文件已下载但未安装：{0}", outPath));
                Paths.Log(L("[GB] 请让 Program.cs 启动时给 GbBrowse.ImportHook 赋值，或手工导入上面的文件"));
                return 2;
            }
            var code = ImportHook(gameRoot, outPath, chapter, force);
            if (code != 0)
            {
                Paths.Log(L("[GB] 导入失败（返回码 {0}）：{1}", code, outPath));
                return code;
            }
            Paths.Log(L("[GB] 安装完成：mod {0} → {1}", modId, outPath));
            return 0;
        }
        catch (Exception ex)
        {
            Paths.Log(L("[GB] 安装失败: {0}", ex.Message));
            return 1;
        }
    }

    /// <summary>批量一键安装：逐个跑 Install，一个失败不中断，记录第一条失败码。
    /// 返回 0 = 全部成功，否则第一条失败的返回码（1/2/3 或 ImportHook 的码）。</summary>
    public static async Task<int> InstallBatch(string gameRoot, List<int> modIds, string? chapter = null, bool force = false)
    {
        if (modIds == null || modIds.Count == 0)
        {
            Paths.Log(L("[GB] 批量安装：没有指定 mod id"));
            return 0;
        }
        Paths.Log(L("[GB] 批量安装：{0} 个 mod", modIds.Count));
        var ok = 0;
        var fail = 0;
        var first = 0;
        foreach (var id in modIds)
        {
            var code = await Install(gameRoot, id, null, null, chapter, force);
            if (code == 0) ok++;
            else
            {
                fail++;
                if (first == 0) first = code;
            }
        }
        Paths.Log(L("[GB] 批量安装结束：成功 {0} / 失败 {1}", ok, fail));
        return fail == 0 ? 0 : (first != 0 ? first : 1);
    }

    /// <summary>下载一个文件到 outPath：curl 优先 + HttpClient 兜底 + 每 10% 一行进度 + 幂等复用。
    /// 返回实际拿到的字节数，失败返回 -1。DownloadQueue 复用这一份实现，避免队列里再养一套下载逻辑跑偏。</summary>
    internal static async Task<long> FetchAsync(string url, string outPath, long expectedSize)
    {
        var dir = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        // 幂等：文件已在且体积相符就直接复用 —— 重跑 --gb-install / --queue-run 不会重复拉几百 MB
        if (expectedSize > 0 && File.Exists(outPath))
        {
            var len = new FileInfo(outPath).Length;
            if (len == expectedSize)
            {
                Paths.Log(L("[下载] 已存在且体积相符，跳过下载：{0}（{1} KB）", Path.GetFileName(outPath), len / 1024));
                return len;
            }
        }
        Paths.Log(L("[下载] 开始下载：{0}", Path.GetFileName(outPath)));
        var viaCurl = false;
        if (Platform.IsWindows) viaCurl = await Task.Run(() => CurlWithProgress(url, outPath, expectedSize));
        if (viaCurl)
        {
            var gotCurl = File.Exists(outPath) ? new FileInfo(outPath).Length : 0;
            if (expectedSize > 0 && gotCurl != expectedSize)
            {
                Paths.Log(L("[下载] 体积不符：期望 {0} KB，实际 {1} KB", expectedSize / 1024, gotCurl / 1024));
                return -1;
            }
            // curl 报成功但文件是空的/没落地：与其把 0 字节的包交给导入器，不如当下载失败
            if (gotCurl <= 0)
            {
                Paths.Log(L("[下载] 失败：{0}", Path.GetFileName(outPath)));
                return -1;
            }
            Paths.Log(L("[下载] 完成：{0}（{1} KB）", Path.GetFileName(outPath), gotCurl / 1024));
            return gotCurl;
        }
        // curl 不可用（非 Windows 或失败）→ 回退 HttpClient 流式下载
        try
        {
            var total = expectedSize;
            long last = -1;
            await GameBanana.DownloadAsync(url, outPath, (read, tot) =>
            {
                if (tot > 0) total = tot;
                if (total <= 0) return;
                var pct = (int)(read * 100 / total);
                if (pct / 10 > last / 10)
                {
                    last = pct;
                    Paths.Log(L("[下载] {0}% ({1}/{2} KB)", pct, read / 1024, total / 1024));
                }
            });
            var size = File.Exists(outPath) ? new FileInfo(outPath).Length : 0;
            if (expectedSize > 0 && size != expectedSize)
            {
                Paths.Log(L("[下载] 体积不符：期望 {0} KB，实际 {1} KB", expectedSize / 1024, size / 1024));
                return -1;
            }
            if (size <= 0)
            {
                Paths.Log(L("[下载] 失败：{0}", Path.GetFileName(outPath)));
                return -1;
            }
            Paths.Log(L("[下载] 完成：{0}（{1} KB）", Path.GetFileName(outPath), size / 1024));
            return size;
        }
        catch (Exception ex)
        {
            Paths.Log(L("[下载] 失败：{0}", ex.Message));
            return -1;
        }
    }

    /// <summary>跑 curl 下载，并在等待期间轮询目标文件体积、每跨过一档 10% 打一行进度
    /// （curl 自己不给进度回调；curl 用 -o 直接写目标文件，所以体积就是进度）。
    /// 返回 false = curl 不可用或失败，调用方应回退 GameBanana.DownloadAsync。</summary>
    private static bool CurlWithProgress(string url, string outPath, long expectedSize)
    {
        try
        {
            var task = Task.Run(() => GameBanana.DownloadWithCurl(url, outPath, expectedSize, s => Paths.Log(s)));
            var last = 0;
            while (!task.IsCompleted)
            {
                DumpProgress(outPath, expectedSize, ref last);
                Thread.Sleep(400);
            }
            DumpProgress(outPath, expectedSize, ref last);
            return task.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Paths.Log(L("[下载] curl 调用异常，回退到 HTTP 下载：{0}", ex.Message));
            return false;
        }
    }

    /// <summary>按目标文件当前体积打进度：只在跨过新的 10% 档位时打一行，避免刷屏。</summary>
    private static void DumpProgress(string path, long expectedSize, ref int last)
    {
        if (expectedSize <= 0) return;
        long got;
        try
        {
            got = File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch
        {
            return;
        }
        var pct = (int)Math.Min(100, got * 100 / expectedSize);
        if (pct / 10 <= last) return;
        last = pct / 10;
        Paths.Log(L("[下载] {0}% ({1}/{2} KB)", pct, got / 1024, expectedSize / 1024));
    }

    /// <summary>下载落盘路径：游戏根/Neutraled/dl/modId-fileId-原文件名。
    /// 文件名一律过 SafeFileName，杜绝 API 或参数里的 ../ 把文件写到 dl 之外。</summary>
    internal static string DownloadPathFor(string gameRoot, int modId, int? fileId, string? fileName)
    {
        var dir = Paths.DownloadsRoot(gameRoot);
        Directory.CreateDirectory(dir);
        var name = SafeFileName(fileName);
        if (name.Length == 0) name = "download.bin";
        var prefix = modId + "-" + (fileId.HasValue ? fileId.Value.ToString() : "0") + "-";
        return Path.Combine(dir, SafeFileName(prefix + name));
    }

    /// <summary>把任意来源的文件名洗成安全文件名：先砍掉目录部分（\ 与 / 都当分隔符），
    /// 再只保留字母数字与 . _ - ( ) 空格 + ，其余替换为下划线；超长时保留扩展名（导入器要靠它判断包类型）。</summary>
    internal static string SafeFileName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var s = raw.Replace('\\', '/');
        var slash = s.LastIndexOf('/');
        if (slash >= 0) s = s[(slash + 1)..];
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-' || c == '(' || c == ')' || c == ' ' || c == '+') sb.Append(c);
            else sb.Append('_');
        }
        var name = sb.ToString().Trim().TrimStart('.');
        if (name.Length > 120)
        {
            var ext = Path.GetExtension(name);
            if (ext.Length > 16) ext = "";
            var keep = 120 - ext.Length;
            name = keep > 0 ? name[..keep] + ext : name[..120];
        }
        return name;
    }

    /// <summary>GameBanana 的类型名会直接拼进 API 路径（apiv11/类型/modId/ProfilePage），
    /// 所以只允许纯字母、最长 16 位 —— 顺手挡掉 ../ 与查询串注入。</summary>
    internal static bool IsValidType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return false;
        var t = type.Trim();
        if (t.Length is 0 or > 16) return false;
        foreach (var c in t)
        {
            if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))) return false;
        }
        return true;
    }

    /// <summary>把说明字段里的换行压成空格，并把竖线间隔符换成斜杠：
    /// 输出是「一行一条、以 | 分列」的可 grep 文本，字段内部再出现换行或 | 会把列搞乱。</summary>
    internal static string OneLine(string? s) =>
        string.IsNullOrEmpty(s) ? "" : s.Replace("\r", " ").Replace("\n", " ").Replace(" | ", " / ").Trim();
}
