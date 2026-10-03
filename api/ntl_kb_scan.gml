/// ntl_kb_scan() —— 逐键扫描本帧新输入的字符（返回字符串）
/// ⚠️ 关键：DELTARUNE 每帧可能调用 keyboard_clear_all，会清掉 GM 的按键状态，
///    所以一律用 **keyboard_check_direct**（直接读硬件状态，清不掉）。
///    为了得到"按下一次"的语义，用 global.ntl_kb_prev 记录上一帧状态。
if (!variable_global_exists("ntl_kb_prev")) global.ntl_kb_prev = ds_map_create();
var _prev = global.ntl_kb_prev;

// ntl_kb_scan(true) = 只重置状态、不返回字符（控制台关闭时每帧调用，
// 这样打开控制台的首帧不会把"当时按住的键"误判成新输入）
// ★ 游戏不在前台 → 同样走"只重置"路径：不产生任何字符。
//   原因：keyboard_check_direct 是硬件状态，游戏在后台时玩家在别的窗口打字照样会被读进来
//   （用户实测："控制台输入会把游戏外的输入写进去"）。失焦期间只同步 prev，回前台不会补发。
var _resetOnly = ((argument_count > 0) && (argument[0] == true)) || (ntl_has_focus() == 0);
if (_resetOnly)
{
    // ⚠️ 不能清空 prev！否则打开控制台首帧会把"当时按住的键"全部当成新输入。
    //    正确做法：把当前所有键的状态记录进 prev，让首帧的 _was == _now（不触发）。
    var _allKeys = [113, vk_escape, vk_enter, vk_backspace, vk_space, vk_shift, vk_rshift, vk_control,
                    vk_left, vk_right, vk_up, vk_down, vk_tab];
    for (var _rk = ord("A"); _rk <= ord("Z"); _rk += 1) array_push(_allKeys, _rk);
    for (var _rd = ord("0"); _rd <= ord("9"); _rd += 1) array_push(_allKeys, _rd);
    for (var _rs = 0; _rs < 10; _rs += 1) array_push(_allKeys, vk_numpad0 + _rs);
    for (var _ri = 0; _ri < array_length(_allKeys); _ri += 1)
    {
        var _kk = string(_allKeys[_ri]);
        var _now = keyboard_check_direct(_allKeys[_ri]);
        if (ds_map_exists(_prev, _kk)) ds_map_replace(_prev, _kk, _now);
        else ds_map_add(_prev, _kk, _now);
    }
    // ★ 标点键（OEM VK 0xBA-0xDE + 空格）在字符扫描里用的是 "s"+vk 的键名，
    //   这里必须同名同步，否则开着控制台时按住的标点会被下一帧误判成新输入。
    var _symRst = [vk_space, 0xBD, 0xBB, 0xDB, 0xDD, 0xBA, 0xBC, 0xBE, 0xBF, 0xDC, 0xC0, 0xDE];
    for (var _rx = 0; _rx < array_length(_symRst); _rx += 1)
    {
        var _xk = "s" + string(_symRst[_rx]);
        var _xv = keyboard_check_direct(_symRst[_rx]);
        if (ds_map_exists(_prev, _xk)) ds_map_replace(_prev, _xk, _xv);
        else ds_map_add(_prev, _xk, _xv);
    }
    var _hold2 = variable_global_exists("ntl_kb_hold") ? global.ntl_kb_hold : undefined;
    if (_hold2 != undefined && is_real(_hold2)) ds_map_clear(_hold2);
    return "";
}

var _out = "";
var _shift = keyboard_check_direct(vk_shift) || keyboard_check_direct(vk_rshift);

