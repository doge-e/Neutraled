using System.Text.RegularExpressions;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>lint 规则扩展（第五批，2026-09-27）—— 资源不许按裸索引写死。
///
/// 新增规则：
///  17. api/**/*.gml 或 live/**/*.gml 里把**精灵索引用数字字面量**写死
///      （draw_sprite(3695, …) / sprite_index = 922 / draw_sprite_ext(3695, …)…）
///      ⇒ 换章节必炸：精灵索引用的是"这一份 data.win 里的序号"，每个章节各不相同。
///
///      真实事故（2026-09-27，用户报"报错了"）：
///      api/ntl_modmenu_page_draw.gml 写死 draw_sprite(3695, 0, _heartXPos, …)
///      （3695 是 **chapter4** 的红心；chapter1 的红心是 922，对照表见 docs/MANAGE.md §11.4）
///      ⇒ chapter1 里一开 Mod 设置面板就弹 Code Error：
///
///          ERROR in action number 1 / of Draw Event / for object obj_darkcontroller:
///          Trying to draw non-existing sprite.
///          at gml_Script_ntl_modmenu_page_draw
///
///      修法：asset_get_index("spr_heart") + sprite_exists 守卫 + 画不出来时的兜底
///      （见 api/ntl_heart_sprite.gml）。同类先例：字体也是按名字取
///      （api/ntl_font_big.gml，主字体索引随语言变化）。
///
/// 例外：文件里出现标记 ntl:raw-asset-ok 时跳过该文件。
/// </summary>
public static class LintRules5
{
    private const string Exempt = "ntl:raw-asset-ok";

    private static readonly Regex BlockComment = new(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex LineComment = new(@"//[^\n]*", RegexOptions.Compiled);
    private static readonly Regex StrLit = new("\"(?:[^\"\\\n]|\\.)*\"", RegexOptions.Compiled);

    /// <summary>资源参数位置（第 1 个实参）或资源属性右边必须是资源名，不能是数字。</summary>
    private static readonly Regex SpriteFnCall = new(
        @"\b(draw_sprite|draw_sprite_ext|draw_sprite_part|draw_sprite_part_ext|draw_sprite_stretched|draw_sprite_stretched_ext|draw_sprite_tiled|draw_sprite_tiled_ext|draw_sprite_pos|draw_sprite_general|sprite_get_width|sprite_get_height|sprite_get_number|sprite_get_name|sprite_get_xoffset|sprite_get_yoffset|draw_sprite_support)\s*\(\s*(\d+)\b",
        RegexOptions.Compiled);
    private static readonly Regex SpriteProp = new(
        @"\b(sprite_index|mask_index)\s*=[^=]\s*(\d+)\b",
        RegexOptions.Compiled);

    public static void Run(string neutraledRoot, List<Lint.Issue> issues)
    {
        var dirs = new[] { Path.Combine(neutraledRoot, "api"), Path.Combine(neutraledRoot, "live") };
        var files = new List<string>();
        foreach (var d in dirs) if (Directory.Exists(d)) files.AddRange(Directory.GetFiles(d, "*.gml", SearchOption.AllDirectories));

        foreach (var f in files)
        {
            var raw = File.ReadAllText(f);
            if (raw.Contains(Exempt)) continue;
            var code = StrLit.Replace(BlockComment.Replace(raw, " "), "\"\"");
            code = LineComment.Replace(code, " ");
            var lines = code.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                Match m = SpriteFnCall.Match(lines[i]);
                if (!m.Success) m = SpriteProp.Match(lines[i]);
                if (!m.Success) continue;
                var idx = m.Groups[2].Value;
                issues.Add(new Lint.Issue
                {
                    File = f, Line = i + 1, Rule = L("资源索引写死"), IsError = true,
                    Message = L("把资源索引 '{0}' 直接写在代码里了。\n", idx) +
                              L("        → 精灵索引每个章节都不一样（chapter4 红心 = 3695、chapter1 红心 = 922，见 docs/MANAGE.md §11.4），\n") +
                              L("          换章节运行时必弹 Code Error：\"Trying to draw non-existing sprite.\"\n") +
                              L("        → 修复：asset_get_index(\"spr_xxx\") + sprite_exists 守卫 + 兜底画法（参考 api/ntl_heart_sprite.gml）\n") +
                              L("        → 确实要按索引写时，在文件里加标记 {0}", Exempt)
                });
            }
        }
    }
}
