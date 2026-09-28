/// ntl_console_mod_dir(id, name) —— 反查某个已加载 mod 在 Neutraled/mods/ 下的目录名（找不到返回 ""）
/// 复用 ntl_modmenu_modinfo()（面板用的同一套匹配规则：大小写/空格/下划线/点不敏感 + Id 分段）
var _id = (argument_count > 0) ? string(argument[0]) : "";
var _nm = (argument_count > 1) ? string(argument[1]) : "";
if (_id == "" && _nm == "") return "";

var _dirs = ntl_modmenu_mods();
var _res = "";
try
{
    for (var _i = 0; _i < array_length(_dirs); _i += 1)
    {
        var _d = string(_dirs[_i]);
        var _mi = ntl_modmenu_modinfo(_d);
        if (!is_array(_mi)) continue;
        var _dn = string(_mi[1]);
        // 1) 显示名精确命中（面板同一套匹配）
        if (_nm != "" && _dn == _nm) { _res = _d; break; }
        // 2) 退回 Id 归一化比较（大小写/点/下划线不敏感）
        if (_id != "" && ntl_keynorm(_d) == ntl_keynorm(_id)) { _res = _d; break; }
        // 3) 退回 Id 分段比较（目录 dojo 命中 Id "converted.dojo.xanzo1" 的第 2 段）
        if (_id != "") {
            var _seg = _id;
            while (string_length(_seg) > 0)
            {
                var _p = string_pos(".", _seg);
                var _one = (_p > 0) ? string_copy(_seg, 1, _p - 1) : _seg;
                _seg = (_p > 0) ? string_delete(_seg, 1, _p) : "";
                if (string_length(_one) > 0 && ntl_keynorm(_one) == ntl_keynorm(_d)) { _res = _d; break; }
            }
            if (_res != "") break;
        }
    }
}
catch (e) { ntl_log("console", "[ntl] ntl_console_mod_dir 查目录失败: " + string(e)); }
return _res;
