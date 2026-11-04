using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>Neutraled/config.json 的单一事实源（C4 P1-8）。
///
/// 背景（A3 审计）：config.json 的键分散在 GUI 增强对话框、builder 各模块与 GML 加载器里，
/// 没有任何一处能回答「这个键是什么类型、谁负责写、默认值多少、是不是已经废弃」。
/// 于是历史键（skip_legend / skip_logo / auto_skip_intro / auto_skip_selector）一直没人敢删，
/// GUI 还在写 auto_chapter_id=""、auto_skip_delay=90 这类它自己都不读的键。
///
/// 本类只做四件事：
///   1. 声明**全部**键（类型 / 默认值 / owner / 中英说明 / 是否废弃 / 替代键）；
///   2. 把废弃键映射成规范键（迁移只读旧键、写规范键；旧键**保留**，让还没重新部署的旧产物继续能读）；
///   3. 提供 --config-keys / --config-get / --config-set / --config-migrate 的只读/读改写实现；
///   4. 语言的统一解析顺序（--lang &gt; NTL_LANG &gt; config.json.lang &gt; zh）与 gui_lang.txt 一次性迁移。
///
/// 写入一律经 ConfigFile（读-改-写、未知键与 _comment 保留、无 BOM、镜像存档区）；本类不自己写盘。
/// 运行时侧见 api/ntl_config_load.gml 的「新键展开成旧键」兼容块。</summary>
public static class ConfigSchema
{
    public enum Kind { Bool, Int, String }

    public const int SchemaVersion = 2;

    /// <summary>一个配置键的元数据。Owner 取值：all / gui / cli / game。</summary>
    public sealed record Entry(
        string Name, Kind Kind, string Default, string Owner,
        string Zh, string En, bool Deprecated = false, string Replacement = "",
        string NoteZh = "", string NoteEn = "");

    /// <summary>按当前界面语言取键说明（--config-keys 的文本输出；JSON 里 zh/en 两列都给）。</summary>
    static string Desc(Entry e) => Lang.IsEn ? e.En : e.Zh;

    /// <summary>规范键在前、废弃键在后（--config-keys 的输出顺序）。</summary>
    public static readonly Entry[] All =
    {
        new("lang", Kind.String, "auto", "all",
            "界面语言（zh / en / auto；GUI、CLI 与游戏内文字共用这一个键）",
            "interface language (zh / en / auto; shared by GUI, CLI and in-game text)"),
        new("theme", Kind.String, "dark", "all",
            "控制台/界面主题名（api/ntl_theme_* 读取）",
            "console/UI theme name (read by api/ntl_theme_*)"),
        new("base_mod", Kind.String, "", "cli",
            "整包 data.win 基底 mod 的 id（部署时选择基底）",
            "id of the whole-pack data.win base mod used when deploying"),
        new("cache_max_mb", Kind.Int, "4096", "cli",
            "部署缓存上限（MB；缓存可重建，不是备份）",
            "deploy cache cap in MB (cache is rebuildable, not a backup)"),
        new("restore_on_exit", Kind.Bool, "false", "gui",
            "游戏退出后按部署会话自动还原原版（默认关）",
            "auto-restore vanilla after the game exits, by deploy session (off by default)"),
        new("debug_live", Kind.Bool, "false", "game",
            "live 脚本调试日志（排障用，会刷屏）",
            "live-script debug logging (troubleshooting; noisy)"),
        new("audio_sniff", Kind.Bool, "false", "game",
            "音频诊断（实验，排障用）",
            "audio diagnostics (experimental; troubleshooting)"),
        new("auto_skip", Kind.Bool, "false", "game",
            "自动跳过章节选择器，直接进入 auto_chapter",
            "skip the chapter selector and enter auto_chapter directly"),
        new("auto_chapter", Kind.Int, "0", "game",
            "自动进入的章节序号（0 = 关）",
            "chapter number to auto-enter (0 = off)"),
        new("auto_chapter_id", Kind.String, "", "game",
            "直接指定章节 id（外部/时间线章节用；优先于 auto_chapter；GUI 不再写它）",
            "explicit chapter id (external/timeline chapters; wins over auto_chapter; the GUI no longer writes it)"),
        new("skip_intro", Kind.Bool, "false", "game",
            "跳过章节内开场演出（传说 + DELTARUNE 报幕）",
            "skip the in-chapter intro (legend + DELTARUNE logo)"),
        new("auto_skip_delay", Kind.Int, "90", "game",
            "自动跳过选择器前等待帧数（高级）",
            "frames to wait before skipping the selector (advanced)", false, "",
            "旧 GUI 会写 90；一般不用改", "the old GUI wrote 90; rarely changed"),
        new("intro_delay", Kind.Int, "45", "game",
            "跳过开场演出前等待帧数（高级）",
            "frames to wait before skipping the intro (advanced)", false, "",
            "旧 GUI 会写 45；一般不用改", "the old GUI wrote 45; rarely changed"),
        new("schema_version", Kind.Int, "2", "cli",
            "配置结构版本（--config-migrate 写入）",
            "config schema version (written by --config-migrate)"),

        // ---------- 已废弃：只读兼容，迁移后写规范键；旧键保留是为了还没重新部署的旧产物 ----------
        new("auto_skip_selector", Kind.Bool, "false", "game",
            "【已废弃】改用 auto_skip", "deprecated: use auto_skip", true, "auto_skip"),
        new("auto_skip_intro", Kind.Bool, "false", "game",
            "【已废弃】改用 skip_intro", "deprecated: use skip_intro", true, "skip_intro"),
        new("skip_legend", Kind.Bool, "false", "game",
            "【已废弃】改用 skip_intro", "deprecated: use skip_intro", true, "skip_intro"),
        new("skip_logo", Kind.Bool, "false", "game",
            "【已废弃】改用 skip_intro", "deprecated: use skip_intro", true, "skip_intro"),
    };

