using static Neutraled.Builder.Lang;
namespace Neutraled.Builder;

/// <summary>mod 脚手架 —— 一条命令生成可运行的 mod 骨架。</summary>
public static class Scaffold
{
    public static int Create(string gameRoot, string modName, string author, string chapter)
    {
        if (string.IsNullOrWhiteSpace(modName))
        {
            Console.WriteLine(L("[错误] 请指定 mod 名字：--new-mod MyMod"));
            return 1;
        }
        var safe = new string(modName.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());
        if (safe.Length == 0) safe = "MyMod";
        if (string.IsNullOrWhiteSpace(author)) author = "unknown";

        var root = Paths.NeutraledRoot(gameRoot);
        var modDir = Path.Combine(root, "mods", safe, author, chapter);
        if (Directory.Exists(modDir))
        {
            Console.WriteLine(L("[错误] 目录已存在: {0}", modDir));
            return 1;
        }

        Directory.CreateDirectory(modDir);
        Directory.CreateDirectory(Path.Combine(modDir, "gml"));
        Directory.CreateDirectory(Path.Combine(modDir, "patches"));
        Directory.CreateDirectory(Path.Combine(modDir, "hooks"));

        var modId = safe.ToLowerInvariant();
        var chDecl = chapter.StartsWith("chapter") ? ("Chapter:" + chapter.Substring(7)) : chapter;

        // ---------- mod.json ----------
        var modJson = $@"{{
  ""id"": ""{modId}"",
  ""name"": ""{safe}"",
  ""author"": ""{author}"",
  ""version"": ""1.0.0"",
  ""enabled"": true,
  ""description"": ""我的第一个 Neutraled mod"",
  ""chapters"": [""{chDecl}""],
  ""hooks"": []
}}";
        File.WriteAllText(Path.Combine(modDir, "mod.json"), modJson);

        // ---------- main.lua ----------
        var mainLua = "-- ============================================================\n" +
                      "--  " + safe + " —— mod 入口脚本\n" +
                      "--  改完保存、重启游戏即可看到效果（不需要重新部署！）\n" +
                      "-- ============================================================\n" +
                      "\n" +
                      "print(\"[" + safe + "] 你好，Neutraled！\")\n" +
                      "\n" +
                      "-- 打印一些游戏信息，确认 mod 已被加载\n" +
                      "print(\"  Kristal 兼容层: \" .. type(Kristal))\n" +
                      "print(\"  love 桥接: \" .. type(love))\n" +
                      "\n" +
                      "-- ============ 试着改这里 ============\n" +
                      "-- 把下面的文字改成你自己的，重启游戏后看日志\n" +
                      "print(\"  我的 mod 正在运行！\")\n" +
                      "\n" +
                      "-- 提示：进游戏后按 F2 打开控制台，输入 help 看可用命令\n";
        File.WriteAllText(Path.Combine(modDir, "main.lua"), mainLua);

        // ---------- on_frame.lua ----------
        var frameLua = "-- 每帧执行（arg 是帧号）\n" +
                       "-- 注意：live mod 的 scripts 里声明 \"on_frame\": \"on_frame.lua\" 才会调用\n" +
                       "if arg % 300 == 0 then\n" +
                       "    print(\"[" + safe + "] 第 \" .. arg .. \" 帧\")\n" +
                       "end\n";
        File.WriteAllText(Path.Combine(modDir, "on_frame.lua"), frameLua);

        // ---------- hooks/example.lua ----------
        var hookLua = "-- Hook 示例：拦截游戏的 snd_play（播放音效时）\n" +
                      "-- 在 mod.json 的 hooks 里声明后生效：\n" +
                      "--   { \"script\": \"snd_play\", \"mode\": \"pre\", \"handler\": \"hooks/example.lua\" }\n" +
                      "--\n" +
                      "-- args[1] 是声音资源 ID；返回 nil = 不接管，继续执行原函数\n" +
                      "\n" +
                      "print(\"[hook] snd_play -> \" .. tostring(args[1]))\n" +
                      "return nil\n";
        File.WriteAllText(Path.Combine(modDir, "hooks", "example.lua"), hookLua);

        // ---------- README.md ----------
        var readme = "# " + safe + "\n\n" +
                     "你的第一个 Neutraled mod。\n\n" +
                     "## 文件说明\n\n" +
                     "| 文件 | 作用 |\n|---|---|\n" +
                     "| `mod.json` | 元数据。`id`/`name`/`author` 必填；`hooks` 声明要拦截的游戏函数 |\n" +
                     "| `main.lua` | 入口脚本，启动时执行一次 |\n" +
                     "| `on_frame.lua` | 每帧执行（需在 mod.json 的 scripts 里声明） |\n" +
                     "| `gml/` | （可选）要注入的 GML 脚本，**文件名必须等于函数名** |\n" +
                     "| `patches/` | （可选）要替换的游戏函数 |\n\n" +
                     "## 怎么跑\n\n" +
                     "```powershell\n" +
                     "# 关掉游戏（重要！否则部署会静默失败）\n" +
                     "Get-Process DELTARUNE -EA 0 | Stop-Process -Force\n\n" +
                     "# 部署并启动\n" +
                     "E:\\steam\\steamapps\\common\\DELTARUNE\\Neutraled\\launch.ps1\n" +
                     "```\n\n" +
                     "## 怎么改\n\n" +
                     "**改 Lua 脚本不需要重新部署** —— 直接改 `main.lua`，重启游戏就生效。\n\n" +
                     "看效果：\n" +
                     "1. 日志 `%LOCALAPPDATA%\\DELTARUNE\\Neutraled\\dr-api.log`（搜 `[" + safe + "]`）\n" +
                     "2. 游戏内按 **F2** 打开控制台，输入 `help` / `mods` / `version`\n\n" +
                     "## 常用 API\n\n" +
                     "```lua\n" +
                     "Kristal.getFlag(\"my_flag\")          -- 读 flag\n" +
                     "Kristal.setFlag(\"my_flag\", 1)       -- 写 flag\n" +
                     "Kristal.playSound(\"snd_xxx\")         -- 播音效\n" +
                     "print(\"你好\")                        -- 写日志\n" +
                     "local lib = require(\"lib.mylib\")     -- 加载模块\n" +
                     "```\n\n" +
                     "更多见 `Neutraled/docs/SCRIPTING.md`。\n";
        File.WriteAllText(Path.Combine(modDir, "README.md"), readme);

        Console.WriteLine(L("===== mod 骨架已生成 ====="));
        Console.WriteLine(L("  目录: {0}", modDir));
        Console.WriteLine();
        Console.WriteLine(L("  生成的文件:"));
        Console.WriteLine(L("    mod.json       元数据（已填好，可直接用）"));
        Console.WriteLine(L("    main.lua       入口脚本 ← 从这里开始改"));
        Console.WriteLine(L("    on_frame.lua   每帧执行"));
        Console.WriteLine(L("    hooks/         函数 Hook 示例"));
        Console.WriteLine(L("    README.md      使用说明"));
        Console.WriteLine();
        Console.WriteLine(L("  下一步:"));
        Console.WriteLine(L("    1) 编辑 main.lua（随便改一行 print）"));
        Console.WriteLine(L("    2) 关闭游戏后运行 Neutraled\\launch.ps1"));
        Console.WriteLine(L("    3) 看日志里的 [") + safe + L("] 输出"));
        Console.WriteLine();
        return 0;
    }
}
