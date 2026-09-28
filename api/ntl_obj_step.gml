/// ntl_obj_step() —— 每帧更新所有 Kristal 对象实例
if (!variable_global_exists("ntl_objects")) return 0;

var _n = ds_list_size(global.ntl_objects);
var _updated = 0;
var _i = 0;
while (_i < _n)
{
    var _o = ds_list_find_value(global.ntl_objects, _i);
    _i += 1;
    if (!is_real(_o)) continue;
    if (ds_map_find_value(_o, "active") != 1) continue;

    var _inst = ds_map_find_value(_o, "lua");
    if (_inst == undefined || !is_real(_inst)) continue;

    // 调 Lua 侧的 update（若定义了）
    var _up = ntl_lua_index_get(_inst, "update");
    if (_up != undefined && (ntl_lua_is_fn(_up) || is_string(_up)))
    {
        global.ntl_lua_err = "";
        ntl_lua_call(_up, [_inst, delta_time / 1000000]);
        if (global.ntl_lua_err != "")
        {
            ntl_log("obj", "[update 错误] " + string(ds_map_find_value(_o, "class")) + ": " + global.ntl_lua_err);
            global.ntl_lua_err = "";
        }
        _updated += 1;
    }

    // 同步位置（Lua 侧可能改了 x/y）
    var _lx = ntl_lua_index_get(_inst, "x");
    var _ly = ntl_lua_index_get(_inst, "y");
    if (_lx != undefined && is_real(_lx)) ds_map_replace(_o, "x", _lx);
    if (_ly != undefined && is_real(_ly)) ds_map_replace(_o, "y", _ly);
}
return _updated;