    public static IEnumerable<Entry> Canonical => All.Where(e => !e.Deprecated);

    public static Entry? Find(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return Array.Find(All, e => string.Equals(e.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static bool OwnerValid(string owner) => owner is "all" or "gui" or "cli" or "game";

    // ------------------------------------------------------------------ 值解析

    public static bool ToBool(JsonNode? n)
    {
        if (n == null) return false;
        try { return n.GetValueKind() == JsonValueKind.True || n.GetValue<bool>(); } catch { }
        var s = n.ToString().Trim();
        return s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1"
            || s.Equals("on", StringComparison.OrdinalIgnoreCase) || s.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    public static long ToInt(JsonNode? n, long dflt)
    {
        if (n == null) return dflt;
        try { return n.GetValue<long>(); } catch { }
        return long.TryParse(n.ToString().Trim(), out var v) ? v : dflt;
    }

    public static string ToStr(JsonNode? n) => n == null ? "" : n.ToString();

    static JsonNode? DefaultNode(Entry e) => e.Kind switch
    {
        Kind.Bool => JsonValue.Create(ToBool(JsonValue.Create(e.Default))),
        Kind.Int => JsonValue.Create(long.TryParse(e.Default, out var i) ? i : 0L),
        _ => JsonValue.Create(e.Default),
    };

    /// <summary>解析一个字符串成该键的 JSON 值（--config-set 用）。失败返回 null 并给出原因。</summary>
    public static JsonNode? ParseValue(Entry e, string raw, out string error)
    {
        error = "";
        var t = raw.Trim();
        if (t.Length >= 2 && ((t[0] == '"' && t[^1] == '"') || (t[0] == '\'' && t[^1] == '\''))) t = t[1..^1];
        switch (e.Kind)
        {
            case Kind.Bool:
                if (t.Equals("true", StringComparison.OrdinalIgnoreCase) || t == "1"
                    || t.Equals("on", StringComparison.OrdinalIgnoreCase) || t.Equals("yes", StringComparison.OrdinalIgnoreCase))
                    return JsonValue.Create(true);
                if (t.Equals("false", StringComparison.OrdinalIgnoreCase) || t == "0"
                    || t.Equals("off", StringComparison.OrdinalIgnoreCase) || t.Equals("no", StringComparison.OrdinalIgnoreCase))
                    return JsonValue.Create(false);
                error = L("布尔键只接受 true/false/1/0/on/off/yes/no，收到: {0}", raw);
                return null;
            case Kind.Int:
                if (long.TryParse(t, out var n)) return JsonValue.Create(n);
                error = L("数字键只接受整数，收到: {0}", raw);
                return null;
            default:
                return JsonValue.Create(t);
        }
    }

    // ------------------------------------------------------------------ 生效值 / 迁移

    /// <summary>规范键的生效值（文件里有就用文件值，没有用默认值；废弃键按映射兜底）。</summary>
    public static JsonObject Effective(string gameRoot)
    {
        var raw = ConfigFile.Load(gameRoot);
        var eff = new JsonObject();
        foreach (var e in Canonical)
        {
            var v = raw[e.Name];
            eff[e.Name] = v != null ? v.DeepClone() : DefaultNode(e);
        }
        // 废弃键兜底：文件里没有新键、但有旧键时，按旧值算出生效值（只影响读取，不写盘）。
        if (raw["auto_skip"] == null && raw["auto_skip_selector"] != null)
            eff["auto_skip"] = JsonValue.Create(ToBool(raw["auto_skip_selector"]));
        if (raw["skip_intro"] == null)
        {
            bool legacy = ToBool(raw["auto_skip_intro"]) || ToBool(raw["skip_legend"]) || ToBool(raw["skip_logo"]);
            if (legacy) eff["skip_intro"] = JsonValue.Create(true);
        }
        return eff;
    }

    /// <summary>一条迁移映射：旧键 → 规范键。ToValue 是**合并后**的目标值（多个旧键合并成一个新键时取 OR）。</summary>
    public sealed record Migration(string From, string To, string FromValue, string ToValue, string ReasonZh, string ReasonEn);

    public static List<Migration> Plan(string gameRoot) => PlanFrom(ConfigFile.Load(gameRoot));

    public static List<Migration> PlanFrom(JsonObject raw)
    {
        var list = new List<Migration>();
        bool needAutoSkip = raw["auto_skip"] == null;
        bool needSkipIntro = raw["skip_intro"] == null;
        if (needAutoSkip && raw["auto_skip_selector"] != null)
            list.Add(new("auto_skip_selector", "auto_skip",
                ToStr(raw["auto_skip_selector"]), ToBool(raw["auto_skip_selector"]) ? "true" : "false",
                "旧名：跳过章节选择器", "legacy name: skip the chapter selector"));
        if (needSkipIntro)
        {
            bool merged = ToBool(raw["auto_skip_intro"]) || ToBool(raw["skip_legend"]) || ToBool(raw["skip_logo"]);
            var mergedText = merged ? "true" : "false";
            foreach (var oldName in new[] { "auto_skip_intro", "skip_legend", "skip_logo" })
                if (raw[oldName] != null)
                    list.Add(new(oldName, "skip_intro", ToStr(raw[oldName]), mergedText,
                        "旧名：开场跳过（多个旧键按「或」合并）", "legacy names: intro skip (merged with OR)"));
        }
        return list;
    }

    // ------------------------------------------------------------------ CLI

    static readonly JsonSerializerOptions JsonOutOpts = new(Paths.Json) { WriteIndented = true };

    /// <summary>--config-keys [--json]</summary>
    public static int CliKeys(string gameRoot, bool json)
    {
        var raw = ConfigFile.Load(gameRoot);
        var eff = Effective(gameRoot);
        var rows = All.Select(e =>
        {
            bool inFile = raw[e.Name] != null;
            return new
            {
                name = e.Name,
                kind = e.Kind.ToString().ToLowerInvariant(),
                owner = e.Owner,
                @default = e.Default,
                effective = eff[e.Name]?.ToString() ?? "",
                present = inFile,
                deprecated = e.Deprecated,
                replacement = e.Replacement,
                zh = e.Zh,
                en = e.En,
                noteZh = e.NoteZh,
                noteEn = e.NoteEn,
            };
        }).ToList();

        if (json)
        {
            var payload = new
            {
                schemaVersion = 1,
                gameRoot,
                configPath = Paths.ConfigPath(gameRoot),
                configSchemaVersion = (int)ToInt(raw["schema_version"], 0),
                expectedSchemaVersion = SchemaVersion,
                keys = rows,
                summary = new
                {
                    total = rows.Count,
                    canonical = rows.Count(r => !r.deprecated),
                    deprecated = rows.Count(r => r.deprecated),
                    present = rows.Count(r => r.present),
                },
            };
            Console.WriteLine(JsonSerializer.Serialize(payload, JsonOutOpts));
            return 0;
        }

        Console.WriteLine(L("===== 配置键目录（config.json）====="));
        Console.WriteLine(L("文件: {0}", Paths.ConfigPath(gameRoot)));
        Console.WriteLine(L("结构版本: {0}（期望 {1}）",
            ToInt(raw["schema_version"], 0), SchemaVersion));
        Console.WriteLine();
        Console.WriteLine(L("规范键:"));
        foreach (var e in Canonical)
        {
            var val = eff[e.Name]?.ToString() ?? "";
            Console.WriteLine("  " + e.Name.PadRight(18) + " " + e.Kind.ToString().ToLowerInvariant().PadRight(6)
                + " " + val.PadRight(10) + " " + L("owner={0}", e.Owner) + "  " + Desc(e));
        }
        Console.WriteLine();
        Console.WriteLine(L("已废弃（只读兼容，--config-migrate 会写规范键）:"));
        foreach (var e in All.Where(x => x.Deprecated))
            Console.WriteLine("  " + e.Name.PadRight(18) + " -> " + e.Replacement + "  " + Desc(e));
        Console.WriteLine();
        Console.WriteLine(L("提示: --config-get <键> / --config-set <键=值> / --config-migrate --dry-run"));
        return 0;
    }

    /// <summary>规范键的生效值来自哪个旧键（没有则空串）：--config-get 用来说明「默认值」其实是旧键兜底。</summary>
    static string LegacySource(JsonObject raw, string name)
    {
        if (raw[name] != null) return "";
        string[] olds = name switch
        {
            "auto_skip" => new[] { "auto_skip_selector" },
            "skip_intro" => new[] { "auto_skip_intro", "skip_legend", "skip_logo" },
            _ => Array.Empty<string>(),
        };
        foreach (var o in olds) if (raw[o] != null) return o;
        return "";
    }

    /// <summary>--config-get &lt;键&gt; [--json]</summary>
    public static int CliGet(string gameRoot, string? keyArg, bool json)
    {
        var raw = ConfigFile.Load(gameRoot);
        var e = Find(keyArg);
        if (e == null)
        {
            // 未知键也允许查看（只读）：配置里可能有历史遗留键，粗暴报错会让人以为配置坏了。
            var loose = raw[keyArg ?? ""];
            if (loose != null)
            {
                if (json)
                {
                    Console.WriteLine(JsonSerializer.Serialize(new
                    {
                        key = keyArg, kind = "unknown", value = loose.ToString(), source = "file",
                        documented = false, configPath = Paths.ConfigPath(gameRoot),
                    }, JsonOutOpts));
                    return 0;
                }
                Console.WriteLine(L("{0} = {1}    （未知键：不在配置键目录里，会原样保留）", keyArg ?? "", loose.ToString()));
                return 0;
            }
            Console.WriteLine(L("[错误] 配置里没有这个键: {0}", keyArg ?? ""));
            Console.WriteLine(L("       查看全部键: --config-keys"));
            return 2;
        }
        var node = raw[e.Name];
        string source;
        string value;
        string legacy = "";
        if (node != null) { source = "file"; value = node.ToString(); }
        else if (e.Deprecated)
        {
            source = "missing";
            value = "";
        }
        else
        {
            legacy = LegacySource(raw, e.Name);
            source = legacy.Length > 0 ? "legacy" : "default";
            value = Effective(gameRoot)[e.Name]?.ToString() ?? e.Default;
        }

        if (json)
        {
            var payload = new
            {
                key = e.Name,
                kind = e.Kind.ToString().ToLowerInvariant(),
                owner = e.Owner,
                @default = e.Default,
                value,
                source,
                legacyKey = legacy.Length > 0 ? legacy : null,
                deprecated = e.Deprecated,
                replacement = e.Replacement,
                configPath = Paths.ConfigPath(gameRoot),
                zh = e.Zh,
                en = e.En,
            };
            Console.WriteLine(JsonSerializer.Serialize(payload, JsonOutOpts));
            return 0;
        }
        Console.WriteLine(L("{0} = {1}    （类型 {2}，owner={3}，来源 {4}）",
            e.Name, value, e.Kind.ToString().ToLowerInvariant(), e.Owner,
            source == "file" ? L("配置文件") : source == "default" ? L("默认值")
                : source == "legacy" ? L("旧键 {0}", legacy) : L("缺失")));
        if (e.Deprecated)
            Console.WriteLine(L("  注意: 该键已废弃，请改用 {0}（--config-migrate 可自动迁移）", e.Replacement));
        return 0;
    }

    /// <summary>--config-set &lt;键=值&gt; [--json]（读-改-写，未知键与 _comment 保留；值不存在则新建）。</summary>
    public static int CliSet(string gameRoot, string? assign, bool json)
    {
        if (string.IsNullOrWhiteSpace(assign) || !assign.Contains('='))
        {
            Console.WriteLine(L("[错误] 用法: --config-set <键=值>（例: --config-set auto_skip=1）"));
            return 2;
        }
        var idx = assign.IndexOf('=');
        var name = assign[..idx].Trim();
        var text = assign[(idx + 1)..].Trim();
        var e = Find(name);
        if (e == null)
        {
            Console.WriteLine(L("[错误] 配置里没有这个键: {0}", name));
            Console.WriteLine(L("       查看全部键: --config-keys"));
            return 2;
        }
        if (e.Deprecated)
        {
            Console.WriteLine(L("[错误] {0} 已废弃，请改用 {1}", e.Name, e.Replacement));
            return 2;
        }
        var parsed = ParseValue(e, text, out var err);
        if (parsed == null)
        {
            Console.WriteLine(L("[错误] {0}", err));
            return 2;
        }
        var raw = ConfigFile.Load(gameRoot);
        var old = raw[e.Name]?.ToString() ?? "";
        if (raw[e.Name] != null && string.Equals(old, parsed.ToString(), StringComparison.Ordinal)) 
        {
            var sameMsg = L("{0} 已经是 {1}，没有改动。", e.Name, old);
            if (json) Console.WriteLine(JsonSerializer.Serialize(new { key = e.Name, oldValue = old, newValue = old, changed = false, configPath = Paths.ConfigPath(gameRoot) }, JsonOutOpts));
            else Console.WriteLine(sameMsg);
            return 0;
        }
        ConfigFile.Set(gameRoot, e.Name, parsed);
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                key = e.Name, oldValue = old, newValue = parsed.ToString(), changed = true,
                configPath = Paths.ConfigPath(gameRoot),
            }, JsonOutOpts));
            return 0;
        }
        Console.WriteLine(L("已写入 {0}: {1} -> {2}", e.Name, old.Length == 0 ? L("（无）") : old, parsed.ToString()));
        Console.WriteLine(L("配置文件: {0}", Paths.ConfigPath(gameRoot)));
        if (e.Kind == Kind.Bool || e.Name is "lang" or "theme")
            Console.WriteLine(L("提示: 游戏内生效需要重启游戏；GUI 与游戏读同一个 config.json。"));
        return 0;
    }

