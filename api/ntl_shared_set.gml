/// ntl_shared_set(key, value) —— 跨 mod 共享状态总线（任意 mod 可读写）
/// 用途：mod A 设置一个状态，mod B 读取并做出反应（无需互相依赖代码）
if (!variable_global_exists("ntl_shared")) global.ntl_shared = ds_map_create();
var _k = string(argument[0]);
var _v = (argument_count > 1) ? argument[1] : undefined;
if (ds_map_exists(global.ntl_shared, _k)) ds_map_replace(global.ntl_shared, _k, _v);
else ds_map_add(global.ntl_shared, _k, _v);
return _v;
