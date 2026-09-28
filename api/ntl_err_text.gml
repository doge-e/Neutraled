/// ntl_err_text(e) —— 把 try/catch 捕获到的异常变成一行可读文本
/// 背景：GMS2 的运行时异常是个结构体（message / longMessage / stacktrace / script / line），
///       直接 string(e) 会得到 8 行 JSON，控制台里既难读又白占缓冲区。
///       这里优先取 message，取不到再退回 string(e)，并把换行折成空格。
var _e = argument[0];
var _m = "";
try
{
    if (is_struct(_e) && variable_struct_exists(_e, "message")) _m = string(_e.message);
}
catch (e2) { _m = ""; }
if (_m == "")
{
    try { _m = string(_e); } catch (e3) { _m = "(无法读取的异常)"; }
}
_m = string_replace_all(_m, chr(13), " ");
_m = string_replace_all(_m, chr(10), " ");
while (string_pos("  ", _m) > 0) _m = string_replace_all(_m, "  ", " ");
_m = string_trim(_m);
if (_m == "") _m = "(空异常)";
return _m;
