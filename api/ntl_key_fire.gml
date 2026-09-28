/// ntl_key_fire(key[, firstDelayUs, repeatUs]) —— 「本帧该触发一次吗」（自带按下沿 + 按住自动重复）
/// 为什么不用 keyboard_check_pressed：DELTARUNE 每帧 keyboard_clear_all() 会把 GM 的按键状态清掉，
///   实测表现就是「按下了却没反应」（用户反馈：控制台 Backspace 常常按了不删字）。
/// 为什么不用裸 keyboard_check_direct：它是硬件状态，**游戏不在前台时同样为真** ——
///   玩家在别的窗口打字会被读进来（用户实测）。所以统一过 ntl_has_focus() 这道门。
/// 参数：firstDelayUs = 按住多久后开始重复（微秒；0 = 只在按下沿触发一次）
///       repeatUs     = 重复间隔（微秒；<= 0 = 不重复）
/// 返回：0 或 1（本帧触发次数）
if (!variable_global_exists("ntl_key_state")) global.ntl_key_state = ds_map_create();
var _k = argument[0];
var _delay = (argument_count > 1) ? argument[1] : 0;
var _rate = (argument_count > 2) ? argument[2] : 0;
var _ks = string(_k);
var _now = (ntl_has_focus() && keyboard_check_direct(_k)) ? 1 : 0;
var _held = 0;
var _t = 0;
if (ds_map_exists(global.ntl_key_state, _ks))
{
    var _st = ds_map_find_value(global.ntl_key_state, _ks);
    _held = _st[0];
    _t = _st[1];
}
var _fire = 0;
if (_now == 1)
{
    if (_held == 0) { _fire = 1; _t = _delay; }
    else if (_rate > 0)
    {
        _t -= delta_time;                 // delta_time 单位微秒 → 与帧率无关
        if (_t <= 0) { _fire = 1; _t = _rate; }
    }
}
else _t = 0;
if (ds_map_exists(global.ntl_key_state, _ks)) ds_map_replace(global.ntl_key_state, _ks, [_now, _t]);
else ds_map_add(global.ntl_key_state, _ks, [_now, _t]);
return _fire;