    /// <summary>--config-migrate [--dry-run] [--json]：把废弃键迁移成规范键 + 写 schema_version。</summary>
    public static int CliMigrate(string gameRoot, bool dryRun, bool json)
    {
        var raw = ConfigFile.Load(gameRoot);
        var plan = PlanFrom(raw);
        bool needVersion = ToInt(raw["schema_version"], 0) < SchemaVersion;

        if (json)
        {
            var payload = new
            {
                schemaVersion = 1,
                gameRoot,
                configPath = Paths.ConfigPath(gameRoot),
                dryRun,
                currentSchemaVersion = (int)ToInt(raw["schema_version"], 0),
                expectedSchemaVersion = SchemaVersion,
                migrations = plan.Select(m => new { from = m.From, to = m.To, fromValue = m.FromValue, toValue = m.ToValue }),
                applied = !dryRun && (plan.Count > 0 || needVersion),
            };
            Console.WriteLine(JsonSerializer.Serialize(payload, JsonOutOpts));
            if (dryRun || (plan.Count == 0 && !needVersion)) return 0;
        }

        if (plan.Count == 0 && !needVersion)
        {
            if (!json) Console.WriteLine(L("配置已是最新结构（schema_version={0}），无需迁移。", SchemaVersion));
            return 0;
        }

        if (dryRun)
        {
            Console.WriteLine(L("===== 配置迁移预演（不写盘） ====="));
            foreach (var m in plan)
                Console.WriteLine("  " + m.From + " = " + m.FromValue + "  ->  " + m.To + " = " + m.ToValue + "  （" + (Lang.IsEn ? m.ReasonEn : m.ReasonZh) + "）");
            Console.WriteLine(L("将写入 config.json 的规范键；旧键保留（还没重新部署的旧产物仍会读）。"));
            return 0;
        }

        var values = new Dictionary<string, JsonNode?>();
        foreach (var m in plan)
        {
            var target = Find(m.To);
            if (target == null) continue;
            var node = ParseValue(target, m.ToValue, out _);
            if (node != null) values[m.To] = node;
        }
        if (needVersion) values["schema_version"] = SchemaVersion;
        if (values.Count > 0) ConfigFile.SetMany(gameRoot, values);

        foreach (var m in plan)
            Console.WriteLine(L("  已迁移 {0} -> {1}", m.From, m.To));
        Console.WriteLine(L("配置迁移完成（schema_version={0}）。旧键保留，运行时两侧都认。", SchemaVersion));
        Console.WriteLine(L("配置文件: {0}", Paths.ConfigPath(gameRoot)));
        return 0;
    }

