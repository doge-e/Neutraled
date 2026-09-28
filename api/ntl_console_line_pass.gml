/// ntl_console_line_pass(line) —— 该行是否通过当前过滤器
if (!variable_global_exists("ntl_console_filter_mode")) return 1;
var _m = global.ntl_console_filter_mode;
if (_m == "all") return 1;
var _l = string(argument[0]);
if (_m == "error")
    return (string_pos("[错误]", _l) > 0 || string_pos("[Error]", _l) > 0 ||
            string_pos("[失败]", _l) > 0 || string_pos("[Failed]", _l) > 0);
if (_m == "warn")
    return (string_pos("[警告]", _l) > 0 || string_pos("[Warning]", _l) > 0 ||
            string_pos("[注意]", _l) > 0 || string_pos("[Note]", _l) > 0);
if (_m == "cmd")
    return (string_copy(_l, 1, 1) == ">");
return 1;
