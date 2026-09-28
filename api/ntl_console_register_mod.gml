/// ntl_console_register_mod(name, desc, handler, usage) —— mod 注册控制台命令
///
/// ★ 范围解析规则（用户要求）：
///   - **ntl 内置命令不需要前缀**：直接 help / mods / eval
///   - **mod 命令必须带命名空间前缀**：my.mod:heal
///   - 执行时：**先查内置**，找不到再按 "modid:name" 查 mod 命令
///   - 同名冲突：不同 mod 的命令互不影响（各自在自己的命名空间里）
///
/// 用法（mod 的 main.lua）：
///   ntl_console_register_mod("heal", "恢复满血", "console/heal.lua", "heal [目标]")
///
/// 之后玩家可以在控制台输入：
///   > my.mod:heal            （带前缀）
///   > help my.mod:heal       （查用法）
var _name = string(argument[0]);
var _desc = string(argument[1]);
var _handler = (argument_count > 2) ? string(argument[2]) : "";
var _usage = (argument_count > 3) ? string(argument[3]) : "";

// 当前 mod id（由 ntl_live_emit 设置）
var _mod = "";
if (variable_global_exists("ntl_current_mod")) _mod = string(global.ntl_current_mod);
if (_mod == "") _mod = "unknown";

var _full = _mod + ":" + _name;
if (!variable_global_exists("ntl_console_cmds")) global.ntl_console_cmds = ds_map_create();

var _rec = ds_map_create();
ds_map_add(_rec, "desc", _desc);
ds_map_add(_rec, "fn", -1);
ds_map_add(_rec, "usage", _usage);
ds_map_add(_rec, "cat", "mod");
ds_map_add(_rec, "scope", "mod");
ds_map_add(_rec, "mod", _mod);
ds_map_add(_rec, "handler", _handler);

if (ds_map_exists(global.ntl_console_cmds, _full)) ds_map_replace(global.ntl_console_cmds, _full, _rec);
else ds_map_add(global.ntl_console_cmds, _full, _rec);

ntl_log("console", "[命令] " + _full + " 已注册（mod " + _mod + "）");
return 1;
