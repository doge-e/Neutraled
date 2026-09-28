/// ntl_theme_load() —— 读取 Neutraled/console-theme.json（由 ntl-builder --theme-use 写出）
///
/// 文件格式（与 builder 侧 Themes.cs 一致）：
///   { "schema":1, "id":"dark", "name":"深色（默认）",
///     "colors":    { "bg":"#101014", ... 11 键 ... },
///     "colors_bgr":{ "bg":1315856, ... 同键 → (b<<16)|(g<<8)|r ... } }
///
/// 为什么用 colors_bgr：GameMaker 的颜色是 BGR 整数，游戏侧直接取整数即可，
/// 不必在 GML 里解析十六进制（也就没有解析失败的可能）。
///
/// 缺失/损坏一律**保持内置默认**（等于改造前的配色），所以没装主题的机器像素级不变。
/// 返回值：1 = 读到主题文件；0 = 没有/不可用（默认配色）。
if (!variable_global_exists("ntl_theme")) global.ntl_theme = ds_map_create();
if (!variable_global_exists("ntl_theme_id")) global.ntl_theme_id = "builtin";

global.ntl_theme_checked = 1;
var _rel = "Neutraled/console-theme.json";
var _txt = "";
var _cands = [ program_directory + _rel, string(working_directory) + _rel ];
for (var _i = 0; _i < array_length(_cands); _i++)
{
    if (file_exists(_cands[_i])) { _txt = ntl_live_file_read(_cands[_i]); break; }
}
if (_txt == "") return 0;

var _j = undefined;
try { _j = json_parse(_txt); } catch (e) { _j = undefined; }
if (_j == undefined) { ntl_log("theme", "[错误] console-theme.json 解析失败，用默认配色"); return 0; }

var _bgr = undefined;
try { _bgr = variable_struct_get(_j, "colors_bgr"); } catch (e1) { _bgr = undefined; }
if (_bgr == undefined) { ntl_log("theme", "[警告] console-theme.json 缺 colors_bgr，用默认配色"); return 0; }

/// 只认自己知道的名字（顺序与 Themes.ColorKeys 一致）
var _names = [ "bg", "panel", "fg", "dim", "border", "accent", "highlight", "selection", "ok", "warn", "error" ];
var _n = 0;
for (var _k = 0; _k < array_length(_names); _k++)
{
    var _nm = _names[_k];
    var _has = false;
    try { _has = variable_struct_exists(_bgr, _nm); } catch (e2) { _has = false; }
    if (!_has) continue;
    var _v = 0;
    try { _v = real(variable_struct_get(_bgr, _nm)); } catch (e3) { _v = 0; }
    // ★ ds_map_replace 只替换**已存在**的键；空 map 上必须用 ds_map_add，否则主题永远不生效
    if (ds_map_exists(global.ntl_theme, _nm)) ds_map_replace(global.ntl_theme, _nm, _v);
    else ds_map_add(global.ntl_theme, _nm, _v);
    _n += 1;
}

var _id = "custom";
try { if (variable_struct_exists(_j, "id")) _id = string(variable_struct_get(_j, "id")); } catch (e4) { _id = "custom"; }
global.ntl_theme_id = _id;
ntl_log("theme", "主题已加载: " + _id + "（" + string(_n) + " 个颜色）");
return 1;
