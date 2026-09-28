/// ntl_mod_data_load() —— 从磁盘恢复 mod 共享数据
var _path = program_directory + "Neutraled/mods-shared.json";
if (!file_exists(_path)) return 0;
if (!variable_global_exists("ntl_mod_data")) global.ntl_mod_data = ds_map_create();
var _txt = "";
try
{
    var _f = file_text_open_read(_path);
    while (!file_text_eof(_f)) { _txt += file_text_read_string(_f); file_text_readln(_f); }
    file_text_close(_f);
}
catch (e) { return 0; }
if (string_length(_txt) <= 2) return 0;

var _j = undefined;
try { _j = json_parse(_txt); } catch (e) { return 0; }
if (_j == undefined) return 0;

var _keys = variable_struct_get_names(_j);
for (var _i = 0; _i < array_length(_keys); _i += 1)
{
    var _k = _keys[_i];
    var _v = variable_struct_get(_j, _k);
    if (ds_map_exists(global.ntl_mod_data, _k)) ds_map_replace(global.ntl_mod_data, _k, _v);
    else ds_map_add(global.ntl_mod_data, _k, _v);
}
ntl_log("mod", "[共享数据] 已恢复 " + string(array_length(_keys)) + " 项");
return array_length(_keys);
