-- ============================================================
-- Neutraled 控制台 (Kristal 库版)
--   注入方式：mods/<激活的 mod>/libraries/ntlconsole/
--   不改引擎任何文件 —— 只包装 LOVE 回调，任何 Kristal 版本都能用。
--   F2 开关 · Enter 执行 · Esc 关闭 · ↑/↓ 历史
-- ============================================================
local C = {}
_G.NTLConsole = C

-- ============================================================
-- 文案表（zh / en）
--   ★ 这一行的占位符由 builder 在注入时替换成配置文件里的语言（zh / en）；
--     手动改成 "en" 也可以。占位符没被替换（直接跑源码）时按 zh 处理。
-- ============================================================
local NTL_LANG = "@@NTL_LANG@@"
local S = {
    zh = {
        help_head   = "=== Neutraled 控制台 ===",
        no_game     = "不在游戏内",
        heal_done   = "已治疗 %d 名队员",
        money_set   = "金钱 = %s",
        fail        = "失败: %s",
        pos         = "坐标: %d, %d",
        tp_done     = "已传送到 %d, %d",
        give_done   = "已给予 %s x%d",
        map_id      = "地图: %s",
        reloaded    = "已重载地图",
        quit_msg    = "退出游戏",
        ver_line    = "Neutraled 控制台 v%s (Kristal)",
        unknown_cmd = "未知命令: %s（输入 help）",
        err_run     = "错误: %s",
        banner      = "Neutraled 控制台 v%s · 输入 help 查看命令",
        banner_hint = "（中文输入法开着也能打字：键盘兜底已启用）",
        footer      = "Neutraled 控制台 · Enter 执行 · Esc/F2 关闭 · ↑↓ 历史",
        usage_flag  = "用法: flag <编号> [0/1]",
        usage_tp    = "用法: tp <x> <y>",
        usage_give  = "用法: give <物品id> [数量]",
        ["cmd.help.d"]   = "列出全部命令",
        ["cmd.heal.d"]   = "治疗全队",
        ["cmd.money.u"]  = "money <数>",
        ["cmd.money.d"]  = "设置金钱（默认 9999）",
        ["cmd.flag.u"]   = "flag <编号> [0/1]",
        ["cmd.flag.d"]   = "读/写 flag",
        ["cmd.pos.d"]    = "当前坐标",
        ["cmd.tp.d"]     = "传送",
        ["cmd.give.u"]   = "give <物品id> [数量]",
        ["cmd.give.d"]   = "给予物品",
        ["cmd.map.d"]    = "当前地图 id",
        ["cmd.reload.d"] = "重载当前地图",
        ["cmd.party.d"]  = "显示队伍血量",
        ["cmd.fps.d"]    = "帧率",
        ["cmd.ver.d"]    = "版本信息",
        ["cmd.quit.d"]   = "退出游戏",
    },
    en = {
        help_head   = "=== Neutraled Console ===",
        no_game     = "Not in game",
        heal_done   = "Healed %d party member(s)",
        money_set   = "Money = %s",
        fail        = "Failed: %s",
        pos         = "Pos: %d, %d",
        tp_done     = "Teleported to %d, %d",
        give_done   = "Gave %s x%d",
        map_id      = "Map: %s",
        reloaded    = "Map reloaded",
        quit_msg    = "Quitting game",
        ver_line    = "Neutraled Console v%s (Kristal)",
        unknown_cmd = "Unknown command: %s (type help)",
        err_run     = "Error: %s",
        banner      = "Neutraled Console v%s · type help for commands",
        banner_hint = "(Keyboard fallback is active, so you can type with a CJK IME on)",
        footer      = "Neutraled Console · Enter run · Esc/F2 close · ↑↓ history",
        usage_flag  = "Usage: flag <index> [0/1]",
        usage_tp    = "Usage: tp <x> <y>",
        usage_give  = "Usage: give <item id> [count]",
        ["cmd.help.d"]   = "List all commands",
        ["cmd.heal.d"]   = "Heal the whole party",
        ["cmd.money.u"]  = "money <amount>",
        ["cmd.money.d"]  = "Set money (default 9999)",
        ["cmd.flag.u"]   = "flag <index> [0/1]",
        ["cmd.flag.d"]   = "Read/write a flag",
        ["cmd.pos.d"]    = "Current position",
        ["cmd.tp.d"]     = "Teleport",
        ["cmd.give.u"]   = "give <item id> [count]",
        ["cmd.give.d"]   = "Give an item",
        ["cmd.map.d"]    = "Current map id",
        ["cmd.reload.d"] = "Reload the current map",
        ["cmd.party.d"]  = "Show party HP",
        ["cmd.fps.d"]    = "FPS",
        ["cmd.ver.d"]    = "Version info",
        ["cmd.quit.d"]   = "Quit the game",
    },
}
local LANG = (NTL_LANG == "en") and "en" or "zh"
local function T(k, ...)
    local tbl = S[LANG] or S.zh
    local s = tbl[k]
    if s == nil then s = S.zh[k] end
    if s == nil then return k end
    if select("#", ...) > 0 then return string.format(s, ...) end
    return s
