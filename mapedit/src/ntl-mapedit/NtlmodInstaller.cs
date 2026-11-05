using System.IO.Compression;
using System.Text.Json;

namespace NtlMapEdit;

/// <summary>
/// 把 .ntlmod（Neutraled 的 mod 安装包 = 叶子目录的 zip）安装到
/// <c>Neutraled/mods/&lt;id&gt;/&lt;author&gt;/&lt;leaf&gt;/</c>，布局与 <c>ntl-builder --mod-install</c> 一致。
/// ★ 为什么编辑器自己要会安装：编辑器的承诺是「所有编辑一律产出 ntl mod」——
///   导出到 editor/export 只是备份，玩家还要去命令行装一次才算闭环；这里让「导出 → 安装」
///   在 GUI / CLI 内一次完成，导出的包也能直接交给别人装。
/// leaf 取值顺序：显式参数 &gt; 包文件名后缀（&lt;id&gt;-chapterN.ntlmod） &gt; mod.json 的 chapter 字段 &gt; 报错。
/// </summary>
public static class NtlmodInstaller
{
    public sealed record Result(bool Ok, string Message, string Target);

    /// <summary>安装一个 .ntlmod；已存在同名叶子会整体替换（先删后解，避免旧文件残留）。</summary>
    public static Result Install(string gameRoot, string packagePath, string? leafHint = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(gameRoot)) return new Result(false, "游戏根目录为空", "");
            if (!File.Exists(packagePath)) return new Result(false, "包不存在：" + packagePath, "");

            string id = "", author = "", leafFromJson = "";
            using (var zip = ZipFile.OpenRead(packagePath))
            {
                var entry = zip.Entries.FirstOrDefault(e =>
                    string.Equals(e.FullName.Replace('\\', '/'), "mod.json", StringComparison.OrdinalIgnoreCase));
                if (entry == null) return new Result(false, "包内没有 mod.json（不是 .ntlmod 包？）", "");
                using var sr = new StreamReader(entry.Open());
                using var doc = JsonDocument.Parse(sr.ReadToEnd());
                var root = doc.RootElement;
                id = SafeSegment(GetStr(root, "id"));
                author = SafeSegment(GetStr(root, "author"));
                leafFromJson = SafeSegment(GetStr(root, "chapter"));
            }
            if (id.Length == 0) return new Result(false, "mod.json 缺 id（无法确定安装目录）", "");
            if (author.Length == 0) author = "Neutraled";

            string leaf = FirstNonEmpty(leafHint, LeafFromFileName(packagePath), leafFromJson);
            if (!IsLeaf(leaf))
                return new Result(false, string.IsNullOrWhiteSpace(leafHint)
                    ? "无法确定部署叶子（需要 root 或 chapter1..chapter7）：可显式传 leaf 参数，或把包名写成 <id>-chapterN.ntlmod"
                    : "leaf 必须是 root 或 chapter1..chapter7：" + leafHint, "");

            string modsRoot = Path.Combine(gameRoot, "Neutraled", "mods");
            string target = Path.Combine(modsRoot, id, author, leaf);
            Directory.CreateDirectory(modsRoot);
            if (Directory.Exists(target))
            {
                // 已经装过：整体替换（旧版本文件不能残留，否则会覆盖新包里的同名脚本/数据）。
                foreach (var f in Directory.GetFiles(target, "*", SearchOption.AllDirectories))
                    try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
                Directory.Delete(target, true);
            }
            Directory.CreateDirectory(target);
            ZipFile.ExtractToDirectory(packagePath, target, true);
            int files = Directory.GetFiles(target, "*", SearchOption.AllDirectories).Length;
            return new Result(true, "已安装到 " + target + "（" + files + " 个文件）；重新部署或启动 Neutraled 后生效", target);
        }
        catch (Exception ex)
        {
            return new Result(false, "安装失败：" + ex.Message, "");
        }
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values) if (!string.IsNullOrWhiteSpace(v)) return v!.Trim();
        return "";
    }

    /// <summary>从包名尾部取叶子：battle.demo-chapter1.ntlmod → chapter1；取不到返回空。</summary>
    internal static string LeafFromFileName(string packagePath)
    {
        string name = Path.GetFileNameWithoutExtension(packagePath);
        int dash = name.LastIndexOf('-');
        if (dash < 0 || dash + 1 >= name.Length) return "";
        string tail = name.Substring(dash + 1).Trim();
        return IsLeaf(tail) ? tail : "";
    }

    internal static bool IsLeaf(string leaf) =>
        System.Text.RegularExpressions.Regex.IsMatch(leaf ?? "", "^(root|chapter[1-7])$");

    /// <summary>只允许单段目录名（防 ../ 或绝对路径借 mod.json 跳出 mods/）。</summary>
    private static string SafeSegment(string s)
    {
        s = (s ?? "").Trim();
        if (s.Length == 0) return "";
        if (s.Contains('/') || s.Contains('\\') || s.Contains(':') || s == "." || s == "..") return "";
        return s;
    }

    private static string GetStr(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object) return "";
        foreach (var p in root.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String)
                return p.Value.GetString() ?? "";
        return "";
    }
}
