using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>旧式 xdelta / 修改版 data.win 的转换：生成 Neutraled mod。
/// 输出统一格式：mods/&lt;mod_name&gt;/&lt;author&gt;/&lt;chapterN|root&gt;/</summary>
public static class DeltaImport
{
    /// <summary>转换入口（文件 / zip / 目录）。</summary>
    public static int Import(string gameRoot, string input, string chapter, string modName, string author = "unknown")
    {
        if (Directory.Exists(input)) return ImportDirectory(gameRoot, input, modName, author, chapter);
        return ImportOne(gameRoot, input, chapter, modName, author, Path.GetDirectoryName(input) ?? ".");
    }

    /// <summary>目录批量：识别目录内的 chapterN.xdelta / chapterN_windows/data.win / main.xdelta。</summary>
    private static int ImportDirectory(string gameRoot, string dir, string modName, string author, string fallbackChapter)
    {
        var jobs = new List<(string file, string chapter)>();
        foreach (var f in Directory.GetFiles(dir, "*.xdelta", SearchOption.AllDirectories))
        {
            var fn = Path.GetFileNameWithoutExtension(f);
            var ch = InferChapter(fn, Path.GetDirectoryName(f)!, dir);
            if (ch != null) jobs.Add((f, ch));
        }
        foreach (var f in Directory.GetFiles(dir, "data.win", SearchOption.AllDirectories))
        {
            var ch = InferChapter(Path.GetFileName(Path.GetDirectoryName(f)!) ?? "", Path.GetDirectoryName(f)!, dir);
            if (ch != null && !jobs.Any(j => j.chapter == ch)) jobs.Add((f, ch));
        }
        if (jobs.Count == 0)
        {
            Paths.Log(L("  [错误] 目录内未找到可识别的 xdelta / data.win"));
            return 1;
        }

        int ok = 0, fail = 0;
        foreach (var (file, ch) in jobs.OrderBy(j => j.chapter))
        {
            Paths.Log(L("--- 转换 {0}: {1} ---", ch, Path.GetFileName(file)));
            var rc = ImportOne(gameRoot, file, ch, modName, author, dir);
            if (rc == 0) ok++; else fail++;
        }
        Paths.Log(L("批量转换完成: {0} 成功, {1} 失败", ok, fail));
        return fail == 0 ? 0 : 1;
    }

