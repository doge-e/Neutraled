using UndertaleModLib;
using UndertaleModLib.Util;
using ImageMagick;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>列出 data.win 里的字体与度量，并导出纹理页样张（分析原版字体风格用）。</summary>
public static class FontProbe
{
    public static int Run(string winPath, string outDir)
    {
        Directory.CreateDirectory(outDir);
        if (string.IsNullOrWhiteSpace(winPath) || !File.Exists(winPath))
        {
            Console.WriteLine(L("  [错误] 找不到 {0}", string.IsNullOrWhiteSpace(winPath) ? "data.win" : winPath));
            return 2;
        }
        var data = Injector.Load(winPath);
        Console.WriteLine(L("字体数: {0}", data.Fonts.Count));
        foreach (var f in data.Fonts)
        {
            Console.WriteLine($"  {f.Name?.Content}  EmSize={f.EmSize} LineHeight={f.LineHeight} Ascender={f.Ascender} " +
                              $"Scale=({f.ScaleX},{f.ScaleY}) AA={f.AntiAliasing} Glyphs={f.Glyphs.Count} Charset={f.Charset}");
            var g0 = f.Glyphs.FirstOrDefault(g => g.Character == 'A') ?? f.Glyphs.FirstOrDefault();
            if (g0 != null)
                Console.WriteLine(L("      样例 '{0}': {1}x{2} @({3},{4}) Shift={5} Offset={6}", (char)g0.Character, g0.SourceWidth, g0.SourceHeight, g0.SourceX, g0.SourceY, g0.Shift, g0.Offset));
            // 纹理页几何（诊断用）：Glyph 矩形最终是按这一页解释的，Scaled 决定运行期是否按 2 倍读
            var tpi0 = f.Texture;
            var tex0 = tpi0?.TexturePage;
            if (tpi0 != null)
                Console.WriteLine($"      TPI src=({tpi0.SourceX},{tpi0.SourceY}) {tpi0.SourceWidth}x{tpi0.SourceHeight} tgt=({tpi0.TargetX},{tpi0.TargetY}) {tpi0.TargetWidth}x{tpi0.TargetHeight} bound={tpi0.BoundingWidth}x{tpi0.BoundingHeight}");
            if (tex0 != null)
                Console.WriteLine($"      TEX {tex0.Name?.Content} {tex0.TextureWidth}x{tex0.TextureHeight} Scaled={tex0.Scaled} Mips={tex0.GeneratedMips} External={tex0.TextureExternal}");
            var im0 = tex0?.TextureData?.Image;
            if (im0 != null)
                Console.WriteLine($"      IMG 存储位图 {im0.Width}x{im0.Height}（声明 {tex0!.TextureWidth}x{tex0.TextureHeight}；比值 {tex0.TextureWidth / (double)Math.Max(1, im0.Width):0.###}）");
        }
        foreach (var f in data.Fonts)
        {
            var pg = f.Texture?.TexturePage;
            var gm = pg?.TextureData?.Image;
            if (gm == null) continue;
            try
            {
                using var mi = gm.GetMagickImage();
                var p = Path.Combine(outDir, "font_" + Sanitize(f.Name?.Content ?? "x") + ".png");
                mi.Write(p, MagickFormat.Png32);
                Console.WriteLine(L("样张: {0}  {1}x{2}", Path.GetFileName(p), mi.Width, mi.Height));
            }
            catch (Exception ex) { Console.WriteLine(L("  样张失败 {0}: {1}", f.Name?.Content, ex.Message)); }
        }
        // 导出各字体的字符表（供覆盖率分析）
        foreach (var f in data.Fonts)
        {
            var name = Sanitize(f.Name?.Content ?? "x");
            var codes = f.Glyphs.Select(g => (int)g.Character).OrderBy(x => x).ToList();
            File.WriteAllText(Path.Combine(outDir, "chars_" + name + ".txt"), string.Join(",", codes));
        }
        // diag: 关键字形记录 + 字形表自检（临时诊断，只读）
        foreach (var f in data.Fonts)
        {
            var nm = f.Name?.Content ?? "?";
            // diag2: 遍历全部字体，找出谁有 Latin-1 / 西里尔
            int inversions = 0; ushort prevCh = 0;
            var dupChars = new List<int>();
            var seenCh = new HashSet<int>();
            foreach (var g in f.Glyphs)
            {
                if (g.Character < prevCh) inversions++;
                prevCh = g.Character;
                if (!seenCh.Add(g.Character)) dupChars.Add(g.Character);
            }
            Console.WriteLine($"  [自检] {nm}: 字形 {f.Glyphs.Count} 逆序对 {inversions} 重复Char {dupChars.Count}" +
                              (dupChars.Count > 0 ? " 例: " + string.Join(",", dupChars.Take(8).Select(c => "U+" + c.ToString("X4"))) : ""));
            bool interesting = f.Glyphs.Any(x => x.Character == 0xE4 || x.Character == 0x416 || x.Character == 0x8FD9);
            if (!nm.StartsWith("ntl_font") && !interesting) continue;
            foreach (var cp in new int[] { 0x41, 0x75, 0x67, 0xE4, 0xFC, 0xDF, 0x430, 0x432, 0x7BC0, 0x8FD9, 0x662F, 0x4F46, 0x82E5, 0x5E73, 0x8861, 0x88AB })
            {
                var gl = f.Glyphs.FirstOrDefault(x => x.Character == cp);
                if (gl == null) { Console.WriteLine($"        缺失 U+{cp:X4}"); continue; }
                Console.WriteLine($"        U+{cp:X4} -> {gl.SourceWidth}x{gl.SourceHeight} @({gl.SourceX},{gl.SourceY}) Shift={gl.Shift} Offset={gl.Offset}");
            }
        }
        // 字形矩形表（供「探针导出页 + 逐格墨迹」外部校验：验证部署后的字体里真的画得出来）
        foreach (var f in data.Fonts)
        {
            var gname = Sanitize(f.Name?.Content ?? "x");
            // sh/of = Shift/Offset：部署期的本机字形覆盖要按 Shift 排版，探针必须把它们带出来
            var rows = f.Glyphs.Select(g => "{\"c\":" + (int)g.Character + ",\"x\":" + g.SourceX + ",\"y\":" + g.SourceY +
                ",\"w\":" + g.SourceWidth + ",\"h\":" + g.SourceHeight + ",\"sh\":" + g.Shift + ",\"of\":" + g.Offset + "}");
            File.WriteAllText(Path.Combine(outDir, "glyphs_" + gname + ".json"), "[" + string.Join(",", rows) + "]");
        }
        Console.WriteLine(L("字符表已导出 chars_*.txt"));
        return 0;
    }
    private static string Sanitize(string s) => new string(s.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
}
