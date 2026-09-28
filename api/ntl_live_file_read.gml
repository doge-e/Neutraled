/// ntl_live_file_read —— 老式脚本（每个函数一个同名脚本资源）
var _path = argument[0];
    if (!file_exists(_path)) return "";
    var _f = file_text_open_read(_path);
    var _out = "";
    while (!file_text_eof(_f))
    {
        _out += file_text_read_string(_f);
        file_text_readln(_f);
        if (!file_text_eof(_f)) _out += chr(10);
    }
    file_text_close(_f);
    // ★ 剥掉 UTF-8 BOM（U+FEFF）：本机 74 个 mod 叶子里 23 个 mod.json 带 BOM
    //   （PowerShell 5.1 的 Set-Content -Encoding utf8、.NET 旧式 UTF8 编码都会写 BOM），
    //   json_parse 遇到 BOM 会直接抛异常 → 那个 mod 静默消失（老代码 catch 里只 continue）。
    if (string_length(_out) > 0 && ord(string_char_at(_out, 1)) == 65279) _out = string_delete(_out, 1, 1);
    return _out;
