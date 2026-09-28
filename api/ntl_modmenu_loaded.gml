/// ntl_modmenu_loaded() —— 本产物**实际加载**的 mod 清单（数组，元素 = struct: id/name/version）
/// 来源：builder 每次部署写在**产物目录**里的 <working_directory>Neutraled/mods.json
///       （builder\Program.cs 的 mods.Select(m => new { m.Id, m.Name, m.Version })）。
/// ★ 用户诉求（m10511）：面板/入口行要显示"已加载"而不是"已安装" —— 磁盘 Neutraled\mods\ 下有 47 个
///   目录（已安装），但本产物只加载了其中一部分；显示已安装数会误导。
/// 读不到（老产物 / 外部章节 exe）返回空数组，调用方回退显示已安装数。
/// 缓存：global.ntl_modmenu_loaded_cache（面板打开时由 ntl_modmenu_open() 清掉重读）。
if (!variable_global_exists("ntl_modmenu_loaded_cache")) global.ntl_modmenu_loaded_cache = undefined;
if (!is_undefined(global.ntl_modmenu_loaded_cache)) return global.ntl_modmenu_loaded_cache;

var _out = [];
var _p = string(working_directory) + "Neutraled/mods.json";
if (file_exists(_p))
{
    try
    {
        var _j = json_parse(ntl_live_file_read(_p));
        if (_j != undefined && is_array(_j)) _out = _j;
    }
    catch (e) { _out = []; }
}
global.ntl_modmenu_loaded_cache = _out;
return _out;
