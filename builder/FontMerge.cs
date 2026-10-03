using ImageMagick;
using UndertaleModLib.Util;
using UndertaleModLib;
using UndertaleModLib.Models;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>字体补全（"font 字体支持"）：把内置字体包 ntl_font_cjk 的字形补进**游戏自己的字体**。
///
/// 为什么需要：本地化组只交文本（lang/lang_*.json），不交字体；而原版 fnt_main 只有 96 个 ASCII 字形。
/// 汉化包把 lang_en.json 换成中文后，中文文本在游戏里**一个字都画不出来**（第一章传说文本整片空白
/// 的事故就是这么来的）。以往只能靠"整包 data.win 型 mod"顺带带上字体，纯文本 mod 无解。
///
/// 做法（页面保持 2048 见方，和原版/汉化包的字体页同规格）：
///   1) 把旧字形像素的**紧致包围盒**裁出来贴到新页 (0,0)，旧字形坐标整体减去包围盒原点（重定基）；
///   2) 缺失字形从内置字体包页裁下来，按 EmSize 比例缩放后货架式摆进包围盒后面的空白区；
///   3) 字形记录追加后**按 Character 升序整表重排**（GMS2 运行期对字形表二分查找，乱序会成片丢字，
///      见 FontImport.UpsertFont 的注释）。
/// 教训（2026-09-27 真机）：把 2048x2048 的旧页整页贴在 (0,0)、新字形排到 y>=2048 的 2048x4096
/// 长页上时，中文**画得出来但取样是错的**（字形来自图集别处）⇒ 字体页不要超过 2048 见方。
/// 调试开关：NTL_FONTMERGE_MAXSIDE（默认 2048，可设 4096）、NTL_FONTMERGE_SCALED（默认 1）、
/// NTL_FONTMERGE_FITBOX（默认 1：补进来的字形不许超过目标字体原有字形盒高，见 Complete 里的「限高」注释）。
/// 行盒开关（1.0.7）：config.json 的 font_linebox_extra（或环境变量 NTL_FONTMERGE_LINEBOX_EXTRA）= 行盒额外像素 N，
/// **默认 0 = 1.0.6 行为、逐像素一致**；N>0 时把限高上限抬到「原版盒高 + N」，行距随之松 N 像素（见 Complete 里 ceilH 的注释）。
/// 触发条件是"真的缺"：纯 ASCII 产物的目标字体一个字形都不用补 ⇒ 产物与以前完全一致。
/// </summary>
public static class FontMerge
{
    /// <summary>默认要补的目标字体（第一章传说用的是 fnt_legend，正文/菜单用 fnt_main）。</summary>
    public static readonly string[] DefaultTargets = { "fnt_main", "fnt_mainbig", "fnt_small", "fnt_legend" };
    private const string PackName = "ntl_font_cjk";
    private const long MaxPagePixels = 18L * 1024 * 1024;   // 单页像素上限（约 4096x4096 级别），超了就跳过并告警

    /// <summary>部署期入口：取「本产物要画的字符」→ 补进游戏字体。返回补进去的字形数。</summary>
    public static int Run(UndertaleData data, string gameRoot, string chapter, List<ModEntry> mods)
    {
        try
        {
            if (data.Fonts.Count == 0) return 0;
            var win = Paths.ChapterDataWin(gameRoot, chapter);
            var (needed, source) = ContentCheck.NeedChars(gameRoot, chapter, win);
            var extra = CollectModChars(mods);
            if (extra.Count > 0)
            {
                foreach (var c in extra) needed.Add(c);
                source += L(" + mod 的 files/ 语言文件");
            }
            if (needed.Count == 0) { Paths.Log(L("    字体补全: 没有取到文本，跳过")); return 0; }
            Paths.Log(L("    字体补全: 文本来源 {0}（{1} 个字符）", source, needed.Count));
            int linebox = ResolveLineboxExtra(gameRoot);
            if (linebox > 0) Paths.Log(L("    字体行盒: +{0}px（font_linebox_extra）", linebox));
            return Complete(data, needed, null, linebox);
        }
        catch (Exception ex)
        {
            Paths.Log(L("    [警告] 字体补全失败（不改动字体）: {0}", ex.Message));
            return 0;
        }
    }

