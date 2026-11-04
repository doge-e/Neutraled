
using System.Text;

namespace NtlMapEdit;

public sealed class GenerateResult
{
    public string ModDir { get; set; } = "";
    public int GmlFiles { get; set; }
    public int ObjectFiles { get; set; }
    public int DataFiles { get; set; }
}

/// <summary>把工程生成成一个可直接放进 Neutraled/mods 的 mod 目录。</summary>
public static class BattleGenerator
{
    /// <summary>生成器为该 mod 建的自定义敌人对象名（配置里可直接引用，任何章节都存在）。</summary>
    public static string EnemyObjectName(string modId) => "ntlb_" + Safe(modId) + "_enemy";


    public static GenerateResult Generate(BattleProject project, string modsDir, GameData? assets = null)
    {
        project.Normalize();
        var file = project.File;
        var modId = file.Mod.Id.Trim();
        var author = string.IsNullOrWhiteSpace(file.Mod.Author) ? "Neutraled" : file.Mod.Author.Trim();
        var leaf = file.Mod.Leaf.Trim();
        var safe = Safe(modId);
        var prefix = "ntlb_" + safe;
        var enemyObj = prefix + "_enemy";
        var bulletObj = prefix + "_bullet";
        var owObj = prefix + "_ow";
        var textObj = prefix + "_text";

        var modDir = Path.Combine(modsDir, modId, author, leaf);
        var gmlDir = Path.Combine(modDir, "gml");
        var objDir = Path.Combine(modDir, "objects");
        var filesDir = Path.Combine(modDir, "files", "Neutraled", "ntl_battle");
        Directory.CreateDirectory(gmlDir);
        Directory.CreateDirectory(objDir);
        Directory.CreateDirectory(filesDir);
        foreach (var f in Directory.GetFiles(gmlDir, "ntlb_*.gml")) File.Delete(f);
        foreach (var f in Directory.GetFiles(objDir, "*.gml")) File.Delete(f);

        var result = new GenerateResult { ModDir = modDir };

        // 绑定到地图实体的战斗：触发条件直接取地图上的房间/坐标（用户只在地图上选中实体，不必手填房间）
        foreach (var b in file.Battles)
        {
            if (string.IsNullOrWhiteSpace(b.Trigger.Room)) b.Trigger.Room = b.Room;
            if (b.Trigger.X <= 0 && b.Trigger.Y <= 0 && b.InstanceX >= 0 && b.InstanceY >= 0)
            {
                b.Trigger.X = b.InstanceX;
                b.Trigger.Y = b.InstanceY;
            }
        }
        foreach (var d in file.Dialogues)
        {
            d.Trigger ??= new TriggerDef();
            if (string.IsNullOrWhiteSpace(d.Trigger.Room)) d.Trigger.Room = d.Room;
            if (d.Trigger.Radius <= 0) d.Trigger.Radius = 64;
            if (d.Trigger.X <= 0 && d.Trigger.Y <= 0 && d.InstanceX >= 0 && d.InstanceY >= 0)
            {
                d.Trigger.X = d.InstanceX;
                d.Trigger.Y = d.InstanceY;
            }
        }
        foreach (var c in file.Cutscenes)
        {
            c.Trigger ??= new TriggerDef();
            if (string.IsNullOrWhiteSpace(c.Trigger.Room)) c.Trigger.Room = c.Room;
            if (c.Trigger.Radius <= 0) c.Trigger.Radius = 64;
            if (c.Trigger.X <= 0 && c.Trigger.Y <= 0 && c.InstanceX >= 0 && c.InstanceY >= 0)
            {
                c.Trigger.X = c.InstanceX;
                c.Trigger.Y = c.InstanceY;
            }
        }

        // 1) 数据：内嵌一份（不依赖 files 路径），同时写一份人可读的到 files/
        var json = BattleJson.Serialize(file, compact: true);
        var embedded = json.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var dataGml = "/// 由 ntl-battle 生成：战斗配置（内嵌单行 JSON，json_parse 直接读）\n" +
                      "function ntlb_data_json()\n{\n    return \"" + embedded + "\";\n}\n";
        File.WriteAllText(Path.Combine(gmlDir, "ntlb_data.gml"), dataGml, new UTF8Encoding(false));
        result.GmlFiles++;

        // 1.5) 原版文本改写：按 lang key 覆盖，不改 data.win。
        var ov = new StringBuilder();
        ov.AppendLine("/// 由 ntl-battle 生成：原版文本改写（覆盖 global.lang_map，不改 data.win）。");
        ov.AppendLine("/// 每一条对应编辑器里「编辑本房间原版对话」保存的文本；返回覆盖条数。");
        ov.AppendLine("function ntlb_apply_text_overrides()");
        ov.AppendLine("{");
        if (file.TextOverrides.Count == 0)
            ov.AppendLine("    // 没有配置原版文本改写。");
        foreach (var t in file.TextOverrides)
            ov.AppendLine("    ntl_set_lang_string(" + JsonStr(t.Key) + ", " + JsonStr(t.Text.TrimEnd('\r', '\n')) + ");");
        ov.AppendLine("    return " + file.TextOverrides.Count + ";");
        ov.AppendLine("}");
        File.WriteAllText(Path.Combine(gmlDir, "ntlb_apply_text_overrides.gml"), ov.ToString(), new UTF8Encoding(false));
        result.GmlFiles++;

        // 2) 运行时 GML
        foreach (var kv in RuntimeTemplates.Files)
        {
            var text = kv.Value
                .Replace("@@ENEMY_OBJ@@", enemyObj)
                .Replace("@@BULLET_OBJ@@", bulletObj)
                .Replace("@@OW_OBJ@@", owObj)
                .Replace("@@TEXT_OBJ@@", textObj)
                .Replace("@@PREFIX@@", prefix);
            File.WriteAllText(Path.Combine(gmlDir, kv.Key + ".gml"), text, new UTF8Encoding(false));
            result.GmlFiles++;
        }

        // 2.5) 每个 mod 设置项一个面板动作脚本（ntl_menu_add 的 action 按脚本名查找，文件名必须等于脚本名）。
        for (int i = 0; i < file.Settings.Count; i++)
        {
            var actName = prefix + "_setting_" + i.ToString();
            var body = "/// Mod 设置项 #" + i + " 的面板动作：由 ntl_menu_add 注册，玩家按 Z/Enter 时执行。\n" +
                       "ntlb_setting_cycle(" + i.ToString() + ");\n";
            File.WriteAllText(Path.Combine(gmlDir, actName + ".gml"), body, new UTF8Encoding(false));
            result.GmlFiles++;
        }

        // 3) 入口
        var main = new StringBuilder();
        main.AppendLine("/// 由 ntl-battle 生成的战斗 mod 入口：Neutraled 在启动时执行一次。");
        main.AppendLine("/// 配置存在 gml/ntlb_data.gml（内嵌 JSON），运行时用 json_parse 读成 struct。");
        main.AppendLine("ntlb_init();");
        main.AppendLine("var _ntlb_text_n = ntlb_apply_text_overrides();");
        main.AppendLine("if (_ntlb_text_n > 0) ntl_log(\"ntlb\", \"[文本] 原版文本改写 \" + string(_ntlb_text_n) + \" 条（lang key 覆盖）\");");
        File.WriteAllText(Path.Combine(gmlDir, "main.gml"), main.ToString(), new UTF8Encoding(false));

        // 4) 对象事件
        var ev = new List<(string path, string body)>
        {
            ("objects/gml_Object_" + enemyObj + "_Create_0.gml", "ntlb_enemy_create();\n"),
            ("objects/gml_Object_" + enemyObj + "_Step_0.gml", "ntlb_enemy_step();\n"),
            ("objects/gml_Object_" + enemyObj + "_Draw_0.gml", "ntlb_enemy_draw();\n"),
            ("objects/gml_Object_" + enemyObj + "_Other_22.gml", "ntlb_monstersetup();\n"),
            ("objects/gml_Object_" + bulletObj + "_Create_0.gml", "ntlb_bullet_create();\n"),
            ("objects/gml_Object_" + bulletObj + "_Step_0.gml", "ntlb_bullet_step();\n"),
            ("objects/gml_Object_" + bulletObj + "_Draw_0.gml", "ntlb_bullet_draw();\n"),
            ("objects/gml_Object_" + owObj + "_Create_0.gml", "ntlb_ow_create();\n"),
            ("objects/gml_Object_" + owObj + "_Step_0.gml", "ntlb_ow_step();\n"),
            ("objects/gml_Object_" + owObj + "_Draw_0.gml", "ntlb_ow_draw();\n"),
            ("objects/gml_Object_" + textObj + "_Create_0.gml", "ntlb_text_create();\n"),
            ("objects/gml_Object_" + textObj + "_Step_0.gml", "ntlb_text_step();\n"),
            ("objects/gml_Object_" + textObj + "_Draw_0.gml", "ntlb_text_draw();\n"),
        };
        foreach (var (path, body) in ev)
        {
            var full = Path.Combine(modDir, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, body, new UTF8Encoding(false));
            result.ObjectFiles++;
        }

        // 5) mod.json
        var modJson = BuildModJson(file, enemyObj, bulletObj, owObj, textObj);
        File.WriteAllText(Path.Combine(modDir, "mod.json"), modJson, new UTF8Encoding(false));

        // 6) files/ 给人看的副本
        File.WriteAllText(Path.Combine(filesDir, "battles.json"), BattleJson.Serialize(file), new UTF8Encoding(false));
        result.DataFiles++;
        if (file.RoomPatches.Count > 0 || file.NewRooms.Count > 0)
        {
            var editorDir = Path.Combine(modDir, "files", "Neutraled", "editor");
            Directory.CreateDirectory(editorDir);
            var roomJson = new StringBuilder();
            roomJson.AppendLine("{");
            roomJson.AppendLine("  \"roomPatches\": " + IndentJson(BattleJson.Serialize(file.RoomPatches), 2) + ",");
            roomJson.AppendLine("  \"rooms\": " + IndentJson(BattleJson.Serialize(file.NewRooms), 2));
            roomJson.AppendLine("}");
            File.WriteAllText(Path.Combine(editorDir, "roomPatches.json"), roomJson.ToString(), new UTF8Encoding(false));
            result.DataFiles++;
        }

        // 7) README
        File.WriteAllText(Path.Combine(modDir, "README.md"), BuildReadme(file, modId, prefix), new UTF8Encoding(false));
        return result;
    }

