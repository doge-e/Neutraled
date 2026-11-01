using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>lint 规则扩展（第七批，C4 P1-9）—— GUI/Studio 不许再写死章节默认值。
///
/// 背景（A3 审计 F01）：GUI 有 9 处 `?? "chapter4"` 兜底，Studio 的「部署」按钮永远部署第 4 章；
/// 结果是玩家选了 `all` 或什么都没选时，会**静默**降级成第 4 章（看起来像 bug，但没有任何提示）。
/// C4 P0-2 把 builder 的默认目标改成 root，本规则防止 GUI/Studio 再长出同类写死默认值。
///
/// <list type="number">
/// <item>规则 19：gui/ 与 studio/ 的 .cs 源码里出现字符串字面量 "chapter4" ⇒ 报错。</item>
/// </list>
/// 例外：章节下拉列表这类「显式枚举」是合法的，在该行行尾加注释标记 `// ntl:chapter4-ok` 放行。</summary>
public static class LintRules7
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
                    if (!line.Contains("\"chapter4\"", StringComparison.Ordinal)) continue;
                    if (line.Contains("ntl:chapter4-ok", StringComparison.Ordinal)) continue;
                    issues.Add(new Lint.Issue
                    {
                        File = rel,
                        Line = i + 1,
                        Rule = L("写死章节默认值"),
                        Message = L("写死了 \"chapter4\" 兜底（C4 P1-9：禁止 all/空值静默降级成第 4 章）。"
                            + "要章节就显式传/选；确属合法列表请在行尾加 // ntl:chapter4-ok"),
                        IsError = true,
                    });
                }
            }
        }
    }
}
