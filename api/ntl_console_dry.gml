/// ntl_console_dry(rest) —— dry [on|off]：破坏性命令的干跑（演练）开关
///   ★ 反人类修复（用户建议）：goto / loadmap / warp / spawn / destroy / setvar / setflag
///   以前一敲就生效（destroy 能一次干掉几十个实例），想先看看"会发生什么"完全没办法。
///   打开干跑后，这些命令照样做参数校验（房间不存在/对象不存在照样报错），
///   但只打印「将要执行 …」，一个字节的游戏状态都不改。
var _rest = string_lower(string_trim(string(argument[0])))
if (!variable_global_exists("ntl_console_dry")) global.ntl_console_dry = 0;

if (_rest == "on" || _rest == "1" || _rest == "true" || _rest == "yes")
{
    global.ntl_console_dry = 1;
    ntl_console_log(ntl_t("dry.on"));
    ntl_console_log(ntl_t("dry.list"));
    return 0;
}

if (_rest == "off" || _rest == "0" || _rest == "false" || _rest == "no")
{
    global.ntl_console_dry = 0;
    ntl_console_log(ntl_t("dry.off"));
    return 0;
}

if (_rest == "" || _rest == "status")
{
    if (global.ntl_console_dry)
    {
        ntl_console_log(ntl_t("dry.on"));
        ntl_console_log(ntl_t("dry.list"));
    }
    else ntl_console_log(ntl_t("dry.off"));
    ntl_console_log(ntl_t("dry.usage"));
    return 0;
}

ntl_console_log(ntl_t("dry.usage"));
return 0;