end
-- 日志配色判据（drawConsole 用）：错误前缀 / 失败关键词，中英各一套
local COL_ERR = (LANG == "en") and "Error:" or "错误"
local COL_FAIL = (LANG == "en") and "Failed" or "失败"

C.version = "1.1.2"
C.open = false
C.input = ""
C.lines = {}
C.history = {}
C.histIdx = 0
C.font = nil
C.scroll = 0
C.lastToggle = -10
C.lastText = -10          -- 最近一次 textinput 的时间（用来判断 textinput 到底有没有在工作）
C.lastFallback = -10      -- 最近一次"键盘兜底补字"的时间
C.lastFallbackChar = nil

-- ---------- 键名 → 字符（兜底用） ----------
-- ⚠ 中文输入法开着时，SDL **不会**发 textinput（字母被输入法吃掉）→ 只能从键盘事件自己补字符。
--    F2/Esc 能收到说明键盘事件本身是通的（实测：控制台能开能关，就是打不进字）。
local SHIFT_MAP = {
    ["1"] = "!", ["2"] = "@", ["3"] = "#", ["4"] = "$", ["5"] = "%",
    ["6"] = "^", ["7"] = "&", ["8"] = "*", ["9"] = "(", ["0"] = ")",
    ["-"] = "_", ["="] = "+", ["["] = "{", ["]"] = "}", ["\\"] = "|",
    [";"] = ":", ["'"] = "\"", [","] = "<", ["."] = ">", ["/"] = "?", ["`"] = "~",
}
local function keyToChar(key)
    if key == "space" then return " " end
    if key == "kp0" then return "0" end
    if key == "kp1" then return "1" end
    if key == "kp2" then return "2" end
    if key == "kp3" then return "3" end
    if key == "kp4" then return "4" end
    if key == "kp5" then return "5" end
    if key == "kp6" then return "6" end
    if key == "kp7" then return "7" end
    if key == "kp8" then return "8" end
    if key == "kp9" then return "9" end
    if key == "kp." then return "." end
    if #key ~= 1 then return nil end
    local shift = love.keyboard.isDown("lshift", "rshift")
    if key:match("%a") then return shift and key:upper() or key end
    if shift then return SHIFT_MAP[key] or key end
    return key
end

-- ---------- 日志（写到 LOVE 存档目录，方便排查） ----------
local function logf(s)
    pcall(function()
        love.filesystem.append("ntlconsole.log", os.date("[%H:%M:%S] ") .. tostring(s) .. "\n")
    end)
end
C.log = logf

local function out(s)
    table.insert(C.lines, tostring(s))
    while #C.lines > 200 do table.remove(C.lines, 1) end
    C.scroll = 0
end

-- ---------- 字体 ----------
local FONT_CANDS = {
    { "assets/fonts/ntl_cjk.ttf", 18 },   -- Neutraled 注入的中文字体
    { "assets/fonts/ja_main.ttf", 18 },
    { "assets/fonts/small.ttf", 14 },
}
local function getFont()
    if C.font then return C.font end
    for _, c in ipairs(FONT_CANDS) do
        local ok, f = pcall(love.graphics.newFont, c[1], c[2])
        if ok and f then C.font = f; return f end
    end
    C.font = love.graphics.getFont()
    return C.font
end

-- ---------- 宿主数据 ----------
local function G() return rawget(_G, "Game") end

-- ---------- 命令 ----------
local cmds = {}
local function CMD(name, usage, help, fn) cmds[name] = { usage = usage, help = help, fn = fn } end

CMD("help", "help", T("cmd.help.d"), function()
    out(T("help_head"))
    local names = {}
    for k in pairs(cmds) do table.insert(names, k) end
    table.sort(names)
    for _, k in ipairs(names) do
        out(string.format("  %-10s %s", cmds[k].usage, cmds[k].help))
    end
end)

CMD("heal", "heal", T("cmd.heal.d"), function()
    local g = G(); if not g then out(T("no_game")) return end
    local n = 0
    for _, p in ipairs(g.party or {}) do
        if p and p.heal then pcall(function() p:heal(p.maxhp or 9999) end); n = n + 1 end
    end
    out(T("heal_done", n))
end)

CMD("money", T("cmd.money.u"), T("cmd.money.d"), function(a)
    local g = G(); if not g then out(T("no_game")) return end
    local n = tonumber(a) or 9999
    local ok, err = pcall(function() g.money = n end)
    out(ok and T("money_set", n) or T("fail", tostring(err)))
end)

