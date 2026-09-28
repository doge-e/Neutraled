// ntl_mod_register(id, name, version, entry_script_name) —— 由 builder 内联清单调用
var _id = string(argument[0]);
var _name = string(argument[1]);
var _ver = string(argument[2]);
var _entry = string(argument[3]);

if (!variable_global_exists("ntl_mods")) global.ntl_mods = ds_list_create();
if (!variable_global_exists("ntl_mod_ids")) global.ntl_mod_ids = ds_map_create();

if (ds_map_exists(global.ntl_mod_ids, _id)) return 0;

var _rec = ds_map_create();
ds_map_add(_rec, "id", _id);
ds_map_add(_rec, "name", _name);
ds_map_add(_rec, "version", _ver);
ds_map_add(_rec, "entry", _entry);
ds_list_add(global.ntl_mods, _rec);
ds_map_add(global.ntl_mod_ids, _id, true);
return 1;
