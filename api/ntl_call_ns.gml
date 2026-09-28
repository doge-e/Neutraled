/// ntl_call_ns(name, args) —— 解析 "ns.func" 形式的依赖调用
/// 返回 ds_map { found: 0/1, value: 结果 }（found=0 表示不是命名空间调用）
var _name = argument[0];
var _args = argument[1];

var _out = ds_map_create();
ds_map_add(_out, "found", 0);
ds_map_add(_out, "value", undefined);

if (string_pos(".", _name) <= 0) return _out;
if (!variable_global_exists("ntl_ns")) return _out;
if (!ds_map_exists(global.ntl_ns, _name)) return _out;

var _script = ds_map_find_value(global.ntl_ns, _name);
var _idx = asset_get_index(_script);
ds_map_replace(_out, "found", 1);
if (_idx < 0)
{
    ntl_log("ns", "[错误] 依赖函数未找到: " + _name + " -> 脚本 " + _script);
    return _out;
}

var _n = array_length(_args);
switch (_n)
{
    case 0: ds_map_replace(_out, "value", script_execute(_idx)); break;
    case 1: ds_map_replace(_out, "value", script_execute(_idx, _args[0])); break;
    case 2: ds_map_replace(_out, "value", script_execute(_idx, _args[0], _args[1])); break;
    case 3: ds_map_replace(_out, "value", script_execute(_idx, _args[0], _args[1], _args[2])); break;
    case 4: ds_map_replace(_out, "value", script_execute(_idx, _args[0], _args[1], _args[2], _args[3])); break;
    case 5: ds_map_replace(_out, "value", script_execute(_idx, _args[0], _args[1], _args[2], _args[3], _args[4])); break;
    case 6: ds_map_replace(_out, "value", script_execute(_idx, _args[0], _args[1], _args[2], _args[3], _args[4], _args[5])); break;
    default: ntl_log("ns", "[错误] 参数过多: " + _name); break;
}
return _out;
