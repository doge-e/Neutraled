using System.Text.Json;
using System.Text.Json.Nodes;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>mods/ 目录的**唯一**扫描规则（C4 P1-13）。
///
/// 背景：以前"哪些目录算一个 mod 叶子"这套规则在三个地方各写一遍——
///   · builder/Mods.cs 的 ScanMods（部署用，带缓存）
///   · builder/ModInstall.cs 的 FindInstalled（安装记录用）
///   · gui/MainForm.cs 的 RefreshMods（列表用，GUI 不引用 builder 工程）
/// 三份实现稍微不同步就会出现"GUI 里看得到、部署时却扫不到"这类幽灵问题。
/// 现在规则只在这里定义：
///   · Mods.ScanMods（部署）直接用 <see cref="DiscoverLeaves"/>
///   · --mod-list --json / --profile-* 等 CLI JSON 输出用 <see cref="ReadAll"/>
///   · GUI 通过 CLI 的 JSON 读同一份结果（gui/ProfilesDialog、--mod-list --json）
///
/// 叶子规则（与 Mods.cs 历史行为逐字一致，改动会改变部署产物，勿随意动）：
///   A. 旧格式 mods/&lt;chapter&gt;/&lt;mod&gt;/
///   B. 新格式 mods/&lt;mod&gt;/&lt;author&gt;/&lt;chapter&gt;/
///   C. 旧格式（无叶子层）mods/&lt;mod&gt;/mod.json —— 仍可被 builder 扫到（历史兼容）
///   其中 chapter = root 或 chapter 开头且长度 &gt; 7 的目录名。</summary>
public static class ModScanner
{
    /// <summary>一个候选 mod 叶子（目录 + 由路径推导出的章节/名字/作者）。</summary>
    public sealed record Leaf(string Dir, string Chapter, string ModName, string Author);

    /// <summary>一个可显示/可统计的 mod 条目（JSON 输出用；部署仍走 Mods.ScanMods 的 ModEntry）。</summary>
    public sealed record Entry(
        string Path, string Dir, string Id, string Name, string Author,
        string Chapter, string Version, bool Enabled);

    /// <summary>章节叶子目录名：root 或 chapter 开头且长度 &gt; 7（即 chapter1..chapterN，不含裸 chapter）。</summary>
    public static bool IsChapterFolder(string name) =>
        name.Equals("root", StringComparison.OrdinalIgnoreCase) ||
        (name.StartsWith("chapter", StringComparison.OrdinalIgnoreCase) && name.Length > 7);

