/// ntl_modmenu_rows() —— 当前视图的完整行列表（**唯一来源**：绘制 / 输入 / 计数都读它）
/// 每行 = [标签, 数值, 说明, 变灰, kind, action]：
///   kind   0 = 普通项（可选）  1 = 分组标题（纯排版，光标跳过）  2 = 模组项（可选的普通项，缩进一档）
///   action 主视图按 Z 时干什么：
///          "chapters" / "mods" / "lang" / "deploy" / "close" = 面板内置动作
///          "script:<脚本名>" = mod 用 ntl_menu_add 注册的开关（跑那个脚本）
///          "" = 没有动作（分组标题）
///          子视图（chapters/mods/langs）的输入由各自的按键分支处理，这里统一留 ""。
/// ★ 用户反馈「设置选项无法分清是哪个 mod 的」的修法：mod 项不再与内置行混排，
///   而是插在分组标题「── <mod 名> ──」下面并缩进一档。
/// 缓存：global.ntl_modmenu_rows_cache = [签名, 行数组]；按键确认 / 打开面板 / 切语言时清空
///（行内容会随开关状态变，别让缓存骗人）。
var _view = variable_global_exists("ntl_modmenu_view") ? string(global.ntl_modmenu_view) : "main";
var _k = ntl_menu_count();
var _modsArr = variable_global_exists("ntl_modmenu_mods") ? global.ntl_modmenu_mods : [];
var _chN = variable_global_exists("ntl_ch") ? array_length(global.ntl_ch) : 0;
var _sig = _view + "|" + string(_k) + "|" + string(_chN) + "|" + string(ntl_modmenu_loaded_count()) + "|"
         + string(ntl_modmenu_modcount()) + "|" + string(global.ntl_lang) + "|" + string(array_length(_modsArr));
if (variable_global_exists("ntl_modmenu_rows_cache") && !is_undefined(global.ntl_modmenu_rows_cache))
{
    var _cc = global.ntl_modmenu_rows_cache;
    if (is_array(_cc) && array_length(_cc) >= 2 && string(_cc[0]) == _sig) return _cc[1];
}

var _rows = [];

