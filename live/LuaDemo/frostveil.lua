-- 冰封帷幕（Frostveil）加载示例
-- 说明：这是真实的 Kristal mod（mods/chapter5wr_windows）在 Neutraled Lua 里运行的示例
print("=== 冰封帷幕加载 ===")
local modDef = require("mods.chapter5wr_windows.mod")
Mod.info = { name = "Frostveil", id = "chapter_5_weird" }
Mod.name = "Frostveil"
local ok1 = pcall(function() Mod:init() end)
print("Mod:init() = " .. tostring(ok1))
local ok2 = pcall(function() Mod:postInit() end)
print("Mod:postInit() = " .. tostring(ok2))
print("flag wr_set = " .. tostring(Game:getFlag("wr_set")))
print("=== 冰封帷幕就绪 ===")
