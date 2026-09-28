/// ntl_hook_run_ntl(src, name, args, script, mode) —— 执行 NTL Script hook
var _src = string(argument[0]);
var _name = string(argument[1]);
var _args = argument[2];

var _env = ntl_e_new_env();
ntl_e_env_set(_env, "args", _args);
ntl_e_env_set(_env, "argc", array_length(_args));
for (var _j = 0; _j < array_length(_args) && _j < 8; _j += 1)
    ntl_e_env_set(_env, "a" + string(_j + 1), _args[_j]);

var _tmp = ds_map_create();
var _ast = ntl_live_compile("(hook:" + _name + ")", _src, _tmp);
if (_ast == undefined) { ds_map_destroy(_tmp); return undefined; }
var _ok = ntl_live_run_ast(_ast, _env, _name);
ds_map_destroy(_tmp);
if (!_ok) return undefined;
var _r = ntl_e_env_get(_env, "hook_result");
return _r;
