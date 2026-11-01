using System.Text.Encodings.Web;
using System.Text.Json;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>C4 P1-2：GameBanana 搜索的唯一实现。
///
/// 历史上 <c>--search</c>（Program.cs，不过黑名单）与 <c>--gb-search</c>（CliFeatures.cs，
/// 过黑名单）是两套并行实现，参数与输出都不一样；现在两者都调用这里：
/// <list type="bullet">
/// <item><c>--search</c> —— 正式入口，默认过滤 <see cref="Blacklist"/>；</item>
/// <item><c>--gb-search</c> —— 旧名别名，打一行 <c>[deprecated]</c> 后走同一实现；</item>
/// </list>
/// <c>--dry-run</c> 在发出任何网络请求之前返回，只打印/序列化将要使用的 URL 与参数。</summary>
public sealed class SearchOptions
{
    public string Query = "";
    public int PerPage = 15;
    public bool UseBlocklist = true;
    public bool Json;
    public bool DryRun;
    /// <summary>非空表示是从旧名（--gb-search）进来的，需要打印 deprecated 提示。</summary>
    public string? Alias;
}

public static class GbSearch
{
    public const int MinPerPage = 1;
    public const int MaxPerPage = 100;
    public const string PreferredSwitch = "--search";

    public static int Run(string gameRoot, SearchOptions o)
    {
        if (o.Alias != null) Deprecation.Print(o.Alias, PreferredSwitch, o.Json);

        if (string.IsNullOrWhiteSpace(o.Query))
        {
            Error("缺少搜索关键词（用法: {0} <关键词> [--per-page N] [--no-blocklist] [--dry-run] [--json]）", o, PreferredSwitch);
            return 2;
        }
        if (o.PerPage < MinPerPage || o.PerPage > MaxPerPage)
        {
            Error("--per-page 必须在 {0}..{1} 之间（收到 {2}）", o, MinPerPage, MaxPerPage, o.PerPage);
            return 2;
        }

        var url = GameBanana.SearchUrl(o.Query, o.PerPage);
        if (o.DryRun)
        {
            if (o.Json)
            {
                WriteJson(new
                {
                    schemaVersion = 1,
                    query = o.Query,
                    perPage = o.PerPage,
                    blocklist = o.UseBlocklist,
                    dryRun = true,
                    network = false,
                    url,
                    count = 0,
                    results = Array.Empty<object>(),
                });
            }
            else
            {
                Console.WriteLine(L("搜索计划（dry-run，零网络请求）:"));
                Console.WriteLine(L("  查询: {0}", o.Query));
                Console.WriteLine(L("  每页: {0}", o.PerPage));
                Console.WriteLine(L("  黑名单: {0}", o.UseBlocklist ? L("已启用（过滤命中项）") : L("已禁用（--no-blocklist）")));
                Console.WriteLine(L("  URL: {0}", url));
                Console.WriteLine(L("  请求参数: {0}", Params(o.Query, o.PerPage)));
                Console.WriteLine(L("  结果: 未请求（--dry-run）"));
            }
            return 0;
        }

        try
        {
            Console.WriteLine(L("[GB] 搜索「{0}」（每页 {1} 条）", o.Query, o.PerPage));
            var list = GameBanana.SearchAsync(o.Query, o.PerPage).GetAwaiter().GetResult();
            var filtered = 0;
            if (o.UseBlocklist)
            {
                filtered = Blacklist.FilterOut(gameRoot, list);
                Console.WriteLine(L("[GB] 黑名单已过滤 {0} 条", filtered));
            }

            if (list.Count == 0)
            {
                Console.WriteLine(L("[GB] 没有搜索结果"));
            }
            else
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var r = list[i];
                    Console.WriteLine(L("[GB] {0} | {1} | {2} | {3} | {4} | {5}",
                        i + 1, r.Id, OneLine(r.Name), OneLine(r.Author), r.DownloadCount, OneLine(r.Updated)));
                }
                Console.WriteLine(L("[GB] 共 {0} 条结果", list.Count));
            }

            if (o.Json)
            {
                WriteJson(new
                {
                    schemaVersion = 1,
                    query = o.Query,
                    perPage = o.PerPage,
                    blocklist = o.UseBlocklist,
                    dryRun = false,
                    network = true,
                    url,
                    filtered,
                    count = list.Count,
                    results = list.Select(r => new
                    {
                        id = r.Id,
                        name = r.Name,
                        model = r.Model,
                        author = r.Author,
                        url = r.ProfileUrl,
                        downloads = r.DownloadCount,
                        likes = r.LikeCount,
                        updated = r.Updated,
                    }),
                });
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine(L("[GB] 搜索失败: {0}", ex.Message));
            return 1;
        }
    }

    private static string Params(string query, int perPage) =>
        $"_sModelName=Mod&_sOrder=best_match&_sSearchString={Uri.EscapeDataString(query)}&_nPerpage={perPage}&_idGameRow={GameBanana.DeltaruneGameId}";

    private static string OneLine(string? s) =>
        (s ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static void Error(string zh, SearchOptions o, params object?[] args)
    {
        var msg = L(zh, args);
        if (o.Json) Console.Error.WriteLine(msg);
        else Console.WriteLine(msg);
    }

    internal static void WriteJson(object payload)
    {
        Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }));
    }
}

/// <summary>旧开关改别名的统一提示。JSON 模式走 stderr，避免污染 stdout 的纯 JSON。</summary>
internal static class Deprecation
{
    public static void Print(string oldName, string newName, bool json, string? note = null)
    {
        var line = note == null
            ? L("[deprecated] {0} 是旧名，请改用 {1}（语义相同）", oldName, newName)
            : L("[deprecated] {0} 是旧名，请改用 {1}（{2}）", oldName, newName, note);
        if (json) Console.Error.WriteLine(line);
        else Console.WriteLine(line);
    }
}
