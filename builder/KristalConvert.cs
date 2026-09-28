using System.Text;
using static Neutraled.Builder.Lang;

namespace Neutraled.Builder;

/// <summary>Kristal Lua -> NTL Lua 辅助转换（F7）
/// 扫描 Kristal mod 的 Lua 脚本，标出可自动改写 / 需人工确认 / 不支持的地方。</summary>
public static class KristalConvert
{
    public sealed class Report
    {
        public int Files = 0;
        public int Converted = 0;
        public List<string> Mechanical = new();
        public List<string> Manual = new();
        public List<string> Unsupported = new();
    }

    // 关键词 -> 说明（用 Contains 检测，避免正则转义问题）
    private static readonly string[,] ManualPatterns = new[,]
    {
        { "require(", "require 路径：Kristal 用 scripts.xxx，NTL 需改成 mod 相对路径" },
        { "love.graphics.", "LOVE2D 图形：只有基础绘制被桥接，图像/字体/画布需改用 GM 精灵" },
        { "love.audio.", "LOVE2D 音频：改用 Kristal.playSound / Music.play" },
        { "Assets.", "资源加载：需先把素材转成 NTL 资源包" },
        { "Registry.", "Kristal Registry：部分桥接，复杂用法需确认" },
        { "Stage(", "Stage 对象：未桥接" },
        { "Textbox", "Textbox：未桥接（改用 DELTARUNE 对话框）" },
        { "Game.world:addChild", "world:addChild 已桥接" },
        { "Game.battle", "battle 字段：部分桥接" },
    };

    private static readonly string[,] UnsupportedPatterns = new[,]
    {
        { "love.window.", "窗口操作：NTL 运行在 GM 里，无窗口控制" },
        { "love.event.", "LOVE 事件系统：改用 NTL 的 on_* 事件" },
        { "love.thread.", "线程：不支持" },
        { "os.execute", "外部进程：出于安全不支持" },
        { "io.popen", "外部进程：出于安全不支持" },
    };

    public static Report Convert(string srcDir, string outDir)
    {
        var rep = new Report();
        if (!Directory.Exists(srcDir)) { Console.WriteLine(L("[错误] 目录不存在: ") + srcDir); return rep; }

        Directory.CreateDirectory(outDir);
        var files = Directory.GetFiles(srcDir, "*.lua", SearchOption.AllDirectories);
        Console.WriteLine(L("扫描 ") + files.Length + L(" 个 .lua 文件..."));

        foreach (var f in files)
        {
            rep.Files++;
            var rel = Path.GetRelativePath(srcDir, f);
            var src = File.ReadAllText(f);
            var orig = src;

            // 唯一安全的机械替换
            if (src.Contains("love.graphics.print("))
            {
                src = src.Replace("love.graphics.print(", "ntl_console_log(");
                rep.Mechanical.Add(rel + ": love.graphics.print -> ntl_console_log");
            }

            for (int i = 0; i < ManualPatterns.GetLength(0); i++)
                if (src.Contains(ManualPatterns[i, 0]))
                    rep.Manual.Add(rel + ": " + ManualPatterns[i, 1]);

            for (int i = 0; i < UnsupportedPatterns.GetLength(0); i++)
                if (src.Contains(UnsupportedPatterns[i, 0]))
                    rep.Unsupported.Add(rel + ": " + UnsupportedPatterns[i, 1]);

            var outPath = Path.Combine(outDir, rel);
            var outParent = Path.GetDirectoryName(outPath);
            if (outParent != null) Directory.CreateDirectory(outParent);
            var nl = Environment.NewLine;
            var header = "-- 由 Neutraled KristalConvert 转换（原文件: " + rel + "）" + nl +
                         "-- 转换时间: " + DateTime.Now.ToString("s") + nl +
                         "-- 需要人工检查的地方见 CONVERSION.md" + nl + nl;
            File.WriteAllText(outPath, header + src);
            if (src != orig) rep.Converted++;
        }

        var sb = new StringBuilder();
        var br = Environment.NewLine;
        sb.Append("# Kristal -> Neutraled 转换报告" + br + br);
        sb.Append("- 扫描文件: " + rep.Files + br);
        sb.Append("- 自动改写: " + rep.Converted + br);
        sb.Append("- 需人工确认: " + rep.Manual.Count + br);
        sb.Append("- 不支持: " + rep.Unsupported.Count + br + br);

        if (rep.Mechanical.Count > 0)
        {
            sb.Append("## 已自动改写" + br);
            foreach (var m in rep.Mechanical.Distinct().Take(50)) sb.Append("- " + m + br);
            sb.Append(br);
        }
        if (rep.Manual.Count > 0)
        {
            sb.Append("## 需人工确认（重要）" + br);
            foreach (var m in rep.Manual.Distinct().Take(80)) sb.Append("- " + m + br);
            sb.Append(br);
        }
        if (rep.Unsupported.Count > 0)
        {
            sb.Append("## 不支持（需要重写）" + br);
            foreach (var m in rep.Unsupported.Distinct().Take(50)) sb.Append("- " + m + br);
            sb.Append(br);
        }

        File.WriteAllText(Path.Combine(outDir, "CONVERSION.md"), sb.ToString());

        Console.WriteLine(L("===== Kristal 转换完成 ====="));
        Console.WriteLine(L("  文件: ") + rep.Files + L("  自动改写: ") + rep.Converted);
        Console.WriteLine(L("  需人工确认: ") + rep.Manual.Count + L("  不支持: ") + rep.Unsupported.Count);
        Console.WriteLine(L("  输出: ") + outDir);
        return rep;
    }
}