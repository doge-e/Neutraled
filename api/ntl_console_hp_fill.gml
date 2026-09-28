/// ntl_console_hp_fill() —— 把 HP 拉满（god 每帧调用；找不到就静默）
// ★ 2026-09-28 实测事故修复（god 崩机）：本函数由 ntl_console_power_tick 在 Step 事件里每帧调用，
//   那里 try/catch 兜不住 ⇒ 任何抛错都会让游戏弹 Code Error 直接死。现在整体包 try/catch，
//   最坏情况只是"这次没拉满"，绝不带走游戏；非数值的 HP/MAXHP 变量一律忽略。
try
{
    var _m = ntl_console_hp_find();
    if (_m == -1) return 0;
    var _mx = string(ds_map_find_value(_m, "maxname"));
    var _target = 999;
    if (_mx != "")
    {
        if (ds_map_find_value(_m, "kind") == "global")
        {
            if (variable_global_exists(_mx))
            {
                var _gv2 = variable_global_get(_mx);
                if (is_real(_gv2)) _target = _gv2;
            }
        }
        else
        {
            var _oi = asset_get_index(string(ds_map_find_value(_m, "obj")));
            var _inst = (_oi >= 0) ? instance_find(_oi, 0) : noone;
            if (_inst != noone && variable_instance_exists(_inst, _mx))
            {
                var _iv2 = variable_instance_get(_inst, _mx);
                if (is_real(_iv2)) _target = _iv2;
            }
        }
    }
    var _cur = ntl_console_hp_get();
    if (_cur < 0) return 0;
    if (_cur >= _target) return 0;
    return ntl_console_hp_set(_target);
}
catch (e)
{
    ntl_log("power", "hp_fill 失败（已忽略，不影响游戏）: " + string(e));
    return 0;
}
