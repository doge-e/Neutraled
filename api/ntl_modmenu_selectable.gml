/// ntl_modmenu_selectable(i) —— 第 i 行能不能被光标选中（分组标题只用来排版，必须跳过）
/// 行结构见 ntl_modmenu_rows：kind == 1 就是分组标题；越界 / 空行返回 0。
var _r = ntl_modmenu_row(argument[0]);
if (array_length(_r) <= 0) return 0;
if (array_length(_r) < 5) return 1;              // 兼容只有 4 元组的老式行
return (real(_r[4]) != 1);
