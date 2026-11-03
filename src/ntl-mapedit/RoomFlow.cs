using UndertaleModLib.Models;

namespace NtlMapEdit;

public enum RoomFlowSection
{
    Main,
    Side,
    Special,
}

/// <summary>左侧房间列表的一行：Room=null 表示分组标题行（不可选中、不会打开房间）。</summary>
public sealed class RoomFlowEntry
{
    public UndertaleRoom? Room { get; init; }
    public string Name { get; init; } = "";
    public int DataIndex { get; init; } = -1;
    /// <summary>主线顺序号（从 1 开始）；支线沿用其前置主线的编号。</summary>
    public int Order { get; init; }
    public int Depth { get; init; }
    public string Tag { get; init; } = "";
    public RoomFlowSection Section { get; init; }
    public bool IsHeader => Room == null;
}

/// <summary>
/// 把 data.win 的原始房间顺序整理成「玩家实际流程顺序」：
/// 主线按数据顺序编号；支线（商店/隐藏/支线房）紧跟在它前面的主线房之后并缩进；
/// 测试/调试/占位/旧版/开场演示等房间集中放到底部「特殊 / 测试 / 旧版」分组。
/// 纯确定性规则（不反编译），因此自检可以断言顺序；无法识别的房间一律按主线处理（宁可少分组，不乱排）。
/// </summary>
public static class RoomFlow
{
    // 名字里出现这些片段 → 放到最底部（测试/调试/占位/旧版/开场演示/特殊流程）。
    private static readonly string[] SpecialTokens =
    {
        "test", "debug", "placeholder", "unused", "splashscreen", "splash_screen",
        "gameover", "game_over", "room_initialize", "place_", "battletest",
        "gms2", "teacup_demo", "shaun_puzzle", "room_legend", "_legend",
        "intro_ch2", "chapter_continue", "musical_sync", "transformation_sequence",
        "darkbase", "darkempty", "dark_empty", "room_empty", "_empty",
        "_old", "_original", "_og", "_backup", "_bak", "_copy", "_disabled",
    };

    // 名字里出现这些片段 → 视为支线（放在前置主线旁边，缩进一层）。
    private static readonly string[] SideTokens =
    {
        "secret", "hidden", "deadend", "dead_end", "bonus", "side", "shop",
        "_2a", "_3a", "_2b", "_3b", "extra",
    };

    public static bool IsSpecialName(string name)
    {
        var n = (name ?? "").ToLowerInvariant();
        foreach (var t in SpecialTokens)
            if (n.Contains(t, StringComparison.Ordinal)) return true;
        return false;
    }

    public static bool IsSideName(string name)
    {
        var n = (name ?? "").ToLowerInvariant();
        foreach (var t in SideTokens)
            if (n.Contains(t, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>
    /// 生成左侧列表的行。includeSpecial=false 时省略底部特殊分组（搜索过滤时用）。
    /// </summary>
    public static List<RoomFlowEntry> Build(IReadOnlyList<UndertaleRoom> rooms, bool includeSpecial = true)
    {
        var main = new List<RoomFlowEntry>();
        var sideAfter = new Dictionary<int, List<RoomFlowEntry>>();
        var specials = new List<RoomFlowEntry>();
        int lastMain = -1;

        for (int i = 0; i < rooms.Count; i++)
        {
            var room = rooms[i];
            if (room == null) continue;
            var name = GameData.Name(room.Name);
            if (IsSpecialName(name))
            {
                specials.Add(new RoomFlowEntry
                {
                    Room = room,
                    Name = name,
                    DataIndex = i,
                    Section = RoomFlowSection.Special,
                    Tag = "特殊",
                });
                continue;
            }
            if (IsSideName(name))
            {
                if (!sideAfter.TryGetValue(lastMain, out var list))
                {
                    list = new List<RoomFlowEntry>();
                    sideAfter[lastMain] = list;
                }
                list.Add(new RoomFlowEntry
                {
                    Room = room,
                    Name = name,
                    DataIndex = i,
                    Depth = 1,
                    Tag = "支线",
                    Section = RoomFlowSection.Side,
                    Order = lastMain >= 0 ? main[lastMain].Order : 0,
                });
                continue;
            }
            var entry = new RoomFlowEntry
            {
                Room = room,
                Name = name,
                DataIndex = i,
                Order = main.Count + 1,
                Section = RoomFlowSection.Main,
            };
            main.Add(entry);
            lastMain = main.Count - 1;
        }

        var result = new List<RoomFlowEntry>(main.Count + specials.Count + 4);
        for (int i = -1; i < main.Count; i++)
        {
            // 主线在前、支线紧跟在它后面（「支线放在主线旁/下面」）；没有前置主线时支线排最前。
            if (i >= 0) result.Add(main[i]);
            if (sideAfter.TryGetValue(i, out var sides))
                result.AddRange(sides);
        }

        if (includeSpecial && specials.Count > 0)
        {
            result.Add(new RoomFlowEntry
            {
                Name = "特殊 / 测试 / 旧版（" + specials.Count + "）",
                Section = RoomFlowSection.Special,
                Tag = "分组",
            });
            result.AddRange(specials);
        }
        return result;
    }

    /// <summary>没有流程排序时用的原始顺序（保持旧行为，也方便对照）。</summary>
    public static List<RoomFlowEntry> BuildDataOrder(IReadOnlyList<UndertaleRoom> rooms)
    {
        var list = new List<RoomFlowEntry>(rooms.Count);
        for (int i = 0; i < rooms.Count; i++)
        {
            var room = rooms[i];
            if (room == null) continue;
            list.Add(new RoomFlowEntry
            {
                Room = room,
                Name = GameData.Name(room.Name),
                DataIndex = i,
                Section = RoomFlowSection.Main,
            });
        }
        return list;
    }
}
