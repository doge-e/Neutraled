using System.IO.Compression;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>极简 PNG 写出器（RGBA8 / 非隔行 / 每行过滤字节 0）+ 一个够用的像素画布。
/// 不引入任何图像库：IDAT 用 <see cref="ZLibStream"/> 直接产 zlib 流，四个 chunk 手写并附 CRC32。
/// 用途：--new-chapter 生成"空白章节"自带的房间底图 —— 让新章节在没有任何美术资源时也能看得见东西。</summary>
public static class PngWrite
{
    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = ((c & 1) != 0) ? 0xEDB88320u ^ (c >> 1) : (c >> 1);
            t[n] = c;
        }
        return t;
    }

    private static uint Crc32(byte[] data)
    {
        uint c = 0xFFFFFFFFu;
        for (int i = 0; i < data.Length; i++) c = CrcTable[(c ^ data[i]) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    private static void BeUInt(byte[] buf, int off, uint v)
    {
        buf[off] = (byte)(v >> 24); buf[off + 1] = (byte)(v >> 16);
        buf[off + 2] = (byte)(v >> 8); buf[off + 3] = (byte)v;
    }

    private static void WriteChunk(Stream s, string type, byte[] payload)
    {
        var len = new byte[4];
        BeUInt(len, 0, (uint)payload.Length);
        s.Write(len, 0, 4);
        var body = new byte[4 + payload.Length];
        for (int i = 0; i < 4; i++) body[i] = (byte)type[i];
        Buffer.BlockCopy(payload, 0, body, 4, payload.Length);
        s.Write(body, 0, body.Length);
        var crc = new byte[4];
        BeUInt(crc, 0, Crc32(body));
        s.Write(crc, 0, 4);
    }

    /// <summary>写出 PNG。rgba 长度必须 == w*h*4（逐行、逐像素 R,G,B,A）。返回写出的字节数。</summary>
    public static long Save(string path, int w, int h, byte[] rgba)
    {
        if (w <= 0 || h <= 0) throw new ArgumentException(L("PNG 尺寸必须为正"));
        if (rgba.Length != w * h * 4) throw new ArgumentException(L("RGBA 缓冲区长度与尺寸不符"));
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        // 原始扫描线：每行 1 字节过滤类型 0 + w*4 字节
        var raw = new byte[h * (1 + w * 4)];
        for (int y = 0; y < h; y++)
        {
            int o = y * (1 + w * 4);
            raw[o] = 0;
            Buffer.BlockCopy(rgba, y * w * 4, raw, o + 1, w * 4);
        }
        byte[] zlib;
        using (var ms = new MemoryStream())
        {
            using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true)) z.Write(raw, 0, raw.Length);
            zlib = ms.ToArray();
        }

        var ihdr = new byte[13];
        BeUInt(ihdr, 0, (uint)w); BeUInt(ihdr, 4, (uint)h);
        ihdr[8] = 8;    // bit depth
        ihdr[9] = 6;    // color type: RGBA
        ihdr[10] = 0;   // compression
        ihdr[11] = 0;   // filter
        ihdr[12] = 0;   // interlace

        using var fs = File.Create(full);
        fs.Write(Signature, 0, Signature.Length);
        WriteChunk(fs, "IHDR", ihdr);
        WriteChunk(fs, "IDAT", zlib);
        WriteChunk(fs, "IEND", Array.Empty<byte>());
        return fs.Length;
    }

    /// <summary>行优先 RGBA 画布（原点是左上角）。</summary>
    public sealed class Canvas
    {
        public readonly int W, H;
        public readonly byte[] Px;

        public Canvas(int w, int h) { W = w; H = h; Px = new byte[w * h * 4]; }

        public void Pixel(int x, int y, int r, int g, int b, int a = 255)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return;
            int o = (y * W + x) * 4;
            Px[o] = (byte)r; Px[o + 1] = (byte)g; Px[o + 2] = (byte)b; Px[o + 3] = (byte)a;
        }

        public void Fill(int x, int y, int w, int h, int r, int g, int b, int a = 255)
        {
            for (int yy = y; yy < y + h; yy++)
                for (int xx = x; xx < x + w; xx++) Pixel(xx, yy, r, g, b, a);
        }

        /// <summary>矩形描边（线宽 t，向内）。</summary>
        public void Frame(int x, int y, int w, int h, int r, int g, int b, int t = 2, int a = 255)
        {
            Fill(x, y, w, t, r, g, b, a);
            Fill(x, y + h - t, w, t, r, g, b, a);
            Fill(x, y, t, h, r, g, b, a);
            Fill(x + w - t, y, t, h, r, g, b, a);
        }
    }
}
