/// ntl_config_set_lang(code) —— 把语言码写回 Neutraled/config.json（保留其它字段）
/// 纯文本级替换：游戏里没有 JSON 序列化，手写最稳。返回 1 成功 / 0 失败。
/// ★ 2026-09-27：两份副本都写（存档区 + 游戏根，见 ntl_config_paths.gml 的沙箱说明）。
///   读原文取"存在的最后一个候选"（= 游戏根优先），写完把同一份文本写回所有候选，让两份收敛。
/// ★ 2026-09-29 修复「重复 lang 键」：判据从「替换后文本没变」改成显式 _found —— 要写的语言码
///   与文件里的现值相同时，旧判据会误判成「没有 lang 字段」而走追加分支，写出第二个 lang 键；
///   builder 用 System.Text.Json 读重复键会抛 "An item with the same key has already been added.
///   Key: lang"，于是 --plugin-hooks / --lang-coverage / --plugin-list 等全部崩。
///   同一处顺手自愈历史遗留的第二份 lang 键（整对删除，连它的分隔逗号），写回的文件永远只有一份。
var _code = string_lower(string_trim(string(argument[0])));
if (_code == "") return 0;
var _paths = ntl_config_paths();
var _src = _paths[0];
for (var _pi = 0; _pi < array_length(_paths); _pi += 1)
{
    if (file_exists(_paths[_pi])) _src = _paths[_pi];
}
var _txt = ntl_config_read_text(_src);
var _out = _txt;
var _found = false;                                          // ★ 真的找到并替换了 lang 的值
var _p = string_pos("\"lang\"", _txt);
if (_p > 0)
{
    var _after = string_delete(_txt, 1, _p + 5);            // "lang" 之后
    var _ci = string_pos(":", _after);
    if (_ci > 0)
    {
        var _after2 = string_delete(_after, 1, _ci);        // 冒号之后
        var _q1 = string_pos("\"", _after2);
        if (_q1 > 0)
        {
            var _after3 = string_delete(_after2, 1, _q1);   // 开引号之后
            var _q2 = string_pos("\"", _after3);
            if (_q2 > 0)
            {
                var _absStart = _p + _ci + _q1 + 5;         // 开引号在 _txt 里的下标
                _out = string_delete(_txt, _absStart, _q2 + 1);
                _out = string_insert("\"" + _code + "\"", _out, _absStart);
                _found = true;
            }
        }
    }
}
if (!_found && _p <= 0)
{
    // 确实没有 lang 字段：插到最后一个 } 之前
    //   （有 lang 键但值不是带引号的字符串时保持原文不动 —— 宁可不写，也不写出第二份）
    var _rb = 0;
    for (var _i = string_length(_txt); _i >= 1; _i -= 1)
    {
        if (string_char_at(_txt, _i) == "}") { _rb = _i; break; }
    }
    if (_rb > 0)
    {
        var _head = string_delete(_txt, _rb, string_length(_txt) - _rb + 1);
        var _tail = string_delete(_txt, 1, _rb - 1);
        var _ht = string_trim(_head);
        var _sep = (string_char_at(_ht, string_length(_ht)) == "{") ? "" : ",";
        _out = _head + _sep + chr(10) + "  \"lang\": \"" + _code + "\"" + chr(10) + _tail;
    }
}
// ---- 自愈：重复的 lang 键只留第一个（最早的版本会写出第二份），最多 8 轮防手滑写出 N 份 ----
var _dups = 0;
var _guard = 0;
while (_guard < 8)
{
    _guard += 1;
    var _i1 = string_pos("\"lang\"", _out);
    if (_i1 <= 0) break;
    var _j2 = string_pos("\"lang\"", string_delete(_out, 1, _i1 + 5));
    if (_j2 <= 0) break;
    var _i2 = _i1 + _j2 + 5;                                 // 第二份的 1 基下标
    var _start = _i2;
    var _len = 6;                                            // 兜底：只删键名
    var _c2 = string_pos(":", string_delete(_out, 1, _i2 + 5));
    if (_c2 > 0)
    {
        var _ca = _i2 + _c2 + 5;                             // 冒号下标
        var _qa = string_pos("\"", string_delete(_out, 1, _ca));
        if (_qa > 0)
        {
            var _q1a = _ca + _qa;                            // 开引号下标
            var _q2a = string_pos("\"", string_delete(_out, 1, _q1a));
            if (_q2a > 0) _len = (_q1a + _q2a) - _i2 + 1;     // 到闭引号为止
        }
    }
    var _g = _i2 - 1;                                        // 往前吃掉空白与一个逗号（逗号属于第二份）
    while (_g >= 1)
    {
        var _ch = string_char_at(_out, _g);
        if (_ch == " " || _ch == chr(9) || _ch == chr(10) || _ch == chr(13)) { _g -= 1; continue; }
        if (_ch == ",") { _len = _len + (_i2 - _g); _start = _g; }
        break;
    }
    _out = string_delete(_out, _start, _len);
    _dups += 1;
}
if (_dups > 0) ntl_log("cfg", "config.json duplicate lang key removed: " + string(_dups));
if (string_length(_out) <= 0)
{
    // 两份副本都不存在：生成一份最小配置
    _out = "{" + chr(10) + "  \"lang\": \"" + _code + "\"" + chr(10) + "}";
}
var _ok = 0;
for (var _wi = 0; _wi < array_length(_paths); _wi += 1)
{
    if (ntl_config_write_text(_paths[_wi], _out)) _ok += 1;
}
if (_ok > 0)
{
    ntl_log("cfg", "config.json 语言已写回: " + _code + "（" + string(_ok) + " 份）");
    return 1;
}
ntl_log("cfg", "config.json 语言写回失败: " + _code);
return 0;