    // ------------------------------------------------------------------ 语言

    public sealed record LangResolution(string Code, string Source, string Effective,
        string ConfigLang, string ConfigPath, string GuiLangPath, string GuiLangState);

    /// <summary>语言解析顺序：--lang &gt; NTL_LANG &gt; config.json.lang &gt; 默认 zh。
    /// "auto" 按系统 UI 语言归一到 zh/en（与 Lang.Set 的语义一致）。</summary>
    public static LangResolution ResolveLang(string? gameRoot, string? cliLang, string? envLang = null)
    {
        // 配置文件里的 lang **总是**读出来（即使被 --lang/NTL_LANG 覆盖）：
        // --lang-resolve 要能同时回答「生效的是哪来的」和「config.json 里存的是什么」。
        var cfgLang = "";
        if (!string.IsNullOrWhiteSpace(gameRoot))
        {
            try { cfgLang = ToStr(ConfigFile.Load(gameRoot)["lang"]).Trim(); }
            catch { /* 配置读不了就按默认，绝不影响启动 */ }
        }
        string code = "zh", source = "default";
        if (!string.IsNullOrWhiteSpace(cliLang)) { code = cliLang.Trim(); source = "cli"; }
        else
        {
            envLang ??= Environment.GetEnvironmentVariable("NTL_LANG");
            if (!string.IsNullOrWhiteSpace(envLang)) { code = envLang.Trim(); source = "env"; }
            else if (cfgLang.Length > 0) { code = cfgLang; source = "config"; }
        }
        code = code.ToLowerInvariant();
        var effective = code == "auto"
            ? (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase) ? "zh" : "en")
            : code;

