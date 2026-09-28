/// ntl_sprite_replace(targetSpriteName, pngPath) —— 运行时替换某个精灵的图像
/// 让 mod 可以"换皮"而不需要重新部署
var _target = string(argument[0]);
var _png = string(argument[1]);

var _tidx = asset_get_index(_target);
if (_tidx < 0 || !sprite_exists(_tidx))
{
    ntl_log("res", "[错误] 目标精灵不存在: " + _target);
    return 0;
}
var _newIdx = ntl_sprite_from_file(_png);
if (_newIdx < 0) return 0;

// 把新图的帧复制到目标精灵上（保留原精灵的索引，所有引用它的对象自动换皮）
var _w = sprite_get_width(_newIdx);
var _h = sprite_get_height(_newIdx);
var _frames = sprite_get_number(_newIdx);

try
{
    // GM 运行时不支持直接改精灵，但可以给实例换 sprite_index
    // 这里采用"登记替换表"方案：提供 ntl_sprite_resolve 让对象查询替换后的精灵
    if (!variable_global_exists("ntl_sprite_swap")) global.ntl_sprite_swap = ds_map_create();
    ds_map_replace(global.ntl_sprite_swap, string(_tidx), _newIdx);
    ntl_log("res", "已登记精灵替换 " + _target + " -> " + _png + " (" + string(_w) + "x" + string(_h) + ", " + string(_frames) + " 帧)");
    return _newIdx;
}
catch (e) { ntl_log("res", "[错误] " + string(e)); return 0; }
