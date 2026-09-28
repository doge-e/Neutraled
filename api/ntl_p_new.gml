/// ntl_p_new —— 老式脚本（每个函数一个同名脚本资源）
var _toks = argument[0];
    var _s = ds_map_create();
    ds_map_add(_s, "toks", _toks);
    ds_map_add(_s, "pos", 0);
    ds_map_add(_s, "err", "");
    return _s;
