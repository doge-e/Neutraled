using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ImageMagick;
using UndertaleModLib;
using UndertaleModLib.Models;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>资源包导出器 —— SpriteImport / SoundImport / FontImport 的逆操作。
/// 把 data.win 里的精灵/声音/字体导出成 mods/&lt;mod&gt;/&lt;author&gt;/&lt;chapter&gt;/{sprites,sounds,fonts}/ 结构。
///
/// JSON schema 与三个导入器【共用同一批 DTO 类型】（SpriteDef / SpriteFrameDef / SoundDef /
/// FontDef / GlyphDef），因此字段名、类型与导入器天然一致（编译期对齐，不会漂移）。
/// 写入用 Paths.Json 的副本（与读取端同一套选项，附加 WriteIndented）。
///
/// 差异导出（baselineWin != null）判据（宁可多导，不漏导）：
///   精灵：基线无同名 → 导出；宽高/原点/边距/包围盒模式/帧数不同 → 导出；
///         帧的目标位置与尺寸(Source/Target/Bounding)不同 → 导出；以上全同则再比帧像素
///         （按纹理页 SourceX/Y/W/H 裁出的 RGBA 区域做 FNV-1a 64 哈希），像素不同 → 导出；
///         两侧像素都不可得（纹理页解码失败/裁剪越界） → 判定为"有改动" → 导出；
///         两侧同为结构性无像素（帧无 TexturePageItem / 源尺寸 0） → 视为未变（避免把九宫格类精灵导成 1x1 占位图）；
///         基底已有同名资源、但源侧帧结构性无纹理 → 跳过并响亮记录（schema 无法表达，硬导会破坏基底）。
///   声音：基线无同名 → 导出；内嵌音频字节的 (长度, FNV-1a 64) 或 Volume/Pitch/Preload/Type 不同 → 导出。
///   字体：基线无同名 → 导出；EmSize/LineHeight/Ascender/ScaleX/ScaleY/字形数不同 → 导出；
///         字形 (char, 相对纹理页的矩形, shift, offset) 或字形像素哈希不同 → 导出。
///
/// 不导出（不可表达，全部响亮记录）：外部纹理页(TextureExternal)、无内嵌数据的声音、
/// 非普通类型精灵(Spine/SWF/Vector)、0 帧精灵、0 字形字体、基线独有的资源（导入器不支持删除）。</summary>
public static class PackExport
{
    /// <summary>写入 JSON 的选项：基于 Paths.Json（与导入器同一套），附加缩进。</summary>
    private static readonly JsonSerializerOptions JsonOut = new(Paths.Json) { WriteIndented = true };

    /// <summary>纹理页解码缓存条数（2048x2048 RGBA 单页约 16MB，限制峰值内存）。</summary>
    private const int PageCacheLimit = 4;

    /// <summary>每类问题最多打印多少条明细（其余折叠成一行计数，避免刷屏但不是静默）。</summary>
    private const int WarnDetailLimit = 20;

    /// <summary>从 sourceWin 导出资源包到 outDir（sprites/ sounds/ fonts/）。
    /// baselineWin 不为 null 时，只导出"相对基线新增或改动"的资源（差异导出）；
    /// 为 null 时全量导出。返回导出的资源总数（精灵+声音+字体），
    /// 硬失败（参数/文件缺失、data.win 读取失败、异常）返回 -1；单个资源的问题只记录日志不改变返回值。</summary>
    public static int Export(string sourceWin, string? baselineWin, string outDir,
                             bool sprites = true, bool sounds = true, bool fonts = true)
    {
        try
        {
            return ExportCore(sourceWin, baselineWin, outDir, sprites, sounds, fonts);
        }
        catch (Exception ex)
        {
            Paths.Log(L("  [错误] 资源包导出失败: {0}: {1}", ex.GetType().Name, ex.Message));
            return -1;
        }
    }

