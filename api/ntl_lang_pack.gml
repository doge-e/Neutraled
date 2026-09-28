/// ntl_lang_pack(code) —— 加载**外部语言包**（Neutraled/lang/lang_<code>.json），支持任意语言。
///
/// 文件格式（与 builder 侧 LangPacks 共用同一份规格）：
///   { "code": "ja", "name": "日本語", "map": { "中文原文": "译文", ... } }
///
/// 语义：
///   1) 语言包的 key 是**中文原文**（builder 的 L() 参数、GML 表里中文那一侧的值）；
///   2) 本函数把「符号键 → 中文」的 zh 表和语言包的「中文 → 译文」map 合成一张新表（符号键 → 译文），
///      注册进 global.ntl_i18n[code] —— 于是 ntl_t / ntl_ts / ntl_tf 一行都不用改就能说任意语言；
///   3) 查不到的条目**回退中文**（宁可显示中文，也不能留空白）；
///   4) zh / en 不走这里（它们是硬编码表，更准）。
///
/// 返回值：1 = 已加载并注册（幂等：同语言重复调用直接返回 1）；0 = 缺文件 / 解析失败（调用方保持原语言）。
var _code = string_lower(string_trim(string(argument[0])));
if (_code == "" || _code == "zh" || _code == "en") return 0;
if (!variable_global_exists("ntl_i18n")) return 0;
var _L = global.ntl_i18n;
if (ds_map_exists(_L, _code)) return 1;                 // 幂等：已经注册过

var _zh = undefined;
try { _zh = ds_map_find_value(_L, "zh"); } catch (e0) { _zh = undefined; }
if (is_undefined(_zh) || !is_real(_zh) || !ds_exists(_zh, ds_type_map)) return 0;

/// 路径：program_directory 恒为游戏根（见 ntl_product_scope.gml 注释：所有产物共用一个 exe），
/// 但外部引擎/Kristal 融合产物可能以别的 cwd 启动，所以再兜一个 working_directory。
var _rel = "Neutraled/lang/lang_" + _code + ".json";
var _txt = "";
var _cands = [ program_directory + _rel, string(working_directory) + _rel ];
for (var _i = 0; _i < array_length(_cands); _i++)
{
    if (file_exists(_cands[_i])) { _txt = ntl_live_file_read(_cands[_i]); break; }
}
if (_txt == "")
{
    ntl_log("i18n", "语言包缺失，保持当前语言: " + _rel);
    return 0;
}

var _j = undefined;
try { _j = json_parse(_txt); } catch (e1) { _j = undefined; }
if (_j == undefined) { ntl_log("i18n", "[错误] 语言包解析失败: " + _rel); return 0; }

var _map = undefined;
try { _map = variable_struct_get(_j, "map"); } catch (e2) { _map = undefined; }
if (_map == undefined) { ntl_log("i18n", "[错误] 语言包缺 map 字段: " + _rel); return 0; }

/// 合成新表：**逐条走 zh 表的 key**（保证键集与 zh 完全一致，缺译就留中文）。
var _tbl = ds_map_create();
var _ks = ntl_dsmap_keys(_zh);
var _nk = array_length(_ks);
var _hit = 0;
for (var _k = 0; _k < _nk; _k++)
{
    var _key = _ks[_k];
    var _srcs = string(ds_map_find_value(_zh, _key));
    var _dst = _srcs;
    var _has = false;
    try { _has = variable_struct_exists(_map, _srcs); } catch (e3) { _has = false; }
    if (_has)
    {
        var _v = "";
        try { _v = string(variable_struct_get(_map, _srcs)); } catch (e4) { _v = ""; }
        /// 空译文与「译文 == 中文」的占位一律丢弃 —— 保证回退中文而不是显示空白/原文重复
        if (_v != "" && _v != _srcs) { _dst = _v; _hit += 1; }
    }
    ds_map_add(_tbl, _key, _dst);
}
ds_map_add(_L, _code, _tbl);
ntl_log("i18n", "语言包已加载: " + _code + "（" + string(_hit) + "/" + string(_nk) + " 条命中译文）");
return 1;