    /// <summary>与 Mods.Sanitize 同义（id 兜底生成用）：只保留字母数字，其余转下划线并转小写。</summary>
    public static string Sanitize(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in s) sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_');
        return sb.ToString().Trim('_');
    }

    /// <summary>按目录结构发现候选叶子（不读 mod.json、不碰缓存；顺序 = 历史实现的遍历顺序）。</summary>
    public static List<Leaf> DiscoverLeaves(string modsRoot)
    {
        var leaves = new List<Leaf>();
        if (!Directory.Exists(modsRoot)) return leaves;

        foreach (var modDir in Directory.GetDirectories(modsRoot))
        {
            var modName = Path.GetFileName(modDir);
            if (IsChapterFolder(modName))
            {
                // A. 旧格式: mods/<chapter>/<mod>/
                foreach (var sub in Directory.GetDirectories(modDir))
                    leaves.Add(new Leaf(sub, modName, Path.GetFileName(sub), ""));
                continue;
            }

            // B. 新格式: mods/<mod>/<author>/<chapter>/
            bool matchedNew = false;
            foreach (var authorDir in Directory.GetDirectories(modDir))
            {
                var author = Path.GetFileName(authorDir);
                foreach (var chDir in Directory.GetDirectories(authorDir))
                {
                    var chName = Path.GetFileName(chDir);
                    if (!IsChapterFolder(chName)) continue;
                    leaves.Add(new Leaf(chDir, chName, modName, author));
                    matchedNew = true;
                }
            }
            if (matchedNew) continue;

            // C. 旧格式（无叶子层）: mods/<mod>/mod.json
            if (File.Exists(Path.Combine(modDir, "mod.json")))
                leaves.Add(new Leaf(modDir, "", modName, ""));
        }
        return leaves;
    }

    /// <summary>读所有叶子的 mod.json，返回可显示的条目（跳过没有/读不了 mod.json 的叶子）。
    /// 章节优先取叶子目录名，其次取 mod.json 的 chapter；enabled 缺省 = true（与 Mods.ModEntry 一致）。
    /// 排序：(章节, 名字, id)，保证 JSON 输出稳定、便于 diff 与自动化断言。</summary>
    public static List<Entry> ReadAll(string modsRoot, List<string>? warnings = null)
    {
        var list = new List<Entry>();
        foreach (var leaf in DiscoverLeaves(modsRoot))
        {
            var mj = Path.Combine(leaf.Dir, "mod.json");
            if (!File.Exists(mj)) continue;
            try
            {
                var root = JsonNode.Parse(File.ReadAllText(mj), null, new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                })?.AsObject();
                if (root == null) continue;
                string Str(string k) => root[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
                string chapter = leaf.Chapter;
                if (string.IsNullOrEmpty(chapter)) chapter = Str("chapter");
                var name = Str("name");
                if (string.IsNullOrEmpty(name)) name = leaf.ModName;
                var author = Str("author");
                if (string.IsNullOrEmpty(author)) author = leaf.Author;
                var id = Str("id");
                if (string.IsNullOrEmpty(id))
                    id = author != "" ? Sanitize(leaf.ModName) + "." + Sanitize(author) : Sanitize(leaf.ModName);
                var enabled = !(root["enabled"] is JsonValue ev && ev.TryGetValue<bool>(out var b) && b == false);
                list.Add(new Entry(mj, leaf.Dir, id, name, author, chapter, Str("version"), enabled));
            }
            catch (Exception ex)
            {
                // JSON 输出必须保持 stdout 纯 JSON：警告收进 payload（warnings）而不是混进 stdout。
                var msg = L("跳过无法解析的 mod.json: {0}（{1}）", mj, ex.Message);
                if (warnings != null) warnings.Add(msg);
                else Paths.Log("  [警告] " + msg);
            }
        }
        list.Sort((a, b) =>
        {
            int c = string.CompareOrdinal(a.Chapter, b.Chapter);
            if (c != 0) return c;
            c = string.CompareOrdinal(a.Name, b.Name);
            return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
        });
        return list;
    }

    /// <summary>--mod-list --json 的数据源（供 GUI / 外部自动化读同一份扫描结果）。</summary>
    public static JsonObject BuildJson(string gameRoot)
    {
        var modsRoot = Path.Combine(Paths.NeutraledRoot(gameRoot), "mods");
        var warnings = new List<string>();
        var list = ReadAll(modsRoot, warnings);
        var arr = new JsonArray();
        foreach (var m in list)
            arr.Add(new JsonObject
            {
                ["name"] = m.Name,
                ["id"] = m.Id,
                ["author"] = m.Author,
                ["chapter"] = m.Chapter,
                ["version"] = m.Version,
                ["enabled"] = m.Enabled,
                ["path"] = m.Path,
                ["dir"] = m.Dir
            });
        return new JsonObject
        {
            ["schemaVersion"] = 1,
            ["gameRoot"] = gameRoot,
            ["modsRoot"] = modsRoot,
            ["count"] = list.Count,
            ["enabledCount"] = list.Count(x => x.Enabled),
            ["warnings"] = new JsonArray(warnings.Select(w => (JsonNode)JsonValue.Create(w)!).ToArray()),
            ["mods"] = arr
        };
    }

    /// <summary>打印 --mod-list --json（缩进、非 ASCII 原样、未知键保留）。</summary>
    public static int CliJson(string gameRoot)
    {
        var opts = new JsonSerializerOptions(Paths.Json) { WriteIndented = true };
        Console.WriteLine(BuildJson(gameRoot).ToJsonString(opts));
        return 0;
    }
}
