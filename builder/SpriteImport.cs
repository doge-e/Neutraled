using System.Text.Json;
using ImageMagick;
using UndertaleModLib;
using UndertaleModLib.Models;
using UndertaleModLib.Util;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

public sealed class SpriteFrameDef
{
    public string File { get; set; } = "";
    public int TargetX { get; set; }
    public int TargetY { get; set; }
}

public sealed class SpriteDef
{
    public string Name { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public int OriginX { get; set; }
    public int OriginY { get; set; }
    public int MarginLeft { get; set; }
    public int MarginRight { get; set; }
    public int MarginTop { get; set; }
    public int MarginBottom { get; set; }
    public int BBoxMode { get; set; }
    public List<SpriteFrameDef> Frames { get; set; } = new();
}

/// <summary>精灵资源包导入（sprites/*.json + PNG）。
/// 规则：已有精灵只替换帧纹理，不碰 CollisionMasks/SepMasks（避免碰撞失效）；
/// 新精灵创建完整资源（含全实心包围盒掩码）。</summary>
public static class SpriteImport
{
    private const int PageSize = 2048;

    public static int Import(UndertaleData data, string spritesDir)
    {
        var jsonFiles = Directory.GetFiles(spritesDir, "*.json", SearchOption.TopDirectoryOnly);
        if (jsonFiles.Length == 0) { Paths.Log(L("    [跳过] 无 json 定义")); return 0; }

        var defs = new List<(SpriteDef def, string dir)>();
        foreach (var jf in jsonFiles)
        {
            try
            {
                var d = JsonSerializer.Deserialize<SpriteDef>(File.ReadAllText(jf), Paths.Json);
                if (d != null && !string.IsNullOrEmpty(d.Name) && d.Frames.Count > 0) defs.Add((d, Path.GetDirectoryName(jf)!));
            }
            catch (Exception ex) { Paths.Log(L("    [警告] 解析失败 {0}: {1}", Path.GetFileName(jf), ex.Message)); }
        }
        if (defs.Count == 0) { Paths.Log(L("    [跳过] 无有效定义")); return 0; }

        // 1) 收集所有帧图，统一拼进一张新纹理页
        var placed = new List<(SpriteDef def, SpriteFrameDef frame, int pageIndex, int srcX, int srcY, MagickImage img, string file)>();
        int pageIndex = 0, curX = 0, curY = 0, rowH = 0;
        var pages = new List<MagickImage> { new MagickImage(MagickColors.Transparent, PageSize, PageSize) };
        pages[0].Format = MagickFormat.Png32;

        int newCount = 0, replacedCount = 0, skipped = 0;
        foreach (var (def, dir) in defs)
        {
            foreach (var fr in def.Frames)
            {
                var path = Path.Combine(dir, fr.File);
                if (!File.Exists(path)) { Paths.Log(L("    [警告] 帧缺失: {0}", fr.File)); skipped++; continue; }
                MagickImage img;
                try { img = new MagickImage(path); } catch (Exception ex) { Paths.Log(L("    [警告] 读图失败 {0}: {1}", fr.File, ex.Message)); skipped++; continue; }

                int w = (int)img.Width, h = (int)img.Height;
                if (curX + w > PageSize) { curX = 0; curY += rowH + 1; rowH = 0; }
                if (curY + h > PageSize)
                {
                    pages.Add(new MagickImage(MagickColors.Transparent, PageSize, PageSize) { Format = MagickFormat.Png32 });
                    pageIndex++; curX = 0; curY = 0; rowH = 0;
                }
                // 用 Copy(src) 而不是 Over：Over 会把 alpha=0 处的隐藏 RGB 归一化、并做 alpha 舍入，
                // 导致"导出→导入"往返后逐像素比对出现差异（实测 572 个精灵有差，全在不可见像素）。
                // 帧之间不重叠（每帧留 1px 间隙），所以 Copy 语义完全安全，且能逐字节保真。
                pages[pageIndex].Composite(img, curX, curY, CompositeOperator.Copy);
                placed.Add((def, fr, pageIndex, curX, curY, img, fr.File));
                curX += w + 1;
                if (h > rowH) rowH = h;
            }
        }

        // 2) 写入纹理页（EmbeddedTexture）
        var pageItems = new List<UndertaleEmbeddedTexture>();
        foreach (var pg in pages)
        {
            var png = pg.ToByteArray(MagickFormat.Png32);
            var tex = new UndertaleEmbeddedTexture
            {
                Name = data.Strings.MakeString("ntl_page_" + Guid.NewGuid().ToString("N")[..8]),
                Scaled = 1,
                GeneratedMips = 0,
                TextureExternal = false,
                TextureWidth = PageSize,
                TextureHeight = PageSize
            };
            tex.TextureData = new UndertaleEmbeddedTexture.TexData
            {
                Image = GMImage.FromPng(png, true)
            };
            data.EmbeddedTextures.Add(tex);
            pageItems.Add(tex);
        }

        // 3) 为每帧建 TexturePageItem 并写入精灵
        foreach (var (def, fr, pi, sx, sy, img, file) in placed)
        {
            int w = (int)img.Width, h = (int)img.Height;
            var tpi = new UndertaleTexturePageItem
            {
                Name = data.Strings.MakeString("ntl_tpi_" + Guid.NewGuid().ToString("N")[..8]),
                SourceX = (ushort)sx,
                SourceY = (ushort)sy,
                SourceWidth = (ushort)w,
                SourceHeight = (ushort)h,
                TargetX = (ushort)Math.Max(0, fr.TargetX),
                TargetY = (ushort)Math.Max(0, fr.TargetY),
                TargetWidth = (ushort)w,
                TargetHeight = (ushort)h,
                BoundingWidth = (ushort)w,
                BoundingHeight = (ushort)h,
                TexturePage = pageItems[pi]
            };
            data.TexturePageItems.Add(tpi);

            var spr = data.Sprites.FirstOrDefault(s => s.Name?.Content == def.Name);
            int frameIdx = def.Frames.IndexOf(fr);

            if (spr == null)
            {
                // 第 0 帧：创建完整精灵资源（后续帧走下面统一的"追加"分支）
                spr = new UndertaleSprite
                {
                    Name = data.Strings.MakeString(def.Name),
                    Width = (uint)Math.Max(1, def.Width),
                    Height = (uint)Math.Max(1, def.Height),
                    MarginLeft = (int)def.MarginLeft,
                    MarginRight = (int)def.MarginRight,
                    MarginTop = (int)def.MarginTop,
                    MarginBottom = (int)def.MarginBottom,
                    OriginX = def.OriginX,
                    OriginY = def.OriginY,
                    BBoxMode = (uint)def.BBoxMode,
                    SepMasks = UndertaleSprite.SepMaskType.AxisAlignedRect,
                    Transparent = true,
                    Smooth = false,
                    Preload = false
                };
                data.Sprites.Add(spr);
                newCount++;
            }

            // 帧落位：已有帧→替换；正好下一帧→追加；越界→跳过
            // ⚠ 这里曾经是"新精灵只建第 0 帧，第 1..N 帧掉进 skipped 被静默丢弃" ——
            //    实测 1851 个新精灵里有 999 个多帧精灵只剩第 0 帧（详见 docs/PIPELINE.md 的往返验证）。
            if (frameIdx < spr.Textures.Count)
            {
                spr.Textures[frameIdx].Texture = tpi;
                replacedCount++;
            }
            else if (frameIdx == spr.Textures.Count)
            {
                spr.Textures.Add(new UndertaleSprite.TextureEntry { Texture = tpi });
                replacedCount++;
            }
            else skipped++;
        }

        Paths.Log(L("    精灵: {0} 新增 / {1} 帧替换 / {2} 跳过（纹理页 {3} 张）", newCount, replacedCount, skipped, pages.Count));
        foreach (var pg in pages) pg.Dispose();
        foreach (var (_, _, _, _, _, img, _) in placed) img.Dispose();
        return newCount + replacedCount;
    }
}
