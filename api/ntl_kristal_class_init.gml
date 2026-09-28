/// ntl_kristal_class_init() —— Kristal 类系统与基类（Object / Sprite / Text / EnemyBattler 等）
/// 用 Neutraled 的 Lua 元表实现 Kristal 的 Class(include, id) 约定：
///   local MyClass, super = Class(Object)
///   function MyClass:init() super.init(self) ... end
ntl_log("kristal", "[class] 1 开始");
if (!variable_global_exists("ntl_lua_globals")) { ntl_log("kristal", "[class] 无 globals"); return 0; }
var _g = global.ntl_lua_globals;
ntl_log("kristal", "[class] 2 globals ok");

// ---------- 1) _Class（hump 风格基类）----------
var _base = ntl_lua_table_new();
ntl_lua_table_set(_base, "init", ntl_lua_fn_host("__kr_noop"));
ntl_lua_table_set(_base, "update", ntl_lua_fn_host("__kr_noop"));
ntl_lua_table_set(_base, "draw", ntl_lua_fn_host("__kr_noop"));
ntl_lua_table_set(_base, "extend", ntl_lua_fn_host("__kr_class_extend"));
ntl_lua_table_set(_base, "__index", ntl_lua_table_new());   // 占位，实际由 extend 设置
ntl_lua_table_set(_g, "_Class", _base);
ntl_log("kristal", "[class] 3 _Class ok");

// ---------- 2) Class(include, id) ----------
ntl_lua_table_set(_g, "Class", ntl_lua_fn_host("__kr_Class"));
ntl_log("kristal", "[class] 4 Class ok");

// ---------- 3) 引擎基类（都是"空壳"，能被 Class 继承即可）----------
var _baseNames = ["Object", "Sprite", "Text", "Shape", "Actor", "Battler",
                  "EnemyBattler", "PartyBattler", "Bullet", "Arena", "Stage",
                  "World", "GameObject", "UIElement", "Frame", "Layout",
                  "Cutscene", "Wave", "Encounter", "Shop", "Light", "Shader"];
ntl_log("kristal", "[class] 5 开始建基类，共 " + string(array_length(_baseNames)) + " 个");
for (var _i = 0; _i < array_length(_baseNames); _i += 1)
{
    var _nm = _baseNames[_i];
    if (ntl_lua_table_get(_g, _nm) == undefined)
    {
        var _c = ntl_lua_table_new();
        var _ci = ntl_lua_table_new();
        ntl_lua_table_set(_ci, "init", ntl_lua_fn_host("__kr_noop"));
        ntl_lua_table_set(_ci, "update", ntl_lua_fn_host("__kr_noop"));
        ntl_lua_table_set(_ci, "draw", ntl_lua_fn_host("__kr_noop"));
        ntl_lua_table_set(_ci, "remove", ntl_lua_fn_host("__kr_noop"));
        ntl_lua_table_set(_ci, "addChild", ntl_lua_fn_host("__kr_noop"));
        ntl_lua_table_set(_ci, "setActor", ntl_lua_fn_host("__kr_noop"));
        ntl_lua_table_set(_ci, "setSprite", ntl_lua_fn_host("__kr_noop"));
        ntl_lua_table_set(_ci, "setColor", ntl_lua_fn_host("__kr_noop"));
        ntl_lua_table_set(_ci, "setText", ntl_lua_fn_host("__kr_noop"));
        ntl_lua_table_set(_ci, "getCharacter", ntl_lua_fn_host("__kr_noop"));
        ntl_lua_table_set(_ci, "addChild", ntl_lua_fn_host("__kr_noop"));
        ntl_lua_table_set(_c, "__index", _ci);
        ntl_lua_table_set(_c, "init", ntl_lua_fn_host("__kr_noop"));
        ntl_lua_table_set(_g, _nm, _c);
    }
}

// ---------- 4) 工厂函数（Text(...) / Sprite(...) 等直接调用即创建实例）----------
var _factories = ["Text", "Sprite", "Shape", "Frame", "Layout", "Object",
                  "Bullet", "Arena", "Cutscene", "Wave"];
// 注意：这些名字同时是基类表 —— Kristal 里它们既可被继承也可被调用（Class 实例可调用）
// 这里给每个基类表加 __call 元方法，使其可被当作构造函数使用
ntl_log("kristal", "[class] 6 基类建完，开始工厂");
for (var _j = 0; _j < array_length(_factories); _j += 1)
{
    var _fn = _factories[_j];
    var _cls = ntl_lua_table_get(_g, _fn);
    if (_cls == undefined || !is_real(_cls)) continue;
    var _mt = ntl_lua_getmetatable(_cls);
    if (_mt == undefined || !is_real(_mt))
    {
        _mt = ntl_lua_table_new();
        ntl_lua_setmetatable(_cls, _mt);
    }
    ntl_lua_table_set(_mt, "__call", ntl_lua_fn_host("__kr_new_instance"));
}

ntl_log("kristal", "类系统已注册（Class + " + string(array_length(_baseNames)) + " 个基类）");
return 1;