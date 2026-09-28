using System.Net.Http;
using System.Text.Json;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

public sealed class GbSearchResult
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Model { get; set; } = "";
    public string Author { get; set; } = "";
    public string ProfileUrl { get; set; } = "";
    public string Description { get; set; } = "";
    public long DownloadCount { get; set; }
    public long LikeCount { get; set; }
    public string Updated { get; set; } = "";
}

public sealed class GbFile
{
    public int Id { get; set; }
    public string FileName { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public long Size { get; set; }
    public string Description { get; set; } = "";
}

/// <summary>GameBanana 在线搜索 / 下载（无 key）。</summary>
public static class GameBanana
{
    public const int DeltaruneGameId = 6755;

    /// <summary>API 走默认 HttpClient（本机实测可用）。
    /// 但**文件下载**在这台机器上不可靠（带代理 0 字节卡住 / 不带代理 API 挂死），
    /// 所以下载改走 curl.exe 子进程（见 DownloadWithCurl），带断点续传与体积校验。</summary>
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    static GameBanana()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("Neutraled/0.1 (mod manager)");
        Http.DefaultRequestHeaders.Referrer = new Uri("https://gamebanana.com/");
    }

    /// <summary>搜索 mod（只保留 Mod / Wip / Sound / Script 类型）。</summary>
    public static async Task<List<GbSearchResult>> SearchAsync(string query, int perPage = 15)
    {
        var url = $"https://gamebanana.com/apiv11/Util/Search/Results?_sModelName=Mod&_sOrder=best_match" +
                  $"&_sSearchString={Uri.EscapeDataString(query)}&_nPerpage={perPage}" +
                  $"&_idGameRow={DeltaruneGameId}";
        var json = await Http.GetStringAsync(url);
        using var doc = JsonDocument.Parse(json);
        var list = new List<GbSearchResult>();
        if (!doc.RootElement.TryGetProperty("_aRecords", out var recs)) return list;

        foreach (var r in recs.EnumerateArray())
        {
            var model = Str(r, "_sModelName");
            if (model is not ("Mod" or "Wip" or "Sound" or "Script")) continue;
            list.Add(new GbSearchResult
            {
                Id = Int(r, "_idRow"),
                Name = Str(r, "_sName"),
                Model = model,
                Author = Str(r, "_aSubmitter", "_sName"),
                ProfileUrl = Str(r, "_sProfileUrl"),
                Description = Str(r, "_sDescription"),
                DownloadCount = Long(r, "_nDownloadCount"),
                LikeCount = Long(r, "_nLikeCount"),
                Updated = Str(r, "_tsDateUpdated")
            });
        }
        return list;
    }

    /// <summary>取某个 mod 的文件列表。</summary>
    public static async Task<List<GbFile>> GetFilesAsync(int modId, string type = "Mod")
    {
        var url = $"https://gamebanana.com/apiv11/{type}/{modId}/ProfilePage";
        var json = await Http.GetStringAsync(url);
        using var doc = JsonDocument.Parse(json);
        var list = new List<GbFile>();
        if (!doc.RootElement.TryGetProperty("_aFiles", out var files)) return list;
        foreach (var f in files.EnumerateArray())
        {
            list.Add(new GbFile
            {
                Id = Int(f, "_idRow"),
                FileName = Str(f, "_sFile"),
                DownloadUrl = Str(f, "_sDownloadUrl"),
                Size = Long(f, "_nFilesize"),
                Description = Str(f, "_sDescription")
            });
        }
        return list;
    }

    /// <summary>下载文件到指定路径（带进度回调）。</summary>
    public static async Task DownloadAsync(string url, string outPath, Action<long, long>? onProgress = null, CancellationToken ct = default)
    {
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? -1;
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(outPath);
        var buf = new byte[81920];
        long read = 0;
        int n;
        while ((n = await src.ReadAsync(buf, ct)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, n), ct);
            read += n;
            onProgress?.Invoke(read, total);
        }
    }

    /// <summary>用 curl.exe 下载（本机 HttpClient 下载路径不可靠）。
    /// 断点续传（-C -）+ 浏览器 UA + Referer + 体积校验 + 最多 4 次重试。</summary>
    public static bool DownloadWithCurl(string url, string outPath, long expectedSize, Action<string>? log = null)
    {
        if (!OperatingSystem.IsWindows()) return false;
        for (int attempt = 1; attempt <= 4; attempt++)
        {
            try
            {
                if (File.Exists(outPath) && expectedSize > 0 && new FileInfo(outPath).Length > expectedSize)
                    File.Delete(outPath);   // 上次残留比目标还大 → 重下
                var psi = new System.Diagnostics.ProcessStartInfo("curl.exe")
                {
                    Arguments = $"-s -L -m 900 -C - -A \"Mozilla/5.0 (Windows NT 10.0; Win64; x64)\" " +
                                $"-e \"https://gamebanana.com/\" -o \"{outPath}\" \"{url}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = System.Diagnostics.Process.Start(psi)!;
                p.WaitForExit();
                var got = File.Exists(outPath) ? new FileInfo(outPath).Length : 0;
                log?.Invoke(L("    curl 第 {0} 次: {1} KB / {2} KB", attempt, got / 1024, expectedSize / 1024));
                if (expectedSize <= 0 || got == expectedSize) return true;
            }
            catch (Exception ex) { log?.Invoke(L("    [警告] curl 调用失败: ") + ex.Message); }
            Thread.Sleep(1500);
        }
        return false;
    }

    private static string Str(JsonElement e, params string[] path)
    {
        var cur = e;
        foreach (var p in path)
        {
            if (cur.ValueKind != JsonValueKind.Object || !cur.TryGetProperty(p, out var next)) return "";
            cur = next;
        }
        return cur.ValueKind == JsonValueKind.String ? cur.GetString() ?? "" : "";
    }

    private static int Int(JsonElement e, string p) =>
        e.TryGetProperty(p, out var v) && v.TryGetInt32(out var i) ? i : 0;

    private static long Long(JsonElement e, string p) =>
        e.TryGetProperty(p, out var v) && v.TryGetInt64(out var i) ? i : 0;
}
