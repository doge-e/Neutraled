/// ntl_console_hp_find() —— best-effort 找玩家 HP 变量（hp / god 命令用）
/// 返回 ds_map{ kind(global|inst), obj, name, maxname, where } 或 -1（找不到）
var _globals = [["hp", "maxhp"], ["myhp", "mymaxhp"], ["player_hp", "player_maxhp"], ["global_hp", "global_maxhp"]];
for (var _i = 0; _i < array_length(_globals); _i += 1)
{
    var _n = _globals[_i][0];
    if (variable_global_exists(_n))
    {
        var _m = ds_map_create();
        ds_map_add(_m, "kind", "global");
        ds_map_add(_m, "obj", "");
        ds_map_add(_m, "name", _n);
        ds_map_add(_m, "maxname", _globals[_i][1]);
        ds_map_add(_m, "where", "global." + _n);
        return _m;
    }
}
var _objs = ["obj_heart", "obj_player", "obj_dw_player", "obj_kris"];
for (var _j = 0; _j < array_length(_objs); _j += 1)
{
    var _oi = asset_get_index(_objs[_j]);
    if (_oi < 0) continue;
    var _inst = instance_find(_oi, 0);
    if (_inst == noone) continue;
    if (!variable_instance_exists(_inst, "hp")) continue;
    var _m2 = ds_map_create();
    ds_map_add(_m2, "kind", "inst");
    ds_map_add(_m2, "obj", _objs[_j]);
    ds_map_add(_m2, "name", "hp");
    ds_map_add(_m2, "maxname", variable_instance_exists(_inst, "maxhp") ? "maxhp" : "");
    ds_map_add(_m2, "where", _objs[_j] + ".hp");
    return _m2;
}
return -1;
