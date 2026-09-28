/// ntl_mod_data_set(key, value) —— mod 间共享**持久数据**（会存盘）
///
/// 和 ntl_shared_set 的区别：
///   shared_set → 内存里的共享变量（重开游戏就没了）
///   data_set   → 存到磁盘（Neutraled/mods-shared.json），重开还在
///
/// 用途：mod 之间共享进度、解锁状态、配置
var _key = string(argument[0]);
var _val = (argument_count > 1) ? argument[1] : undefined;
if (!variable_global_exists("ntl_mod_data")) global.ntl_mod_data = ds_map_create();
if (ds_map_exists(global.ntl_mod_data, _key)) ds_map_replace(global.ntl_mod_data, _key, _val);
else ds_map_add(global.ntl_mod_data, _key, _val);
global.ntl_mod_data_dirty = 1;
return 1;
