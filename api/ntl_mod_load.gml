/// ntl_mod_load(mod, slot) —— 从独立存档槽读回
var _mod = "unknown";
if (argument_count > 0) _mod = string(argument[0]);
var _slot = "auto";
if (argument_count > 1) _slot = string(argument[1]);
if (_mod == "") _mod = "unknown";

var _path = program_directory + "Neutraled/mod-saves/" + _mod + "/" + _slot + ".json";
if (!file_exists(_path)) { ntl_log("mod", "[存档] 文件不存在: " + _path); return 0; }

if (!variable_global_exists("ntl_mod_data")) global.ntl_mod_data = ds_map_create();

var _txt = "";
try
{
    var _f = file_text_open_read(_path);
    while (!file_text_eof(_f))
    {
        _txt += file_text_read_string(_f);
        file_text_readln(_f);
    }
    file_text_close(_f);
}
catch (e) { ntl_log("mod", "[存档] 读取失败: " + string(e)); return 0; }

if (string_length(_txt) <= 2) return 0;

var _j = undefined;
try { _j = json_parse(_txt); } catch (e) { ntl_log("mod", "[存档] 解析失败"); return 0; }
if (_j == undefined) return 0;

var _keys = variable_struct_get_names(_j);
for (var _i = 0; _i < array_length(_keys); _i += 1)
{
    var _k = _keys[_i];
    var _v = variable_struct_get(_j, _k);
    if (ds_map_exists(global.ntl_mod_data, _k)) ds_map_replace(global.ntl_mod_data, _k, _v);
    else ds_map_add(global.ntl_mod_data, _k, _v);
}
ntl_log("mod", "[存档] " + _mod + "/" + _slot + " 读取 " + string(array_length(_keys)) + " 项");
return array_length(_keys);
