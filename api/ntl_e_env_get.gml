/// ntl_e_env_get —— 老式脚本（每个函数一个同名脚本资源）
var _env = argument[0];
var _name = argument[1];
    if (!is_real(_env) || !ds_exists(_env, ds_type_map)) return undefined;
    if (ds_map_exists(_env, _name)) return ds_map_find_value(_env, _name);
    return undefined;
