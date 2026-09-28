/// ntl_perf_test() —— 性能基准：对比 GML 原生与 Lua 解释器的开销
/// 结果写入日志（[perf] 前缀）供外部对比
ntl_log("perf", "===== 性能基准开始 =====");

// --- 1) GML 原生：10 万次整数累加 ---
var _t0 = current_time;
var _sum = 0;
for (var _i = 1; _i <= 100000; _i += 1) _sum += _i;
var _gmlAdd = current_time - _t0;

// --- 2) GML 原生：10 万次函数调用 ---
var _t1 = current_time;
for (var _j = 1; _j <= 100000; _j += 1) { var _x = ntl_perf_noop(); }
var _gmlCall = current_time - _t1;

// --- 3) GML 原生：1 万次字符串拼接 ---
var _t2 = current_time;
var _s = "";
for (var _k = 1; _k <= 10000; _k += 1) _s = string(_k);
var _gmlStr = current_time - _t2;

ntl_log("perf", "GML 原生: 10万次累加=" + string(_gmlAdd) + "ms  10万次调用=" + string(_gmlCall) +
                "ms  1万次字符串=" + string(_gmlStr) + "ms");

// --- 4) Lua 解释器：同类操作（由 ntl_perf_lua 脚本执行后回报） ---
global.ntl_perf_gml_add = _gmlAdd;
global.ntl_perf_gml_call = _gmlCall;
global.ntl_perf_gml_str = _gmlStr;
global.ntl_perf_ready = 1;
global.ntl_perf_t = current_time;   // 供 Lua 侧读取的时间源（普通全局变量）
ntl_log("perf", "GML 基准完成，等待 Lua 基准...");
return 1;
