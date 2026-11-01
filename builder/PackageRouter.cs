using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>C4 P1-3：包型探测的唯一实现。
///
/// 输入一个本地文件/目录，输出「包型 + 建议导入器 + 理由」，供
/// <c>--route-probe</c>（只读）与 <c>--install-id/--install-url</c>（下载后）共用。
///
/// 探测规则（文档化在 docs/CLI_REFERENCE.md 与 C4-p12-p13.md）：
/// <list type="number">
/// <item>先看魔数：VCDIFF(<c>D6 C3 C4 00</c>) → xdelta；<c>FORM</c>+<c>GEN8</c> → data.win；</item>
/// <item>再看 zip/x.ntlmod 内容：mod.lua 或 mod.json 的 engineVer → kristal；
///       exe+运行库 → external；*.xdelta → xdelta；只有 data.win → datawin；
///       data.win+其它负载 → gm3p；只有 mod.json → ntlmod；</item>
/// <item>目录：mod.lua/engineVer → kristal；成品程序 → external；mod.json → ntlmod；
///       *.xdelta → xdelta；data.win 单独 → datawin、带其它文件 → gm3p；</item>
/// <item>最后才按扩展名兜底（.xdelta/.win/.ntlmod/.exe/.lua），无法识别的给 unknown 并提示 --as。</item>
/// </list>
/// <c>--as &lt;kind&gt;</c> 可以覆盖探测结果（Forced=true），用于误判时强制走某条导入路径。</summary>
public static class PackageRouter
{
    public const string Xdelta = "xdelta";
    public const string DataWin = "datawin";
    public const string Ntlmod = "ntlmod";
    public const string Kristal = "kristal";
    public const string Gm3p = "gm3p";
    public const string External = "external";
    public const string Unknown = "unknown";

    public static readonly string[] Kinds =
        { Xdelta, DataWin, Ntlmod, Kristal, Gm3p, External, Unknown };

    private static readonly HashSet<string> RuntimeDlls = new(StringComparer.OrdinalIgnoreCase)
    {
        "love.dll", "SDL2.dll", "OpenAL32.dll", "lua51.dll", "msvcp120.dll",
    };

    public sealed record ProbeResult(
        string Path, string Kind, string Importer, string ReasonCode, bool Forced, bool IsDirectory);

    public static bool IsValidKind(string? kind) =>
        !string.IsNullOrWhiteSpace(kind) &&
        Kinds.Any(k => string.Equals(k, kind, StringComparison.OrdinalIgnoreCase));

    public static string ImporterFor(string kind) => kind switch
    {
        Xdelta or DataWin => "delta",
        Ntlmod or Kristal or Gm3p or External => "modding",
        _ => "none",
    };

    public static string KindList => string.Join("|", Kinds);

    public static ProbeResult Probe(string path, string? forcedKind = null)
    {
        var full = Path.GetFullPath(path);
        bool isDir = Directory.Exists(full);
        string kind;
        string reason;
        if (!string.IsNullOrWhiteSpace(forcedKind))
        {
            kind = forcedKind.Trim().ToLowerInvariant();
            reason = "forced";
        }
        else if (isDir)
        {
            (kind, reason) = ProbeDirectory(full);
        }
        else
        {
            (kind, reason) = ProbeFile(full);
        }
        return new ProbeResult(full, kind, ImporterFor(kind), reason,
            !string.IsNullOrWhiteSpace(forcedKind), isDir);
    }

    // ---------------- 目录 ----------------

    private static (string Kind, string Reason) ProbeDirectory(string dir)
    {
        var files = SafeFiles(dir);
        bool hasModLua = files.Any(f => Name(f).Equals("mod.lua", StringComparison.OrdinalIgnoreCase));
        var modJsons = files.Where(f => Name(f).Equals("mod.json", StringComparison.OrdinalIgnoreCase)).ToList();
        if (hasModLua) return (Kristal, "dir_kristal_mod_lua");
        if (modJsons.Any(f => DeclaresEngineVer(f))) return (Kristal, "dir_kristal_enginever");

        if (ModdingImport.LooksLikeStandaloneBuild(dir, out _)) return (External, "dir_external");
        if (modJsons.Count > 0) return (Ntlmod, "dir_ntlmod");
        if (files.Any(f => f.EndsWith(".xdelta", StringComparison.OrdinalIgnoreCase)))
            return (Xdelta, "dir_xdelta");

        var dataWins = files.Where(f => Name(f).Equals("data.win", StringComparison.OrdinalIgnoreCase)).ToList();
        if (dataWins.Count == 1 && files.Count == 1) return (DataWin, "dir_datawin_only");
        if (dataWins.Count > 0) return (Gm3p, "dir_gm3p");
        return (Unknown, "unknown");
    }

