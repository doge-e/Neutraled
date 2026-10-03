/// ntl_console_api(keyword) —— 在 API 注册表里搜索函数/对象/资源
/// 数据源：Neutraled/api-registry.json（部署时生成，含游戏原有的 711 函数 / 1577 对象 / 5888 精灵 等）
var _kw = string_lower(string(argument[0]));
if (string_length(_kw) <= 0)
{
    ntl_console_log(ntl_t("api.u"));
    return 0;
}

// 懒加载注册表
if (!variable_global_exists("ntl_api_reg"))
{
    global.ntl_api_reg = undefined;
    var _p = program_directory + "Neutraled/api-registry.json";
    if (file_exists(_p))
    {
        // 一次读完再交给 json_parse（原来多了一次无意义的 file_text_open_read，且把文件句柄当字符串判断）
        try {
            var _f = file_text_open_read(_p);
            var _s = "";
            while (!file_text_eof(_f)) { _s += file_text_read_string(_f); file_text_readln(_f); }
            file_text_close(_f);
            global.ntl_api_reg = json_parse(_s);
        } catch (e2) { global.ntl_api_reg = undefined; }
    }
    if (global.ntl_api_reg == undefined)
    {
        ntl_console_log(ntl_t("api.noreg"));
        global.ntl_api_reg = -1;
        return 0;
    }
}
if (global.ntl_api_reg == -1) { ntl_console_log(ntl_t("api.unavail")); return 0; }

var _reg = global.ntl_api_reg;
var _orig = variable_struct_get(_reg, "original");
if (_orig == undefined) { ntl_console_log(ntl_t("api.badfmt")); return 0; }

// ★ 用户主诉修复：不再限制条数 —— 命中多少条就打多少条（原来只打印前 25 条 + 一条"还有 N 条"的尾巴）。
//   _found 现在恒等于 _all，保留变量只是为了尾部文案的兼容。
var _found = 0;
var _all = 0;
var _cats = ["functions", "objects", "rooms", "sprites", "sounds"];
for (var _ci = 0; _ci < array_length(_cats); _ci += 1)
{
    var _cat = _cats[_ci];
    var _list = variable_struct_get(_orig, _cat);
    if (_list == undefined) continue;
    var _n = array_length(_list);
    for (var _i = 0; _i < _n; _i += 1)
    {
        var _nm = string(_list[_i]);
        if (string_pos(_kw, string_lower(_nm)) <= 0) continue;
        _all += 1;
        _found += 1;
        ntl_console_log("  [" + string_copy(_cat, 1, string_length(_cat) - 1) + "] " + _nm);
    }
}

if (_all == 0) ntl_console_log(ntl_ts("api.nomatch", [_kw]));
else
{
    ntl_console_log(ntl_ts("api.found", [string(_found)]));
    if (_all > _found) ntl_console_log(ntl_ts("api.more", [string(_all - _found)]));
}
return 0;
