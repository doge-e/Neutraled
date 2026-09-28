/// ntl_config_load() —— 读取 Neutraled/config.json 到 global.ntl_cfg
/// 配置项（全部可选）：
///   auto_skip_selector : 自动跳过章节选择器，直接进入 auto_chapter
///   auto_chapter       : 自动进入的章节序号（1-7 官方；mod 章节用 auto_chapter_id）
///   auto_chapter_id    : 直接指定章节 id（如 timeline:4:mod:alt）
///   auto_skip_delay    : 跳过前的等待帧数（默认 90）
///   auto_skip_intro    : 尝试跳过章节内的开场演出（实验性）
///   debug_live         : 打开 live 脚本调试日志
///   skip_legend / skip_logo / intro_delay / lang
/// ★ 2026-09-27：候选路径见 ntl_config_paths()（存档区 + 游戏根，root 产物受 GM 文件沙箱遮蔽）。
///   规则：**两处都读**，后读的（游戏根那份）覆盖先读的（存档区那份）；读完把游戏根那份原文
///   镜像回存档区 ⇒ 章节内改的语言/CLI 改的配置都能传到下一次启动的章节选择器。

global.ntl_cfg = ds_map_create();
ds_map_add(global.ntl_cfg, "auto_skip_selector", 0);
ds_map_add(global.ntl_cfg, "auto_chapter", 0);
ds_map_add(global.ntl_cfg, "auto_chapter_id", "");
ds_map_add(global.ntl_cfg, "auto_skip_delay", 90);
ds_map_add(global.ntl_cfg, "auto_skip_intro", 0);
ds_map_add(global.ntl_cfg, "skip_legend", 0);      // 跳过"传说"画面
ds_map_add(global.ntl_cfg, "skip_logo", 0);        // 跳过"DELTARUNE"报幕
ds_map_add(global.ntl_cfg, "intro_delay", 45);     // 跳过前等待帧数
ds_map_add(global.ntl_cfg, "debug_live", 0);
ds_map_add(global.ntl_cfg, "lang", "auto");     // zh / en / auto —— 只影响 Neutraled 新增的界面文字

var _paths = ntl_config_paths();
var _pList = "";
for (var _pj = 0; _pj < array_length(_paths); _pj += 1)
{
    if (_pj > 0) _pList += " | ";
    _pList += string(_paths[_pj]);
}
var _js = [];        // 解析出的 struct（与 _srcs 一一对应）
var _srcs = [];      // 对应路径
for (var _pi = 0; _pi < array_length(_paths); _pi += 1)
{
    var _p = _paths[_pi];
    var _t = ntl_config_read_text(_p);
    if (_t == "") continue;
    var _one = undefined;
    try { _one = json_parse(_t); }
    catch (e)
    {
        // 兼容：去掉注释字段后重试（某些编辑器会写入转义或注释）
        ntl_log("cfg", "[警告] config.json 首次解析失败，尝试容错解析: " + _p);
        var _clean = "";
        var _len = string_length(_t);
        var _i = 1;
        while (_i <= _len)
        {
            var _c = string_char_at(_t, _i);
            if (_c == chr(9) || _c == chr(10) || _c == chr(13)) { _i += 1; continue; }
            _clean += _c;
            _i += 1;
        }
        try { _one = json_parse(_clean); } catch (e2) { _one = undefined; }
    }
    if (_one == undefined)
    {
        ntl_log("cfg", "[错误] config.json 无法解析，跳过这份: " + _p);
        continue;
    }
    array_push(_js, _one);
    array_push(_srcs, _p);
}
var _n = array_length(_js);
if (_n == 0)
{
    ntl_log("cfg", "未找到可用的 config.json（使用默认配置），候选路径: " + _pList);
    return 0;
}

// 一律用 variable_struct_get（struct.field 形式在 UTMT 编译下不可靠）
// 注意：这里不使用内部 function 定义（跨作用域不可靠），全部内联
var _keys = [
    ["auto_skip_selector", 1], ["auto_skip_intro", 1], ["debug_live", 1],
    ["skip_legend", 1], ["skip_logo", 1], ["auto_chapter", 0],
    ["auto_skip_delay", 0], ["intro_delay", 0], ["auto_chapter_id", 2],
    ["lang", 2]
];
for (var _si = 0; _si < _n; _si += 1)
{
    // 后一份（游戏根）覆盖前一份（存档区）
    var _j = _js[_si];
    var _diag = "";
    var _kc = array_length(_keys);
    for (var _ki = 0; _ki < _kc; _ki += 1)
    {
        var _key = _keys[_ki][0];
        var _kind = _keys[_ki][1];
        var _v = variable_struct_get(_j, _key);
        if (_v == undefined) continue;

        if (_kind == 1)
        {
            var _b = (_v == true || _v == 1) ? 1 : 0;
            ds_map_replace(global.ntl_cfg, _key, _b);
            _diag += _key + "=" + string(_b) + " ";
        }
        else if (_kind == 0)
        {
            var _rn = real(_v);
            ds_map_replace(global.ntl_cfg, _key, _rn);
            _diag += _key + "=" + string(_rn) + " ";
        }
        else
        {
            var _s = string(_v);
            if (string_length(_s) > 0) ds_map_replace(global.ntl_cfg, _key, _s);
            _diag += _key + "=" + _s + " ";
        }
    }
    ntl_log("cfg", "解析字段[" + string(_si + 1) + "/" + string(_n) + " " + string(_srcs[_si]) + "]: " + _diag);
}

// 镜像：让两份内容收敛（存档区缺失/不同 ⇒ 用游戏根那份覆盖；游戏根缺失 ⇒ 用存档区那份补写）
var _save = _paths[0];
var _bundle = _paths[array_length(_paths) - 1];
if (_save != _bundle)
{
    var _bTxt = ntl_config_read_text(_bundle);
    var _sTxt = ntl_config_read_text(_save);
    if (_bTxt == "" && _sTxt != "")
    {
        if (ntl_config_write_text(_bundle, _sTxt)) ntl_log("cfg", "已把存档区 config.json 补写到游戏根: " + _bundle);
    }
    else if (_bTxt != "" && _bTxt != _sTxt)
    {
        if (ntl_config_write_text(_save, _bTxt)) ntl_log("cfg", "已把游戏根 config.json 镜像到存档区: " + _save);
    }
}
var _srcList = "";
for (var _li = 0; _li < array_length(_srcs); _li += 1)
{
    if (_li > 0) _srcList += " | ";
    _srcList += string(_srcs[_li]);
}
ntl_log("cfg", "配置来源: " + _srcList + "  game_save_id=" + string(game_save_id) + "  working_dir=" + string(working_directory));
return 1;
