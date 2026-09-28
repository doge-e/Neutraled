/// ntl_hook_run(script, mode, args) —— 运行某脚本在指定模式的 hook
///  返回 ds_map { handled: 0/1, value: 结果 }
var _script = string(argument[0]);
var _mode = string(argument[1]);
var _args = argument[2];
if (_args == undefined) _args = [];

var _out = ds_map_create();
ds_map_add(_out, "handled", 0);
ds_map_add(_out, "value", undefined);

var _list = ntl_hook_list(_script);
if (_list == undefined) return _out;

var _n = ds_list_size(_list);
for (var _i = 0; _i < _n; _i += 1)
{
    var _h = ds_list_find_value(_list, _i);
    // ★ 函数 hook 槽的元素是 ds_map（api/ntl_hook_init.gml:68 用 ds_map_create）；
    //   事件订阅槽的元素是脚本索引(real) → 这里只认 ds_map，否则会把脚本索引当 map 用
    if (!is_real(_h) || !ds_exists(_h, ds_type_map)) continue;
    if (ds_map_find_value(_h, "mode") != _mode) continue;

    var _handler = ds_map_find_value(_h, "handler");
    var _modDir = ds_map_find_value(_h, "moddir");
    if (_modDir == undefined) _modDir = "";
    var _source = ds_map_find_value(_h, "source");

    // 每次重新读取（开发期热重载友好）
    var _src = ntl_hook_load(_handler, _modDir, _source);
    if (_src == "") continue;

    var _r = undefined;
    if (_source == "lua") _r = ntl_hook_run_lua(_src, _handler, _args, _script, _mode);
    else _r = ntl_hook_run_ntl(_src, _handler, _args, _script, _mode);

    if (_r != undefined)
    {
        ds_map_replace(_out, "handled", 1);
        ds_map_replace(_out, "value", _r);
    }
}
return _out;