    private static int ExportCore(string sourceWin, string? baselineWin, string outDir,
                                  bool doSprites, bool doSounds, bool doFonts)
    {
        if (string.IsNullOrWhiteSpace(sourceWin) || !File.Exists(sourceWin))
        {
            Paths.Log(L("  [错误] 源 data.win 不存在: {0}", sourceWin));
            return -1;
        }
        if (string.IsNullOrWhiteSpace(outDir))
        {
            Paths.Log(L("  [错误] 输出目录为空"));
            return -1;
        }
        if (baselineWin != null && !File.Exists(baselineWin))
        {
            Paths.Log(L("  [错误] 基线 data.win 不存在: {0}", baselineWin));
            return -1;
        }
        if (baselineWin != null &&
            string.Equals(Path.GetFullPath(baselineWin), Path.GetFullPath(sourceWin), StringComparison.OrdinalIgnoreCase))
        {
            Paths.Log(L("  [警告] 基线与源是同一个文件 -> 按全量导出"));
            baselineWin = null;
        }

        var sw = Stopwatch.StartNew();
        Paths.Log(baselineWin == null
            ? L("  资源包导出(全量): {0} -> {1}", Path.GetFileName(sourceWin), outDir)
            : L("  资源包导出(差异): {0} - {1} -> {2}", Path.GetFileName(sourceWin), Path.GetFileName(baselineWin), outDir));

        // ---------- 1) 基线指纹（先算完即释放，避免同时驻留两份 data.win） ----------
        Dictionary<string, SpriteSig>? sprBase = null;
        Dictionary<string, FontSig>? fontBase = null;
        Dictionary<string, SoundSig>? sndBase = null;
        if (baselineWin != null)
        {
            var bdata = LoadData(baselineWin);
            Paths.Log(L("  基线载入: 精灵={0} 声音={1} 字体={2}", bdata.Sprites.Count, bdata.Sounds.Count, bdata.Fonts.Count)
                + L(" 纹理页={0} ({1} ms)", bdata.EmbeddedTextures.Count, sw.ElapsedMilliseconds));
            using (var bcache = new PageCache(L("基线")))
            {
                if (doSprites) sprBase = BuildSpriteIndex(bdata, bcache);
                if (doFonts) fontBase = BuildFontIndex(bdata, bcache);
            }
            if (doSounds) sndBase = BuildSoundIndex(bdata);
            bdata.Dispose();
            Paths.Log(L("  基线指纹: 精灵={0} 字体={1} 声音={2}", sprBase?.Count ?? 0, fontBase?.Count ?? 0, sndBase?.Count ?? 0)
                + $" ({sw.ElapsedMilliseconds} ms)");
        }

        // ---------- 2) 载入源并导出 ----------
        var data = LoadData(sourceWin);
        Paths.Log(L("  源载入: 精灵={0} 声音={1} 字体={2}", data.Sprites.Count, data.Sounds.Count, data.Fonts.Count)
            + L(" 纹理页={0} ({1} ms)", data.EmbeddedTextures.Count, sw.ElapsedMilliseconds));

        int total = 0;
        using (var cache = new PageCache(L("源")))
        {
            if (doSprites) total += ExportSprites(data, sprBase, Path.Combine(outDir, "sprites"), cache);
            if (doSounds) total += ExportSounds(data, sndBase, Path.Combine(outDir, "sounds"));
            if (doFonts) total += ExportFonts(data, fontBase, Path.Combine(outDir, "fonts"), cache);
        }
        data.Dispose();

        Paths.Log(L("  导出完成: 合计 {0} 个资源 ({1} ms) -> {2}", total, sw.ElapsedMilliseconds, outDir));
        return total;
    }

    private static UndertaleData LoadData(string path)
    {
        using var fs = File.OpenRead(path);
        return UndertaleIO.Read(fs,
            (w, important) => Paths.Log(L("    [{0}] 读取 {1}: {2}", (important ? L("警告") : L("提示")), Path.GetFileName(path), w)),
            m => { });
    }

    // ==================================================================
    //  精灵
    // ==================================================================

    private static int ExportSprites(UndertaleData data, Dictionary<string, SpriteSig>? baseline,
                                     string dir, PageCache cache)
    {
        Directory.CreateDirectory(dir);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int exported = 0, unchanged = 0, empty = 0, special = 0, badFrames = 0, noTex = 0, warn = 0;
        long scaledFrames = 0, bboxFrames = 0;
        // 差异模式：先一次性建立源指纹（内部按纹理页分组哈希，避免逐精灵反复解码纹理页）
        var sigs = baseline != null ? BuildSpriteIndex(data, cache) : null;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var pendingFrames = new List<(IMagickImage<byte> img, string path, string label)>();
        foreach (var spr in data.Sprites)
        {
            var name = spr.Name?.Content;
            if (string.IsNullOrEmpty(name)) { if (warn++ < WarnDetailLimit) Paths.Log(L("    [警告] 跳过无名精灵")); continue; }
            if (spr.SSpriteType != UndertaleSprite.SpriteType.Normal)
            {
                special++;
                if (warn++ < WarnDetailLimit) Paths.Log(L("    [警告] 精灵 {0} 类型 {1}（Spine/SWF/Vector），资源包 schema 不表达 -> 跳过", name, spr.SSpriteType));
                continue;
            }
            if (spr.Textures.Count == 0) { empty++; continue; }

            SpriteSig? srcSig = null;
            if (baseline != null)
            {
                sigs!.TryGetValue(name, out srcSig);
                if (seen.Add(name) && srcSig != null && baseline.TryGetValue(name, out var b) && SameSig(srcSig, b))
                { unchanged++; continue; }
                // 帧结构性无纹理（九宫格等）且基底已有同名精灵：schema 无法表达，硬导会把基底整帧换成 1x1 -> 跳过并响亮记录
                if (srcSig != null && baseline.ContainsKey(name) && srcSig.Frames.Exists(f => f.NoTpi))
                {
                    noTex++;
                    if (warn++ < WarnDetailLimit) Paths.Log(L("    [警告] 精灵 {0} 有帧没有纹理页条目（结构无法表达）-> 跳过，保留基底原样", name));
                    continue;
                }
            }

            var safe = SafeName(name, used);
            var def = new SpriteDef
            {
                Name = name,
                Width = Clamp(spr.Width),
                Height = Clamp(spr.Height),
                OriginX = spr.OriginX,
                OriginY = spr.OriginY,
                MarginLeft = spr.MarginLeft,
                MarginRight = spr.MarginRight,
                MarginTop = spr.MarginTop,
                MarginBottom = spr.MarginBottom,
                BBoxMode = Clamp(spr.BBoxMode)
            };

            for (int i = 0; i < spr.Textures.Count; i++)
            {
                var tpi = spr.Textures[i].Texture;
                var file = safe + "_f" + i + ".png";
                var full = Path.Combine(dir, file);
                int tx = 0, ty = 0;
                if (tpi == null)
                {
                    badFrames++;
                    if (warn++ < WarnDetailLimit) Paths.Log(L("    [警告] {0} 帧 {1}: 无 TexturePageItem -> 写 1x1 占位图", name, i));
                    WritePlaceholder(full);
                }
                else
                {
                    tx = tpi.TargetX;
                    ty = tpi.TargetY;
                    if (tpi.TargetWidth != tpi.SourceWidth || tpi.TargetHeight != tpi.SourceHeight) scaledFrames++;
                    if (tpi.BoundingWidth != tpi.SourceWidth || tpi.BoundingHeight != tpi.SourceHeight) bboxFrames++;
                    var crop = cache.TryCrop(tpi.TexturePage, tpi.SourceX, tpi.SourceY, tpi.SourceWidth, tpi.SourceHeight, L("{0} 帧 {1}", name, i));
                    if (crop == null) { badFrames++; WritePlaceholder(full); }
                    else pendingFrames.Add((crop, full, L("{0} 帧 {1}", name, i)));
                }
                def.Frames.Add(new SpriteFrameDef { File = file, TargetX = tx, TargetY = ty });
            }

            File.WriteAllText(Path.Combine(dir, safe + ".json"), JsonSerializer.Serialize(def, JsonOut));
            exported++;
        }

        foreach (var e in WritePngsParallel(pendingFrames))
        {
            badFrames++;
            if (warn++ < WarnDetailLimit) Paths.Log(L("    [警告] 写 PNG 失败 {0}", e));
        }

        Paths.Log(L("    精灵: 导出 {0} / 未变 {1} / 0帧 {2} / 非普通类型 {3} / 坏帧 {4} / 无纹理跳过 {5}", exported, unchanged, empty, special, badFrames, noTex));
        if (scaledFrames > 0) Paths.Log(L("    [警告] {0} 帧 TargetWidth/Height != SourceWidth/Height（缩放帧），导入器 schema 不表达该缩放，往返会丢失", scaledFrames));
        if (bboxFrames > 0) Paths.Log(L("    [警告] {0} 帧 BoundingWidth/Height != 源尺寸（包围盒与帧不同），导入器 schema 不表达，往返会丢失", bboxFrames));
        if (warn > WarnDetailLimit) Paths.Log(L("    [警告] 另有 {0} 条精灵警告已折叠", warn - WarnDetailLimit));
        return exported;
    }

