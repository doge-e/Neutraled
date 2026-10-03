/// ntl_rt_retry() —— 重试挂起的启动请求：再修一次运行时目录，能写就落盘
/// 返回 1 = 已写出（调用方接回正常 pending 流程）
///      0 = 还没修好，继续等
///      2 = 超过上限（900 帧 ≈15 秒）仍不可写，放弃，交给调用方兜底提示
if (!variable_global_exists("ntl_rt_pending_txt")) return 0;
var _txt = string(global.ntl_rt_pending_txt);
if (string_length(_txt) <= 0) return 0;
if (!variable_global_exists("ntl_rt_retry_frame")) global.ntl_rt_retry_frame = global.ntl_frames;
var _waited = global.ntl_frames - real(global.ntl_rt_retry_frame);
if (_waited >= 900) return 2;

// 允许重新探测（ntl_rt_dir 成功后会缓存；这里清掉缓存 + 解除节流）
global.ntl_rt_prefix = "";
global.ntl_rt_last_try = -9999;
var _pfx = ntl_rt_dir();
if (_pfx == "") return 0;

var _req = _pfx + "launch-request.json";
ntl_ensure_dir(_req);
try { if (file_exists(_req)) file_delete(_req); } catch (e_rtr0) { }
var _ok = 0;
try
{
    var _f = file_text_open_write(_req);
    if (_f != -1) { file_text_write_string(_f, _txt); file_text_close(_f); _ok = 1; }
}
catch (e_rtr) { ntl_log("ext", "[错误] 补写启动请求失败: " + string(e_rtr)); }
if (_ok != 1 || !file_exists(_req)) return 0;

// 接回正常 pending 流程：请求确实落盘了，门控那边凭「存在 -> 不存在」确认被守候进程消费
global.ntl_ext_req_frame = global.ntl_frames;
global.ntl_ext_confirmed = 0;
global.ntl_ext_park_seen = 0;
global.ntl_ext_req_seen = 1;
ntl_rt_stash_clear();
ntl_log("rt", "[修复] 运行时目录已自动修复，启动请求已补写: " + _req + "（挂起 " + string(_waited) + " 帧后）");
return 1;