    /// <summary>章节推断：文件名 chapterN / chN / main(→root)，或所在目录 chapterN_windows。</summary>
    private static string? InferChapter(string fileNameNoExt, string fileDir, string rootDir)
    {
        var m = System.Text.RegularExpressions.Regex.Match(fileNameNoExt,
            @"(?:^|[_\-\s])(?:chapter|ch)0?(\d)(?:[_\-\s.]|$)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m.Success) return "chapter" + m.Groups[1].Value;
        if (fileNameNoExt.Equals("main", StringComparison.OrdinalIgnoreCase)) return "root";
        if (fileNameNoExt.Equals("data", StringComparison.OrdinalIgnoreCase))
        {
            var rel = Path.GetRelativePath(rootDir, fileDir).Replace('\\', '/');
            if (rel.StartsWith("chapter", StringComparison.OrdinalIgnoreCase))
                return rel.Split('/')[0];
            return "root";
        }
        var rel2 = Path.GetRelativePath(rootDir, fileDir).Replace('\\', '/');
        var dm = System.Text.RegularExpressions.Regex.Match(rel2, @"chapter(\d)_(windows|linux|unix|macos)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (dm.Success) return "chapter" + dm.Groups[1].Value;
        return null;
    }

    private static int ImportOne(string gameRoot, string input, string chapter, string modName, string author, string sourceRoot)
    {
        var neutraled = Paths.NeutraledRoot(gameRoot);
        var modDir = Path.Combine(neutraled, "mods", Sanitize(modName), Sanitize(author), chapter);
        Directory.CreateDirectory(Path.Combine(modDir, "ref"));

        var work = Path.Combine(Path.GetTempPath(), "ntl_conv_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(work);
        try
        {
            string source = input;
            string extractRoot = sourceRoot;
            var ext = Path.GetExtension(input).ToLowerInvariant();
            if (ext is ".7z" or ".rar" or ".tar" or ".gz")
        {
            work = Path.Combine(Path.GetTempPath(), "ntl-delta-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(work);
            ModdingImport.ExtractAny(input, work);
        }
        else if (ext == ".zip")
            {
                ZipFile.ExtractToDirectory(input, work, true);
                extractRoot = work;
                var all = Directory.GetFiles(work, "*.xdelta", SearchOption.AllDirectories).ToList();
                if (all.Count > 0)
                {
                    var num = ChapterNumber(chapter);
                    string? pick = num > 0
                        ? all.FirstOrDefault(f => System.Text.RegularExpressions.Regex.IsMatch(
                            Path.GetFileName(f), $@"(?:^|[_-s])(?:chapter|ch)0?{num}(?:[_-s.]|$)",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                        : null;
                    if (pick == null)
                    {
                        if (num > 0 && all.Count > 1)
                        {
                            Paths.Log(L("  [错误] zip 内无 chapter{0} 补丁，可用: {1}", num, string.Join(", ", all.Select(f => Path.GetFileName(f)))));
                            return 1;
                        }
                        pick = all.OrderBy(f => f).First();
                    }
                    source = pick;
                    Paths.Log(L("  发现: {0}（共 {1} 个补丁）", Path.GetFileName(source), all.Count));
                }
                else
                {
                    var cand = Directory.GetFiles(work, "data.win", SearchOption.AllDirectories).FirstOrDefault();
                    if (cand == null) { Paths.Log(L("  [错误] zip 内未找到 .xdelta 或 data.win")); return 1; }
                    source = cand;
                }
            }

            var baseline = Paths.BackupDataWin(gameRoot, chapter);
            if (!File.Exists(baseline)) { Paths.Log(L("  [错误] 缺少原版基线: {0}", baseline)); return 1; }

            var outWin = Path.Combine(modDir, "ref", "data.win");
            if (Path.GetExtension(source).ToLowerInvariant() == ".xdelta")
            {
                var xdelta = FindXdelta(gameRoot);
                if (xdelta == null) { Paths.Log(L("  [错误] 未找到 xdelta3.exe")); return 1; }
                Paths.Log(L("  应用 xdelta: {0}", Path.GetFileName(source)));
                var psi = new ProcessStartInfo(xdelta)
                {
                    Arguments = $"-d -f -s \"{baseline}\" \"{source}\" \"{outWin}\"",
                    RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false
                };
                using var p = Process.Start(psi)!;
                var err = p.StandardError.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode != 0 || !File.Exists(outWin))
                {
                    Paths.Log(L("  [错误] xdelta 应用失败 (exit={0}): {1}", p.ExitCode, err.Trim()));
                    Paths.Log(L("  提示：该补丁可能基于其它游戏版本，请从 GameBanana 下载适配当前版本的补丁"));
                    return 1;
                }
            }
            else
            {
                File.Copy(source, outWin, true);
                Paths.Log(L("  复制修改版 data.win"));
            }

            var data = Injector.Load(outWin);
            Paths.Log(L("  产物: 对象={0} 代码={1} 精灵={2} 声音={3}", data.GameObjects.Count, data.Code.Count, data.Sprites.Count, data.Sounds.Count));

            // 收集外部文件（lang / vid / 其它）→ mod/files/
            int fileCount = CollectExternalFiles(sourceRoot, work, chapter, modDir);
            if (fileCount > 0) Paths.Log(L("  外部文件: {0} 个", fileCount));

            var modJson = new
            {
                id = "converted." + Sanitize(modName) + "." + Sanitize(author),
                name = modName,
                author,
                version = "1.0.0",
                enabled = true,
                references = new { source = "ref/data.win", assets = "inherit" }
            };
            File.WriteAllText(Path.Combine(modDir, "mod.json"),
                JsonSerializer.Serialize(modJson, new JsonSerializerOptions { WriteIndented = true }));

            Paths.Log(L("  已生成 mod: {0}", modDir));
            return 0;
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { }
        }
    }

    /// <summary>在导入源目录里定位章节子目录：chapterN_windows / _linux / _unix / _macos 任一种存在即可
    /// （跨平台：别人在 Linux 上导出的 mod 目录不该认不出来）。</summary>
    private static string ChapterSubdirFor(string root, string chapter)
    {
        foreach (var s in new[] { "windows", "linux", "unix", "macos" })
        {
            var p = Path.Combine(root, chapter + "_" + s);
            if (Directory.Exists(p)) return p;
        }
        return Path.Combine(root, chapter + "_" + Platform.ChapterSuffix(root));
    }

    /// <summary>把源目录里 chapterN_windows/** （root 用顶层）的外部文件复制到 mod/files/。</summary>
    private static int CollectExternalFiles(string sourceRoot, string workRoot, string chapter, string modDir)
    {
        var roots = new List<string>();
        if (Directory.Exists(sourceRoot)) roots.Add(sourceRoot);
        if (Directory.Exists(workRoot) && workRoot != sourceRoot) roots.Add(workRoot);

        int copied = 0;
        foreach (var root in roots)
        {
            string sub = chapter.Equals("root", StringComparison.OrdinalIgnoreCase)
                ? root
                : ChapterSubdirFor(root, chapter);
            if (!Directory.Exists(sub)) continue;

            foreach (var f in Directory.GetFiles(sub, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(sub, f);
                var name = Path.GetFileName(f).ToLowerInvariant();
                if (name == "data.win" || name.StartsWith("audiogroup") || rel.StartsWith("Neutraled")) continue;
                var dst = Path.Combine(modDir, "files", rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Copy(f, dst, true);
                copied++;
            }
            if (copied > 0) break;
        }
        return copied;
    }

    public static string? FindXdelta(string gameRoot)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "xdelta3.exe"),
            Path.Combine(gameRoot, "xdelta", "xdelta3.exe"),
            Path.Combine(gameRoot, "Kristal-main", "xdelta.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static int ChapterNumber(string chapter)
    {
        var digits = new string(chapter.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var n) ? n : 0;
    }

    public static string Sanitize(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in s) sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_');
        return sb.ToString().Trim('_');
    }
}
