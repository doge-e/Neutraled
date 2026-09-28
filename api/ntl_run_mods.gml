// ntl_run_mods() —— 依次执行各 mod 入口脚本
if (!variable_global_exists("ntl_mods")) return 0;
var _n = ds_list_size(global.ntl_mods);
var _ran = 0;
for (var _i = 0; _i < _n; _i++)
{
    var _rec = ds_list_find_value(global.ntl_mods, _i);
    if (is_undefined(_rec)) continue;
    var _entry = ds_map_find_value(_rec, "entry");
    if (is_undefined(_entry) || _entry == "") continue;

    ntl_log("mod", "load " + string(ds_map_find_value(_rec, "id")) + " -> " + _entry);

    var _sid = asset_get_index(_entry);
    if (_sid == -1)
    {
        ntl_log("mod", "entry script not found: " + _entry);
        continue;
    }
    script_execute(_sid);
    _ran += 1;
}
ntl_log("mod", "mods run: " + string(_ran) + "/" + string(_n));
return _ran;