    /// <summary>精灵指纹（像素部分只登记待哈希的矩形，稍后按纹理页统一哈希）。</summary>
    private static SpriteSig BuildSpriteSig(UndertaleSprite spr, List<RectNeed>? needs)
    {
        var sig = new SpriteSig
        {
            W = spr.Width, H = spr.Height, Ox = spr.OriginX, Oy = spr.OriginY,
            Ml = spr.MarginLeft, Mr = spr.MarginRight, Mt = spr.MarginTop, Mb = spr.MarginBottom,
            BBox = spr.BBoxMode
        };
        foreach (var te in spr.Textures)
        {
            var fs = new FrameSig();
            var tpi = te.Texture;
            if (tpi == null) fs.NoTpi = true;
            else
            {
                fs.Sw = tpi.SourceWidth; fs.Sh = tpi.SourceHeight;
                fs.Tx = tpi.TargetX; fs.Ty = tpi.TargetY; fs.Tw = tpi.TargetWidth; fs.Th = tpi.TargetHeight;
                fs.Bw = tpi.BoundingWidth; fs.Bh = tpi.BoundingHeight;
                fs.Zero = tpi.SourceWidth <= 0 || tpi.SourceHeight <= 0;
                if (needs != null && !fs.Zero && tpi.TexturePage != null)
                    needs.Add(new RectNeed(tpi.TexturePage, tpi.SourceX, tpi.SourceY, tpi.SourceWidth, tpi.SourceHeight, fs));
            }
            sig.Frames.Add(fs);
        }
        return sig;
    }

    private static Dictionary<string, SpriteSig> BuildSpriteIndex(UndertaleData data, PageCache cache)
    {
        var map = new Dictionary<string, SpriteSig>(StringComparer.Ordinal);
        var needs = new List<RectNeed>();
        int dup = 0;
        foreach (var spr in data.Sprites)
        {
            var name = spr.Name?.Content;
            if (string.IsNullOrEmpty(name) || spr.SSpriteType != UndertaleSprite.SpriteType.Normal) continue;
            if (!map.TryAdd(name, BuildSpriteSig(spr, needs))) dup++;
        }
        HashRects(needs, cache);
        if (dup > 0) Paths.Log(L("    [警告] 有 {0} 个同名精灵，取首个", dup));
        return map;
    }