CMD("flag", T("cmd.flag.u"), T("cmd.flag.d"), function(a, b)
    local g = G(); if not g then out(T("no_game")) return end
    local id = tonumber(a)
    if not id then out(T("usage_flag")) return end
    if b == nil or b == "" then
        local ok, v = pcall(function() return g:getFlag(id) end)
        out(ok and ("flag[" .. id .. "] = " .. tostring(v)) or T("fail", tostring(v)))
    else
        local v = (b == "0") and 0 or 1
        local ok, err = pcall(function() g:setFlag(id, v) end)
        out(ok and ("flag[" .. id .. "] = " .. v) or T("fail", tostring(err)))
    end
end)

CMD("pos", "pos", T("cmd.pos.d"), function()
    local g = G(); if not g then out(T("no_game")) return end
    local ok, x, y = pcall(function() return g.world.player.x, g.world.player.y end)
    out(ok and T("pos", x, y) or T("fail", tostring(x)))
end)

CMD("tp", "tp <x> <y>", T("cmd.tp.d"), function(a, b)
    local g = G(); if not g then out(T("no_game")) return end
    local x, y = tonumber(a), tonumber(b)
    if not x or not y then out(T("usage_tp")) return end
    local ok, err = pcall(function() g.world.player.x = x; g.world.player.y = y end)
    out(ok and T("tp_done", x, y) or T("fail", tostring(err)))
end)

CMD("give", T("cmd.give.u"), T("cmd.give.d"), function(a, b)
    local g = G(); if not g then out(T("no_game")) return end
    if not a or a == "" then out(T("usage_give")) return end
    local n = tonumber(b) or 1
    local ok, err = pcall(function() g:addItem(a, n) end)
    out(ok and T("give_done", a, n) or T("fail", tostring(err)))
end)

CMD("map", "map", T("cmd.map.d"), function()
    local g = G(); if not g then out(T("no_game")) return end
    local ok, id = pcall(function() return g.world.map.id end)
    out(ok and T("map_id", tostring(id)) or T("fail", tostring(id)))
end)

CMD("reload", "reload", T("cmd.reload.d"), function()
    local g = G(); if not g then out(T("no_game")) return end
    local ok, err = pcall(function() g.world:loadMap(g.world.map.id, g.world.map.id) end)
    out(ok and T("reloaded") or T("fail", tostring(err)))
end)

CMD("party", "party", T("cmd.party.d"), function()
    local g = G(); if not g then out(T("no_game")) return end
    local ok, err = pcall(function()
        for i, p in ipairs(g.party) do
            out(string.format("  %d. %s  %d/%d", i, tostring(p.name or p.id or "?"),
                math.floor(p.hp or 0), math.floor(p.maxhp or 0)))
        end
    end)
    if not ok then out(T("fail", tostring(err))) end
end)

CMD("fps", "fps", T("cmd.fps.d"), function()
    out("FPS: " .. tostring(love.timer.getFPS()))
end)

CMD("ver", "ver", T("cmd.ver.d"), function()
    out(T("ver_line", C.version))
end)

CMD("quit", "quit", T("cmd.quit.d"), function()
    out(T("quit_msg"))
    pcall(function() love.event.quit() end)
end)

local function run(line)
    line = line:gsub("^%s+", ""):gsub("%s+$", "")
    if line == "" then return end
    table.insert(C.history, line)
    C.histIdx = #C.history + 1
    local before = #C.lines
    out("> " .. line)
    logf("执行: " .. line)
    local name, rest = line:match("^(%S+)%s*(.*)$")
    local c = cmds[name]
    if not c then out(T("unknown_cmd", tostring(name))) end
    if c then
        local a, b = rest:match("^(%S*)%s*(.*)$")
        local ok, err = pcall(c.fn, a, b)
        if not ok then out(T("err_run", tostring(err))) end
    end
    for i = before + 1, #C.lines do logf("   " .. C.lines[i]) end
end

-- ---------- 按键 ----------
local function toggle()
    local t = love.timer.getTime()
    if t - C.lastToggle < 0.12 then return end   -- 去抖（原来 0.25 太大，用户反馈"按快了没反应"）
    C.lastToggle = t
    C.open = not C.open
    C.input = ""
    if C.open then
        C.font = nil
        getFont()
        if #C.lines == 0 then
            out(T("banner", C.version))
            out(T("banner_hint"))
        end
    end
    logf(C.open and "打开控制台" or "关闭控制台")
end

