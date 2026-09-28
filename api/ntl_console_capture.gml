/// ntl_console_capture(on) —— 控制台输入屏蔽（改用游戏自身的 kbdBlocked 机制）
///
/// 关键发现：DELTARUNE 自己就有输入屏蔽开关：
///   function sunkus_kb_block() { global.kbdBlocked = true; }
///   function sunkus_kb_check(arg0) { return global.kbdBlocked ? false : keyboard_check(arg0); }
/// 所以只需在 BeginStep 里设 global.kbdBlocked = true，整个游戏的输入就被拦住了。
/// 本函数保留作为兼容入口（不再做键盘重映射）。
var _on = argument[0];
if (_on)
{
    global.kbdBlocked = true;
}
else
{
    global.kbdBlocked = false;
    // 恢复键盘映射（若之前有遗留）
    if (variable_global_exists("ntl_captured_keys"))
    {
        var _m = array_length(global.ntl_captured_keys);
        for (var _j = 0; _j < _m; _j += 1)
        {
            var _k2 = global.ntl_captured_keys[_j];
            if (_k2 > 0) keyboard_set_map(_k2, _k2);
        }
        global.ntl_captured_keys = [];
    }
}
return 0;