    private static bool SameSig(SpriteSig a, SpriteSig b)
    {
        if (a.W != b.W || a.H != b.H || a.Ox != b.Ox || a.Oy != b.Oy ||
            a.Ml != b.Ml || a.Mr != b.Mr || a.Mt != b.Mt || a.Mb != b.Mb || a.BBox != b.BBox) return false;
        if (a.Frames.Count != b.Frames.Count) return false;
        for (int i = 0; i < a.Frames.Count; i++)
        {
            var x = a.Frames[i];
            var y = b.Frames[i];
            if (x.Sw != y.Sw || x.Sh != y.Sh || x.Tx != y.Tx || x.Ty != y.Ty ||
                x.Tw != y.Tw || x.Th != y.Th || x.Bw != y.Bw || x.Bh != y.Bh) return false;
            // 结构性无像素（两侧都缺纹理条目/尺寸为 0）：结构一致即视为未变，
            // 否则会把九宫格类精灵整帧导成 1x1 占位图，反过来破坏基底资源。
            if (x.NoTpi || y.NoTpi) { if (x.NoTpi != y.NoTpi) return false; continue; }
            if (x.Zero || y.Zero) { if (x.Zero != y.Zero) return false; continue; }
            if (!x.HasPix || !y.HasPix) return false;   // 像素不可比（纹理页解码失败/越界）-> 认定有改动（宁可多导）
            if (x.Pix != y.Pix) return false;
        }
        return true;
    }

    // ==================================================================
    //  声音
    // ==================================================================

    private static int ExportSounds(UndertaleData data, Dictionary<string, SoundSig>? baseline, string dir)
    {
        Directory.CreateDirectory(dir);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int exported = 0, unchanged = 0, noData = 0, warn = 0;

        foreach (var snd in data.Sounds)
        {
            var name = snd.Name?.Content;
            if (string.IsNullOrEmpty(name)) { if (warn++ < WarnDetailLimit) Paths.Log(L("    [警告] 跳过无名声音")); continue; }
            var bytes = snd.AudioFile?.Data;
            if (bytes == null || bytes.Length == 0)
            {
                noData++;
                if (warn++ < WarnDetailLimit)
                    Paths.Log(L("    [警告] 声音 {0} 无内嵌数据（外部/audiogroup，File={1}）-> 跳过", name, snd.File?.Content));
                continue;
            }

            var type = snd.Type?.Content ?? "";
            var sig = new SoundSig
            {
                Len = bytes.Length, Hash = Hash64(bytes, 0, bytes.Length),
                Volume = snd.Volume, Pitch = snd.Pitch, Preload = snd.Preload, Type = type
            };
            if (baseline != null && baseline.TryGetValue(name, out var b) && b.Equals(sig)) { unchanged++; continue; }

            var (ext, stype) = AudioFormat(bytes, type, name, ref warn);
            var safe = SafeName(name, used);
            var file = safe + ext;
            File.WriteAllBytes(Path.Combine(dir, file), bytes);

            var def = new SoundDef
            {
                Name = name,
                File = file,
                Volume = snd.Volume,
                Pitch = snd.Pitch,
                Type = stype,
                Preload = snd.Preload
            };
            File.WriteAllText(Path.Combine(dir, safe + ".json"), JsonSerializer.Serialize(def, JsonOut));
            exported++;
        }

        Paths.Log(L("    声音: 导出 {0} / 未变 {1} / 无数据 {2}", exported, unchanged, noData));
        if (warn > WarnDetailLimit) Paths.Log(L("    [警告] 另有 {0} 条声音警告已折叠", warn - WarnDetailLimit));
        return exported;
    }

    private static Dictionary<string, SoundSig> BuildSoundIndex(UndertaleData data)
    {
        var map = new Dictionary<string, SoundSig>(StringComparer.Ordinal);
        int dup = 0, noData = 0;
        foreach (var snd in data.Sounds)
        {
            var name = snd.Name?.Content;
            if (string.IsNullOrEmpty(name)) continue;
            var bytes = snd.AudioFile?.Data;
            if (bytes == null || bytes.Length == 0) { noData++; continue; }
            var sig = new SoundSig
            {
                Len = bytes.Length, Hash = Hash64(bytes, 0, bytes.Length),
                Volume = snd.Volume, Pitch = snd.Pitch, Preload = snd.Preload, Type = snd.Type?.Content ?? ""
            };
            if (!map.TryAdd(name, sig)) dup++;
        }
        if (dup > 0) Paths.Log(L("    [警告] 基线有 {0} 个同名声音，取首个", dup));
        if (noData > 0) Paths.Log(L("    [提示] 基线有 {0} 个声音无内嵌数据（不参与差分）", noData));
        return map;
    }

    /// <summary>按数据魔数判定容器格式，返回 (扩展名, 写进 JSON 的 type)。无法识别时响亮警告。</summary>
    private static (string ext, string type) AudioFormat(byte[] bytes, string origType, string name, ref int warn)
    {
        string magic = bytes.Length >= 4 ? Encoding.ASCII.GetString(bytes, 0, 4) : "";
        switch (magic)
        {
            case "RIFF": return (".wav", "wav");
            case "OggS": return (".ogg", "ogg");
            case "fLaC": return (".flac", "flac");
            default:
                if (warn++ < WarnDetailLimit)
                    Paths.Log(L("    [警告] 声音 {0} 音频魔数未知 '{1}'（原 Type='{2}'）-> 写 .bin，type 用原值", name, magic, origType));
                return (".bin", string.IsNullOrEmpty(origType) ? "ogg" : origType);
        }
    }

