/// ntl_kristal_world_init() —— 把 Kristal 的 Game.world 映射到 Neutraled 的对象管理器
///
/// Kristal 的关卡系统核心 API：
///   Game.world:addChild(obj)          加入场景
///   Game.world:removeChild(obj)       移除
///   Game.world:getCharacter(name)     取得队伍角色
///   Game.world.characters             角色列表
///   Game.world:hasCharacter(name)
///   Game.world:getSolid(x, y)         碰撞查询
///   Game.world:detect(x, y, w, h)     区域检测
///
/// 我们把它接到：
///   - ntl_obj_*（对象实例管理器）
///   - 地图碰撞系统（ntl_map_collide）
var _g = global.ntl_lua_globals;
if (_g == undefined) return 0;

var _world = ntl_lua_table_get(_g, "Game");
if (_world == undefined || !is_real(_world)) return 0;
var _worldTbl = ntl_lua_index_get(_world, "world");
if (_worldTbl == undefined || !is_real(_worldTbl))
{
    _worldTbl = ntl_lua_table_new();
    ntl_lua_index_set(_world, "world", _worldTbl);
}

// ---- addChild：加入对象管理器 ----
var _add = ntl_lua_table_new();
ntl_lua_table_set(_add, "_ntlfn", 1);
ntl_lua_table_set(_add, "_ntlctrl", "__krfn");
ntl_lua_table_set(_add, "_krname", "__kr_world_addChild");
ntl_lua_index_set(_worldTbl, "addChild", _add);

// ---- removeChild ----
var _rem = ntl_lua_table_new();
ntl_lua_table_set(_rem, "_ntlfn", 1);
ntl_lua_table_set(_rem, "_ntlctrl", "__krfn");
ntl_lua_table_set(_rem, "_krname", "__kr_world_removeChild");
ntl_lua_index_set(_worldTbl, "removeChild", _rem);

// ---- getCharacter：从游戏里找真实角色实例 ----
var _gc = ntl_lua_table_new();
ntl_lua_table_set(_gc, "_ntlfn", 1);
ntl_lua_table_set(_gc, "_ntlctrl", "__krfn");
ntl_lua_table_set(_gc, "_krname", "__kr_world_getCharacter");
ntl_lua_index_set(_worldTbl, "getCharacter", _gc);

ntl_lua_index_set(_worldTbl, "hasCharacter", _gc);

// ---- 碰撞查询 ----
var _solid = ntl_lua_table_new();
ntl_lua_table_set(_solid, "_ntlfn", 1);
ntl_lua_table_set(_solid, "_ntlctrl", "__krfn");
ntl_lua_table_set(_solid, "_krname", "__kr_world_getSolid");
ntl_lua_index_set(_worldTbl, "getSolid", _solid);

// ---- 角色列表 ----
var _chars = ntl_lua_table_new();
ntl_lua_index_set(_worldTbl, "characters", _chars);

// ---- 常用标志位 ----
ntl_lua_index_set(_worldTbl, "can_open_menu", 1);
ntl_lua_index_set(_worldTbl, "isDarkWorld", 1);
ntl_lua_index_set(_worldTbl, "map", ntl_lua_table_new());

ntl_log("kristal", "Game.world 已映射（addChild / removeChild / getCharacter / getSolid）");
return 1;
