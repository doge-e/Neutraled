/// ntl_console_cmds(catFilter) —— 按分类列出所有命令
var _filter = string_lower(string(argument[0]));

if (!variable_global_exists("ntl_console_cmds"))
{
    ntl_console_log(ntl_t("cmd.noreg"));
    return 0;
}
var _cmds = global.ntl_console_cmds;
var _keys = ntl_dsmap_keys(_cmds);

// 分类元数据
var _cats = ["info", "state", "action", "power", "debug", "auto", "mod"];
var _catName = [ntl_t("cat.info"), ntl_t("cat.state"), ntl_t("cat.action"), ntl_t("cat.power"),
                ntl_t("cat.debug"), ntl_t("cat.auto"), ntl_t("cat.mod")];

// 统计注册表里全部命令数（不只是本次过滤出来的）
var _regAll = 0;
for (var _k0 = 0; _k0 < array_length(_keys); _k0 += 1)
{
    var _r0 = ds_map_find_value(_cmds, string(_keys[_k0]));
    if (is_real(_r0) && ds_exists(_r0, ds_type_map)) _regAll += 1;
}
// ★ 反人类修复：以前总数只在**输出的最后一行**，长列表刷屏后根本看不到
ntl_console_log(ntl_ts("msg.cmd_total", [string(_regAll), string(array_length(_cats)), "cmds <分类>"]));

var _total = 0;
for (var _c = 0; _c < array_length(_cats); _c += 1)
{
    var _cat = _cats[_c];
    if (_filter != "" && string_pos(_filter, _cat) <= 0 && string_pos(_filter, _catName[_c]) <= 0) continue;

    // 收集本类命令
    var _list = [];
    for (var _i = 0; _i < array_length(_keys); _i += 1)
    {
        var _k = string(_keys[_i]);
        var _r = ds_map_find_value(_cmds, _k);
        if (!is_real(_r) || !ds_exists(_r, ds_type_map)) continue;
        if (string(ds_map_find_value(_r, "cat")) != _cat) continue;
        array_push(_list, _k);
    }
    if (array_length(_list) == 0) continue;

    // 排序
    for (var _a = 1; _a < array_length(_list); _a += 1)
    {
        var _cur = _list[_a]; var _b = _a - 1;
        while (_b >= 0 && _list[_b] > _cur) { _list[_b + 1] = _list[_b]; _b -= 1; }
        _list[_b + 1] = _cur;
    }

    ntl_console_log("");
    ntl_console_log("== " + _catName[_c] + " (" + string(array_length(_list)) + ") ==");
    for (var _i = 0; _i < array_length(_list); _i += 1)
    {
        var _r2 = ds_map_find_value(_cmds, _list[_i]);
        if (!is_real(_r2) || !ds_exists(_r2, ds_type_map)) continue;
        var _u = string(ds_map_find_value(_r2, "usage"));
        var _line = "  " + _list[_i];
        if (_u != "") _line += "   " + _u;
        _line += "\n      " + string(ds_map_find_value(_r2, "desc"));
        ntl_console_log(_line);
        _total += 1;
    }
}

ntl_console_log("");
ntl_console_log(ntl_tf("msg.cmd_count", _total));
ntl_console_log(ntl_t("msg.help_tip"));
return 0;
