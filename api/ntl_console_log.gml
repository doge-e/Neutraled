// ntl_console_log(text [, force]) —— 输出到控制台面板（按换行拆分，多行不重叠）
// 可选 force=1：无视当前日志过滤器强制输出（filter 命令自己的回显用它）
var _text = string(argument[0]);
var _force = (argument_count > 1 && argument[1] == 1) ? 1 : 0;

// ★ F5 接线 ntl_console_line_pass：filter 命令设置的模式在这里真正生效
//   （以前 filter 只写 global，没有任何过滤点，所以静默无效）
if (_force == 0 && ntl_console_line_pass(_text) == 0) return 0;

if (!variable_global_exists("ntl_console_lines")) global.ntl_console_lines = ds_list_create();

var _parts = string_split(_text, chr(10));
var _n = array_length(_parts);
if (_n <= 0) ds_list_add(global.ntl_console_lines, "");
else for (var _i = 0; _i < _n; _i++) ds_list_add(global.ntl_console_lines, _parts[_i]);

while (ds_list_size(global.ntl_console_lines) > 200) ds_list_delete(global.ntl_console_lines, 0);
// 新内容到来时：在底部就自动跟随；**已经翻到上面就把视口钉住**
//   （scroll 是"距底部行数"，不跟着加就会每来一行整体上跳一行 —— 这就是"滚动出现了问题"）
var _added = (_n <= 0) ? 1 : _n;
if (variable_global_exists("ntl_console_autoscroll") && global.ntl_console_autoscroll == 1) global.ntl_console_scroll = 0;
else if (variable_global_exists("ntl_console_scroll")) global.ntl_console_scroll += _added;

return 0;