// ---- 字母 A-Z ----
var _upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
var _lower = "abcdefghijklmnopqrstuvwxyz";
for (var _i = 0; _i < 26; _i += 1)
{
    var _k = ord("A") + _i;
    var _ks = string(_k);
    var _now = keyboard_check_direct(_k);
    var _was = ds_map_exists(_prev, _ks) ? ds_map_find_value(_prev, _ks) : 0;
    if (_now != _was)
    {
        if (ds_map_exists(_prev, _ks)) ds_map_replace(_prev, _ks, _now);
        else ds_map_add(_prev, _ks, _now);
    }
    if (_now && !_was) _out += string_char_at(_shift ? _upper : _lower, _i + 1);
}

// ---- 数字 0-9 ----
var _digits = "0123456789";
var _sym = ")!@#$%^&*(";
for (var _d = 0; _d < 10; _d += 1)
{
    var _kd = ord("0") + _d;
    var _kds = string(_kd);
    var _nd = keyboard_check_direct(_kd);
    var _wd = ds_map_exists(_prev, _kds) ? ds_map_find_value(_prev, _kds) : 0;
    if (_nd != _wd)
    {
        if (ds_map_exists(_prev, _kds)) ds_map_replace(_prev, _kds, _nd);
        else ds_map_add(_prev, _kds, _nd);
    }
    if (_nd && !_wd) _out += string_char_at(_shift ? _sym : _digits, _d + 1);
}

// ---- 小键盘数字 ----
for (var _n = 0; _n < 10; _n += 1)
{
    var _kn = vk_numpad0 + _n;
    var _kns = "np" + string(_n);
    var _nn = keyboard_check_direct(_kn);
    var _wn = ds_map_exists(_prev, _kns) ? ds_map_find_value(_prev, _kns) : 0;
    if (_nn != _wn)
    {
        if (ds_map_exists(_prev, _kns)) ds_map_replace(_prev, _kns, _nn);
        else ds_map_add(_prev, _kns, _nn);
    }
    if (_nn && !_wn) _out += string_char_at(_digits, _n + 1);
}

// ---- 常用符号 ----
// ★★ 修复（2026-09-28 真机实测确认）：GM 的 keyboard_check_direct 对标点键用的是 **Windows VK 码**，
//   不是 ASCII 码。旧表里 ord("-")=45 / ord(".")=46 / ord("=")=61 / ord("[")=91 … 在 VK 表里分别是
//   Insert(0x2D=45) / Delete(0x2E=46) / 未定义(0x3D) / 左Win(0x5B) 等，实测后果：
//     · 真实标点键（VK 0xBD 减号、0xBE 句点、0xBB 等号 …）**一个字符都收不到** ——
//       用户在控制台里根本打不出 "_ . / + = [ ] : < > ?"，于是 loadmap before_palace、
//       eval 1+1、whatis obj_kris 这类输入全部作废（用户反馈「全量测试控制台」实测抓到）；
//     · 反过来按 Insert 会插入 "-"、按 Delete 会插入 "."（因为 45/46 就是这两个键的 VK）。
//   现在改用 0xBA-0xDE 这组 OEM VK 码，并补齐 \ ` ' 三个键。
var _symKeys = [vk_space, 0xBD, 0xBB, 0xDB, 0xDD, 0xBA, 0xBC, 0xBE, 0xBF, 0xDC, 0xC0, 0xDE];
var _symNorm = [" ", "-", "=", "[", "]", ";", ",", ".", "/", chr(92), chr(96), chr(39)];
var _symShft = [" ", "_", "+", "{", "}", ":", "<", ">", "?", chr(124), chr(126), chr(34)];
for (var _s = 0; _s < array_length(_symKeys); _s += 1)
{
    var _kk = _symKeys[_s];
    var _kks = "s" + string(_kk);
    var _nk = keyboard_check_direct(_kk);
    var _wk = ds_map_exists(_prev, _kks) ? ds_map_find_value(_prev, _kks) : 0;
    if (_nk != _wk)
    {
        if (ds_map_exists(_prev, _kks)) ds_map_replace(_prev, _kks, _nk);
        else ds_map_add(_prev, _kks, _nk);
    }
    if (_nk && !_wk) _out += _shift ? _symShft[_s] : _symNorm[_s];
}

return _out;
