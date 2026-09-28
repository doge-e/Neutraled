/// ntl_modmenu_slot(view) —— 面板视图 → 游戏 submenucoord 的槽位
/// 每个视图各占一个槽（和游戏给每个子菜单一个 submenucoord[id] 的做法一致）：
/// main=51 / chapters=52 / langs=53 / mods=54；51 同时是「面板开着」的 submenu id。
var _v = string(argument[0]);
if (_v == "chapters") return 52;
if (_v == "langs") return 53;
if (_v == "mods") return 54;
return 51;
