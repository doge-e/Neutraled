/// ntl_modmenu_mods() —— 扫描 Neutraled/mods/ 下的模组目录（面板展示用）
/// 返回已排序的字符串数组；扫描失败返回空数组（面板显示「没有已装模组」）。
var _out = [];
var _roots = [];
array_push(_roots, program_directory + "Neutraled/mods/");
array_push(_roots, program_directory + "Neutraled/mods");

for (var _r = 0; _r < array_length(_roots); _r += 1)
{
    var _root = _roots[_r];
    if (array_length(_out) > 0) break;
    try
    {
        var _fn = file_find_first(_root + "*", fa_directory);
        while (_fn != "")
        {
            var _nm = string(_fn);
            // GM 返回的目录名带尾部分隔符
            while (string_length(_nm) > 0)
            {
                var _last = string_char_at(_nm, string_length(_nm));
                if (_last == "/" || _last == "\\") _nm = string_delete(_nm, string_length(_nm), 1);
                else break;
            }
            if (_nm != "" && _nm != "." && _nm != "..") array_push(_out, _nm);
            _fn = file_find_next();
        }
        file_find_close();
    }
    catch (e) { ntl_log("modmenu", "[面板] 扫描 mods 目录失败: " + string(e)); }
}

// 冒泡排序（不依赖 array_sort 的平台行为，和 ntl_lang_list 一致）
for (var _i = 0; _i < array_length(_out); _i += 1)
{
    for (var _j = _i + 1; _j < array_length(_out); _j += 1)
    {
        if (string_lower(string(_out[_j])) < string_lower(string(_out[_i])))
        {
            var _t = _out[_i]; _out[_i] = _out[_j]; _out[_j] = _t;
        }
    }
}
return _out;