    // ==================================================================
    //  字体
    // ==================================================================

    private static int ExportFonts(UndertaleData data, Dictionary<string, FontSig>? baseline,
                                   string dir, PageCache cache)
    {
        Directory.CreateDirectory(dir);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int exported = 0, unchanged = 0, noGlyph = 0, badGlyph = 0, warn = 0;
        var sigs = baseline != null ? BuildFontIndex(data, cache) : null;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var font in data.Fonts)
        {
            var name = font.Name?.Content;
            if (string.IsNullOrEmpty(name)) { if (warn++ < WarnDetailLimit) Paths.Log(L("    [警告] 跳过无名字体")); continue; }
            if (font.Glyphs.Count == 0) { noGlyph++; continue; }
            var tpi = font.Texture;
            if (tpi == null)
            {
                noGlyph++;
                if (warn++ < WarnDetailLimit) Paths.Log(L("    [警告] 字体 {0} 无纹理页 -> 跳过", name));
                continue;
            }

            FontSig? srcSig = null;
            if (baseline != null)
            {
                sigs!.TryGetValue(name, out srcSig);
                if (seen.Add(name) && srcSig != null && baseline.TryGetValue(name, out var b) && SameSig(srcSig, b))
                { unchanged++; continue; }
                if (srcSig != null && baseline.ContainsKey(name) && srcSig.Glyphs.Exists(g => g.NoTpi))
                {
                    noGlyph++;
                    if (warn++ < WarnDetailLimit) Paths.Log(L("    [警告] 字体 {0} 没有纹理页条目（结构无法表达）-> 跳过，保留基底原样", name));
                    continue;
                }
            }

            var safe = SafeName(name, used);
            var glyphDir = Path.Combine(dir, safe);
            Directory.CreateDirectory(glyphDir);
            var def = new FontDef
            {
                Name = name,
                EmSize = font.EmSize,
                LineHeight = Clamp(font.LineHeight),
                Ascender = Clamp(font.Ascender),
                ScaleX = font.ScaleX,
                ScaleY = font.ScaleY
            };

            var usedGlyph = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pendingGlyphs = new List<(IMagickImage<byte> img, string path, string label)>();
            foreach (var g in font.Glyphs)
            {
                var gname = SafeName("g" + g.Character, usedGlyph) + ".png";
                var rel = safe + "/" + gname;                       // JSON 内一律用正斜杠
                var full = Path.Combine(glyphDir, gname);
                int px = tpi.SourceX + g.SourceX;
                int py = tpi.SourceY + g.SourceY;
                var crop = cache.TryCrop(tpi.TexturePage, px, py, g.SourceWidth, g.SourceHeight,
                                         L("{0} 字形 {1}", name, g.Character));
                if (crop == null) { badGlyph++; WritePlaceholder(full); }
                else pendingGlyphs.Add((crop, full, L("{0} 字形 {1}", name, g.Character)));
                def.Glyphs.Add(new GlyphDef { Char = g.Character, File = rel, Shift = g.Shift, Offset = g.Offset });
            }

            foreach (var e in WritePngsParallel(pendingGlyphs))
            {
                badGlyph++;
                if (warn++ < WarnDetailLimit) Paths.Log(L("    [警告] 字体 {0} 写 PNG 失败 {1}", name, e));
            }

            File.WriteAllText(Path.Combine(dir, safe + ".json"), JsonSerializer.Serialize(def, JsonOut));
            exported++;
        }

