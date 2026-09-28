using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>外部语言包：&lt;游戏根&gt;/Neutraled/lang/lang_&lt;code&gt;.json。
/// 文件格式（与 gui/Localizer.cs 的「中文原文即 key」一致）：
///   { "code": "ja", "name": "日本語", "map": { "中文原文": "訳文" } }
/// 语言包只是往 Lang 的附加表里灌一份「中文 key → 译文」，缺键时 L() 自动回退中文 —— 所以
/// **空译文一律丢弃**：留着空串会让界面显示空白，比不翻译更糟。
/// 内置语言（zh/en）走编译期的 LangTable_*.cs，不存在 lang_zh.json 也能用。
/// 输出行一律以 [语言] 开头（人可读、可直接 grep）。
/// 退出码约定（Use）：0 = 成功，1 = 失败，2 = 参数非法（语言代码不合法）。</summary>
public static class LangPacks
{
    /// <summary>内置语言代码（编译期表，代码内定义，永远可用）。</summary>
    public static List<string> BuiltIn { get; } = new() { "zh", "en" };

    /// <summary>缩进版 JSON 选项（导出的语言包是给译者看/改的，必须缩进）。</summary>
    private static readonly JsonSerializerOptions Indented = new(Paths.Json) { WriteIndented = true };

    /// <summary>语言包文件路径。代码不合法时返回空串（**绝不拼接越界路径**）。</summary>
    public static string FileOf(string gameRoot, string code)
        => IsSafeCode(code) ? Path.Combine(Paths.LangRoot(gameRoot), "lang_" + code.Trim().ToLowerInvariant() + ".json") : "";

