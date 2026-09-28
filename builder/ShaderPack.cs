using System.Text.Json;
using UndertaleModLib;
using UndertaleModLib.Models;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>着色器资源包里的一个着色器（shaders/&lt;名字&gt;.json）。
/// 原始着色器字节（HLSL11 / PSSL / Cg_*）以 base64 存在 JSON 里：着色器没有外部依赖，
/// 不像精灵要纹理页、声音要内嵌音频，所以「一个 JSON 自包含」就够。</summary>
public sealed class ShaderDef
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string GLSL_ES_Fragment { get; set; } = "";
    public string GLSL_ES_Vertex { get; set; } = "";
    public string GLSL_Fragment { get; set; } = "";
    public string GLSL_Vertex { get; set; } = "";
    public string HLSL9_Fragment { get; set; } = "";
    public string HLSL9_Vertex { get; set; } = "";
    public string[] VertexShaderAttributes { get; set; } = Array.Empty<string>();

    /// <summary>原始字节（base64）。null = 该平台没有数据（IsNull=true）；空串 = 有数据但长度为 0。</summary>
    public string? HLSL11_VertexData { get; set; }
    public string? HLSL11_PixelData { get; set; }
    public string? PSSL_VertexData { get; set; }
    public string? PSSL_PixelData { get; set; }
    public string? Cg_PSVita_VertexData { get; set; }
    public string? Cg_PSVita_PixelData { get; set; }
    public string? Cg_PS3_VertexData { get; set; }
    public string? Cg_PS3_PixelData { get; set; }
}

/// <summary>**着色器资源包**（shaders/*.json）。
///
/// ★ 为什么需要它：层（--layer-from-base 抽出来的）只带代码、不带资源。精灵/声音/字体有
///   --export-packs 这条通道，**着色器原来没有** —— 于是代码里的 asset_get_index("sh_color_filter")
///   在产物里永远拿不到东西（运行期静默 -1，滤镜整个失效，日志里一个字都没有）。
///   percentage [color] 的滤镜就是这么静默失效的。
///
/// 用法：
///   1) 把某个 mod 的 data.win 里的着色器导出成资源包（与官方基线比差异）：
///        ntl-builder.exe --export-shaders &lt;mod 的 ref/data.win&gt; &lt;mod 章节目录&gt; --base &lt;官方基线 data.win&gt;
///      产物：&lt;mod 章节目录&gt;/shaders/&lt;名字&gt;.json
///   2) 部署时自动导入（Injector 2.42 段）：同名替换、新名字追加，索引保持在末尾。</summary>
public static class ShaderPack
{
    /// <summary>导出的资源包目录名（mod 章节目录下）。</summary>
    public const string DirName = "shaders";

    /// <summary>写入 JSON 的选项：与读取端同一套，附加缩进。</summary>
    private static readonly JsonSerializerOptions JsonOut = new(Paths.Json) { WriteIndented = true };

    // ------------------------------------------------------------------ 导入

    /// <summary>把 shaders/*.json 导入 data（同名替换，新名字追加）。返回导入的着色器数。</summary>
    public static int Import(UndertaleData data, string shaderDir)
    {
        if (!Directory.Exists(shaderDir)) return 0;
        var jsonFiles = Directory.GetFiles(shaderDir, "*.json", SearchOption.TopDirectoryOnly);
        if (jsonFiles.Length == 0) return 0;

        int added = 0, replaced = 0, skipped = 0;
        foreach (var jf in jsonFiles)
        {
            ShaderDef? def;
            try { def = JsonSerializer.Deserialize<ShaderDef>(File.ReadAllText(jf), Paths.Json); }
            catch (Exception ex) { Paths.Log(L("    [警告] 着色器解析失败 {0}: {1}", Path.GetFileName(jf), ex.Message)); skipped++; continue; }
            if (def == null || string.IsNullOrEmpty(def.Name)) { skipped++; continue; }

            try
            {
                var existing = data.Shaders.FirstOrDefault(s => s.Name?.Content == def.Name);
                if (existing != null) { Apply(existing, def, data); replaced++; }
                else
                {
                    var sh = new UndertaleShader();
                    Apply(sh, def, data);
                    data.Shaders.Add(sh);
                    added++;
                }
            }
            catch (Exception ex)
            {
                Paths.Log(L("    [警告] 着色器导入失败 {0}: {1}: {2}", def.Name, ex.GetType().Name, ex.Message));
                skipped++;
            }
        }
        Paths.Log(L("    着色器导入: 新增 {0} / 替换 {1} / 跳过 {2}", added, replaced, skipped));
        return added + replaced;
    }

    private static void Apply(UndertaleShader sh, ShaderDef def, UndertaleData data)
    {
        sh.Name = data.Strings.MakeString(def.Name);
        if (!string.IsNullOrEmpty(def.Type) && Enum.TryParse<UndertaleShader.ShaderType>(def.Type, out var t)) sh.Type = t;
        sh.GLSL_ES_Fragment = data.Strings.MakeString(def.GLSL_ES_Fragment ?? "");
        sh.GLSL_ES_Vertex = data.Strings.MakeString(def.GLSL_ES_Vertex ?? "");
        sh.GLSL_Fragment = data.Strings.MakeString(def.GLSL_Fragment ?? "");
        sh.GLSL_Vertex = data.Strings.MakeString(def.GLSL_Vertex ?? "");
        sh.HLSL9_Fragment = data.Strings.MakeString(def.HLSL9_Fragment ?? "");
        sh.HLSL9_Vertex = data.Strings.MakeString(def.HLSL9_Vertex ?? "");

        sh.VertexShaderAttributes.Clear();
        foreach (var a in def.VertexShaderAttributes ?? Array.Empty<string>())
            sh.VertexShaderAttributes.Add(new UndertaleShader.VertexShaderAttribute { Name = data.Strings.MakeString(a ?? "") });

        sh.HLSL11_VertexData = Raw(def.HLSL11_VertexData);
        sh.HLSL11_PixelData = Raw(def.HLSL11_PixelData);
        sh.PSSL_VertexData = Raw(def.PSSL_VertexData);
        sh.PSSL_PixelData = Raw(def.PSSL_PixelData);
        sh.Cg_PSVita_VertexData = Raw(def.Cg_PSVita_VertexData);
        sh.Cg_PSVita_PixelData = Raw(def.Cg_PSVita_PixelData);
        sh.Cg_PS3_VertexData = Raw(def.Cg_PS3_VertexData);
        sh.Cg_PS3_PixelData = Raw(def.Cg_PS3_PixelData);
    }

