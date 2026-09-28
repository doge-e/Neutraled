/// ntl_p_is —— 老式脚本（每个函数一个同名脚本资源）
var _s = argument[0];
var _type = argument[1];
var _value = argument[2];
    var _t = ntl_p_peek(_s);
    if (ds_map_find_value(_t, "t") != _type) return false;
    if (_value != undefined && _value != "") return (ds_map_find_value(_t, "v") == _value);
    return true;
