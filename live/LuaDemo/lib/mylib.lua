-- 自定义模块示例（require("lib.mylib")）
local M = {}
function M.greet(name)
    return "你好, " .. name .. "!"
end
function M.sum(list)
    local s = 0
    for i = 1, #list do s = s + list[i] end
    return s
end
return M
