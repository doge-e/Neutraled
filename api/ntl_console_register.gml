/// ntl_console_register(name, desc, fn, usage, cat, handler) —— 注册控制台命令
///
/// 参数：
///   name    : 命令名。内置命令直接用名字（help / mods / eval）；
///             mod 注册时自动加前缀 "modid:"（由 ntl_console_register_mod 处理）
///   desc    : 一句话说明
///   fn      : 脚本资源索引（-1 = 由 ntl_console_exec 内部分派）
///   usage   : 参数提示（可选），如 "goto <章节号>"
///   cat     : 分类（可选）：info / state / action / debug / auto / mod
///   handler : "builtin"（默认）或 "lua:<路径>"（mod 提供的脚本）
///
/// 注册表结构：global.ntl_console_cmds[name] = ds_map{ desc, fn, usage, cat, scope, mod, handler }
var _name = string(argument[0]);
var _desc = string(argument[1]);
var _fn = -1;
if (argument_count > 2) _fn = argument[2];
var _usage = (argument_count > 3) ? string(argument[3]) : "";
var _cat = (argument_count > 4) ? string(argument[4]) : "info";
var _handler = (argument_count > 5) ? string(argument[5]) : "builtin";

if (!variable_global_exists("ntl_console_cmds")) global.ntl_console_cmds = ds_map_create();

var _rec = ds_map_create();
ds_map_add(_rec, "desc", _desc);
ds_map_add(_rec, "fn", _fn);
ds_map_add(_rec, "usage", _usage);
ds_map_add(_rec, "cat", _cat);
ds_map_add(_rec, "scope", "ntl");
ds_map_add(_rec, "mod", "");
ds_map_add(_rec, "handler", _handler);

if (ds_map_exists(global.ntl_console_cmds, _name)) ds_map_replace(global.ntl_console_cmds, _name, _rec);
else ds_map_add(global.ntl_console_cmds, _name, _rec);
return 1;
