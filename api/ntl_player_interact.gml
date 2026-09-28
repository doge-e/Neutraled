/// ntl_player_interact(map) —— 玩家与地图对象交互（按 E/Z 触发最近的）
/// 触发时把对象信息写进 global.ntl_interact_* 供 live/脚本读取，并调用 on_interact 事件
var _mp = argument[0];
if (!variable_global_exists("ntl_player")) return 0;
var _p = global.ntl_player;
var _px = ds_map_find_value(_p, "x") + ds_map_find_value(_p, "w") / 2;
var _py = ds_map_find_value(_p, "y") + ds_map_find_value(_p, "h") / 2;

var _near = ntl_map_objects_near(_mp, _px, _py, 48);
global.ntl_near_count = array_length(_near);

// 更新"最近对象"状态（供绘制提示）
if (array_length(_near) > 0)
{
    global.ntl_near_obj = _near[0];
    global.ntl_near_name = ds_map_find_value(_near[0], "name");
    global.ntl_near_type = ds_map_find_value(_near[0], "type");
}
else
{
    global.ntl_near_obj = undefined;
    global.ntl_near_name = "";
    global.ntl_near_type = "";
}

// 按键触发（E 或 Z），去抖
if (!variable_global_exists("ntl_interact_held")) global.ntl_interact_held = false;
var _key = keyboard_check_direct(ord("E")) || keyboard_check_direct(ord("Z")) || keyboard_check_direct(vk_enter);
if (_key && !global.ntl_interact_held && array_length(_near) > 0)
{
    var _hit = _near[0];
    global.ntl_interact_name = ds_map_find_value(_hit, "name");
    global.ntl_interact_group = ds_map_find_value(_hit, "group");
    global.ntl_interact_obj = ds_map_find_value(_hit, "obj");
    ntl_log("map", "交互触发: " + string(global.ntl_interact_name) +
            " (组 " + string(global.ntl_interact_group) + ")");
    // 广播给 live 脚本
    if (variable_global_exists("ntl_live_ready"))
    {
        try { ntl_live_emit("on_interact", 0); } catch (e) { ntl_log("player", "[ntl] ntl_player_interact.gml:40 ntl_live_emit(on_interact) 失败: " + string(e)); }
    }
    // 触发计数（演示用）
    if (!variable_global_exists("ntl_interact_count")) global.ntl_interact_count = 0;
    global.ntl_interact_count += 1;
}
global.ntl_interact_held = _key;
return array_length(_near);
