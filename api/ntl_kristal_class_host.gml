/// ntl_kristal_class_host(name, args) —— 类系统宿主实现（__kr_Class / __kr_new_instance 等）
var _name = string(argument[0]);
var _args = argument[1];
if (_args == undefined) _args = [];
var _n = array_length(_args);
var _a0 = (_n > 0) ? _args[0] : undefined;
var _a1 = (_n > 1) ? _args[1] : undefined;

switch (_name)
{
    case "__kr_noop": return undefined;

    // Class(include, id) → 返回 (新类, super)
    case "__kr_Class":
    {
        var _cls = ntl_lua_table_new();
        var _idx = ntl_lua_table_new();
        ntl_lua_table_set(_cls, "__index", _idx);        // 实例查找走这里
        ntl_lua_table_set(_cls, "__includes", _a0);      // 记录父类
        ntl_lua_table_set(_cls, "init", ntl_lua_fn_host("__kr_noop"));
        ntl_lua_table_set(_cls, "update", ntl_lua_fn_host("__kr_noop"));
        ntl_lua_table_set(_cls, "draw", ntl_lua_fn_host("__kr_noop"));

        // 让新类可被调用（Class() 实例化）
        var _mt = ntl_lua_table_new();
        ntl_lua_table_set(_mt, "__call", ntl_lua_fn_host("__kr_new_instance"));
        ntl_lua_table_set(_cls, "_ntlmt", _mt);   // 用普通键存（setmetatable 也行）
        ntl_lua_setmetatable(_cls, _mt);

        // super 表：super.init(self, ...) 之类
        var _super = ntl_lua_table_new();
        ntl_lua_table_set(_super, "init", ntl_lua_fn_host("__kr_super_init"));
        ntl_lua_table_set(_super, "update", ntl_lua_fn_host("__kr_noop"));
        ntl_lua_table_set(_super, "draw", ntl_lua_fn_host("__kr_noop"));
        ntl_lua_table_set(_cls, "__super", _super);

        // 多返回值
        var _out = [_cls, _super];
        return _out;
    }

    // 实例化：new(cls, ...) —— 建表 + 设元表 + 调 init
    case "__kr_new_instance":
    {
        var _cls = _a0;
        if (_cls == undefined || !is_real(_cls)) return undefined;
        var _obj = ntl_lua_table_new();
        ntl_lua_setmetatable(_obj, _cls);                 // 元表 = 类 → 方法查找走 __index

        // 收集构造参数（跳过 self=类本身）
        var _initArgs = [_obj];
        for (var _i = 1; _i < _n; _i += 1) array_push(_initArgs, _args[_i]);

        var _initFn = ntl_lua_index_get(_cls, "init");
        if (_initFn != undefined && (ntl_lua_is_fn(_initFn) || is_string(_initFn)))
            ntl_lua_call(_initFn, _initArgs);
        return _obj;
    }

    // super.init(self, ...) —— 调父类的 init
    case "__kr_super_init":
    {
        // 参数：self（实例）
        var _self = _a0;
        if (_self == undefined || !is_real(_self)) return undefined;
        var _mtcls = ntl_lua_getmetatable(_self);
        if (_mtcls == undefined || !is_real(_mtcls)) return undefined;
        var _parent = ntl_lua_table_get(_mtcls, "__includes");
        if (_parent == undefined || !is_real(_parent)) return undefined;
        var _pinit = ntl_lua_index_get(_parent, "init");
        if (_pinit != undefined && (ntl_lua_is_fn(_pinit) || is_string(_pinit)) && _pinit != _mtcls)
        {
            var _pa = [_self];
            for (var _j = 1; _j < _n; _j += 1) array_push(_pa, _args[_j]);
            return ntl_lua_call(_pinit, _pa);
        }
        return undefined;
    }

    // _Class.extend(...) —— 简易继承
    case "__kr_class_extend":
    {
        var _parentCls = _a0;
        var _new = ntl_lua_table_new();
        var _nm = ntl_lua_table_new();
        ntl_lua_table_set(_new, "__index", _nm);
        ntl_lua_table_set(_new, "__includes", _parentCls);
        var _m2 = ntl_lua_table_new();
        ntl_lua_table_set(_m2, "__call", ntl_lua_fn_host("__kr_new_instance"));
        ntl_lua_setmetatable(_new, _m2);
        return _new;
    }
}
return undefined;
