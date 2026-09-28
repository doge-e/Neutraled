using System.Text.Json;
using ImageMagick;
using UndertaleModLib;
using UndertaleModLib.Models;
using UndertaleModLib.Util;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

public sealed class GlyphDef
{
    public int Char { get; set; }
    /// <summary>每字形一个 PNG 时的文件名（相对 fontsDir）。sheet / 预拼页模式下留空。</summary>
    public string File { get; set; } = "";
    /// <summary>sheet 模式：字形所在的整页 PNG（相对 fontsDir）。</summary>
    public string Sheet { get; set; } = "";
    public int SX { get; set; }
    public int SY { get; set; }
    public int SW { get; set; }
    public int SH { get; set; }
    /// <summary>字形水平步进。**可空**：不写(null) → 默认用字形宽度；显式写 0 → 就是 0。</summary>
    public int? Shift { get; set; }
    public int Offset { get; set; }
}

public sealed class FontDef
{
    public string Name { get; set; } = "";
    public float EmSize { get; set; } = 16;
    public int LineHeight { get; set; } = 20;
    public int Ascender { get; set; } = 14;
    public float ScaleX { get; set; } = 1;
    public float ScaleY { get; set; } = 1;
    /// <summary>★ 预拼页模式：字形已经排好在一张 PNG 里（生成器自己排的），直接拿它当纹理页 ——
    /// 省掉「逐字形裁剪 + 重新拼页 + PNG 重编码」（实测 28190 字形要 10.6 秒：解码 4.0 + 拼页 1.7 + 重编码 4.9）。
    /// 为空则走老路径（--export-packs 的每字形文件格式）。</summary>
    public string Page { get; set; } = "";
    public List<GlyphDef> Glyphs { get; set; } = new();
}

/// <summary>字体资源包导入（fonts/*.json）。三种来源：
///   1) **预拼页**（Page 字段）：整页 PNG 直接当纹理页 —— 最快，中文字体走这条；
///   2) **sheet 模式**（每字形 Sheet + sx/sy/sw/sh）：重新拼页，兼容手写的包；
///   3) 每字形一个 PNG（File 字段）：--export-packs 导出的格式。
/// 纹理页尺寸按实际用量取 2 的幂（老实现固定 4096×4096 = 67MB 显存）。</summary>
public static class FontImport
{
    private const int MaxPage = 4096;

