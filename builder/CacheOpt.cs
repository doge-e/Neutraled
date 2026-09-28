using System.Security.Cryptography;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>缓存优化 —— 去重（B3）与增量部署（B4）。
///
/// B3 去重：多份缓存里往往有大量相同文件（比如同一个 data.win 的多份副本）。
///         用一个「内容哈希 → 主副本」的中央池，其余全部改成硬链接，可省一半以上空间。
///
/// B4 增量：把「输入函数重定向」「内置函数 Hook」这类耗时步骤的**结果**缓存起来，
///         当 mod 集合、api 版本、目标章节都没变时直接复用，跳过反编译。
/// </summary>
public static class CacheOpt
{
    /// <summary>B3：对缓存目录做硬链接去重，返回节省的字节数</summary>
    public static long Deduplicate(string cacheRoot)
    {
        if (!Directory.Exists(cacheRoot)) return 0;

        var pool = Path.Combine(cacheRoot, ".pool");
        Directory.CreateDirectory(pool);

        var byHash = new Dictionary<string, string>(StringComparer.Ordinal);   // hash -> 主副本路径
        long saved = 0;
        int linked = 0;

        foreach (var f in Directory.GetFiles(cacheRoot, "*", SearchOption.AllDirectories))
        {
            // 跳过池本身与索引
            if (f.StartsWith(pool, StringComparison.OrdinalIgnoreCase)) continue;
            if (f.EndsWith("index.json", StringComparison.OrdinalIgnoreCase)) continue;

            FileInfo fi;
            try { fi = new FileInfo(f); } catch { continue; }
            if (!fi.Exists || fi.Length < 4096) continue;      // 小文件不值得去重

            string hash;
            try { hash = HashFile(f); } catch { continue; }

            if (byHash.TryGetValue(hash, out var master))
            {
                // 已是同一份（可能已经是硬链接）→ 跳过
                try
                {
                    if (SameFile(f, master)) continue;
                }
                catch { }

                // 删除并建立硬链接
                try
                {
                    var size = fi.Length;
                    var tmp = f + ".dedup-tmp";
                    File.Move(f, tmp);
                    File.Delete(tmp);
                    if (Platform.TryHardLink(f, master))   // 跨平台硬链接
                    {
                        saved += size;
                        linked++;
                    }
                    else
                    {
                        File.Copy(master, f);   // 硬链接失败则复制回去
                    }
                }
                catch { }
            }
            else
            {
                byHash[hash] = f;
            }
        }

        Paths.Log(L("  缓存去重: 合并 {0} 个文件，节省 {1} MB", linked, saved / 1024 / 1024));
        return saved;
    }

    /// <summary>B4：重定向结果缓存 —— 判断是否可以跳过耗时步骤</summary>
    public static bool TryGetRedirectCache(string neutraledRoot, string chapter, string modsSignature, out string cachedJson)
    {
        cachedJson = "";
        var path = Path.Combine(neutraledRoot, "cache", "redirect-" + chapter + ".json");
        if (!File.Exists(path)) return false;
        try
        {
            var txt = File.ReadAllText(path);
            var doc = System.Text.Json.JsonDocument.Parse(txt);
            if (doc.RootElement.TryGetProperty("sig", out var sig) && sig.GetString() == modsSignature)
            {
                cachedJson = txt;
                return true;
            }
        }
        catch { }
        return false;
    }

    public static void SaveRedirectCache(string neutraledRoot, string chapter, string modsSignature, string json)
    {
        try
        {
            var dir = Path.Combine(neutraledRoot, "cache");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "redirect-" + chapter + ".json"), json);
        }
        catch { }
    }

    // ---------- 辅助 ----------
    private static string HashFile(string path)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        // 大文件只哈希头 1MB + 长度（足够区分，且极快）
        var buf = new byte[Math.Min(1024 * 1024, fs.Length)];
        fs.Read(buf, 0, buf.Length);
        var head = sha.ComputeHash(buf);
        return Convert.ToHexString(head) + "-" + fs.Length;
    }

    private static bool SameFile(string a, string b)
    {
        try
        {
            var fa = new FileInfo(a);
            var fb = new FileInfo(b);
            return fa.Length == fb.Length && fa.LastWriteTimeUtc == fb.LastWriteTimeUtc;
        }
        catch { return false; }
    }

}