        Paths.Log(L("    字体: 导出 {0} / 未变 {1} / 无字形或纹理 {2} / 坏字形 {3}", exported, unchanged, noGlyph, badGlyph));
        if (warn > WarnDetailLimit) Paths.Log(L("    [警告] 另有 {0} 条字体警告已折叠", warn - WarnDetailLimit));
        return exported;
    }

    private static FontSig BuildFontSig(UndertaleFont font, List<RectNeed>? needs)
    {
        var sig = new FontSig
        {
            EmSize = font.EmSize, LineHeight = font.LineHeight, Ascender = font.Ascender,
            ScaleX = font.ScaleX, ScaleY = font.ScaleY
        };
        var tpi = font.Texture;
        foreach (var g in font.Glyphs)
        {
            var gs = new GlyphSig
            {
                Char = g.Character, X = g.SourceX, Y = g.SourceY,
                W = g.SourceWidth, H = g.SourceHeight, Shift = g.Shift, Offset = g.Offset
            };
            if (tpi == null) gs.NoTpi = true;
            else
            {
                gs.Zero = g.SourceWidth <= 0 || g.SourceHeight <= 0;
                // 字形坐标相对字体自己的纹理页条目（已实测：如 fnt_small TPI=(1546,1422,128x64)，字形仅在其内）
                if (needs != null && !gs.Zero && tpi.TexturePage != null)
                    needs.Add(new RectNeed(tpi.TexturePage, tpi.SourceX + g.SourceX, tpi.SourceY + g.SourceY,
                                           g.SourceWidth, g.SourceHeight, gs));
            }
            sig.Glyphs.Add(gs);
        }
        return sig;
    }

    private static Dictionary<string, FontSig> BuildFontIndex(UndertaleData data, PageCache cache)
    {
        var map = new Dictionary<string, FontSig>(StringComparer.Ordinal);
        var needs = new List<RectNeed>();
        int dup = 0;
        foreach (var font in data.Fonts)
        {
            var name = font.Name?.Content;
            if (string.IsNullOrEmpty(name) || font.Glyphs.Count == 0) continue;
            if (!map.TryAdd(name, BuildFontSig(font, needs))) dup++;
        }
        HashRects(needs, cache);
        if (dup > 0) Paths.Log(L("    [警告] 有 {0} 个同名字体，取首个", dup));
        return map;
    }

    // ==================================================================
    //  按纹理页分组的像素哈希（每页只解码一次）
    // ==================================================================

    /// <summary>待哈希矩形：指向指纹里的目标槽位，哈希完成后直接写回。</summary>
    private abstract class PixelSlot
    {
        public abstract void Set(ulong hash);
    }

    private sealed class FrameSlot : PixelSlot
    {
        private readonly FrameSig _s;
        public FrameSlot(FrameSig s) { _s = s; }
        public override void Set(ulong hash) { _s.Pix = hash; _s.HasPix = true; }
    }

    private sealed class GlyphSlot : PixelSlot
    {
        private readonly GlyphSig _s;
        public GlyphSlot(GlyphSig s) { _s = s; }
        public override void Set(ulong hash) { _s.Pix = hash; _s.HasPix = true; }
    }

    private sealed class RectNeed
    {
        public readonly UndertaleEmbeddedTexture Page;
        public readonly int X, Y, W, H;
        public readonly PixelSlot Slot;
        public RectNeed(UndertaleEmbeddedTexture page, int x, int y, int w, int h, FrameSig s)
        { Page = page; X = x; Y = y; W = w; H = h; Slot = new FrameSlot(s); }
        public RectNeed(UndertaleEmbeddedTexture page, int x, int y, int w, int h, GlyphSig s)
        { Page = page; X = x; Y = y; W = w; H = h; Slot = new GlyphSlot(s); }
    }

    /// <summary>按纹理页分组做区域哈希：每页解码/取像素一次，显著快于逐资源随机访问。</summary>
    private static void HashRects(List<RectNeed> needs, PageCache cache)
    {
        if (needs.Count == 0) return;
        foreach (var group in needs.GroupBy(n => n.Page))
        {
            var px = cache.AcquireRgba(group.Key, L("差异比较"));
            var buf = px.Buf;
            if (buf != null)
            {
                foreach (var n in group)
                {
                    if (n.X < 0 || n.Y < 0 || n.X + n.W > px.W || n.Y + n.H > px.H) continue;
                    ulong h = 14695981039346656037UL;
                    int rowBytes = n.W * 4;
                    for (int row = 0; row < n.H; row++)
                    {
                        int off = ((n.Y + row) * px.W + n.X) * 4;
                        for (int i = 0; i < rowBytes; i++) { h ^= buf[off + i]; h *= 1099511628211UL; }
                    }
                    n.Slot.Set(h);
                }
            }
            cache.Release(group.Key);
        }
    }

    private static bool SameSig(FontSig a, FontSig b)
    {
        if (a.EmSize != b.EmSize || a.LineHeight != b.LineHeight || a.Ascender != b.Ascender ||
            a.ScaleX != b.ScaleX || a.ScaleY != b.ScaleY) return false;
        if (a.Glyphs.Count != b.Glyphs.Count) return false;
        for (int i = 0; i < a.Glyphs.Count; i++)
        {
            var x = a.Glyphs[i];
            var y = b.Glyphs[i];
            if (x.Char != y.Char || x.X != y.X || x.Y != y.Y || x.W != y.W || x.H != y.H ||
                x.Shift != y.Shift || x.Offset != y.Offset) return false;
            if (x.NoTpi || y.NoTpi) { if (x.NoTpi != y.NoTpi) return false; continue; }
            if (x.Zero || y.Zero) { if (x.Zero != y.Zero) return false; continue; }
            if (!x.HasPix || !y.HasPix) return false;   // 像素不可比 -> 认定有改动（宁可多导）
            if (x.Pix != y.Pix) return false;
        }
        return true;
    }

    // ==================================================================
    //  纹理页缓存 / 裁剪 / 哈希
    // ==================================================================

    /// <summary>纹理页按需解码 + LRU 缓存（限制峰值内存），提供裁切与区域哈希。</summary>
    private sealed class PageCache : IDisposable
    {
        private readonly string _tag;
        private readonly Dictionary<UndertaleEmbeddedTexture, Entry> _map = new();
        private readonly List<UndertaleEmbeddedTexture> _order = new();
        private UndertaleEmbeddedTexture? _pinned;
        private int _warn;

        public PageCache(string tag) { _tag = tag; }

        private sealed class Entry
        {
            public MagickImage? Img;
            public byte[]? Rgba;
            public int W, H;
            public bool RgbaTried;
            public bool Failed;
        }

        private Entry? Get(UndertaleEmbeddedTexture? page, string where)
        {
            if (page == null)
            {
                if (_warn++ < WarnDetailLimit) Paths.Log(L("    [警告] {0}: {1} 无纹理页", _tag, where));
                return null;
            }
            if (_map.TryGetValue(page, out var e))
            {
                _order.Remove(page);
                _order.Add(page);
                return e.Failed ? null : e;
            }
            if (page.TextureExternal && _warn++ < WarnDetailLimit)
                Paths.Log(L("    [警告] {0}: {1} 纹理页 {2} 标记为外部纹理(TextureExternal)", _tag, where, page.Name?.Content));
            var ne = new Entry();
            try
            {
                var gm = page.TextureData?.Image;
                if (gm == null) { ne.Failed = true; }
                else
                {
                    ne.Img = gm.GetMagickImage();
                    ne.W = (int)ne.Img.Width;
                    ne.H = (int)ne.Img.Height;
                    if (ne.W <= 0 || ne.H <= 0) ne.Failed = true;
                }
            }
            catch (Exception ex)
            {
                ne.Failed = true;
                if (_warn++ < WarnDetailLimit)
                    Paths.Log(L("    [警告] {0}: {1} 纹理页 {2} 解码失败 {3}: {4}", _tag, where, page.Name?.Content, ex.GetType().Name, ex.Message));
            }
            if (ne.Failed && _warn++ < WarnDetailLimit)
                Paths.Log(L("    [警告] {0}: {1} 纹理页 {2} 无像素数据", _tag, where, page.Name?.Content));

            _map[page] = ne;
            _order.Add(page);
            while (_order.Count > PageCacheLimit)
            {
                var old = _order.FirstOrDefault(p => !ReferenceEquals(p, _pinned));
                if (old == null) break;                     // 只剩被 AcquireRgba 钉住的那页
                _order.Remove(old);
                if (_map.Remove(old, out var oe)) Dispose(oe);
            }
            return ne.Failed ? null : ne;
        }

        /// <summary>裁出 [x,y,w,h)；越界/空尺寸/解码失败返回 null（调用方写占位图并响亮记录）。</summary>
        public IMagickImage<byte>? TryCrop(UndertaleEmbeddedTexture? page, int x, int y, int w, int h, string where)
        {
            var e = Get(page, where);
            if (e?.Img == null) return null;
            if (w <= 0 || h <= 0)
            {
                if (_warn++ < WarnDetailLimit)
                    Paths.Log(L("    [警告] {0}: {1} 源尺寸为 0 ({2}x{3}) -> 写 1x1 占位图", _tag, where, w, h));
                return null;
            }
            if (x < 0 || y < 0 || x + w > e.W || y + h > e.H)
            {
                if (_warn++ < WarnDetailLimit)
                    Paths.Log(L("    [警告] {0}: {1} 裁切越界 ({2},{3},{4}x{5}) 超出纹理页 {6}x{7} -> 写 1x1 占位图", _tag, where, x, y, w, h, e.W, e.H));
                return null;
            }
            try { return e.Img.CloneArea(x, y, (uint)w, (uint)h); }
            catch (Exception ex)
            {
                if (_warn++ < WarnDetailLimit)
                    Paths.Log(L("    [警告] {0}: {1} 裁切失败 {2}: {3}", _tag, where, ex.GetType().Name, ex.Message));
                return null;
            }
        }

        /// <summary>整页 RGBA 像素视图（RawImage 不可得时 Ok=false）。</summary>
        public readonly struct Pixels
        {
            public readonly byte[]? Buf;
            public readonly int W, H;
            public Pixels(byte[] buf, int w, int h) { Buf = buf; W = w; H = h; }
            public bool Ok => Buf != null;
        }

        /// <summary>取整页 RGBA（解码/取像素一次，驻留到 Release 为止），失败返回 Ok=false。</summary>
        public Pixels AcquireRgba(UndertaleEmbeddedTexture? page, string where)
        {
            _pinned = page;
            var e = Get(page, where);
            if (e?.Img == null) return default;
            if (!e.RgbaTried)
            {
                e.RgbaTried = true;
                try
                {
                    var b = e.Img.ToByteArray(MagickFormat.Rgba);
                    if (b.Length == e.W * e.H * 4) e.Rgba = b;
                    else if (_warn++ < WarnDetailLimit)
                        Paths.Log(L("    [警告] {0}: 纹理页 {1} RGBA 长度 {2} != {3}，跳过像素差分", _tag, page?.Name?.Content, b.Length, e.W * e.H * 4));
                }
                catch (Exception ex)
                {
                    if (_warn++ < WarnDetailLimit)
                        Paths.Log(L("    [警告] {0}: 纹理页 {1} 取像素失败 {2}，跳过像素差分", _tag, page?.Name?.Content, ex.Message));
                }
            }
            return e.Rgba == null ? default : new Pixels(e.Rgba, e.W, e.H);
        }

        /// <summary>释放 AcquireRgba 钉住的纹理页（立即回收内存）。</summary>
        public void Release(UndertaleEmbeddedTexture? page)
        {
            if (page == null) return;
            if (ReferenceEquals(_pinned, page)) _pinned = null;
            if (_map.Remove(page, out var e)) { _order.Remove(page); Dispose(e); }
        }

        public void Dispose()
        {
            foreach (var e in _map.Values) Dispose(e);
            _map.Clear();
            _order.Clear();
            if (_warn > WarnDetailLimit)
                Paths.Log(L("    [警告] {0}: 另有 {1} 条纹理页警告已折叠", _tag, _warn - WarnDetailLimit));
        }

        private static void Dispose(Entry e)
        {
            try { e.Img?.Dispose(); } catch { }
            e.Img = null;
            e.Rgba = null;
        }
    }

    private static ulong Hash64(byte[] buf, int offset, int count)
    {
        ulong h = 14695981039346656037UL;
        for (int i = 0; i < count; i++)
        {
            h ^= buf[offset + i];
            h *= 1099511628211UL;
        }
        return h;
    }

    /// <summary>写 1x1 透明 PNG 占位（保证帧号/字形数与源一致，绝不静默丢帧）。</summary>
    /// <summary>并行写 PNG。PNG 编码是导出的 CPU 大头（实测 1851 精灵/8258 帧要 ~290 秒），
    /// 而裁剪阶段必须保持**单线程**（共享纹理页缓存不是线程安全的）——
    /// 所以流程是：顺序裁剪 → 收集待写项 → 在这里一次性并行编码写盘。
    /// 返回出错信息（调用方拿去计数/记日志，保证并行不影响原有的告警语义）。</summary>
    private static List<string> WritePngsParallel(
        List<(IMagickImage<byte> img, string path, string label)> items)
    {
        var errs = new System.Collections.Concurrent.ConcurrentQueue<string>();
        System.Threading.Tasks.Parallel.ForEach(items,
            new System.Threading.Tasks.ParallelOptions
            {
                // NTL_EXPORT_SEQ=1 时退化为串行 —— 用来做"并行 vs 串行"的逐字节对照验证
                MaxDegreeOfParallelism = Environment.GetEnvironmentVariable("NTL_EXPORT_SEQ") == "1"
                    ? 1
                    : Math.Max(2, Environment.ProcessorCount)
            },
            it =>
            {
                try
                {
                    // ★ 去掉元数据再写：ImageMagick 默认会往 PNG 里塞 tIME / date:create / date:modify，
                    //   导致**同样的像素每次导出得到不同字节**（实测两次导出 8258 个 PNG 全部不一致，
                    //   连串行版本也不自洽）。strip 之后输出是确定性的、可复现的，也稍小一点。
                    it.img.Strip();
                    it.img.Write(it.path, MagickFormat.Png32);
                }
                catch (Exception ex)
                {
                    errs.Enqueue(it.label + ": " + ex.Message);
                    try { WritePlaceholder(it.path); } catch { }
                }
                finally { try { it.img.Dispose(); } catch { } }
            });
        return errs.ToList();
    }

    private static void WritePlaceholder(string path)
    {
        try
        {
            using var img = new MagickImage(MagickColors.Transparent, 1, 1) { Format = MagickFormat.Png32 };
        img.Strip();
            img.Write(path, MagickFormat.Png32);
        }
        catch (Exception ex) { Paths.Log(L("    [警告] 占位图写入失败 {0}: {1}", path, ex.Message)); }
    }

    // ==================================================================
    //  指纹类型 / 文件名安全 / 小工具
    // ==================================================================

    private sealed class FrameSig
    {
        public int Sw, Sh, Tx, Ty, Tw, Th, Bw, Bh;
        public ulong Pix;
        public bool HasPix;
        public bool NoTpi;   // 该帧没有 TexturePageItem（两侧都无像素可导，如九宫格等结构）
        public bool Zero;    // 源尺寸为 0
    }

    private sealed class SpriteSig
    {
        public uint W, H, BBox;
        public int Ox, Oy, Ml, Mr, Mt, Mb;
        public List<FrameSig> Frames = new();
    }

    private sealed class GlyphSig
    {
        public int Char, X, Y, W, H, Shift, Offset;
        public ulong Pix;
        public bool HasPix;
        public bool NoTpi;   // 字体无纹理页条目
        public bool Zero;    // 字形源尺寸为 0
    }

    private sealed class FontSig
    {
        public float EmSize, ScaleX, ScaleY;
        public uint LineHeight, Ascender;
        public List<GlyphSig> Glyphs = new();
    }

    private sealed class SoundSig
    {
        public int Len;
        public ulong Hash;
        public float Volume, Pitch;
        public bool Preload;
        public string Type = "";

        public bool Equals(SoundSig o) =>
            Len == o.Len && Hash == o.Hash && Volume == o.Volume && Pitch == o.Pitch &&
            Preload == o.Preload && string.Equals(Type, o.Type, StringComparison.Ordinal);
    }

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>资源名 -> 安全文件名（替换非法字符、避开 Windows 保留名、去尾点、限长、去重）。</summary>
    private static string SafeName(string raw, HashSet<string> used)
    {
        var sb = new StringBuilder(raw.Length);
        foreach (var c in raw) sb.Append(IsInvalid(c) ? '_' : c);
        var s = sb.ToString().Trim().TrimEnd('.', ' ');
        if (s.Length == 0) s = "unnamed";
        if (s.Length > 120) s = s[..120];
        if (ReservedNames.Contains(s)) s = "_" + s;
        var candidate = s;
        int n = 2;
        while (!used.Add(candidate)) candidate = s + "_" + n++;
        return candidate;
    }

    private static bool IsInvalid(char c) => c < 32 || c == '<' || c == '>' || c == ':' || c == '"' ||
                                             c == '/' || c == '\\' || c == '|' || c == '?' || c == '*';

    private static int Clamp(uint v) => v > int.MaxValue ? int.MaxValue : (int)v;
}
