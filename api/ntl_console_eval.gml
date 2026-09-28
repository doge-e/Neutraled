/// ntl_console_eval(expr) —— 用 Lua 求值任意表达式（开发调试利器）
/// 例：eval 1+2 / eval Kristal.getFlag("wr_set") / eval ntl_sprite_resolve(5)
var _expr = string(argument[0]);
if (string_length(_expr) <= 0)
{
    ntl_console_log(ntl_t("eval.u"));
    return 0;
}

// 包成 "local __r = (表达式)" 以便拿到值
//   ★ 表达式出错时执行**不会**中断（ntl_lua_rt_err 记错误后继续），此时 __r 没被赋值，
//   而查未定义全局名会回退成占位符 "__host:__r"（非 nil）⇒ 旧写法会先打一行假的 "= __host:__r"，
//   把真正的 [求值失败] 淹没。这里把该占位符判掉（真实结果恰好等于这个串的概率可忽略）。
var _src = "local __r = (" + _expr + ")\nlocal __s = tostring(__r)\nif __s ~= \"__host:__r\" then ntl_console_log(\"= \" .. __s) end";

// 全局 Lua 环境（与 live 脚本共用；早期调用时按需建立）
if (!variable_global_exists("ntl_lua_globals"))
{
    global.ntl_lua_globals = ntl_lua_table_new();
    ntl_lua_stdlib();
}
else if (!ds_map_exists(global.ntl_lua_globals, "sprint_check")) ntl_lua_stdlib();
if (!variable_global_exists("ntl_lua_env")) global.ntl_lua_env = ntl_lua_env_new(global.ntl_lua_globals);

global.ntl_lua_err = "";
try
{
    // 直接走 Lua 编译路径：ntl_live_compile 只对 .lua 结尾的路径走 Lua 分支，
    // 而 "<eval>" 会落进 NTL 解析器（NTL 不支持 local）。
    // 修复前的写法 ntl_live_compile(_src, "<eval>") 参数顺序颠倒且缺 cache → eval 恒报 [求值异常]。
    var _lc = ntl_lua_compile(_src);
    if (ds_map_find_value(_lc, "ok") != 1)
    {
        ntl_console_log(ntl_t("out.syntax") + string(ds_map_find_value(_lc, "err")));
        ds_map_destroy(_lc);
        return 0;
    }
    ntl_lua_ev_block(global.ntl_lua_env, ds_map_find_value(_lc, "ast"));
    ds_map_destroy(_lc);
}
catch (e)
{
    ntl_console_log(ntl_t("eval.exc") + string(e));
    return 0;
}
if (global.ntl_lua_err != "")
{
    ntl_console_log(ntl_t("eval.fail") + ntl_err_friendly(global.ntl_lua_err, "<eval>", -1));
    global.ntl_lua_err = "";
}
return 0;
