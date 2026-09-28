using System.Text;
using System.Text.RegularExpressions;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>API 文档自动生成（--api-doc）
///
/// 从 api/*.gml 的文件头注释块提取签名和说明，生成 Markdown 参考文档。
/// 好处：文档与代码同步（手写的文档一定会过期）。
///
/// 识别格式：
///   /// ntl_foo(a, b) —— 说明
///   /// 用法：
///   ///   ntl_foo(1, 2)
/// </summary>
public static class ApiDoc
{
    public sealed class Api
    {
        public string Name = "";
        public string Signature = "";
        public string Summary = "";
        public List<string> Body = new();
        public string Group = "";
        public int Lines;
        public bool IsPublic;
    }

    public static int Generate(string gameRoot, string? outPath = null)
    {
        var api = Path.Combine(Paths.NeutraledRoot(gameRoot), "api");
        if (!Directory.Exists(api)) { Console.WriteLine(L("[错误] 找不到 api/")); return 1; }

        var apis = new List<Api>();
        foreach (var f in Directory.GetFiles(api, "*.gml", SearchOption.AllDirectories).OrderBy(x => x))
        {
            if (f.Contains(Path.Combine("api", "events"))) continue;
            var name = Path.GetFileNameWithoutExtension(f);

            var lines = File.ReadAllLines(f);
            var a = new Api { Name = name, Lines = lines.Length, Group = GroupOf(name) };

            // 提取头部注释块（连续的 /// 或 // 开头）
            var head = new List<string>();
            foreach (var ln in lines.Take(40))
            {
                var t = ln.TrimStart();
                if (t.StartsWith("///")) head.Add(t.Substring(3).Trim());
                else if (t.StartsWith("//")) head.Add(t.Substring(2).Trim());
                else if (t.Length == 0 && head.Count > 0) break;
                else if (head.Count > 0 && !t.StartsWith("//")) break;
            }

            if (head.Count == 0)
            {
                // 没注释 → 标记为内部 API
                a.IsPublic = false;
                a.Summary = "（无文档）";
            }
            else
            {
                var first = head[0];
                // 签名：以函数名开头
                if (first.StartsWith(name))
                {
                    var dashIdx = first.IndexOf('—');
                    if (dashIdx < 0) dashIdx = first.IndexOf("--", StringComparison.Ordinal);
                    if (dashIdx > 0)
                    {
                        a.Signature = first.Substring(0, dashIdx).Trim();
                        a.Summary = first.Substring(dashIdx).TrimStart('—', '-', ' ');
                    }
                    else
                    {
                        a.Signature = first;
                        a.Summary = "";
                    }
                }
                else
                {
                    a.Signature = name;
                    a.Summary = first;
                }
                a.Body = head.Skip(1).ToList();
                // 公开 API：名字不以 _ 开头，且注释里有说明
                a.IsPublic = !name.StartsWith("_") && a.Summary.Length > 0;
            }

            apis.Add(a);
        }

        // 生成 Markdown
        var sb = new StringBuilder();
        sb.AppendLine("# Neutraled API 参考");
        sb.AppendLine();
        sb.AppendLine($"> 自动生成于 {DateTime.Now:yyyy-MM-dd HH:mm}（从 api/*.gml 的注释块提取）");
        sb.AppendLine($"> 共 {apis.Count} 个脚本，其中 {apis.Count(x => x.IsPublic)} 个有文档");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        foreach (var grp in apis.Where(x => x.IsPublic).GroupBy(x => x.Group).OrderByDescending(g => g.Count()))
        {
            sb.AppendLine($"## {grp.Key}（{grp.Count()}）");
            sb.AppendLine();
            foreach (var a in grp.OrderBy(x => x.Name))
            {
                sb.AppendLine($"### `{a.Signature}`");
                sb.AppendLine();
                sb.AppendLine(a.Summary);
                sb.AppendLine();
                if (a.Body.Count > 0)
                {
                    // 保留代码示例（缩进的行）
                    bool inCode = false;
                    foreach (var b in a.Body.Take(20))
                    {
                        if (b.Length == 0) { if (inCode) { sb.AppendLine("```"); inCode = false; } sb.AppendLine(); continue; }
                        bool isCode = b.StartsWith("  ") || b.StartsWith("ntl_") || b.Contains("(") && b.Contains(")");
                        if (isCode && !inCode) { sb.AppendLine("```gml"); inCode = true; }
                        if (!isCode && inCode) { sb.AppendLine("```"); inCode = false; }
                        sb.AppendLine(isCode ? b.TrimStart() : b);
                    }
                    if (inCode) sb.AppendLine("```");
                    sb.AppendLine();
                }
            }
            sb.AppendLine("---");
            sb.AppendLine();
        }

        // 附录：无文档的 API
        var undocumented = apis.Where(x => !x.IsPublic).ToList();
        if (undocumented.Count > 0)
        {
            sb.AppendLine($"## 附录：无文档脚本（{undocumented.Count}）");
            sb.AppendLine();
            sb.AppendLine("这些是内部实现，一般不直接调用：");
            sb.AppendLine();
            foreach (var grp in undocumented.GroupBy(x => x.Group).OrderBy(g => g.Key))
                sb.AppendLine($"- **{grp.Key}**：{grp.Count()} 个 — {string.Join(", ", grp.Take(8).Select(x => x.Name))}{(grp.Count() > 8 ? " ..." : "")}");
            sb.AppendLine();
        }

        var target = outPath ?? Path.Combine(Paths.NeutraledRoot(gameRoot), "docs", "API_REFERENCE.md");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, sb.ToString());

