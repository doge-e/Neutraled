/// ntl_dsmap_keys(map) —— 取 ds_map 的 key 数组；空 map / 非法 map 一律返回空数组 []
/// ★ 真机硬坑（2026-09-26 定位）：GameMaker 2023.6 的 ds_map_keys_to_array(空 map) 返回 undefined
///   （不是空数组），随后 array_length(undefined) 也是 undefined，于是
///   `array_length(_keys) - N` 会以 "DoSub :2: undefined value" 崩掉整局
///   （真机现场：root 进程 obj_init_pc Create → ntl_hook_init，开机即 Code Error）。
///   需要取 key 时一律用本函数，不要直接调 ds_map_keys_to_array。
var _map = argument[0];
if (!is_real(_map) || _map == 0) return [];
if (!ds_exists(_map, ds_type_map)) return [];
var _keys = ds_map_keys_to_array(_map);
if (is_undefined(_keys)) return [];
return _keys;
