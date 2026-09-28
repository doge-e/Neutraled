using static Neutraled.Builder.Lang;
namespace Neutraled.Builder;

/// <summary>mod 模板库 + IDE 支持（D4 / D5）</summary>
public static class Templates
{
    public sealed record Template(string Id, string Name, string Desc);

    public static readonly Template[] All = new[]
    {
        new Template("basic",    "基础 mod", "最小可运行骨架"),
        new Template("dialogue", "对话 mod", "在游戏里显示自定义对话"),
        new Template("battle",   "战斗 mod", "自定义敌人与弹幕"),
        new Template("map",      "地图 mod", "加载 Kristal 转换的地图并可走动"),
        new Template("skin",     "换皮 mod", "运行时替换精灵图片"),
        new Template("interop",  "联动 mod", "导出 API 供其他 mod 调用 + 监听事件"),
    };

    public static int Create(string gameRoot, string modName, string author, string chapter, string template)
    {
        var safe = new string(modName.Where(c => char.IsLetterOrDigit(c) || c == 95 || c == 45).ToArray());
        if (safe.Length == 0) safe = "MyMod";
        if (string.IsNullOrWhiteSpace(author)) author = "unknown";
        var tpl = All.FirstOrDefault(t => t.Id == template) ?? All[0];

        var root = Paths.NeutraledRoot(gameRoot);
        var modDir = Path.Combine(root, "mods", safe, author, chapter);
        if (Directory.Exists(modDir)) { Console.WriteLine(L("[错误] 目录已存在: ") + modDir); return 1; }

        foreach (var d in new[] { "", "hooks", "gml", "patches", "raw" })
            Directory.CreateDirectory(Path.Combine(modDir, d));

        var modId = safe.ToLowerInvariant();
        var chDecl = chapter.StartsWith("chapter") ? ("Chapter:" + chapter.Substring(7)) : chapter;

        var hooksLine = tpl.Id == "dialogue"
            ? "    { script: scr_text }"
            : tpl.Id == "battle"
                ? "    { script: snd_play }"
                : "";
        var extra = tpl.Id == "skin" ? "SKIN_EXTRA" : "";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"id\": \"" + modId + "\",");
        sb.AppendLine("  \"name\": \"" + safe + "\",");
        sb.AppendLine("  \"author\": \"" + author + "\",");
        sb.AppendLine("  \"version\": \"1.0.0\",");
        sb.AppendLine("  \"enabled\": true,");
        sb.AppendLine("  \"description\": \"" + tpl.Desc + "\",");
        sb.AppendLine("  \"chapters\": [\"" + chDecl + "\"],");
        sb.AppendLine("  \"scripts\": { \"on_init\": \"main.lua\" },");
        if (hooksLine != "")
        {
            sb.AppendLine("  \"hooks\": [");
            if (tpl.Id == "dialogue") sb.AppendLine("    { \"script\": \"scr_text\", \"mode\": \"pre\", \"handler\": \"hooks/text.lua\" }");
            else sb.AppendLine("    { \"script\": \"snd_play\", \"mode\": \"pre\", \"handler\": \"hooks/snd.lua\" }");
            sb.AppendLine("  ]");
        }
        else sb.AppendLine("  \"hooks\": []");
        sb.AppendLine("}");
        File.WriteAllText(Path.Combine(modDir, "mod.json"), sb.ToString());

        File.WriteAllText(Path.Combine(modDir, "main.lua"), MainLua(safe, tpl.Id));
        File.WriteAllText(Path.Combine(modDir, "README.md"), Readme(safe, tpl));
        File.WriteAllText(Path.Combine(modDir, "hooks", "snd.lua"), "-- " + safe + ": hook snd_play\nreturn nil\n");
        File.WriteAllText(Path.Combine(modDir, "hooks", "text.lua"), "-- " + safe + ": hook scr_text\nreturn nil\n");
        File.WriteAllText(Path.Combine(modDir, "hooks", "skin.lua"), "-- " + safe + ": hook draw_sprite\nreturn nil\n");

        WriteVsCodeSupport(root);

