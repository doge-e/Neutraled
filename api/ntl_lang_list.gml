/// ntl_lang_list() —— 可用语言码列表：内置 zh/en + Neutraled/lang/lang_<code>.json 外部包
/// 顺序：zh 在最前（界面中文原文），外部包按字母序，en 在最后。章节选择器按 L 用这个顺序循环。
var _out = [];
array_push(_out, "zh");
var _names = [];
try
{
    var _dir = program_directory + "Neutraled/lang/";
    var _fn = file_find_first(_dir + "lang_*.json", 0);
    while (_fn != "" && _fn != -1)
    {
        var _code = string_replace(string_replace(_fn, "lang_", ""), ".json", "");
        if (string_length(_code) > 0) array_push(_names, _code);
        _fn = file_find_next();
    }
    file_find_close();
}
catch (e) { ntl_log("i18n", "语言列表枚举失败: " + string(e)); }
// 冒泡排序（不想依赖 array_sort 对不同平台的行为）
for (var _i = 0; _i < array_length(_names); _i += 1)
{
    for (var _j = _i + 1; _j < array_length(_names); _j += 1)
    {
        if (string_lower(string(_names[_j])) < string_lower(string(_names[_i])))
        {
            var _tmp = _names[_i]; _names[_i] = _names[_j]; _names[_j] = _tmp;
        }
    }
}
for (var _k = 0; _k < array_length(_names); _k += 1) array_push(_out, _names[_k]);
array_push(_out, "en");
return _out;
