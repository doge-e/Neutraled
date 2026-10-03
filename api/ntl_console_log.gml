// ntl_console_log(text [, force]) —— 输出到控制台面板（按换行拆分，多行不重叠）
// 可选 force=1：无视当前日志过滤器强制输出（filter 命令自己的回显用它）
var _text = string(argument[0]);
var _force = (argument_count > 1 && argument[1] == 1) ? 1 : 0;

// ★ F5 接线 ntl_console_line_pass：filter 命令设置的模式在这里真正生效
//   （以前 filter 只写 global，没有任何过滤点，所以静默无效）
if (_force == 0 && ntl_console_line_pass(_text) == 0) return 0;

if (!variable_global_exists("ntl_console_lines")) global.ntl_console_lines = ds_list_create();

var _parts = string_split(_text, chr(10));
var _n = 0;
// ★ 反人类修复：超过面板宽度的行会被右边缘**静默裁掉**（长路径、长提示的结尾用户永远看不到）。
//   这里按画面缓存下来的字宽把长行切成多行再入库 —— 滚动、行号、save 导出全部保持 1 行 1 条。
var _ww = variable_global_exists("ntl_console_wrap_w") ? global.ntl_console_wrap_w : 0;
var _wa = variable_global_exists("ntl_cw_ascii") ? global.ntl_cw_ascii : 0;
var _wc = variable_global_exists("ntl_cw_cjk") ? global.ntl_cw_cjk : 0;
for (var _i = 0; _i < array_length(_parts); _i += 1)
{
    var _ln = string(_parts[_i]);
    if (_ww <= 0 || _wa <= 0 || _wc <= 0 || string_length(_ln) <= 0)
    {
        ds_list_add(global.ntl_console_lines, _ln);
        _n += 1;
        continue;
    }
    var _cur = "";
    var _curw = 0;
    var _lnn = string_length(_ln);
    for (var _ci = 1; _ci <= _lnn; _ci += 1)
    {
        var _ch = string_char_at(_ln, _ci);
        var _cwd = (ord(_ch) > 127) ? _wc : _wa;
        if (_curw + _cwd > _ww && string_length(_cur) > 0)
        {
            ds_list_add(global.ntl_console_lines, _cur);
            _n += 1;
            _cur = "";
            _curw = 0;
        }
        _cur += _ch;
        _curw += _cwd;
    }
    ds_list_add(global.ntl_console_lines, _cur);
    _n += 1;
}

// ★ 用户主诉修复：缓冲区从 400 行放大到 20000 行 —— api/mods/objs 等命令不再限制条数后，
//   一次输出几千行是正常的，400 行会把用户最想看的开头直接挤掉。
//   仍保留上限（防内存失控），丢掉的条数记在 global.ntl_console_dropped，由标题行与导出头部显示。
if (!variable_global_exists("ntl_console_dropped")) global.ntl_console_dropped = 0;
while (ds_list_size(global.ntl_console_lines) > 20000)
{
    ds_list_delete(global.ntl_console_lines, 0);
    global.ntl_console_dropped += 1;
}
// 新内容到来时：在底部就自动跟随；**已经翻到上面就把视口钉住**
//   （scroll 是"距底部行数"，不跟着加就会每来一行整体上跳一行 —— 这就是"滚动出现了问题"）
var _added = (_n <= 0) ? 1 : _n;
if (variable_global_exists("ntl_console_autoscroll") && global.ntl_console_autoscroll == 1) global.ntl_console_scroll = 0;
else if (variable_global_exists("ntl_console_scroll")) global.ntl_console_scroll += _added;

return 0;
