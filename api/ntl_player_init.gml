/// ntl_player_init(x, y) —— 初始化 Neutraled 玩家（用于 Kristal 地图导航演示）
if (!variable_global_exists("ntl_player")) global.ntl_player = ds_map_create();
var _p = global.ntl_player;
ds_map_replace(_p, "x", real(argument[0]));
ds_map_replace(_p, "y", real(argument[1]));
ds_map_replace(_p, "w", 20);
ds_map_replace(_p, "h", 30);
ds_map_replace(_p, "vx", 0);
ds_map_replace(_p, "vy", 0);
ds_map_replace(_p, "speed", 3.5);
ds_map_replace(_p, "dir", 2);          // 0上1右2下3左
ds_map_replace(_p, "sprite", -1);
ds_map_replace(_p, "active", 1);
return 1;
