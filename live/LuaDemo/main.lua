-- Neutraled 演示
print("=== Neutraled ===")
local ok = pcall(function() return require("mods.chapter5wr_windows.mod") end)
print("Kristal mod: " .. tostring(ok))
if Mod then
    Mod.info = { name = "Frostveil" }
    Mod.name = "Frostveil"
    pcall(function() Mod:init() end)
    pcall(function() Mod:postInit() end)
end
-- ⚠ 不要在这里自动加载演示地图：它会盖掉**章节选择器**（root 房间也被它接管），

--   玩家会看不到章节列表。需要看地图运行时的效果时，在控制台手动执行：

--       ntl_player_test("before_palace")

-- （已实测：自动调用会导致"游戏跑起来是黑屏 + 一个小人"而不是章节选择）
print("=== 就绪 ===")
