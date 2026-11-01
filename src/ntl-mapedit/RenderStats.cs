using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using UndertaleModLib.Models;

namespace NtlMapEdit;

/// <summary>
/// Offline render coverage report used by the C3b acceptance probe. It renders the room
/// with the editor background, then reports what the renderer actually drew per layer and
/// what it could not represent (Path/Effect layers, sequences, particles, text items...).
/// No UI, no game process.
/// </summary>
public static class RenderStats
{
    public sealed class LayerInfo
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public long Depth { get; set; }
        public bool Visible { get; set; }
        public int Drawn { get; set; }
        public int Unsupported { get; set; }
        public string Detail { get; set; } = "";
    }

    public sealed class Summary
    {
        public string Room { get; set; } = "";
        public int Width { get; set; }
        public int Height { get; set; }
        public int Scale { get; set; }
        public long Pixels { get; set; }
        public long BlackPixels { get; set; }
        public double BlackPercent { get; set; }
        public long CoverPixels { get; set; }
        public double CoverPercent { get; set; }
        public long TransparentPixels { get; set; }
        public long UnsupportedTotal { get; set; }
        public List<LayerInfo> Layers { get; set; } = new();
    }

    public static Summary Run(GameData gd, UndertaleRoom room, int scale = 1)
    {
        var summary = new Summary
        {
            Room = GameData.Name(room.Name),
            Width = (int)room.Width,
            Height = (int)room.Height,
            Scale = Math.Max(1, scale),
        };
        foreach (var layer in room.Layers ?? Enumerable.Empty<UndertaleRoom.Layer>())
        {
            var info = InSpectLayer(gd, layer);
            summary.Layers.Add(info);
            summary.UnsupportedTotal += info.Unsupported;
        }

        using var bmp = RoomRender.Render(gd, room, summary.Scale);
        summary.Pixels = (long)bmp.Width * bmp.Height;
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var buf = new byte[data.Stride * data.Height];
            Marshal.Copy(data.Scan0, buf, 0, buf.Length);
            var canvas = RoomRender.CanvasBackground;
            int nr = canvas.R, ng = canvas.G, nb = canvas.B;
            for (int y = 0; y < bmp.Height; y++)
            {
                int row = y * data.Stride;
                for (int x = 0; x < bmp.Width; x++)
                {
                    int i = row + x * 4;
                    byte b = buf[i], g = buf[i + 1], r = buf[i + 2], a = buf[i + 3];
                    if (a > 0 && r == 0 && g == 0 && b == 0) summary.BlackPixels++;
                    if (a == 0) summary.TransparentPixels++;
                    if (!(a == 255 && r == nr && g == ng && b == nb)) summary.CoverPixels++;
                }
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        summary.BlackPercent = 100.0 * summary.BlackPixels / summary.Pixels;
        summary.CoverPercent = 100.0 * summary.CoverPixels / summary.Pixels;
        return summary;
    }

    private static LayerInfo InSpectLayer(GameData gd, UndertaleRoom.Layer layer)
    {
        var info = new LayerInfo
        {
            Name = GameData.Name(layer.LayerName),
            Type = layer.LayerType.ToString(),
            Depth = layer.LayerDepth,
            Visible = layer.IsVisible,
        };
        switch (layer.LayerType)
        {
            case UndertaleRoom.LayerType.Instances:
            {
                int total = layer.InstancesData?.Instances?.Count ?? 0;
                int hidden = 0, noSprite = 0, drawable = 0;
                foreach (var inst in layer.InstancesData?.Instances ?? Enumerable.Empty<UndertaleRoom.GameObject>())
                {
                    if (inst.Nonexistent) continue;
                    if (inst.ObjectDefinition?.Visible == false) { hidden++; continue; }
                    var spr = inst.ObjectDefinition?.Sprite;
                    if (spr == null || gd.Frame(spr, inst.ImageIndex) == null) { noSprite++; continue; }
                    drawable++;
                }
                info.Drawn = drawable;
                info.Unsupported = 0;
                info.Detail = $"instances={total} drawable={drawable} hiddenObject={hidden} noSprite={noSprite}";
                break;
            }
            case UndertaleRoom.LayerType.Tiles:
            {
                var bg = layer.TilesData?.Background;
                int total = NativeTiles.CountNonZero(layer);
                int invalid = 0, missing = 0;
                foreach (var (x, y, value) in NativeTiles.Cells(layer))
                {
                    int id = NativeTiles.TileId(value);
                    if (!NativeTiles.IsTileIdValid(bg, id)) { invalid++; continue; }
                    if (gd.Tile(bg, id, NativeTiles.Flags(value)) == null) missing++;
                }
                info.Drawn = Math.Max(0, total - invalid - missing);
                info.Unsupported = invalid + missing;
                info.Detail = $"nonzero={total} drawable={info.Drawn} invalidId={invalid} missingBitmap={missing} tileset={GameData.Name(bg?.Name)} tile={bg?.GMS2TileWidth}x{bg?.GMS2TileHeight}";
                break;
            }
            case UndertaleRoom.LayerType.Assets:
            {
                var ad = layer.AssetsData;
                int legacy = ad?.LegacyTiles?.Count ?? 0;
                int sprites = ad?.Sprites?.Count ?? 0;
                int nine = ad?.NineSlices?.Count ?? 0;
                int seq = ad?.Sequences?.Count ?? 0;
                int particles = ad?.ParticleSystems?.Count ?? 0;
                int text = ad?.TextItems?.Count ?? 0;
                int unresolved = 0;
                foreach (var si in (ad?.Sprites ?? Enumerable.Empty<UndertaleRoom.SpriteInstance>()).Concat(ad?.NineSlices ?? Enumerable.Empty<UndertaleRoom.SpriteInstance>()))
                    if (RoomRender.SpriteInstancePlacement(gd, layer, si) == null) unresolved++;
                info.Drawn = legacy + sprites + nine - unresolved;
                info.Unsupported = seq + particles + text + unresolved;
                info.Detail = $"legacyTiles={legacy} sprites={sprites} nineSlices={nine} sequences={seq} particles={particles} textItems={text} unresolved={unresolved}";
                break;
            }
            case UndertaleRoom.LayerType.Background:
            {
                var bd = layer.BackgroundData;
                int sprite = bd?.Sprite != null ? 1 : 0;
                bool color = bd != null && GameData.ColorFromGMExact(bd.Color).A > 0;
                info.Drawn = bd?.Visible == false ? 0 : (sprite > 0 ? 1 : (color ? 1 : 0));
                info.Unsupported = sprite > 0 && bd?.Visible != false && gd.Frame(bd!.Sprite, (int)bd.FirstFrame) == null ? 1 : 0;
                info.Detail = $"sprite={(bd?.Sprite == null ? "null" : GameData.Name(bd.Sprite.Name))} color=0x{(bd?.Color ?? 0):X8} fg={(bd?.Foreground == true)} visible={bd?.Visible} tiled={bd?.TiledHorizontally}/{bd?.TiledVertically} stretch={bd?.Stretch}";
                break;
            }
            case UndertaleRoom.LayerType.Path:
            case UndertaleRoom.LayerType.Path2:
                info.Drawn = 0;
                info.Unsupported = 1;
                info.Detail = "UTMT/GMS2 room data has no PathData for path layers (only a placeholder can be drawn)";
                break;
            case UndertaleRoom.LayerType.Effect:
                info.Drawn = 0;
                info.Unsupported = 1;
                info.Detail = $"effect={layer.EffectType} enabled={layer.EffectEnabled}";
                break;
            default:
                info.Drawn = NativeTiles.CountContent(layer);
                info.Unsupported = 0;
                info.Detail = "unclassified layer";
                break;
        }
        if (!layer.IsVisible) info.Drawn = 0;
        return info;
    }

    public static string Format(Summary s)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"ROOM {s.Room} {s.Width}x{s.Height} scale={s.Scale} layers={s.Layers.Count}");
        foreach (var l in s.Layers)
            sb.AppendLine($"  {l.Type,-11} {l.Name,-34} depth={l.Depth,6} vis={(l.Visible ? 1 : 0)} drawn={l.Drawn,6} unsupported={l.Unsupported,5}  {l.Detail}");
        sb.AppendLine($"PIXELS total={s.Pixels} black={s.BlackPixels} ({s.BlackPercent:F2}%) covered={s.CoverPixels} ({s.CoverPercent:F2}%) transparent={s.TransparentPixels}");
        sb.AppendLine($"UNSUPPORTED total={s.UnsupportedTotal}");
        return sb.ToString().TrimEnd();
    }
}
