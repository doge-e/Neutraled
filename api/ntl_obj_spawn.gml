/// ntl_obj_spawn(classPath, x, y, opts) —— 实例化一个 Kristal 对象
///   classPath: Lua require 路径（如 mods.xxx.scripts.objects.Foo）
///   x, y: 初始位置
///   opts: ds_map（可选，写入实例字段）
/// 返回实例 ds_map（含 class/inst/x/y/vx/vy/visible/lua），失败返回 undefined
ntl_obj_init();
var _path = string(argument[0]);
var _x = (argument_count > 1) ? real(argument[1]) : 0;
var _y = (argument_count > 2) ? real(argument[2]) : 0;

// 加载类（走 Lua require）
var _r = ntl_require_lua(_path);
var _cls = ds_map_find_value(_r, "value");
ds_map_destroy(_r);
if (_cls == undefined || !is_real(_cls))
{
    ntl_log("obj", "[错误] 类加载失败: " + _path);
    return undefined;
}

// 实例化（调用类的 __call）
var _inst = ntl_lua_call(_cls, []);
if (_inst == undefined)
{
    ntl_log("obj", "[错误] 实例化失败: " + _path);
    return undefined;
}

// 引擎侧包装
var _o = ds_map_create();
ds_map_add(_o, "class", _path);
ds_map_add(_o, "lua", _inst);
ds_map_add(_o, "x", _x);
ds_map_add(_o, "y", _y);
ds_map_add(_o, "vx", 0);
ds_map_add(_o, "vy", 0);
ds_map_add(_o, "visible", 1);
ds_map_add(_o, "active", 1);
ds_map_add(_o, "w", 20);
ds_map_add(_o, "h", 30);

// 回写位置到 Lua 实例
ntl_lua_index_set(_inst, "x", _x);
ntl_lua_index_set(_inst, "y", _y);

ds_list_add(global.ntl_objects, _o);
global.ntl_obj_count += 1;

ntl_log("obj", "已实例化: " + _path + " @(" + string(_x) + "," + string(_y) + ") 当前 " + string(global.ntl_obj_count) + " 个");
return _o;
