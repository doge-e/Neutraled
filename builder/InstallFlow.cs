using System.Text.Encodings.Web;
using System.Text.Json;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>C4 P1-3 的选项。字段名与 CLI 开关一一对应。</summary>
public sealed class InstallOptions
{
    public bool FromUrl;
    public string Value = "";
    public int? FileIndex;
    public bool AsDelta;
    public string? As;
    public bool DryRun;
    public bool Json;
    public string Type = "Mod";
    public string? Chapter;
    public bool ChapterExplicit;
    public bool Force;
    public string? ModName;
    public string? Author;
    /// <summary>非空表示从旧名（--fetch / --fetch-mod / --gb-install）进来。</summary>
    public string? Alias;
    public bool PickLargest;
}

/// <summary>C4 P1-3：下载 + 导入的唯一入口。
///
/// 三条旧下载链（<c>--fetch</c>、<c>--fetch-mod</c>、<c>--gb-install</c>）与两个新入口
/// （<c>--install-id</c>、<c>--install-url</c>）都走这里，流程固定为
/// <b>探测 → 下载 → 探测包型 → 分派导入器</b>：
/// <list type="bullet">
/// <item>xdelta / data.win → <see cref="DeltaImport"/>（xdelta / 裸数据文件）；</item>
/// <item>ntlmod / kristal / gm3p / external / 未识别 → <see cref="ModdingImport"/>（内部再细分）；</item>
/// </list>
/// <c>--dry-run</c> 在下载前返回：只打印计划，不发任何网络请求。</summary>
public static class InstallFlow
{
    public const string PreferredId = "--install-id";
    public const string PreferredUrl = "--install-url";
    public const string DefaultChapter = "chapter4";

