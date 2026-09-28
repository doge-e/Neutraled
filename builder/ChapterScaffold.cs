using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>--new-chapter：从零生成一个**可运行**的自定义章节（模板 + 脚手架）。
///
/// 改这个文件之前先读这几条（都是踩过的坑）：
///   1. Mods.ScanMods 只认三种摆放，最通用的是 mods/&lt;mod&gt;/&lt;作者&gt;/&lt;chapter*&gt;/，
///      叶子目录名**必须以 "chapter" 开头且长度 &gt; 7**（Mods.IsChapterFolder），否则整个 mod 被静默忽略。
///   2. mod.json 必须 "enabled": true，否则扫描（includeDisabled=false）直接跳过。
///   3. 自定义章节要在 chapters.json 里成为一条**时间线条目**：mod.json 里写
///      {"timeline":true,"order":N,"name":"显示名","slug":"ascii-slug"}。
///      它必须自带基底 data.win（Chapters.cs:244 找 &lt;mod&gt;/../data/&lt;显示名&gt;/data.win，回退 data/data.win），
///      否则 DeployTimelines 报「平行时间线缺少自带 data」。本命令统一写 data/data.win（与显示名无关）。
///   4. 产物目录名必须含 "chapter"（api/ntl_is_root.gml:6 靠它判断"自己不在 root"），
///      所以 mod id 固定 ntl.chapter.&lt;slug&gt; → 产物目录 ntl_timeline_N_ntl_chapter_* 天然满足。
///   5. 给 GML 读的 JSON 一律用 UnsafeRelaxedJsonEscaping —— GameMaker 的 json_parse 吃不下 \uXXXX 转义。
///   6. 地图只能从 program_directory + "Neutraled/kristal-maps/" 读（api/ntl_map_load.gml:13），
///      所以走 mod 的 files/ 通道（Deploy 把叶子目录下 files/ 整棵树覆盖进章节目录，Program.cs:2030），
///      同时也写一份到游戏根的 Neutraled/kristal-maps/ 做双保险。
/// </summary>
public static class ChapterScaffold
{
    private const int Tile = 40;
    private const int RoomCols = 16;
    private const int RoomRows = 12;
    private const int RoomW = RoomCols * Tile;   // 640 —— 正好一屏（room_width），整图 1:1 画出来不用滚动
    private const int RoomH = RoomRows * Tile;   // 480
    private const int ExitTop = 160;             // 右墙缺口上沿
    private const int ExitBottom = 280;          // 右墙缺口下沿
    private const int ExitX = 602;               // 玩家 x 到这个值就算"走出房间"（玩家宽 20，clamp 上限 620）

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>生成一个自定义章节。返回 0 成功 / 1 失败。
    /// baseChapterArg 用 Program.cs 里那个 chapter 字符串（"chapter1"…"chapter5"）。</summary>
    public static int Create(string gameRoot, string? rawName, string? rawAuthor, string mode, string baseChapterArg,
                             int slot, string? forkSpec, string template, bool force)
    {
        var ntlRoot = Paths.NeutraledRoot(gameRoot);
        var modsRoot = Paths.ModsRoot(gameRoot);

        // ---------- 1) 参数校验（失败就说清楚怎么改，不要只说"参数错误"）----------
        if (string.IsNullOrWhiteSpace(rawName))
        {
            Paths.Log(L("  [错误] --new-chapter 需要一个章节名字，例如："));
            Paths.Log(L("         ntl-builder.exe --new-chapter \"我的第一个章节\""));
            return 1;
        }
        var name = rawName.Trim();
        if (name.Equals("help", StringComparison.OrdinalIgnoreCase) || name == "?")
            return PrintScaffoldHelp(gameRoot);
        var bad = BadFileNameChar(name);
        if (bad != null) { Paths.Log(L("  [错误] 章节名字不能包含 {0}（它会当作 mod 目录名）", bad)); return 1; }
        var author = string.IsNullOrWhiteSpace(rawAuthor) ? "Player" : rawAuthor!.Trim();
        bad = BadFileNameChar(author);
        if (bad != null) { Paths.Log(L("  [错误] 作者名不能包含 {0}（它会当作目录名）", bad)); return 1; }

        mode = string.IsNullOrWhiteSpace(mode) ? "independent" : mode.Trim().ToLowerInvariant();
        if (mode != "independent" && mode != "overlay" && mode != "fork")
        {
            Paths.Log(L("  [错误] --mode 只支持 independent / overlay / fork（收到 '{0}'）", mode));
            return 1;
        }
        template = string.IsNullOrWhiteSpace(template) ? "room" : template.Trim().ToLowerInvariant();

        var baseNum = ChapterNumFrom(baseChapterArg);
        var baseName = "chapter" + baseNum;

        // ---------- 2) fork 源解析 ----------
        ModEntry? src = null;
        var leaf = baseName;
        if (mode == "fork")
        {
            if (string.IsNullOrWhiteSpace(forkSpec))
            {
                Paths.Log(L("  [错误] --mode fork 需要 --fork <mod目录名|mod id>[:作者[:chapterN]]"));
                Paths.Log(L("         先跑 ntl-builder.exe --new-chapter help 看可 fork 的清单，或见下面的列表"));
                ListForkable(modsRoot);
                return 1;
            }
            src = ResolveFork(modsRoot, forkSpec!.Trim());
            if (src == null) return 1;
            leaf = Path.GetFileName(src.Dir.TrimEnd('\\', '/'));
            Paths.Log(L("  [fork] 源章节：{0}（id={1}，chapter={2}）", Path.GetRelativePath(gameRoot, src.Dir), src.Id, src.Chapter));
        }

        // 叶子目录名必须能被 Mods.IsChapterFolder 认出来，否则 mod 会被静默跳过
        if (!IsChapterFolderName(leaf))
        {
            Paths.Log(L("  [错误] mod 的叶子目录名必须是 \"root\" 或以 \"chapter\" 开头且长度 > 7，当前是 \"{0}\"", leaf));
            Paths.Log(L("         （Mods.IsChapterFolder 的硬性要求；fork 时说明源 mod 的目录结构不标准）"));
            return 1;
        }

        var slug = Slugify(name);
        var modId = "ntl.chapter." + slug;
        var prefix = "ch_" + slug.Replace('-', '_');
        var mapId = "ntl_" + slug + "_room";
        var theSlot = slot > 0 ? slot : SuggestSlot(modsRoot, Path.Combine(modsRoot, name));

        var modDir = Path.Combine(modsRoot, name, author);   // mods/<名字>/<作者>
        var leafDir = Path.Combine(modDir, leaf);            // mods/<名字>/<作者>/chapterN

        // ---------- 3) 模板目录 ----------
        var tplDir = Path.Combine(ntlRoot, "templates", "chapter-" + template);
        if (mode != "fork" && !Directory.Exists(tplDir))
        {
            Paths.Log(L("  [错误] 找不到模板目录：{0}", tplDir));
            Paths.Log(L("         可用模板："));
            var tplRoot = Path.Combine(ntlRoot, "templates");
            if (Directory.Exists(tplRoot))
                foreach (var d in Directory.GetDirectories(tplRoot))
                    Paths.Log("           - " + Path.GetFileName(d).Replace("chapter-", ""));
            else
                Paths.Log(L("           （Neutraled/templates 不存在 —— 安装包不完整？）"));
            return 1;
        }

        // ---------- 4) 建目录 ----------
        if (Directory.Exists(leafDir))
        {
            if (!force)
            {
                Paths.Log(L("  [错误] 目录已存在：{0}", Path.GetRelativePath(gameRoot, leafDir)));
                Paths.Log(L("         换个章节名字，或者加 --force 覆盖（会先删掉这个目录再重建）"));
                return 1;
            }
            Paths.Log(L("  [覆盖] 先删除已存在的目录：{0}", Path.GetRelativePath(gameRoot, leafDir)));
            Directory.Delete(leafDir, true);
        }
        Directory.CreateDirectory(leafDir);
        Paths.Log(L("  [目录] {0}", Path.GetRelativePath(gameRoot, leafDir)));

        // ---------- 5) 写文件 ----------
        var wrote = new List<string>();
        if (mode == "fork")
        {
            CopyTree(src!.Dir, leafDir);
            // fork 的源 mod 自带基底数据在 mod 级目录（mods/<mod>/<作者>/data/），一并搬过来
            var srcModDir = ParentOf(ParentOf(src.Dir));
            var srcData = Path.Combine(srcModDir, "data");
            if (Directory.Exists(srcData))
            {
                CopyTree(srcData, Path.Combine(modDir, "data"));
                Paths.Log(L("  [fork] 复制源 mod 的 data/ 目录（基底 data.win 等）"));
            }
            RewriteForkModJson(gameRoot, Path.Combine(leafDir, "mod.json"), name, author, modId, theSlot, slug, src);
        }
        else
        {
            var vars = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["NAME"] = name,
                ["AUTHOR"] = author,
                ["SLOT"] = theSlot.ToString(),
                ["BASE_CHAPTER"] = baseNum.ToString(),
                ["CHAPTER_FOLDER"] = leaf,
                ["MOD_ID"] = modId,
                ["PREFIX"] = prefix,
                ["MAP_ID"] = mapId,
                ["DIR_NAME"] = name,
                ["MODE"] = mode,
                ["MODE_LABEL"] = mode == "overlay" ? "官方章节覆盖层" : "独立章节",
                ["TEMPLATE"] = "chapter-" + template,
                ["DATE"] = DateTime.Now.ToString("yyyy-MM-dd"),
                ["EXIT_X"] = ExitX.ToString(),
                ["CHAPTER_DECL"] = ChapterDeclText(mode, baseNum, theSlot, name, slug),
                ["BOOT"] = BuildBoot(mode, name, mapId, prefix),
                ["GUARD"] = BuildGuard(mode, name, leaf)
            };
            foreach (var f in Directory.GetFiles(tplDir, "*.tmpl", SearchOption.AllDirectories).OrderBy(x => x))
            {
                var rel = Path.GetRelativePath(tplDir, f);
                // 文件名里也能写占位符：模板 gml/{{PREFIX}}_frame.gml.tmpl -> gml/ch_xxx_frame.gml
                // （脚本名 = 文件名，GML 里 ntl_hook("on_frame", <PREFIX>_frame) 必须与它对上；
                //   用通用文件名 frame.gml 会和游戏自带脚本重名，被 Injector 改名后钩子就断了）
                var outRel = rel.Substring(0, rel.Length - ".tmpl".Length);
                foreach (var kv in vars) outRel = outRel.Replace("{{" + kv.Key + "}}", kv.Value);
                var dst = Path.Combine(leafDir, outRel);
                var text = File.ReadAllText(f);
                foreach (var kv in vars) text = text.Replace("{{" + kv.Key + "}}", kv.Value);
                var left = Regex.Match(text, @"\{\{[A-Z_]+\}\}");
                if (left.Success)
                    Paths.Log(L("  [警告] 模板 {0} 里还有没替换的占位符 {1}（模板比脚手架新？）", rel, left.Value));
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.WriteAllText(dst, text, new UTF8Encoding(false));   // 不带 BOM：与其它 mod 的 gml 一致
                wrote.Add(dst);
                Paths.Log(L("  [写] {0}", Path.GetRelativePath(gameRoot, dst)));
            }
        }