    /// <summary>把 needed 里目标字体缺失、内置包又有的字形补进去；返回补进去的字形总数。</summary>
    /// <param name="lineboxExtra">行盒额外像素（config.json 的 font_linebox_extra / NTL_FONTMERGE_LINEBOX_EXTRA）：
    /// 0 = 1.0.6 行为；N&gt;0 时字形盒高上限抬到「原版盒高 + N」，行距随之松 N 像素。见下面 ceilH 的注释。</param>
    public static int Complete(UndertaleData data, HashSet<char> needed, IEnumerable<string>? targets = null, int lineboxExtra = 0)
    {
        var pack = data.Fonts.FirstOrDefault(f => f.Name?.Content == PackName);
        if (pack == null)
        {
            Paths.Log(L("    字体补全: 内置字体包 {0} 不存在，跳过（可用 --make-cjk-font 生成）", PackName));
            return 0;
        }
        var packGlyph = new Dictionary<int, UndertaleFont.Glyph>();
        foreach (var g in pack.Glyphs) if (!packGlyph.ContainsKey((int)g.Character)) packGlyph[(int)g.Character] = g;
        var packImage = pack.Texture?.TexturePage?.TextureData?.Image;
        if (packImage == null) { Paths.Log(L("    字体补全: 内置字体包没有纹理页，跳过")); return 0; }
        using var srcPage = packImage.GetMagickImage();
        if (srcPage.Width == 0 || srcPage.Height == 0) { Paths.Log(L("    字体补全: 内置字体包纹理页尺寸为 0，跳过")); return 0; }

        int totalAdded = 0;
        foreach (var name in targets ?? DefaultTargets)
        {
            var font = data.Fonts.FirstOrDefault(f => f.Name?.Content == name);
            if (font == null) continue;

            var have = new HashSet<int>();
            foreach (var g in font.Glyphs) have.Add((int)g.Character);
            var miss = new List<UndertaleFont.Glyph>();
            foreach (var c in needed)
                if (!have.Contains(c) && packGlyph.TryGetValue(c, out var pg)) miss.Add(pg);
            if (miss.Count == 0) continue;

            var oldImage = font.Texture?.TexturePage?.TextureData?.Image;
            if (oldImage == null) { Paths.Log(L("    字体补全 {0}: 字体没有纹理页，跳过", name)); continue; }
            var oldTpi = font.Texture;

            double ratio = 1.0;
            if (pack.EmSize > 0 && font.EmSize > 0) ratio = (double)font.EmSize / pack.EmSize;
            if (ratio < 0.5) ratio = 0.5;
            if (ratio > 2.5) ratio = 2.5;

            // ★ 限高（1.0.6）：补进来的字形**不许比目标字体原有的字形更高**。
            //   运行期的行距/定位按「字体的最大字形高」算 —— 我们的 ntl_root_draw.gml 用 string_height 推导条高，
            //   游戏本体 obj_savemenu_Draw_0.gml 也用 (string_height(...) / 4) 定光标；而 string_height 的高度是
            //   **字体级**的（哪怕字符串全是 ASCII）。内置字体包里几个超高字形（U+23F3 ⏳ 22、U+2714/U+2717/U+2718
            //   ✔✗✘ 22、U+AD6D/U+C5B4/U+D55C 국어한 19）会把 fnt_main 的行盒从原版 16 撑到 22
            //   （fnt_mainbig 32->44、fnt_small 7->11）⇒ 真机上就是「行距变大 / 正文压住下一栏 / 光标错位」。
            //   这里把超高字形按同一比例缩进 boxH（w/Shift/Offset 一起缩，保持外观比例）。
            //   汉字本体只有 14 高（< 16）⇒ 完全不受影响，中文渲染与 1.0.5 逐像素相同。
            //   NTL_FONTMERGE_FITBOX=0 关掉（回到 1.0.5 行为）。
            int boxH = 0;
            foreach (var g0 in font.Glyphs) if (g0.SourceHeight > boxH) boxH = (int)g0.SourceHeight;
            bool fitBox = EnvUInt("NTL_FONTMERGE_FITBOX", 1) != 0;
            // ★ 行盒 +N px（1.0.7；默认 N=0 = 1.0.6 逐像素一致）：这三个主字体在原版与汉化包里
            //   LineHeight/Ascender **都是 0**，运行期的行盒 = 字形最大高（fnt_main 16 / mainbig 32 / small 7）。
            //   所以「行距松 N 像素」的做法就是把限高上限从 boxH 抬到 boxH+N：被压进盒里的高字形
            //   （⏳✔✗ 等）随之长到 boxH+N ⇒ 盒高确定性地变成 boxH+N，行距跟着松 N。
            //   为什么不去写 font.LineHeight/Ascender：docs/PIPELINE.md:937 的教训「度量照抄源字体，
            //   自己编会让字形垂直位置偏移」—— Ascender 没法一起编，只改 LineHeight 会把基线比例搞乱。
            int lineN = lineboxExtra > 0 ? lineboxExtra : 0;
            int ceilH = boxH > 0 ? boxH + lineN : 0;   // 0 = 不限高
            int clamped = 0;

            using var oldPage = oldImage.GetMagickImage();
            int oldW = (int)oldPage.Width, oldH = (int)oldPage.Height;
            if (oldW <= 0 || oldH <= 0) { Paths.Log(L("    字体补全 {0}: 原纹理页尺寸异常，跳过", name)); continue; }

            // ★ 旧字形像素的紧致包围盒（绝对页坐标）。字形坐标是相对**旧 TPI 子矩形**的，
            //   把包围盒裁出来贴到新页 (0,0) 后，旧字形只要整体减掉包围盒原点就继续有效
            //   —— 这一步顺带修掉「换 TPI 时不重定基 ⇒ 旧 ASCII 字形指到错像素」的老 bug。
            int ox = oldTpi?.SourceX ?? 0, oy = oldTpi?.SourceY ?? 0;
            int bx0 = int.MaxValue, by0 = int.MaxValue, bx1 = -1, by1 = -1;
            foreach (var g0 in font.Glyphs)
            {
                if (g0.SourceWidth == 0 || g0.SourceHeight == 0) continue;
                int ax = ox + g0.SourceX, ay = oy + g0.SourceY;
                if (ax < bx0) bx0 = ax;
                if (ay < by0) by0 = ay;
                if (ax + g0.SourceWidth > bx1) bx1 = ax + g0.SourceWidth;
                if (ay + g0.SourceHeight > by1) by1 = ay + g0.SourceHeight;
            }
            if (bx1 < 0)                                    // 没有带像素的字形：退回旧 TPI 子矩形
            {
                bx0 = ox; by0 = oy;
                bx1 = ox + (oldTpi?.SourceWidth ?? (ushort)oldW);
                by1 = oy + (oldTpi?.SourceHeight ?? (ushort)oldH);
            }
            bx0 = Math.Max(0, bx0); by0 = Math.Max(0, by0);
            bx1 = Math.Min(oldW, bx1); by1 = Math.Min(oldH, by1);
            int cropW = Math.Max(1, bx1 - bx0), cropH = Math.Max(1, by1 - by0);

            var items = new List<(UndertaleFont.Glyph g, int w, int h, double k)>();
            foreach (var g in miss)
            {
                double k = ratio;
                if (fitBox && ceilH > 0 && g.SourceHeight * ratio > ceilH)
                {
                    k = ratio * ceilH / (g.SourceHeight * ratio);   // 缩到盒高（+N），保持外观比例
                    clamped++;
                }
                int w = Math.Max(1, (int)Math.Round(g.SourceWidth * k));
                int h = Math.Max(1, (int)Math.Round(g.SourceHeight * k));
                if (ceilH > 0 && h > ceilH) h = ceilH;             // 取整兜底：绝不越过盒高（+N）
                items.Add((g, w, h, k));
            }
            items.Sort((a, b) => b.h.CompareTo(a.h));       // 高的排前面，货架更满

            // ★ 页几何（2026-09-27 重做）：本作运行期的字体页就是 2048x2048（原版页与汉化包页都是）。
            //   旧做法「旧页整页贴在 (0,0) + 新字形排在下方的 2048x4096 长页」在真机上取到的是**错像素**
            //   （中文画得出来，但字形是图集里别的东西）⇒ 现在页面限制在 maxSide（默认 2048）见方：
            //   旧字形像素裁到 (0,0)，新字形货架式排在其后；装不下才先加宽（宽页实测可用）再加高。
            int maxSide = EnvInt("NTL_FONTMERGE_MAXSIDE", 2048);
            if (maxSide < 512) maxSide = 512;
            int pageW = NextPow2(Math.Max(cropW, 1024));
            int pageH = NextPow2(Math.Max(cropH, 1024));
            List<(UndertaleFont.Glyph g, int x, int y, int w, int h, double k)> placed = new();
            int skipped = 0;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                placed.Clear();
                skipped = 0;
                int cx = 0, cy = cropH, rowH = 0;
                foreach (var (g, w, h, k) in items)
                {
                    if (cx + w > pageW) { cx = 0; cy += rowH + 1; rowH = 0; }
                    if (cy + h > pageH) { skipped++; continue; }
                    placed.Add((g, cx, cy, w, h, k));
                    cx += w + 1;
                    if (h > rowH) rowH = h;
                }
                if (skipped == 0) break;
                if (pageW < 4096) { pageW = NextPow2(pageW * 2); continue; }
                if (pageH < 4096) { pageH = NextPow2(pageH * 2); continue; }
                break;
            }
            if (placed.Count == 0) { Paths.Log(L("    字体补全 {0}: 新纹理页装不下（跳过 {1} 字形）", name, skipped)); continue; }
            if ((long)pageW * pageH > MaxPagePixels)
            {
                Paths.Log(L("    字体补全 {0}: 需要的新纹理页过大（{1}x{2}），跳过", name, pageW, pageH));
                continue;
            }

            using var canvas = new MagickImage(MagickColors.Transparent, (uint)pageW, (uint)pageH) { Format = MagickFormat.Png32 };
            using (var oldCrop = oldPage.CloneArea(bx0, by0, (uint)cropW, (uint)cropH))
                canvas.Composite(oldCrop, 0, 0, CompositeOperator.Over);
            foreach (var g in font.Glyphs)                  // 旧字形整体重定基到新页原点
            {
                int nx = ox + g.SourceX - bx0; if (nx < 0) nx = 0;
                int ny = oy + g.SourceY - by0; if (ny < 0) ny = 0;
                g.SourceX = (ushort)nx; g.SourceY = (ushort)ny;
            }
            foreach (var (g, x, y, w, h, k) in placed)
            {
                using var crop = srcPage.CloneArea(g.SourceX, g.SourceY, g.SourceWidth, g.SourceHeight);
                if (Math.Abs(k - 1.0) > 0.001)
                {
                    crop.FilterType = FilterType.Point;      // 像素字体：点采样，别糊
                    crop.Resize(new MagickGeometry((uint)w, (uint)h) { IgnoreAspectRatio = true });
                }
                canvas.Composite(crop, x, y, CompositeOperator.Over);
            }

            byte[] png;
            using (var ms = new MemoryStream()) { canvas.Write(ms, MagickFormat.Png32); png = ms.ToArray(); }

            var tex = new UndertaleEmbeddedTexture
            {
                Name = data.Strings.MakeString("ntl_fontmerge_" + Guid.NewGuid().ToString("N")[..8]),
                Scaled = EnvUInt("NTL_FONTMERGE_SCALED", 1),
                GeneratedMips = 0,
                TextureExternal = false,
                TextureWidth = pageW,
                TextureHeight = pageH
            };
            tex.TextureData = new UndertaleEmbeddedTexture.TexData { Image = GMImage.FromPng(png, true) };
            data.EmbeddedTextures.Add(tex);
            var tpi = new UndertaleTexturePageItem
            {
                Name = data.Strings.MakeString("ntl_fontmergetpi_" + Guid.NewGuid().ToString("N")[..8]),
                SourceX = 0, SourceY = 0,
                SourceWidth = (ushort)pageW, SourceHeight = (ushort)pageH,
                TargetX = 0, TargetY = 0,
                TargetWidth = (ushort)pageW, TargetHeight = (ushort)pageH,
                BoundingWidth = (ushort)pageW, BoundingHeight = (ushort)pageH,
                TexturePage = tex
            };
            data.TexturePageItems.Add(tpi);
            font.Texture = tpi;

            foreach (var (g, x, y, w, h, k) in placed)
            {
                font.Glyphs.Add(new UndertaleFont.Glyph
                {
                    Character = g.Character,
                    SourceX = (ushort)x,
                    SourceY = (ushort)y,
                    SourceWidth = (ushort)w,
                    SourceHeight = (ushort)h,
                    Shift = (short)Math.Max(0, Math.Round(g.Shift * k)),
                    Offset = (short)Math.Round(g.Offset * k)
                });
            }
            // ★ 字形表必须按 Character 升序（GMS2 运行期二分查找）
            var all = font.Glyphs.ToList();
            all.Sort((a, b) => a.Character.CompareTo(b.Character));
            font.Glyphs.Clear();
            foreach (var g in all) font.Glyphs.Add(g);

            totalAdded += placed.Count;
            Paths.Log(L("    字体补全 {0}: +{1} 字形（{2} -> {3}；旧页 {4}x{5} 裁到 {6}x{7} -> 新页 {8}x{9}，缩放 {10:0.##}x{11}）",
                name, placed.Count, have.Count, have.Count + placed.Count, oldW, oldH, cropW, cropH, pageW, pageH, ratio,
                skipped > 0 ? L("，另有 {0} 个放不下", skipped) : ""));
            if (fitBox && clamped > 0)
                Paths.Log(lineN > 0
                    ? L("      限高 {0}px（原版盒高 {1} + 行盒 {2}px）：{3} 个超高字形已缩进盒内", ceilH, boxH, lineN, clamped)
                    : L("      限高 {0}px：{1} 个超高字形已缩进盒内（原版字形盒高，行距不变）", boxH, clamped));
        }
        if (totalAdded == 0) Paths.Log(L("    字体补全: 无需补（目标字体已覆盖所需字符）"));
        return totalAdded;
    }

    /// <summary>mod 的 files/ 覆盖里带的语言文件（文件覆盖发生在注入之后，所以这里一并读）。</summary>
    public static HashSet<char> CollectModChars(List<ModEntry> mods)
    {
        var chars = new HashSet<char>();
        foreach (var m in mods)
        {
            var files = Path.Combine(m.Dir, "files");
            if (!Directory.Exists(files)) continue;
            foreach (var f in Directory.GetFiles(files, "lang_*.json", SearchOption.AllDirectories))
            {
                try { ContentCheck.AddJsonStrings(chars, f); } catch { }
            }
        }
        return chars;
    }

    /// <summary>「行盒 +N px」的当前取值（默认 0）：环境变量 NTL_FONTMERGE_LINEBOX_EXTRA 优先
    /// （接受 0，便于临时关掉），否则读游戏根 config.json 的 font_linebox_extra。上限 64：
    /// 再大行距就离谱了，而且被压的字形会跟着长、纹理页更吃紧。</summary>
    public static int ResolveLineboxExtra(string? gameRoot)
    {
        var raw = Environment.GetEnvironmentVariable("NTL_FONTMERGE_LINEBOX_EXTRA");
        if (raw != null && int.TryParse(raw, out var ev) && ev >= 0) return Math.Min(ev, 64);
        if (string.IsNullOrWhiteSpace(gameRoot))
        {
            try { gameRoot = Paths.DetectGameRoot(); } catch { gameRoot = null; }
        }
        int v = 0;
        if (!string.IsNullOrWhiteSpace(gameRoot))
        {
            try { v = ConfigFile.GetInt(gameRoot!, "font_linebox_extra") ?? 0; } catch { v = 0; }
        }
        if (v < 0) v = 0;
        return Math.Min(v, 64);
    }

    /// <summary>签名项（见 Cache.SignatureRaw）：把行盒开关带进缓存键 —— 改 N 后必须重新部署，
    /// 否则 --deploy 会「内容未变」静默跳过、游戏里还是旧行距（1.0.6 在 fast/fonts 上踩过同类坑）。</summary>
    public static string LineboxSig(string? gameRoot) => ResolveLineboxExtra(gameRoot).ToString();

    /// <summary>环境变量整数（调试/实验用；给不出正整数就用默认值）。</summary>
    private static int EnvInt(string name, int def)
    {
        var v = Environment.GetEnvironmentVariable(name);
        return int.TryParse(v, out var n) && n > 0 ? n : def;
    }

    /// <summary>环境变量无符号整数（调试/实验用；给不出就用默认值）。</summary>
    private static uint EnvUInt(string name, uint def)
    {
        var v = Environment.GetEnvironmentVariable(name);
        return uint.TryParse(v, out var n) ? n : def;
    }

    private static int NextPow2(int v)
    {
        int p = 1;
        while (p < v && p < (1 << 15)) p <<= 1;
        return p;
    }
}
