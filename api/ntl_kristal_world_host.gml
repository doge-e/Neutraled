/// ntl_kristal_world_host(name, args) —— Game.world 方法的宿主实现
var _name = string(argument[0]);
var _args = argument[1];
if (_args == undefined) _args = [];
var _n = array_length(_args);
var _a0 = (_n > 0) ? _args[0] : undefined;
var _a1 = (_n > 1) ? _args[1] : undefined;

switch (_name)
{
    case "__kr_world_addChild":
    {
        // Kristal: world:addChild(obj) —— obj 是 Lua 表（我们自己创建的）
        if (_a0 == undefined || !is_real(_a0)) return undefined;
        // 用 x/y 建一个引擎侧包装，让它可以被统一更新
        var _x = ntl_lua_index_get(_a0, "x");
        var _y = ntl_lua_index_get(_a0, "y");
        if (_x == undefined) _x = 0;
        if (_y == undefined) _y = 0;

        var _o = ds_map_create();
        ds_map_add(_o, "class", "kristal.child");
        ds_map_add(_o, "lua", _a0);
        ds_map_add(_o, "x", _x);
        ds_map_add(_o, "y", _y);
        ds_map_add(_o, "vx", 0);
        ds_map_add(_o, "vy", 0);
        ds_map_add(_o, "visible", 1);
        ds_map_add(_o, "active", 1);
        ds_map_add(_o, "w", 20);
        ds_map_add(_o, "h", 20);

        if (!variable_global_exists("ntl_objects")) ntl_obj_init();
        ds_list_add(global.ntl_objects, _o);
        global.ntl_obj_count += 1;
        return _a0;
    }

    case "__kr_world_removeChild":
    {
        if (_a0 == undefined || !is_real(_a0)) return undefined;
        if (!variable_global_exists("ntl_objects")) return undefined;
        var _ln = ds_list_size(global.ntl_objects);
        for (var _i = 0; _i < _ln; _i += 1)
        {
            var _o2 = ds_list_find_value(global.ntl_objects, _i);
            if (!is_real(_o2)) continue;
            if (ds_map_find_value(_o2, "lua") == _a0)
            {
                ds_map_replace(_o2, "active", 0);
                ds_map_replace(_o2, "visible", 0);
                global.ntl_obj_count -= 1;
                return 1;
            }
        }
        return 0;
    }

    case "__kr_world_getCharacter":
    case "__kr_world_hasCharacter":
    {
        // 从游戏里找角色对象（DELTARUNE 的角色对象命名规律）
        var _want = string(_a0);
        var _cands = ["obj_kris", "obj_susie", "obj_ralsei", "obj_noelle",
                      "obj_berdly", "obj_lancer", "obj_ralsei_dw"];
        var _found = undefined;
        for (var _c = 0; _c < array_length(_cands); _c += 1)
        {
            var _cn = _cands[_c];
            if (string_pos(_want, string_lower(_cn)) <= 0 && string_pos(string_lower(_want), _cn) <= 0) continue;
            var _obj = asset_get_index(_cn);
            if (_obj < 0) continue;
            if (instance_number(_obj) > 0) { _found = instance_find(_obj, 0); break; }
        }
        if (_found == undefined) return undefined;
        // 包成 Lua 表，带常用字段
        var _t = ntl_lua_table_new();
        ntl_lua_table_set(_t, "id", _found);
        ntl_lua_table_set(_t, "x", _found.x);
        ntl_lua_table_set(_t, "y", _found.y);
        ntl_lua_table_set(_t, "name", _want);
        ntl_lua_table_set(_t, "hp", variable_instance_exists(_found, "hp") ? _found.hp : 100);
        return _t;
    }

    case "__kr_world_getSolid":
    {
        // 用地图碰撞系统查询
        var _sx = real(_a0);
        var _sy = real(_a1);
        if (variable_global_exists("ntl_test_map"))
        {
            var _hit = ntl_map_collide(global.ntl_test_map, _sx, _sy, 1, 1);
            if (_hit) return 1;
        }
        return 0;
    }
}
return undefined;
