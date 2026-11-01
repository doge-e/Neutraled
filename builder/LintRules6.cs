using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>lint 规则扩展（第六批，C4 P1-1）—— 命令行目录防回归。
///
/// 背景（A3 审计）：1.0.7 有 182 个开关，其中 63 个不在 --help、47 个不在 docs、26 个两边都无；
/// 根因是「合法开关」和「开关说明」各写各的。C4 把两者分别收敛到
/// <see cref="CliCatalog.KnownNames"/>（合法）与 <see cref="CliCatalogDetails.All"/>（说明），
/// 本规则保证两份不漂移：
/// <list type="number">
/// <item>规则 18a：Known 里的开关必须有目录条目（否则 --help all 看不到它）；</item>
/// <item>规则 18b：目录条目必须对应 Known 开关（否则用户按它输入会被判「未知开关」）；</item>
/// <item>规则 18c：同名目录条目只能有一条。</item>
/// </list></summary>
public static class LintRules6
{
    public static void Run(List<Lint.Issue> issues)
    {
        var known = new HashSet<string>(CliCatalog.KnownNames, StringComparer.Ordinal);
        var catalog = new HashSet<string>(CliCatalogDetails.All.Select(e => e.Name), StringComparer.Ordinal);

        foreach (var name in known.Where(k => !catalog.Contains(k)).OrderBy(k => k, StringComparer.Ordinal))
        {
            issues.Add(new Lint.Issue
            {
                File = "CliCatalogDetails.cs",
                Line = 1,
                Rule = L("命令行目录缺条目"),
                Message = L("开关 {0} 在 CliCatalog.Known 里，但 CliCatalogDetails 没有对应条目（--help all 里看不到它）", name),
                IsError = true,
            });
        }

        foreach (var name in catalog.Where(k => !known.Contains(k)).OrderBy(k => k, StringComparer.Ordinal))
        {
            issues.Add(new Lint.Issue
            {
                File = "CliCatalogDetails.cs",
                Line = 1,
                Rule = L("命令行目录多条目"),
                Message = L("开关 {0} 有目录条目，但不在 CliCatalog.Known（用户按它输入会被判未知开关）", name),
                IsError = true,
            });
        }

        foreach (var dup in CliCatalogDetails.All.GroupBy(e => e.Name).Where(g => g.Count() > 1)
                     .Select(g => g.Key).OrderBy(k => k, StringComparer.Ordinal))
        {
            issues.Add(new Lint.Issue
            {
                File = "CliCatalogDetails.cs",
                Line = 1,
                Rule = L("命令行目录重复条目"),
                Message = L("开关 {0} 在 CliCatalogDetails 里登记了多次", dup),
                IsError = true,
            });
        }
    }
}
