/// ntl_console_binds_check() —— 每帧检查按键绑定（控制台打开时）
if (!variable_global_exists("ntl_console_binds")) return 0;
if (!global.ntl_console_open) return 0;
var _keys = ntl_dsmap_keys(global.ntl_console_binds);
for (var _i = 0; _i < array_length(_keys); _i += 1)
{
    var _kn = _keys[_i];
    var _vk = ntl_console_key_vk(_kn);
    if (_vk <= 0) continue;
    if (keyboard_check_pressed(_vk) || keyboard_check_direct(_vk))
    {
        ntl_console_exec(string(ds_map_find_value(global.ntl_console_binds, _kn)));
    }
}
return 0;
