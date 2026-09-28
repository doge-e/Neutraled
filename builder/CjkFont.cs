using System.Text;
using ImageMagick;
using UndertaleModLib;
using UndertaleModLib.Util;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>中文字体生成器：从系统 TTF 生成 Neutraled 可用的 GameMaker 字体资源包。
///
/// 为什么要它：DELTARUNE 自带字体只有 ASCII，Neutraled 控制台/UI 里的中文画不出来（空白/方块）。
/// 产物与 FontImport 的 schema 一致（fonts/&lt;name&gt;.json + sheet PNG），用 **sheet 模式**
/// （一张大图里按 sx/sy/sw/sh 取字形），避免两万个碎 PNG。
///
/// ★ 性能要点：**绝不能一个字一次 label: 渲染** —— ImageMagick 每次都会重读整个字体文件
///   （simhei.ttf 9.5MB），实测 2000 字跑 7 分钟还没完。汉字/全角在字体里步进一致，
///   所以按 **每行 64 字批量渲染再切格**；只有 ASCII/Latin 才逐字渲染（95 个，便宜）。</summary>
public static class CjkFont
{
    private const int SheetW = 4096;
    private const int MaxSheetH = 4096;
    private const int Batch = 64;

    public static List<int> Charset(string kind, string? scanRoot)
    {
        var set = new SortedSet<int>();
        if (kind == "used" && !string.IsNullOrEmpty(scanRoot))
            foreach (var ch in ScanUsed(scanRoot)) set.Add(ch);
        for (int c = 0x20; c <= 0x7E; c++) set.Add(c);
        for (int c = 0xA0; c <= 0xFF; c++) set.Add(c);
        for (int c = 0x2000; c <= 0x206F; c++) set.Add(c);
        for (int c = 0x3000; c <= 0x303F; c++) set.Add(c);
        for (int c = 0xFF00; c <= 0xFFEF; c++) set.Add(c);
        // ---- 多语言界面要用的「非中文脚本」字形 ----
        // ★ 加这一批的直接原因：界面语言切到 ru 后游戏内控制台**整片空白** ——
        //   语言包生效了（文本真是俄语），但字体包里一个西里尔字形都没有，draw_text 画不出东西。
        //   同一原因也影响 日语（假名/汉字缺口）、法语（œ Œ）、符号行（← → ↑ ↓ ■ ⚠）。
        for (int c = 0x100; c <= 0x24F; c++) set.Add(c);    // 拉丁扩展 A/B（œ Œ š ž ł đ ď）
        for (int c = 0x370; c <= 0x3FF; c++) set.Add(c);    // 希腊字母
        for (int c = 0x400; c <= 0x52F; c++) set.Add(c);    // 西里尔字母 + 补充（俄语/乌克兰语）
        for (int c = 0x2190; c <= 0x21FF; c++) set.Add(c);  // 箭头 ← → ↑ ↓
        for (int c = 0x2500; c <= 0x257F; c++) set.Add(c);  // 制表符 ─ │ ┌ ┐
        for (int c = 0x25A0; c <= 0x25FF; c++) set.Add(c);  // 几何图形 ■ □ ● ◆ ▶
        for (int c = 0x2600; c <= 0x26FF; c++) set.Add(c);  // 杂项符号 ⚠ ★
        for (int c = 0x3040; c <= 0x30FF; c++) set.Add(c);  // 平假名 / 片假名（日语界面）
        if (kind == "hangul")                                // 谚文（韩语界面）—— 中文字体没有，得用 malgun.ttf 单独生成
        {
            for (int c = 0x1100; c <= 0x11FF; c++) set.Add(c);
            for (int c = 0x3130; c <= 0x318F; c++) set.Add(c);
            for (int c = 0xAC00; c <= 0xD7A3; c++) set.Add(c);  // 谚文音节 11172 字
            return set.ToList();
        }
        // ---- "extra"：只出「现有字体包里还没有的字」 ----
        // ★ 用途：从汉化 mod 搬来的字体是**简体**字库（3580 字形），繁体中文字（節/擇/內/關/結/換/遊/戲…）
        //   一个都没有 ⇒ 切到 zh-TW 后整片空洞（实测截图）。这里算出缺口集合，用系统 TTF
        //   单独渲一张补充 sheet，再由 _feat/merge-fonts.mjs 追加进主包（**不覆盖**已有字形）。
        if (kind == "extra")
        {
            // ★ 先把"本工程实际用到的字"和"CJK 基本区"并进来，再减掉主包已有的 —— 否则集合是空的
            //   （上半段的 ScanUsed / CJK 两个 range 分别只在 kind=="used" / kind!="used" 时加入，
            //    实测写成只取自 set 时输出「共 0 个字符」而失败）。
            //   只取基本区（0x4E00-0x9FFF）：msyh 对扩展 A 覆盖不全，渲染缺字会得到空框。
            if (!string.IsNullOrEmpty(scanRoot))
                foreach (var ch in ScanUsed(scanRoot)) set.Add(ch);
            for (int c = 0x4E00; c <= 0x9FFF; c++) set.Add(c);
            var have = new HashSet<int>();
            try
            {
                var baseJson = Path.Combine(scanRoot ?? ".", "fonts", "ntl_font_cjk.json");
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(baseJson));
                foreach (var g in doc.RootElement.GetProperty("Glyphs").EnumerateArray())
                    have.Add(g.GetProperty("Char").GetInt32());
                Paths.Log(L("  补充字体: 现有字体包已有 {0} 个字形", have.Count));
            }
            catch (Exception ex) { Paths.Log(L("  补充字体: 读不到已有字形集（按空处理）: ") + ex.Message); }
            set.RemoveWhere(c => have.Contains(c));
            return set.ToList();
        }
        // ---- "latin"：只出「拉丁字母 + 常用标点」，用于拿游戏自带字体（8bitoperator JVE）重渲 ----
        // ★ 原因：我们补充的拉丁字形原来是系统 TTF（雅黑）渲的，和基底汉字库里从原版 fnt_main
        //   搬来的 ASCII（6x13 像素风）**风格不一致** —— 同一个词里 'd','r','c','k' 是游戏风，
        //   'ü' 却是雅黑体，肉眼能看出来（用户提示：游戏用的是 8-Bit Operator JVE）。
        //   这里只取 8bit 确实有的范围（ASCII + Latin-1 + 拉丁扩展 + 常用标点 + €），
        //   西里尔/希腊/箭头/制表/假名/CJK 都留给 scripts 包，避免空字形顶掉好字形。
        if (kind == "latin")
        {
            var lat = new SortedSet<int>();
            for (int c = 0x20; c <= 0x7E; c++) lat.Add(c);
            for (int c = 0xA0; c <= 0x24F; c++) lat.Add(c);
            for (int c = 0x2000; c <= 0x206F; c++) lat.Add(c);
            lat.Add(0x20AC);                                  // €
            return lat.ToList();
        }
        if (kind == "scripts") return set.ToList();             // 只出「非中文脚本」那一批，用于追加进已有中文字体包
        if (kind != "used")
        {
            for (int c = 0x4E00; c <= 0x9FFF; c++) set.Add(c);   // CJK 基本区 20902 字
            for (int c = 0x3400; c <= 0x4DBF; c++) set.Add(c);   // CJK 扩展 A 6592 字
        }
        return set.ToList();
    }

    private static IEnumerable<int> ScanUsed(string root)
    {
        var exts = new[] { ".gml", ".md", ".json", ".txt", ".ntl" };
        var files = new List<string>();
        try
        {
            // ★ 必须包含 lang/：外部语言包里的字（尤其简体之外的字形）如果不在字形集里，
            //   切到那门语言后整片字会画不出来 —— 实测 zh-TW 的 節/擇/內/容/關/結/換/遊/戲 全是空洞。
            foreach (var sub in new[] { "api", "docs", "lang", "themes", "templates", "plugins", "web", "sdk", "scripts", "live", "bside" })
            {
                var d = Path.Combine(root, sub);
                if (Directory.Exists(d)) files.AddRange(Directory.GetFiles(d, "*", SearchOption.AllDirectories));
            }
        }
        catch { }
        var seen = new HashSet<int>();
        foreach (var f in files)
        {
            if (!exts.Contains(Path.GetExtension(f).ToLowerInvariant())) continue;
            string text;
            try { text = File.ReadAllText(f); } catch { continue; }
            foreach (var ch in text)
            {
                if (ch < 0x80) continue;
                // ★ 跳过 UTF-16 代理对（U+D800-U+DFFF）：遍历 string 得到的是 char（UTF-16 码元），
                //   星平面字符（emoji 等，docs/lang 里确实有）会各贡献一个孤立代理码元，
                //   送进 ImageMagick 的 label 会直接抛
                //   "A valid UTF32 value is between 0x000000 and 0x10ffff ... (Parameter 'utf32')"
                //   ⇒ 整个字体生成失败。字体集只收 BMP，代理码元一律跳过。
                if (ch >= 0xD800 && ch <= 0xDFFF) continue;
                if (seen.Add(ch)) yield return ch;
            }
        }
    }

    /// <summary>渲染一批字符，返回切好的小图（步进一致）。</summary>
    private static List<(int ch, MagickImage img, int shift, int w, int h)> RenderBatch(
        List<int> cps, MagickReadSettings rs, bool uniform, int sizePt)
    {
        var outp = new List<(int, MagickImage, int, int, int)>();
        if (cps.Count == 0) return outp;
        if (!uniform)
        {
            int spaceW = Math.Max(3, sizePt / 2 + 1);
            foreach (var cp in cps)
            {
                // ★ 空格必须给固定宽度：label:" " 渲染出来是 0 宽，会导致
                //   英文单词全部粘在一起（实测 "NeutraledConsole[F2close]"）。
                if (cp == 0x20 || cp == 0xA0 || cp == 0x3000)
                {
                    var blank = new MagickImage(MagickColors.Transparent, (uint)spaceW, (uint)Math.Max(1, sizePt));
                    blank.Format = MagickFormat.Png32;
                    outp.Add((cp, blank, spaceW, spaceW, Math.Max(1, sizePt)));
                    continue;
                }
                try
                {
                    var im = new MagickImage("label:" + char.ConvertFromUtf32(cp), rs);
                    if (im.Width == 0 || im.Height == 0) { im.Dispose(); continue; }
                    outp.Add((cp, im, (int)im.Width + 1, (int)im.Width, (int)im.Height));
                }
                catch { }
            }
            return outp;
        }
        var sb = new StringBuilder();
        foreach (var cp in cps) sb.Append(char.ConvertFromUtf32(cp));
        MagickImage row;
        try { row = new MagickImage("label:" + sb, rs); }
        catch { return outp; }
        int step = (int)Math.Round((double)row.Width / cps.Count);
        if (step <= 0) { row.Dispose(); return outp; }
        for (int i = 0; i < cps.Count; i++)
        {
            int x = i * step;
            int w = Math.Min(step, (int)row.Width - x);
            if (w <= 0) break;
            try
            {
                var cell = (MagickImage)row.Clone();
                cell.Crop(new MagickGeometry(x, 0, (uint)w, (uint)row.Height));
                outp.Add((cps[i], cell, step, w, (int)row.Height));
            }
            catch { }
        }
        row.Dispose();
        return outp;
    }

    public static (int glyphs, int sheets) Make(string outDir, string ttf, int size, string charset, string fontName, string? scanRoot)
    {
        if (!File.Exists(ttf)) throw new FileNotFoundException(L("找不到字体文件: ") + ttf);
        Directory.CreateDirectory(outDir);

        var chars = Charset(charset, scanRoot);
        Paths.Log(L("  中文字体: 字符集={0} 共 {1} 个字符，源字体={2}，字号={3}", charset, chars.Count, Path.GetFileName(ttf), size));

        var rs = new MagickReadSettings
        {
            Font = ttf,
            FontPointsize = size,
            BackgroundColor = MagickColors.Transparent,
            FillColor = MagickColors.White
        };

        // 全宽组（汉字/标点/全角，步进一致）与窄组（ASCII/Latin，逐字渲染）
        var wide = chars.Where(c => c >= 0x2E80).ToList();
        var narrow = chars.Where(c => c < 0x2E80).ToList();
        Paths.Log(L("  渲染: 全宽 {0} 个（每批 {1}），窄体 {2} 个", wide.Count, Batch, narrow.Count));

        var items = new List<(int ch, MagickImage img, int shift, int w, int h)>();
        items.AddRange(RenderBatch(narrow, rs, false, size));
        for (int i = 0; i < wide.Count; i += Batch)
        {
            var slice = wide.GetRange(i, Math.Min(Batch, wide.Count - i));
            items.AddRange(RenderBatch(slice, rs, true, size));
            if (i % (Batch * 20) == 0) Paths.Log(L("    …已渲染 {0} 个字形", items.Count));
        }
        if (items.Count == 0) throw new Exception(L("没有渲染出任何字形（字体可能不支持这些字符）"));
        Paths.Log(L("  渲染完成: {0} 个字形", items.Count));

        int lineH = items.Max(x => x.h) + 2;
        int descentPad = Math.Max(1, (int)Math.Round(size * 0.2));

        // 货架式排版
        var plan = new List<List<(int ch, MagickImage img, int x, int y, int shift, int w, int h)>>();
        var cur = new List<(int ch, MagickImage img, int x, int y, int shift, int w, int h)>();
        int cx = 0, cy = 0;
        foreach (var (ch, img, shift, w, h) in items)
        {
            if (cx + shift > SheetW) { cx = 0; cy += lineH + 2; }
            if (cy + lineH > MaxSheetH) { plan.Add(cur); cur = new(); cx = 0; cy = 0; }
            int gy = cy + Math.Max(0, lineH - descentPad - h);
            cur.Add((ch, img, cx, gy, shift, w, h));
            cx += shift;
        }
        if (cur.Count > 0) plan.Add(cur);

        var glyphDefs = new List<GlyphDef>();
        var sheetFiles = new List<string>();
        int sheets = 0;
        for (int si = 0; si < plan.Count; si++)
        {
            var list = plan[si];
            int usedH = list.Max(g => g.y + g.h) + 4;
            int usedW = list.Max(g => g.x + g.w) + 4;
            int pageW = NextPow2(Math.Min(SheetW, Math.Max(1, usedW)));
            int pageH = NextPow2(Math.Min(MaxSheetH, Math.Max(1, usedH)));
            using var canvas = new MagickImage(MagickColors.Transparent, (uint)pageW, (uint)pageH) { Format = MagickFormat.Png32 };
            foreach (var g in list)
                if (g.x + g.w <= pageW && g.y + g.h <= pageH)
                    canvas.Composite(g.img, g.x, g.y, CompositeOperator.Copy);
            var name = $"{fontName}_sheet{si}.png";
            canvas.Write(Path.Combine(outDir, name), MagickFormat.Png32);
            sheetFiles.Add(name);
            foreach (var g in list)
            {
                if (g.x + g.w > pageW || g.y + g.h > pageH) continue;
                glyphDefs.Add(new GlyphDef
                {
                    Char = g.ch, File = "", Sheet = name,
                    SX = g.x, SY = g.y, SW = g.w, SH = g.h,
                    Shift = g.shift, Offset = 0
                });
            }
            Paths.Log(L("  sheet {0}/{1}: {2} 字形, {3}x{4}", si + 1, plan.Count, list.Count, pageW, pageH));
            sheets++;
        }
        foreach (var (_, img, _, _, _) in items) img.Dispose();

        var def = new FontDef
        {
            Name = fontName, EmSize = size, LineHeight = lineH,
            Ascender = (int)Math.Round(size * 0.88), ScaleX = 1, ScaleY = 1, Glyphs = glyphDefs,
            // 单页时走「预拼页」：整张 sheet 直接当纹理页，省掉导入端的裁剪/拼页/重编码
            Page = sheetFiles.Count == 1 ? sheetFiles[0] : ""
        };
        File.WriteAllText(Path.Combine(outDir, fontName + ".json"),
            System.Text.Json.JsonSerializer.Serialize(def, Paths.Json), new UTF8Encoding(false));
        Paths.Log(L("  字体包已写入 {0}（{1} 字形 / {2} 张 sheet）", outDir, glyphDefs.Count, sheets));
        return (glyphDefs.Count, sheets);
    }


    /// <summary>★ 从已有的 data.win 里**搬一个字体**（含字形位图）做成 Neutraled 字体包。
    ///
    /// 用途：中文字形应当与原版同风格 —— 汉化 mod（mods/hanhua）把游戏字体替换成了
    /// 「原版像素风 + 3500+ 常用汉字」的版本（实测 fnt_main 96 → 3580 字形，AA=0、12px、度量一致）。
    /// 直接搬它的字形，比用系统黑体现渲染**风格对得多**。
    ///
    /// 产物同样走「预拼页」格式（一张 PNG + 每字形坐标），导入端 30ms 搞定。</summary>
    public static (int glyphs, int sheets) FromDataWin(string outDir, string winPath, string sourceFont, string packName)
    {
        Directory.CreateDirectory(outDir);
        var data = Injector.Load(winPath);
        UndertaleModLib.Models.UndertaleFont? font = null;
        if (!string.IsNullOrEmpty(sourceFont))
            font = data.Fonts.FirstOrDefault(f => f.Name?.Content == sourceFont);
        if (font == null)
            font = data.Fonts.OrderByDescending(f => f.Glyphs.Count).FirstOrDefault();
        if (font == null) throw new Exception(L("源 data.win 里没有字体"));
        var gm = font.Texture?.TexturePage?.TextureData?.Image;
        if (gm == null) throw new Exception(L("字体 {0} 没有可用的纹理页", font.Name?.Content));

        Paths.Log(L("  源字体: {0}  EmSize={1} AA={2} 字形={3}", font.Name?.Content, font.EmSize, font.AntiAliasing, font.Glyphs.Count));

        // ★ 原样搬运：直接把源纹理页导出成 PNG 当我们的页，字形坐标**一个都不改**。
        //   早期版本把字形重排到新 sheet，结果出现"间距不对 + 引号状杂点"（空格字形指向 0,0，
        //   而 0,0 正好落在一个汉字格上）。重排没有任何好处，只会引入坐标 bug。
        using var srcImg = gm.GetMagickImage();
        int srcW = (int)srcImg.Width, srcH = (int)srcImg.Height;
        if (srcW > SheetW || srcH > MaxSheetH)
            throw new Exception(L("源纹理页 {0}x{1} 超过 {2} 上限，无法原样搬运", srcW, srcH, SheetW));
        var sheetName = packName + "_page.png";
        srcImg.Write(Path.Combine(outDir, sheetName), MagickFormat.Png32);
        Paths.Log(L("  源纹理页已原样导出: {0} {1}x{2}（字形坐标不改）", sheetName, srcW, srcH));

        var glyphs = new List<GlyphDef>();
        var seen = new HashSet<int>();
        int skipped = 0;
        foreach (var g in font.Glyphs)
        {
            int ch = g.Character;
            if (!seen.Add(ch)) continue;
            int w = g.SourceWidth, h = g.SourceHeight;
            if (w <= 0 || h <= 0 || g.SourceX + w > srcW || g.SourceY + h > srcH) { skipped++; continue; }
            glyphs.Add(new GlyphDef
            {
                Char = ch, File = "", Sheet = sheetName,
                SX = g.SourceX, SY = g.SourceY, SW = w, SH = h,
                Shift = g.Shift, Offset = g.Offset
            });
        }
        if (glyphs.Count == 0) throw new Exception(L("一个字形都没搬过来"));
        // 源字体常缺空格／空格过窄（实测 shift=3，画面上像没空格）→ 合成一个透明的 + 加宽
        int _half = Math.Max(3, (int)font.EmSize / 2 + 1);
        var _byCh = new Dictionary<int, int>();
        for (int i = 0; i < glyphs.Count; i++) _byCh[glyphs[i].Char] = i;
        foreach (var sc in new[] { 0x20, 0xA0, 0x3000 })
        {
            int w = sc == 0x3000 ? Math.Max(4, (int)font.EmSize) : _half;
            if (_byCh.TryGetValue(sc, out var idx))
            {
                var gg = glyphs[idx];
                if (gg.Shift < w)
                {
                    glyphs[idx] = new GlyphDef
                    {
                        Char = gg.Char, File = "", Sheet = sheetName,
                        SX = gg.SX, SY = gg.SY, SW = gg.SW, SH = gg.SH,
                        Shift = w, Offset = gg.Offset
                    };
                }
            }
            else
            {
                // 指向页面右下角一块空白（源页一般没占满）
                glyphs.Add(new GlyphDef
                {
                    Char = sc, File = "", Sheet = sheetName,
                    SX = srcW - 1, SY = srcH - 1, SW = 1, SH = 1, Shift = w, Offset = 0
                });
            }
        }

        var def = new FontDef
        {
            Name = packName,
            EmSize = font.EmSize,
            // ★ 度量必须**逐字照搬源字体**（源里是 0 就写 0）：
            //   曾经填了"看起来合理"的 9/18，结果字形垂直位置与原版不一致，画面出现行间抖动。
            LineHeight = (int)font.LineHeight,
            Ascender = (int)font.Ascender,
            ScaleX = font.ScaleX == 0 ? 1 : font.ScaleX,
            ScaleY = font.ScaleY == 0 ? 1 : font.ScaleY,
            Page = sheetName,
            Glyphs = glyphs
        };
        File.WriteAllText(Path.Combine(outDir, packName + ".json"),
            System.Text.Json.JsonSerializer.Serialize(def, Paths.Json), new UTF8Encoding(false));
        Paths.Log(L("  搬字形完成: {0} 个（跳过 {1}），页 {2}x{3}", def.Glyphs.Count, skipped, srcW, srcH));
        return (def.Glyphs.Count, 1);
    }

    private static int NextPow2(int v) { int p = 1; while (p < v) p <<= 1; return p; }
}
