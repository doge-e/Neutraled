using System.Text.Json;
using static Neutraled.Builder.Lang;
using System.Text.Json.Nodes;

namespace Neutraled.Builder;

/// <summary>新功能（配置档 / 快照 / 恢复点 / 黑名单 / GameBanana / 下载队列 / 插件 / 主题 / 语言包 / Web UI）的统一 CLI 入口。
/// 为什么不写进 Program.cs 的大 switch：这些开关需要「子命令 + 若干位置参数 + 可选开关」，
/// 在 Program.cs 里摊开会多出 40 多个 case 与 30 多个局部变量；这里用独立扫描器（主开关后的非 -- 记号即位置参数）保持简单。
/// 接线点：Program.Main 在 Lang.Init 之后、老分派之前调用 <see cref="Detect"/>，命中即 <see cref="Run"/> 并直接返回其返回码。</summary>
public static class CliFeatures
{
    /// <summary>本类负责的全部主开关（Detect 用；值为内部命令名）。</summary>
    private static readonly Dictionary<string, string> Switches = new(StringComparer.Ordinal)
    {
        ["--profile-list"] = "profile-list",
        ["--profile-new"] = "profile-new",
        ["--profile-use"] = "profile-use",
        ["--profile-show"] = "profile-show",
        ["--profile-copy"] = "profile-copy",
        ["--profile-rename"] = "profile-rename",
        ["--profile-delete"] = "profile-delete",
        ["--profile-export"] = "profile-export",
        ["--profile-import"] = "profile-import",
        ["--snapshot-list"] = "snapshot-list",
        ["--snapshot-create"] = "snapshot-create",
        ["--snapshot-use"] = "snapshot-use",
        ["--snapshot-import"] = "snapshot-import",
        ["--snapshot-delete"] = "snapshot-delete",
        ["--snapshot-auto"] = "snapshot-auto",
        ["--restore-list"] = "restore-list",
        ["--restore-create"] = "restore-create",
        ["--restore-apply"] = "restore-apply",
        ["--restore-export"] = "restore-export",
        ["--restore-import"] = "restore-import",
        ["--restore-delete"] = "restore-delete",
        ["--block-list"] = "block-list",
        ["--block-add"] = "block-add",
        ["--block-remove"] = "block-remove",
        ["--gb-search"] = "gb-search",
        ["--gb-files"] = "gb-files",
        ["--gb-install"] = "gb-install",
        ["--queue-list"] = "queue-list",
        ["--queue-run"] = "queue-run",
        ["--queue-add"] = "queue-add",
        ["--queue-remove"] = "queue-remove",
        ["--plugin-list"] = "plugin-list",
        ["--plugin-enable"] = "plugin-enable",
        ["--plugin-disable"] = "plugin-disable",
        ["--plugin-info"] = "plugin-info",
        ["--plugin-install"] = "plugin-install",
        ["--plugin-remove"] = "plugin-remove",
        ["--plugin-hooks"] = "plugin-hooks",
        ["--theme-list"] = "theme-list",
        ["--theme-use"] = "theme-use",
        ["--theme-show"] = "theme-show",
        ["--theme-import"] = "theme-import",
        ["--lang-list"] = "lang-list",
        ["--lang-use"] = "lang-use",
        ["--lang-coverage"] = "lang-coverage",
        ["--lang-template"] = "lang-template",
        ["--web"] = "web",
        ["--platform-info"] = "platform-info",
    };

    /// <summary>argv 里有没有本类负责的开关；返回内部命令名或 null。</summary>
    public static string? Detect(string[] args)
    {
        foreach (var a in args)
            if (Switches.TryGetValue(a, out var cmd)) return cmd;
        return null;
    }

    /// <summary>主开关之后、下一个 -- 之前的位置参数。</summary>
    private static List<string> Tail(string[] a, string sw)
    {
        var r = new List<string>();
        int i = Array.IndexOf(a, sw);
        if (i < 0) return r;
        for (int j = i + 1; j < a.Length; j++)
        {
            if (a[j].StartsWith("--", StringComparison.Ordinal)) break;
            r.Add(a[j]);
        }
        return r;
    }

