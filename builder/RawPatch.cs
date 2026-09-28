using UndertaleModLib;
using UndertaleModLib.Compiler;
using UndertaleModLib.Decompiler;
using Underanalyzer.Decompiler;
using UndertaleModLib.Models;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>原始代码替换 —— mod 可以彻底改写任意函数/对象事件的完整实现。</summary>
public static class RawPatch
{
    public static int Apply(CodeImportGroup group, UndertaleData data, List<ModEntry> mods)
    {
        int applied = 0;
        var gctx = new GlobalDecompileContext(data);

        foreach (var m in mods)
        {
            var rawDir = Path.Combine(m.Dir, "raw");
            if (!Directory.Exists(rawDir)) continue;

            foreach (var f in Directory.GetFiles(rawDir, "*.gml"))
            {
                var codeName = Path.GetFileNameWithoutExtension(f);
                var code = data.Code.ByName(codeName);
                if (code == null)
                {
                    Paths.Log(L("    [警告] raw 替换目标不存在: {0}（{1}）", codeName, m.Id));
                    continue;
                }

                try
                {
                    var newSrc = File.ReadAllText(f);

                    if (codeName.StartsWith("gml_Script_", StringComparison.Ordinal))
                    {
                        var fn = codeName.Substring("gml_Script_".Length);
                        if (!newSrc.Contains("function " + fn))
                            Paths.Log(L("    [警告] {0}: 新实现里没有 function {1}()，可能无法被调用", codeName, fn));
                    }

                    // 备份原实现（便于回滚与调试）
                    try
                    {
                        var bakDir = Path.Combine(Paths.NeutraledRoot(Paths.DetectGameRoot()), ".raw-backup");
                        Directory.CreateDirectory(bakDir);
                        var bak = Path.Combine(bakDir, codeName + ".orig.gml");
                        if (!File.Exists(bak)) File.WriteAllText(bak, DecompileWith(gctx, data, codeName));
                    }
                    catch { }

                    group.QueueReplace(code, newSrc);
                    applied++;
                    Paths.Log(L("    raw 替换: {0}（来自 {1}）", codeName, m.Id));
                }
                catch (Exception ex) { Paths.Log(L("    [错误] raw 替换失败 {0}: {1}", codeName, ex.Message)); }
            }
        }
        return applied;
    }

    private static string DecompileWith(GlobalDecompileContext gctx, UndertaleData data, string codeName)
    {
        var code = data.Code.ByName(codeName);
        if (code == null) return "";
        var dctx = new Underanalyzer.Decompiler.DecompileContext(gctx, code, null!);
        return dctx.DecompileToString();
    }
}