        Console.WriteLine(L("===== mod 骨架已生成 ====="));
        Console.WriteLine(L("  模板: ") + tpl.Name);
        Console.WriteLine(L("  目录: ") + modDir);
        Console.WriteLine();
        Console.WriteLine(L("  下一步: 编辑 main.lua -> 关游戏 -> 运行 Neutraled\\launch.ps1"));
        Console.WriteLine(L("  可用模板: ") + string.Join(", ", All.Select(t => t.Id)));
        return 0;
    }

    private static string MainLua(string name, string tpl)
    {
        var L = new List<string>();
        if (tpl == "dialogue")
        {
            L.Add("-- " + name + " -- 对话 mod");
            L.Add("print(\"[" + name + "] 对话 mod 已加载\")");
            L.Add("");
            L.Add("function say(who, text)");
            L.Add("    local line = who .. \": \" .. text");
            L.Add("    ntl_console_log(\"[对话] \" .. line)");
            L.Add("    print(\"[对话] \" .. line)");
            L.Add("    set_global(\"ntl_say_text\", line)");
            L.Add("    set_global(\"ntl_say_frames\", 300)");
            L.Add("end");
            L.Add("");
            L.Add("say(\"Kris\", \"这是我用 Neutraled 写的对话。\")");
            L.Add("say(\"Susie\", \"看起来能跑。\")");
        }
        else if (tpl == "battle")
        {
            L.Add("-- " + name + " -- 战斗 mod");
            L.Add("print(\"[" + name + "] 战斗 mod 已加载\")");
            L.Add("");
            L.Add("local Snowman, super = Class(EnemyBattler)");
            L.Add("function Snowman:init()");
            L.Add("    super.init(self)");
            L.Add("    self.name = \"雪人\"");
            L.Add("    self.max_health = 500");
            L.Add("    self.health = 500");
            L.Add("    self.attack = 12");
            L.Add("end");
            L.Add("");
            L.Add("local e = Snowman()");
            L.Add("print(\"  敌人 \" .. e.name .. \" HP=\" .. e.health)");
        }
        else if (tpl == "map")
        {
            L.Add("-- " + name + " -- 地图 mod");
            L.Add("print(\"[" + name + "] 地图 mod 已加载\")");
            L.Add("");
            L.Add("local ok = pcall(function() ntl_player_test(\"before_palace\") end)");
            L.Add("print(\"  地图加载: \" .. tostring(ok))");
            L.Add("print(\"  方向键移动 / E 交互 / F2 控制台\")");
        }
        else if (tpl == "skin")
        {
            L.Add("-- " + name + " -- 换皮 mod");
            L.Add("print(\"[" + name + "] 换皮 mod 已加载\")");
            L.Add("");
            L.Add("-- 把 PNG 放到 mod 目录，然后运行时替换精灵");
            L.Add("-- ntl_sprite_replace(\"spr_kris_overworld\", __mod_dir .. \"/my_kris.png\")");
        }
        else if (tpl == "interop")
        {
            L.Add("-- " + name + " -- 联动 mod");
            L.Add("print(\"[" + name + "] 联动 mod 已加载\")");
            L.Add("");
            L.Add("-- 1) 导出 API");
            L.Add("function give_item(item, count)");
            L.Add("    print(\"[" + name + "] 给出 \" .. item .. \" x\" .. count)");
            L.Add("    return true");
            L.Add("end");
            L.Add("ntl_mod_export(\"give_item\", give_item)");
            L.Add("ntl_mod_export(\"VERSION\", \"1.0.0\")");
            L.Add("");
            L.Add("-- 2) 广播事件");
            L.Add("ntl_mod_emit(\"" + name + ":ready\", { version = \"1.0.0\" })");
            L.Add("");
            L.Add("-- 3) 共享总线");
            L.Add("ntl_shared_set(\"" + name + ":state\", \"running\")");
            L.Add("");
            L.Add("-- 4) 调用别的 mod");
            L.Add("local other = ntl_mod_require(\"other.mod.id\")");
            L.Add("if other ~= nil then");
            L.Add("    print(\"  找到联动目标\")");
            L.Add("end");
            L.Add("");
            L.Add("-- 5) 监听事件");
            L.Add("ntl_mod_on(\"other.mod:event\", function(data)");
            L.Add("    print(\"  收到: \" .. tostring(data))");
            L.Add("end)");
        }
        else
        {
            L.Add("-- " + name + " -- 入口脚本");
            L.Add("print(\"[" + name + "] 你好，Neutraled！\")");
            L.Add("print(\"  进游戏后按 F2 打开控制台，输入 help\")");
        }
        return string.Join("\n", L) + "\n";
    }

    private static string Readme(string name, Template tpl)
    {
        var L = new List<string>();
        L.Add("# " + name);
        L.Add("");
        L.Add("模板：" + tpl.Name + " -- " + tpl.Desc);
        L.Add("");
        L.Add("## 目录");
        L.Add("");
        L.Add("- mod.json  元数据 + 事件声明");
        L.Add("- main.lua  入口脚本（改完重启游戏即生效）");
        L.Add("- hooks/    函数 Hook");
        L.Add("- raw/      完全替换某段代码（文件名 = 代码块名）");
        L.Add("");
        L.Add("## 运行");
        L.Add("");
        L.Add("    Get-Process DELTARUNE -EA 0 | Stop-Process -Force");
        L.Add("    E:\\steam\\steamapps\\common\\DELTARUNE\\Neutraled\\launch.ps1");
        L.Add("");
        L.Add("## 调试（游戏内按 F2）");
        L.Add("");
        L.Add("    help / eval 1+2 / vars / profile / mods / api snd");
        L.Add("");
        L.Add("## mod 联动");
        L.Add("");
        L.Add("    ntl_mod_export(\"my_func\", my_func)          导出给别人用");
        L.Add("    local o = ntl_mod_require(\"other.mod\")       调用别人");
        L.Add("    ntl_mod_emit(\"myevent\", { a = 1 })          广播事件");
        L.Add("    ntl_mod_on(\"myevent\", function(d) end)      监听事件");
        L.Add("    ntl_shared_set(\"key\", 123)                  共享状态");
        return string.Join("\n", L) + "\n";
    }

    public static void WriteVsCodeSupport(string ntlRoot)
    {
        try
        {
            var vsc = Path.Combine(ntlRoot, ".vscode");
            Directory.CreateDirectory(vsc);

            var tasks = new List<string>();
            tasks.Add("{");
            tasks.Add("  \"version\": \"2.0.0\",");
            tasks.Add("  \"tasks\": [");
            tasks.Add("    { \"label\": \"Neutraled: 部署并启动\", \"type\": \"shell\", \"command\": \"${workspaceFolder}/launch.ps1\" },");
            tasks.Add("    { \"label\": \"Neutraled: 静态检查\", \"type\": \"shell\", \"command\": \"${workspaceFolder}/builder/bin/Release/net9.0/ntl-builder.exe\", \"args\": [\"--lint\"] },");
            tasks.Add("    { \"label\": \"Neutraled: 自检\", \"type\": \"shell\", \"command\": \"${workspaceFolder}/builder/bin/Release/net9.0/ntl-builder.exe\", \"args\": [\"--selftest\", \"--no-launch\"] },");
            tasks.Add("    { \"label\": \"Neutraled: 可用模板\", \"type\": \"shell\", \"command\": \"${workspaceFolder}/builder/bin/Release/net9.0/ntl-builder.exe\", \"args\": [\"--templates\"] }");
            tasks.Add("  ]");
            tasks.Add("}");
            File.WriteAllText(Path.Combine(vsc, "tasks.json"), string.Join("\n", tasks));

            var globals = new[]
            {
                "Kristal", "Game", "Registry", "Assets", "Mod", "Music", "Input", "Camera", "Draw", "love",
                "Class", "Object", "Sprite", "Text", "Actor", "Battler", "EnemyBattler", "PartyBattler",
                "Bullet", "Arena", "Stage", "World", "Cutscene", "Wave", "Encounter", "Shop",
                "arg", "event_arg", "args", "self",
                "ntl_mod_require", "ntl_mod_export", "ntl_mod_emit", "ntl_mod_on", "ntl_mod_list",
                "ntl_shared_set", "ntl_shared_get",
                "ntl_inst_create", "ntl_inst_destroy", "ntl_inst_set", "ntl_inst_get", "ntl_inst_find", "ntl_inst_all_objs",
                "ntl_sprite_replace", "ntl_sprite_from_file", "ntl_loop_hook",
                "ntl_player_test", "ntl_map_load", "ntl_obj_spawn", "ntl_console_log", "ntl_goto_chapter",
                "get_global", "set_global", "require_lua", "file_read", "file_write"
            };
            var setL = new List<string>();
            setL.Add("{");
            setL.Add("  \"Lua.runtime.version\": \"Lua 5.1\",");
            setL.Add("  \"Lua.diagnostics.globals\": [");
            for (int i = 0; i < globals.Length; i++)
                setL.Add("    \"" + globals[i] + "\"" + (i < globals.Length - 1 ? "," : ""));
            setL.Add("  ],");
            setL.Add("  \"files.associations\": { \"*.ntl\": \"javascript\" }");
            setL.Add("}");
            File.WriteAllText(Path.Combine(vsc, "settings.json"), string.Join("\n", setL));

            File.WriteAllText(Path.Combine(ntlRoot, "NeutraledApi.lua"), ApiStub());
        }
        catch { }
    }

    private static string ApiStub()
    {
        var L = new List<string>();
        L.Add("--- Neutraled API 类型定义（供编辑器补全，不参与运行）");
        L.Add("");
        L.Add("--- 取得另一个 mod 的导出表（跨 mod 调用）");
        L.Add("--- @param modId string 目标 mod 的 id");
        L.Add("--- @return table|nil");
        L.Add("function ntl_mod_require(modId) end");
        L.Add("");
        L.Add("--- 导出函数/常量给其他 mod 使用");
        L.Add("function ntl_mod_export(name, value) end");
        L.Add("");
        L.Add("--- 广播跨 mod 事件");
        L.Add("function ntl_mod_emit(eventName, data) end");
        L.Add("");
        L.Add("--- 监听跨 mod 事件");
        L.Add("function ntl_mod_on(eventName, fn) end");
        L.Add("");
        L.Add("--- 列出所有可联动的 mod");
        L.Add("function ntl_mod_list() end");
        L.Add("");
        L.Add("--- 跨 mod 共享状态（写 / 读）");
        L.Add("function ntl_shared_set(key, value) end");
        L.Add("function ntl_shared_get(key) end");
        L.Add("");
        L.Add("--- 运行时创建任意对象的实例");
        L.Add("function ntl_inst_create(objName, x, y, depth) end");
        L.Add("");
        L.Add("--- 销毁任意实例");
        L.Add("function ntl_inst_destroy(inst) end");
        L.Add("");
        L.Add("--- 设置/读取任意实例的任意变量（完全支配）");
        L.Add("function ntl_inst_set(inst, varName, value) end");
        L.Add("function ntl_inst_get(inst, varName) end");
        L.Add("");
        L.Add("--- 找对象的所有实例 / 列出房间所有对象名");
        L.Add("function ntl_inst_find(objName) end");
        L.Add("function ntl_inst_all_objs() end");
        L.Add("");
        L.Add("--- 运行时替换精灵 / 从 PNG 创建精灵");
        L.Add("function ntl_sprite_replace(targetSpriteName, pngPath) end");
        L.Add("function ntl_sprite_from_file(path) end");
        L.Add("");
        L.Add("--- 接管主循环（phase: step / draw）");
        L.Add("function ntl_loop_hook(phase, handler) end");
        L.Add("");
        L.Add("--- 载入 Kristal 地图并放置玩家");
        L.Add("function ntl_player_test(mapId) end");
        L.Add("");
        L.Add("--- 写一行到游戏内控制台");
        L.Add("function ntl_console_log(text) end");
        return string.Join("\n", L) + "\n";
    }
}