    // ---------------- 文件 ----------------

    private static (string Kind, string Reason) ProbeFile(string file)
    {
        var name = Name(file);
        var ext = Path.GetExtension(file).ToLowerInvariant();
        var magic = ReadMagic(file, 16);

        if (IsXdeltaMagic(magic)) return (Xdelta, "file_xdelta_magic");
        if (IsDataWinMagic(magic)) return (DataWin, "file_datawin_magic");
        if (ext == ".xdelta") return (Xdelta, "file_xdelta_ext");
        if (name.Equals("data.win", StringComparison.OrdinalIgnoreCase) || ext == ".win")
            return (DataWin, "file_datawin_name");

        if (ext is ".zip" or ".ntlmod")
        {
            if (TryProbeZip(file, out var kind, out var reason)) return (kind, reason);
            if (ext == ".ntlmod") return (Ntlmod, "ext_ntlmod");
            return (Unknown, "unknown");
        }

        if (ext is ".7z" or ".rar" or ".tar" or ".gz" or ".tgz" or ".bz2")
            return (Unknown, "archive_uninspected");
        if (ext == ".exe") return (External, "file_external_exe");
        if (ext == ".lua") return (Kristal, "file_kristal_lua");
        return (Unknown, "unknown");
    }

    private static bool TryProbeZip(string file, out string kind, out string reason)
    {
        kind = Unknown;
        reason = "unknown";
        try
        {
            using var zip = ZipFile.OpenRead(file);
            var names = zip.Entries
                .Where(e => !string.IsNullOrEmpty(e.Name))
                .Select(e => e.FullName.Replace('\\', '/'))
                .ToList();
            if (names.Count == 0) return false;

            bool Has(string simple) => names.Any(n => n.Equals(simple, StringComparison.OrdinalIgnoreCase) ||
                                                      n.EndsWith("/" + simple, StringComparison.OrdinalIgnoreCase));
            if (Has("mod.lua")) { kind = Kristal; reason = "zip_kristal_mod_lua"; return true; }

            var modJson = zip.Entries.FirstOrDefault(e =>
                e.Name.Equals("mod.json", StringComparison.OrdinalIgnoreCase));
            if (modJson != null && EntryDeclaresEngineVer(modJson))
            {
                kind = Kristal;
                reason = "zip_kristal_enginever";
                return true;
            }

            bool hasExe = names.Any(n => n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
            bool hasRuntime = names.Any(n => RuntimeDlls.Contains(Name(n)));
            if (hasExe && hasRuntime) { kind = External; reason = "zip_external"; return true; }
            if (names.Any(n => n.EndsWith(".xdelta", StringComparison.OrdinalIgnoreCase)))
            {
                kind = Xdelta;
                reason = "zip_xdelta";
                return true;
            }

            bool hasDataWin = Has("data.win");
            if (hasDataWin && names.Count == 1) { kind = DataWin; reason = "zip_datawin_only"; return true; }
            if (hasDataWin) { kind = Gm3p; reason = "zip_gm3p"; return true; }
            if (modJson != null) { kind = Ntlmod; reason = "zip_ntlmod"; return true; }
        }
        catch
        {
            return false;
        }
        return false;
    }

    private static bool EntryDeclaresEngineVer(ZipArchiveEntry e)
    {
        try
        {
            if (e.Length > 2 * 1024 * 1024) return false;
            using var s = e.Open();
            using var r = new StreamReader(s);
            return r.ReadToEnd().Contains("engineVer", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    // ---------------- CLI ----------------

    public static int Cli(string? rawPath, string? forced, bool json)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            var msg = L("[错误] 缺少路径（用法: --route-probe <路径> [--as {0}] [--json]）", KindList);
            if (json) Console.Error.WriteLine(msg); else Console.WriteLine(msg);
            return 2;
        }
        if (!string.IsNullOrWhiteSpace(forced) && !IsValidKind(forced))
        {
            var msg = L("[错误] --as 的取值非法: {0}（可用 {1}）", forced, KindList);
            if (json) Console.Error.WriteLine(msg); else Console.WriteLine(msg);
            return 2;
        }

        var full = Path.GetFullPath(rawPath);
        if (!File.Exists(full) && !Directory.Exists(full))
        {
            var msg = L("[错误] 找不到路径: {0}", full);
            if (json) Console.Error.WriteLine(msg); else Console.WriteLine(msg);
            return 2;
        }

        var p = Probe(full, forced);
        if (json)
        {
            GbSearch.WriteJson(new
            {
                schemaVersion = 1,
                path = p.Path,
                kind = p.Kind,
                importer = p.Importer,
                reason = p.ReasonCode,
                reasonText = ReasonText(p.ReasonCode),
                forced = p.Forced,
                isDirectory = p.IsDirectory,
            });
            return 0;
        }

        Console.WriteLine(L("包探测: {0}", p.Path));
        Console.WriteLine(L("  包型: {0}", p.Kind));
        Console.WriteLine(L("  建议导入器: {0}", p.Importer));
        Console.WriteLine(L("  理由: {0}", ReasonText(p.ReasonCode)));
        if (p.Forced) Console.WriteLine(L("  强制: 是（--as {0}）", p.Kind));
        return 0;
    }

    public static string ReasonText(string code) => code switch
    {
        "dir_kristal_mod_lua" => L("目录里有 mod.lua（Kristal 工程）"),
        "dir_kristal_enginever" => L("mod.json 声明了 engineVer（Kristal mod）"),
        "dir_external" => L("目录是成品程序（exe + 运行库，无 mod 签名）"),
        "dir_ntlmod" => L("目录里有 mod.json（Neutraled / 传统 mod 包）"),
        "dir_xdelta" => L("目录里有 .xdelta 补丁"),
        "dir_datawin_only" => L("目录里只有 data.win（修改版数据文件）"),
        "dir_gm3p" => L("目录里有 data.win 及其它负载（GM3P 整包）"),
        "file_xdelta_magic" => L("文件以 xdelta 魔数开头"),
        "file_xdelta_ext" => L("文件扩展名是 .xdelta"),
        "file_datawin_magic" => L("文件头是 GameMaker FORM/GEN8"),
        "file_datawin_name" => L("文件名是 data.win"),
        "zip_kristal_mod_lua" => L("zip 内有 mod.lua（Kristal）"),
        "zip_kristal_enginever" => L("zip 内的 mod.json 声明了 engineVer（Kristal）"),
        "zip_external" => L("zip 内有 exe 与运行库（外部引擎包）"),
        "zip_xdelta" => L("zip 内有 .xdelta 补丁"),
        "zip_datawin_only" => L("zip 内只有 data.win（修改版数据文件）"),
        "zip_gm3p" => L("zip 内有 data.win 及其它文件（GM3P 整包）"),
        "zip_ntlmod" => L("zip 内有 mod.json（Neutraled mod 包）"),
        "ext_ntlmod" => L("按扩展名判定为 .ntlmod 包"),
        "file_external_exe" => L("可执行文件（外部引擎包）"),
        "file_kristal_lua" => L("Lua 文件（Kristal 脚本）"),
        "archive_uninspected" => L("该压缩格式无法在不下载/解压时探测（可用 --as 指定）"),
        "forced" => L("由 --as 强制指定"),
        _ => L("无法识别的包（可用 --as 指定）"),
    };

    // ---------------- 工具 ----------------

    private static string Name(string path) => Path.GetFileName(path);

    private static List<string> SafeFiles(string dir)
    {
        try { return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToList(); }
        catch { return new List<string>(); }
    }

    private static bool DeclaresEngineVer(string modJson)
    {
        try
        {
            var info = new FileInfo(modJson);
            if (info.Length > 2 * 1024 * 1024) return false;
            return File.ReadAllText(modJson).Contains("engineVer", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static byte[] ReadMagic(string file, int count)
    {
        try
        {
            using var fs = File.OpenRead(file);
            var buf = new byte[count];
            int n = fs.Read(buf, 0, buf.Length);
            return n == count ? buf : buf.Take(n).ToArray();
        }
        catch { return Array.Empty<byte>(); }
    }

    private static bool IsXdeltaMagic(byte[] b) =>
        b.Length >= 4 && b[0] == 0xD6 && b[1] == 0xC3 && b[2] == 0xC4 && b[3] == 0x00;

    private static bool IsDataWinMagic(byte[] b) =>
        b.Length >= 12 &&
        b[0] == (byte)'F' && b[1] == (byte)'O' && b[2] == (byte)'R' && b[3] == (byte)'M' &&
        b[8] == (byte)'G' && b[9] == (byte)'E' && b[10] == (byte)'N' && b[11] == (byte)'8';
}
