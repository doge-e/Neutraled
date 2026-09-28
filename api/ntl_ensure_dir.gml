/// ntl_ensure_dir(path) —— 确保路径所在目录存在（GM 不会自动建目录）
/// 传入文件路径或目录路径都可以：只对"看起来像文件"的去掉最后一段
var _p = string(argument[0]);
if (_p == "") return 0;

// 统一成 /
_p = string_replace_all(_p, chr(92), "/");

// 如果最后一段含 "." 就当它是文件，取前面的目录部分
var _lastSlash = 0;
for (var _i = string_length(_p); _i >= 1; _i -= 1)
{
    if (string_copy(_p, _i, 1) == "/") { _lastSlash = _i; break; }
}
if (_lastSlash > 0)
{
    var _tail = string_delete(_p, 1, _lastSlash);
    if (string_pos(".", _tail) > 0) _p = string_delete(_p, _lastSlash, string_length(_p));
}

if (_p == "" || string_length(_p) <= 3) return 0;
if (directory_exists(_p)) return 1;

// 逐级创建（GM 的 directory_create 不能一次建多级）
var _acc = "";
var _i2 = 1;
while (_i2 <= string_length(_p))
{
    var _c = string_copy(_p, _i2, 1);
    _acc += _c;
    if (_c == "/" && string_length(_acc) > 3)
    {
        if (!directory_exists(_acc)) { try { directory_create(_acc); } catch (e) { ntl_log("fs", "[ntl] ntl_ensure_dir.gml:33 directory_create 失败: " + string(_acc) + " / " + string(e)); } }
    }
    _i2 += 1;
}
if (!directory_exists(_p)) { try { directory_create(_p); } catch (e) { ntl_log("fs", "[ntl] ntl_ensure_dir.gml:37 directory_create 失败: " + string(_p) + " / " + string(e)); } }
return (directory_exists(_p)) ? 1 : 0;
