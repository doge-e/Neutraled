/// ntl_lua_load(chunk, chunkname) —— 编译一段 Lua 源码（load / loadstring）
///
/// ★ 返回的是宿主函数名字符串（如 "__loaded_3"），不是手工构造的函数值：
///   函数值经 Lua 环境传递后在某些路径会失真（求值正确但传不回去），
///   返回字符串则由既有的「未定义函数名 -> 宿主」机制天然驱动。
var _src = string(argument[0]);
if (_src == "") return undefined;

var _lc = undefined;
try { _lc = ntl_lua_compile(_src); }
catch (e) { ntl_log("lua", "[load] 编译异常: " + string(e)); return undefined; }
if (_lc == undefined) return undefined;

if (ds_map_find_value(_lc, "ok") != 1)
{
    ntl_log("lua", "[load] 语法错误: " + string(ds_map_find_value(_lc, "err")));
    return undefined;
}

var _body = ds_map_find_value(_lc, "ast");
if (_body == undefined || !is_array(_body)) return undefined;

var _reg = ntl_lua_chunk_reg();
global.ntl_lua_chunk_seq += 1;
var _name = "__loaded_" + string(global.ntl_lua_chunk_seq);
ds_map_add(_reg, _name, _body);

ntl_log("lua", "[load] 编译为 " + _name + "（" + string(array_length(_body)) + " 条语句）");
return _name;