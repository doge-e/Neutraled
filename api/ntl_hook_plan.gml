/// ntl_hook_plan(scriptName) —— 生成某个函数的 hook 执行计划
///
/// ★ 自动解决冲突：多个 mod hook 同一函数时，不再"谁覆盖谁"，
///   而是**按声明顺序链式执行**（pre → post），互不干扰。
///
/// 返回：数组，每项是 ds_map { mod, mode, handler, order }
///
/// mode 语义：
///   pre      —— 原函数之前执行（可改参数）
///   post     —— 原函数之后执行（可改返回值）
///   override —— 独占（只有它能跑；有 override 时其余 pre/post 仍会执行但原函数不跑）
if (!variable_global_exists("ntl_hooks")) return [];
var _name = string(argument[0]);
if (!ds_map_exists(global.ntl_hooks, _name)) return [];

var _list = ds_map_find_value(global.ntl_hooks, _name);
var _out = [];
var _n = ds_list_size(_list);
for (var _i = 0; _i < _n; _i += 1)
{
    var _h = ds_list_find_value(_list, _i);
    // ★ 函数 hook 槽的元素是 ds_map（api/ntl_hook_init.gml:68 用 ds_map_create）；
    //   事件订阅槽的元素是脚本索引(real) → 这里只认 ds_map，否则会把脚本索引当 map 用
    if (!is_real(_h) || !ds_exists(_h, ds_type_map) || !ds_map_exists(_h, "mode")) continue;
    var _rec = ds_map_create();
    ds_map_add(_rec, "mod", ds_map_find_value(_h, "mod"));
    ds_map_add(_rec, "mode", ds_map_find_value(_h, "mode"));
    ds_map_add(_rec, "handler", ds_map_find_value(_h, "handler"));
    ds_map_add(_rec, "order", _i);
    array_push(_out, _rec);
}

// 排序：pre 在前，post 在后，override 最后（但要标记让原函数不跑）
// 简单稳定排序（按 mode 权重）
var _w = function(_m) {
    if (_m == "pre") return 0;
    if (_m == "post") return 2;
    if (_m == "override") return 1;
    return 3;
};
for (var _a = 1; _a < array_length(_out); _a += 1)
{
    var _cur = _out[_a]; var _b = _a - 1;
    while (_b >= 0 && _w(ds_map_find_value(_out[_b], "mode")) > _w(ds_map_find_value(_cur, "mode")))
    { _out[_b + 1] = _out[_b]; _b -= 1; }
    _out[_b + 1] = _cur;
}
return _out;
