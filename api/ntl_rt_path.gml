/// ntl_rt_path(name) —— 运行时文件的路径（自动带本进程统一的运行时目录前缀）
/// 前缀不可用时退回相对路径（此时写入本身也会失败；需要精确判定请先看 ntl_rt_dir()）
var _nm = string(argument[0]);
var _p = ntl_rt_dir();
if (_p == "") return "Neutraled/" + _nm;
return _p + _nm;