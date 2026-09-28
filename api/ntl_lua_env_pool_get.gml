/// ntl_lua_env_pool_get(parent, key) —— 从池里取一个可复用的子环境
///
/// ★ 为什么需要：ntl_lua_ev_stat 的 if / do 分支原本每次执行都
///   ntl_lua_env_new()（= ds_map_create()），在循环体里会反复创建且不释放。
///   实测 fornum 的环境复用让累加基准从 2.251s 降到 1.894s。
///
/// 池的键 = 父环境句柄 + 用途标记，保证：
///   - 同一父环境下的同种块复用同一个子环境
///   - 不同父环境（嵌套调用）互不干扰
if (!variable_global_exists("ntl_lua_env_pool")) global.ntl_lua_env_pool = ds_map_create();

var _parent = argument[0];
var _key = string(argument[1]);
var _pk = string(_parent) + "|" + _key;

if (ds_map_exists(global.ntl_lua_env_pool, _pk))
{
    var _env = ds_map_find_value(global.ntl_lua_env_pool, _pk);
    if (is_real(_env) && ds_exists(_env, ds_type_map))
    {
        ds_map_clear(_env);
        ds_map_add(_env, "_ntlp", _parent);
        return _env;
    }
}

var _new = ntl_lua_env_new(_parent);
ds_map_add(global.ntl_lua_env_pool, _pk, _new);
return _new;
