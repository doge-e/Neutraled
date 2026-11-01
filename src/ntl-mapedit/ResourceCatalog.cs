using UndertaleModLib;
using UndertaleModLib.Models;

namespace NtlMapEdit;

/// <summary>
/// Read-only catalogue over every resource family UTMT parsed out of a data.win.
/// Shared by the GUI resource browser and the headless `resource-browser-test` CLI,
/// so "the editor can see it" and "the test proves it" use exactly one implementation.
/// Nothing here writes to disk or mutates the model.
/// </summary>
public static class ResourceCatalog
{
    public sealed record Entry(string Name, string Detail, string Full);

    public sealed record Category(string Key, string Label, Func<UndertaleData, IEnumerable<Entry>> Build);

    private static string N(UndertaleString? s) => s?.Content ?? "(unnamed)";

    /// <summary>Order is what the GUI tree shows; keys are stable for CLI output.</summary>
    public static readonly IReadOnlyList<Category> Categories = new[]
    {
        new Category("rooms", "房间 Rooms", data => data.Rooms.Select(r => new Entry(
            N(r.Name),
            $"{r.Width}x{r.Height}  层 {r.Layers?.Count ?? 0}  实例 {CountInstances(r)}",
            $"房间 {N(r.Name)}\n尺寸 {r.Width} x {r.Height}\n层 {r.Layers?.Count ?? 0}  实例 {CountInstances(r)}\n创建代码 {(r.CreationCodeId == null ? "无" : N(r.CreationCodeId.Name))}\n背景色 #{r.BackgroundColor & 0xFFFFFF:X6}  绘制背景色 {r.DrawBackgroundColor}"))),

        new Category("objects", "对象 Objects", data => data.GameObjects.Select(o => new Entry(
            N(o.Name),
            $"精灵 {N(o.Sprite?.Name)}  深度 {o.Depth}  visible {o.Visible}  solid {o.Solid}",
            $"对象 {N(o.Name)}\n精灵 {N(o.Sprite?.Name)}\n深度 {o.Depth}  可见 {o.Visible}  实心 {o.Solid}  持久 {o.Persistent}\n事件 {o.Events?.Count ?? 0} 组  父对象 {N(o.ParentId?.Name)}"))),

        new Category("sprites", "精灵 Sprites", data => data.Sprites.Select(s => new Entry(
            N(s.Name),
            $"{s.Width}x{s.Height}  帧 {s.Textures?.Count ?? 0}  原点 {s.OriginX},{s.OriginY}",
            $"精灵 {N(s.Name)}\n尺寸 {s.Width} x {s.Height}\n帧数 {s.Textures?.Count ?? 0}\n原点 {s.OriginX}, {s.OriginY}\n边界框 左{s.MarginLeft} 右{s.MarginRight} 下{s.MarginBottom} 上{s.MarginTop})"))),

        new Category("backgrounds", "背景/瓦片集 Backgrounds", data => data.Backgrounds.Select(b => new Entry(
            N(b.Name),
            $"瓦片 {b.GMS2TileWidth}x{b.GMS2TileHeight}  列 {b.GMS2TileColumns}  共 {b.GMS2TileCount}",
            $"背景/瓦片集 {N(b.Name)}\n瓦片尺寸 {b.GMS2TileWidth} x {b.GMS2TileHeight}\n列数 {b.GMS2TileColumns}  瓦片总数 {b.GMS2TileCount}\n纹理页 {N(b.Texture?.Name)} 源 {b.Texture?.SourceX},{b.Texture?.SourceY} {b.Texture?.SourceWidth}x{b.Texture?.SourceHeight}"))),

        new Category("fonts", "字体 Fonts", data => data.Fonts.Select(f => new Entry(
            N(f.Name),
            $"EmSize {f.EmSize}  字形 {f.Glyphs?.Count ?? 0}  行高 {f.LineHeight}",
            $"字体 {N(f.Name)}\n显示名 {N(f.DisplayName)}\nEmSize {f.EmSize}  粗体 {f.Bold}  斜体 {f.Italic}\n字形数 {f.Glyphs?.Count ?? 0}  行高 {f.LineHeight}  上伸 {f.Ascender}\n范围 {f.RangeStart}..{f.RangeEnd}  缩放 {f.ScaleX},{f.ScaleY}\n纹理页 {N(f.Texture?.Name)}"))),

        new Category("sounds", "音效 Sounds", data => data.Sounds.Select(s => new Entry(
            N(s.Name),
            $"文件 {N(s.File)}  音量 {s.Volume}",
            $"音效 {N(s.Name)}\n文件 {N(s.File)}\n音量 {s.Volume}  音高 {s.Pitch}  预载 {s.Preload}  类型 {s.Type}\n音频组 {N(s.AudioGroup?.Name)}  长度 {s.AudioLength}"))),

        new Category("code", "脚本/代码 Scripts & Code", BuildCodeEntries),

        new Category("shaders", "着色器 Shaders", data => data.Shaders.Select(s => new Entry(
            N(s.Name),
            $"类型 {s.Type}  GLSL {Len(s.GLSL_Vertex)}+{Len(s.GLSL_Fragment)}",
            $"着色器 {N(s.Name)}\n类型 {s.Type}\n版本 {s.Version}\nGLSL ES 顶点 {Len(s.GLSL_ES_Vertex)} 字节 / 片段 {Len(s.GLSL_ES_Fragment)} 字节\nGLSL 顶点 {Len(s.GLSL_Vertex)} / 片段 {Len(s.GLSL_Fragment)}\nHLSL9 顶点 {Len(s.HLSL9_Vertex)} / 片段 {Len(s.HLSL9_Fragment)}\n顶点属性 {s.VertexShaderAttributes?.Count ?? 0}"))),

        new Category("sequences", "序列 Sequences", data => data.Sequences.Select(q => new Entry(
            N(q.Name),
            $"长度 {q.Length}  尺寸 {q.Width}x{q.Height}  轨道 {q.Tracks?.Count ?? 0}",
            $"序列 {N(q.Name)}\n长度 {q.Length}  播放速度 {q.PlaybackSpeed}  速度类型 {q.PlaybackSpeedType}\n尺寸 {q.Width} x {q.Height}  原点 {q.OriginX},{q.OriginY}\n轨道 {q.Tracks?.Count ?? 0}  关键帧 {q.Moments?.Count ?? 0}  函数 {q.FunctionIDs?.Count ?? 0}  广播 {q.BroadcastMessages?.Count ?? 0}"))),

        new Category("timelines", "时间线 Timelines", data => data.Timelines.Select(t => new Entry(
            N(t.Name),
            $"时刻 {t.Moments?.Count ?? 0}",
            $"时间线 {N(t.Name)}\n时刻 {t.Moments?.Count ?? 0}"))),

        new Category("textures", "纹理页 Textures", data => data.EmbeddedTextures.Select((t, i) => new Entry(
            N(t.Name) is "(unnamed)" ? $"纹理页 #{i}" : N(t.Name),
            $"{t.TextureWidth}x{t.TextureHeight}  外部 {t.TextureExternal}",
            $"嵌入纹理 #{i}  {N(t.Name)}\n尺寸 {t.TextureWidth} x {t.TextureHeight}\n缩放 {t.Scaled}  生成 mipmap {t.GeneratedMips}  外部 {t.TextureExternal}\n解码 {(t.TextureData?.Image == null ? "失败/无" : "成功")}"))),

        new Category("paths", "路径 Paths", data => data.Paths.Select(p => new Entry(
            N(p.Name),
            $"点 {p.Points?.Count ?? 0}  闭合 {p.IsClosed}  平滑 {p.IsSmooth}",
            $"路径 {N(p.Name)}\n点 {p.Points?.Count ?? 0}  闭合 {p.IsClosed}  平滑 {p.IsSmooth}  精度 {p.Precision}"))),
    };

