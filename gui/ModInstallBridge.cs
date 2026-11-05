using System.Text.Json;

namespace Neutraled.Gui;

/// <summary>GUI 侧读 mod 列表的桥（C4 P1-13：不引用 builder 程序集，保持独立）。
///
/// 数据源 = ntl-gui.exe --mod-list --json —— 与部署用的是同一个扫描器（builder/ModScanner.cs）。
/// 以前这里自己用 Directory.GetFiles(modsRoot, "mod.json", AllDirectories) 递归扫、按 name 去重，
/// 与部署的叶子规则不同（实测 72 vs 71），会出现「GUI 里看得到、部署时却扫不到」的幽灵。
/// 现在规则只有一份，GUI 只负责显示与把勾选写回 mod.json。</summary>
public static class ModInstallBridge
{
    public sealed class ModInfo
    {
        /// <summary>mod.json 的绝对路径（勾选写回用）。</summary>
        public string Path = "";
        /// <summary>叶子目录（mods/<mod>/<author>/<chapter> 或旧格式）。</summary>
        public string Dir = "";
        public string Id = "";
        public string Name = "";
        public string Version = "?";
        public string Author = "";
        public string Chapter = "";
        public bool Enabled = true;
    }

    /// <summary>读 --mod-list --json。返回 (mods, error)：error 非空表示读取/解析失败（调用方照旧写日志）。</summary>
    public static async Task<(List<ModInfo> Mods, string Error)> LoadAsync(string ntlRoot)
    {
        var lines = new List<string>();
        int rc;
        try { rc = await Program.RunBuilderAsync("--mod-list --json", lines.Add); }
        catch (Exception ex) { return (new List<ModInfo>(), ex.Message); }

        var text = string.Join("\n", lines);
        int start = text.IndexOf('{');
        int end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
            return (new List<ModInfo>(), Localizer.T("--mod-list --json 没有输出 JSON（退出码 {0}）", rc));

        var list = new List<ModInfo>();
        try
        {
            using var doc = JsonDocument.Parse(text[start..(end + 1)]);
            if (doc.RootElement.TryGetProperty("mods", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in arr.EnumerateArray())
                {
                    var info = new ModInfo
                    {
                        Path = Str(m, "path"),
                        Dir = Str(m, "dir"),
                        Id = Str(m, "id"),
                        Name = Str(m, "name"),
                        Version = Str(m, "version"),
                        Author = Str(m, "author"),
                        Chapter = Str(m, "chapter"),
                        Enabled = !(m.TryGetProperty("enabled", out var en) && en.ValueKind == JsonValueKind.False)
                    };
                    if (info.Version.Length == 0) info.Version = "?";
                    list.Add(info);
                }
            }
        }
        catch (Exception ex) { return (new List<ModInfo>(), Localizer.T("解析 --mod-list --json 失败: {0}", ex.Message)); }
        return (list, rc == 0 ? "" : Localizer.T("--mod-list --json 退出码 {0}", rc));
    }

    /// <summary>同步封装（联动对话框等老调用点用；会阻塞 UI 线程约 0.3-0.8 秒）。</summary>
    public static List<ModInfo> List(string ntlRoot) => LoadAsync(ntlRoot).GetAwaiter().GetResult().Mods;

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
