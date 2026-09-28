/// ntl_e_env_set —— 老式脚本（每个函数一个同名脚本资源）
var _env = argument[0];
var _name = argument[1];
var _val = argument[2];
    if (!is_real(_env) || !ds_exists(_env, ds_type_map)) return undefined;
    if (ds_map_exists(_env, _name)) ds_map_replace(_env, _name, _val);
    else ds_map_add(_env, _name, _val);
