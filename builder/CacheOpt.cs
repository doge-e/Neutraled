using System.Security.Cryptography;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>缓存优化 —— 硬链接去重（B3）。
///
/// B3 去重：多份缓存里往往有大量相同文件（比如同一个 data.win 的多份副本）。
///         用一个「内容哈希 → 主副本」的中央池，其余全部改成硬链接，可省一半以上空间。
///
/// 产物级「跳过整天部署」由 Cache.cs 的签名缓存负责（api 指纹 + 游戏版本 + 章节 + 启用 mod + 基底指纹），
/// 所以不再单独缓存「输入函数重定向」的结果（B4 已移除）。
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

    // B4（重定向结果缓存）已移除（2026-11-05）：TryGetRedirectCache / SaveRedirectCache 从来没有调用点。
    // 原因：整个产物级缓存（Cache.cs：api 指纹 + 游戏版本 + 章节 + 启用 mod + 基底指纹 → 硬链接复用）
    // 已经覆盖「mod 集合/版本不变 → 跳过整次部署」，把输入重定向结果单独缓存没有收益；
    // 而 mod 集合一变，重定向的代码块索引也会随之位移，跨签名复用结果反而不安全。

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
