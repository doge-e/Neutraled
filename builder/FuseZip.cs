using System.IO.Compression;
using System.Text;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>
/// 往「融合版 LÖVE exe」（LÖVE fused build：PE 前缀 + 尾部内嵌 zip）里**追加**文件。
///
/// 用途：像「冰封帷幕」这种没有工程源码的成品 exe，mods/ 是**打包在 exe 内部**的，
///       磁盘上的 mods/ 目录根本不会被扫描（实测：放 mods/ntlprobe 进去毫无反应）
///       → 想给它加东西只能改它内嵌的那份 zip。
///
/// 做法（纯追加，不改任何旧条目）：
///   [PE 前缀][旧 zip 数据区][旧中央目录]  →  [PE 前缀][旧 zip 数据区][新条目][旧中央目录][新中央目录][EOCD]
///   旧条目的本地偏移不变（数据区没动过），所以完全兼容；中央目录本来就是允许不紧贴数据区的。
///
/// ⚠ 反复注入要用「原始 exe」做输入（本类会自动使用同目录的 .orig 备份），
///   否则同名条目会在 zip 里出现两次，行为取决于读取器（PhysFS 取第一个）→ 更新不生效。
/// </summary>
internal static class FuseZip
{
    public sealed record Entry(string Name, byte[] Data);

    private const uint SigLocal = 0x04034b50;
    private const uint SigCentral = 0x02014b50;
    private const uint SigEocd = 0x06054b50;

    private static void ReadFully(Stream s, byte[] buf)
    {
        int off = 0;
        while (off < buf.Length)
        {
            int n = s.Read(buf, off, buf.Length - off);
            if (n <= 0) throw new EndOfStreamException();
            off += n;
        }
    }

    private static void CopyRange(Stream src, Stream dst, long count, byte[] buf)
    {
        long left = count;
        while (left > 0)
        {
            int want = (int)Math.Min(buf.Length, left);
            int n = src.Read(buf, 0, want);
            if (n <= 0) throw new EndOfStreamException();
            dst.Write(buf, 0, n);
            left -= n;
        }
    }

