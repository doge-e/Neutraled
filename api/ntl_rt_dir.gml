/// ntl_rt_dir() —— 本进程统一使用的运行时目录前缀（末尾带 "/"）；都不可用时返回 ""
/// ★ 必须缓存：请求文件与 external-*.txt 标记要落在**同一个**目录里，中途换前缀会互相看不见。
///   只缓存成功；失败每 30 帧才重试一次（Step_1 每帧都查 external-running.txt，
///   不节流会在悬空联接上每帧做一整轮建目录+写探针）。
if (!variable_global_exists("ntl_frames")) global.ntl_frames = 0;
if (variable_global_exists("ntl_rt_prefix"))
{
    var _c = string(global.ntl_rt_prefix);
    if (string_length(_c) > 0) return _c;
}
if (variable_global_exists("ntl_rt_last_try") && (global.ntl_frames - real(global.ntl_rt_last_try)) < 30) return "";
global.ntl_rt_last_try = global.ntl_frames;
var _p = ntl_rt_repair();
if (_p != "") global.ntl_rt_prefix = _p;
return _p;