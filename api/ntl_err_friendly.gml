/// ntl_err_friendly(err, ctxFile, ctxLine) —— 把 Lua 的英文报错翻译成中文 + 给出修改建议
/// 返回形如：
///   [文件:行] 调用了不存在的函数
///     Lua:  attempt to call a nil value (global 'foo')
///     建议:  检查函数名拼写；或用 pcall 包住调用
var _err = string(argument[0]);
var _file = (argument_count > 1) ? string(argument[1]) : "";
var _line = (argument_count > 2) ? argument[2] : -1;

if (_err == "") return "";

// ---- ★ 幂等保护：输入已经是本函数渲染过的友好文本（ntl_lua_rt_err 会先渲染一次并存进
//      global.ntl_lua_err），此时只补位置前缀，别再映射一遍——否则调用方（如 eval）再渲染一次
//      会把 "Lua: …" 原句与 "建议: …" 重复打印两遍。
var _already = (string_pos(chr(10) + "        Lua:  ", _err) > 0) || (string_pos("        建议: ", _err) > 0 || string_pos("        advice: ", _err) > 0);
if (_already)
{
    var _loc0 = "";
    if (_file != "")
    {
        _loc0 = _file;
        if (_line >= 0) _loc0 += ":" + string(_line);
        _loc0 = "[" + _loc0 + "] ";
    }
    return _loc0 + _err;
}

var _zh = "";
var _advice = "";

// ---- 常见 Lua 错误的中文映射 ----
if (string_pos("attempt to call", _err) > 0)
{
    _zh = ntl_t("err.call");
    _advice = ntl_t("adv.call");
}
else if (string_pos("attempt to index", _err) > 0)
{
    _zh = ntl_t("err.index");
    _advice = ntl_t("adv.index");
}
else if (string_pos("attempt to perform arithmetic", _err) > 0)
{
    _zh = ntl_t("err.arith");
    _advice = ntl_t("adv.arith");
}
else if (string_pos("attempt to compare", _err) > 0)
{
    _zh = ntl_t("err.compare");
    _advice = ntl_t("adv.compare");
}
else if (string_pos("attempt to concatenate", _err) > 0)
{
    _zh = ntl_t("err.concat");
    _advice = ntl_t("adv.concat");
}
else if (string_pos("'end' expected", _err) > 0 || string_pos("'end'", _err) > 0)
{
    _zh = ntl_t("err.end");
    _advice = ntl_t("adv.end");
}
else if (string_pos("unexpected symbol", _err) > 0 || string_pos("unexpected token", _err) > 0)
{
    _zh = ntl_t("err.syntax");
    _advice = ntl_t("adv.syntax");
}
else if (string_pos("is not a function", _err) > 0)
{
    _zh = ntl_t("err.notfunc");
    _advice = ntl_t("adv.notfunc");
}
else if (string_pos("stack overflow", _err) > 0)
{
    _zh = ntl_t("err.stack");
    _advice = ntl_t("adv.stack");
}
else if (string_pos("execution budget", _err) > 0 || string_pos("while 次数", _err) > 0)
{
    _zh = ntl_t("err.budget");
    _advice = ntl_t("adv.budget");
}
else if (string_pos("module not found", _err) > 0 || string_pos("找不到模块", _err) > 0)
{
    _zh = ntl_t("err.module");
    _advice = ntl_t("adv.module");
}
else
{
    _zh = _err;   // 未识别：原样返回
    _advice = "";
}

// ---- 组装 ----
var _loc = "";
if (_file != "")
{
    _loc = _file;
    if (_line >= 0) _loc += ":" + string(_line);
    _loc = "[" + _loc + "] ";
}

var _out = _loc + _zh;
_out += chr(10) + "        Lua:  " + _err;
if (_advice != "") _out += chr(10) + ntl_ts("err.advice", [_advice]);
return _out;
