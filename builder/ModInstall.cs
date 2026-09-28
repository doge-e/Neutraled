using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>一键安装 / 更新 mod（F6）—— 打通玩家侧的最后一公里。
///
/// 三类输入：
///   1) **.ntlmod 文件**（Neutraled 自己的打包格式，就是 zip）
///   2) **zip 文件**（常见 mod 压缩包）
///   3) **目录**（解压好的 mod）
///
/// 安装流程：
///   1) 识别 mod 根目录（含 mod.json 的那一层）
///   2) 读取 id / name / author / version
///   3) 检查是否已安装 → 已装则为"更新"，备份旧版本
///   4) 复制到 mods/<name>/<author>/<chapter>/
///   5) 写入 install.json（记录来源、时间、版本）供更新检查
///
/// 更新检查：
///   --mod-updates   对比本地 install.json 与远端清单
/// </summary>
public static class ModInstall
{
    public sealed class Installed
    {
        public string Id = "";
        public string Name = "";
        public string Author = "";
        public string Version = "";
        public string Source = "";      // 来源 URL 或文件路径
        public string InstalledAt = "";
        public string Dir = "";
    }

    /// <summary>安装/更新一个 mod 包</summary>
    public static int Install(string gameRoot, string packagePath, bool force = false)
    {
        var ntlRoot = Paths.NeutraledRoot(gameRoot);
        var modsRoot = Path.Combine(ntlRoot, "mods");

        Console.WriteLine(L("===== mod 安装 ====="));
        Console.WriteLine(L("  包: {0}", packagePath));

        string stageDir;

        // ---------- 1) 解压到临时目录 ----------
        try
        {
            stageDir = Path.Combine(Path.GetTempPath(), "ntl-install-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(stageDir);

            if (Directory.Exists(packagePath))
            {
                // 已经是目录 → 直接复制
                CopyDir(packagePath, stageDir);
            }
            else if (File.Exists(packagePath))
            {
                ZipFile.ExtractToDirectory(packagePath, stageDir, true);
            }
            else { Console.WriteLine(L("[错误] 找不到包")); return 1; }
        }
        catch (Exception ex) { Console.WriteLine(L("[错误] 解压失败: {0}", ex.Message)); return 1; }

        try
        {
            // ---------- 2) 找 mod.json ----------
            var modJson = FindFile(stageDir, "mod.json");
            if (modJson == null)
            {
                Console.WriteLine(L("[错误] 包里没有 mod.json（不是有效的 Neutraled/Kristal mod）"));
                return 1;
            }
            var modRoot = Path.GetDirectoryName(modJson)!;
            Console.WriteLine(L("  mod 根目录: {0}", Path.GetRelativePath(stageDir, modRoot)));

            // ---------- 3) 读元数据 ----------
            string id = Path.GetFileName(modRoot), name = id, author = "unknown", version = "1.0.0";
            string chapterHint = "";   // mod.json 的 chapter 字段（叶子名提示），见下面的目标目录推导
            try
            {
                var doc = JsonDocument.Parse(File.ReadAllText(modJson),
                    new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                var r = doc.RootElement;
                if (r.TryGetProperty("id", out var a)) id = a.GetString() ?? id;
                if (r.TryGetProperty("name", out var b)) name = b.GetString() ?? name;
                if (r.TryGetProperty("author", out var c)) author = c.GetString() ?? author;
                if (r.TryGetProperty("authors", out var c2) && c2.ValueKind == JsonValueKind.Array && c2.GetArrayLength() > 0)
                    author = c2[0].GetString() ?? author;
                if (r.TryGetProperty("version", out var d)) version = d.GetString() ?? version;
                if (r.TryGetProperty("chapter", out var e))
                    chapterHint = (e.ValueKind == JsonValueKind.Number) ? ("chapter" + e.GetRawText()) : (e.GetString() ?? "");
            }
            catch { }

            Console.WriteLine(L("  名称: {0}  作者: {1}  版本: {2}  id: {3}", name, author, version, id));

            // ---------- 4) 目标目录 ----------
            var safeName = Sanitize(name);
            var safeAuthor = Sanitize(author);
            // ★ 2026-09-26 修（B2）：以前直接写进 mods/<name>/<author>/（**没有叶子层**），而扫描器
            //   Mods.cs:300-329 只认 mods/<mod>/<author>/<叶子>/mod.json（Mods.cs:454-456：叶子必须是
            //   root 或 chapterN）→ 装完的 mod 零候选、零警告，静默不进任何产物（docs/PIPELINE.md 指定的
            //   原生入口等于无效）。叶子来源依次为：包文件名提示（--pack 默认名 <mod>-chapterN.ntlmod，
            //   正则与 Mods.cs:287 同一份口径）→ mod.json 的 chapter 字段 → 兜底 root + 响亮警告。
            var leaf = "";
            var pkgBase = Path.GetFileNameWithoutExtension(packagePath);
            var mLeaf = System.Text.RegularExpressions.Regex.Match(pkgBase,
                @"(?:^|[_-])(chapter\d|root)(?:$|[_-])", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (mLeaf.Success) leaf = mLeaf.Groups[1].Value.ToLowerInvariant();
            if (leaf == "" && chapterHint != "")
            {
                var _ch = chapterHint.Trim().ToLowerInvariant();
                if (System.Text.RegularExpressions.Regex.IsMatch(_ch, @"^\d+$")) _ch = "chapter" + _ch;
                if (System.Text.RegularExpressions.Regex.IsMatch(_ch, @"^(chapter\d+|root)$")) leaf = _ch;
            }
            if (leaf == "")
            {
                leaf = "root";
                Console.WriteLine(L("  [警告] 包名与 mod.json 都没有章节提示（chapterN / root）。"));
                Console.WriteLine(L("          已按 root 目标安装 —— 章节 mod 请把包改名成 <名字>-chapterN.ntlmod，"));
                Console.WriteLine(L("          或在 mod.json 里加 \"chapter\": \"chapterN\"，否则它不会进入对应章节产物。"));
            }
            var targetRoot = Path.Combine(modsRoot, safeName, safeAuthor, leaf);
            Console.WriteLine(L("  部署目标（叶子）: {0}", leaf));

            // 检测已安装（按 id）
            var existing = FindInstalled(ntlRoot).FirstOrDefault(x => x.Id == id);
            if (existing != null && !force)
            {
                Console.WriteLine(L("  已安装（版本 {0}）→ 将更新到 {1}", existing.Version, version));
                // 备份旧版本
                try
                {
                    var bakDir = Path.Combine(ntlRoot, ".mod-backup", safeName + "-" + existing.Version);
                    if (Directory.Exists(Path.Combine(modsRoot, safeName)) && !Directory.Exists(bakDir))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(bakDir)!);
                        CopyDir(Path.Combine(modsRoot, safeName), bakDir);
                        Console.WriteLine(L("  旧版本已备份: {0}", bakDir));
                    }
                }
                catch (Exception ex) { Console.WriteLine(L("  [警告] 备份失败: {0}", ex.Message)); }
            }

            // ---------- 5) 复制 ----------
            Directory.CreateDirectory(targetRoot);
            CopyDir(modRoot, targetRoot);
            Console.WriteLine(L("  已安装到: {0}", targetRoot));

            // ---------- 6) 写安装记录 ----------
            var rec = new JsonObject
            {
                ["id"] = id,
                ["name"] = name,
                ["author"] = author,
                ["version"] = version,
                ["source"] = packagePath,
                ["installedAt"] = DateTime.Now.ToString("s"),
                ["dir"] = targetRoot
            };
            File.WriteAllText(Path.Combine(targetRoot, "install.json"),
                rec.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            Console.WriteLine();
            Console.WriteLine(L("  ✅ 安装完成"));
            Console.WriteLine(L("  下一步: 关闭游戏后运行 launch.ps1 部署并启动"));
            return 0;
        }
        finally
        {
            try { if (Directory.Exists(stageDir)) Directory.Delete(stageDir, true); } catch { }
        }
    }

    /// <summary>列出已安装的 mod（带版本信息）</summary>
    public static List<Installed> FindInstalled(string ntlRoot)
    {
        var list = new List<Installed>();
        var modsRoot = Path.Combine(ntlRoot, "mods");
        if (!Directory.Exists(modsRoot)) return list;

        foreach (var rec in Directory.GetFiles(modsRoot, "install.json", SearchOption.AllDirectories))
        {
            try
            {
                var doc = JsonDocument.Parse(File.ReadAllText(rec));
                var r = doc.RootElement;
                list.Add(new Installed
                {
                    Id = r.TryGetProperty("id", out var a) ? (a.GetString() ?? "") : "",
                    Name = r.TryGetProperty("name", out var b) ? (b.GetString() ?? "") : "",
                    Author = r.TryGetProperty("author", out var c) ? (c.GetString() ?? "") : "",
                    Version = r.TryGetProperty("version", out var d) ? (d.GetString() ?? "") : "",
                    Source = r.TryGetProperty("source", out var e) ? (e.GetString() ?? "") : "",
                    InstalledAt = r.TryGetProperty("installedAt", out var f) ? (f.GetString() ?? "") : "",
                    Dir = Path.GetDirectoryName(rec) ?? ""
                });
            }
            catch { }
        }

        // 也扫描没有 install.json 的 mod（手动放进去的）
        foreach (var mj in Directory.GetFiles(modsRoot, "mod.json", SearchOption.AllDirectories))
        {
            var dir = Path.GetDirectoryName(mj)!;
            if (File.Exists(Path.Combine(dir, "install.json"))) continue;
            try
            {
                var doc = JsonDocument.Parse(File.ReadAllText(mj),
                    new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                var r = doc.RootElement;
                list.Add(new Installed
                {
                    Id = r.TryGetProperty("id", out var a) ? (a.GetString() ?? "") : "",
                    Name = r.TryGetProperty("name", out var b) ? (b.GetString() ?? Path.GetFileName(dir)) : "",
                    Version = r.TryGetProperty("version", out var d) ? (d.GetString() ?? "?") : "?",
                    Source = L("（手动放置）"),
                    Dir = dir
                });
            }
            catch { }
        }

        return list;
    }

    public static void ListInstalled(string gameRoot)
    {
        var ntlRoot = Paths.NeutraledRoot(gameRoot);
        var list = FindInstalled(ntlRoot);
        Console.WriteLine(L("===== 已安装的 mod ====="));
        if (list.Count == 0) { Console.WriteLine(L("  （无）")); return; }
        foreach (var m in list.OrderBy(x => x.Name))
        {
            Console.WriteLine($"  {m.Name,-24} v{m.Version,-10} {m.Author,-14} {m.Source}");
        }
        Console.WriteLine();
        Console.WriteLine(L("  共 {0} 个", list.Count));
    }

    // ---------- 辅助 ----------
    private static string Sanitize(string s)
    {
        var bad = Path.GetInvalidFileNameChars();
        var t = new string(s.Where(c => !bad.Contains(c)).ToArray()).Trim();
        return t.Length == 0 ? "unknown" : t;
    }

    private static string? FindFile(string dir, string name)
    {
        var direct = Path.Combine(dir, name);
        if (File.Exists(direct)) return direct;
        foreach (var f in Directory.GetFiles(dir, name, SearchOption.AllDirectories)) return f;
        return null;
    }

    private static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src))
        {
            try { File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true); } catch { }
        }
        foreach (var d in Directory.GetDirectories(src))
            CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }
}
