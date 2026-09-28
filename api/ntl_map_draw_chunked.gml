/// ntl_map_draw_chunked(map, camX, camY, viewW, viewH) —— 分块绘制（只加载视野内的块）
var _mp = argument[0];
var _cx = real(argument[1]);
var _cy = real(argument[2]);
var _vw = (argument_count > 3) ? real(argument[3]) : room_width;
var _vh = (argument_count > 4) ? real(argument[4]) : room_height;

var _id = ds_map_find_value(_mp, "id");
if (!variable_global_exists("ntl_map_chunks") || !ds_map_exists(global.ntl_map_chunks, _id))
{
    // 非分块模式：整图绘制
    return ntl_map_draw(_mp, _cx, _cy);
}

var _info = ds_map_find_value(global.ntl_map_chunks, _id);
if (!is_real(_info) || !ds_exists(_info, ds_type_map)) return 0;
var _size = ds_map_find_value(_info, "chunkSize");
var _cols = ds_map_find_value(_info, "cols");
var _rows = ds_map_find_value(_info, "rows");
var _loaded = ds_map_find_value(_info, "loaded");
var _png = ds_map_find_value(_info, "png");

// 视野覆盖的块范围（多留 1 块余量）
var _c0 = max(0, floor(_cx / _size) - 1);
var _c1 = min(_cols - 1, floor((_cx + _vw) / _size) + 1);
var _r0 = max(0, floor(_cy / _size) - 1);
var _r1 = min(_rows - 1, floor((_cy + _vh) / _size) + 1);

for (var _r = _r0; _r <= _r1; _r += 1)
{
    for (var _c = _c0; _c <= _c1; _c += 1)
    {
        var _key = string(_c) + "," + string(_r);
        if (!ds_map_exists(_loaded, _key))
        {
            // 从整图裁出这一块
            var _full = ds_map_find_value(_info, "full");
            if (_full == undefined || _full < 0) continue;
            var _sx = _c * _size;
            var _sy = _r * _size;
            var _sw = min(_size, ds_map_find_value(_mp, "width") - _sx);
            var _sh = min(_size, ds_map_find_value(_mp, "height") - _sy);
            if (_sw <= 0 || _sh <= 0) continue;

            // sprite_add 不支持裁切，这里用 surface 复制
            var _surf = surface_create(_sw, _sh);
            surface_set_target(_surf);
            draw_clear_alpha(c_black, 0);
            draw_sprite_part(_full, 0, _sx, _sy, _sw, _sh, 0, 0);
            surface_reset_target();
            var _chunkSprite = sprite_create_from_surface(_surf, 0, 0, _sw, _sh, false, false, 0, 0);
            surface_free(_surf);
            ds_map_add(_loaded, _key, _chunkSprite);
        }

        var _spr = ds_map_find_value(_loaded, _key);
        draw_sprite(_spr, 0, _c * _size - _cx, _r * _size - _cy);
    }
}
return 0;