    public static int Run(string gameRoot, InstallOptions o)
    {
        if (o.Alias != null)
            Deprecation.Print(o.Alias, o.FromUrl ? PreferredUrl : PreferredId, o.Json);

        if (!string.IsNullOrWhiteSpace(o.As) && !PackageRouter.IsValidKind(o.As))
            return Fail(o, "[错误] --as 的取值非法: {0}（可用 {1}）", o.As, PackageRouter.KindList);
        if (o.FileIndex is int fi0 && fi0 < 1)
            return Fail(o, "[错误] --file 必须是正整数（收到 {0}）", fi0);

        int id = 0;
        string? url = null;
        if (o.FromUrl)
        {
            if (!Uri.TryCreate(o.Value, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return Fail(o, "[错误] --install-url 必须是 http(s) 地址（收到 {0}）", o.Value);
            url = uri.ToString();
        }
        else
        {
            if (!int.TryParse(o.Value, out id) || id <= 0)
                return Fail(o, "[错误] --install-id 必须是正整数（收到 {0}）", o.Value);
        }

        var forced = o.As ?? (o.AsDelta ? PackageRouter.Xdelta : null);
        var chapter = string.IsNullOrWhiteSpace(o.Chapter) ? DefaultChapter : o.Chapter!;
        return o.DryRun ? DryRun(gameRoot, o, id, url, forced, chapter)
                        : Execute(gameRoot, o, id, url, forced, chapter);
    }

    // ---------------- dry-run（零网络） ----------------

    private static int DryRun(string gameRoot, InstallOptions o, int id, string? url, string? forced, string chapter)
    {
        var profileUrl = !o.FromUrl ? $"https://gamebanana.com/apiv11/{o.Type}/{id}/ProfilePage" : null;
        var fileName = o.FromUrl ? FileNameFromUrl(url!) : null;
        var importer = forced == null ? "auto" : PackageRouter.ImporterFor(forced);

        if (o.Json)
        {
            var steps = new object[]
            {
                new { step = "probe", status = "deferred", detail = "download then probe the package locally" },
                new
                {
                    step = "download",
                    status = "planned",
                    url = o.FromUrl ? url : profileUrl,
                    fileListUrl = profileUrl,
                    fileIndex = o.FileIndex,
                    targetFile = fileName,
                },
                new { step = "import", status = "planned", importer, forcedKind = forced },
            };
            GbSearch.WriteJson(new
            {
                schemaVersion = 1,
                mode = o.FromUrl ? "url" : "id",
                id = o.FromUrl ? (int?)null : id,
                url = o.FromUrl ? url : profileUrl,
                type = o.FromUrl ? null : o.Type,
                file = o.FileIndex,
                asKind = forced,
                asDelta = o.AsDelta,
                dryRun = true,
                network = false,
                chapter,
                steps,
            });
            return 0;
        }

        Console.WriteLine(L("安装计划（dry-run，零网络请求）:"));
        if (o.FromUrl)
        {
            Console.WriteLine(L("  来源: {0}", url));
            Console.WriteLine(L("  文件名: {0}", fileName));
            Console.WriteLine(L("  下载目标: {0}", DownloadPath(gameRoot, fileName!)));
        }
        else
        {
            Console.WriteLine(L("  来源: GameBanana #{0}", id));
            Console.WriteLine(L("  文件列表 URL: {0}", profileUrl));
            Console.WriteLine(o.FileIndex is int idx
                ? L("  文件: 第 {0} 个（1 起）", idx)
                : L("  文件: {0}", o.PickLargest ? L("默认（最大文件）") : L("默认（第一个）")));
        }
        if (forced != null)
        {
            var note = o.As != null ? "--as " + o.As : "--as-delta";
            Console.WriteLine(L("  强制包型: {0}（{1}）", forced, note));
        }
        Console.WriteLine(L("  章节: {0}", chapter));
        Console.WriteLine(L("  1. 探测: 下载后按包内容探测"));
        Console.WriteLine(L("  2. 下载: {0}", o.FromUrl ? url! : L("从上面的文件列表运行时选择")));
        Console.WriteLine(L("  3. 导入: {0}", forced == null
            ? L("探测后自动选择导入器")
            : L("{0} 导入器", PackageRouter.ImporterFor(forced))));
        Console.WriteLine(L("  零网络请求：未访问 GameBanana API。"));
        return 0;
    }

    // ---------------- 真跑 ----------------

    private static int Execute(string gameRoot, InstallOptions o, int id, string? url, string? forced, string chapter)
    {
        var oldOut = Console.Out;
        if (o.Json) Console.SetOut(Console.Error);
        string path = "";
        string kind = PackageRouter.Unknown;
        string reason = "unknown";
        var code = 1;
        try
        {
            GbFile? file = null;
            if (!o.FromUrl)
            {
                var type = string.IsNullOrWhiteSpace(o.Type) ? "Mod" : o.Type.Trim();
                if (!GbBrowse.IsValidType(type))
                {
                    Info(o, L("[错误] 类型名非法: {0}（只允许字母，最长 16 位）", type));
                    code = 2;
                    return 2;
                }
                var files = GameBanana.GetFilesAsync(id, type).GetAwaiter().GetResult();
                if (files.Count == 0 && type == "Mod")
                    files = GameBanana.GetFilesAsync(id, "Wip").GetAwaiter().GetResult();
                if (files.Count == 0)
                {
                    Info(o, L("[错误] 该 GameBanana 条目没有可下载文件（#{0}）", id));
                    return 1;
                }

                if (o.FileIndex is int idx)
                {
                    if (idx < 1 || idx > files.Count)
                    {
                        Info(o, L("[错误] --file 序号超出范围（该条目共 {0} 个文件）", files.Count));
                        code = 2;
                        return 2;
                    }
                    file = files[idx - 1];
                }
                else
                {
                    file = o.PickLargest ? files.OrderByDescending(f => f.Size).First() : files[0];
                }
                if (string.IsNullOrWhiteSpace(file.DownloadUrl))
                {
                    Info(o, L("[错误] 文件 {0} 没有下载地址", file.Id));
                    return 1;
                }
                Info(o, L("[安装] 选中文件: {0}（id {1}，{2} KB）", file.FileName, file.Id, file.Size / 1024));
                path = GbBrowse.DownloadPathFor(gameRoot, id, file.Id, file.FileName);
                var got = GbBrowse.FetchAsync(file.DownloadUrl, path, file.Size).GetAwaiter().GetResult();
                if (got < 0 || !File.Exists(path) || new FileInfo(path).Length == 0)
                {
                    Info(o, L("[错误] 下载失败: {0}", path));
                    return 1;
                }
            }
            else
            {
                var fileName = FileNameFromUrl(url!);
                path = DownloadPath(gameRoot, fileName);
                Info(o, L("[安装] 下载: {0}", url));
                var got = GbBrowse.FetchAsync(url!, path, 0).GetAwaiter().GetResult();
                if (got < 0 || !File.Exists(path) || new FileInfo(path).Length == 0)
                {
                    Info(o, L("[错误] 下载失败: {0}", url));
                    return 1;
                }
            }

            var probe = PackageRouter.Probe(path, forced);
            kind = probe.Kind;
            reason = probe.ReasonCode;
            Info(o, L("[安装] 探测结果: {0}（{1}）", probe.Kind, PackageRouter.ReasonText(probe.ReasonCode)));
            path = EnsureImportableExtension(path, probe.Kind);

            if (probe.Kind is PackageRouter.Xdelta or PackageRouter.DataWin)
            {
                Info(o, L("[安装] 导入器: {0}", "delta"));
                var modName = o.ModName ?? Path.GetFileNameWithoutExtension(path);
                code = DeltaImport.Import(gameRoot, path, chapter, modName, o.Author ?? "unknown");
            }
            else
            {
                if (probe.Kind == PackageRouter.Unknown)
                    Info(o, L("[安装] 包型未识别，交给传统导入器尝试"));
                Info(o, L("[安装] 导入器: {0}", "modding"));
                code = ModdingImport.Import(gameRoot, path, o.ModName, o.Author, false, o.Force,
                    o.ChapterExplicit ? chapter : null);
            }

            if (code == 0) Info(o, L("[安装] 完成: {0}", path));
            else Info(o, L("[安装] 失败（返回码 {0}）: {1}", code, path));
            return code;
        }
        catch (Exception ex)
        {
            Info(o, L("[错误] 安装失败: {0}", ex.Message));
            return 1;
        }
        finally
        {
            if (o.Json)
            {
                Console.SetOut(oldOut);
                GbSearch.WriteJson(new
                {
                    schemaVersion = 1,
                    ok = code == 0,
                    kind,
                    importer = kind is PackageRouter.Xdelta or PackageRouter.DataWin ? "delta"
                        : (string.IsNullOrEmpty(path) ? "none" : "modding"),
                    reason,
                    path,
                    exitCode = code,
                });
            }
        }
    }

    // ---------------- 工具 ----------------

    private static int Fail(InstallOptions o, string zh, params object?[] args)
    {
        var msg = L(zh, args);
        if (o.Json) Console.Error.WriteLine(msg);
        else Console.WriteLine(msg);
        return 2;
    }

    private static void Info(InstallOptions o, string message) => Console.WriteLine(message);

    private static string DownloadPath(string gameRoot, string fileName)
    {
        var name = string.IsNullOrWhiteSpace(fileName) ? "download.bin" : fileName;
        return Path.Combine(Paths.DownloadsRoot(gameRoot), GbBrowse.SafeFileName(name));
    }

    private static string FileNameFromUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "download.bin";
        var p = uri.AbsolutePath.TrimEnd('/');
        var slash = p.LastIndexOf('/');
        var raw = slash >= 0 ? p[(slash + 1)..] : p;
        try { raw = Uri.UnescapeDataString(raw); } catch { /* keep raw */ }
        var safe = GbBrowse.SafeFileName(raw);
        return safe.Length == 0 ? "download.bin" : safe;
    }

    /// <summary>下载站给的直链经常没有扩展名或扩展名与内容不符，而两个导入器都会先看扩展名。
    /// 探测出包型后，若扩展名不被导入器接受，就原地改名（同目录、同卷，零拷贝）。</summary>
    private static string EnsureImportableExtension(string path, string kind)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var supported = ext is ".xdelta" or ".win" or ".zip" or ".ntlmod" or ".7z" or ".rar"
            or ".tar" or ".gz" or ".tgz" or ".bz2";
        if (supported) return path;
        var want = kind switch
        {
            PackageRouter.Xdelta => ".xdelta",
            PackageRouter.DataWin => ".win",
            PackageRouter.Ntlmod or PackageRouter.Kristal or PackageRouter.Gm3p or PackageRouter.External => ".zip",
            _ => null,
        };
        if (want == null) return path;
        var target = path + ".import" + want;
        try
        {
            if (File.Exists(target)) File.Delete(target);
            File.Move(path, target);
            return target;
        }
        catch
        {
            return path;
        }
    }
}
