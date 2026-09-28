using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using ImageMagick;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>Kristal / Tiled 地图转换器（最小闭环）
///
/// 输入：Tiled 地图（.tmx 或 Tiled 导出的 .lua）+ 其 .tsx tileset + PNG 素材
/// 输出：
///   1. 一张烘好的整图 PNG（所有图块按正确顺序合成）→ Neutraled 精灵包
///   2. 一份地图数据 JSON（尺寸 / 碰撞矩形 / 对象 / 出生点 / 音乐）→ 运行时读取
///
/// 为什么烘成整图：GM 的 room 是二进制格式、tileset 结构复杂；
/// 烘图 + 运行时 GML 对象承载碰撞，是可靠且可移植的做法。
/// </summary>
public static class KristalMap
{
    public sealed class TileLayer { public string Name = ""; public int Width, Height; public int[] Data = Array.Empty<int>(); public float Opacity = 1; public bool Visible = true; }
    public sealed class MapObject { public int Id; public string Name = ""; public string Type = ""; public float X, Y, W, H; public bool IsPoint; public Dictionary<string,string> Props = new(); }
    public sealed class ObjGroup { public string Name = ""; public List<MapObject> Objects = new(); }
    public sealed class TilesetRef { public string Name = ""; public int FirstGid; public string ImagePath = ""; public int TileWidth, TileHeight, Columns, TileCount, Margin, Spacing, ImageWidth, ImageHeight; }
    public sealed class TiledMap
    {
        public int Width, Height, TileWidth, TileHeight;
        public Dictionary<string,string> Properties = new();
        public List<TilesetRef> Tilesets = new();
        public List<TileLayer> TileLayers = new();
        public List<ObjGroup> ObjectGroups = new();
    }

    /// <summary>解析 .tmx（XML）地图。</summary>
    public static TiledMap ParseTmx(string path)
    {
        var doc = XDocument.Load(path);
        var root = doc.Root!;
        var map = new TiledMap
        {
            Width = Int(root, "width"),
            Height = Int(root, "height"),
            TileWidth = Int(root, "tilewidth"),
            TileHeight = Int(root, "tileheight")
        };
        var baseDir = Path.GetDirectoryName(path)!;

        // 地图属性
        foreach (var p in root.Elements("properties").Elements("property"))
            map.Properties[Attr(p, "name")] = Attr(p, "value");

        // tilesets
        foreach (var ts in root.Elements("tileset"))
        {
            var src = Attr(ts, "source");
            if (!string.IsNullOrEmpty(src))
            {
                var tsxPath = Path.GetFullPath(Path.Combine(baseDir, src));
                map.Tilesets.Add(ParseTsx(tsxPath, Int(ts, "firstgid")));
            }
        }

        // 图块层
        foreach (var layer in root.Elements("layer"))
        {
            var tl = new TileLayer
            {
                Name = Attr(layer, "name"),
                Width = Int(layer, "width"),
                Height = Int(layer, "height"),
                Opacity = F(layer, "opacity", 1f),
                Visible = Attr(layer, "visible") != "0"
            };
            var data = layer.Element("data");
            if (data != null && Attr(data, "encoding") == "csv")
            {
                var nums = data.Value.Split(new[] { ',', '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                tl.Data = nums.Select(s => int.TryParse(s.Trim(), out var v) ? v : 0).ToArray();
            }
            map.TileLayers.Add(tl);
        }

        // 对象层
        foreach (var og in root.Elements("objectgroup"))
        {
            var g = new ObjGroup { Name = Attr(og, "name") };
            foreach (var o in og.Elements("object"))
            {
                var mo = new MapObject
                {
                    Id = Int(o, "id"),
                    Name = Attr(o, "name"),
                    Type = Attr(o, "type"),
                    X = F(o, "x", 0), Y = F(o, "y", 0),
                    W = F(o, "width", 0), H = F(o, "height", 0),
                    IsPoint = o.Element("point") != null
                };
                foreach (var p in o.Elements("properties").Elements("property"))
                    mo.Props[Attr(p, "name")] = Attr(p, "value");
                g.Objects.Add(mo);
            }
            map.ObjectGroups.Add(g);
        }
        return map;
    }

    private static TilesetRef ParseTsx(string tsxPath, int firstGid)
    {
        // 容错：mod 引用的 tileset 可能已不存在（作者删过资源）。
        // 此时不要整体失败，返回空引用让该 tileset 的图块留空即可。
        if (!File.Exists(tsxPath))
        {
            Paths.Log(L("    [警告] tileset 缺失: {0}（该部分图块将留空）", Path.GetFileName(tsxPath)));
            return new TilesetRef
            {
                Name = Path.GetFileNameWithoutExtension(tsxPath),
                FirstGid = firstGid,
                ImagePath = ""
            };
        }
        var doc = XDocument.Load(tsxPath);
        var root = doc.Root!;
        var ts = new TilesetRef
        {
            Name = Attr(root, "name"),
            FirstGid = firstGid,
            TileWidth = Int(root, "tilewidth"),
            TileHeight = Int(root, "tileheight"),
            Columns = Int(root, "columns"),
            TileCount = Int(root, "tilecount"),
            Margin = Int(root, "margin"),
            Spacing = Int(root, "spacing")
        };
        var img = root.Element("image");
        if (img != null)
        {
            var src = Attr(img, "source");
            ts.ImagePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(tsxPath)!, src));
            ts.ImageWidth = Int(img, "width");
            ts.ImageHeight = Int(img, "height");
        }
        return ts;
    }

