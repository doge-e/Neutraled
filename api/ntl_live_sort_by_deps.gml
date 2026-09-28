/// ntl_live_sort_by_deps() —— 按 mod 之间的依赖关系重排加载顺序
///
/// 目的：**mod 互操作**要求被依赖的 mod 先初始化。
///       例如 InteropB 依赖 InteropA，则 A 的 on_init 必须先跑（A 先导出 API）。
///
/// 算法：简单拓扑排序（dependencies 里声明的 id 必须先于自己）
///       - 读取每个 mod 目录下的 mod.json 的 dependencies
///       - 未在依赖图中的 mod 保持原相对顺序（稳定）
///       - 检测到环时保持原顺序并记日志（不阻塞）
if (!variable_global_exists("ntl_live_mods")) return 0;
var _mods = global.ntl_live_mods;
var _n = ds_list_size(_mods);
if (_n <= 1) return _n;

// 1) 收集每个 mod 的 id 与依赖
var _ids = [];
var _deps = [];
for (var _i = 0; _i < _n; _i += 1)
{
    var _e = ds_list_find_value(_mods, _i);
    var _id = string(ds_map_find_value(_e, "name"));
    array_push(_ids, _id);
    var _dlist = [];
    var _dj = ds_map_find_value(_e, "dir") + "mod.json";
    if (file_exists(_dj))
    {
        try
        {
            var _mj = json_parse(ntl_live_file_read(_dj));
            if (variable_struct_exists(_mj, "dependencies"))
            {
                var _dp = variable_struct_get(_mj, "dependencies");
                if (is_array(_dp))
                {
                    for (var _k = 0; _k < array_length(_dp); _k += 1)
                    {
                        var _spec = string(_dp[_k]);
                        // 支持 "id" 与 "id >= 1.0.0" 两种写法
                        var _sp = string_pos(" ", _spec);
                        if (_sp > 0) _spec = string_copy(_spec, 1, _sp - 1);
                        array_push(_dlist, _spec);
                    }
                }
            }
        }
        catch (e) { ntl_log("live", "[ntl] ntl_live_sort_by_deps.gml:46 解析 mod.json 依赖失败: " + string(e)); }
    }
    array_push(_deps, _dlist);
}

// 2) 拓扑排序（稳定：同等条件下保持原顺序）
var _out = [];
var _done = [];
for (var _i = 0; _i < _n; _i += 1) array_push(_done, 0);

var _progress = 1;
var _guard = 0;
while (_progress == 1 && _guard < _n + 5)
{
    _guard += 1;
    _progress = 0;
    for (var _i = 0; _i < _n; _i += 1)
    {
        if (_done[_i] == 1) continue;
        // 检查它的依赖是否都已完成
        var _ok = 1;
        var _myDeps = _deps[_i];
        for (var _d = 0; _d < array_length(_myDeps); _d += 1)
        {
            var _want = _myDeps[_d];
            // 找到依赖在列表里的位置
            for (var _j = 0; _j < _n; _j += 1)
            {
                if (_ids[_j] != _want) continue;
                if (_done[_j] != 1 && _j != _i) { _ok = 0; }
                break;
            }
            if (_ok == 0) break;
        }
        if (_ok == 1)
        {
            _done[_i] = 1;
            array_push(_out, ds_list_find_value(_mods, _i));
            _progress = 1;
        }
    }
}

// 3) 把剩下的（有环的）按原顺序接在后面
for (var _i = 0; _i < _n; _i += 1)
{
    if (_done[_i] == 0) array_push(_out, ds_list_find_value(_mods, _i));
}

// 4) 回写
ds_list_clear(_mods);
for (var _i = 0; _i < array_length(_out); _i += 1) ds_list_add(_mods, _out[_i]);

ntl_log("live", "[依赖排序] 加载顺序已按依赖调整（" + string(_n) + " 个 mod）");
return _n;
