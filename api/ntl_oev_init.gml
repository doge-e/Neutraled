/// ntl_oev_init() —— 对象事件 Hook 的注册表初始化
/// 数据源：Neutraled/object-hooks.json（部署时由 builder 生成，含被包装的对象事件代码块名）
global.ntl_oev = ds_map_create();     // "objName:event" -> ds_list（hook 列表）
global.ntl_oev_on = 0;

var _path = program_directory + "Neutraled/object-hooks.json";
if (!file_exists(_path)) return 0;

var _f = file_text_open_read(_path);
var _txt = "";
while (!file_text_eof(_f))
{
    _txt += file_text_read_string(_f);
    file_text_readln(_f);
    if (!file_text_eof(_f)) _txt += chr(10);
}
file_text_close(_f);

var _j = undefined;
try { _j = json_parse(_txt); } catch (e) { return 0; }
if (_j == undefined) return 0;

var _items = variable_struct_get(_j, "hooks");
if (_items == undefined || !is_array(_items)) return 0;

var _n = array_length(_items);
for (var _i = 0; _i < _n; _i += 1)
{
    var _it = _items[_i];
    if (!is_struct(_it)) continue;
    var _key = string(variable_struct_get(_it, "object")) + ":" + string(variable_struct_get(_it, "event"));
    if (!ds_map_exists(global.ntl_oev, _key)) ds_map_add(global.ntl_oev, _key, ds_list_create());
    var _rec = ds_map_create();
    ds_map_add(_rec, "mode", string(variable_struct_get(_it, "mode")));
    ds_map_add(_rec, "handler", string(variable_struct_get(_it, "handler")));
    ds_map_add(_rec, "moddir", string(variable_struct_get(_it, "moddir")));
    ds_list_add(ds_map_find_value(global.ntl_oev, _key), _rec);
}
global.ntl_oev_on = 1;
ntl_log("oev", "对象事件 Hook 已注册: " + string(ds_map_size(global.ntl_oev)) + " 个事件点");
return 1;
