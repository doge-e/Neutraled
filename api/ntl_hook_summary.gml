/// ntl_hook_summary() —— 输出 hook 冲突摘要（哪些函数被多个 mod 改了）
if (!variable_global_exists("ntl_hooks")) { ntl_console_log(ntl_t("hook.none")); return 0; }
var _keys = ntl_dsmap_keys(global.ntl_hooks);
var _conflict = 0;
var _total = 0;
for (var _i = 0; _i < array_length(_keys); _i += 1)
{
    var _l = ds_map_find_value(global.ntl_hooks, _keys[_i]);
    var _n = ds_list_size(_l);
    _total += _n;
    if (_n > 1)
    {
        _conflict += 1;
        var _mods = "";
        for (var _j = 0; _j < _n; _j += 1)
        {
            var _h = ds_list_find_value(_l, _j);
            if (_j > 0) _mods += " + ";
            // ★ 函数 hook 槽的元素是 ds_map（api/ntl_hook_init.gml:68 用 ds_map_create）；
            //   事件订阅槽的元素是脚本索引(real) → 这里只认 ds_map，否则会把脚本索引当 map 用
            if (!is_real(_h) || !ds_exists(_h, ds_type_map) || !ds_map_exists(_h, "mode")) { _mods += ntl_t("hook.evsub"); continue; }
            _mods += string(ds_map_find_value(_h, "mod")) + "(" + string(ds_map_find_value(_h, "mode")) + ")";
        }
        ntl_console_log(ntl_ts("hook.multi", [_keys[_i], _mods]));
    }
}
ntl_console_log(ntl_ts("hook.sum", [_total, _conflict]));
if (_conflict > 0) ntl_console_log(ntl_t("hook.chain"));
return _conflict;
