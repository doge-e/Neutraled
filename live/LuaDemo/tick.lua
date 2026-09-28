-- 每 600 帧报一次帧号（演示 on_frame 事件与 arg 参数）
local f = arg
if f == nil then f = 0 end
if f > 0 and f % 600 == 0 then
    print("[LuaDemo] 第 " .. f .. " 帧")
end