    /// <summary>语言代码是否安全：只允许 ASCII 字母/数字/下划线/短横线（BCP-47 的 zh-Hans、pt-BR 也放行），
    /// 长度 ≤ 24；不接受点号，从根上挡掉 ".." 穿越。</summary>
    public static bool IsSafeCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 24) return false;
        foreach (var c in code.Trim())
        {
            var ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_';
            if (!ok) return false;
        }
        return true;
    }

    /// <summary>可选语言：内置 zh/en + lang/lang_*.json 里的全部代码（去重、内置在前、其余按代码排序）。
    /// 代码以**文件名**为准（lang_ja.json → ja），保证下面 LoadExternal 一定能定址到同一个文件。</summary>
    public static List<string> Available(string gameRoot)
    {
        var list = new List<string>(BuiltIn);
        var root = Paths.LangRoot(gameRoot);
        if (!Directory.Exists(root)) return list;
        var ext = new List<string>();
        foreach (var f in Directory.GetFiles(root, "lang_*.json"))
        {
            var code = CodeFromFileName(f);
            if (!IsSafeCode(code)) continue;
            code = code.ToLowerInvariant();
            if (list.Contains(code, StringComparer.OrdinalIgnoreCase) || ext.Contains(code, StringComparer.OrdinalIgnoreCase)) continue;
            ext.Add(code);
        }
        ext.Sort(StringComparer.Ordinal);
        list.AddRange(ext);
        return list;
    }

    /// <summary>语言显示名：内置给译名（中文/English），外部优先用包里的 name 字段，没有就用代码。</summary>
    public static string DisplayName(string gameRoot, string code)
    {
        if (code.Equals("zh", StringComparison.OrdinalIgnoreCase)) return L("中文");
        if (code.Equals("en", StringComparison.OrdinalIgnoreCase)) return "English";
        if (TryRead(gameRoot, code, out _, out var name, out _, quiet: true) && !string.IsNullOrWhiteSpace(name)) return name;
        return code;
    }

    /// <summary>把 lang_&lt;code&gt;.json 灌进 Lang 的附加表（可重复调用，重复即覆盖）。
    /// 文件不存在/损坏只打印一行警告，不抛、不影响启动。</summary>
    public static void LoadExternal(string gameRoot, string code)
    {
        if (!IsSafeCode(code)) { Console.WriteLine(L("[语言] 非法语言代码: {0}", code)); return; }
        code = code.Trim().ToLowerInvariant();
        if (!TryRead(gameRoot, code, out var map, out _, out var problem))
        {
            var f = FileOf(gameRoot, code);
            if (f.Length > 0 && File.Exists(f)) Console.WriteLine(L("[语言] 语言包损坏，已跳过: {0} → {1}", f, problem));
            else Console.WriteLine(L("[语言] 找不到语言包: {0}", code));
            return;
        }
        Lang.AddTable(code, map);
        Console.WriteLine(L("[语言] 已加载 {0}（{1} 条译文）", code, map.Count));
    }

    /// <summary>扫描并加载全部外部语言包（启动时在 Lang.Init 之后调用一次）。
    /// 一个都没有时安静返回：这是每次启动都会走的路径，没装语言包不该刷屏。
    /// ★ 不要在这里改 Lang.Current：当前语言由 --lang / NTL_LANG / config.json 决定，加载 ≠ 切换。</summary>
    public static void LoadAllExternal(string gameRoot)
    {
        var root = Paths.LangRoot(gameRoot);
        if (!Directory.Exists(root)) return;
        var loaded = new List<string>();
        var total = 0;
        foreach (var f in Directory.GetFiles(root, "lang_*.json").OrderBy(x => x, StringComparer.Ordinal))
        {
            var code = CodeFromFileName(f);
            if (!IsSafeCode(code)) { Console.WriteLine(L("[语言] 非法语言代码: {0}", code)); continue; }
            code = code.ToLowerInvariant();
            if (!TryRead(gameRoot, code, out var map, out _, out var problem))
            {
                Console.WriteLine(L("[语言] 语言包损坏，已跳过: {0} → {1}", f, problem));
                continue;
            }
            Lang.AddTable(code, map);
            loaded.Add(code);
            total += map.Count;
        }
        if (loaded.Count > 0)
            Console.WriteLine(L("[语言] 已加载 {0} 个外部语言包: {1}（共 {2} 条译文）", loaded.Count, string.Join(", ", loaded), total));
    }

    /// <summary>打印语言清单（--lang-list）：代码 / 显示名 / 内置还是外部 / 是否已加载。</summary>
    public static void PrintList(string gameRoot)
    {
        var codes = Available(gameRoot);
        var external = codes.Count(c => !BuiltIn.Contains(c, StringComparer.OrdinalIgnoreCase));
        Console.WriteLine(L("===== 语言包 ====="));
        foreach (var c in codes)
        {
            var mark = c.Equals(Lang.Current, StringComparison.OrdinalIgnoreCase) ? "*" : " ";
            var builtin = BuiltIn.Contains(c, StringComparer.OrdinalIgnoreCase);
            var state = builtin ? L("内置") : (Lang.HasTable(c) ? L("外部（已加载）") : L("外部（未加载）"));
            Console.WriteLine($"  {mark} {c,-8} {DisplayName(gameRoot, c),-18} {state}");
        }
        Console.WriteLine(L("  当前语言: {0}（--lang / NTL_LANG / config.json 的 lang 决定）", Lang.Current));
        Console.WriteLine(L("  共 {0} 种语言（内置 {1}，外部 {2}）", codes.Count, codes.Count - external, external));
        Console.WriteLine(L("  外部语言包目录: {0}", Paths.LangRoot(gameRoot)));
        Coverage(gameRoot);   // 清单后面直接跟覆盖率（一行一语言，便于 grep 汇总）
    }

    /// <summary>覆盖率：对每种可选语言逐行输出「语言 / 总键 / 已译 / 缺失 / 覆盖率%」。
    /// 传 code 只统计那一种；不传则统计 Available 里的全部语言。
    /// 返回**未达 100% 的语言数**（0 = 全都补齐；全项目 0 键时按 100% 处理，避免除零）。
    /// 语言代码非法时打印错误并返回 1 —— CLI 把这个返回值直接当退出码用（0 = 全部语言 100%）。</summary>
    public static int Coverage(string gameRoot, string? code = null)
    {
        var keys = Lang.Keys.ToList();
        var gml = GmlKeys(gameRoot);          // 游戏内 GML 表的 zh 原文（语言包的 key 就是它）
        // 外部语言包以「中文原文」为 key ⇒ 纯 ASCII 的条目（Mod / ID / OK …）语言包装不进去，
        // 把它们算成「缺失」就是永远补不齐的假缺口。外部语言一律按**可翻译子集**（含中文的键）统计；
        // 内置 zh/en 是编译期表，仍按全集统计。
        var keysX = keys.Where(HasCjk).ToList();
        var gmlX = gml.Where(HasCjk).ToList();
        var codes = new List<string>();
        if (!string.IsNullOrWhiteSpace(code))
        {
            if (!IsSafeCode(code)) { Console.WriteLine(L("[语言] 非法语言代码: {0}", code)); return 1; }
            codes.Add(code.Trim().ToLowerInvariant());
        }
        else codes = Available(gameRoot);

        Console.WriteLine(L("===== 语言覆盖率 ====="));
        Console.WriteLine(L("  词条总数: {0}（当前语言 {1}）", keys.Count, Lang.Current));
        if (gml.Count > 0) Console.WriteLine(L("  游戏内词条: {0} 条（api/ 的 zh 表）", gml.Count));
        var incomplete = 0;
        foreach (var c in codes)
        {
            EnsureTable(gameRoot, c);   // 磁盘上有包就先灌表（幂等、静默），否则统计的是"没加载过"的空表
            var builtin = BuiltIn.Contains(c, StringComparer.OrdinalIgnoreCase);
            var pool = builtin ? keys : keysX;                  // 外部语言只看可翻译子集
            var translated = pool.Count(k => Lang.Has(c, k));
            var missing = pool.Count - translated;
            var pct = pool.Count == 0 ? 100.0 : translated * 100.0 / pool.Count;
            Console.WriteLine(L("[语言] {0}  总键 {1}  已译 {2}  缺失 {3}  覆盖率 {4}%", c, pool.Count, translated, missing, pct.ToString("0.0", CultureInfo.InvariantCulture)));
            var bad = missing > 0;
            // 内置 zh/en 的 GML 侧由 GML 自己的 _zh/_en 表提供，语言包不参与，不在此统计
            if (gmlX.Count > 0 && !builtin)
            {
                var g2 = gmlX.Count(z => Lang.Has(c, z));
                var gp = g2 * 100.0 / gmlX.Count;
                Console.WriteLine(L("[语言] {0}  游戏内 总键 {1}  已译 {2}  缺失 {3}  覆盖率 {4}%", c, gmlX.Count, g2, gmlX.Count - g2, gp.ToString("0.0", CultureInfo.InvariantCulture)));
                if (g2 < gmlX.Count) bad = true;
            }
            if (bad) incomplete++;
        }
        if (codes.Count > 1) Console.WriteLine(L("  未达 100% 的语言: {0} 个", incomplete));
        return incomplete;
    }

    /// <summary>含中日韩文字的判据：语言包能装的 key 只可能是「中文原文」，纯 ASCII 键不算。</summary>
    private static bool HasCjk(string s)
    {
        foreach (var ch in s)
            if ((ch >= 0x3400 && ch <= 0x9fff) || (ch >= 0xf900 && ch <= 0xfaff) ||
                (ch >= 0x3000 && ch <= 0x303f) || (ch >= 0xff00 && ch <= 0xffef)) return true;
        return false;
    }

    /// <summary>游戏内 GML 文案表的中文原文（_zh 值）。语言包以「中文原文」为 key，
    /// 因此这一集合就是**游戏侧**的待译全集（与 C# 侧的 <see cref="Lang.Keys"/> 是两个域，分开统计才不会互相污染）。
    /// 只读 api/ 下两张表；目录不存在（例如只装了 exe）时返回空表。</summary>
    public static List<string> GmlKeys(string gameRoot)
    {
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var api = Path.Combine(Paths.NeutraledRoot(gameRoot), "api");
        foreach (var name in new[] { "ntl_i18n_init.gml", "ntl_i18n_out.gml" })
        {
            var f = Path.Combine(api, name);
            if (!File.Exists(f)) continue;
            try
            {
                foreach (var line in File.ReadLines(f))
                {
                    if (!line.Contains("ds_map_add(_zh")) continue;
                    int i = line.IndexOf('"');
                    if (i < 0 || ReadGmlString(line, ref i) == null) continue;   // 键
                    i = line.IndexOf('"', i);
                    if (i < 0) continue;
                    var v = ReadGmlString(line, ref i);                          // 值 = 中文原文
                    if (!string.IsNullOrEmpty(v) && seen.Add(v!)) list.Add(v!);
                }
            }
            catch (Exception ex) { Console.WriteLine(L("[语言] 读取 GML 表失败: {0}", ex.Message)); }
        }
        return list;
    }

    /// <summary>从 <paramref name="i"/> 指向的开引号处读一个 GML 字符串字面量，成功时把 i 移到闭引号之后。
    /// 只处理 \" \\ \n \t \r 这几种转义（GML 源码里就这几种）。</summary>
    private static string? ReadGmlString(string s, ref int i)
    {
        if (i >= s.Length || s[i] != '"') return null;
        var sb = new StringBuilder();
        for (i++; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '\\' && i + 1 < s.Length)
            {
                i++;
                sb.Append(s[i] switch { 'n' => '\n', 't' => '\t', 'r' => '\r', '"' => '"', '\\' => '\\', var e => e });
                continue;
            }
            if (c == '"') { i++; return sb.ToString(); }
            sb.Append(c);
        }
        return null;
    }

    /// <summary>导出待译模板：{ "code": …, "name": …, "map": { "中文key": "" }, "hint": { "中文key": "英文参考" } }。
    /// 译者只需填 map 里的值；把填好的文件存成 lang_&lt;code&gt;.json 就生效（空值会被 LoadExternal 丢弃，不会显示空白）。
    /// 返回导出的键数；失败返回 0。</summary>
    public static int ExportTemplate(string gameRoot, string code, string outPath)
    {
        if (!IsSafeCode(code)) { Console.WriteLine(L("[语言] 非法语言代码: {0}", code)); return 0; }
        code = code.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(outPath)) { Console.WriteLine(L("[语言] 导出失败：没有给出输出路径")); return 0; }
        var keys = Lang.Keys.ToList();
        var map = new JsonObject();
        var hint = new JsonObject();
        foreach (var k in keys)
        {
            map[k] = "";
            hint[k] = Lang.Has("en", k) ? Lang.Get("en", k) : "";   // 没有英文参考就留空，绝不把中文塞进 hint 骗译者
        }
        var name = "";
        if (TryRead(gameRoot, code, out _, out var nm, out _)) name = nm;
        var obj = new JsonObject
        {
            ["schema"] = 1,
            ["code"] = code,
            ["name"] = name,
            ["map"] = map,
            ["hint"] = hint
        };
        try
        {
            var full = Path.GetFullPath(outPath);
            var dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            Paths.SafeWrite(full, obj.ToJsonString(Indented));
        }
        catch (Exception ex) { Console.WriteLine(L("[语言] 导出失败: {0}", ex.Message)); return 0; }
        Console.WriteLine(L("[语言] 已导出待译模板: {0}（{1} 条键）", outPath, keys.Count));
        return keys.Count;
    }

    /// <summary>切换语言：校验代码可用 → 灌表 → Lang.SetCode → 写 config.json 的 lang（下次启动沿用）。
    /// 返回码：0 成功，1 失败（代码没有对应语言包），2 代码非法。</summary>
    public static int Use(string gameRoot, string code)
    {
        if (!IsSafeCode(code)) { Console.WriteLine(L("[语言] 非法语言代码: {0}", code)); return 2; }
        code = code.Trim().ToLowerInvariant();
        var f = FileOf(gameRoot, code);
        if (!BuiltIn.Contains(code, StringComparer.OrdinalIgnoreCase) && (f.Length == 0 || !File.Exists(f)))
        { Console.WriteLine(L("[语言] 找不到语言包: {0}", code)); return 1; }
        if (!BuiltIn.Contains(code, StringComparer.OrdinalIgnoreCase) && !Lang.HasTable(code))
        {
            if (TryRead(gameRoot, code, out var map, out _, out _)) Lang.AddTable(code, map);
        }
        Lang.SetCode(code);
        ConfigFile.Set(gameRoot, "lang", JsonValue.Create(Lang.Current));
        Console.WriteLine(L("[语言] 已切换语言: {0}", Lang.Current));
        return 0;
    }

    /// <summary>lang_ja.json → ja（认不出来返回空串）。</summary>
    private static string CodeFromFileName(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        const string prefix = "lang_";
        if (name.Length <= prefix.Length || !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return "";
        return name.Substring(prefix.Length);
    }

    /// <summary>磁盘上有包就先灌表（幂等、静默）：覆盖率统计的是"磁盘上的语言包"，不是"本次进程加载过的"。</summary>
    private static void EnsureTable(string gameRoot, string code)
    {
        if (BuiltIn.Contains(code, StringComparer.OrdinalIgnoreCase)) return;
        if (!IsSafeCode(code)) return;
        if (Lang.HasTable(code)) return;
        if (TryRead(gameRoot, code, out var map, out _, out _, quiet: true)) Lang.AddTable(code, map);
    }

    /// <summary>读一个语言包。成功给出「中文 key → 译文」表（已剔除空译文与"值等于 key"的未译占位）。
    /// 失败时 problem 里是原因；quiet=true 不打印（清单/覆盖率这类查询不该刷警告）。</summary>
    private static bool TryRead(string gameRoot, string code, out Dictionary<string, string> map, out string name, out string problem, bool quiet = false)
    {
        map = new Dictionary<string, string>(StringComparer.Ordinal);
        name = "";
        var path = FileOf(gameRoot, code);
        if (path.Length == 0 || !File.Exists(path))
        {
            problem = L("[语言] 找不到语言包: {0}", code);
            return false;
        }
        try
        {
            var obj = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            if (obj == null) { problem = L("JSON 根不是对象"); return false; }
            name = obj["name"]?.GetValue<string>() ?? "";
            var inner = obj["code"]?.GetValue<string>() ?? "";
            if (inner.Length > 0 && !inner.Equals(code, StringComparison.OrdinalIgnoreCase) && !quiet)
                Console.WriteLine(L("[语言] {0} 里的 code 字段是 {1}，与文件名不一致（以文件名为准）", Path.GetFileName(path), inner));
            var m = obj["map"] as JsonObject;
            if (m == null) { problem = L("缺少 map 对象"); return false; }
            foreach (var kv in m)
            {
                if (string.IsNullOrWhiteSpace(kv.Key)) continue;
                var v = kv.Value is JsonValue jv && jv.TryGetValue<string>(out var sv) ? sv : kv.Value?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(v)) continue;   // 空译文丢弃：留着会让界面显示空白
                // 值 == 中文 key 一律**保留**：日语 / 繁体中文里大量词条本来就与简体同形（警告 / 注意 / 操作 / 位置 / 透明度…），
            // 这是「明确译成同形」而不是「没翻」；丢掉会让覆盖率报出永远补不齐的假缺口，繁体包更是被错杀。
            // 真没翻的条目留在包里只会显示中文原文，与回退行为完全一致，无害。
                map[kv.Key] = v;
            }
            problem = "";
            return true;
        }
        catch (Exception ex)
        {
            problem = ex.Message;   // 含 JSON 语法错误位置，直接给用户看比吞掉强
            return false;
        }
    }
}