if (_view == "main")
{
    array_push(_rows, [ntl_t("menu.chapters"), ntl_ts("menu.ch_count", [string(_chN)]), ntl_t("menu.d_chapters"), 0, 0, "chapters"]);

    // ★ 显示"本产物实际加载 N / 磁盘已安装 M"：只写一个 12 会让人以为是已安装数（用户的老诉求）
    var _ld = ntl_modmenu_loaded_count();
    var _ins = ntl_modmenu_modcount();
    var _vl = (_ld >= 0) ? (string(_ld) + " / " + string(_ins)) : string(_ins);
    array_push(_rows, [ntl_t("menu.mods_loaded"), _vl, ntl_ts("menu.d_loaded", [string(_ld >= 0 ? _ld : 0), string(_ins)]), 0, 0, "mods"]);

    array_push(_rows, [ntl_t("menu.lang"), "< " + string(global.ntl_lang) + " >", ntl_t("menu.d_lang"), 0, 0, "lang"]);
    array_push(_rows, [ntl_t("menu.deploy"), "", ntl_t("menu.d_deploy"), 0, 0, "deploy"]);

    if (_k > 0)
    {
        // 按 owner 分组（保持注册顺序；owner 为空 = 通用组）。不用 ds_map，避免每帧建销资源。
        var _onames = [];
        var _oidxs = [];
        for (var _i = 0; _i < _k; _i += 1)
        {
            var _m = ntl_menu_entry(_i);
            if (_m == -1) continue;
            var _ow = ds_map_exists(_m, "owner") ? string(ds_map_find_value(_m, "owner")) : "";
            if (string_length(_ow) <= 0) _ow = ntl_t("menu.group_generic");
            var _gi = -1;
            for (var _g = 0; _g < array_length(_onames); _g += 1)
            {
                if (string(_onames[_g]) == _ow) { _gi = _g; break; }
            }
            if (_gi < 0)
            {
                array_push(_onames, _ow);
                array_push(_oidxs, []);
                _gi = array_length(_onames) - 1;
            }
            var _l = _oidxs[_gi];
            array_push(_l, _i);
            _oidxs[_gi] = _l;
        }
        for (var _g2 = 0; _g2 < array_length(_onames); _g2 += 1)
        {
            array_push(_rows, [ntl_ts("menu.group_head", [string(_onames[_g2])]), "", ntl_t("menu.d_group"), 0, 1, ""]);
            var _idx = _oidxs[_g2];
            for (var _j = 0; _j < array_length(_idx); _j += 1)
            {
                var _mm = ntl_menu_entry(real(_idx[_j]));
                if (_mm == -1) continue;
                var _dsc = ds_map_exists(_mm, "desc") ? ntl_menu_text(ds_map_find_value(_mm, "desc")) : "";
                var _act = ds_map_exists(_mm, "action") ? string(ds_map_find_value(_mm, "action")) : "";
                array_push(_rows, [ntl_menu_text(ds_map_find_value(_mm, "label")),
                                   ntl_menu_text(ds_map_find_value(_mm, "value")),
                                   _dsc, 0, 2,
                                   (string_length(_act) > 0) ? ("script:" + _act) : ""]);
            }
        }
    }
    array_push(_rows, [ntl_t("menu.close"), "", ntl_t("menu.d_close"), 0, 0, "close"]);
}
else if (_view == "langs")
{
    var _codes = ntl_lang_list();
    var _cur = string_lower(string(global.ntl_lang));
    for (var _i = 0; _i < array_length(_codes); _i += 1)
    {
        var _code = string_lower(string(_codes[_i]));
        array_push(_rows, [ntl_lang_name(_code), (_code == _cur) ? ntl_t("menu.lang_now_tag") : "", ntl_t("menu.d_lang"), 0, 0, "langset"]);
    }
}
else if (_view == "chapters")
{
    var _n2 = variable_global_exists("ntl_ch") ? array_length(global.ntl_ch) : 0;
    for (var _i = 0; _i < _n2; _i += 1)
    {
        var _cm = global.ntl_ch[_i];
        var _order = real(ds_map_find_value(_cm, "order"));
        var _name = string(ds_map_find_value(_cm, "name"));
        var _kindv = string(ds_map_find_value(_cm, "kind"));
        var _en = ds_map_find_value(_cm, "enabled");
        var _value = "";
        if (_kindv == "official") _value = ntl_t("menu.k_official");
        else if (_kindv == "patch") _value = ntl_t("menu.k_patch");
        else if (_kindv == "timeline") _value = ntl_t("menu.k_timeline");
        else if (_kindv == "external") _value = ntl_t("menu.k_external");
        var _dim = 0;
        if (_en != 1) { _value = ntl_t("root.no_content"); _dim = 1; }
        array_push(_rows, [ntl_ts("menu.ch_line", [string(_order), _name]), _value, ntl_t("menu.d_chapters"), _dim, 0, "goto"]);
    }
}
else
{
    // mods 视图：mods/ 下的目录名 + 「本产物是否加载了这个 mod」+ 显示名/版本（只读，没有动作）
    for (var _i = 0; _i < array_length(_modsArr); _i += 1)
    {
        var _dn = string(_modsArr[_i]);
        var _info = ntl_modmenu_modinfo(_dn);
        var _on = (real(_info[0]) == 1);
        array_push(_rows, [_dn,
                           _on ? ntl_t("menu.m_loaded") : ntl_t("menu.m_unloaded"),
                           _on ? ntl_ts("menu.d_mods_loaded", [string(_info[1]), string(_info[2])]) : ntl_t("menu.mods_ro"),
                           0, 0, ""]);
    }
}

global.ntl_modmenu_rows_cache = [_sig, _rows];
return _rows;