        var cfgPath = string.IsNullOrWhiteSpace(gameRoot) ? "" : Paths.ConfigPath(gameRoot);
        var guiPath = string.IsNullOrWhiteSpace(gameRoot) ? "" : Path.Combine(Paths.NeutraledRoot(gameRoot), "gui_lang.txt");
        var guiState = "missing";
        if (guiPath.Length > 0 && File.Exists(guiPath))
        {
            var v = "";
            try { v = File.ReadAllText(guiPath).Trim().ToLowerInvariant(); } catch { }
            guiState = v is "zh" or "en" ? "present:" + v : "present:unknown";
        }
        return new LangResolution(code, source, effective, cfgLang, cfgPath, guiPath, guiState);
    }

    /// <summary>--lang-resolve [--json]：显示语言从哪来（GUI/游戏读同一个键）。</summary>
    public static int CliLangResolve(string gameRoot, string? cliLang, bool json)
    {
        var r = ResolveLang(gameRoot, cliLang);
        var guiEffective = r.Effective == "en" ? "en" : "zh";
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                code = r.Code,
                source = r.Source,
                effective = r.Effective,
                guiEffective,
                configPath = r.ConfigPath,
                configLang = r.ConfigLang,
                guiLangPath = r.GuiLangPath,
                guiLangState = r.GuiLangState,
                priority = "--lang > NTL_LANG > config.json.lang > zh",
            }, JsonOutOpts));
            return 0;
        }
        Console.WriteLine(L("[语言] 生效: {0}（来源: {1}）", r.Code, r.Source switch
        {
            "cli" => L("命令行 --lang"),
            "env" => "NTL_LANG",
            "config" => L("config.json 的 lang"),
            _ => L("默认值"),
        }));
        Console.WriteLine(L("       GUI/游戏实际显示: {0}（GUI 只支持 zh/en）", guiEffective));
        Console.WriteLine(L("       配置文件: {0}", r.ConfigPath));
        if (r.GuiLangState != "missing")
            Console.WriteLine(L("       [提示] 发现旧文件 gui_lang.txt（{0}）：已不再作为语言来源，跑 --lang-migrate 可清理。", r.GuiLangState));
        return 0;
    }

    /// <summary>--lang-migrate [--dry-run] [--json]：把 gui_lang.txt（旧真源）并进 config.json.lang 并改名归档。</summary>
    public static int CliLangMigrate(string gameRoot, bool dryRun, bool json)
    {
        var legacy = Path.Combine(Paths.NeutraledRoot(gameRoot), "gui_lang.txt");
        if (!File.Exists(legacy))
        {
            if (json) Console.WriteLine(JsonSerializer.Serialize(new { migrated = false, reason = "no gui_lang.txt", configPath = Paths.ConfigPath(gameRoot) }, JsonOutOpts));
            else Console.WriteLine(L("没有需要迁移的 gui_lang.txt（语言真源已经是 config.json 的 lang）。"));
            return 0;
        }
        var value = "";
        try { value = File.ReadAllText(legacy).Trim().ToLowerInvariant(); } catch { }
        var raw = ConfigFile.Load(gameRoot);
        var cfgLang = ToStr(raw["lang"]).Trim();
        bool writeConfig = cfgLang.Length == 0 && value is "zh" or "en";
        var moved = legacy + ".migrated";

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                migrated = !dryRun,
                dryRun,
                legacyPath = legacy,
                legacyValue = value,
                configLangBefore = cfgLang,
                configLangAfter = writeConfig ? value : cfgLang,
                renamedTo = dryRun ? "" : moved,
                note = writeConfig ? "config.json.lang was empty; migrated from gui_lang.txt" : "config.json.lang kept (it is authoritative)",
            }, JsonOutOpts));
            if (dryRun) return 0;
        }
        else if (dryRun)
        {
            Console.WriteLine(L("===== 语言迁移预演（不写盘） ====="));
            Console.WriteLine(L("  gui_lang.txt = {0}", value.Length == 0 ? L("（空）") : value));
            Console.WriteLine(L("  config.json.lang = {0}", cfgLang.Length == 0 ? L("（未设置）") : cfgLang));
            Console.WriteLine(writeConfig
                ? L("  将把 config.json.lang 设为 {0}，并把 gui_lang.txt 改名为 {1}", value, Path.GetFileName(moved))
                : L("  config.json.lang 已有值（权威），只把 gui_lang.txt 改名为 {0}", Path.GetFileName(moved)));
            return 0;
        }

        if (writeConfig) ConfigFile.Set(gameRoot, "lang", JsonValue.Create(value));
        var target = moved;
        if (File.Exists(target)) target = legacy + ".migrated-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        try { File.Move(legacy, target); }
        catch (Exception ex)
        {
            Console.WriteLine(L("[警告] 改名 gui_lang.txt 失败（不影响语言真源）: {0}", ex.Message));
            target = "";
        }
        Console.WriteLine(L("[语言] 迁移完成: config.json.lang = {0}（唯一真源）", writeConfig ? value : cfgLang));
        if (target.Length > 0) Console.WriteLine(L("        旧文件已归档: {0}", Path.GetFileName(target)));
        return 0;
    }
}
