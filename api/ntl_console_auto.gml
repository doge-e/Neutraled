/// ntl_console_auto(cmd, rest) —— 自动化类命令实现
var _cmd = string(argument[0]);
var _rest = (argument_count > 1) ? string(argument[1]) : "";
var _args = ntl_live_split_args(_rest);

if (_cmd == "alias_split")
{
    if (array_length(_args) < 2) { ntl_console_alias("", ""); return 0; }   // 列出
    // 把剩余部分作为展开值（支持带空格的命令串）
    var _name = _args[0];
    var _exp = string_delete(_rest, 1, string_length(_name));
    ntl_console_alias(_name, string_trim(_exp));
    return 0;
}

if (_cmd == "bind_split")
{
    if (array_length(_args) < 2) { ntl_console_bind("", ""); return 0; }    // 列出
    var _key = _args[0];
    var _cmd2 = string_delete(_rest, 1, string_length(_key));
    ntl_console_bind(_key, string_trim(_cmd2));
    return 0;
}

if (_cmd == "macro")
{
    var _sub = (array_length(_args) > 0) ? _args[0] : "";
    if (!variable_global_exists("ntl_macro"))
    {
        global.ntl_macro = [];
        global.ntl_macro_rec = 0;
    }
    if (_sub == "start")
    {
        array_resize(global.ntl_macro, 0);
        global.ntl_macro_rec = 1;
        ntl_console_log(ntl_t("macro.start"));
    }
    else if (_sub == "stop")
    {
        global.ntl_macro_rec = 0;
        ntl_console_log(ntl_ts("macro.stop", [array_length(global.ntl_macro)]));
    }
    else if (_sub == "play")
    {
        ntl_console_log(ntl_ts("macro.play", [array_length(global.ntl_macro)]));
        for (var _i = 0; _i < array_length(global.ntl_macro); _i += 1)
            ntl_console_exec(string(global.ntl_macro[_i]));
    }
    else if (_sub == "show")
    {
        ntl_console_log(ntl_t("macro.show"));
        for (var _i = 0; _i < array_length(global.ntl_macro); _i += 1)
            ntl_console_log("  " + string(global.ntl_macro[_i]));
        if (array_length(global.ntl_macro) == 0) ntl_console_log(ntl_t("macro.empty"));
    }
    else ntl_console_log(ntl_t("macro.u"));
    return 0;
}

if (_cmd == "loop")
{
    if (array_length(_args) < 2) { ntl_console_log(ntl_t("loop.u")); return 0; }
    var _times = real(_args[0]);
    if (_times <= 0 || _times > 1000) { ntl_console_log(ntl_t("loop.range")); return 0; }
    var _c = string_delete(_rest, 1, string_length(_args[0]));
    _c = string_trim(_c);
    for (var _i = 0; _i < _times; _i += 1) ntl_console_exec(_c);
    return 0;
}

return 0;
