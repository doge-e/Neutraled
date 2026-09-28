/// ntl_sprite_from_file(path) —— 运行时从 PNG 文件创建精灵（无需重新部署）
/// 返回 sprite 索引，失败返回 -1
var _p = string(argument[0]);
if (!file_exists(_p))
{
    ntl_log("res", "[错误] 文件不存在: " + _p);
    return -1;
}
var _idx = -1;
try { _idx = sprite_add(_p, 1, false, false, 0, 0); }
catch (e) { ntl_log("res", "[错误] sprite_add 失败: " + string(e)); return -1; }
if (_idx >= 0)
{
    ntl_log("res", "已载入精灵 " + _p + " -> index " + string(_idx));
    try { ntl_res_track("sprite", _idx, _p); } catch (e) { ntl_log("res", "[ntl] ntl_sprite_from_file.gml:15 ntl_res_track 失败: " + string(e)); }   // ★ F5：登记以便泄漏检测
}
return _idx;
