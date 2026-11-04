using UndertaleModLib;

namespace NtlMapEdit;

/// <summary>
/// Headless self-check for the read-only resource browser: proves every resource family that
/// used to have no UI is visible to the catalogue, with non-empty names/details, without
/// touching the file on disk. Used by the no-GUI acceptance run.
/// </summary>
public static class ResourceBrowserSelfTest
{
    private static int _pass;
    private static int _fail;

    private static void Check(bool ok, string what)
    {
        if (ok) { _pass++; return; }
        _fail++;
        Console.WriteLine("FAIL " + what);
    }

    public static int Run(string srcPath)
    {
        _pass = 0;
        _fail = 0;
        Console.WriteLine("===== resource-browser-test =====");
        Console.WriteLine("src: " + srcPath);
        if (!File.Exists(srcPath))
        {
            Console.WriteLine("FAIL 源文件不存在");
            Console.WriteLine("RESOURCE-BROWSER-TEST FAIL (1)");
            return 1;
        }
        var bytes = new FileInfo(srcPath).Length;
        var hash = SHA256Of(srcPath);

        UndertaleData data;
        using (var fs = File.OpenRead(srcPath)) data = UndertaleIO.Read(fs, (w, imp) => { }, m => { }, false);

        var counts = ResourceCatalog.Counts(data);
        var mustHave = new HashSet<string> { "rooms", "objects", "sprites", "backgrounds", "fonts", "sounds", "code", "textures" };
        var optional = new HashSet<string> { "shaders", "sequences", "timelines", "paths" };

        foreach (var (key, label, count) in counts)
        {
            Console.WriteLine($"  {key,-12} {count,7}  {label}");
            Check(count >= 0, $"category {key}: enumerates without error");
            if (mustHave.Contains(key)) Check(count > 0, $"category {key}: non-empty");
            else if (optional.Contains(key)) Console.WriteLine($"    (optional) {key} = {count}");
        }
        Check(counts.Length == ResourceCatalog.Categories.Count, "all categories reported");

        // First item of every non-empty category must have a usable name and detail text.
        foreach (var cat in ResourceCatalog.Categories)
        {
            var n = 0;
            foreach (var e in ResourceCatalog.Enumerate(data, cat.Key).Take(25))
            {
                n++;
                Check(!string.IsNullOrWhiteSpace(e.Name), $"category {cat.Key}: entry name non-empty");
                Check(!string.IsNullOrWhiteSpace(e.Detail), $"category {cat.Key}: entry detail non-empty ({e.Name})");
                Check(!string.IsNullOrWhiteSpace(e.Full) && e.Full.Contains('\n'), $"category {cat.Key}: entry full text multi-line ({e.Name})");
            }
            Console.WriteLine($"  sample {cat.Key}: checked {n}");
        }

        // Old loader-level counts still hold (nothing was dropped by the catalogue refactor).
        Console.WriteLine($"  raw: rooms={data.Rooms.Count} sprites={data.Sprites.Count} objects={data.GameObjects.Count} backgrounds={data.Backgrounds.Count} fonts={data.Fonts.Count} sounds={data.Sounds.Count} code={data.Code.Count} scripts={data.Scripts.Count} shaders={data.Shaders.Count} textures={data.EmbeddedTextures.Count} paths={data.Paths.Count} sequences={data.Sequences.Count} timelines={data.Timelines.Count}");
        Check(data.Code.Count > 0 && data.Scripts.Count > 0, "scripts+code present");
        Check(data.Fonts.Count > 0 && data.Sounds.Count > 0, "fonts+sounds present");

        var hash2 = SHA256Of(srcPath);
        var bytes2 = new FileInfo(srcPath).Length;
        Check(hash == hash2 && bytes == bytes2, "源文件只读：测试前后 SHA256/大小不变");

        Console.WriteLine(_fail == 0 ? $"RESOURCE-BROWSER-TEST PASS ({_pass} checks)" : $"RESOURCE-BROWSER-TEST FAIL ({_fail} of {_pass + _fail})");
        return _fail == 0 ? 0 : 1;
    }

    private static string SHA256Of(string path)
    {
        using var fs = File.OpenRead(path);
        using var sha = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(fs));
    }
}
