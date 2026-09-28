/// ntl_sprite_resolve(spriteIdx) —— 查询精灵是否被替换过（返回替换后的索引）
if (!variable_global_exists("ntl_sprite_swap")) return argument[0];
var _k = string(argument[0]);
if (ds_map_exists(global.ntl_sprite_swap, _k)) return ds_map_find_value(global.ntl_sprite_swap, _k);
return argument[0];