    public static int Import(UndertaleData data, string fontsDir)
    {
        var jsonFiles = Directory.GetFiles(fontsDir, "*.json", SearchOption.TopDirectoryOnly);
        if (jsonFiles.Length == 0) return 0;

        int done = 0;
        foreach (var jf in jsonFiles)
        {
            FontDef? def;
            try { def = JsonSerializer.Deserialize<FontDef>(File.ReadAllText(jf), Paths.Json); }
            catch (Exception ex) { Paths.Log(L("    [警告] 解析失败 {0}: {1}", Path.GetFileName(jf), ex.Message)); continue; }
            if (def == null || string.IsNullOrEmpty(def.Name) || def.Glyphs.Count == 0) continue;

            // ---------- 1) 预拼页：直接用页图，零图像处理 ----------
            if (!string.IsNullOrEmpty(def.Page))
            {
                var pp = Path.Combine(fontsDir, def.Page);
                if (!File.Exists(pp)) { Paths.Log(L("    [警告] {0}: 找不到页图 {1}", def.Name, def.Page)); continue; }
                var bytes = File.ReadAllBytes(pp);
                int pw = 0, ph = 0;
                if (bytes.Length > 24)
                {
                    pw = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
                    ph = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
                }
                if (pw <= 0 || ph <= 0) { Paths.Log(L("    [警告] {0}: 页图尺寸读不出来", def.Name)); continue; }
                var placed0 = def.Glyphs.Select(g => (g, g.SX, g.SY, g.SW, g.SH)).ToList();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                if (UpsertFont(data, def, bytes, pw, ph, placed0))
                {
                    Paths.Log(L("    字体: {0} {1} 字形，纹理页 {2}x{3}（预拼页直用，{4}ms）", def.Name, placed0.Count, pw, ph, sw.ElapsedMilliseconds));
                    done++;
                }
                continue;
            }

            // ---------- 2/3) 老路径：取字形图 → 排版 → 拼页 ----------
            var sources = new List<(GlyphDef g, MagickImage img)>();
            var sheets = new Dictionary<string, MagickImage>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in def.Glyphs)
            {
                MagickImage? img = null;
                try
                {
                    if (!string.IsNullOrEmpty(g.Sheet))
                    {
                        if (!sheets.TryGetValue(g.Sheet, out var sheet))
                        {
                            var sp = Path.Combine(fontsDir, g.Sheet);
                            if (!File.Exists(sp)) continue;
                            sheet = new MagickImage(sp);
                            sheets[g.Sheet] = sheet;
                        }
                        img = (MagickImage)sheet.Clone();
                        img.Crop(new MagickGeometry(g.SX, g.SY, (uint)Math.Max(1, g.SW), (uint)Math.Max(1, g.SH)));
                    }
                    else
                    {
                        var path = Path.Combine(fontsDir, g.File);
                        if (!File.Exists(path)) continue;
                        img = new MagickImage(path);
                    }
                }
                catch { img?.Dispose(); continue; }
                sources.Add((g, img));
            }
            if (sources.Count == 0) { foreach (var s in sheets.Values) s.Dispose(); continue; }

            int cx = 0, cy = 0, rowH = 0, usedW = 0, usedH = 0;
            var placed = new List<(GlyphDef g, int x, int y, int w, int h)>();
            foreach (var (g, img) in sources)
            {
                int w = (int)img.Width, h = (int)img.Height;
                if (cx + w > MaxPage) { cx = 0; cy += rowH + 1; rowH = 0; }
                placed.Add((g, cx, cy, w, h));
                cx += w + 1;
                if (h > rowH) rowH = h;
                if (cx > usedW) usedW = cx;
                if (cy + h > usedH) usedH = cy + h;
            }
            int pageW = NextPow2(Math.Min(MaxPage, Math.Max(1, usedW)));
            int pageH = NextPow2(Math.Min(MaxPage, Math.Max(1, usedH)));
            using var page = new MagickImage(MagickColors.Transparent, (uint)pageW, (uint)pageH) { Format = MagickFormat.Png32 };
            var kept = new List<(GlyphDef g, int x, int y, int w, int h)>();
            for (int i = 0; i < sources.Count; i++)
            {
                var (g, img) = sources[i];
                var p = placed[i];   // 必须用循环下标：kept.Count 在有字形被裁掉时会与 sources 错位
                if (p.x + p.w > pageW || p.y + p.h > pageH) { img.Dispose(); continue; }
                page.Composite(img, p.x, p.y, CompositeOperator.Copy);
                kept.Add(p);
                img.Dispose();
            }
            foreach (var s in sheets.Values) s.Dispose();
            if (kept.Count == 0) continue;
            var png = page.ToByteArray(MagickFormat.Png32);
            if (UpsertFont(data, def, png, pageW, pageH, kept))
            {
                Paths.Log(L("    字体: {0} {1} 字形，纹理页 {2}x{3}", def.Name, kept.Count, pageW, pageH));
                done++;
            }
        }
        return done;
    }

    /// <summary>写入纹理页 + TPI + 字体资源 + 字形表（两条路径共用）。</summary>
    private static bool UpsertFont(UndertaleData data, FontDef def, byte[] pngBytes, int pageW, int pageH,
                                   List<(GlyphDef g, int x, int y, int w, int h)> placed)
    {
        if (placed.Count == 0) return false;
        var tex = new UndertaleEmbeddedTexture
        {
            Name = data.Strings.MakeString("ntl_font_" + Guid.NewGuid().ToString("N")[..8]),
            Scaled = 1,
            GeneratedMips = 0,
            TextureExternal = false,
            TextureWidth = pageW,
            TextureHeight = pageH
        };
        tex.TextureData = new UndertaleEmbeddedTexture.TexData { Image = GMImage.FromPng(pngBytes, true) };
        data.EmbeddedTextures.Add(tex);

        var tpi = new UndertaleTexturePageItem
        {
            Name = data.Strings.MakeString("ntl_fonttpi_" + Guid.NewGuid().ToString("N")[..8]),
            SourceX = 0, SourceY = 0,
            SourceWidth = (ushort)pageW, SourceHeight = (ushort)pageH,
            TargetX = 0, TargetY = 0,
            TargetWidth = (ushort)pageW, TargetHeight = (ushort)pageH,
            BoundingWidth = (ushort)pageW, BoundingHeight = (ushort)pageH,
            TexturePage = tex
        };
        data.TexturePageItems.Add(tpi);

        var font = data.Fonts.FirstOrDefault(f => f.Name?.Content == def.Name);
        if (font == null)
        {
            font = new UndertaleFont { Name = data.Strings.MakeString(def.Name) };
            data.Fonts.Add(font);
        }
        font.DisplayName = data.Strings.MakeString(def.Name);
        font.EmSize = def.EmSize;
        font.EmSizeIsFloat = false;
        font.Bold = false;
        font.Italic = false;
        font.Charset = 0;
        font.AntiAliasing = 0;
        font.RangeStart = 0;
        font.RangeEnd = 0;
        font.Texture = tpi;
        font.ScaleX = def.ScaleX;
        font.ScaleY = def.ScaleY;
        font.Ascender = (uint)def.Ascender;
        font.LineHeight = (uint)def.LineHeight;
        font.SDFSpread = 0;
        font.AscenderOffset = 0;

        /// 字形表**必须按 Character 升序**写入：GMS2 运行期用一个有序数组二分查找字形，
        /// 乱序时只需两处「块交界」错位，就会让一整批字符（汉字/假名/西里尔）查不到 ⇒ 界面整片空白，
        /// 而 ASCII（表头有序段）与谚文（表尾有序段）却正常。合并多张 sheet 的字体包时尤其要注意。
        placed.Sort((a, b) => a.g.Char.CompareTo(b.g.Char));

        font.Glyphs.Clear();
        foreach (var (g, x, y, w, h) in placed)
        {
            font.Glyphs.Add(new UndertaleFont.Glyph
            {
                Character = (ushort)g.Char,
                SourceX = (ushort)x,
                SourceY = (ushort)y,
                SourceWidth = (ushort)w,
                SourceHeight = (ushort)h,
                Shift = (short)(g.Shift ?? w),
                Offset = (short)g.Offset
            });
        }
        return true;
    }

    private static int NextPow2(int v) { int p = 1; while (p < v) p <<= 1; return p; }
}