        // ---------- 6) 时间线章节的基底 data.win（覆盖层模式不需要）----------
        if (mode != "overlay")
        {
            var dataDir = Path.Combine(modDir, "data");
            var dataWin = Path.Combine(dataDir, "data.win");
            if (File.Exists(dataWin))
            {
                Paths.Log(L("  [跳过] 已有基底数据：{0}（想换就手动删掉它再跑一次）", Path.GetRelativePath(gameRoot, dataWin)));
            }
            else
            {
                Directory.CreateDirectory(dataDir);
                // fork 自带 data/<旧名字>/data.win 时先摊平到 data/data.win
                var own = Directory.GetFiles(dataDir, "data.win", SearchOption.AllDirectories).FirstOrDefault();
                var origin = "";
                if (own != null) { File.Copy(own, dataWin, true); origin = "fork 自带"; }
                else
                {
                    var srcWin = Paths.BackupDataWin(gameRoot, baseName);
                    origin = "官方原版备份";
                    if (!File.Exists(srcWin))
                    {
                        srcWin = Paths.ChapterDataWin(gameRoot, baseName);
                        origin = "当前章节（可能已含其它 mod 的内容）";
                        if (File.Exists(srcWin))
                            Paths.Log(L("  [警告] 没找到 {0} 的官方原版备份，退而用当前 data.win", baseName));
                    }
                    if (!File.Exists(srcWin))
                    {
                        Paths.Log(L("  [错误] 找不到 {0} 的 data.win（备份目录和游戏目录都没有）", baseName));
                        Paths.Log(L("         先跑一次：ntl-builder.exe --deploy --chapter {0}   （它会自动备份原版）", baseName));
                        return 1;
                    }
                    File.Copy(srcWin, dataWin, false);
                }
                Paths.Log(L("  [写] {0}（{1}，{2:F1} MB）", Path.GetRelativePath(gameRoot, dataWin), origin, new FileInfo(dataWin).Length / 1048576.0));
                wrote.Add(dataWin);
            }
        }

