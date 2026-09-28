using System.Text.Json;

namespace Neutraled.Gui;

/// <summary>GUI 侧读 mod 安装记录的桥（不引用 builder 程序集，保持独立）</summary>
public static class ModInstallBridge
{
    public sealed class ModInfo
    {
        public string Name = "";
        public string Version = "?";
        public string Author = "";
        public string Dir = "";
    }

    public static List<ModInfo> List(string ntlRoot)
    {
        var list = new List<ModInfo>();
        var modsRoot = Path.Combine(ntlRoot, "mods");
        if (!Directory.Exists(modsRoot)) return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mj in Directory.GetFiles(modsRoot, "mod.json", SearchOption.AllDirectories))
        {
            try
            {
                var doc = JsonDocument.Parse(File.ReadAllText(mj));
                var r = doc.RootElement;
                var name = r.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : Path.GetFileName(Path.GetDirectoryName(mj)!);
                if (!seen.Add(name)) continue;
                list.Add(new ModInfo
                {
                    Name = name,
                    Version = r.TryGetProperty("version", out var v) ? (v.GetString() ?? "?") : "?",
                    Author = r.TryGetProperty("author", out var a) ? (a.GetString() ?? "") : "",
                    Dir = Path.GetDirectoryName(mj) ?? ""
                });
            }
            catch { }
        }
        return list;
    }
}
