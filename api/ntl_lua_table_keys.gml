/// ntl_lua_table_keys(t) —— 返回表的所有键（GML 数组，跳过内部键）
///
/// ★ 关键：ntl_lua_key 会给键加类型前缀（字符串 "s"、数字 "n"、布尔 "b"），
///   这里必须**还原成 Lua 层能直接用的形式**，否则拿回去 table_get 会被再加一次前缀。
var _t = argument[0];
var _out = [];
if (_t == undefined || !is_real(_t)) return _out;

try
{
    var _all = ntl_dsmap_keys(_t);
    for (var _i = 0; _i < array_length(_all); _i += 1)
    {
        var _raw = string(_all[_i]);
        // 跳过内部键
        if (string_copy(_raw, 1, 4) == "_ntl") continue;

        var _c = string_copy(_raw, 1, 1);
        if (_c == "s")
        {
            // 字符串键：剥掉 "s" 前缀
            array_push(_out, string_delete(_raw, 1, 1));
        }
        else if (_c == "n")
        {
            // 数字键：还原成数字
            var _num = real(string_delete(_raw, 1, 1));
            array_push(_out, _num);
        }
        else if (_raw == "b1") array_push(_out, true);
        else if (_raw == "b0") array_push(_out, false);
        else if (_raw == "nil") continue;
        else array_push(_out, _raw);   // 兜底（老格式）
    }
}
catch (e) { ntl_log("lua", "[ntl] ntl_lua_table_keys.gml:36 遍历表键失败: " + string(e)); }
return _out;
