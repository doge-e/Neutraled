/// ntl_map_verify_all() —— 批量验证所有转换后的地图
/// 逐张加载并检查：尺寸/碰撞块数/对象组数，输出到日志
ntl_log("mapverify", "[地图验证] 函数被调用");
if (!variable_global_exists("ntl_maps")) global.ntl_maps = ds_map_create();

var _dir = program_directory + "Neutraled/kristal-maps/";
ntl_log("mapverify", "[地图验证] 目录=" + _dir + " 存在=" + string(directory_exists(_dir)));
if (!directory_exists(_dir))
{
    ntl_log("mapverify", "[错误] 地图目录不存在: " + _dir);
    return 0;
}

// 枚举 .map.json
var _files = [];
var _f = file_find_first(_dir + "*.map.json", 0);
while (_f != "" && _f != -1)
{
    array_push(_files, _f);
    _f = file_find_next();
}
file_find_close();

var _n = array_length(_files);
ntl_log("mapverify", "[地图验证] 共 " + string(_n) + " 张地图");

var _ok = 0;
var _fail = 0;
var _totalColl = 0;
var _totalObjs = 0;
var _bad = [];
var _i = 0;
while (_i < _n)
{
    var _fn = _files[_i];
    var _id = string_replace(_fn, ".map.json", "");
    _i += 1;

    var _mp = ntl_map_load(_id);
    if (ds_map_find_value(_mp, "ok") != 1)
    {
        _fail += 1;
        if (array_length(_bad) < 10) array_push(_bad, _id);
        continue;
    }

    var _w = ds_map_find_value(_mp, "width");
    var _h = ds_map_find_value(_mp, "height");
    var _coll = ds_map_find_value(_mp, "collision");
    var _groups = ds_map_find_value(_mp, "groups");
    var _cc = is_array(_coll) ? array_length(_coll) : 0;
    var _gc = (is_real(_groups) && ds_exists(_groups, ds_type_map)) ? ds_map_size(_groups) : 0;

    // 异常检测：尺寸为 0 或过大
    if (_w <= 0 || _h <= 0 || _w > 20000 || _h > 20000)
    {
        _fail += 1;
        if (array_length(_bad) < 10) array_push(_bad, _id + "(尺寸异常 " + string(_w) + "x" + string(_h) + ")");
        continue;
    }

    _ok += 1;
    _totalColl += _cc;
    _totalObjs += _gc;
}

ntl_log("mapverify", "[地图验证] 成功 " + string(_ok) + " / 失败 " + string(_fail) +
        "  碰撞块合计 " + string(_totalColl) + "  对象组合计 " + string(_totalObjs));
if (array_length(_bad) > 0)
{
    for (var _b = 0; _b < array_length(_bad); _b += 1)
        ntl_log("mapverify", "  异常: " + _bad[_b]);
}
return _ok;