        Console.WriteLine(L("===== API 文档生成 ====="));
        Console.WriteLine(L("  脚本总数: {0}", apis.Count));
        Console.WriteLine(L("  有文档:   {0}", apis.Count(x => x.IsPublic)));
        Console.WriteLine(L("  无文档:   {0}", undocumented.Count));
        Console.WriteLine();
        Console.WriteLine(L("  分组:"));
        foreach (var g in apis.GroupBy(x => x.Group).OrderByDescending(g => g.Count()))
            Console.WriteLine(L("    {0,-16} {1,4} 个（{2} 有文档）", g.Key, g.Count(), g.Count(x => x.IsPublic)));
        Console.WriteLine();
        Console.WriteLine(L("  输出: {0}", target));
        return 0;
    }

    private static string GroupOf(string name)
    {
        if (name.StartsWith("ntl_lua_")) return "Lua 解释器";
        if (name.StartsWith("ntl_kristal_")) return "Kristal 兼容";
        if (name.StartsWith("ntl_love_")) return "LOVE2D 桥接";
        if (name.StartsWith("ntl_map_")) return "地图";
        if (name.StartsWith("ntl_player_")) return "玩家";
        if (name.StartsWith("ntl_obj_")) return "对象";
        if (name.StartsWith("ntl_console_")) return "控制台";
        if (name.StartsWith("ntl_hook_") || name.StartsWith("ntl_bh_") || name.StartsWith("ntl_oev_")) return "Hook 系统";
        if (name.StartsWith("ntl_mod_") || name.StartsWith("ntl_shared_") || name.StartsWith("ntl_asset_")) return "mod 互操作";
        if (name.StartsWith("ntl_inst_")) return "实例操作";
        if (name.StartsWith("ntl_sprite_")) return "资源操作";
        if (name.StartsWith("ntl_loop_")) return "主循环";
        if (name.StartsWith("ntl_perf_") || name.StartsWith("ntl_res_")) return "性能/资源";
        if (name.StartsWith("ntl_i18n") || name.StartsWith("ntl_t") || name.StartsWith("ntl_lang")) return "国际化";
        if (name.StartsWith("ntl_json_") || name.StartsWith("ntl_string_") || name.StartsWith("ntl_dir_") || name.StartsWith("ntl_file_") || name.StartsWith("ntl_ensure_")) return "工具函数";
        if (name.StartsWith("ntl_live_")) return "live 运行时";
        if (name.StartsWith("ntl_ns_")) return "命名空间";
        if (name.StartsWith("ntl_err_")) return "错误处理";
        return "其他";
    }
}
