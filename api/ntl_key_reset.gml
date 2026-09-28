/// ntl_key_reset() —— 把 ntl_key_fire 的按键状态**对齐到当前物理状态**
/// 用途：切控制台、窗口重新获得前台、面板里执行完一次动作后调用，避免"切换那一刻已经按住的键"被当成新按下。
/// ★ 不能直接 ds_map_clear()：清空之后，"此刻仍按住的键"下一帧又会被当成新按下 ——
///   实测踩过：面板里按一下 Z 切语言，因为 Z 还按着，之后每帧都触发一次 ⇒ 一次按键把 6 种语言整整转了一圈。
///   正确做法是把每个已跟踪的键写成"当前物理状态 + 计时器归零"：按住期间不再触发，松开后重新按下照常触发。
if (variable_global_exists("ntl_key_state"))
{
    var _m = global.ntl_key_state;
    var _k = ds_map_find_first(_m);
    while (!is_undefined(_k))
    {
        var _code = real(_k);
        var _now = (ntl_has_focus() && _code > 0 && keyboard_check_direct(_code)) ? 1 : 0;
        ds_map_replace(_m, _k, [_now, 0]);
        _k = ds_map_find_next(_m, _k);
    }
}
return 0;
