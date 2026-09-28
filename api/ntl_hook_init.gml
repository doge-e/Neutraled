/// ntl_hook_init() —— 初始化 hook 系统（从 hook-registry.json 读取）
///
/// ★ 缺陷 1 修复（1/2）：以前这里是**无条件** global.ntl_hooks = ds_map_create()。
///   而 global.ntl_hooks 是"函数 hook + 事件订阅"共用表：mod 入口脚本在
///   scr_ntl_init.gml:91 ntl_run_mods() 里已经用 ntl_hook() 订阅了事件，
///   本函数在 scr_ntl_init.gml:104 才跑 —— 于是 mod 的订阅被整体抹掉
///   （真机现象：ntl_hook() 返回 0 却一次不跑 + 日志「已注册 0 个 hook（覆盖 0 个脚本）」）。
///   现在改为幂等初始化；注册表自己的槽位在下面按 key 精确重置。
///
///   幂等契约：重复调用只重置「上一次注册表写入过的 key」（global.ntl_hook_reg_keys），
///   mod 自己订阅的事件槽不在其中 —— 入口脚本里可以直接订阅，不必等本函数跑完。
if (!variable_global_exists("ntl_hooks")) global.ntl_hooks = ds_map_create();      // key -> ds_list（函数 hook：pre/post/override；事件订阅：handler 列表）
if (!variable_global_exists("ntl_bh_map")) global.ntl_bh_map = ds_map_create();    // 内置函数名 -> ds_list（内置函数 Hook）
// 上一次注册表加载占用的 key：只重置这些，绝不碰 mod 的事件订阅槽
if (!variable_global_exists("ntl_hook_reg_keys")) global.ntl_hook_reg_keys = ds_list_create();
if (!is_real(global.ntl_hook_reg_keys) || !ds_exists(global.ntl_hook_reg_keys, ds_type_list)) global.ntl_hook_reg_keys = ds_list_create();

var _path = program_directory + "Neutraled/hook-registry.json";
if (!file_exists(_path))
{
    ntl_log("hook", "无 hook 注册表（跳过）");
    return 0;
}

var _txt = "";
var _f = file_text_open_read(_path);
while (!file_text_eof(_f))
{
    _txt += file_text_read_string(_f);
    file_text_readln(_f);
    if (!file_text_eof(_f)) _txt += chr(10);
}
file_text_close(_f);

var _j = undefined;
try { _j = json_parse(_txt); } catch (e) { ntl_log("hook", "[错误] 注册表解析失败"); return 0; }
if (_j == undefined) return 0;

var _items = variable_struct_get(_j, "hooks");
if (_items == undefined || !is_array(_items)) return 0;

// ★ 缺陷 1 修复（2/2）：只重置上一次由注册表写入的槽位（幂等：重复初始化不追加重复项）。
//   这些 key 记在 global.ntl_hook_reg_keys 里，mod 的事件订阅槽不在其中，因此不受影响。
var _regOld = ds_list_size(global.ntl_hook_reg_keys);
for (var _ro = 0; _ro < _regOld; _ro += 1)
{
    var _rk = ds_list_find_value(global.ntl_hook_reg_keys, _ro);
    if (!ds_map_exists(global.ntl_hooks, _rk)) continue;
    var _rl = ds_map_find_value(global.ntl_hooks, _rk);
    if (is_real(_rl) && ds_exists(_rl, ds_type_list)) ds_list_destroy(_rl);
    ds_map_delete(global.ntl_hooks, _rk);
}
ds_list_clear(global.ntl_hook_reg_keys);

var _n = array_length(_items);
var _cnt = 0;
for (var _i = 0; _i < _n; _i += 1)
{
    var _it = _items[_i];
    if (!is_struct(_it)) continue;
    var _script = string(variable_struct_get(_it, "script"));
    var _mode = string(variable_struct_get(_it, "mode"));
    var _handler = string(variable_struct_get(_it, "handler"));
    var _mod = string(variable_struct_get(_it, "mod"));
    var _source = string(variable_struct_get(_it, "source"));   // lua / ntl

    if (!ds_map_exists(global.ntl_hooks, _script))
        ds_map_add(global.ntl_hooks, _script, ds_list_create());
    var _list = ds_map_find_value(global.ntl_hooks, _script);

    var _h = ds_map_create();
    ds_map_add(_h, "mode", _mode);
    ds_map_add(_h, "handler", _handler);
    ds_map_add(_h, "mod", _mod);
    ds_map_add(_h, "source", _source);
    // moddir：hook 脚本所在 mod 的目录（拼接 handler 时用）
    var _md = "";
    try { _md = string(variable_struct_get(_it, "moddir")); } catch (e) { _md = ""; }
    ds_map_add(_h, "moddir", _md);
    ds_list_add(_list, _h);
    ds_list_add(global.ntl_hook_reg_keys, _script);
    _cnt += 1;
}

// ---- 内置函数 Hook（builtin_hooks）----
var _bhItems = variable_struct_get(_j, "builtin_hooks");
var _bhCnt = 0;
if (_bhItems != undefined && is_array(_bhItems))
{
    var _bn = array_length(_bhItems);
    for (var _bi = 0; _bi < _bn; _bi += 1)
    {
        var _bit = _bhItems[_bi];
        if (!is_struct(_bit)) continue;
        var _bfn = string(variable_struct_get(_bit, "func"));
        // 幂等：重复初始化时替换该函数的内置 hook 槽（而不是追加重复项）
        if (ds_map_exists(global.ntl_bh_map, _bfn)) ds_map_delete(global.ntl_bh_map, _bfn);
        ds_map_add(global.ntl_bh_map, _bfn, []);
        var _blist = ds_map_find_value(global.ntl_bh_map, _bfn);
        var _brec = ds_map_create();
        ds_map_add(_brec, "mode", string(variable_struct_get(_bit, "mode")));
        ds_map_add(_brec, "handler", string(variable_struct_get(_bit, "handler")));
        ds_map_add(_brec, "moddir", string(variable_struct_get(_bit, "moddir")));
        ds_map_add(_brec, "script", _bfn);
        array_push(_blist, _brec);
        _bhCnt += 1;
    }
}

// ---- 对象事件 Hook ----
var _oevCnt = 0;
try { _oevCnt = ntl_oev_init(); } catch (e) { ntl_log("hook", "[ntl] ntl_hook_init.gml:109 ntl_oev_init 失败: " + string(e)); }

// 覆盖数只算注册表自己的脚本槽（与修复前语义一致）；事件订阅槽单独报一行 ——
// 它是 mod 入口脚本 ntl_hook() 建的，修复前会被本函数抹掉，这一行就是"没被抹掉"的证据。
var _regN = 0;
var _allKeys = ntl_dsmap_keys(global.ntl_hooks);
for (var _kk = 0; _kk < array_length(_allKeys); _kk += 1)
    if (ds_list_find_index(global.ntl_hook_reg_keys, _allKeys[_kk]) >= 0) _regN += 1;
var _evN = array_length(_allKeys) - _regN;

ntl_log("hook", "已注册 " + string(_cnt) + " 个 hook（覆盖 " + string(_regN) + " 个脚本）");
if (_evN > 0) ntl_log("hook", "事件订阅槽: " + string(_evN) + " 个（来自 mod 入口脚本的 ntl_hook()，未被初始化清空）");
if (_bhCnt > 0) ntl_log("hook", "内置函数 Hook: " + string(_bhCnt) + " 条（" + string(ds_map_size(global.ntl_bh_map)) + " 个函数）");
return _cnt;
