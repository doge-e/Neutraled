using System.Text.Json;

namespace NtlMapEdit;

/// <summary>
/// 战斗编辑器的离线自检（CLI：ntl-mapedit battle-test [data.win] [outDir]）。
/// 覆盖：配置校验 -> sidecar 往返 -> 生成 mod -> 产物结构断言。
/// 不启动游戏、不碰游戏根，只在临时目录里生成。
/// </summary>
public static class BattleSelfTest
{
    private static int _pass;
    private static int _fail;

    public static int Run(string? dataWin, string? outDir)
    {
        _pass = 0;
        _fail = 0;
        GameData? gd = null;
        try
        {
            if (dataWin != null && File.Exists(dataWin))
            {
                Console.WriteLine("[battle-test] 载入 " + dataWin);
                gd = new GameData(dataWin);
                Console.WriteLine($"[battle-test] rooms={gd.Rooms.Count()} objects={gd.GameObjects.Count()} sprites={gd.Sprites.Count()}");
            }
            else
            {
                Console.WriteLine("[battle-test] 未提供有效 data.win，跳过资源名校验（用 obj_bloxer_enemy / room_krisroom 当默认值）");
            }

            string dir = outDir ?? Path.Combine(Path.GetTempPath(), "ntl-battle-selftest-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            Console.WriteLine("[battle-test] 输出目录 " + dir);

            var project = BuildProject(gd, out string expectedRoom);
            var issues = project.Validate(gd);
            Check(issues.All(i => !i.IsError), "validate: 0 errors" + DescribeIssues(issues));

            var bad = BuildProject(gd, out _);
            bad.File.Battles[0].Enemies[0].Object = "obj_definitely_not_in_data_win";
            Check(bad.Validate(gd).Any(i => i.IsError && i.Message.Contains("data.win 里没有对象")),
                "validate: 未知敌人对象会报错");

            var badChoice = BuildProject(gd, out _);
            badChoice.File.Settings.Add(new SettingDef { Id = "bad.choice", Type = "choice", Options = new() });
            Check(badChoice.Validate(gd).Any(i => i.IsError && i.Message.Contains("choice 类型至少需要 2 个选项")),
                "validate: choice 缺选项会报错");
            Check(project.File.Dialogues.Count == 1 && project.File.Settings.Count == 4,
                "validate: 自检工程带 1 段对话 + 4 个设置项");
            Check(project.File.Cutscenes.Count == 1 && project.File.Cutscenes[0].Steps.Count == 8,
                "validate: 自检工程带 1 段剧情（8 步，含 sound/shake/waitinput）");

            // 1) sidecar 往返
            string winPath = Path.Combine(dir, "ch1-edit.win");
            var sidecar = BattleProject.SidecarFor(winPath);
            project.Save(sidecar);
            Check(File.Exists(sidecar), "sidecar: 文件已写出 " + sidecar);
            var reloaded = BattleProject.Load(sidecar);
            Check(reloaded.File.Battles.Count == project.File.Battles.Count, "sidecar: 场次数一致");
            Check(reloaded.ForInstance(expectedRoom, project.File.Battles[0].InstanceId) != null, "sidecar: 按房间+实例能找回战斗");
            Check(reloaded.File.Battles[0].Turns[0].Barrages[0].RandomTypes.Count == 2, "sidecar: 随机弹幕 type 列表保留");
            Check(reloaded.File.Dialogues.Count == 1, "sidecar: 对话配置持久化");
            Check(reloaded.File.Settings.Count == 4, "sidecar: 设置项持久化");
            Check(reloaded.File.Cutscenes.Count == 1 && reloaded.File.Cutscenes[0].Steps.Count == 8,
                "sidecar: 剧情步骤持久化（8 步）");
            Check(reloaded.File.Cutscenes[0].Steps.Any(s => s.Kind == "sound" && s.Sound == "snd_text" && Math.Abs(s.Gain - 0.5) < 0.001),
                "sidecar: 剧情音效步骤保留 sound/gain");
            Check(reloaded.File.Cutscenes[0].Steps.Any(s => s.Kind == "shake" && s.Frames == 20),
                "sidecar: 剧情震动步骤保留帧数");
            Check(reloaded.File.Cutscenes[0].Steps.Any(s => s.Kind == "waitinput"),
                "sidecar: 剧情等待输入步骤保留");
            Check(reloaded.File.Battles[0].TriggerMode == "interact" && reloaded.File.Battles[0].Trigger.PersistOnce,
                "sidecar: 战斗触发方式/跨重启保留");
            Check(reloaded.File.Dialogues[0].Mode == "area" && reloaded.File.Dialogues[0].Trigger.PersistOnce,
                "sidecar: 对话 area 触发/跨重启保留");
            var reloadedChoice = reloaded.File.Settings.FirstOrDefault(s => s.Type == "choice");
            Check(reloadedChoice != null && reloadedChoice.Options.Count == 2, "sidecar: choice 选项保留");

            // 2) 生成 mod
            string modsDir = Path.Combine(dir, "mods");
            var result = BattleGenerator.Generate(project, modsDir, gd);
            Check(result.ModDir.StartsWith(modsDir, StringComparison.OrdinalIgnoreCase), "generate: mod 落在 mods 目录");
            Check(Directory.Exists(result.ModDir), "generate: mod 目录存在");
            Check(result.GmlFiles >= 30, "generate: GML 文件数 " + result.GmlFiles + " >= 30");
            Check(result.ObjectFiles == 13, "generate: 对象事件 13 个（含文本对象），实际 " + result.ObjectFiles);
            Check(result.DataFiles >= 1, "generate: 数据文件 " + result.DataFiles);

            string gmlDir = Path.Combine(result.ModDir, "gml");
            Check(Directory.Exists(gmlDir), "generate: gml/ 存在");
            var gmlFiles = Directory.GetFiles(gmlDir, "*.gml").Select(Path.GetFileName).ToList();
            Check(gmlFiles.Contains("main.gml"), "generate: main.gml");
            Check(gmlFiles.Contains("ntlb_data.gml"), "generate: ntlb_data.gml");
            Check(gmlFiles.Contains("ntlb_init.gml"), "generate: ntlb_init.gml");
            Check(gmlFiles.Contains("ntlb_frame.gml"), "generate: ntlb_frame.gml");
            Check(gmlFiles.Contains("ntlb_settings_init.gml"), "generate: ntlb_settings_init.gml");
            Check(gmlFiles.Contains("ntlb_cmd_setting.gml"), "generate: ntlb_cmd_setting.gml");
            Check(gmlFiles.Contains("ntlb_feature.gml"), "generate: ntlb_feature.gml");
            Check(gmlFiles.Contains("ntlb_dialogues_init.gml"), "generate: ntlb_dialogues_init.gml");
            Check(gmlFiles.Contains("ntlb_start_dialogue.gml"), "generate: ntlb_start_dialogue.gml");
            Check(gmlFiles.Contains("ntlb_cmd_dialogue.gml"), "generate: ntlb_cmd_dialogue.gml");
            Check(gmlFiles.Contains("ntlb_cutscenes_init.gml"), "generate: ntlb_cutscenes_init.gml");
            Check(gmlFiles.Contains("ntlb_start_cutscene.gml"), "generate: ntlb_start_cutscene.gml");
            Check(gmlFiles.Contains("ntlb_cutscene_tick.gml"), "generate: ntlb_cutscene_tick.gml");
            Check(gmlFiles.Contains("ntlb_cmd_cutscene.gml"), "generate: ntlb_cmd_cutscene.gml");
            Check(gmlFiles.Contains("ntlb_once.gml"), "generate: ntlb_once.gml（跨重启一次性）");
            Check(gmlFiles.Contains("ntlb_in_interact_zone.gml"), "generate: ntlb_in_interact_zone.gml（原版交互矩形）");
            Check(gmlFiles.Contains("ntlb_show_text.gml"), "generate: ntlb_show_text.gml（屏幕文本）");
            Check(gmlFiles.Contains("ntlb_text_draw.gml"), "generate: ntlb_text_draw.gml（文本绘制）");
            var settingActions = gmlFiles.Where(f => System.Text.RegularExpressions.Regex.IsMatch(f, @"_setting_\d+\.gml$")).ToList();
            Check(settingActions.Count == project.File.Settings.Count,
                "generate: 每个设置项一个面板动作脚本（" + settingActions.Count + "/" + project.File.Settings.Count + "）");
            Check(File.ReadAllText(Path.Combine(gmlDir, settingActions[0])).Contains("ntlb_setting_cycle(0)"),
                "generate: 面板动作调用 ntlb_setting_cycle(0)");
            Check(Directory.GetFiles(Path.Combine(result.ModDir, "objects"), "*.gml").Length == 13, "generate: objects/ 13 个事件文件");
            string textObjFile = "gml_Object_" + BattleGenerator.EnemyObjectName("battle.selftest").Replace("_enemy", "") + "_text_Create_0.gml";
            Check(File.Exists(Path.Combine(result.ModDir, "objects", textObjFile)), "generate: 文本对象 Create 事件文件存在 " + textObjFile);

            string dataGml = File.ReadAllText(Path.Combine(gmlDir, "ntlb_data.gml"));
            Check(dataGml.Contains("function ntlb_data_json()"), "generate: 内嵌数据函数存在");
            var embedded = ExtractEmbedded(dataGml);
            var parsed = embedded != null ? BattleJson.Deserialize<BattlesFile>(embedded) : null;
            Check(parsed != null && parsed.Battles.Count == project.File.Battles.Count,
                "generate: 内嵌 JSON 可反解析且场次数一致");
            Check(parsed != null && parsed.Battles.Any(b => b.Turns.Any(t => t.Barrages.Any(g => g.RandomTypes.Count > 0))),
                "generate: 内嵌 JSON 保留随机弹幕");
            Check(parsed != null && parsed.Battles.Any(b => b.Kind == "overworld" && b.Overworld.Patterns.Count >= 3),
                "generate: overworld 三种 pattern 保留");
            Check(parsed != null && parsed.Dialogues.Count == 1 && parsed.Dialogues[0].Lines.Count == 3,
                "generate: 内嵌 JSON 保留对话与 3 行文本");
            Check(parsed != null && parsed.Settings.Count == 4 &&
                  parsed.Settings.Any(s => string.Equals(s.Feature, "battlesEnabled", StringComparison.OrdinalIgnoreCase)),
                "generate: 内嵌 JSON 保留设置项与 feature 绑定");
            Check(parsed != null && parsed.Cutscenes.Count == 1 && parsed.Cutscenes[0].Steps.Count == 8,
                "generate: 内嵌 JSON 保留剧情 8 步");
            Check(parsed != null && parsed.Cutscenes[0].Steps.Any(s => s.Kind == "sound" && !string.IsNullOrEmpty(s.Sound)),
                "generate: 内嵌 JSON 保留音效步骤");
            Check(parsed != null && parsed.Battles.First(b => b.Kind == "encounter").TriggerMode == "interact",
                "generate: 内嵌 JSON 保留战斗触发方式");
            Check(parsed != null && parsed.Dialogues[0].Mode == "area", "generate: 内嵌 JSON 保留对话触发方式");

            string modJsonPath = Path.Combine(result.ModDir, "mod.json");
            Check(File.Exists(modJsonPath), "generate: mod.json");
            using (var doc = JsonDocument.Parse(File.ReadAllText(modJsonPath)))
            {
                var root = doc.RootElement;
                Check(root.TryGetProperty("id", out var idEl) && idEl.GetString() == project.File.Mod.Id, "generate: mod.json id");
                Check(root.TryGetProperty("objects", out var objs) && objs.GetArrayLength() >= 4, "generate: mod.json objects >= 4（含文本对象）");
                Check(root.TryGetProperty("main", out var mainEl) && mainEl.GetString() == "main", "generate: mod.json main=main");
            }

            string shipped = Path.Combine(result.ModDir, "files", "Neutraled", "ntl_battle", "battles.json");
            Check(File.Exists(shipped), "generate: files/ 里也随包一份 battles.json");
            var shippedModel = BattleJson.Deserialize<BattlesFile>(File.ReadAllText(shipped));
            Check(shippedModel != null && shippedModel.Battles.Count == project.File.Battles.Count, "generate: 随包 JSON 场次数一致");

            string readme = Path.Combine(result.ModDir, "README.md");
            Check(File.Exists(readme) && new FileInfo(readme).Length > 100, "generate: README.md 已生成");
            Check(File.ReadAllText(readme).Contains("Mod 设置项") && File.ReadAllText(readme).Contains("纯对话")
                  && File.ReadAllText(readme).Contains("简单剧情"),
                "generate: README 写明对话/设置项/剧情用法");

            // 2b) 运行时模板的两个真机回归修复（防回退；两个坑都表现为「功能静默不生效」）
            string battleTick = File.ReadAllText(Path.Combine(gmlDir, "ntlb_battle_tick.gml"));
            int wipeIdx = battleTick.IndexOf("队伍全灭", StringComparison.Ordinal);
            int ctrlIdx = battleTick.IndexOf("obj_battlecontroller", StringComparison.Ordinal);
            Check(wipeIdx >= 0 && ctrlIdx > wipeIdx,
                "generate: 全灭判定在 battlecontroller 之前（GAME OVER 后 controller/主角同帧销毁也能判负）");
            string frameGml = File.ReadAllText(Path.Combine(gmlDir, "ntlb_frame.gml"));
            int tickIdx = frameGml.IndexOf("ntlb_battle_tick()", StringComparison.Ordinal);
            int mcIdx = frameGml.IndexOf("var _mc = _mc0", StringComparison.Ordinal);
            Check(tickIdx >= 0 && mcIdx > tickIdx,
                "generate: 战斗监督在主角色存在守卫之前（否则全灭后 tick 永远不跑）");
            Check(frameGml.Contains("ntlb_cutscene_tick()") && frameGml.Contains("ntlb_dialogue_tick()"),
                "generate: 剧情/对话 tick 仍在主角色守卫之后");
            Check(File.ReadAllText(Path.Combine(gmlDir, "ntlb_show_text.gml")).Contains("[文本]"),
                "generate: 屏幕文本先写日志再尝试绘制（无文本对象时也能取证）");
            string cutTick = File.ReadAllText(Path.Combine(gmlDir, "ntlb_cutscene_tick.gml"));
            Check(cutTick.Contains("[剧情] 音效") && cutTick.Contains("[剧情] 震动") && cutTick.Contains("等待按键"),
                "generate: 剧情音效/震动/等待按键三个分支齐全");
            Check(project.File.Mod.Leaf == "chapter1" && result.ModDir.Replace('\\','/').EndsWith("/chapter1", StringComparison.Ordinal),
                "generate: 生成目录 = mods/<id>/<author>/<leaf>（leaf=" + project.File.Mod.Leaf + "）");

            // 3) 触发条件从地图实体自动填充
            var generated = parsed?.Battles.FirstOrDefault(b => b.Kind == "encounter");
            Check(generated != null && generated.Room == expectedRoom, "generate: 战斗绑定房间自动带上");
            Check(generated != null && generated.Trigger.Room == expectedRoom, "generate: trigger.room 自动填充");
            Check(generated != null && generated.InstanceId == project.File.Battles[0].InstanceId, "generate: 实例 ID 保留");
            var generatedDialogue = parsed?.Dialogues.FirstOrDefault();
            Check(generatedDialogue != null && generatedDialogue.Room == expectedRoom, "generate: 对话绑定房间自动带上");
            Check(generatedDialogue != null && generatedDialogue.Trigger.Room == expectedRoom, "generate: 对话 trigger.room 自动填充");
            Check(generatedDialogue != null && generatedDialogue.InstanceId == project.File.Dialogues[0].InstanceId, "generate: 对话实例 ID 保留");

            // 4) 清理
            try { Directory.Delete(dir, true); } catch { }
        }
        catch (Exception ex)
        {
            _fail++;
            Console.WriteLine("[battle-test] 异常: " + ex);
        }
        finally
        {
            gd?.Dispose();
        }

        Console.WriteLine($"[battle-test] PASS={_pass} FAIL={_fail}");
        Console.WriteLine(_fail == 0 ? "BATTLE-TEST PASS" : "BATTLE-TEST FAIL");
        return _fail == 0 ? 0 : 1;
    }

    public static BattleProject BuildProject(GameData? gd, out string room)
    {
        var roomName = "room_krisroom";
        uint instanceId = 100001;
        if (gd != null)
        {
            var r = gd.Rooms.FirstOrDefault(x => GameData.Name(x.Name) == roomName) ?? gd.Rooms.First();
            roomName = GameData.Name(r.Name);
            var inst = RoomEdit.AllInstances(r).Select(t => t.Inst).FirstOrDefault();
            if (inst != null) instanceId = inst.InstanceID;
        }

        var project = new BattleProject();
        project.File.Mod = new BattleModInfo
        {
            Id = "battle.selftest",
            Name = "Battle Selftest",
            Author = "Neutraled",
            Version = "1.0.0",
            Leaf = "chapter1",
            Description = "生成器自检用",
        };

        // 用生成器随 mod 建的自定义敌人对象：它在任何章节产物里都存在，且不依赖原版敌人对象的
        // 父事件（章节 2 的 obj_monsterparent.Other_25 会读 recruitable，旧版 dummy 没这个变量）。
        string enemyObj = BattleGenerator.EnemyObjectName("battle.selftest");
        var encounter = new BattleDef
        {
            Id = "selftest_encounter",
            Name = "自检遭遇战",
            Kind = "encounter",
            Room = roomName,
            InstanceId = instanceId,
            ObjectName = "obj_bloxer_enemy",
            InstanceX = 160,
            InstanceY = 120,
            RoundLimit = 3,
            StartingTension = 0,
            Enemies =
            {
                new EnemyDef { Object = enemyObj, Name = "Bloxer", Sprite = "spr_heart", Hp = 80, At = 6, Df = 1, Acts = { "Check", "Talk" } },
                new EnemyDef { Object = enemyObj, Name = "Hathy", Sprite = "spr_heartsmall", Hp = 60 },
            },
            Turns =
            {
                new TurnDef
                {
                    Message = "第一回合",
                    Duration = 240,
                    // 弹幕 type 是「目标章节」原版 obj_dbulletcontroller 的模板编号，跨章节语义不同
                    // （章节2 的 26/27 是 Rouxls 专用攻击，会引用不存在的 obj_rouxls_enemy 而崩溃）。
                    // 自检用各章都存在的通用模板 0（环形）/ 1（上下边缘），保证 ch1/ch2 都能跑。
                    Barrages = { new BarrageDef { Type = 0, Count = 2, Ratio = 1.0, RandomTypes = { 0, 1 } } },
                },
                new TurnDef
                {
                    Message = "第二回合",
                    Duration = 200,
                    Barrages =
                    {
                        new BarrageDef { Type = 1, Count = 1, Ratio = 1.2 },
                        new BarrageDef { Type = 0, Count = 2, Damage = 8 },
                    },
                },
                new TurnDef { Message = "第三回合（随机）", Duration = 180, Barrages = { new BarrageDef { Type = 0, Count = 1, RandomRatio = 0.2 } } },
            },
        };
        encounter.Trigger.Room = roomName;
        encounter.Trigger.Radius = 40;
        encounter.Trigger.PersistOnce = true;
        encounter.TriggerMode = "interact";
        encounter.Victory.SetFlags.Add(900);
        encounter.Victory.Message = "自检胜利";
        project.File.Battles.Add(encounter);

        var overworld = new BattleDef
        {
            Id = "selftest_overworld",
            Name = "自检 overworld 弹幕",
            Kind = "overworld",
            Room = roomName,
            InstanceId = instanceId + 1,
            ObjectName = "obj_testoverworldenemy",
            InstanceX = 320,
            InstanceY = 180,
            Overworld =
            {
                Mode = "danmaku",
                Duration = 420,
                ContactDamage = 4,
                EnemyRadius = 18,
                Patterns =
                {
                    new PatternDef { Kind = "rain", Interval = 20, Count = 3, Speed = 3.0, Spread = 24, Damage = 4, Life = 180 },
                    new PatternDef { Kind = "ring", Interval = 40, Count = 8, Speed = 2.0, Damage = 3, Life = 150, RandomCount = 2 },
                    new PatternDef { Kind = "aim", Interval = 30, Count = 1, Speed = 4.0, Damage = 5, Angle = 90, RandomAngle = 5 },
                },
            },
        };
        overworld.Trigger.Room = roomName;
        overworld.Trigger.Radius = 60;
        overworld.Victory.SetFlags.Add(901);
        overworld.Defeat.SetFlags.Add(902);
        project.File.Battles.Add(overworld);

        // 纯对话：绑定同一个房间的另一个实体，玩家靠近按 Z 播放三行文本，结束后设 flag 903。
        var dialogue = new DialogueDef
        {
            Id = "selftest_dialogue",
            Name = "自检纯对话",
            Room = roomName,
            InstanceId = instanceId + 2,
            ObjectName = "obj_readable",
            InstanceX = 240,
            InstanceY = 140,
            Mode = "area",
            Speaker = "演示角色",
            Lines =
            {
                "这是自检对话的第一行。",
                "第二行：对话编辑器会把这些文本写进生成 mod 的配置里。",
                "第三行结束，并设置剧情 flag 903。",
            },
        };
        dialogue.Trigger.Room = roomName;
        dialogue.Trigger.Radius = 64;
        dialogue.Trigger.Once = true;
        dialogue.Trigger.PersistOnce = true;
        dialogue.End.SetFlags.Add(903);
        dialogue.End.Message = "自检对话结束";
        project.File.Dialogues.Add(dialogue);

        // 简单剧情：绑定同一房间的实体，玩家按 Z 触发后走位、转向、换动作、播两句对话、最后设 flag 904。
        var cutscene = new CutsceneDef
        {
            Id = "selftest_cutscene",
            Name = "自检剧情",
            Room = roomName,
            InstanceId = instanceId + 3,
            ObjectName = "obj_readable",
            InstanceX = 260,
            InstanceY = 150,
            Mode = "interact",
        };
        cutscene.Trigger.Room = roomName;
        cutscene.Trigger.Radius = 72;
        cutscene.Trigger.Once = true;
        cutscene.Trigger.PersistOnce = true;
        cutscene.Steps.Add(new CutsceneStep { Kind = "face", Actor = "player", Direction = "left" });
        cutscene.Steps.Add(new CutsceneStep { Kind = "move", Actor = "player", X = 200, Y = 150, Frames = 20 });
        cutscene.Steps.Add(new CutsceneStep { Kind = "action", Actor = "player", Sprite = "spr_heart", ImageIndex = 0, ImageSpeed = 0, Frames = 10 });
        cutscene.Steps.Add(new CutsceneStep
        {
            Kind = "dialogue",
            Lines = { "自检剧情：走位与转向完成。", "第二句结束后设置剧情 flag 904。" },
        });
        cutscene.Steps.Add(new CutsceneStep { Kind = "sound", Sound = "snd_text", Gain = 0.5 });
        cutscene.Steps.Add(new CutsceneStep { Kind = "shake", Frames = 20, X = 4 });
        cutscene.Steps.Add(new CutsceneStep { Kind = "waitinput", Frames = 30 });
        cutscene.Steps.Add(new CutsceneStep { Kind = "flag", SetFlags = { 904 } });
        cutscene.End.Message = "自检剧情结束";
        project.File.Cutscenes.Add(cutscene);

        // Mod 设置项：bool 两个（战斗/对话开关）、int（弹幕伤害 %）、choice（演示难度）。
        project.File.Settings.Add(new SettingDef
        {
            Id = "battle.enabled",
            Label = "战斗系统",
            Type = "bool",
            Value = "1",
            Feature = "battlesEnabled",
            Description = "关闭后不再自动触发或启动战斗",
        });
        project.File.Settings.Add(new SettingDef
        {
            Id = "dialog.enabled",
            Label = "纯对话",
            Type = "bool",
            Value = "1",
            Feature = "dialoguesEnabled",
            Description = "关闭后地图上的纯对话实体不再响应",
        });
        project.File.Settings.Add(new SettingDef
        {
            Id = "barrage.damage",
            Label = "弹幕伤害",
            Type = "int",
            Value = "100",
            Min = 0,
            Max = 200,
            Step = 25,
            Feature = "barrageDamagePercent",
            Description = "弹幕伤害百分比，实时生效",
        });
        project.File.Settings.Add(new SettingDef
        {
            Id = "demo.mode",
            Label = "演示难度",
            Type = "choice",
            Value = "normal",
            Feature = "none",
            Description = "仅演示 choice 类型",
            Options =
            {
                new SettingOption { Label = "普通", Value = "normal" },
                new SettingOption { Label = "困难", Value = "hard" },
            },
        });

        project.Normalize();
        room = roomName;
        return project;
    }

    /// <summary>从 ntlb_data.gml 里把 return "..." 的 JSON 取出来（与写盘时的转义互逆）。</summary>
    private static string? ExtractEmbedded(string dataGml)
    {
        int a = dataGml.IndexOf("return \"", StringComparison.Ordinal);
        if (a < 0) return null;
        a += "return \"".Length;
        int b = dataGml.LastIndexOf('"');
        if (b <= a) return null;
        var raw = dataGml.Substring(a, b - a);
        return raw.Replace("\\\\", "\\").Replace("\\\"", "\"");
    }

    private static string DescribeIssues(List<ValidationIssue> issues)
    {
        if (issues.Count == 0) return "";
        return "（" + string.Join(" / ", issues.Select(i => i.Message)) + "）";
    }

    private static void Check(bool ok, string what)
    {
        if (ok) { _pass++; Console.WriteLine("  PASS  " + what); }
        else { _fail++; Console.WriteLine("  FAIL  " + what); }
    }
}
