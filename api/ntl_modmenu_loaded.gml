/// ntl_modmenu_loaded() —— 本产物**实际加载**的 mod 清单（数组，元素 = struct: Id/Name/Version）
/// 来源：builder 每次部署写在**产物目录**里的 <working_directory>Neutraled/mods.json
///   ★ 2026-10-02（用户 m23281「显示 0 mod 加载」）真根因：builder 那时用**默认编码器**写 mods.json，
///     「冰封帷幕」「汉化组」等含中文的 mod 名被写成 \uXXXX 转义，而 **GameMaker 的 json_parse 吃不下 \uXXXX**
///     （老坑 15，chapters.json 已实测）⇒ json_parse 抛异常 ⇒ 本函数返回空数组 ⇒ 面板恒显示「已加载 0」。
///     已在 builder 侧修掉（Program.cs:2432 改用 Paths.Json）；这里保留双形态读取 + 转义计数诊断作兜底。
///     注意：**不是**「顶层数组读不出数组」——顶层数组形态本身没问题，见 ntl_json_diag 对照组。
/// 读不到（老产物 / 外部章节 exe）返回空数组，调用方回退显示"已安装"数。
/// 缓存：global.ntl_modmenu_loaded_cache（面板打开时由 ntl_modmenu_open() 清掉重读）。
if (!variable_global_exists("ntl_modmenu_loaded_cache")) global.ntl_modmenu_loaded_cache = undefined;
if (!is_undefined(global.ntl_modmenu_loaded_cache)) return global.ntl_modmenu_loaded_cache;

var _out = [];
var _p = string(working_directory) + "Neutraled/mods.json";
global.ntl_modmenu_loaded_diag = "无文件";
if (file_exists(_p))
{
    var _txt = ntl_live_file_read(_p);
    var _j = undefined;
    var _etype = "";
    try { _j = json_parse(_txt); } catch (e) { _etype = " 异常=1"; }
    var _kind = "undefined";
    if (is_array(_j)) _kind = "array[" + string(array_length(_j)) + "]";
    else if (is_struct(_j)) _kind = "struct";
    else if (is_string(_j)) _kind = "string";
    else if (is_real(_j)) _kind = "real";
    if (is_array(_j)) _out = _j;
    else if (is_struct(_j) && variable_struct_exists(_j, "mods"))
    {
        var _m = variable_struct_get(_j, "mods");
        if (is_array(_m)) _out = _m;
    }
    global.ntl_modmenu_loaded_diag = "字符=" + string(string_length(_txt)) + " 转义=" + string(string_count("\\u", _txt)) + " 解析=" + _kind + " 条目=" + string(array_length(_out)) + _etype;
}
global.ntl_modmenu_loaded_cache = _out;
return _out;
