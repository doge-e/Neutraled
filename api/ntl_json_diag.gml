/// ntl_json_diag() —— 一次性诊断：本产物 Neutraled/mods.json 的读取/解析链路（排 "已加载 0"）。
/// 真根因（2026-10-02 已定，见 builder/Program.cs:2432 注释）：C# 默认编码器把中文 mod 名写成 \uXXXX，
///   而 GameMaker 的 json_parse 吃不下 \uXXXX ⇒ 整个清单解析失败 ⇒ 面板恒「已加载 0」。
/// 这条日志仍保留，用于**下次真机一键定性**：打印 转义个数 / 解析计数 / 原文头部 + 合成对照组
///   （对照组含一例 \uXXXX 转义样本，可直接看到 json_parse 是否吃得下）。1.0.1 发布前可删。
if (variable_global_exists("ntl_json_diag_done")) return 0;
global.ntl_json_diag_done = 1;

var _p = string(working_directory) + "Neutraled/mods.json";
var _txt = file_exists(_p) ? ntl_live_file_read(_p) : "";
var _hd = string_copy(_txt, 1, 90);
_hd = string_replace_all(_hd, chr(13), "");
_hd = string_replace_all(_hd, chr(10), "<NL>");

// 合成对照组：每例独立 try，异常不打断（json_parse 失败在 GM 里是抛异常）
var _syn = "";
var _cases = ["[]", "[1,2,3]", "[{\"Id\":\"a\"},{\"Id\":\"b\"}]", "{\"mods\":[{\"Id\":\"a\"}]}",
    "{\"version\":1,\"mods\":[]}", "{\"n\":\"\\u51B0\"}", "{\"n\":\"冰\"}"];
for (var _i = 0; _i < array_length(_cases); _i += 1)
{
    var _s = _cases[_i];
    var _r = undefined;
    var _bad = 0;
    try { _r = json_parse(_s); } catch (e) { _bad = 1; }
    var _k = "undefined";
    if (_bad == 1) _k = "异常";
    else if (is_array(_r)) _k = "array[" + string(array_length(_r)) + "]";
    else if (is_struct(_r)) _k = "struct";
    else if (is_string(_r)) _k = "string";
    else if (is_real(_r)) _k = "real";
    _syn += " | " + _s + " => " + _k;
}

// ★ 顺序 bug 修复（2026-10-02）：原来把 global.ntl_modmenu_loaded_diag 写在 ntl_modmenu_loaded()
//   **之前**求值 ⇒ 该全局尚未被设置 ⇒ unset variable 异常，整条日志变成「诊断脚本异常（已忽略）」。
//   现在先算计数，再拼字符串，并用 variable_global_exists 守卫。
var _ldc = -1;
try { _ldc = ntl_modmenu_loaded_count(); } catch (e) { _ldc = -2; }
var _ldn = -1;
try { var _a = ntl_modmenu_loaded(); _ldn = is_array(_a) ? array_length(_a) : -3; } catch (e) { _ldn = -4; }
var _esc = string_count("\\u", _txt);
var _dg = variable_global_exists("ntl_modmenu_loaded_diag") ? string(global.ntl_modmenu_loaded_diag) : "<未设置>";

ntl_log("jdiag", "[jdiag] wd=" + string(working_directory) + " 存在=" + string(file_exists(_p)) +
    " 字符数=" + string(string_length(_txt)) + " 转义=" + string(_esc) +
    " 计数=" + string(_ldc) + " 清单=" + string(_ldn) + " 头部=" + _hd);
ntl_log("jdiag", "[jdiag] 原文=" + _dg);
ntl_log("jdiag", "[jdiag] 对照组:" + _syn);
return 1;