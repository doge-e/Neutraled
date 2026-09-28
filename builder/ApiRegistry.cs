using System.Text.Json;
using System.Text.Encodings.Web;
using UndertaleModLib;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>生成 API 注册表：原版函数/资源 + 各 mod 导出的接口。
/// 供 IDE 自动补全、依赖解析与 mod 命名空间调用使用。
///
/// 产出两份：
///   api-registry.json —— 完整版（含原版 711 函数 / 1577 对象 / 5888 精灵），供控制台 `api` 命令搜索；
///   ns-registry.json  —— 运行时精简版（只含 mods 段），供开机时的 ntl_ns_load 读取。
/// 两份的意义见 Write 内的注释：GameMaker 的字符串 += 是 O(n^2)。</summary>
public static class ApiRegistry
{
    /// <param name="data">非空 = 连原版资源一起写完整版；null = 只写运行时精简版（顶层 data.win 资源不全，不配当完整版）。</param>
    public static void Write(string gameRoot, UndertaleData? data, List<ModEntry> mods)
    {
        var dir = Paths.NeutraledRoot(gameRoot);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "api-registry.json");

        var functions = new List<string>();
        if (data != null)
        {
            foreach (var f in data.Functions)
            {
                var n = f.Name?.Content;
                if (string.IsNullOrEmpty(n)) continue;
                // 过滤编译器内部符号与匿名子条目
                if (n.StartsWith("@@") || n.StartsWith("gml_") || n.Contains("_gml_")) continue;
                if (n.StartsWith("anon_")) continue;
                functions.Add(n);
            }
            functions.Sort(StringComparer.OrdinalIgnoreCase);
        }

        string[] Names<T>(IEnumerable<T> items, Func<T, string?> sel) =>
            items.Select(sel).Where(n => !string.IsNullOrEmpty(n)).Select(n => n!).OrderBy(n => n).ToArray();

        var registry = new
        {
            version = 1,
            generated = DateTime.Now.ToString("s"),
            engine = "GameMaker 2023.6.0.0",
            original = new
            {
                functions,
                objects = data == null ? Array.Empty<string>() : Names(data.GameObjects, o => o.Name?.Content),
                rooms   = data == null ? Array.Empty<string>() : Names(data.Rooms, r => r.Name?.Content),
                sprites = data == null ? Array.Empty<string>() : Names(data.Sprites, s => s.Name?.Content),
                sounds  = data == null ? Array.Empty<string>() : Names(data.Sounds, s => s.Name?.Content),
                fonts   = data == null ? Array.Empty<string>() : Names(data.Fonts, f => f.Name?.Content),
                scripts = data == null ? Array.Empty<string>() : Names(data.Scripts, s => s.Name?.Content)
            },
            mods = mods.Select(m => new
            {
                id = m.Id,
                name = m.Name,
                author = m.Author,
                version = m.Version,
                chapter = m.Chapter ?? "",
                ns = m.Api?.Ns ?? Mods.Sanitize(m.Id),
                dependencies = m.Dependencies,
                functions = (m.Api?.Functions ?? new List<ModApiFunction>())
                    .Select(f => new
                    {
                        f.Name,
                        script = !string.IsNullOrEmpty(f.Script)
                            ? f.Script
                            : (m.Api?.Ns ?? Mods.Sanitize(m.Id)) + "_" + f.Name,
                        f.Params, f.Returns, f.Desc
                    }),
                constants = (m.Api?.Constants ?? new List<ModApiConstant>())
                    .Select(c => new { c.Name, c.Value, c.Desc }),
                // 该 mod 提供的脚本资源（每个 gml 文件一个）
                scripts = Directory.Exists(Path.Combine(m.Dir, "gml"))
                    ? Directory.GetFiles(Path.Combine(m.Dir, "gml"), "*.gml")
                        .Select(f => Path.GetFileNameWithoutExtension(f)).OrderBy(x => x).ToArray()
                    : Array.Empty<string>()
            }).ToList()
        };

        if (data != null)
        {
            // * 单行紧凑写出：完整版有 ~1 MB / 2 万行，而运行时（控制台 `api` 命令）用
            //   file_text_open_read + 逐行 `_txt += ...` 读取；GameMaker 的字符串 += 
            //   每次都复制整个累积串（O(n^2)）-> 实测在真机上卡 19.6 秒。单行化后只读 1 行。
            Paths.SafeWrite(path, JsonSerializer.Serialize(registry, new JsonSerializerOptions
            {
                WriteIndented = false,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }));

            var kb = new FileInfo(path).Length / 1024;
            Paths.Log(L("  API 注册表: 函数 {0} / 对象 {1} / ", functions.Count, registry.original.objects.Length) +
                      $"mod {registry.mods.Count} -> {path} ({kb} KB)");
        }

        // * 运行时精简注册表：开机初始化（ntl_ns_load）只需要 mods 段，
        //   不需要 original 段的原版清单。这份文件小到即使逐行拼接也只要毫秒级，
        //   彻底避开 O(n^2)，同时也让「先部署 root」的全新仓库有文件可读。
        var nsPath = Path.Combine(dir, "ns-registry.json");
        Paths.SafeWrite(nsPath, JsonSerializer.Serialize(new
        {
            version = 1,
            generated = registry.generated,
            mods = registry.mods
        }, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }));

        Paths.Log(L("  运行时注册表: mod {0} -> {1} ({2} KB)", registry.mods.Count, nsPath, new FileInfo(nsPath).Length / 1024));
    }
}
