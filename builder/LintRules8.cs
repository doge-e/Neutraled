using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>lint 规则扩展（第八批，C4 P1-11 收尾）—— 前端调用 builder 的开关必须真实存在。
///
/// 背景（A3 审计 F19/R8）：GUI 与 Studio 通过 <c>RunBuilderAsync("--xxx")</c> 拼参数调用 builder。
/// 以前这类字符串没有任何校验：builder 改了开关名、或前端手滑多打一个字母，都会被参数校验判成
/// 「未知开关」→ 前端功能静默失效，而 <c>--lint</c> 全绿。C4 P0-4 起 builder 对未知开关 exit 2，
/// 本规则把这类错误提前到 lint 阶段暴露。
///
/// <list type="number">
/// <item>规则 20：gui/ 与 studio/ 里 builder 调用行（含 RunBuilder / RunBuilderAsync / Arguments =）中的
/// "--xxx" 字面量必须在 CliCatalog.Known 中存在，否则报错。</item>
/// </list>
/// 例外：确属非 builder 参数（例如前端自己的 --headless 之类）且恰好出现在这些行，加行尾标记
/// <c>// ntl:switch-ok</c> 放行。</summary>
public static class LintRules8
{
    public static void Run(string neutraledRoot, List<Lint.Issue> issues)
    {
        foreach (var dir in new[] { "gui", "studio" })
        {
            var root = Path.Combine(neutraledRoot, dir);
            if (!Directory.Exists(root)) continue;
            foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(neutraledRoot, file).Replace('\\', '/');
                // 跳过构建产物（obj/bin 里是生成代码，不是产品源码）
                if (rel.Contains("/obj/") || rel.Contains("/bin/")) continue;
                string[] lines;
                try { lines = File.ReadAllLines(file); } catch { continue; }
                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (!line.Contains("RunBuilder", StringComparison.Ordinal) &&
                        !line.Contains("Arguments =", StringComparison.Ordinal)) continue;
                    if (line.Contains("ntl:switch-ok", StringComparison.Ordinal)) continue;
                    foreach (var token in ExtractSwitches(line))
                    {
                        if (CliCatalog.IsKnown(token)) continue;
                        issues.Add(new Lint.Issue
                        {
                            File = rel,
                            Line = i + 1,
                            Rule = L("开关不存在"),
                            Message = L("调用了命令行目录里不存在的开关 {0}（C4 P1-11：前端调 builder 的开关必须真实存在，"
                                + "否则运行时会 exit 2 且功能静默失效）。确认不是 builder 参数就加行尾 // ntl:switch-ok", token),
                            IsError = true,
                        });
                    }
                }
            }
        }
    }

    /// <summary>从一行源码里抽出 "--xxx" 形式的字符串字面量（保持出现顺序，去重）。</summary>
    private static IEnumerable<string> ExtractSwitches(string line)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var i = 0;
        while (i < line.Length)
        {
            var q = line.IndexOf('"', i);
            if (q < 0) yield break;
            var end = line.IndexOf('"', q + 1);
            if (end < 0) yield break;
            var lit = line.Substring(q + 1, end - q - 1);
            i = end + 1;
            if (lit.Length < 3 || lit[0] != '-' || lit[1] != '-') continue;
            var ok = true;
            for (var k = 2; k < lit.Length; k++)
            {
                var ch = lit[k];
                if (!(ch >= 'a' && ch <= 'z') && !(ch >= '0' && ch <= '9') && ch != '-') { ok = false; break; }
            }
            if (ok && seen.Add(lit)) yield return lit;
        }
    }
}