    private static int CountInstances(UndertaleRoom r) => r.Layers?.Sum(l => l.InstancesData?.Instances?.Count ?? 0) ?? 0;

    private static int Len(UndertaleString? s) => s?.Content?.Length ?? 0;

    private static IEnumerable<Entry> BuildCodeEntries(UndertaleData data)
    {
        var byCode = new Dictionary<UndertaleCode, string>();
        if (data.Scripts != null)
        {
            foreach (var sc in data.Scripts)
            {
                if (sc?.Code != null) byCode[sc.Code] = N(sc.Name);
            }
        }

        if (data.Scripts != null)
        {
            foreach (var sc in data.Scripts)
            {
                var code = sc?.Code;
                yield return new Entry(
                    N(sc!.Name),
                    $"脚本 -> {N(code?.Name)}  {code?.Length ?? 0} 字节  指令 {code?.Instructions?.Count ?? 0}",
                    $"脚本 {N(sc.Name)}\n代码条目 {N(code?.Name)}\n字节 {code?.Length ?? 0}  指令 {code?.Instructions?.Count ?? 0}\n局部变量 {code?.LocalsCount ?? 0}  参数 {code?.ArgumentsCount ?? 0}");
            }
        }

        if (data.Code == null) yield break;
        foreach (var code in data.Code)
        {
            if (code == null) continue;
            byCode.TryGetValue(code, out var script);
            var owner = script == null ? "代码" : "属于脚本 " + script;
            yield return new Entry(
                N(code.Name),
                $"{owner}  {code.Length} 字节  指令 {code.Instructions?.Count ?? 0}",
                $"代码条目 {N(code.Name)}\n{owner}\n字节 {code.Length}  指令 {code.Instructions?.Count ?? 0}\n局部变量 {code.LocalsCount}  参数 {code.ArgumentsCount}\n偏移 {code.Offset}");
        }
    }

    public static IEnumerable<Entry> Enumerate(UndertaleData data, string key)
    {
        var cat = Categories.FirstOrDefault(c => c.Key == key);
        return cat?.Build(data) ?? Array.Empty<Entry>();
    }

    /// <summary>Category -> entry count; used by the CLI self-check and the tree labels.</summary>
    public static (string Key, string Label, int Count)[] Counts(UndertaleData data)
    {
        var list = new List<(string, string, int)>();
        foreach (var cat in Categories)
        {
            int n;
            try { n = cat.Build(data).Count(); }
            catch (Exception ex) { Console.Error.WriteLine($"[catalog] {cat.Key}: {ex.Message}"); n = -1; }
            list.Add((cat.Key, cat.Label, n));
        }
        return list.ToArray();
    }
}