    private static string Safe(string id)
    {
        var sb = new StringBuilder();
        foreach (var ch in id.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (ch == '_' || ch == '-') sb.Append('_');
        }
        var s = sb.ToString().Trim('_');
        while (s.StartsWith("_")) s = s[1..];
        if (s.Length == 0) s = "battle";
        if (s.Length > 24) s = s[..24];
        return s;
    }

    private static string JsonStr(string s) =>
        "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"";

    private static string BuildModJson(BattlesFile file, string enemyObj, string bulletObj, string owObj, string textObj)
    {
        var mod = file.Mod;
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"id\": " + JsonStr(mod.Id) + ",");
        sb.AppendLine("  \"name\": " + JsonStr(mod.Name) + ",");
        sb.AppendLine("  \"author\": " + JsonStr(string.IsNullOrWhiteSpace(mod.Author) ? "Neutraled" : mod.Author) + ",");
        sb.AppendLine("  \"version\": " + JsonStr(string.IsNullOrWhiteSpace(mod.Version) ? "1.0.0" : mod.Version) + ",");
        sb.AppendLine("  \"enabled\": " + (mod.Enabled ? "true" : "false") + ",");
        sb.AppendLine("  \"description\": " + JsonStr(string.IsNullOrWhiteSpace(mod.Description) ? "由 ntl-battle 生成的战斗 mod" : mod.Description) + ",");
        // 入口脚本名：gml/main.gml 由注入器的 ScriptUnwrap 拆成脚本 main，显式声明避免依赖隐式判定。
        sb.AppendLine("  \"main\": \"main\",");
        sb.AppendLine("  \"objects\": [");
        AppendObject(sb, enemyObj, new[]
        {
            ("Create", 0, "objects/gml_Object_" + enemyObj + "_Create_0.gml"),
            ("Step", 0, "objects/gml_Object_" + enemyObj + "_Step_0.gml"),
            ("Draw", 0, "objects/gml_Object_" + enemyObj + "_Draw_0.gml"),
            ("Other", 22, "objects/gml_Object_" + enemyObj + "_Other_22.gml"),
        });
        sb.AppendLine("    ,");
        AppendObject(sb, bulletObj, new[]
        {
            ("Create", 0, "objects/gml_Object_" + bulletObj + "_Create_0.gml"),
            ("Step", 0, "objects/gml_Object_" + bulletObj + "_Step_0.gml"),
            ("Draw", 0, "objects/gml_Object_" + bulletObj + "_Draw_0.gml"),
        });
        sb.AppendLine("    ,");
        AppendObject(sb, textObj, new[]
        {
            ("Create", 0, "objects/gml_Object_" + textObj + "_Create_0.gml"),
            ("Step", 0, "objects/gml_Object_" + textObj + "_Step_0.gml"),
            ("Draw", 0, "objects/gml_Object_" + textObj + "_Draw_0.gml"),
        });
        sb.AppendLine("    ,");
        AppendObject(sb, owObj, new[]
        {
            ("Create", 0, "objects/gml_Object_" + owObj + "_Create_0.gml"),
            ("Step", 0, "objects/gml_Object_" + owObj + "_Step_0.gml"),
            ("Draw", 0, "objects/gml_Object_" + owObj + "_Draw_0.gml"),
        });
        sb.Append("  ]");
        // 房间差分 / 新增房间：由部署器（builder/RoomPatches.cs）从 mod.json 读取并注入产物。
        // 只在有内容时输出这两个键，保持旧的 mod.json 形状不变。
        var extra = new List<string>();
        if (file.RoomPatches.Count > 0)
            extra.Add("  \"roomPatches\": " + IndentJson(BattleJson.Serialize(file.RoomPatches), 2));
        if (file.NewRooms.Count > 0)
            extra.Add("  \"rooms\": " + IndentJson(BattleJson.Serialize(file.NewRooms), 2));
        if (extra.Count == 0)
        {
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine(",");
            for (int i = 0; i < extra.Count; i++) sb.AppendLine(extra[i] + (i == extra.Count - 1 ? "" : ","));
        }
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>把一段 JSON 按行缩进，用于嵌进手写的 mod.json。</summary>
    private static string IndentJson(string json, int spaces)
    {
        var pad = new string(' ', spaces);
        var lines = (json ?? "").Replace("\r\n", "\n").Split('\n');
        return string.Join("\n", lines.Select(l => pad + l));
    }

