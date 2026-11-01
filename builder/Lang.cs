using System.Globalization;
using System.Text.Json.Nodes;

namespace Neutraled.Builder;

/// <summary>builder 多语言输出（中/英为编译期表，其余语言为运行期外部语言包）。
/// 语言来源优先级：命令行 --lang &lt;code&gt; &gt; 环境变量 NTL_LANG &gt; &lt;游戏根&gt;/Neutraled/config.json 的 lang &gt; 默认 zh。
/// 设计同 gui/Localizer.cs：**以中文原文为 key** 查译文表，未命中就原样返回中文（缺译文不会崩，只是这一句没翻译）。
/// 覆盖范围：**控制台输出**（Console.Write*/Console.Error.*/Paths.Log）与抛出的异常消息；
/// 日志文件、生成的文档/报告、落盘的 JSON/文本一律保持中文。
/// 外部语言包（lang/lang_&lt;code&gt;.json）由 LangPacks 通过 AddTable 注入，键缺失时自动回退中文。</summary>
public static class Lang
{
    public static string Current { get; private set; } = "zh";
    public static bool IsEn => Current == "en";

    private static readonly Dictionary<string, string> En = new(StringComparer.Ordinal);

    /// <summary>附加语言表：code → (中文 key → 译文)</summary>
    private static readonly Dictionary<string, Dictionary<string, string>> Extra = new(StringComparer.OrdinalIgnoreCase);

    static Lang()
    {
        LangTable_Program.Fill(En);
        LangTable_Deploy.Fill(En);
        LangTable_Mods.Fill(En);
        LangTable_Tools.Fill(En);
        LangTable_Extra.Fill(En);
        LangTable_Features.Fill(En);   // 第十二批：G3M 对等功能（配置档/快照/恢复点/黑名单/GameBanana/队列/插件/主题/语言包/网页界面）
        LangTable_Baseline.Fill(En);   // C1：原版备份卫生 / baseline 账本 / --verify-backup / --repair-backup
        LangTable_C2.Fill(En);         // C2：部署只写 live / live 修改归档 / 单一备份写者
        LangTable_C4.Fill(En);         // C4 P0-1：--deploy-state 按内容哈希判定 live 原版/已部署
        LangTable_Cli.Fill(En);        // C4 P1-1：--help 分层 / --help --audit / --gen-docs / lint 规则 18
        LangTable_Config.Fill(En);     // C4 P1-8 / P1-5：config.json 配置键目录 / 语言唯一真源 / --config-* / --lang-resolve
        LangTable_P12P13.Fill(En);     // C4 P1-2 / P1-3：统一搜索 / 统一下载+导入 / 包探测
    }

    /// <summary>英文表命中数（--lang-diag 用）。</summary>
    public static int EnCount => En.Count;

    /// <summary>内置表里的全部中文 key（覆盖率 / 模板导出用）。
    /// **只含编译期表**：外部语言包是「中文 key → 译文」的补充，若把它们并进来，
    /// 待译全集就会随「装了哪种语言包」浮动（未装包的语言覆盖率虚低），统计结果不可复现。</summary>
    public static IEnumerable<string> Keys => En.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

    public static void Set(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || string.Equals(code, "auto", StringComparison.OrdinalIgnoreCase))
        {
            Current = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase) ? "zh" : "en";
            return;
        }
        // 任意语言代码都接受：没有对应表时 L() 逐条回退中文（不崩、不静默丢内容）
        Current = code.Trim().ToLowerInvariant();
    }

    /// <summary>切换当前语言（LangPacks / --lang-use 用）。</summary>
    public static void SetCode(string code) => Set(code);

    /// <summary>注册/覆盖一份附加语言表（LangPacks.LoadExternal 调用）。</summary>
    public static void AddTable(string code, IDictionary<string, string> map)
    {
        if (string.IsNullOrWhiteSpace(code)) return;
        lock (Extra) Extra[code.Trim().ToLowerInvariant()] = new Dictionary<string, string>(map, StringComparer.Ordinal);
    }

    public static bool HasTable(string code)
    {
        lock (Extra) return Extra.ContainsKey(code);
    }

    /// <summary>某语言是否有该 key（zh 恒 true，en 查编译期表，其它查附加表）。</summary>
    public static bool Has(string code, string key)
    {
        if (string.Equals(code, "zh", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(code, "en", StringComparison.OrdinalIgnoreCase)) return En.ContainsKey(key);
        lock (Extra) return Extra.TryGetValue(code, out var t) && t.ContainsKey(key);
    }

    /// <summary>取某语言的译文；没有则返回中文原文（key 本身）。</summary>
    public static string Get(string code, string key)
    {
        if (string.Equals(code, "en", StringComparison.OrdinalIgnoreCase)) return En.TryGetValue(key, out var v) ? v : key;
        if (string.Equals(code, "zh", StringComparison.OrdinalIgnoreCase)) return key;
        lock (Extra) return Extra.TryGetValue(code, out var t) && t.TryGetValue(key, out var v2) ? v2 : key;
    }

    /// <summary>命令行 --lang 优先；否则 NTL_LANG；否则 config.json 的 lang；都没有则 zh。
    /// 解析逻辑收敛到 ConfigSchema.ResolveLang（--lang-resolve 与 GUI 共用同一套顺序）。</summary>
    public static void Init(string? cliLang, string? gameRoot)
    {
        try
        {
            Set(ConfigSchema.ResolveLang(gameRoot, cliLang).Code);
        }
        catch { /* 配置读不了就保持 zh，绝不影响部署 */ }
    }

    /// <summary>查表并格式化。模板里的 {0}/{1}/{{/}} 走 string.Format 语义，与原来的 $"..." 一致。</summary>
    public static string L(string zh, params object?[] args)
    {
        var s = zh;
        if (Current == "en") { if (En.TryGetValue(zh, out var en)) s = en; }
        else if (Current != "zh")
        {
            lock (Extra) if (Extra.TryGetValue(Current, out var tbl) && tbl.TryGetValue(zh, out var tr)) s = tr;
        }
        if (args.Length == 0) return s;
        try { return string.Format(s, args); } catch (FormatException) { return s; }
    }
}