function C.keypressed(key, scancode, is_repeat)
    if key == "f2" then toggle(); return true end
    if not C.open then return false end
    if key == "escape" then C.open = false; logf("关闭控制台(Esc)"); return true end
    if key == "return" or key == "kpenter" then run(C.input); C.input = ""; return true end
    if key == "backspace" then C.input = C.input:sub(1, -2); return true end
    if key == "up" then
        if C.histIdx > 1 then C.histIdx = C.histIdx - 1; C.input = C.history[C.histIdx] or "" end
        return true
    end
    if key == "down" then
        if C.histIdx < #C.history then C.histIdx = C.histIdx + 1; C.input = C.history[C.histIdx] or ""
        else C.histIdx = #C.history + 1; C.input = "" end
        return true
    end
    if key == "pageup" then C.scroll = math.min(C.scroll + 3, math.max(0, #C.lines - 1)); return true end
    if key == "pagedown" then C.scroll = math.max(C.scroll - 3, 0); return true end
    -- ★ 字符兜底：输入法开着时 SDL 不发 textinput，这里自己把字符补上
    local ch = keyToChar(key)
    if ch then
        if love.timer.getTime() - C.lastText > 0.3 then
            C.input = C.input .. ch
            C.lastFallbackChar = ch
            C.lastFallback = love.timer.getTime()
        end
        return true
    end
    if key == "v" and love.keyboard.isDown("lctrl", "rctrl") then
        local ok, t = pcall(love.keyboard.getClipboardText)
        if ok and t then C.input = C.input .. t end
        return true
    end
    return true   -- 打开时吞掉其它按键，别让游戏同时响应
end

function C.textinput(t)
    if not C.open then return false end
    local now = love.timer.getTime()
    C.lastText = now
    if t == "\r" or t == "\n" then return true end
    -- 去重：这个字符刚被键盘兜底补上过（同一个键先 keypress 后 textinput）→ 撤掉兜底那次
    if C.lastFallbackChar and t == C.lastFallbackChar and now - C.lastFallback < 0.15 then
        C.input = C.input:sub(1, -(#t + 1))
        C.lastFallbackChar = nil
    else
        C.input = C.input .. t
    end
    return true
end

-- ---------- 绘制 ----------
local function drawConsole()
    if not C.open then return end
    local f = getFont()
    local w = love.graphics.getWidth()
    local h = math.min(360, love.graphics.getHeight() - 40)
    love.graphics.push("all")
    love.graphics.setColor(0, 0, 0, 0.86)
    love.graphics.rectangle("fill", 16, 16, w - 32, h, 6, 6)
    love.graphics.setColor(0.35, 0.75, 1, 1)
    love.graphics.rectangle("line", 16, 16, w - 32, h, 6, 6)
    love.graphics.setFont(f)
    love.graphics.setColor(0.6, 0.85, 1, 1)
    love.graphics.print(T("footer"), 32, 26)
    love.graphics.setColor(1, 1, 1, 1)
    local lineH = f:getHeight() + 2
    local maxLines = math.floor((h - 76) / lineH)
    local total = #C.lines
    local last = total - C.scroll                      -- 视口最后一行
    local first = math.max(1, last - maxLines + 1)
    if last > total then last = total end
    local y = 52
    for i = first, last do
        local s = C.lines[i]
        if s:sub(1, 1) == ">" then love.graphics.setColor(1, 1, 0.45, 1)
        elseif s:sub(1, #COL_ERR) == COL_ERR or s:find(COL_FAIL, 1, true) then love.graphics.setColor(1, 0.45, 0.45, 1)
        else love.graphics.setColor(1, 1, 1, 1) end
        love.graphics.print(s, 32, y)
        y = y + lineH
    end
    love.graphics.setColor(0.4, 1, 0.6, 1)
    love.graphics.print("> " .. C.input .. "_", 32, 16 + h - lineH - 8)
    love.graphics.pop()
end

-- ---------- 挂接 LOVE 回调（幂等） ----------
local function hook()
    if rawget(_G, "__NTL_CONSOLE_HOOKED") then
        logf("库已加载（已挂接过，跳过）")
        return
    end
    rawset(_G, "__NTL_CONSOLE_HOOKED", true)

    local kp = love.keypressed
    love.keypressed = function(key, scancode, is_repeat)
        local ok, consumed = pcall(C.keypressed, key, scancode, is_repeat)
        if not ok then logf("keypressed 错误: " .. tostring(consumed)) end
        if consumed then return end
        if kp then return kp(key, scancode, is_repeat) end
    end

    local ti = love.textinput
    love.textinput = function(t)
        local ok, consumed = pcall(C.textinput, t)
        if not ok then logf("textinput 错误: " .. tostring(consumed)) end
        if consumed then return end
        if ti then return ti(t) end
    end

    local dr = love.draw
    love.draw = function(...)
        if dr then dr(...) end
        local ok, err = pcall(drawConsole)
        if not ok then logf("draw 错误: " .. tostring(err)) end
    end

    logf("控制台已挂接（F2 开关）")
end

hook()

return C
