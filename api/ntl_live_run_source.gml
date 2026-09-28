/// ntl_live_run_source(src, env, label) —— 编译并执行一段脚本（一次性，不缓存）
var _src = argument[0];
var _env = argument[1];
var _label = argument[2];

var _tmp = ds_map_create();
var _ast = ntl_live_compile("(inline:" + string(_label) + ")", _src, _tmp);
var _ok = false;
if (_ast != undefined) _ok = ntl_live_run_ast(_ast, _env, _label);
ds_map_destroy(_tmp);
return _ok;
