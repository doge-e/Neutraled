/// ntl_perf_record(modName, ms) —— 记录一次 mod 的执行耗时
/// 用于 profile 命令的"逐 mod 耗时排行"
if (!variable_global_exists("ntl_perf_calls")) global.ntl_perf_calls = ds_map_create();
var _m = string(argument[0]);
var _ms = real(argument[1]);

var _rec = undefined;
if (ds_map_exists(global.ntl_perf_calls, _m)) _rec = ds_map_find_value(global.ntl_perf_calls, _m);
else
{
    _rec = ds_map_create();
    ds_map_add(_rec, "calls", 0);
    ds_map_add(_rec, "total", 0);
    ds_map_add(_rec, "max", 0);
    ds_map_add(global.ntl_perf_calls, _m, _rec);
}

ds_map_replace(_rec, "calls", ds_map_find_value(_rec, "calls") + 1);
ds_map_replace(_rec, "total", ds_map_find_value(_rec, "total") + _ms);
if (_ms > ds_map_find_value(_rec, "max")) ds_map_replace(_rec, "max", _ms);
return 1;
