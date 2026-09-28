/// ntl_player_test(mapId) —— 载入地图并放置玩家（最小闭环演示）
var _id = (argument_count > 0) ? string(argument[0]) : "before_palace";
var _mp = ntl_map_load(_id);
if (ds_map_find_value(_mp, "ok") != 1)
{
    ntl_log("player", "[错误] 地图加载失败: " + _id);
    return 0;
}
global.ntl_test_map = _mp;
global.ntl_cam_x = 0;
global.ntl_cam_y = 0;

// 找一个不在碰撞块里的出生点
var _sx = 200, _sy = 200;
var _coll = ds_map_find_value(_mp, "collision");
if (is_array(_coll))
{
    for (var _try = 0; _try < 200; _try += 1)
    {
        var _tx = 100 + (_try mod 20) * 64;
        var _ty = 100 + (_try div 20) * 64;
        if (ntl_map_collide(_mp, _tx, _ty, 20, 30) == 0) { _sx = _tx; _sy = _ty; break; }
    }
}
ntl_player_init(_sx, _sy);
ntl_log("player", "玩家已就位 (" + string(_sx) + "," + string(_sy) + ")，地图 " +
        string(ds_map_find_value(_mp, "width")) + "x" + string(ds_map_find_value(_mp, "height")));
return 1;
