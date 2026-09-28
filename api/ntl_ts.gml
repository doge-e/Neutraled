/// ntl_ts(key, values) —— 取文案并把 {1}{2}{3}... 替换成 values 里的元素（下标 0 对应 {1}）
///   为什么不用 ntl_tf：ntl_tf 的参数顺序是 {n}{1}{2}{3}（最多 4 个）且容易错位；
///   本函数按数组顺序替换、个数不限，控制台输出改写成"单条模板"时用它。
///   用法: ntl_console_log(ntl_ts("speed.cur", [string(_cur), string(_orig)]));
var _s = ntl_t(argument[0]);
if (argument_count > 1 && is_array(argument[1]))
{
    var _a = argument[1];
    for (var _i = 0; _i < array_length(_a); _i += 1)
        _s = string_replace_all(_s, "{" + string(_i + 1) + "}", string(_a[_i]));
}
return _s;
