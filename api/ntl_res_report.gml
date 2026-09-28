/// ntl_res_report() —— 资源使用报告（泄漏检测）
/// ★ F5 接线：报告是用户主动敲 res 触发的 → 全部 force=1，不受 filter 过滤器影响
if (!variable_global_exists("ntl_res_tracked"))
{
    ntl_console_log(ntl_t("res.off"), 1);
    return 0;
}
var _kinds = ntl_dsmap_keys(global.ntl_res_tracked);
var _total = 0;
for (var _i = 0; _i < array_length(_kinds); _i += 1)
{
    var _k = string(_kinds[_i]);
    var _m = ds_map_find_value(global.ntl_res_tracked, _k);
    if (!is_real(_m) || !ds_exists(_m, ds_type_map)) continue;
    var _n = ds_map_size(_m);
    _total += _n;
    ntl_console_log(ntl_ts("res.line", [_k, _n]), 1);
    if (_n > 200) ntl_console_log(ntl_t("res.many"), 1);
}
if (_total == 0) ntl_console_log(ntl_t("res.none"), 1);
return _total;
