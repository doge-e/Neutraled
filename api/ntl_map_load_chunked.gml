/// ntl_map_load_chunked(id) —— 分块加载大地图（避免一次性把整图塞进显存）
///
/// 原理：把地图按 1024x1024 切成若干块，只加载当前视野附近的块。
/// 对于 2400x1120 这类地图收益不大，但对 8000x8000 的超大地图是必需的。
///
/// 返回 ds_map（与 ntl_map_load 兼容，多一个 "chunks" 字段）
var _id = string(argument[0]);
var _mp = ntl_map_load(_id);
if (ds_map_find_value(_mp, "ok") != 1) return _mp;

var _w = ds_map_find_value(_mp, "width");
var _h = ds_map_find_value(_mp, "height");
var _chunk = 1024;

// 小地图直接整张加载
if (_w <= _chunk * 2 && _h <= _chunk * 2) return _mp;

// 大地图：登记为"分块模式"，绘制时按需加载
var _cols = ceil(_w / _chunk);
var _rows = ceil(_h / _chunk);
if (!variable_global_exists("ntl_map_chunks")) global.ntl_map_chunks = ds_map_create();

var _info = ds_map_create();
ds_map_add(_info, "id", _id);
ds_map_add(_info, "cols", _cols);
ds_map_add(_info, "rows", _rows);
ds_map_add(_info, "chunkSize", _chunk);
ds_map_add(_info, "loaded", ds_map_create());   // "c,r" -> sprite
ds_map_add(_info, "png", ds_map_find_value(_mp, "pngPath"));
ds_map_add(_info, "full", ds_map_find_value(_mp, "sprite"));

ds_map_replace(global.ntl_map_chunks, _id, _info);
ntl_log("map", "[分块] " + _id + " " + string(_w) + "x" + string(_h) +
        " → " + string(_cols) + "x" + string(_rows) + " 块 (" + string(_chunk) + "px/块)");
return _mp;
