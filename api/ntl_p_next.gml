/// ntl_p_next —— 老式脚本（每个函数一个同名脚本资源）
var _s = argument[0];
    if (!is_real(_s) || !ds_exists(_s, ds_type_map)) return undefined;
    var _t = ntl_p_peek(_s);
    ds_map_replace(_s, "pos", ds_map_find_value(_s, "pos") + 1);
    return _t;
