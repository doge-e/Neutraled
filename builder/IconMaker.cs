using ImageMagick;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>生成 Mod 管理器图标（多尺寸 .ico）。
/// 美术：深蓝底 + 白色像素风 "N" + 品红插件方块（呼应"外部章节"的配色）。
/// ⚠ Magick.NET 14 没有 Drawables（踩过），所以全部用**矩形合成**画；
///   每个尺寸都按同一套 32 格设计坐标乘缩放系数重画，保证像素画风清晰。</summary>
public static class IconMaker
{
    private static readonly int[] Sizes = { 16, 24, 32, 48, 64, 128, 256 };

    public static int Run(string outPath)
    {
        var pngs = new List<(int size, byte[] data)>();
        foreach (var s in Sizes)
        {
            using var img = Render(s);
            img.Format = MagickFormat.Png32;
            pngs.Add((s, img.ToByteArray()));
        }
        var ico = PackIco(pngs);
        var full = Path.GetFullPath(outPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, ico);
        Console.WriteLine(L("✓ 图标已生成: {0}（{1} 个尺寸，{2} KB）", full, pngs.Count, ico.Length / 1024));
        return 0;
    }

    private static void Box(MagickImage canvas, double x, double y, double w, double h, string color, double scale)
    {
        int ix = (int)Math.Round(x * scale), iy = (int)Math.Round(y * scale);
        int iw = Math.Max(1, (int)Math.Round(w * scale)), ih = Math.Max(1, (int)Math.Round(h * scale));
        if (ix >= canvas.Width || iy >= canvas.Height) return;
        iw = Math.Min(iw, (int)canvas.Width - ix);
        ih = Math.Min(ih, (int)canvas.Height - iy);
        if (iw <= 0 || ih <= 0) return;
        using var rect = new MagickImage(new MagickColor(color), (uint)iw, (uint)ih);
        canvas.Composite(rect, ix, iy, CompositeOperator.Over);
    }

    private static MagickImage Render(int size)
    {
        var img = new MagickImage(MagickColors.Transparent, (uint)size, (uint)size);
        double s = size / 32.0;
        // 底：深蓝 + 蓝灰描边
        Box(img, 1, 1, 30, 30, "#2b3a67", s);
        Box(img, 2, 2, 28, 28, "#12162e", s);
        // 像素 "N"：左竖 / 右竖 / 7 级斜杠
        const string W = "#f2f5ff";
        Box(img, 8, 7, 3, 18, W, s);
        Box(img, 21, 7, 3, 18, W, s);
        for (int i = 0; i < 7; i++)
            Box(img, 10 + i * 1.85, 7 + i * 2.55, 2.4, 2.8, W, s);
        // 品红插件方块（右下角）
        Box(img, 23, 23, 9, 9, "#ff2d95", s);
        Box(img, 25, 25, 5, 5, "#12162e", s);
        Box(img, 26.5, 26.5, 2, 2, "#ff2d95", s);
        return img;
    }

    /// <summary>手写 ICO 容器：ICONDIR + ICONDIRENTRY[] + 各尺寸 PNG 数据。</summary>
    private static byte[] PackIco(List<(int size, byte[] data)> pngs)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((ushort)0);
        w.Write((ushort)1);
        w.Write((ushort)pngs.Count);
        int offset = 6 + 16 * pngs.Count;
        foreach (var (size, data) in pngs)
        {
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((ushort)1);
            w.Write((ushort)32);
            w.Write(data.Length);
            w.Write(offset);
            offset += data.Length;
        }
        foreach (var (_, data) in pngs) w.Write(data);
        w.Flush();
        return ms.ToArray();
    }
}
