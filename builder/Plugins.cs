using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

// 插件契约（PluginPermissions / PluginStates / PluginManifest / INeutraledPlugin / IPluginContext /
// PluginHooks / IHookDeclaringPlugin / PluginBase）的**唯一来源**是 sdk\Neutraled.PluginSdk\Contract.cs，
// 由 builder\Neutraled.Builder.csproj 用 <Compile Include ... Link> 链接编译进主程序。
// 两边程序集同名（ntl-builder）→ 插件 DLL 里的 TypeRef 在运行时被 PluginHost 的 Resolving 钩子
// 解析回主程序那一份类型，只有一份 INeutraledPlugin。
// 本文件只保留宿主侧逻辑：扫描 / 启停 / 安装 / 打印。

/// <summary>一次 plugins/* 扫描的结果。</summary>
public sealed class PluginEntry
{
    public string Id { get; set; } = "";
    public string Dir { get; set; } = "";
    /// <summary>见 PluginStates。</summary>
    public string State { get; set; } = "";
    public PluginManifest? Manifest { get; set; }
    /// <summary>人可读的问题清单（不阻断扫描，供 --plugin-list / --plugin-info 打印）。</summary>
    public List<string> Diagnostics { get; set; } = new();

    /// <summary>是否处于启用态（只有 State == enabled 才为真）。</summary>
    public bool IsEnabled => string.Equals(State, PluginStates.Enabled, StringComparison.Ordinal);
}

/// <summary>C# DLL 插件系统（SPEC §3.5 的 Plugins 半）。
///
/// 目录布局：&lt;游戏根&gt;/Neutraled/plugins/&lt;id&gt;/plugin.json（+ 插件自己的 dll 与数据）
///   · 只认一级子目录；每个目录一份清单；
///   · 启停状态 = 清单里的 "enabled"（读-改-写，缺省 true）；
///   · 删除/覆盖只允许发生在 plugins 根内（前缀校验 + id 白名单 + 压缩包路径白名单）。
///
/// 实测教训（这些是踩出来的，不是设计洁癖）：
///   1) 插件清单里的 id 与目录名不一致 = 目录穿越/克隆的入口（Enable 会去写别人的目录），
///      所以**写入前**必须两者一致，不一致一律拒绝并说明原因。
///   2) 压缩包必须逐条校验路径（绝对路径 / .. / 盘符 / 保留名 / 以空格或点结尾），
///      否则一个 ../.. 就能把文件写到 Neutraled 之外（zip 里这种条目很常见）。
///   3) 写 plugin.json 一定要**读-改-写**（JsonNode 原样保留未知键），
///      否则插件作者自己加的配置键会在 enable/disable 时被静默抹掉。</summary>
public static class Plugins
{
    /// <summary>插件根目录（Neutraled/plugins）。</summary>
    public static string Root(string gameRoot) => Paths.PluginsRoot(gameRoot);

    /// <summary>写清单 / 启用状态的统一序列化选项（缩进 + 非 ASCII 不转义，与 config.json 同一口径）。</summary>
    private static readonly JsonSerializerOptions ManifestJson = new(Paths.Json) { WriteIndented = true };

    /// <summary>Windows 保留设备名（用作文件/目录名会被系统截断或拒绝）。</summary>
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>路径比较口径：Windows 不区分大小写，其它平台区分（借 Platform.IsWindows，不在业务里判 OS）。</summary>
    private static StringComparison PathCmp =>
        Platform.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    // ---------------------------------------------------------------- 扫描

