/// ntl_ns_load() —— 从 api-registry.json 构建命名空间映射
///  注意：一律用 variable_struct_get 读取 JSON 字段（struct.field 形式在 UTMT 编译下不可靠）
///  global.ntl_ns       : ds_map "ns.func" -> 实际脚本资源名
///  global.ntl_ns_const : ds_map "ns.CONST" -> 值
///  global.ntl_mods_info: 数组 [{id,name,author,ns}]

global.ntl_ns = ds_map_create();
global.ntl_ns_const = ds_map_create();
global.ntl_mods_info = [];

// * 优先读「运行时精简注册表」（只含 mods 段，~10 KB）。
//   完整版 api-registry.json 有 ~1 MB / 2 万行，而下面这句逐行拼接是 O(n^2)，
//   实测在真机上卡 19.6 秒（开机总耗时 22.5 秒的主因）。
var _path = program_directory + "Neutraled/ns-registry.json";
if (!file_exists(_path)) _path = program_directory + "Neutraled/api-registry.json";
if (!file_exists(_path))
{
    ntl_log("ns", "API 注册表不存在（命名空间调用不可用）: " + _path);
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

ntl_log("ns", "注册表文本: " + string(string_length(_txt)) + " 字符 / 源 " + _path);

var _j = undefined;
try { _j = json_parse(_txt); }
catch (e) { ntl_log("ns", "[错误] api-registry.json 解析失败"); return 0; }
if (_j == undefined || !is_struct(_j)) { ntl_log("ns", "[错误] api-registry.json 解析结果异常"); return 0; }

var _mods = variable_struct_get(_j, "mods");
if (_mods == undefined || !is_array(_mods))
{
    ntl_log("ns", "[错误] api-registry.json 缺少 mods 数组");
    return 0;
}

var _n = array_length(_mods);
var _fnCount = 0;
var _cCount = 0;

try
{
    for (var _i = 0; _i < _n; _i += 1)
    {
        var _m = _mods[_i];
        if (!is_struct(_m)) continue;

        var _ns = string(variable_struct_get(_m, "ns"));
        var _id = string(variable_struct_get(_m, "id"));

        var _info = ds_map_create();
        ds_map_add(_info, "id", _id);
        ds_map_add(_info, "name", string(variable_struct_get(_m, "name")));
        ds_map_add(_info, "author", string(variable_struct_get(_m, "author")));
        ds_map_add(_info, "ns", _ns);
        global.ntl_mods_info[_i] = _info;

        // 导出函数
        var _fns = variable_struct_get(_m, "functions");
        if (_fns == undefined || !is_array(_fns)) _fns = [];
        var _fc = array_length(_fns);
        for (var _k = 0; _k < _fc; _k += 1)
        {
            var _fn = _fns[_k];
            if (!is_struct(_fn)) continue;
            var _key = _ns + "." + string(variable_struct_get(_fn, "Name"));
            var _script = string(variable_struct_get(_fn, "script"));
            if (ds_map_exists(global.ntl_ns, _key)) ds_map_replace(global.ntl_ns, _key, _script);
            else ds_map_add(global.ntl_ns, _key, _script);
            _fnCount += 1;
        }

        // 导出常量
        var _cs = variable_struct_get(_m, "constants");
        if (_cs == undefined || !is_array(_cs)) _cs = [];
        var _cc = array_length(_cs);
        for (var _k2 = 0; _k2 < _cc; _k2 += 1)
        {
            var _c = _cs[_k2];
            if (!is_struct(_c)) continue;
            var _ckey = _ns + "." + string(variable_struct_get(_c, "Name"));
            var _cval = variable_struct_get(_c, "Value");
            if (ds_map_exists(global.ntl_ns_const, _ckey)) ds_map_replace(global.ntl_ns_const, _ckey, _cval);
            else ds_map_add(global.ntl_ns_const, _ckey, _cval);
            _cCount += 1;
        }
    }
}
catch (e3)
{
    var _m3 = "unknown";
    try { _m3 = string(e3.message); } catch (e4) { ntl_log("ns", "[ntl] ntl_ns_load.gml:95 读取异常消息失败: " + string(e4)); }
    ntl_log("ns", "[错误] 命名空间加载异常: " + _m3);
    return 0;
}

ntl_log("ns", "命名空间已加载: " + string(_n) + " 个 mod / " + string(_fnCount) + " 个导出函数 / " + string(_cCount) + " 个常量");
return _n;
