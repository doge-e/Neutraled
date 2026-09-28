/// ntl_map_load(id) —— 加载 Kristal 转换后的地图（整图 PNG + 数据 JSON）
/// 数据位置：Neutraled/kristal-maps/<id>.png 与 <id>.map.json
/// 返回 ds_map：{ ok, id, sprite, width, height, tileW, tileH, collision[], groups[], props }
if (!variable_global_exists("ntl_maps")) global.ntl_maps = ds_map_create();
var _id = string(argument[0]);

if (ds_map_exists(global.ntl_maps, _id)) return ds_map_find_value(global.ntl_maps, _id);

var _out = ds_map_create();
ds_map_add(_out, "ok", 0);
ds_map_add(_out, "id", _id);

var _dir = program_directory + "Neutraled/kristal-maps/";
var _png = _dir + _id + ".png";
var _json = _dir + _id + ".map.json";

if (!file_exists(_png) || !file_exists(_json))
{
    ntl_log("map", "[错误] 地图不存在: " + _id);
    ds_map_add(global.ntl_maps, _id, _out);
    return _out;
}

// 读 JSON
var _txt = ntl_live_file_read(_json);
var _j = undefined;
try { _j = json_parse(_txt); } catch (e) { ntl_log("map", "[错误] JSON 解析失败: " + _id); }
if (_j == undefined)
{
    ds_map_add(global.ntl_maps, _id, _out);
    return _out;
}

// 动态载入 PNG 为精灵
var _spr = -1;
try { _spr = sprite_add(_png, 1, false, false, 0, 0); } catch (e) { _spr = -1; }
if (_spr < 0)
{
    ntl_log("map", "[错误] 无法载入地图图: " + _png);
    ds_map_add(global.ntl_maps, _id, _out);
    return _out;
}

var _w = variable_struct_get(_j, "width");
var _h = variable_struct_get(_j, "height");

ds_map_replace(_out, "ok", 1);
ds_map_add(_out, "sprite", _spr);
ds_map_add(_out, "width", _w);
ds_map_add(_out, "height", _h);
ds_map_add(_out, "tileW", variable_struct_get(_j, "tileWidth"));
ds_map_add(_out, "tileH", variable_struct_get(_j, "tileHeight"));

// 碰撞矩形
var _coll = [];
var _ca = variable_struct_get(_j, "collision");
if (is_array(_ca))
{
    for (var _i = 0; _i < array_length(_ca); _i += 1)
    {
        var _c = _ca[_i];
        array_push(_coll, [
            variable_struct_get(_c, "x"), variable_struct_get(_c, "y"),
            variable_struct_get(_c, "w"), variable_struct_get(_c, "h")
        ]);
    }
}
ds_map_add(_out, "collision", _coll);

// 对象组（markers / objects 等）
var _groups = ds_map_create();
var _ga = variable_struct_get(_j, "objectGroups");
if (is_array(_ga))
{
    for (var _g = 0; _g < array_length(_ga); _g += 1)
    {
        var _grp = _ga[_g];
        var _gname = variable_struct_get(_grp, "name");
        var _objs = variable_struct_get(_grp, "objects");
        var _list = [];
        if (is_array(_objs))
        {
            for (var _o = 0; _o < array_length(_objs); _o += 1)
            {
                var _ob = _objs[_o];
                var _m = ds_map_create();
                ds_map_add(_m, "name", variable_struct_get(_ob, "name"));
                ds_map_add(_m, "type", variable_struct_get(_ob, "type"));
                ds_map_add(_m, "x", variable_struct_get(_ob, "x"));
                ds_map_add(_m, "y", variable_struct_get(_ob, "y"));
                ds_map_add(_m, "w", variable_struct_get(_ob, "w"));
                ds_map_add(_m, "h", variable_struct_get(_ob, "h"));
                ds_map_add(_m, "point", variable_struct_get(_ob, "point"));
                var _p = variable_struct_get(_ob, "props");
                if (is_struct(_p))
                {
                    var _keys = variable_struct_get_names(_p);
                    for (var _k = 0; _k < array_length(_keys); _k += 1)
                        ds_map_add(_m, "prop:" + _keys[_k], variable_struct_get(_p, _keys[_k]));
                }
                array_push(_list, _m);
            }
        }
        ds_map_add(_groups, _gname, _list);
    }
}
ds_map_add(_out, "groups", _groups);

// 地图属性（music 等）
var _props = ds_map_create();
var _pa = variable_struct_get(_j, "properties");
if (is_struct(_pa))
{
    var _pks = variable_struct_get_names(_pa);
    for (var _pk = 0; _pk < array_length(_pks); _pk += 1)
        ds_map_add(_props, _pks[_pk], variable_struct_get(_pa, _pks[_pk]));
}
ds_map_add(_out, "props", _props);

ntl_log("map", "已加载: " + _id + " (" + string(_w) + "x" + string(_h) + ")");
ds_map_add(global.ntl_maps, _id, _out);
return _out;
