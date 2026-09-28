/// ntl_bh_run(funcName, mode, args, origValue) —— 内置函数 Hook 的运行时分派
/// 返回数组 [handled(0/1), value]
///   handled=1 → 包装函数直接返回 value，不再调用原函数
var _fn = string(argument[0]);
var _mode = string(argument[1]);
var _args = argument[2];
var _orig = (argument_count > 3) ? argument[3] : undefined;

if (!variable_global_exists("ntl_bh_map")) return [0, undefined];
if (!ds_map_exists(global.ntl_bh_map, _fn)) return [0, undefined];

var _list = ds_map_find_value(global.ntl_bh_map, _fn);
if (!is_array(_list)) return [0, undefined];

for (var _i = 0; _i < array_length(_list); _i += 1)
{
    var _rec = _list[_i];
    if (ds_map_find_value(_rec, "mode") != _mode) continue;

    var _res = ntl_bh_invoke(_rec, _args, _orig);
    if (_res[0] == 1) return _res;
}
return [0, undefined];
