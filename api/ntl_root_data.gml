/// ntl_root_data() —— 加载章节注册表到运行时状态
/// 全局：ntl_ch (数组，元素为 ds_map: id/order/name/kind/source/author/dir/mods/enabled)
///       ntl_ch_pages / ntl_ch_page / ntl_ch_sel / ntl_ch_search / ntl_ch_filtered

var _path = program_directory + "Neutraled/chapters.json";
global.ntl_ch = [];
global.ntl_ch_max = 0;
global.ntl_ch_page = 0;
global.ntl_ch_sel = 0;
global.ntl_ch_search = "";
global.ntl_ch_search_mode = 0;
global.ntl_ch_filtered = [];
global.ntl_ch_loaded = 0;

if (!file_exists(_path))
{
    ntl_log("root", "章节注册表不存在: " + _path);
    return 0;
}

var _txt = "";
var _f = file_text_open_read(_path);
while (!file_text_eof(_f))
{
    _txt += file_text_read_string(_f);
    file_text_readln(_f);
    if (!file_text_eof(_f)) _txt += chr(10);
}
file_text_close(_f);

var _j = undefined;
try { _j = json_parse(_txt); }
catch (e) { ntl_log("root", "[错误] chapters.json 解析失败"); return 0; }
if (_j == undefined) return 0;

var _arr = _j.chapters;
var _n = array_length(_arr);
for (var _i = 0; _i < _n; _i += 1)
{
    var _c = _arr[_i];
    var _m = ds_map_create();
    ds_map_add(_m, "id", string(_c.Id));
    ds_map_add(_m, "order", real(_c.Order));
    ds_map_add(_m, "name", string(_c.Name));
    ds_map_add(_m, "kind", string(_c.Kind));
    ds_map_add(_m, "source", string(_c.Source));
    ds_map_add(_m, "author", string(_c.Author));
    ds_map_add(_m, "dir", string(_c.Dir));
    ds_map_add(_m, "enabled", (_c.Enabled == true) ? 1 : 0);
    ds_map_add(_m, "note", string(_c.Note));
    // 外部引擎章节（Kind="external"）：选中它时退出游戏并启动这个程序
    ds_map_add(_m, "exe", string(_c.Exe));
    ds_map_add(_m, "args", string(_c.Args));
    ds_map_add(_m, "cwd", string(_c.Cwd));
    global.ntl_ch[_i] = _m;
    if (real(_c.Order) > global.ntl_ch_max) global.ntl_ch_max = real(_c.Order);
}

global.ntl_ch_loaded = 1;
ntl_log("root", "章节注册表已加载: " + string(_n) + " 条，最大章节 " + string(global.ntl_ch_max));
return _n;