    private static uint Crc32(byte[] data)
    {
        uint[]? table = _crcTable;
        if (table == null)
        {
            table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++) c = ((c & 1) != 0) ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
                table[i] = c;
            }
            _crcTable = table;
        }
        uint crc = 0xFFFFFFFFu;
        foreach (var b in data) crc = table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
    private static uint[]? _crcTable;

    private static byte[] Deflate(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var ds = new DeflateStream(ms, CompressionLevel.Optimal, true)) ds.Write(data, 0, data.Length);
        return ms.ToArray();
    }

    // ---------------------------------------------------------------- 读

    private sealed record CdEntry(string Name, uint Crc, uint CSize, uint USize, uint LocalOff, ushort Method, ushort Flags, int RecStart, int RecLen);

    /// <summary>定位 zip 在 exe 里的起始偏移，并返回中央目录原始字节 + 条目表。</summary>
    private static (long Base, uint CdOff, byte[] Cd, List<CdEntry> Entries, ushort CommentLen, byte[]? Comment) ReadIndex(Stream fs, long len)
    {
        int tailLen = (int)Math.Min(1 << 20, len);
        var tail = new byte[tailLen];
        fs.Position = len - tailLen;
        ReadFully(fs, tail);
        int eocd = -1;
        for (int i = tail.Length - 22; i >= 0; i--)
            if (BitConverter.ToUInt32(tail, i) == SigEocd) { eocd = i; break; }
        if (eocd < 0) throw new InvalidOperationException(L("没找到 zip 结束记录 —— 这不是融合版 LÖVE exe？"));
        uint cdSize = BitConverter.ToUInt32(tail, eocd + 12);
        uint cdOff = BitConverter.ToUInt32(tail, eocd + 16);
        ushort cmtLen = BitConverter.ToUInt16(tail, eocd + 20);
        long eocdAbs = (len - tailLen) + eocd;
        long baseOff = eocdAbs - cdSize - cdOff;
        if (baseOff < 0) throw new InvalidOperationException(L("zip 起始偏移异常"));

        var cd = new byte[cdSize];
        fs.Position = baseOff + cdOff;
        ReadFully(fs, cd);

        var list = new List<CdEntry>();
        int p = 0;
        while (p + 46 <= cd.Length && BitConverter.ToUInt32(cd, p) == SigCentral)
        {
            ushort flags = BitConverter.ToUInt16(cd, p + 8);
            ushort method = BitConverter.ToUInt16(cd, p + 10);
            uint crc = BitConverter.ToUInt32(cd, p + 16);
            uint csize = BitConverter.ToUInt32(cd, p + 20);
            uint usize = BitConverter.ToUInt32(cd, p + 24);
            ushort nameLen = BitConverter.ToUInt16(cd, p + 28);
            ushort extraLen = BitConverter.ToUInt16(cd, p + 30);
            ushort cmt = BitConverter.ToUInt16(cd, p + 32);
            uint lho = BitConverter.ToUInt32(cd, p + 42);
            string name = Encoding.UTF8.GetString(cd, p + 46, nameLen);
            if (csize == 0xFFFFFFFF || usize == 0xFFFFFFFF || lho == 0xFFFFFFFF)
                throw new InvalidOperationException(L("条目 {0} 用了 zip64，本工具不支持（避免写坏包）", name));
            list.Add(new CdEntry(name, crc, csize, usize, lho, method, flags, p, 46 + nameLen + extraLen + cmt));
            p += 46 + nameLen + extraLen + cmt;
        }
        byte[]? comment = null;
        if (cmtLen > 0 && eocdAbs + 22 + cmtLen <= len)
        {
            comment = new byte[cmtLen];
            fs.Position = eocdAbs + 22;
            ReadFully(fs, comment);
        }
        return (baseOff, cdOff, cd, list, cmtLen, comment);
    }

    public static List<string> ListNames(string exe)
    {
        using var fs = File.OpenRead(exe);
        var (_, _, _, entries, _, _) = ReadIndex(fs, fs.Length);
        return entries.Select(e => e.Name).ToList();
    }

    public static byte[]? ReadEntry(string exe, string name)
    {
        using var fs = File.OpenRead(exe);
        long len = fs.Length;
        var (baseOff, _, _, entries, _, _) = ReadIndex(fs, len);
        var e = entries.FirstOrDefault(x => x.Name == name);
        if (e == null) return null;
        fs.Position = baseOff + e.LocalOff;
        var lh = new byte[30];
        ReadFully(fs, lh);
        if (BitConverter.ToUInt32(lh, 0) != SigLocal) throw new InvalidOperationException(L("本地头损坏: ") + name);
        int nl = BitConverter.ToUInt16(lh, 26), el = BitConverter.ToUInt16(lh, 28);
        fs.Position = baseOff + e.LocalOff + 30 + nl + el;
        var raw = new byte[e.CSize];
        ReadFully(fs, raw);
        if (e.Method == 0) return raw;
        if (e.Method == 8)
        {
            using var ms = new MemoryStream();
            using (var ds = new DeflateStream(new MemoryStream(raw), CompressionMode.Decompress)) ds.CopyTo(ms);
            return ms.ToArray();
        }
        throw new InvalidOperationException(L("压缩方式不支持: ") + e.Method);
    }

    // ---------------------------------------------------------------- 写

    /// <summary>把若干文件追加进 exe 内嵌 zip，输出到 dst（src 与 dst 可以是同一个文件，会先读进临时缓冲）。</summary>
    public static void AddEntries(string src, string dst, IReadOnlyList<Entry> adds, ISet<string>? replace = null)
    {
        string tmp = dst + ".ntlnew";
        using (var fs = File.OpenRead(src))
        {
            long len = fs.Length;
            var (baseOff, oldCdOff, oldCd, entries, cmtLen, comment) = ReadIndex(fs, len);
            long dataEnd = baseOff + oldCdOff;   // 旧数据区末尾（旧中央目录 offset 都是相对 zip 起始的，可直接复用）

            using var o = File.Create(tmp);
            var buf = new byte[4 << 20];
            fs.Position = 0;
            CopyRange(fs, o, dataEnd, buf);                  // PE 前缀 + 旧数据区（原样）

            long newLocalStart = oldCdOff;
            long newLocalSize = 0;
            var newCd = new MemoryStream();
            var stamp = new byte[4];
            BitConverter.GetBytes((ushort)0).CopyTo(stamp, 0);        // time
            BitConverter.GetBytes((ushort)0x2821).CopyTo(stamp, 2);   // date 2000-01-01
            foreach (var a in adds)
            {
                var compressed = Deflate(a.Data);
                var nameBytes = Encoding.UTF8.GetBytes(a.Name);
                uint crc = Crc32(a.Data);
                long lho = newLocalStart + newLocalSize;

                using (var lh = new MemoryStream())
                {
                    void W16(ushort v) => lh.Write(BitConverter.GetBytes(v));
                    void W32(uint v) => lh.Write(BitConverter.GetBytes(v));
                    W32(SigLocal); W16(20); W16(0x0800); W16(8);
                    lh.Write(stamp); W32(crc);
                    W32((uint)compressed.Length); W32((uint)a.Data.Length);
                    W16((ushort)nameBytes.Length); W16(0);
                    lh.Write(nameBytes);
                    var hb = lh.ToArray();
                    o.Write(hb, 0, hb.Length);
                }
                o.Write(compressed, 0, compressed.Length);

                using (var ce = new MemoryStream())
                {
                    void W16(ushort v) => ce.Write(BitConverter.GetBytes(v));
                    void W32(uint v) => ce.Write(BitConverter.GetBytes(v));
                    W32(SigCentral); W16(20); W16(20); W16(0x0800); W16(8);
                    ce.Write(stamp); W32(crc);
                    W32((uint)compressed.Length); W32((uint)a.Data.Length);
                    W16((ushort)nameBytes.Length); W16(0); W16(0); W16(0); W16(0);
                    W32(0); W32((uint)lho);
                    ce.Write(nameBytes);
                    newCd.Write(ce.GetBuffer(), 0, (int)ce.Length);   // ⚠ 不能用 ce.CopyTo：它从「当前位置」复制，而位置已经在末尾 → 复制 0 字节（踩过）
                }
                newLocalSize += 30 + nameBytes.Length + compressed.Length;
            }

            // 旧中央目录：逐条原样照抄，但**被替换的条目不再列出**（同名条目只能有一个，
            // 否则取哪一个取决于读取器实现 —— PhysFS 取先出现的那个，更新就不会生效）。
            int kept = 0; long keptCdSize = 0;
            foreach (var en in entries)
            {
                if (replace != null && replace.Contains(en.Name)) continue;
                o.Write(oldCd, en.RecStart, en.RecLen);
                kept++; keptCdSize += en.RecLen;
            }
            newCd.Position = 0;
            newCd.CopyTo(o);                                 // 新中央目录

            int total = kept + adds.Count;
            using (var ec = new MemoryStream())
            {
                void W16(ushort v) => ec.Write(BitConverter.GetBytes(v));
                void W32(uint v) => ec.Write(BitConverter.GetBytes(v));
                W32(SigEocd); W16(0); W16(0);
                W16((ushort)total); W16((ushort)total);
                W32((uint)keptCdSize + (uint)newCd.Length);
                W32((uint)(oldCdOff + newLocalSize));
                W16(cmtLen);
                if (comment != null) ec.Write(comment, 0, comment.Length);
                var eb = ec.ToArray();
                o.Write(eb, 0, eb.Length);
            }
        }
        try
        {
            if (File.Exists(dst)) File.Delete(dst);
            File.Move(tmp, dst);
        }
        finally
        {
            // ⚠ 目标被占用时会抛异常；不清理就会留下一个 105MB 的 .ntlnew 垃圾（实测踩过）
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    // ---------------------------------------------------------------- 注入控制台

    /// <summary>给融合版 Kristal exe 注入 Neutraled 控制台（纯追加；自动用 .orig 原版做输入）。</summary>
    public static int InjectConsole(string exePath, string libDir, string? fontPath)
    {
        exePath = Path.GetFullPath(exePath);
        if (!File.Exists(exePath)) { Console.WriteLine(L("  [错误] 找不到 {0}", exePath)); return 1; }
        string orig = exePath + ".orig";
        string src = File.Exists(orig) ? orig : exePath;      // 永远从原始 exe 出发 → 可反复注入
        if (!File.Exists(orig))
        {
            Console.WriteLine(L("  备份原版 → {0}", Path.GetFileName(orig)));
            File.Copy(exePath, orig);
        }

        Console.WriteLine(L("===== 给融合版 Kristal 注入控制台 ====="));
        Console.WriteLine(L("  输入: {0}", src));
        List<string> names;
        try { names = ListNames(src); }
        catch (Exception ex) { Console.WriteLine(L("  [错误] ") + ex.Message); return 1; }

        var modJsonNames = names.Where(n => n.StartsWith("mods/") && n.EndsWith("/mod.json")).ToList();
        if (modJsonNames.Count == 0) { Console.WriteLine(L("  [错误] 这个 exe 里没有 mods/*/mod.json —— 不是 Kristal 融合包？")); return 1; }
        var modDir = modJsonNames.OrderBy(n => n.Length).First();      // 路径最短 = 最外层 mod
        modDir = modDir[..modDir.LastIndexOf('/')];
        Console.WriteLine(L("  激活 mod 目录: {0}", modDir));

        var adds = new List<Entry>();
        var replace = new HashSet<string>();
        string libLua = Path.Combine(libDir, "lib.lua");
        string libJson = Path.Combine(libDir, "lib.json");
        if (!File.Exists(libLua)) { Console.WriteLine(L("  [错误] 缺少 {0}", libLua)); return 1; }

        // ① 控制台本体 → src/ntlconsole.lua
        var luaBytes = ConsoleLuaBytes(libLua);
        adds.Add(new Entry("src/ntlconsole.lua", luaBytes));
        Console.WriteLine(L("  + src/ntlconsole.lua  ({0} 字节)", luaBytes.Length));

        // ② **开机就加载**：在 main.lua 末尾追加一行 require。
        //    ⚠ 只放进 libraries/ 是不够的：Kristal 只有在「玩家真正进入那个 mod」时才执行 lib.lua，
        //      主菜单里按 F2 会毫无反应（实测）。所以要在 main.lua 这个**启动路径**上挂钩。
        var mainRaw = ReadEntry(src, "main.lua");
        if (mainRaw == null) { Console.WriteLine(L("  [错误] 包里没有 main.lua，无法挂启动钩子")); return 1; }
        string mainTxt = Encoding.UTF8.GetString(mainRaw);
        if (mainTxt.Contains("src.ntlconsole") || mainTxt.Contains("ntl-console.lua"))
            Console.WriteLine(L("  = main.lua 已挂过钩子（沿用）"));
        else
        {
            // 钩子优先加载 **exe 同目录的 ntl-console.lua**：这样以后改控制台只要覆盖那个文件，
            // 不用再动 exe（exe 在游戏运行时是锁住的，改一次就得让玩家先退出 —— 实测踩过）。
            var hook = string.Join("\n", new[]
            {
                "",
                "",
                "-- Neutraled 控制台（注入）：F2 开关",
                "-- 优先加载 exe 同目录的 ntl-console.lua（可热更新），没有则用包内副本。",
                "do",
                "    local __ntl_ok, __ntl_err = pcall(function()",
                "        local __ntl_src = love.filesystem.getSource()",
                "        local __ntl_dir = __ntl_src:match(\"^(.*)[/\\\\][^/\\\\]*$\") or \".\"",
                "        local __ntl_f = io.open(__ntl_dir .. \"/ntl-console.lua\", \"rb\")",
                "        if __ntl_f then",
                "            local __ntl_code = __ntl_f:read(\"*a\")",
                "            __ntl_f:close()",
                "            local __ntl_load = loadstring or load",
                "            local __ntl_chunk, __ntl_cerr = __ntl_load(__ntl_code, \"@ntl-console.lua\")",
                "            if not __ntl_chunk then error(\"外部 ntl-console.lua 编译失败: \" .. tostring(__ntl_cerr)) end",
                "            __ntl_chunk()",
                "        else",
                "            require(\"src.ntlconsole\")",
                "        end",
                "    end)",
                "    if not __ntl_ok then",
                "        local __ntl_p = (love.filesystem.getSaveDirectory() or \".\") .. \"/ntlconsole.log\"",
                "        local __ntl_g = io.open(__ntl_p, \"a\")",
                "        if __ntl_g then __ntl_g:write(\"[注入] 控制台加载失败: \" .. tostring(__ntl_err) .. \"\\n\") __ntl_g:close() end",
                "    end",
                "end",
                "",
            });
            mainTxt = mainTxt.TrimEnd() + "\n" + hook;
            adds.Add(new Entry("main.lua", Encoding.UTF8.GetBytes(mainTxt)));
            replace.Add("main.lua");
            Console.WriteLine(L("  ~ main.lua 末尾挂启动钩子（{0} → {1} 字节）", mainRaw.Length, Encoding.UTF8.GetByteCount(mainTxt)));
        }

        // ③ 同时把控制台源码写到 exe 同目录（钩子优先读它 → 以后改控制台不用再动 exe）
        try
        {
            var extPath = Path.Combine(Path.GetDirectoryName(exePath) ?? ".", "ntl-console.lua");
            File.WriteAllBytes(extPath, luaBytes);
            Console.WriteLine(L("  + {0}  （热更新入口：改它即可，不必再注入）", extPath));
        }
        catch (Exception ex) { Console.WriteLine(L("  [警告] 写外部 ntl-console.lua 失败: ") + ex.Message); }

        // ③ 冗余保险：同时作为 mod 库挂一份（挂过了就自动跳过，见 __NTL_CONSOLE_HOOKED）
        adds.Add(new Entry($"{modDir}/libraries/ntlconsole/lib.lua", luaBytes));
        adds.Add(new Entry($"{modDir}/libraries/ntlconsole/lib.json",
            File.Exists(libJson) ? File.ReadAllBytes(libJson) : Encoding.UTF8.GetBytes("{\n    \"id\": \"ntlconsole\"\n}\n")));
        Console.WriteLine(L("  + {0}/libraries/ntlconsole/lib.lua  ({1} 字节，冗余保险)", modDir, luaBytes.Length));

        if (fontPath != null && File.Exists(fontPath))
        {
            if (names.Contains("assets/fonts/ntl_cjk.ttf"))
                Console.WriteLine(L("  = assets/fonts/ntl_cjk.ttf 已存在（沿用）"));
            else
            {
                adds.Add(new Entry("assets/fonts/ntl_cjk.ttf", File.ReadAllBytes(fontPath)));
                Console.WriteLine(L("  + assets/fonts/ntl_cjk.ttf  ({0} 字节，中文字形)", new FileInfo(fontPath).Length));
            }
        }
        else Console.WriteLine(L("  [警告] 没找到中文字体源，控制台中文会缺字"));

        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            AddEntries(src, exePath, adds, replace);
            sw.Stop();
            var fi = new FileInfo(exePath);
            Console.WriteLine(L("  ✅ 写回 {0}（{1:F1} MB，{2} ms）", exePath, fi.Length / 1048576.0, sw.ElapsedMilliseconds));
            var after = ListNames(exePath);
            foreach (var a in adds)
                Console.WriteLine(L("     验证: {0} → {1}", a.Name, (after.Contains(a.Name) ? L("在包里 ✓") : L("缺失 ✗"))));
            Console.WriteLine(L("     条目数 {0} → {1}", names.Count, after.Count));
        }
        catch (Exception ex) { Console.WriteLine(L("  [错误] 注入失败: ") + ex.Message); return 1; }
        return 0;
    }

    /// <summary>守候在**拉起外部引擎之前**调用：确保融合版 Kristal exe 里装着最新控制台。
    /// 为什么放在这里：此刻 exe 一定没在运行 → 文件不会被占用
    /// （游戏运行时 exe 是锁住的，注入会失败 —— 实测踩过，所以"注入必须赶在启动前"）。
    /// 顺带刷新 exe 同目录的 ntl-console.lua（热更新入口，钩子优先读它，以后改控制台不必再动 exe）。</summary>
    public static void EnsureConsole(string exe, string libDir, string? fontPath)
    {
        try
        {
            if (!File.Exists(exe)) return;
            var libLua = Path.Combine(libDir, "lib.lua");
            if (!File.Exists(libLua)) return;
            var want = ConsoleLuaBytes(libLua);

            List<string> names;
            try { names = ListNames(exe); } catch { return; }              // 不是融合包 → 不管
            if (!names.Contains("main.lua")) return;
            if (!names.Any(n => n.StartsWith("mods/") && n.EndsWith("/mod.json"))) return;   // 不是 Kristal 融合包

            var have = ReadEntry(exe, "src/ntlconsole.lua");
            var mainRaw = ReadEntry(exe, "main.lua");
            bool same = have != null && have.Length == want.Length && have.AsSpan().SequenceEqual(want);
            bool hooked = mainRaw != null && Encoding.UTF8.GetString(mainRaw).Contains("ntl-console.lua");

            if (same && hooked) Console.WriteLine(L("  [控制台] 已是最新（F2 打开）"));
            else
            {
                Console.WriteLine(L("  [控制台] 给外部引擎装配/更新 Neutraled 控制台 …"));
                var rc = InjectConsole(exe, libDir, fontPath);
                Console.WriteLine(rc == 0 ? L("  [控制台] ✅ 完成 —— 进游戏后按 F2") : L("  [控制台] ⚠ 注入失败，照常启动（不影响玩）"));
            }
            try
            {
                var dir = Path.GetDirectoryName(exe);
                if (!string.IsNullOrEmpty(dir)) File.WriteAllBytes(Path.Combine(dir, "ntl-console.lua"), want);
            }
            catch { }
        }
        catch (Exception ex) { Console.WriteLine(L("  [控制台] 跳过: ") + ex.Message); }
    }

    /// <summary>读控制台源码 lib.lua，并把文案占位符 @@NTL_LANG@@ 换成当前语言（zh / en）。
    /// 为什么烧进脚本：控制台跑在游戏内的 Lua 环境里，够不到 builder 的 Lang 表，
    /// 而注入发生在「知道 config.json 语言」的时刻（Lang.Init 已在 Main 最前面跑过）。
    /// 源码里没有占位符时按原始字节返回（老版脚本照样能注入）。</summary>
    static byte[] ConsoleLuaBytes(string libLuaPath)
    {
        var bytes = File.ReadAllBytes(libLuaPath);
        const string mark = "@@NTL_LANG@@";
        string text = Encoding.UTF8.GetString(bytes);
        if (!text.Contains(mark)) return bytes;
        text = text.Replace(mark, Lang.Current);
        return new UTF8Encoding(false).GetBytes(text);
    }

    /// <summary>从 .orig 还原（撤销注入）。</summary>
    public static int Restore(string exePath)
    {
        exePath = Path.GetFullPath(exePath);
        string orig = exePath + ".orig";
        if (!File.Exists(orig)) { Console.WriteLine(L("  [提示] 没有备份 {0}，无需还原", orig)); return 0; }
        File.Copy(orig, exePath, true);
        Console.WriteLine(L("  ✅ 已从 {0} 还原 {1}", Path.GetFileName(orig), Path.GetFileName(exePath)));
        return 0;
    }
}
