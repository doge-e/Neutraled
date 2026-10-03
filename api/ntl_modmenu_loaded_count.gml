/// ntl_modmenu_loaded_count() —— 本产物**已加载**的 mod 数。
/// 返回 -1 = 本产物没有 Neutraled/mods.json（老产物 / 外部章节 exe）⇒ 调用方回退显示"已安装"数。
/// 每帧都可能被入口行调用，所以缓存（面板打开时由 ntl_modmenu_open() 清掉重读）。
/// ★ 2026-10-02（用户反馈「显示 0 mod 加载」）：旧实现 = file_exists 就 array_length(loaded())，
///   清单解析不出来时恒为 0（真机实测：文件在、11 条目、面板却报 0）。
///   现在：清单解析不出来时按原文里 "Id" 出现次数兜底；只有**文件不存在**才返回 -1。
if (!variable_global_exists("ntl_modmenu_loaded_count_cache")) global.ntl_modmenu_loaded_count_cache = -2;
if (global.ntl_modmenu_loaded_count_cache != -2) return global.ntl_modmenu_loaded_count_cache;

var _p = string(working_directory) + "Neutraled/mods.json";
if (!file_exists(_p))
{
    global.ntl_modmenu_loaded_count_cache = -1;
    return -1;
}
var _n = array_length(ntl_modmenu_loaded());
if (_n <= 0)
{
    var _txt = ntl_live_file_read(_p);
    _n = string_count("\"Id\"", _txt) + string_count("\"id\"", _txt);
    if (_n <= 0) _n = -1;   // 兜底也数不出来 ⇒ 让调用方回退"已安装"数，而不是撒谎说 0
}
global.ntl_modmenu_loaded_count_cache = _n;
return _n;
