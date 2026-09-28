/// ntl_tf(key, v1, v2) —— 取文案并替换 {n} / {1} / {2} 占位符
var _s = ntl_t(argument[0]);
if (argument_count > 1) _s = string_replace_all(_s, "{n}", string(argument[1]));
if (argument_count > 2) _s = string_replace_all(_s, "{1}", string(argument[2]));
if (argument_count > 3) _s = string_replace_all(_s, "{2}", string(argument[3]));
if (argument_count > 4) _s = string_replace_all(_s, "{3}", string(argument[4]));
return _s;