    private static string Attr(XElement e, string name) => e.Attribute(name)?.Value ?? "";
    private static int Int(XElement e, string name) => int.TryParse(Attr(e, name), out var v) ? v : 0;
    private static float F(XElement e, string name, float def) =>
        float.TryParse(Attr(e, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;

    /// <summary>把地图烘成整图（Magick.NET）。返回生成的 PNG 路径。</summary>
    public static string Bake(TiledMap map, string outPngPath)
    {
        int w = map.Width * map.TileWidth;
        int h = map.Height * map.TileHeight;
        if (w <= 0 || h <= 0) throw new InvalidOperationException(L("地图尺寸无效"));
        if (w > 16384) w = 16384;
        if (h > 16384) h = 16384;

        // 载入 tileset 位图
        var images = new Dictionary<string, MagickImage>();
        foreach (var ts in map.Tilesets)
        {
            if (string.IsNullOrEmpty(ts.ImagePath) || !File.Exists(ts.ImagePath)) continue;
            if (!images.ContainsKey(ts.ImagePath))
                images[ts.ImagePath] = new MagickImage(ts.ImagePath);
        }

        using var canvas = new MagickImage(MagickColors.Transparent, (uint)w, (uint)h);

        foreach (var layer in map.TileLayers)
        {
            if (!layer.Visible || layer.Data.Length == 0) continue;
            for (int y = 0; y < layer.Height; y++)
            {
                for (int x = 0; x < layer.Width; x++)
                {
                    int idx = y * layer.Width + x;
                    if (idx >= layer.Data.Length) continue;
                    int gid = layer.Data[idx];
                    if (gid == 0) continue;

                    TilesetRef? owner = null;
                    foreach (var ts in map.Tilesets)
                        if (gid >= ts.FirstGid && (owner == null || ts.FirstGid > owner.FirstGid)) owner = ts;
                    if (owner == null || !images.TryGetValue(owner.ImagePath, out var src)) continue;

                    int local = gid - owner.FirstGid;
                    int cols = owner.Columns > 0 ? owner.Columns : Math.Max(1, (int)src.Width / Math.Max(1, owner.TileWidth));
                    int sx = (local % cols) * (owner.TileWidth + owner.Spacing) + owner.Margin;
                    int sy = (local / cols) * (owner.TileHeight + owner.Spacing) + owner.Margin;
                    if (sx < 0 || sy < 0 || sx + owner.TileWidth > src.Width || sy + owner.TileHeight > src.Height) continue;

                    using var tile = new MagickImage(src);
                    tile.Crop(new MagickGeometry(sx, sy, (uint)owner.TileWidth, (uint)owner.TileHeight));
                    if (owner.TileWidth != map.TileWidth || owner.TileHeight != map.TileHeight)
                        tile.Resize((uint)map.TileWidth, (uint)map.TileHeight);
                    canvas.Composite(tile, x * map.TileWidth, y * map.TileHeight, CompositeOperator.Over);
                }
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outPngPath)!);
        canvas.Write(outPngPath, MagickFormat.Png32);
        foreach (var b in images.Values) b.Dispose();
        return outPngPath;
    }

    /// <summary>导出地图数据 JSON（碰撞 / 对象 / 出生点 / 音乐）。</summary>
    public static string ExportData(TiledMap map, string imageName, int imgW, int imgH, string outJsonPath)
    {
        var root = new JsonObject
        {
            ["width"] = map.Width * map.TileWidth,
            ["height"] = map.Height * map.TileHeight,
            ["tileWidth"] = map.TileWidth,
            ["tileHeight"] = map.TileHeight,
            ["image"] = imageName,
            ["imageW"] = imgW,
            ["imageH"] = imgH
        };
        var props = new JsonObject();
        foreach (var kv in map.Properties) props[kv.Key] = kv.Value;
        root["properties"] = props;

        var groups = new JsonArray();
        foreach (var g in map.ObjectGroups)
        {
            var ga = new JsonArray();
            foreach (var o in g.Objects)
            {
                var oo = new JsonObject
                {
                    ["id"] = o.Id,
                    ["name"] = o.Name,
                    ["type"] = o.Type,
                    ["x"] = o.X,
                    ["y"] = o.Y,
                    ["w"] = o.W,
                    ["h"] = o.H,
                    ["point"] = o.IsPoint
                };
                var op = new JsonObject();
                foreach (var kv in o.Props) op[kv.Key] = kv.Value;
                oo["props"] = op;
                ga.Add(oo);
            }
            var go = new JsonObject { ["name"] = g.Name, ["objects"] = ga };
            groups.Add(go);
        }
        root["objectGroups"] = groups;

        // 碰撞矩形（collision 对象层）
        var coll = new JsonArray();
        foreach (var g in map.ObjectGroups)
        {
            if (!g.Name.Contains("collision", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var o in g.Objects)
                coll.Add(new JsonObject { ["x"] = o.X, ["y"] = o.Y, ["w"] = o.W, ["h"] = o.H });
        }
        root["collision"] = coll;

        Directory.CreateDirectory(Path.GetDirectoryName(outJsonPath)!);
        File.WriteAllText(outJsonPath, root.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }));
        return outJsonPath;
    }

    /// <summary>高层入口：转换一张地图 → (PNG, JSON)。返回 (pngPath, jsonPath)。</summary>
    public static (string png, string json, TiledMap map) Convert(string tmxOrLuaPath, string outDir, string mapId)
    {
        var map = ParseTmx(tmxOrLuaPath);
        Directory.CreateDirectory(outDir);
        var png = Path.Combine(outDir, mapId + ".png");
        Bake(map, png);
        int iw, ih;
        using (var b = new MagickImage(png)) { iw = (int)b.Width; ih = (int)b.Height; }
        var json = Path.Combine(outDir, mapId + ".map.json");
        ExportData(map, mapId, iw, ih, json);
        return (png, json, map);
    }
}