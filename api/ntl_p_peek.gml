/// ntl_p_peek —— 老式脚本（每个函数一个同名脚本资源）
var _s = argument[0];
    if (!is_real(_s) || !ds_exists(_s, ds_type_map)) return undefined;
    var _toks = ds_map_find_value(_s, "toks");
    var _pos = ds_map_find_value(_s, "pos");
    if (_pos >= ds_list_size(_toks)) return ds_list_find_value(_toks, ds_list_size(_toks) - 1);
    return ds_list_find_value(_toks, _pos);
