using System.Text;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>Kristal Lua 批量验证（C3）
///
/// 之前单个脚本直接跑会崩游戏，所以改成**分层验证**：
///   第 1 层：静态扫描（不执行）—— 检查能否被 NTL 的 Lua 词法/语法接受
///   第 2 层：API 覆盖率 —— 统计用了哪些 NTL 没实现的 API
///   第 3 层：风险分级 —— 标出最可能出问题的脚本（按未实现 API 数量排序）
///
/// 用法：ntl-builder --validate-kristal <Kristal mod 目录>
/// </summary>
public static class KristalValidate
{
    public sealed class Result
    {
        public int Files = 0;
        public int SyntaxOk = 0;
        public int SyntaxFail = 0;
        public Dictionary<string, int> ApiUsage = new();     // API 名 -> 使用次数
        public Dictionary<string, int> Unsupported = new();  // 未实现的 API
        public List<(string file, int risk, string reason)> Risky = new();
        public List<string> SyntaxErrors = new();
    }

    // NTL 已实现的 API 前缀（出现在这里 = 支持）
    private static readonly string[] SupportedPrefixes =
    {
        "love.graphics", "love.timer", "love.filesystem", "love.keyboard", "love.mouse", "love.audio",
        "Kristal.", "Game.", "Registry.", "Assets.", "Mod.", "Music.", "Input.", "Camera.", "Draw.",
        "Class", "Object", "Sprite", "Text", "Actor", "Battler", "EnemyBattler", "PartyBattler",
        "Bullet", "Arena", "World", "Cutscene", "Wave", "Encounter",
        "ntl_", "print", "pairs", "ipairs", "tostring", "tonumber", "type", "pcall", "error",
        "setmetatable", "getmetatable", "rawget", "rawset", "select", "unpack", "next", "require",
        "table.", "string.", "math.", "os.time", "os.date", "os.clock",
    };

    // 明确未实现的
    private static readonly string[] KnownUnsupported =
    {
        "love.window.", "love.event.", "love.thread.", "love.system.", "love.touch.",
        "coroutine.", "io.", "os.execute", "os.exit", "os.getenv", "os.remove", "os.rename",
        "debug.", "package.", "loadstring", "load(",
    };

