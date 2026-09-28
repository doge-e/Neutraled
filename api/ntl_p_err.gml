/// ntl_p_err —— 老式脚本（每个函数一个同名脚本资源）
var _s = argument[0];
var _msg = argument[1];
    if (!is_real(_s) || !ds_exists(_s, ds_type_map)) return undefined;
    if (ds_map_find_value(_s, "err") == "")
    {
        var _t = ntl_p_peek(_s);
        ds_map_replace(_s, "err", _msg + " (line " + string(ds_map_find_value(_t, "line")) + ")");
    }
