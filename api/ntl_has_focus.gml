/// ntl_has_focus() —— 游戏窗口是否在前台（我们所有 keyboard_check_direct 读键的总闸）
/// 背景：DELTARUNE 每帧可能调用 keyboard_clear_all()，所以我们的输入一律用 keyboard_check_direct
///   （硬件状态，清不掉 GM 状态）；但硬件状态**不区分窗口** —— 游戏在后台时，玩家在别的窗口
///   打字也会被我们读进来（用户实测：控制台把游戏外的输入写了进去）。
/// 判定顺序：os_is_paused()（游戏自己的 obj_time 用它判"切出去"）→ window_has_focus()（较新运行时才有）
///   → 都不可用则返回 1（保持旧行为，绝不误伤正常输入）。
/// 每帧只求值一次（缓存在 global.ntl_focus_now / ntl_focus_frame），并在状态翻转时记一行日志便于取证。
if (variable_global_exists("ntl_focus_frame") && variable_global_exists("ntl_focus_now")
    && global.ntl_focus_frame == global.ntl_frames)
{
    return global.ntl_focus_now;
}
var _f = 1;
var _paused = -1;
try { _paused = os_is_paused(); } catch (e) { _paused = -1; }
if (_paused == 1) _f = 0;
if (_f == 1)
{
    // window_has_focus() 在部分运行时不存在 —— 不存在时抛异常，被这里吞掉（退化为上面的判定）
    try { if (!window_has_focus()) _f = 0; } catch (e2) { }
}
global.ntl_focus_now = _f;
global.ntl_focus_frame = global.ntl_frames;
if (!variable_global_exists("ntl_focus_last") || global.ntl_focus_last != _f)
{
    if (variable_global_exists("ntl_focus_last"))
    {
        ntl_log("focus", "[focus] 窗口前台状态 → " + string(_f) + "（os_is_paused=" + string(_paused) + "）");
        if (_f == 1) ntl_key_reset();   // 回到前台：清掉失焦期间的状态，别把"当时按住的键"当成新按下
    }
    global.ntl_focus_last = _f;
}
return _f;