        // ---------- 7) 房间地图（PNG + map.json）----------
        // fork 是「整份复制」：源章节自己的地图/资源已经在它的 files/ 里被一起复制过来了，
        // 脚手架再塞一张用不上的房间图只会让人以为 fork 出来的章节用的是这张图。
        if (template == "room" && mode != "overlay" && mode != "fork")
        {
            var mapDirs = new List<string> { Path.Combine(leafDir, "files", "Neutraled", "kristal-maps") };
            mapDirs.Add(Path.Combine(ntlRoot, "kristal-maps"));
            foreach (var dir in mapDirs)
            {
                Directory.CreateDirectory(dir);
                var cv = new PngWrite.Canvas(RoomW, RoomH);
                DrawRoom(cv);
                var png = Path.Combine(dir, mapId + ".png");
                PngWrite.Save(png, RoomW, RoomH, cv.Px);
                var js = Path.Combine(dir, mapId + ".map.json");
                File.WriteAllText(js, MapJson(mapId, name), new UTF8Encoding(false));
                Paths.Log(L("  [写] {0}（{1}x{2}）", Path.GetRelativePath(gameRoot, png), RoomW, RoomH));
                wrote.Add(png); wrote.Add(js);
            }
        }

        // ---------- 8) 下一步 ----------
        Paths.Log("");
        Paths.Log(L("===== 章节已生成 ====="));
        Paths.Log(L("  章节名   : {0}   （章节选择器里显示的就是它）", name));
        Paths.Log(L("  顺序号   : {0}     （章节选择器里的位置；改 mod.json 的 order 就能改）", theSlot));
        Paths.Log(L("  mod 目录 : {0}", Path.GetRelativePath(gameRoot, leafDir)));
        if (mode == "fork") Paths.Log(L("  fork 自  : {0}", Path.GetRelativePath(gameRoot, src!.Dir)));
        if (mode != "overlay") Paths.Log(L("  基底数据 : {0} ← 官方 {1}", Path.GetRelativePath(gameRoot, Path.Combine(modDir, "data", "data.win")), baseName));
        if (template == "room" && mode != "overlay" && mode != "fork") Paths.Log(L("  房间地图 : Neutraled/kristal-maps/{0}.png + .map.json", mapId));
        Paths.Log(L("  日志     : %LOCALAPPDATA%\\DRTL<顺序号>_<短码>\\Neutraled\\dr-api.log   ← 章节进程（= 本 mod 的存档目录）"));
        Paths.Log(L("             %LOCALAPPDATA%\\DELTARUNE\\Neutraled\\dr-api.log   ← 章节选择器/根阶段"));
        Paths.Log("");
        Paths.Log(L("下一步（一条一条复制执行）："));
        if (mode == "overlay")
        {
            Paths.Log(L("  1) ntl-builder.exe --deploy --chapter {0} --force     把 mod 叠加进官方第 {1} 章", baseName, baseNum));
            Paths.Log(L("  2) 启动 DELTARUNE（Steam），进第 {0} 章看效果", baseNum));
        }
        else
        {
            Paths.Log(L("  1) ntl-builder.exe --deploy --chapter root --force           生成章节产物（平行时间线会自动一起生成）"));
            Paths.Log(L("  2) 启动 DELTARUNE（Steam）→ 章节选择器里翻到你的章节"));
        }
        Paths.Log(L("  教程：Neutraled/docs/CHAPTER_DEV.md"));
        return 0;
    }

    /// <summary>--new-chapter help：列出可用模板与可 fork 的现成章节（不用翻源码）。</summary>
    private static int PrintScaffoldHelp(string gameRoot)
    {
        var ntlRoot = Paths.NeutraledRoot(gameRoot);
        Paths.Log(L("用法：ntl-builder.exe --new-chapter <章节名字> [选项]"));
        Paths.Log(L("      ntl-builder.exe --new-chapter help            看这份帮助"));
        Paths.Log("");
        Paths.Log(L("选项：--author <作者>  --mode independent|overlay|fork  --chapter chapterN"));
        Paths.Log(L("      --slot N  --fork <mod>[:作者[:chapterN]]  --template room|empty  --force"));
        Paths.Log("");
        Paths.Log(L("可用模板（") + Path.Combine(ntlRoot, "templates") + L("）："));
        var tplRoot = Path.Combine(ntlRoot, "templates");
        if (Directory.Exists(tplRoot))
            foreach (var d in Directory.GetDirectories(tplRoot))
                Paths.Log("   - " + Path.GetFileName(d).Replace("chapter-", ""));
        else
            Paths.Log(L("   （Neutraled/templates 不存在 —— 安装包不完整？）"));
        Paths.Log("");
        Paths.Log(L("可 fork 的现成章节（--fork 后面写 <mod目录名>[:作者[:chapterN]]）："));
        ListForkable(Paths.ModsRoot(gameRoot));
        return 0;
    }

    // ================= 参数处理 =================

    private static string? BadFileNameChar(string s)
    {
        foreach (var c in s)
            if (c < 32 || c == '\\' || c == '/' || c == ':' || c == '*' || c == '?' || c == '"' || c == '<' || c == '>' || c == '|')
                return "'" + c + "'";
        if (s == "." || s == "..") return "\".\" 或 \"..\"";
        return null;
    }

    /// <summary>"chapter3" → 3；没有数字就算 1（基底只可能取自官方 1..7 章）。</summary>
    private static int ChapterNumFrom(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 1;
        var m = Regex.Match(s, "([0-9]+)");
        if (!m.Success) return 1;
        return Math.Clamp(int.Parse(m.Groups[1].Value), 1, 7);
    }

    private static bool IsChapterFolderName(string leaf) =>
        leaf.Equals("root", StringComparison.OrdinalIgnoreCase) ||
        (leaf.StartsWith("chapter", StringComparison.OrdinalIgnoreCase) && leaf.Length > 7);

    /// <summary>把任意名字压成 ASCII 短名（只留 a-z0-9 与 -），用于 mod id / 脚本前缀 / 地图文件名 / 产物目录名。
    /// 纯中文名会压成空 → 用名字的稳定哈希兜底（必须**确定性**：产物目录名进了存档目录名，变了玩家存档就"消失"）。</summary>
    private static string Slugify(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name.ToLowerInvariant())
        {
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) sb.Append(c);
            else if (c == '-' || c == '_' || c == ' ') { if (sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-'); }
        }
        var s = sb.ToString().Trim('-');
        if (s.Length > 24) s = s.Substring(0, 24).Trim('-');
        if (s.Length == 0)
        {
            uint h = 2166136261u;
            foreach (var c in Encoding.UTF8.GetBytes(name)) { h ^= c; h *= 16777619u; }
            s = "c" + h.ToString("x8");
        }
        return s;
    }

    private static string JsonStr(string s) => JsonSerializer.Serialize(s, JsonOpts);

    /// <summary>mod.json 里 chapters 数组的元素文本。
    /// 独立章节用**对象形式**：可同时给中文显示名（name）和 ASCII 路径名（slug，决定产物目录）；
    /// 覆盖层用字符串形式 Chapter:N（无 ~ 无名字 → 只叠加官方第 N 章，不产生章节条目）。</summary>
    private static string ChapterDeclText(string mode, int baseNum, int slot, string name, string slug) =>
        mode == "overlay"
            ? JsonStr("Chapter:" + baseNum)
            : "{\"timeline\": true, \"order\": " + slot + ", \"name\": " + JsonStr(name) + ", \"slug\": " + JsonStr(slug) + "}";

    /// <summary>main.gml 最顶上的"防误注入"守卫。
    ///
    /// 为什么需要：独立章节 mod 的叶子目录名必须是 chapterN（否则 DeployTimelines 里
    /// Program.cs:2223 的"基底章节可用"检查会直接跳过整个章节），而 Mods.ScanMods 又用这个
    /// 目录名判断"这个 mod 属于哪一章" → 于是 --deploy --chapter chapterN / --deploy-all 时，
    /// 本 mod 的入口脚本会被注入到**官方第 N 章**里。官方的第四章不该变成我们的测试房间。
    /// 所以入口脚本第一件事就是确认自己跑在**本 mod 自己的产物目录**里（目录名含 ntl_timeline_）。</summary>
    private static string BuildGuard(string mode, string name, string leaf)
    {
        if (mode == "overlay")
            return "// 覆盖层模式：本 mod 就是要在官方章节里生效，不需要任何守卫。";

        return "// ⚠ 防误注入（独立章节都要有这一句）：本 mod 的目录名是 " + leaf + "，Neutraled 同时也把它\n"
             + "//   当成\"属于 " + leaf + " 的 mod\"，所以 --deploy --chapter " + leaf + " / --deploy-all 时\n"
             + "//   这个入口会被注入进**官方章节**。这里确认工作目录确实是本 mod 自己的章节产物\n"
             + "//   （产物目录名由 Neutraled 生成，一定含 ntl_timeline_），不是就直接退出、不接管画面。\n"
             + "if (string_pos(\"ntl_timeline_\", string_lower(string(working_directory))) <= 0)\n"
             + "{\n"
             + "    ntl_log(\"chapter\", \"[" + name + "] 当前不在本 mod 的章节产物里，跳过初始化：\" + string(working_directory));\n"
             + "    exit;\n"
             + "}";
    }

    /// <summary>main.gml 里 {{BOOT}} 那一段。占位符在这里就替换完（避免依赖字典替换顺序）。</summary>
    private static string BuildBoot(string mode, string name, string mapId, string prefix)
    {
        if (mode == "overlay")
            return "// 覆盖层模式：不接管画面（不调用 ntl_player_test），本 mod 只是叠加在官方章节上\n"
                 + "ntl_log(\"chapter\", \"[" + name + "] 覆盖层模式：已叠加到官方章节，可在这里改剧情 / 加对象\");";

        return "// 载入本 mod 自带的房间地图，并放一个可操控的玩家（Neutraled 会自动接管画面与每帧移动/交互）\n"
             + "var _ok = ntl_player_test(\"" + mapId + "\");\n"
             + "if (_ok == 1) ntl_log(\"chapter\", \"[" + name + "] 房间已载入：" + mapId + "（方向键/WASD 移动，E/Z 交互）\");\n"
             + "else ntl_log(\"chapter\", \"[" + name + "] [警告] 房间载入失败：" + mapId + " —— 检查 Neutraled/kristal-maps/" + mapId + ".png 与 .map.json 是否都部署了\");";
    }

    /// <summary>当前最大的章节 order + 1（至少 6）—— 默认把新章节排在最后，用户不用自己想 slot。
    /// excludeDir：本次要生成的 mod 目录（重新生成时要忽略它自己的旧声明，否则每跑一次 order 就 +1）。</summary>
    private static int SuggestSlot(string modsRoot, string? excludeDir = null)
    {
        var max = 5;
        var skip = string.IsNullOrEmpty(excludeDir) ? null : excludeDir!.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        try
        {
            foreach (var mj in Directory.GetFiles(modsRoot, "mod.json", SearchOption.AllDirectories))
            {
                if (skip != null && mj.StartsWith(skip, StringComparison.OrdinalIgnoreCase)) continue;
                JsonNode? node;
                try { node = JsonNode.Parse(File.ReadAllText(mj)); } catch { continue; }
                if (node?["chapters"] is not JsonArray arr) continue;
                foreach (var it in arr)
                {
                    if (it is JsonValue v && v.TryGetValue<string>(out var s))
                    {
                        var m = Regex.Match(s, "^~?Chapter:([0-9]+)");
                        if (m.Success) max = Math.Max(max, int.Parse(m.Groups[1].Value));
                    }
                    else if (it is JsonObject o && int.TryParse(o["order"]?.ToString(), out var ord))
                        max = Math.Max(max, ord);
                }
            }
        }
        catch { }
        return max + 1;
    }

    // ================= fork =================

    private static string ParentOf(string path) => Path.GetDirectoryName(path.TrimEnd('\\', '/')) ?? "";

    private static List<ModEntry> AllModsForFork(string modsRoot) =>
        Mods.ScanMods(modsRoot, "root", includeDisabled: true, allChapters: true);

    private static void ListForkable(string modsRoot)
    {
        List<ModEntry> all;
        try { all = AllModsForFork(modsRoot); }
        catch (Exception ex) { Paths.Log(L("  [错误] 扫描 mods 失败：") + ex.Message); return; }
        Paths.Log(L("         可 fork 的 mod 章节目录："));
        var n = 0;
        foreach (var m in all.OrderBy(x => ParentOf(x.Dir), StringComparer.OrdinalIgnoreCase)
                             .ThenBy(x => x.Dir, StringComparer.OrdinalIgnoreCase))
        {
            if (n++ >= 40) { Paths.Log(L("           ……（还有更多，已省略）")); break; }
            var modName = Path.GetFileName(ParentOf(ParentOf(m.Dir)));
            Paths.Log("           - " + modName + ":" + (m.Author ?? "") + ":" + Path.GetFileName(m.Dir)
                    + "   (id=" + m.Id + (string.IsNullOrEmpty(m.Chapter) ? "" : ", chapter=" + m.Chapter) + ")");
        }
    }

    /// <summary>--fork 的规格：&lt;mod目录名|mod id|name&gt;[:作者[:chapterN]]，任一段可省。</summary>
    private static ModEntry? ResolveFork(string modsRoot, string spec)
    {
        var parts = spec.Split(':');
        var wantMod = parts[0].Trim();
        var wantAuthor = parts.Length > 1 ? parts[1].Trim() : "";
        var wantLeaf = parts.Length > 2 ? parts[2].Trim() : "";
        List<ModEntry> all;
        try { all = AllModsForFork(modsRoot); }
        catch (Exception ex) { Paths.Log(L("  [错误] 扫描 mods 失败：") + ex.Message); return null; }

        var cands = all.Where(m =>
                (string.Equals(m.Id, wantMod, StringComparison.OrdinalIgnoreCase)
                 || string.Equals(Path.GetFileName(ParentOf(ParentOf(m.Dir))), wantMod, StringComparison.OrdinalIgnoreCase)
                 || string.Equals(m.Name, wantMod, StringComparison.OrdinalIgnoreCase))
                && (wantAuthor.Length == 0 || string.Equals(m.Author, wantAuthor, StringComparison.OrdinalIgnoreCase))
                && (wantLeaf.Length == 0 || string.Equals(Path.GetFileName(m.Dir), wantLeaf, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(m => m.Dir, StringComparer.OrdinalIgnoreCase).ToList();

        if (cands.Count == 0)
        {
            Paths.Log(L("  [错误] 找不到要 fork 的 mod：「{0}」", spec));
            Paths.Log(L("         用法：--fork <mod目录名|mod id>[:作者[:chapterN]]"));
            ListForkable(modsRoot);
            return null;
        }
        if (cands.Count > 1)
        {
            Paths.Log(L("  [提示] 「{0}」匹配到 {1} 个，取第一个：{2}", spec, cands.Count, cands[0].Dir));
            foreach (var c in cands.Skip(1)) Paths.Log(L("         其它候选：") + c.Dir);
        }
        return cands[0];
    }

    private static void CopyTree(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            var to = Path.Combine(dst, Path.GetRelativePath(src, f));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(f, to, true);
        }
    }

    /// <summary>fork 后把 mod.json 改成"新章节"：换 id/name/author、强制 enabled、chapters 换成新时间线条目。</summary>
    private static void RewriteForkModJson(string gameRoot, string mjPath, string name, string author, string modId,
                                           int slot, string slug, ModEntry src)
    {
        JsonObject obj;
        if (File.Exists(mjPath))
        {
            try { obj = JsonNode.Parse(File.ReadAllText(mjPath)) as JsonObject ?? new JsonObject(); }
            catch (Exception ex) { Paths.Log(L("  [警告] 源 mod.json 解析失败（") + ex.Message + L("），按空对象重建")); obj = new JsonObject(); }
        }
        else obj = new JsonObject();

        Paths.Log(L("  [fork] 原 chapters 声明 ") + (obj["chapters"]?.ToJsonString() ?? L("(无)"))
                + L(" → 替换成新的时间线条目（order ") + slot + L("）"));
        if (obj["kristal_external"] != null)
            Paths.Log(L("  [警告] 源 mod 是外部引擎章节（kristal_external），fork 后成了普通时间线章节；要保留外部启动请手动改回"));

        obj["id"] = modId;
        obj["name"] = name;
        obj["author"] = author;
        obj["enabled"] = true;
        obj["version"] = (obj["version"]?.ToString() ?? "1.0.0").Trim('"');
        obj["description"] = "由 " + (src.Name ?? "?") + " fork（ntl-builder --new-chapter --mode fork）";
        obj["chapters"] = new JsonArray(new JsonObject
        {
            ["timeline"] = true,
            ["order"] = slot,
            ["name"] = name,
            ["slug"] = slug
        });
        File.WriteAllText(mjPath, obj.ToJsonString(JsonOpts) + "\n", new UTF8Encoding(false));
        Paths.Log(L("  [写] ") + Path.GetRelativePath(gameRoot, mjPath));
    }

    // ================= 房间底图 =================

    /// <summary>画一张 640x480 的房间底图（几何必须与 MapJson 里的 collision / 对象坐标一致）。</summary>
    private static void DrawRoom(PngWrite.Canvas cv)
    {
        for (var ty = 0; ty < RoomRows; ty++)
            for (var tx = 0; tx < RoomCols; tx++)
            {
                var light = ((tx + ty) & 1) == 0;
                cv.Fill(tx * Tile, ty * Tile, Tile, Tile, light ? 52 : 44, light ? 52 : 44, light ? 64 : 54);
            }

        void Wall(int x, int y, int w, int h)
        {
            cv.Fill(x, y, w, h, 104, 108, 128);
            cv.Fill(x, y, w, 3, 150, 156, 182);
            cv.Frame(x, y, w, h, 58, 60, 74, 2);
        }

        Wall(0, 0, RoomW, Tile);
        Wall(0, RoomH - Tile, RoomW, Tile);
        Wall(0, 0, Tile, RoomH);
        Wall(600, 0, 40, ExitTop);
        Wall(600, ExitBottom, 40, RoomH - ExitBottom);

        // 出口：金色门槛 + 向右箭头（collision 里这段是空的，真的能走出去）
        cv.Fill(600, ExitTop, 40, ExitBottom - ExitTop, 92, 74, 24);
        cv.Frame(600, ExitTop, 40, ExitBottom - ExitTop, 236, 196, 72, 3);
        ArrowRight(cv, 606, 220, 11);
        ArrowRight(cv, 622, 220, 11);

        // 告示牌（对应地图对象 Sign —— 游戏里靠近会显示 [E] Sign）
        cv.Fill(300, 180, 40, 40, 226, 220, 196);
        cv.Frame(300, 180, 40, 40, 120, 100, 60, 3);
        cv.Fill(316, 190, 8, 14, 96, 76, 44);
        cv.Fill(316, 210, 8, 6, 96, 76, 44);

        // 出生点标记（对应 markers 里的 Spawn）
        cv.Frame(100, 100, 20, 20, 120, 220, 140, 2);
        cv.Fill(108, 108, 4, 4, 120, 220, 140);

        // 地面上的指示箭头
        cv.Fill(276, 236, 44, 8, 132, 136, 160);
        ArrowRight(cv, 324, 240, 10);
    }

    private static void ArrowRight(PngWrite.Canvas cv, int x, int y, int size)
    {
        for (var i = 0; i < size; i++)
            cv.Fill(x + i, y - (size - i), 1, 2 * (size - i), 200, 204, 226);
    }

    private static JsonObject Coll(int x, int y, int w, int h) => new()
    {
        ["x"] = x, ["y"] = y, ["w"] = w, ["h"] = h
    };

    /// <summary>地图 JSON。键名必须与 api/ntl_map_load.gml 完全一致
    /// （width/height/tileWidth/tileHeight/collision/objectGroups/properties 一个都不能少，
    ///  否则 json_parse 之后 variable_struct_get 直接报错）。</summary>
    private static string MapJson(string mapId, string name)
    {
        JsonObject Obj(int x, int y, int w, int h, string nm, string type) => new()
        {
            ["name"] = nm,
            ["type"] = type,
            ["x"] = x, ["y"] = y, ["w"] = w, ["h"] = h,
            ["point"] = false,
            ["props"] = new JsonObject()
        };

        var root = new JsonObject
        {
            ["width"] = RoomW,
            ["height"] = RoomH,
            ["tileWidth"] = Tile,
            ["tileHeight"] = Tile,
            ["image"] = mapId,
            ["imageW"] = RoomW,
            ["imageH"] = RoomH,
            ["properties"] = new JsonObject
            {
                ["displayname"] = name,
                ["generator"] = "ntl-builder --new-chapter"
            },
            ["objectGroups"] = new JsonArray(
                new JsonObject
                {
                    ["name"] = "objects",
                    ["objects"] = new JsonArray(
                        Obj(300, 180, 40, 40, "Sign", "sign"),
                        Obj(600, ExitTop, 40, ExitBottom - ExitTop, "Exit", "exit"))
                },
                new JsonObject
                {
                    ["name"] = "markers",
                    ["objects"] = new JsonArray(Obj(100, 100, 20, 20, "Spawn", "spawn"))
                },
                new JsonObject { ["name"] = "collision", ["objects"] = new JsonArray() }),
            ["collision"] = new JsonArray(
                Coll(0, 0, RoomW, Tile),
                Coll(0, RoomH - Tile, RoomW, Tile),
                Coll(0, 0, Tile, RoomH),
                Coll(600, 0, 40, ExitTop),
                Coll(600, ExitBottom, 40, RoomH - ExitBottom))
        };
        return root.ToJsonString(JsonOpts) + "\n";
    }
}
