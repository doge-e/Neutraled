/// ntl_keynorm(s) —— 匹配用归一化：转小写、只保留 a-z0-9（"Deltarune 60 FPS" → "deltarune60fps"）
/// 用途：把 mods/ 目录名（ForestPatch、TimelineForest、deltarune_60_fps）
/// 对上产物 mods.json 里的 Name/Id 段（"Forest Patch"、"test.timelineforest"、"Deltarune 60 FPS"）——
/// 空格、下划线、连字符、点、大小写的差异全部抹平。非 ASCII（中文目录名）会被丢弃，不会误匹配。
var _s = string_lower(string(argument[0]));
var _o = "";
for (var _i = 1; _i <= string_length(_s); _i += 1)
{
    var _c = string_char_at(_s, _i);
    if (string_pos(_c, "abcdefghijklmnopqrstuvwxyz0123456789") > 0) _o += _c;
}
return _o;
