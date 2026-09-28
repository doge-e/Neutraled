/// ntl_lua_env_find(env, name) —— 一次走链完成"查找 + 取值"
///
/// 优化历史：
///   1) 原来调用方要 env_has + env_get 走两遍作用域链
///   2) 现在这一层也不再用 ds_map_exists + ds_map_find_value（两遍哈希查找），
///      改成只调一次 ds_map_find_value：
///        - 键存在且值非 nil   → 返回 [1, value]
///        - 键不存在           → 返回 undefined → [0, undefined]
///        - 键存在但值是 nil   → 返回 undefined → 当"不存在"处理
///      Lua 语义里 nil 与"不存在"等价，所以第三步合并是安全的。
///
/// 返回 [found(0/1), value]
var _e = argument[0];
var _n = string(argument[1]);
var _guard = 0;
while (_e != undefined && is_real(_e) && ds_exists(_e, ds_type_map) && _guard < 64)
{
    var _v = ds_map_find_value(_e, _n);
    if (_v != undefined) return [1, _v];
    _e = ds_map_find_value(_e, "_ntlp");
    _guard += 1;
}
return [0, undefined];