    /// <summary>取具名可选开关的值：--name X / --out X / --port N …（缺失返回 null）。</summary>
    private static string? Opt(string[] a, string name)
    {
        for (int i = 0; i + 1 < a.Length; i++)
            if (a[i] == name) return a[i + 1];
        return null;
    }

    private static bool Flag(string[] a, string name) => Array.IndexOf(a, name) >= 0;

    private static int OptInt(string[] a, string name, int def)
        => int.TryParse(Opt(a, name), out var v) ? v : def;

    /// <summary>缺参数时的统一提示（返回 2 = 用法错误）。</summary>
    private static int Usage(string usage)
    {
        Console.Error.WriteLine(L("用法: {0}", usage));
        return 2;
    }

    /// <summary>执行一条新功能命令；返回进程退出码。</summary>
    public static int Run(string gameRoot, string cmd, string[] args)
    {
        Console.WriteLine(L("游戏根: {0}", gameRoot));
        try
        {
            // 外部语言包要在任何输出之前生效（config.json 的 lang 若为 ja/ru…，此处灌表）
            LangPacks.LoadAllExternal(gameRoot);
            Lang.SetCode(LangPackRequested(gameRoot, args));

            switch (cmd)
            {
                // ---- 配置档 ----
                case "profile-list": Profiles.PrintList(gameRoot); return 0;
                case "profile-show":
                {
                    var showId = Tail(args, "--profile-show").FirstOrDefault();
                    Profiles.PrintShow(gameRoot, showId);
                    // 指定的档不存在 ⇒ 退出码 2（省略 id 时看"活动档"，没有活动档也算失败）
                    // ★ 必须先把 id 解析成具体值再 Load：Load(null) 会打印一句空的「非法 id：」
                    var shownId = string.IsNullOrWhiteSpace(showId) ? Profiles.ActiveId(gameRoot) : showId.Trim();
                    return Profiles.Load(gameRoot, shownId) != null ? 0 : 2;
                }
                case "profile-new":
                {
                    var t = Tail(args, "--profile-new");
                    if (t.Count < 1) return Usage("--profile-new <id> [--name 显示名] [--from 已有档id]");
                    var dup = Profiles.Load(gameRoot, t[0]) != null;   // 同名已存在：Profiles.Create 不会覆盖，原样返回旧档
                    var p = Profiles.Create(gameRoot, t[0], Opt(args, "--name"), Opt(args, "--from"), Opt(args, "--desc"));
                    // 成功行由 Profiles.Create 自己打印（"[配置档] 已创建 …"），这里不再重复；
                    // 退出码：2 = 拒绝（同名已存在 / 非法 id ⇒ 实际什么都没创建），0 = 真的创建了。
                    if (dup || !PathGuard.IsSafeName(p.Id)) return 2;
                    return 0;
                }
                case "profile-use":
                {
                    var t = Tail(args, "--profile-use");
                    if (t.Count < 1) return Usage("--profile-use <id>");
                    var n = Profiles.Apply(gameRoot, t[0]);
                    if (n < 0) return 2;   // -1 = 档不存在（原因已由 Profiles 打印）
                    Console.WriteLine(L("[配置档] 已应用 {0}（改动 {1} 个 mod）", t[0], n));
                    return 0;
                }
                case "profile-copy":
                {
                    var t = Tail(args, "--profile-copy");
                    if (t.Count < 2) return Usage("--profile-copy <源id> <目标id> [--name 显示名]");
                    return Profiles.Copy(gameRoot, t[0], t[1], Opt(args, "--name")) ? 0 : 1;
                }
                case "profile-rename":
                {
                    var t = Tail(args, "--profile-rename");
                    if (t.Count < 2) return Usage("--profile-rename <旧id> <新id>");
                    return Profiles.Rename(gameRoot, t[0], t[1]) ? 0 : 1;
                }
                case "profile-delete":
                {
                    var t = Tail(args, "--profile-delete");
                    if (t.Count < 1) return Usage("--profile-delete <id> [--force]");
                    // 拒绝（没有 --force 且不是空档 / 档不存在）= 用法层面的拒绝，按 SPEC 用 2
                    return Profiles.Delete(gameRoot, t[0], Flag(args, "--force")) ? 0 : 2;
                }
                case "profile-export":
                {
                    var t = Tail(args, "--profile-export");
                    if (t.Count < 1) return Usage("--profile-export <id> [--out 文件]");
                    var text = Profiles.Export(gameRoot, t[0]);
                    if (text.Length == 0) return 2;   // 找不到该档
                    var outPath = Opt(args, "--out") ?? Path.Combine(gameRoot, "Neutraled", "profiles", t[0] + ".export.json");
                    Paths.SafeWrite(outPath, text);
                    Console.WriteLine(L("[配置档] 已导出 {0} → {1}", t[0], outPath));
                    return 0;
                }
                case "profile-import":
                {
                    var t = Tail(args, "--profile-import");
                    if (t.Count < 1) return Usage("--profile-import <文件> [--force]");
                    if (!File.Exists(t[0])) { Paths.Log(L("[配置档] 找不到配置档 {0}", t[0])); return 2; }
                    var n = Profiles.Import(gameRoot, File.ReadAllText(t[0]), Flag(args, "--force"));
                    if (n <= 0) return 2;   // 0 = 档 id 冲突且没给 --force
                    Console.WriteLine(L("[配置档] 已导入 {0} 条", n));
                    return 0;
                }

                // ---- 快照 ----
                case "snapshot-list": Snapshots.PrintList(gameRoot, Tail(args, "--snapshot-list").FirstOrDefault()); return 0;
                case "snapshot-create":
                {
                    var t = Tail(args, "--snapshot-create");
                    if (t.Count < 1) return Usage("--snapshot-create <modId> [--version 版本] [--note 备注]");
                    Snapshots.Create(gameRoot, t[0], Opt(args, "--version"), "live", Opt(args, "--note"));
                    // 成功行由 Snapshots 自己打印（"[快照] 已保存 {0} @ {1}: N 个文件"），这里不再重复
                    return 0;
                }
                case "snapshot-use":
                {
                    var t = Tail(args, "--snapshot-use");
                    if (t.Count < 2) return Usage("--snapshot-use <modId> <版本> [--force]");
                    var n = Snapshots.Use(gameRoot, t[0], t[1], Flag(args, "--force"));
                    if (n < 0) return 2;   // -1 = 快照不存在
                    Console.WriteLine(L("[快照] 已切回 {0} @ {1}（{2} 个文件）", t[0], t[1], n));
                    return 0;
                }
                case "snapshot-import":
                {
                    var t = Tail(args, "--snapshot-import");
                    if (t.Count < 2) return Usage("--snapshot-import <modId> <目录或zip> [--version 版本]");
                    var s = Snapshots.Import(gameRoot, t[0], t[1], Opt(args, "--version"));
                    Console.WriteLine(L("[快照] 已导入 {0} @ {1}", t[0], s.Version));
                    return 0;
                }
                case "snapshot-delete":
                {
                    var t = Tail(args, "--snapshot-delete");
                    if (t.Count < 2) return Usage("--snapshot-delete <modId> <版本> [--force]");
                    return Snapshots.Delete(gameRoot, t[0], t[1], Flag(args, "--force")) ? 0 : 2;
                }
                case "snapshot-auto":
                {
                    var mods = Mods.ScanMods(Paths.ModsRoot(gameRoot), "", true, true);
                    var n = Snapshots.AutoSnapshotAll(gameRoot, mods);
                    Console.WriteLine(L("[快照] 已为 {0} 个 mod 建立保底快照", n));
                    return 0;
                }

                // ---- 恢复点 ----
                case "restore-list": RestorePoints.PrintList(gameRoot); return 0;
                case "restore-create":
                {
                    var p = RestorePoints.Create(gameRoot, Opt(args, "--name"), Opt(args, "--from") ?? "live", Opt(args, "--profile"),
                                               !Flag(args, "--no-backup"));   // --no-backup = 只存清单不复制 data.win（省盘）
                    Console.WriteLine(L("[恢复点] 已创建 {0}（{1}）", p.Id, p.Name));
                    return 0;
                }
                case "restore-apply":
                {
                    var t = Tail(args, "--restore-apply");
                    if (t.Count < 1) return Usage("--restore-apply <id> [--force]");
                    var n = RestorePoints.Apply(gameRoot, t[0], Flag(args, "--force"));
                    // 1 = id 非法 / 2 = 点不存在或没有数据副本 / 3 = DELTARUNE 正在运行被拒绝 —— 都是拒绝码，原样透出
                    if (n is 1 or 2 or 3) return n;
                    Console.WriteLine(L("[恢复点] 已恢复 {0}（{1} 个文件）", t[0], n));
                    return 0;
                }
                case "restore-export":
                {
                    var t = Tail(args, "--restore-export");
                    if (t.Count < 2) return Usage("--restore-export <id> <输出.ntlrestore>");
                    var f = RestorePoints.Export(gameRoot, t[0], t[1]);
                    Console.WriteLine(L("[恢复点] 已导出 → {0}", f));
                    return 0;
                }
                case "restore-import":
                {
                    var t = Tail(args, "--restore-import");
                    if (t.Count < 1) return Usage("--restore-import <文件.ntlrestore> [--force]");
                    var p = RestorePoints.Import(gameRoot, t[0], Flag(args, "--force"));
                    if (string.IsNullOrEmpty(p.Id)) return 2;   // 同名已存在且没给 --force ⇒ 什么都没导入；失败时返回 Id 为空的 Point，不是 null
                    Console.WriteLine(L("[恢复点] 已导入 {0}", p.Id));
                    return 0;
                }
                case "restore-delete":
                {
                    var t = Tail(args, "--restore-delete");
                    if (t.Count < 1) return Usage("--restore-delete <id> [--force]");
                    return RestorePoints.Delete(gameRoot, t[0], Flag(args, "--force")) ? 0 : 2;
                }

                // ---- 黑名单 ----
                case "block-list": Blacklist.PrintList(gameRoot); return 0;
                case "block-add":
                {
                    var t = Tail(args, "--block-add");
                    if (t.Count < 1) return Usage("--block-add <值> [--kind id|name|category] [--note 备注]");
                    var kind = Opt(args, "--kind") ?? "id";
                    Blacklist.Add(gameRoot, t[0], kind, Opt(args, "--note"));
                    // Blacklist.Add 返回 void 且失败只打印原因 ⇒ 加完必须回查，否则非法值也报成功
                    var added = Blacklist.List(gameRoot).Any(e =>
                        string.Equals(e.Value, t[0].Trim(), StringComparison.OrdinalIgnoreCase) &&
                        (string.IsNullOrEmpty(kind) || string.Equals(e.Kind, kind, StringComparison.OrdinalIgnoreCase)));
                    if (!added) return 2;
                    Console.WriteLine(L("[黑名单] 已加入 {0}", t[0]));
                    return 0;
                }
                case "block-remove":
                {
                    var t = Tail(args, "--block-remove");
                    if (t.Count < 1) return Usage("--block-remove <值> [--kind id|name|category]");
                    return Blacklist.Remove(gameRoot, t[0], Opt(args, "--kind") ?? "id") ? 0 : 2;
                }

                // ---- GameBanana / 下载队列 ----
                case "gb-search":
                {
                    var t = Tail(args, "--gb-search");
                    if (t.Count < 1) return Usage("--gb-search <关键词> [--per-page N]");
                    return GbBrowse.Search(gameRoot, string.Join(' ', t), OptInt(args, "--per-page", 15)).GetAwaiter().GetResult();
                }
                case "gb-files":
                {
                    var t = Tail(args, "--gb-files");
                    if (t.Count < 1 || !int.TryParse(t[0], out var id)) return Usage("--gb-files <modId> [--gb-type Mod]");
                    return GbBrowse.Files(gameRoot, id, Opt(args, "--gb-type") ?? "Mod").GetAwaiter().GetResult();
                }
                case "gb-install":
                {
                    var t = Tail(args, "--gb-install");
                    if (t.Count < 1 || !int.TryParse(t[0], out var id)) return Usage("--gb-install <modId> [--file N] [--chapter 章节] [--force]");
                    // 下载完成后要真的导入 mod：钩子原先只在 --web 启动时注入 ⇒ CLI 永远停在「已下载未安装」
                    GbBrowse.ImportHook ??= Program.ImportModForHook;
                    return GbBrowse.Install(gameRoot, id, Opt(args, "--gb-type"), Opt(args, "--file") is string fs && int.TryParse(fs, out var fid) ? fid : null,
                                            Opt(args, "--chapter"), Flag(args, "--force")).GetAwaiter().GetResult();
                }
                case "queue-list": DownloadQueue.PrintList(gameRoot); return 0;
                case "queue-add":
                {
                    var t = Tail(args, "--queue-add");
                    if (t.Count < 1 || !int.TryParse(t[0], out var gid)) return Usage("--queue-add <modId> [--name 名] [--file N] [--url 地址] [--size 字节] [--gb-type Mod]");
                    var it = DownloadQueue.Enqueue(gameRoot, gid, Opt(args, "--name") ?? ("GameBanana #" + gid),
                        Opt(args, "--file") is string qf && int.TryParse(qf, out var qid) ? qid : null,
                        Opt(args, "--url"),
                        OptInt(args, "--size", 0),
                        Opt(args, "--gb-type") ?? "Mod");
                    if (it == null || it.Id.Length == 0) return 3;
                    Console.WriteLine(L("[队列] 已入队 {0}", it.Id));
                    return 0;
                }
                case "queue-remove":
                {
                    var t = Tail(args, "--queue-remove");
                    if (t.Count < 1) return Usage("--queue-remove <条目id> [--delete-file]");
                    return DownloadQueue.Remove(gameRoot, t[0], Flag(args, "--delete-file")) ? 0 : 3;
                }
                case "queue-run":
                {
                    // 与 --gb-install 同样的理由：不注入钩子的话队列跑完也不会安装
                    GbBrowse.ImportHook ??= Program.ImportModForHook;
                    // DownloadQueue.Run 返回「失败条数」而不是 0/1
                    var failed = DownloadQueue.Run(gameRoot, !Flag(args, "--no-install"), Flag(args, "--delete-after"));
                    return failed > 0 ? 1 : 0;
                }

                // ---- 插件 ----
                case "plugin-list": Plugins.PrintList(gameRoot); return 0;
                case "plugin-enable":
                {
                    var t = Tail(args, "--plugin-enable");
                    if (t.Count < 1) return Usage("--plugin-enable <插件id>");
                    return Plugins.Enable(gameRoot, t[0]);
                }
                case "plugin-disable":
                {
                    var t = Tail(args, "--plugin-disable");
                    if (t.Count < 1) return Usage("--plugin-disable <插件id>");
                    return Plugins.Disable(gameRoot, t[0]);
                }
                case "plugin-info":
                {
                    var t = Tail(args, "--plugin-info");
                    if (t.Count < 1) return Usage("--plugin-info <插件id>");
                    return Plugins.Info(gameRoot, t[0]);
                }
                case "plugin-install":
                {
                    var t = Tail(args, "--plugin-install");
                    if (t.Count < 1) return Usage("--plugin-install <目录或.ntlplugin> [--force]");
                    return Plugins.Install(gameRoot, t[0], Flag(args, "--force"));
                }
                case "plugin-remove":
                {
                    var t = Tail(args, "--plugin-remove");
                    if (t.Count < 1) return Usage("--plugin-remove <插件id> [--force]");
                    return Plugins.Remove(gameRoot, t[0], Flag(args, "--force")) ? 0 : 2;
                }
                case "plugin-hooks":
                {
                    PluginHost.Load(gameRoot);
                    Console.WriteLine(L("[插件] 已加载 {0} 个插件的钩子", PluginHost.HookLog.Count));
                    foreach (var l in PluginHost.HookLog) Console.WriteLine("  " + l);
                    PluginHost.Unload();
                    return 0;
                }

                // ---- 主题 ----
                case "theme-list": Themes.PrintList(gameRoot); return 0;
                case "theme-use":
                {
                    var t = Tail(args, "--theme-use");
                    if (t.Count < 1) return Usage("--theme-use <主题id>");
                    // Themes.Use 内部已经写 config.json 并调用 ApplyToGameConsole 生成 console-theme.json，
                    // 这里不能再调一次（会重复写盘 + 重复打印）。
                    var rc = Themes.Use(gameRoot, t[0]);
                    if (rc == 0) Console.WriteLine(L("[主题] 已应用 {0}", t[0]));
                    return rc;
                }
                case "theme-show":
                {
                    var t = Tail(args, "--theme-show");
                    if (t.Count < 1) return Usage("--theme-show <主题id>");
                    Console.WriteLine(Themes.Export(gameRoot, t[0]));
                    return 0;
                }
                case "theme-import":
                {
                    var t = Tail(args, "--theme-import");
                    if (t.Count < 1) return Usage("--theme-import <主题.json> [--force]");
                    return Themes.Import(gameRoot, File.ReadAllText(t[0]), Flag(args, "--force"));
                }

                // ---- 语言包 ----
                case "lang-list": LangPacks.PrintList(gameRoot); return 0;
                case "lang-use":
                {
                    var t = Tail(args, "--lang-use");
                    if (t.Count < 1) return Usage("--lang-use <语言码 zh|en|ja|ko|ru|es|de|fr|zh-TW>");
                    var code = t[0];
                    LangPacks.LoadExternal(gameRoot, code);
                    Lang.SetCode(code);
                    ConfigFile.SetMany(gameRoot, new Dictionary<string, JsonNode?> { ["lang"] = JsonValue.Create(code) });
                    Console.WriteLine(L("[语言] 已切换为 {0}（已写入 config.json）", code));
                    return 0;
                }
                case "lang-coverage":
                {
                    // 退出码只表达「齐了没有」：0 = 全部语言 100%（C# 侧与游戏内两侧都齐），1 = 有语言未达标；明细都在输出里
                    var bad = LangPacks.Coverage(gameRoot, Tail(args, "--lang-coverage").FirstOrDefault());
                    return bad == 0 ? 0 : 1;
                }
                case "lang-template":
                {
                    var t = Tail(args, "--lang-template");
                    if (t.Count < 1) return Usage("--lang-template <语言码> [--out 文件]");
                    var outPath = Opt(args, "--out") ?? Path.Combine(Paths.LangRoot(gameRoot), "template_" + t[0] + ".json");
                    LangPacks.ExportTemplate(gameRoot, t[0], outPath);
                    Console.WriteLine(L("[语言] 模板已导出 → {0}", outPath));
                    return 0;
                }

                // ---- Web UI ----
                case "web":
                {
                    // 钩子（before/after_import、before/after_deploy）由 Import / Deploy 自身触发，这里只做转发，避免重复触发
                    GbBrowse.ImportHook = Program.ImportModForHook;
                    WebUi.DeployHook = Program.DeployHookForWeb;
                    return WebUi.Start(gameRoot, OptInt(args, "--port", 7931), !Flag(args, "--no-open"), Flag(args, "--auto-stop"));
                }

                case "platform-info":
                    Console.WriteLine(Platform.Describe(gameRoot));
                    return 0;
            }
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(L("[错误] {0}", ex.Message));
            return 1;
        }
    }

    /// <summary>--lang 之外，config.json 的 lang 决定外部语言包（此函数只读配置，不改写 config.json）。</summary>
    private static string LangPackRequested(string gameRoot, string[] args)
    {
        var explicitCode = Opt(args, "--lang");
        if (!string.IsNullOrWhiteSpace(explicitCode)) return explicitCode!;
        return ConfigFile.GetString(gameRoot, "lang") ?? "zh";
    }
}