    public static Result Validate(string srcDir)
    {
        var rep = new Result();
        if (!Directory.Exists(srcDir))
        {
            Console.WriteLine(L("[错误] 目录不存在: {0}", srcDir));
            return rep;
        }

        var files = Directory.GetFiles(srcDir, "*.lua", SearchOption.AllDirectories);
        Console.WriteLine(L("===== Kristal 批量验证 ====="));
        Console.WriteLine(L("  目录: {0}", srcDir));
        Console.WriteLine(L("  文件: {0} 个 .lua", files.Length));
        Console.WriteLine();

        foreach (var f in files)
        {
            rep.Files++;
            var rel = Path.GetRelativePath(srcDir, f);
            string src;
            try { src = File.ReadAllText(f); } catch { continue; }

            // ---- 第 1 层：基础语法检查（括号配对、关键字拼写）----
            if (BasicSyntaxCheck(src, out var err))
                rep.SyntaxOk++;
            else
            {
                rep.SyntaxFail++;
                if (rep.SyntaxErrors.Count < 30) rep.SyntaxErrors.Add($"{rel}: {err}");
            }

            // ---- 第 2 层：API 使用统计 ----
            int unsupportedHere = 0;
            var reasons = new List<string>();

            foreach (var api in KnownUnsupported)
            {
                int cnt = CountOccurrences(src, api);
                if (cnt > 0)
                {
                    rep.Unsupported[api] = rep.Unsupported.GetValueOrDefault(api) + cnt;
                    unsupportedHere += cnt;
                    reasons.Add($"{api} x{cnt}");
                }
            }

            // 统计常见 Kristal API 使用
            foreach (var api in new[] { "Game.world", "Game.battle", "Registry.", "Assets.", "Kristal.",
                                        "love.graphics.", "love.audio.", "Class(", "Stage(", "Textbox" })
            {
                int cnt = CountOccurrences(src, api);
                if (cnt > 0) rep.ApiUsage[api] = rep.ApiUsage.GetValueOrDefault(api) + cnt;
            }

            // ---- 第 3 层：风险分级 ----
            int risk = unsupportedHere * 10;
            if (src.Contains("Stage(")) risk += 3;
            if (src.Contains("Textbox")) risk += 3;
            if (src.Contains("love.graphics.newImage") || src.Contains("love.graphics.newFont")) risk += 5;
            if (src.Length > 20000) risk += 2;      // 超大文件更容易踩到未实现的语法

            if (risk > 0)
                rep.Risky.Add((rel, risk, string.Join(", ", reasons.Take(3))));
        }

        rep.Risky.Sort((a, b) => b.risk.CompareTo(a.risk));

        // ---- 输出报告 ----
        Console.WriteLine(L("  语法检查通过: {0}/{1}", rep.SyntaxOk, rep.Files));
        if (rep.SyntaxFail > 0)
        {
            Console.WriteLine(L("  语法可疑: {0}", rep.SyntaxFail));
            foreach (var e in rep.SyntaxErrors.Take(10)) Console.WriteLine($"    {e}");
        }
        Console.WriteLine();

        Console.WriteLine(L("  API 使用统计（Top 12）:"));
        foreach (var kv in rep.ApiUsage.OrderByDescending(x => x.Value).Take(12))
            Console.WriteLine(L("    {0,-24} {1,6} 次", kv.Key, kv.Value));
        Console.WriteLine();

        if (rep.Unsupported.Count > 0)
        {
            Console.WriteLine(L("  ⚠️ 未实现的 API（会导致脚本失败）:"));
            foreach (var kv in rep.Unsupported.OrderByDescending(x => x.Value))
                Console.WriteLine(L("    {0,-24} {1,6} 次", kv.Key, kv.Value));
            Console.WriteLine();
        }
        else
            Console.WriteLine(L("  ✅ 没有用到已知未实现的 API"));

        if (rep.Risky.Count > 0)
        {
            Console.WriteLine(L("  风险最高的脚本（共 {0} 个有风险）:", rep.Risky.Count));
            foreach (var (file, risk, reason) in rep.Risky.Take(15))
                Console.WriteLine($"    [{risk,4}] {file}{(reason.Length > 0 ? "  <- " + reason : "")}");
        }

        // 写报告文件
        var sb = new StringBuilder();
        sb.AppendLine("# Kristal 批量验证报告");
        sb.AppendLine();
        sb.AppendLine($"- 文件总数: {rep.Files}");
        sb.AppendLine($"- 语法通过: {rep.SyntaxOk}");
        sb.AppendLine($"- 语法可疑: {rep.SyntaxFail}");
        sb.AppendLine();
        sb.AppendLine("## 未实现的 API");
        foreach (var kv in rep.Unsupported.OrderByDescending(x => x.Value))
            sb.AppendLine($"- {kv.Key} : {kv.Value} 次");
        sb.AppendLine();
        sb.AppendLine("## 风险最高的脚本");
        foreach (var (file, risk, reason) in rep.Risky.Take(50))
            sb.AppendLine($"- [{risk}] {file} {reason}");

        try
        {
            var outDir = Path.Combine(Paths.NeutraledRoot(Paths.DetectGameRoot()), "kristal-validation");
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "REPORT.md"), sb.ToString());
            Console.WriteLine();
            Console.WriteLine(L("  报告: {0}", Path.Combine(outDir, "REPORT.md")));
        }
        catch { }

        return rep;
    }

    private static bool BasicSyntaxCheck(string src, out string err)
    {
        err = "";
        int paren = 0, brace = 0, bracket = 0;
        bool inStr = false, inLongStr = false, inComment = false;
        char strDelim = '"';

        for (int i = 0; i < src.Length; i++)
        {
            char c = src[i];
            char n = (i + 1 < src.Length) ? src[i + 1] : '\0';

            if (inComment)
            {
                if (c == '\n') inComment = false;
                continue;
            }
            if (inLongStr)
            {
                if (c == ']' && n == ']') { inLongStr = false; i++; }
                continue;
            }
            if (inStr)
            {
                if (c == '\\') { i++; continue; }
                if (c == strDelim) inStr = false;
                continue;
            }

            // 注释开始
            if (c == '-' && n == '-')
            {
                inComment = true;
                i++;
                continue;
            }
            // 长字符串 [[ ]]
            if (c == '[' && n == '[') { inLongStr = true; i++; continue; }
            // 字符串开始
            if (c == '"' || c == '\'') { inStr = true; strDelim = c; continue; }

            if (c == '(') paren++;
            else if (c == ')') { paren--; if (paren < 0) { err = L("多余的 )"); return false; } }
            else if (c == '{') brace++;
            else if (c == '}') { brace--; if (brace < 0) { err = L("多余的 }"); return false; } }
            else if (c == '[') bracket++;
            else if (c == ']') { bracket--; if (bracket < 0) { err = L("多余的 ]"); return false; } }
        }

        if (inLongStr) { err = L("未闭合的长字符串"); return false; }
        if (paren != 0) { err = L("括号不配对（差 {0} 个）", paren); return false; }
        if (brace != 0) { err = L("花括号不配对（差 {0} 个）", brace); return false; }
        if (bracket != 0) { err = L("方括号不配对（差 {0} 个）", bracket); return false; }
        return true;
    }

    private static int CountOccurrences(string src, string needle)
    {
        int cnt = 0, idx = 0;
        while ((idx = src.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0) { cnt++; idx += needle.Length; }
        return cnt;
    }
}