    private static UndertaleShader.UndertaleRawShaderData Raw(string? b64)
    {
        if (b64 == null) return new UndertaleShader.UndertaleRawShaderData { IsNull = true };
        byte[] bytes;
        try { bytes = Convert.FromBase64String(b64); }
        catch { bytes = Array.Empty<byte>(); }
        return new UndertaleShader.UndertaleRawShaderData { IsNull = false, Data = bytes };
    }

    // ------------------------------------------------------------------ 导出

    /// <summary>把 src 里的着色器导出成资源包（写进 outDir/shaders/）。
    /// baseline 不为 null 时只导出「基线没有 / 与基线不同」的着色器；返回导出的个数；失败返回 -1。</summary>
    public static int Export(UndertaleData src, UndertaleData? baseline, string outDir)
    {
        try
        {
            var dir = Path.Combine(outDir, DirName);
            Directory.CreateDirectory(dir);
            int written = 0, same = 0;
            foreach (var sh in src.Shaders)
            {
                var name = sh.Name?.Content ?? "";
                if (name.Length == 0) continue;
                if (baseline != null)
                {
                    var b = baseline.Shaders.FirstOrDefault(x => x.Name?.Content == name);
                    if (b != null && Same(b, sh)) { same++; continue; }
                }
                var def = ToDef(sh);
                File.WriteAllText(Path.Combine(dir, SafeName(name) + ".json"),
                    JsonSerializer.Serialize(def, JsonOut), new System.Text.UTF8Encoding(false));
                written++;
                Paths.Log(L("    导出着色器: {0}（{1}，原始字节约 {2}）", name, def.Type, RawBytes(def)));
            }
            Paths.Log(L("  着色器导出: {0} 个（与基线相同跳过 {1} 个）-> {2}", written, same, dir));
            return written;
        }
        catch (Exception ex)
        {
            Paths.Log(L("  [错误] 着色器导出失败: {0}: {1}", ex.GetType().Name, ex.Message));
            return -1;
        }
    }

    private static int RawBytes(ShaderDef d)
    {
        int n = 0;
        foreach (var s in new[] { d.HLSL11_VertexData, d.HLSL11_PixelData, d.PSSL_VertexData, d.PSSL_PixelData,
                                  d.Cg_PSVita_VertexData, d.Cg_PSVita_PixelData, d.Cg_PS3_VertexData, d.Cg_PS3_PixelData })
            if (!string.IsNullOrEmpty(s)) n += (s!.Length / 4) * 3;
        return n;
    }

    private static ShaderDef ToDef(UndertaleShader sh) => new()
    {
        Name = sh.Name?.Content ?? "",
        Type = sh.Type.ToString(),
        GLSL_ES_Fragment = sh.GLSL_ES_Fragment?.Content ?? "",
        GLSL_ES_Vertex = sh.GLSL_ES_Vertex?.Content ?? "",
        GLSL_Fragment = sh.GLSL_Fragment?.Content ?? "",
        GLSL_Vertex = sh.GLSL_Vertex?.Content ?? "",
        HLSL9_Fragment = sh.HLSL9_Fragment?.Content ?? "",
        HLSL9_Vertex = sh.HLSL9_Vertex?.Content ?? "",
        VertexShaderAttributes = sh.VertexShaderAttributes == null
            ? Array.Empty<string>()
            : sh.VertexShaderAttributes.Select(a => a.Name?.Content ?? "").ToArray(),
        HLSL11_VertexData = B64(sh.HLSL11_VertexData),
        HLSL11_PixelData = B64(sh.HLSL11_PixelData),
        PSSL_VertexData = B64(sh.PSSL_VertexData),
        PSSL_PixelData = B64(sh.PSSL_PixelData),
        Cg_PSVita_VertexData = B64(sh.Cg_PSVita_VertexData),
        Cg_PSVita_PixelData = B64(sh.Cg_PSVita_PixelData),
        Cg_PS3_VertexData = B64(sh.Cg_PS3_VertexData),
        Cg_PS3_PixelData = B64(sh.Cg_PS3_PixelData),
    };

    private static string? B64(UndertaleShader.UndertaleRawShaderData? d)
    {
        if (d == null || d.IsNull || d.Data == null) return null;
        return Convert.ToBase64String(d.Data);
    }

    /// <summary>两个着色器是否完全一致（Type / 六段源码 / 顶点属性 / 八份原始字节）。</summary>
    public static bool Same(UndertaleShader a, UndertaleShader b) =>
        JsonSerializer.Serialize(ToDef(a), Paths.Json) == JsonSerializer.Serialize(ToDef(b), Paths.Json);

    private static string SafeName(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
        return sb.ToString();
    }
}
