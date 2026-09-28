/// ntl_mod_save(mod, slot) —— mod 独立存档槽
var _mod = "unknown";
if (argument_count > 0) _mod = string(argument[0]);
var _slot = "auto";
if (argument_count > 1) _slot = string(argument[1]);
if (_mod == "") _mod = "unknown";

var _dir = program_directory + "Neutraled/mod-saves/" + _mod + "/";
var _path = _dir + _slot + ".json";
if (!directory_exists(_dir)) directory_create(_dir);

var _sb = "{";
var _cnt = 0;
if (variable_global_exists("ntl_mod_data"))
{
    var _keys = ntl_dsmap_keys(global.ntl_mod_data);
    for (var _i = 0; _i < array_length(_keys); _i += 1)
    {
        var _k = string(_keys[_i]);
        if (string_pos(_mod + ":", _k) != 1 && string_pos(_mod + ".", _k) != 1) continue;
        if (_cnt > 0) _sb += ",";
        var _v = ds_map_find_value(global.ntl_mod_data, _k);
        _sb += ntl_json_esc(_k) + ":";
        if (is_real(_v)) _sb += string(_v);
        else if (is_bool(_v)) _sb += (_v ? "true" : "false");
        else if (is_string(_v)) _sb += ntl_json_esc(_v);
        else _sb += "null";
        _cnt += 1;
    }
}
_sb += "}";

var _written = 0;
try
{
    ntl_ensure_dir(_path);
    var _f = file_text_open_write(_path);
    file_text_write_string(_f, _sb);
    file_text_close(_f);
    _written = 1;
}
catch (e) { ntl_log("mod", "[存档] 写入异常: " + string(e)); }

ntl_log("mod", "[存档] " + _mod + "/" + _slot + " 保存 " + string(_cnt) + " 项，" + ((_written == 1) ? "成功" : "失败"));
return (_written * 1000) + _cnt;