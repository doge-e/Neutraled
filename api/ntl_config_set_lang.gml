/// ntl_config_set_lang(code) —— 把语言码写回 Neutraled/config.json（保留其它字段）
/// 纯文本级替换：游戏里没有 JSON 序列化，手写最稳。返回 1 成功 / 0 失败。
/// ★ 2026-09-27：两份副本都写（存档区 + 游戏根，见 ntl_config_paths.gml 的沙箱说明）。
///   读原文取"存在的最后一个候选"（= 游戏根优先），写完把同一份文本写回所有候选，让两份收敛。
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
            }
        }
    }
}
if (_out == _txt)
{
    // 没有 lang 字段：插到最后一个 } 之前
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