    /// <summary>扫描 plugins/*（每个一级子目录一份清单）；目录不存在返回空表（不抛、不建目录）。</summary>
    public static List<PluginEntry> List(string gameRoot)
    {
        var list = new List<PluginEntry>();
        var root = Root(gameRoot);
        if (!Directory.Exists(root)) return list;

        foreach (var dir in Directory.GetDirectories(root)
                     .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
        {
            var dirName = Path.GetFileName(dir);
            var manifestPath = Path.Combine(dir, "plugin.json");
            if (!File.Exists(manifestPath))
            {
                // 没有清单：只有放着 DLL 才算「本地插件」登记（纯数据目录不算插件，避免噪音）
                if (Directory.GetFiles(dir, "*.dll", SearchOption.TopDirectoryOnly).Length == 0) continue;
                list.Add(new PluginEntry
                {
                    Id = dirName,
                    Dir = dir,
                    State = PluginStates.LocalOnly,
                    Diagnostics = { L("[插件] 目录里没有 plugin.json，只有 DLL —— 仅本地可见，不会被加载") }
                });
                continue;
            }
            list.Add(ReadEntry(root, dir, dirName, manifestPath));
        }
        return list;
    }

    /// <summary>读一个插件目录的登记项（清单坏了也返回条目，State=broken + Diagnostics 说明原因）。</summary>
    private static PluginEntry ReadEntry(string pluginsRoot, string dir, string dirName, string manifestPath)
    {
        // 先按 broken 建项：下面每个致命分支都直接 return，走完全程的必须在 CheckEntry 里改回可加载态。
        var e = new PluginEntry { Id = dirName, Dir = dir, State = PluginStates.Broken };

        var m = ReadManifest(manifestPath, out var perr);
        if (m == null)
        {
            e.Diagnostics.Add(L("清单解析失败: {0}", perr));
            return e;
        }
        e.Manifest = m;

        // 目录名本身必须是合法 id（Install 只会建合法目录；不合法 = 手工改过 → 直接判 broken）
        if (!IsSafeId(dirName, out var whyDir))
        {
            e.Diagnostics.Add(L("目录名不合法: {0}", whyDir));
            return e;
        }

        if (string.IsNullOrWhiteSpace(m.Id))
        {
            e.Diagnostics.Add(L("清单 id 为空，已用目录名当 id"));
            m.Id = dirName;
        }
        var id = m.Id.Trim();
        e.Id = id;
        if (!IsSafeId(id, out var whyId))
        {
            e.Diagnostics.Add(L("清单 id 不合法: {0}", whyId));
            return e;
        }
        if (!string.Equals(id, dirName, PathCmp))
        {
            e.Diagnostics.Add(L("清单 id 与目录名不一致: 目录 {0}，清单 {1}（拒绝启用/禁用，防目录穿越）", dirName, id));
            return e;
        }

        var minBroken = false;
        if (!CheckMinBuilder(m, out var verNote))
        {
            e.Diagnostics.Add(verNote);
            e.State = PluginStates.Broken;          // 版本不够 = 注定加载失败，直接判坏
            minBroken = true;
        }

        CheckPermissions(m, e);
        CheckEntry(m, dir, e, minBroken);

        // 依赖只登记为诊断，真正的判定在 PluginHost.Load —— 那时才知道「实际加载成功的是谁」
        // （清单存在 ≠ 该插件加载成功；加载顺序还要靠拓扑排序）
        foreach (var req in m.Requires)
        {
            var dep = (req ?? "").Trim();
            if (dep.Length == 0) continue;
            var depDir = Path.Combine(pluginsRoot, dep);
            if (!Directory.Exists(depDir)) e.Diagnostics.Add(L("依赖的插件不存在: {0}", dep));
            else if (!ReadEnabledFlag(Path.Combine(depDir, "plugin.json"))) e.Diagnostics.Add(L("依赖的插件已禁用: {0}", dep));
        }

        if (e.State == PluginStates.Broken) return e;                       // 已判坏：别被下面覆盖
        if (!ReadEnabledFlag(manifestPath)) { e.State = PluginStates.Disabled; return e; }
        if (e.State != PluginStates.Installed) e.State = PluginStates.Enabled;
        return e;
    }

    /// <summary>入口程序集检查：路径必须在插件目录内，且文件存在。</summary>
    private static void CheckEntry(PluginManifest m, string dir, PluginEntry e, bool minBroken)
    {
        if (string.IsNullOrWhiteSpace(m.Entry))
        {
            e.Diagnostics.Add(L("清单没有 entry（无法加载）"));
            e.State = PluginStates.Broken;
            return;
        }
        var rel = m.Entry.Trim().Replace('\\', '/');
        if (!IsSafeEntry(rel, out var why))
        {
            e.Diagnostics.Add(L("入口路径不合法（越出插件目录）: {0}", m.Entry));
            e.State = PluginStates.Broken;
            return;
        }
        var full = Path.GetFullPath(Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsUnder(Path.GetFullPath(dir), full) || !File.Exists(full))
        {
            e.Diagnostics.Add(L("入口程序集缺失: {0}", m.Entry));
            e.State = PluginStates.Installed;
            return;
        }

        // 入口文件在 = 可以加载：把「默认 broken」改回可加载态；启用/禁用由 enabled 字段决定（见 ReadEntry 尾部）。
        // 只有最低版本不满足（minBroken）才保持 broken —— 那种情况注定加载失败。
        if (!minBroken) e.State = PluginStates.Enabled;
    }

    /// <summary>最低 builder 版本检查（Paths.ApiVersion）。返回 false = 不满足（broken）。</summary>
    private static bool CheckMinBuilder(PluginManifest m, out string note)
    {
        note = "";
        if (string.IsNullOrWhiteSpace(m.MinBuilder)) return true;
        var need = m.MinBuilder.Trim();
        var have = Paths.ApiVersion();
        if (CompareVersion(have, need) >= 0) return true;
        note = L("最低 builder 版本 {0}，当前 {1} —— 需要升级 Neutraled", need, have);
        return false;
    }

    /// <summary>宽松版本比较：能解析成 Version 就比数值，否则按字符串序（绝不因为格式怪就误判）。</summary>
    private static int CompareVersion(string a, string b)
    {
        if (Version.TryParse(a, out var va) && Version.TryParse(b, out var vb)) return va.CompareTo(vb);
        return string.CompareOrdinal(a, b);
    }

    /// <summary>未知权限只警告（允许插件自定义命名空间）。</summary>
    private static void CheckPermissions(PluginManifest m, PluginEntry e)
    {
        foreach (var p in m.Permissions)
        {
            var t = (p ?? "").Trim();
            if (t.Length == 0 || t == "*") continue;
            if (!PluginPermissions.All.Contains(t, StringComparer.OrdinalIgnoreCase))
                e.Diagnostics.Add(L("未知权限: {0}", t));
        }
    }

    // ---------------------------------------------------------------- 启停

    /// <summary>启用插件（写 plugin.json 的 enabled=true）。返回 0 成功 / 1 失败 / 2 拒绝（id 非法或与目录不一致）。</summary>
    public static int Enable(string gameRoot, string id) => SetEnabled(gameRoot, id, true);

    /// <summary>禁用插件（写 plugin.json 的 enabled=false）。返回码同 Enable。</summary>
    public static int Disable(string gameRoot, string id) => SetEnabled(gameRoot, id, false);

    private static int SetEnabled(string gameRoot, string id, bool enabled)
    {
        var dir = ResolveDir(gameRoot, id, out var err);
        if (dir == null) { Paths.Log(err); return 2; }

        var manifestPath = Path.Combine(dir, "plugin.json");
        if (!File.Exists(manifestPath)) { Paths.Log(L("[错误] 找不到插件清单: {0}", manifestPath)); return 1; }

        // ★ 写入前校验「清单 id == 目录名」：不一致时这条写操作可能落在别人的目录上（目录穿越/克隆）
        var m = ReadManifest(manifestPath, out var perr);
        if (m == null) { Paths.Log(L("[错误] 插件清单解析失败: {0}", perr)); return 1; }
        var declared = (m.Id ?? "").Trim();
        var dirName = Path.GetFileName(dir);
        if (declared.Length > 0 && !string.Equals(declared, dirName, PathCmp))
        {
            Paths.Log(L("[错误] 清单 id 与目录名不一致: 目录 {0}，清单 {1}（拒绝写入，防目录穿越）", dirName, declared));
            return 2;
        }

        JsonObject obj;
        try
        {
            obj = JsonNode.Parse(File.ReadAllText(manifestPath)) as JsonObject ?? new JsonObject();
        }
        catch (Exception ex) { Paths.Log(L("[错误] 插件清单解析失败: {0}", ex.Message)); return 1; }

        obj["enabled"] = enabled;   // 读-改-写：插件作者自己的键原样保留
        try { Paths.SafeWrite(manifestPath, obj.ToJsonString(ManifestJson)); }
        catch (Exception ex) { Paths.Log(L("[错误] 写 plugin.json 失败: {0}", ex.Message)); return 1; }

        Paths.Log(enabled ? L("[插件] 已启用 {0}", id) : L("[插件] 已禁用 {0}", id));
        return 0;
    }

    /// <summary>读清单的 enabled 字段（缺省 true；清单坏/无该键也算 true，与 gui/MainForm.cs 对 mod.json 的口径一致）。</summary>
    internal static bool ReadEnabledFlag(string manifestPath)
    {
        try
        {
            if (!File.Exists(manifestPath)) return true;
            var node = JsonNode.Parse(File.ReadAllText(manifestPath));
            var v = node?["enabled"];
            if (v == null) return true;
            try { return v.GetValue<bool>(); } catch { }
            var s = v.ToString();
            return !s.Equals("false", StringComparison.OrdinalIgnoreCase) && s != "0";
        }
        catch { return true; }
    }

    // ---------------------------------------------------------------- 信息

    /// <summary>打印一个插件的全部信息。返回 0 找到 / 1 找不到 / 2 拒绝（id 非法）。</summary>
    public static int Info(string gameRoot, string id)
    {
        var dir = ResolveDir(gameRoot, id, out var err);
        if (dir == null) { Paths.Log(err); return 2; }

        var e = List(gameRoot).FirstOrDefault(x => string.Equals(x.Id, id, PathCmp));
        if (e == null) { Paths.Log(L("[错误] 找不到插件: {0}", id)); return 1; }

        Paths.Log(L("===== 插件: {0} =====", e.Id));
        Paths.Log(L("  状态: {0}", e.State));
        Paths.Log(L("  目录: {0}", e.Dir));
        var m = e.Manifest;
        if (m == null) { PrintDiagnostics(e); return 0; }

        Paths.Log(L("  名称: {0}", Str(m.Name)));
        Paths.Log(L("  版本: {0}", Str(m.Version)));
        Paths.Log(L("  作者: {0}", Str(m.Author)));
        Paths.Log(L("  描述: {0}", Str(m.Description)));
        Paths.Log(L("  入口: {0}", Str(m.Entry)));
        Paths.Log(L("  类型: {0}", string.IsNullOrWhiteSpace(m.Type) ? L("（自动探测）") : m.Type));
        Paths.Log(L("  权限: {0}", m.Permissions.Count == 0 ? L("（无）") : string.Join(", ", m.Permissions)));
        Paths.Log(L("  依赖: {0}", m.Requires.Count == 0 ? L("（无）") : string.Join(", ", m.Requires)));
        Paths.Log(L("  冲突: {0}", m.Conflicts.Count == 0 ? L("（无）") : string.Join(", ", m.Conflicts)));
        if (!string.IsNullOrWhiteSpace(m.MinBuilder)) Paths.Log(L("  最低 builder: {0}", m.MinBuilder.Trim()));
        PrintDiagnostics(e);
        return 0;
    }

    private static void PrintDiagnostics(PluginEntry e)
    {
        if (e.Diagnostics.Count == 0) return;
        Paths.Log(L("  诊断:"));
        foreach (var d in e.Diagnostics) Paths.Log(L("    - {0}", d));
    }

    private static string Str(string? s) => string.IsNullOrWhiteSpace(s) ? L("（无）") : s.Trim();

    // ---------------------------------------------------------------- 安装 / 删除

    /// <summary>从目录或压缩包（.ntlplugin / .zip / 7z / rar，走 SharpCompress）安装插件。
    /// 返回 0 成功 / 1 失败 / 2 已存在需要 --force。</summary>
    public static int Install(string gameRoot, string pathOrZip, bool force = false)
    {
        var root = Root(gameRoot);
        Directory.CreateDirectory(root);
        Paths.Log(L("===== 插件安装 ====="));
        Paths.Log(L("  来源: {0}", pathOrZip));

        if (!Directory.Exists(pathOrZip) && !File.Exists(pathOrZip))
        {
            Paths.Log(L("[错误] 找不到: {0}", pathOrZip));
            return 1;
        }

        var stage = Path.Combine(Paths.NeutraledRoot(gameRoot), ".tmp", "plugin-" + Guid.NewGuid().ToString("N")[..8]);
        var staged = false;
        try
        {
            string srcDir;
            if (Directory.Exists(pathOrZip)) srcDir = Path.GetFullPath(pathOrZip);
            else
            {
                Directory.CreateDirectory(stage);
                staged = true;
                var n = ExtractSafe(pathOrZip, stage);
                if (n < 0) return 1;
                Paths.Log(L("  已解压 {0} 个文件", n));
                srcDir = stage;
            }

            var manifestPath = FindManifest(srcDir);
            if (manifestPath == null)
            {
                Paths.Log(L("[错误] 包里没有 plugin.json（不是有效的 Neutraled 插件包）"));
                return 1;
            }
            var srcRoot = Path.GetFullPath(Path.GetDirectoryName(manifestPath)!);

            var m = ReadManifest(manifestPath, out var perr);
            if (m == null) { Paths.Log(L("[错误] 插件清单无效: {0}", perr)); return 1; }

            var id = (m.Id ?? "").Trim();
            if (id.Length == 0)
            {
                var pkgName = Path.GetFileNameWithoutExtension(pathOrZip.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                id = Mods.Sanitize(pkgName);
                Paths.Log(L("清单 id 为空，已用目录名当 id"));
            }
            if (!IsSafeId(id, out var whyId))
            {
                Paths.Log(L("[错误] 插件 id 不合法: {0}（{1}）", id, whyId));
                return 1;
            }

            var target = Path.GetFullPath(Path.Combine(root, id));
            if (!IsUnder(Path.GetFullPath(root), target))
            {
                Paths.Log(L("[错误] 插件目录越出 plugins 根，已拒绝: {0}", target));
                return 2;
            }
            if (string.Equals(srcRoot, target, PathCmp) || IsUnder(srcRoot, target))
            {
                Paths.Log(L("[错误] 源目录与目标相同或嵌套，已拒绝: {0}", srcRoot));
                return 2;
            }

            if (Directory.Exists(target))
            {
                if (!force)
                {
                    Paths.Log(L("[提示] 插件已存在: {0} —— 加 --force 覆盖", target));
                    return 2;
                }
                if (!RemoveDirSafe(target, root, out var derr))
                {
                    Paths.Log(L("[错误] 删除失败: {0}", derr));
                    return 1;
                }
                Paths.Log(L("  已覆盖旧版本（--force）"));
            }

            CopyDir(srcRoot, target);
            Paths.Log(L("  插件 id: {0}  名称: {1}  版本: {2}", id, Str(m.Name), Str(m.Version)));
            Paths.Log(L("  已安装到: {0}", target));
            Paths.Log(L("  启用状态由 plugin.json 的 enabled 决定（缺省为启用）"));
            return 0;
        }
        catch (Exception ex) { Paths.Log(L("[错误] 安装失败: {0}", ex.Message)); return 1; }
        finally
        {
            if (staged) { try { if (Directory.Exists(stage)) Directory.Delete(stage, true); } catch { } }
        }
    }

    /// <summary>删除插件目录（只允许 plugins 根内的直接子目录）。
    /// **默认拒绝**：破坏性操作必须先加 --force，未加时只提示怎么强制。</summary>
    public static bool Remove(string gameRoot, string id, bool force = false)
    {
        var root = Path.GetFullPath(Root(gameRoot));
        var dir = ResolveDir(gameRoot, id, out var err);
        if (dir == null) { Paths.Log(err); return false; }
        if (!Directory.Exists(dir)) { Paths.Log(L("[错误] 找不到插件: {0}", id)); return false; }

        if (!force)
        {
            Paths.Log(L("[提示] 删除插件会移除它的全部文件: {0}", dir));
            Paths.Log(L("       确认后加 --force: --plugin-remove {0} --force", id));
            return false;
        }
        if (!RemoveDirSafe(dir, root, out var derr)) { Paths.Log(L("[错误] 删除失败: {0}", derr)); return false; }
        Paths.Log(L("  已删除: {0}", dir));
        return true;
    }

    /// <summary>--plugin-list 的人可读输出（状态词是 ASCII，可直接 grep）。
    /// 没有插件时只打两行提示（不产生噪音，方便脚本判定）。</summary>
    public static void PrintList(string gameRoot)
    {
        var list = List(gameRoot);
        Paths.Log(L("===== 插件 ====="));
        if (list.Count == 0)
        {
            Paths.Log(L("  （无插件）"));
            Paths.Log(L("  装法: ntl-builder --plugin-install <目录或 .ntlplugin>"));
            return;
        }

        foreach (var e in list)
        {
            var m = e.Manifest;
            var name = m == null || string.IsNullOrWhiteSpace(m.Name) ? e.Id : m.Name;
            var ver = m == null || string.IsNullOrWhiteSpace(m.Version) ? "-" : m.Version;
            var author = m == null || string.IsNullOrWhiteSpace(m.Author) ? "-" : m.Author;
            Paths.Log($"  {e.State,-10} {e.Id,-24} {ver,-10} {name,-24} {author}");
            foreach (var d in e.Diagnostics) Paths.Log(L("    - {0}", d));
        }

        var en = list.Count(x => x.State == PluginStates.Enabled);
        var dis = list.Count(x => x.State == PluginStates.Disabled);
        var br = list.Count(x => x.State == PluginStates.Broken);
        Paths.Log("");
        Paths.Log(L("  共 {0} 个插件（启用 {1} / 禁用 {2} / 损坏 {3}）", list.Count, en, dis, br));
        Paths.Log(L("  提示: --plugin-enable <id> / --plugin-disable <id> / --plugin-info <id> / --plugin-remove <id> --force"));
    }

    // ---------------------------------------------------------------- 内部：路径安全

    /// <summary>插件 id 白名单校验：只允许字母/数字/点/下划线/连字符，禁止 . 与 ..、路径分隔符、系统保留名。
    /// 这是所有「按 id 拼路径」入口的第一道闸（防 ../ 越界）。</summary>
    internal static bool IsSafeId(string? id, out string reason)
    {
        reason = "";
        if (string.IsNullOrWhiteSpace(id)) { reason = L("空 id"); return false; }
        var t = id.Trim();
        if (t.Length > 64) { reason = L("id 过长（超过 64 字符）"); return false; }
        if (t == "." || t == "..") { reason = L("id 不能是 . 或 .."); return false; }
        if (t.StartsWith('.') || t.EndsWith('.')) { reason = L("id 以点开头或结尾"); return false; }
        foreach (var c in t)
        {
            if (char.IsAsciiLetterOrDigit(c) || c == '.' || c == '_' || c == '-') continue;
            reason = L("id 含非法字符（只允许字母、数字、点、下划线、连字符）: {0}", c);
            return false;
        }
        if (ReservedNames.Contains(t.Split('.')[0])) { reason = L("id 是系统保留名: {0}", t); return false; }
        return true;
    }

    /// <summary>把 id 解析成 plugins 根下的绝对目录（校验 id + 前缀，失败时 error 是可直接打印的一行）。</summary>
    internal static string? ResolveDir(string gameRoot, string id, out string error)
    {
        error = "";
        if (!IsSafeId(id, out var why))
        {
            error = L("[错误] 非法插件 id: {0}（{1}）", id, why);
            return null;
        }
        var rootFull = Path.GetFullPath(Root(gameRoot));
        var full = Path.GetFullPath(Path.Combine(rootFull, id.Trim()));
        if (!IsUnder(rootFull, full))
        {
            error = L("[错误] 插件目录越出 plugins 根，已拒绝: {0}", full);
            return null;
        }
        return full;
    }

    /// <summary>candidate 是否严格位于 rootFull 之下（rootFull 自身不算）。</summary>
    internal static bool IsUnder(string rootFull, string candidate)
    {
        var r = rootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
        return candidate.Length > r.Length && candidate.StartsWith(r, PathCmp);
    }

    /// <summary>删除目录，但只允许删 plugins 根之下的路径（root 自身拒绝）。</summary>
    internal static bool RemoveDirSafe(string target, string root, out string error)
    {
        error = "";
        var t = Path.GetFullPath(target);
        var r = Path.GetFullPath(root);
        if (!IsUnder(r, t)) { error = L("[错误] 目标不在 plugins 根下，拒绝删除: {0}", t); return false; }
        try { Directory.Delete(t, true); return true; }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    /// <summary>压缩包条目路径白名单：拒绝绝对路径 / 盘符 / 回溯 / 非法字符 / 保留名 / 以空格或点结尾。
    /// 实测：zip 里出现 "../" 或 "C:\" 属于常见现象，绝不能直接 Path.Combine 后写盘。</summary>
    internal static bool IsSafeEntry(string rawKey, out string reason)
    {
        reason = "";
        if (string.IsNullOrEmpty(rawKey)) { reason = L("空路径"); return false; }
        if (rawKey.Contains('\0')) { reason = L("含空字符"); return false; }
        if (rawKey.StartsWith('/') || rawKey.StartsWith('\\')) { reason = L("绝对路径"); return false; }
        if (rawKey.Length >= 2 && char.IsAsciiLetter(rawKey[0]) && rawKey[1] == ':') { reason = L("盘符路径"); return false; }

        foreach (var seg in rawKey.Replace('\\', '/').Split('/'))
        {
            if (seg.Length == 0 || seg == ".") continue;      // 重复斜杠 / 当前目录：无副作用
            if (seg == "..") { reason = L("路径回溯 .."); return false; }
            if (seg.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { reason = L("含非法字符"); return false; }
            if (seg.EndsWith(' ') || seg.EndsWith('.')) { reason = L("名字以空格或点结尾（Windows 上会被截断）"); return false; }
            if (ReservedNames.Contains(seg.Split('.')[0])) { reason = L("系统保留名"); return false; }
        }
        return true;
    }

    /// <summary>解压到 destDir（SharpCompress 统一入口，zip/7z/rar 通吃），逐条校验路径。
    /// 返回写出的文件数；返回 -1 = 失败（原因已打印）。</summary>
    internal static int ExtractSafe(string archivePath, string destDir)
    {
        try
        {
            var destFull = Path.GetFullPath(destDir);
            var n = 0;
            using var archive = SharpCompress.Archives.ArchiveFactory.OpenArchive(archivePath);
            foreach (var entry in archive.Entries)
            {
                if (entry.IsDirectory) continue;
                var key = (entry.Key ?? "").Replace('\\', '/');
                if (key.Length == 0) continue;
                if (!IsSafeEntry(key, out var why))
                {
                    Paths.Log(L("[错误] 压缩包内含不安全路径，已拒绝: {0}（{1}）", key, why));
                    return -1;
                }
                var dest = Path.GetFullPath(Path.Combine(destFull, key.Replace('/', Path.DirectorySeparatorChar)));
                if (!IsUnder(destFull, dest))
                {
                    Paths.Log(L("[错误] 压缩包内含不安全路径，已拒绝: {0}（{1}）", key, L("路径回溯 ..")));
                    return -1;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                using var es = entry.OpenEntryStream();
                using var fs = File.Create(dest);
                es.CopyTo(fs);
                n++;
            }
            return n;
        }
        catch (Exception ex) { Paths.Log(L("[错误] 解压失败: {0}", ex.Message)); return -1; }
    }

    /// <summary>在解压目录里找清单：优先根目录，其次任意深度；多个取最外层的那份（并说明）。</summary>
    private static string? FindManifest(string dir)
    {
        var direct = Path.Combine(dir, "plugin.json");
        if (File.Exists(direct)) return direct;

        string[] all;
        try { all = Directory.GetFiles(dir, "plugin.json", SearchOption.AllDirectories); }
        catch { return null; }
        if (all.Length == 0) return null;
        if (all.Length == 1) return all[0];

        var shallow = all
            .OrderBy(p => p.Count(c => c == Path.DirectorySeparatorChar))
            .ThenBy(p => p, StringComparer.Ordinal)
            .First();
        Paths.Log(L("  包里有 {0} 个 plugin.json，取最外层: {1}", all.Length, Path.GetRelativePath(dir, shallow)));
        return shallow;
    }

    /// <summary>读清单（失败时 error 是可打印的一句话）。</summary>
    internal static PluginManifest? ReadManifest(string manifestPath, out string error)
    {
        error = "";
        try
        {
            var text = File.ReadAllText(manifestPath);
            var m = JsonSerializer.Deserialize<PluginManifest>(text, Paths.Json);
            if (m == null) { error = L("清单为空"); return null; }
            m.Id = (m.Id ?? "").Trim();
            m.Entry = (m.Entry ?? "").Trim();
            m.Type = (m.Type ?? "").Trim();
            m.Permissions ??= new List<string>();
            m.Requires ??= new List<string>();
            m.Conflicts ??= new List<string>();
            return m;
        }
        catch (Exception ex) { error = ex.Message; return null; }
    }

    /// <summary>递归复制目录（插件内容可能带数据文件，一律照搬）。</summary>
    private static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src))
        {
            var target = Path.Combine(dst, Path.GetFileName(f));
            try { File.Copy(f, target, true); }
            catch (IOException)
            {
                // 同名文件被占用（多为杀软扫描）：退避重试一次再放弃，避免半个插件目录
                Thread.Sleep(120);
                File.Copy(f, target, true);
            }
        }
        foreach (var d in Directory.GetDirectories(src))
            CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }
}