    private static void AppendObject(StringBuilder sb, string name, (string type, int subtype, string file)[] events)
    {
        sb.AppendLine("    {");
        sb.AppendLine("      \"name\": " + JsonStr(name) + ",");
        sb.AppendLine("      \"events\": [");
        for (int i = 0; i < events.Length; i++)
        {
            var (type, subtype, file) = events[i];
            sb.Append("        { \"type\": " + JsonStr(type) + ", \"subtype\": " + subtype + ", \"file\": " + JsonStr(file) + " }");
            sb.AppendLine(i == events.Length - 1 ? "" : ",");
        }
        sb.AppendLine("      ],");
        sb.AppendLine("      \"visible\": true,");
        sb.AppendLine("      \"solid\": false,");
        sb.AppendLine("      \"persistent\": false,");
        sb.AppendLine("      \"depth\": -100");
        sb.Append("    }");
    }

    private static string BuildReadme(BattlesFile file, string modId, string prefix)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# " + file.Mod.Name);
        sb.AppendLine();
        sb.AppendLine("由 ntl-battle（Neutraled 战斗编辑器）生成。");
        sb.AppendLine();
        sb.AppendLine("- mod id: " + modId);
        sb.AppendLine("- 部署叶子: " + file.Mod.Leaf);
        sb.AppendLine("- 战斗配置: gml/ntlb_data.gml（内嵌 JSON，运行时 json_parse 读取），另有一份可读副本在 files/Neutraled/ntl_battle/battles.json");
        sb.AppendLine("- 生成对象: " + prefix + "_enemy / " + prefix + "_bullet / " + prefix + "_ow / " + prefix + "_text（屏幕文本提示）");
        if (file.RoomPatches.Count > 0 || file.NewRooms.Count > 0)
            sb.AppendLine("- 地图/房间改动: mod.json 的 roomPatches / rooms 段（另有一份可读副本在 files/Neutraled/editor/roomPatches.json），由部署器注入产物");
        sb.AppendLine();
        sb.AppendLine("## 用法");
        sb.AppendLine();
        sb.AppendLine("1. 把本目录（含 mod.json 的这层）放进 Neutraled/mods/<mod>/<author>/<leaf>/。");
        sb.AppendLine("2. ntl-builder.exe --deploy-all --force（或 --launch <leaf>）部署。");
        sb.AppendLine("3. 游戏里按 F2 打开控制台：");
        sb.AppendLine("   - battle list / battle start <id> / battle ow <id> / battle info <id> / battle stop");
        sb.AppendLine("   - dialogue list / dialogue start <id> / dialogue stop");
        sb.AppendLine("   - cutscene list / cutscene start <id> / cutscene stop");
        sb.AppendLine("   - setting list / setting set <id> <值> / setting reset <id>");
        sb.AppendLine("4. 也可以给战斗/对话/剧情配 Trigger.room / requiredFlag / radius，让它在进入房间时自动触发。");
        sb.AppendLine();
        if (file.RoomPatches.Count > 0 || file.NewRooms.Count > 0)
        {
            sb.AppendLine("## 地图/房间改动（mod.json 的 roomPatches / rooms 段）");
            sb.AppendLine();
            sb.AppendLine("只记录相对原版 data.win 的差异；部署时由 builder 注入每个目标产物，原版 backup 与编辑器打开的 data.win 都不被改写。");
            sb.AppendLine();
            foreach (var rp in file.RoomPatches)
            {
                var bits = new List<string>();
                if (rp.InstancesAdd.Count > 0) bits.Add("新增实例 " + rp.InstancesAdd.Count);
                if (rp.InstancesModify.Count > 0) bits.Add("修改实例 " + rp.InstancesModify.Count);
                if (rp.InstancesRemove.Count > 0) bits.Add("删除实例 " + rp.InstancesRemove.Count);
                if (rp.TilesSet.Count > 0) bits.Add("瓦片写入 " + rp.TilesSet.Sum(t => t.Cells.Count) + " 格");
                if (rp.Width != null) bits.Add("宽 " + rp.Width);
                if (rp.Height != null) bits.Add("高 " + rp.Height);
                if (rp.Speed != null) bits.Add("speed " + rp.Speed);
                if (rp.Persistent != null) bits.Add("persistent " + (rp.Persistent.Value ? "1" : "0"));
                if (rp.Flags != null) bits.Add("flags " + rp.Flags);
                if (rp.BackgroundColor != null) bits.Add("背景色 " + rp.BackgroundColor);
                if (rp.DrawBackgroundColor != null) bits.Add("绘制背景色 " + (rp.DrawBackgroundColor.Value ? "1" : "0"));
                sb.AppendLine("- " + rp.Name + " —— " + string.Join("，", bits));
            }
            foreach (var nr in file.NewRooms)
                sb.AppendLine("- 新房间 " + nr.Name + "（" + nr.Width + "x" + nr.Height + "，空房间）");
            sb.AppendLine();
        }
        sb.AppendLine("## 战斗列表");
        sb.AppendLine();
        foreach (var b in file.Battles)
        {
            sb.AppendLine("- " + b.Id + "（" + b.Kind + "）" + (string.IsNullOrWhiteSpace(b.Name) ? "" : " " + b.Name) +
                          " —— 敌人 " + b.Enemies.Count + "，回合 " + b.Turns.Count);
        }
        sb.AppendLine();
        sb.AppendLine("## 纯对话");
        sb.AppendLine();
        sb.AppendLine("绑定到地图实体的纯对话：玩家靠近（trigger.radius）并按原版交互键（Z/Enter）时播放；"
                      + "运行时填原版 global.msg 并创建 obj_dialoguer，每行末尾自动补 % / 最后一行 /%。");
        sb.AppendLine();
        foreach (var d in file.Dialogues)
        {
            sb.AppendLine("- " + d.Id + "（房间 " + (string.IsNullOrWhiteSpace(d.Room) ? "未绑定" : d.Room) + "）"
                          + " —— " + d.Lines.Count + " 行" + (string.IsNullOrWhiteSpace(d.Name) ? "" : " " + d.Name));
        }
        sb.AppendLine();
        sb.AppendLine("## 简单剧情（cutscene）");
        sb.AppendLine();
        sb.AppendLine("绑定到地图实体：mode=interact 时玩家靠近按 Z 触发，mode=area 时走进触发半径自动播放。"
                      + "步骤按顺序执行，可让玩家/触发实体移动（move）、转向（face）、换动作（action）、播放对话（dialogue）、"
                      + "设 flag（flag）、写日志（message），最后执行 end effects。");
        sb.AppendLine();
        foreach (var c in file.Cutscenes)
        {
            sb.AppendLine("- " + c.Id + "（" + c.Mode + "，房间 " + (string.IsNullOrWhiteSpace(c.Room) ? "未绑定" : c.Room) + "）"
                          + " —— " + c.Steps.Count + " 步" + (string.IsNullOrWhiteSpace(c.Name) ? "" : " " + c.Name));
        }
        sb.AppendLine();
        sb.AppendLine("## Mod 设置项（游戏内 Neutraled「Mod 设置」面板）");
        sb.AppendLine();
        sb.AppendLine("每项注册一行到游戏内 Mod 设置面板（ntl_menu_add），开关立即写 ntl_mod_data_set 持久化，"
                      + "并实时影响生成 mod 的功能（通过 feature 绑定）。");
        sb.AppendLine();
        foreach (var s in file.Settings)
        {
            sb.AppendLine("- " + s.Id + "（" + s.Type + "，默认 " + s.Value + "）"
                          + (s.Feature == "none" ? "" : " → feature=" + s.Feature)
                          + (string.IsNullOrWhiteSpace(s.Label) ? "" : " " + s.Label));
        }
        sb.AppendLine();
        sb.AppendLine("## 原版对话/剧情文本改写（不动 data.win）");
        sb.AppendLine();
        sb.AppendLine("gml/ntlb_apply_text_overrides.gml 在启动时逐条调用 ntl_set_lang_string(key, 文本)，"
                      + "直接覆盖 global.lang_map —— 原版台词/剧情文字随之变化，无需重打包 data.win。"
                      + "共 " + file.TextOverrides.Count + " 条：");
        foreach (var t in file.TextOverrides)
            sb.AppendLine("- " + t.Key + (string.IsNullOrWhiteSpace(t.Room) ? "" : "（房间 " + t.Room + "）"));
        return sb.ToString();
    }
}
