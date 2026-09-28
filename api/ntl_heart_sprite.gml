/// ntl_heart_sprite() —— 官方二级菜单里那个「选中红心」的精灵索引。
/// ★ 为什么必须按名字取、不能硬编码索引：**精灵索引每个章节都不一样** ——
///   chapter4 = 3695、chapter1 = 922（对照表见 docs/MANAGE.md §11.4）。
///   硬编码 3695 的版本在 chapter1 真机弹过（2026-09-27，用户 m12373 报错）：
///     ERROR in action number 1 of Draw Event for object obj_darkcontroller:
///     Trying to draw non-existing sprite.
///     at gml_Script_ntl_modmenu_page_draw
///   同一类坑的既有先例：ntl_font_big.gml / ntl_font_main.gml 也是按名字取字体
///   （主字体索引随语言变化，日文语境下 mainbig 会换成 fnt_ja_mainbig）。
/// 返回 -1 = 一个都没找到（调用方必须能不吃 sprite 地画下去）。
var _cands = ["spr_heart", "spr_heartsmall", "spr_heart_centered", "spr_heart_outline2"];
var _hit = -1;
for (var _i = 0; _i < array_length(_cands); _i += 1)
{
    var _s = asset_get_index(_cands[_i]);
    if (_s != -1 && sprite_exists(_s)) { _hit = _s; break; }
}

// 一次性诊断（写进 dr-api.log）：把「名字 -> 索引」的映射留档，
// 下次哪个章节索引又不一样时，日志里直接能看到本章用的是几号。
if (!variable_global_exists("ntl_heart_probe"))
{
    global.ntl_heart_probe = 1;
    try
    {
        var _d = "";
        for (var _j = 0; _j < array_length(_cands); _j += 1)
        {
            var _q = asset_get_index(_cands[_j]);
            if (_q != -1 && sprite_exists(_q))
                _d += _cands[_j] + "=" + string(_q) + "(" + string(sprite_get_width(_q)) + "x" + string(sprite_get_height(_q)) + ") ";
            else
                _d += _cands[_j] + "=(无) ";
        }
        var _nm = (_hit != -1) ? sprite_get_name(_hit) : "(无)";
        // 顺带把本帧的帧率留档：60 FPS 差异层（mods/60fps_layer）生效时 room_speed 应为 60。
        ntl_log("ui", "[面板] 红心精灵解析: " + _d + "⇒ 选用 index=" + string(_hit) + " name=" + _nm
            + " room_speed=" + string(room_speed) + " fps=" + string(fps) + " fps_real=" + string(fps_real));
    }
    catch (_e) { ntl_log("ui", "[面板] 红心精灵解析探针异常: " + string(_e)); }
}
return _hit;
