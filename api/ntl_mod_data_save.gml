/// ntl_mod_data_save() —— 把 mod 共享数据存盘
if (!variable_global_exists("ntl_mod_data")) return 0;
if (!variable_global_exists("ntl_mod_data_dirty") || global.ntl_mod_data_dirty != 1) return 0;

var _path = program_directory + "Neutraled/mods-shared.json";
var _keys = ntl_dsmap_keys(global.ntl_mod_data);
var _sb = "{";
for (var _i = 0; _i < array_length(_keys); _i += 1)
{
    var _k = string(_keys[_i]);
    var _v = ds_map_find_value(global.ntl_mod_data, _k);
    if (_i > 0) _sb += ",";
    _sb += ntl_json_esc(_k) + ":";
    if (is_real(_v)) _sb += string(_v);
    else if (is_bool(_v)) _sb += (_v ? "true" : "false");
    else if (is_string(_v)) _sb += ntl_json_esc(_v);
    else _sb += "null";
}
_sb += "}";

try
{
// 确保目录存在（GM 不会自动建目录，file_text_open_write 会静默失败）
ntl_ensure_dir(_path);
    var _f = file_text_open_write(_path);
    file_text_write_string(_f, _sb);
    file_text_close(_f);
    global.ntl_mod_data_dirty = 0;
    return 1;
}
catch (e) { ntl_log("mod", "[共享数据] 存盘失败: " + string(e)); return 0; }