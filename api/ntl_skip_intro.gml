/// ntl_skip_intro() —— 按配置跳过章节内的开场演出（传说 / DELTARUNE 报幕）
/// 全部由 config.json 开关控制，默认关闭（不影响正常体验）。
///
/// ★ 2026-10-02 修复（用户报「跳过传说后音乐没有跟上」）
///   旧实现只做 room_goto(PLACE_MENU)，把官方跳过里的**音频收尾**整段丢了：
///     · obj_legend_Draw_0 末尾（官方跳过）：mus_volume(global.currentsong[1], 0, 15) 让传说 BGM 淡出，
///       19 帧后 snd_free(global.currentsong[0]) 释放音频流、global.flag[6] = 0，20 帧后 room_goto(137 = PLACE_LOGO)；
///     · PROCESS_LOGO_Draw_0 末尾（官方跳过）：snd_volume(NOISE, 0, 20) 让报幕音淡出，再 room_goto(139 = PLACE_MENU)。
///   旧实现两者都没做 ⇒ legend.ogg 一路播进存档界面（音乐与画面脱节），flag[6] 还残留 1。
///
/// ★ 2026-10-02 用户追加要求：「直接播放存档界面音乐，跳过 legend.ogg」
///   ⇒ 传说分支不再等 19/20 帧做官方淡出序列，而是**当场停掉 legend.ogg**（并复位 flag[6]），
///     屏幕黑场一完成就直接进 PLACE_MENU（存档界面）—— 那里由官方 DEVICE_MENU 起播 AUDIO_STORY.ogg。
///     （skip_logo 没开时，仍按官方路径去 PLACE_LOGO 看报幕。）
///   存档界面音乐兜底：DEVICE_MENU 的 snd_init 走 global.ntl_stream_cache；若缓存里的持有者已被
///   snd_free_all 销毁，旧版 Injector 缓存的是**死流句柄** ⇒ 音乐静音。这里跳过去之后等 ~1 秒确认
///   currentsong[1] 没在播，就按官方口径自己补起一条 AUDIO_STORY.ogg 流（只做一次）。
///   只用**核心函数 + asset_get_index 按名查找**，不依赖各章可能不存在的同名脚本（mus_volume / snd_free 等）。

if (!variable_global_exists("ntl_cfg")) return 0;
if (!is_real(global.ntl_cfg) || !ds_exists(global.ntl_cfg, ds_type_map)) return 0;
// 防御：scr_ntl_init 没跑到的进程里这些 global 可能还不存在
if (!variable_global_exists("ntl_skip_room")) global.ntl_skip_room = -1;
if (!variable_global_exists("ntl_skip_frame")) global.ntl_skip_frame = 0;
if (!variable_global_exists("ntl_skip_phase")) global.ntl_skip_phase = 0;
if (!variable_global_exists("ntl_skip_log_room")) global.ntl_skip_log_room = -1;
if (!variable_global_exists("ntl_skip_menu_guard")) global.ntl_skip_menu_guard = 0;

var _skip_legend = ds_map_find_value(global.ntl_cfg, "skip_legend");
var _skip_logo = ds_map_find_value(global.ntl_cfg, "skip_logo");
// auto_skip_intro = "两种开场都跳过"的主开关，不必分别打开 skip_legend / skip_logo
if (ds_map_find_value(global.ntl_cfg, "auto_skip_intro") == 1)
{
    _skip_legend = 1;
    _skip_logo = 1;
}
if (_skip_legend != 1 && _skip_logo != 1) return 0;

var _rn = "";
try { _rn = room_get_name(room); } catch (e_rn) { _rn = ""; }
if (string_length(_rn) == 0) return 0;

var _isLegend = (_rn == "room_legend" || _rn == "room_legend_neo");
var _isLogo = (_rn == "PLACE_LOGO");
var _isMenu = (_rn == "PLACE_MENU");

// ---------- 存档界面（PLACE_MENU）：只做「音乐有没有响」的兜底守候，不干扰官方设备 ----------
// 守候窗口在跳过成功时置位（见下面两处 global.ntl_skip_menu_guard = 180），普通进菜单时恒为 0。
if (_isMenu)
{
    if (global.ntl_skip_menu_guard > 0)
    {
        global.ntl_skip_menu_guard -= 1;
        if (global.ntl_skip_menu_guard == 120)
        {
            var _playing = 0;
            if (variable_global_exists("currentsong"))
            {
                try { _playing = audio_is_playing(global.currentsong[1]); } catch (e_msq) { _playing = 0; }
            }
            if (_playing == 1)
            {
                ntl_log("skip", "存档界面音频正常：AUDIO_STORY 正在播放");
            }
            else
            {
                var _stream = -1;
                try { _stream = audio_create_stream("mus/AUDIO_STORY.ogg"); } catch (e_ms1) { _stream = -1; }
                if (_stream < 0)
                {
                    try { _stream = audio_create_stream(working_directory + "../mus/AUDIO_STORY.ogg"); } catch (e_ms2) { _stream = -1; }
                }
                if (_stream >= 0)
                {
                    var _inst = -1;
                    try { _inst = audio_play_sound(_stream, 90, 1); } catch (e_ms3) { _inst = -1; }
                    if (_inst >= 0)
                    {
                        try { audio_sound_gain(_inst, 0.95, 0); } catch (e_ms4) { }
                        if (variable_global_exists("currentsong"))
                        {
                            global.currentsong[0] = _stream;
                            global.currentsong[1] = _inst;
                        }
                        ntl_log("skip", "存档界面无 BGM：已按官方口径补起 AUDIO_STORY.ogg（stream=" + string(_stream) + " inst=" + string(_inst) + "）");
                    }
                    else
                    {
                        ntl_log("skip", "[警告] 存档界面无 BGM，且 AUDIO_STORY 流播放失败");
                    }
                }
                else
                {
                    ntl_log("skip", "[警告] 存档界面无 BGM，且打不开 mus/AUDIO_STORY.ogg");
                }
            }
        }
    }
    return 0;
}

if (!_isLegend && !_isLogo)
{
    global.ntl_skip_frame = 0;
    global.ntl_skip_phase = 0;
    global.ntl_skip_log_room = -1;
    return 0;
}
if (_isLegend && _skip_legend != 1) return 0;
if (_isLogo && _skip_logo != 1) return 0;

// 换房间就重置会话状态
if (global.ntl_skip_room != room)
{
    global.ntl_skip_room = room;
    global.ntl_skip_frame = 0;
    global.ntl_skip_phase = 0;
    global.ntl_skip_log_room = -1;
}

// 官方脚本惯例：数值按 30 fps 书写，除以 fps_scale 换算到实际帧率
var _fs = 1;
if (variable_global_exists("fps_scale") && is_real(global.fps_scale) && global.fps_scale > 0) _fs = global.fps_scale;

// ---------- 阶段 0 → 1：屏幕淡出 + **当场收掉正在播的开场音频** ----------
if (global.ntl_skip_phase == 0)
{
    global.ntl_skip_frame += 1;
    var _delay = ds_map_find_value(global.ntl_cfg, "intro_delay");
    if (!is_real(_delay) || _delay < 0) _delay = 45;      // 与 ntl_config_load 的默认值一致
    if (global.ntl_skip_frame < _delay) return 0;

    var _fadeIdx = asset_get_index("obj_fadeout");
    if (_fadeIdx >= 0)
    {
        var _fade = instance_create(0, 0, _fadeIdx);
        if (_isLegend) { _fade.fadespeed = 0.08; } else { _fade.fadespeed = 0.04 / _fs; }
    }

    var _faded = 0;
    if (_isLegend)
    {
        // ★ 用户要求：不要 legend.ogg 继续播 —— 直接停掉（不走官方 15 帧淡出），随后进存档界面放 AUDIO_STORY
        if (variable_global_exists("currentsong"))
        {
            try { audio_stop_sound(global.currentsong[1]); _faded = 1; }
            catch (e_lgstop) { ntl_log("skip", "[警告] 传说 BGM 停止失败: " + string(e_lgstop)); }
        }
        if (variable_global_exists("flag"))
        {
            try { global.flag[6] = 0; }
            catch (e_lgflag) { ntl_log("skip", "[警告] flag[6] 复位失败: " + string(e_lgflag)); }
        }
    }
    else
    {
        var _poIdx = asset_get_index("PROCESS_LOGO");
        if (_poIdx >= 0 && instance_exists(_poIdx))
        {
            var _po = instance_find(_poIdx, 0);
            if (_po != noone)
            {
                try { audio_sound_gain(_po.NOISE, 0, 667); _faded = 1; }
                catch (e_logofade) { ntl_log("skip", "[警告] 报幕音淡出失败: " + string(e_logofade)); }
                _po.skipped = 1;      // 明确由我们接管，避免官方再触发一次
                _po.skiptimer = 0;
            }
        }
    }
    global.ntl_skip_menu_guard = 180;      // 到了存档界面后守候 3 秒：确认音乐在响，没响就补
    if (global.ntl_skip_log_room != room)
    {
        global.ntl_skip_log_room = room;
        if (_isLegend)
        {
            ntl_log("skip", "跳过开场 " + _rn + "：legend.ogg 已当场停止，flag[6] 已复位（音频" + ((_faded == 1) ? "已收" : "未找到对象") + "）");
        }
        else
        {
            ntl_log("skip", "跳过开场 " + _rn + "：报幕音淡出" + ((_faded == 1) ? "已执行" : "未执行（无对应音频对象）"));
        }
    }
    global.ntl_skip_phase = 1;
    global.ntl_skip_frame = 0;
    return 1;
}

// ---------- 阶段 1 → 2：黑场淡出结束后前往目标房间 ----------
global.ntl_skip_frame += 1;

if (_isLegend)
{
    // 黑场 12 帧（≈0.4 s）就够；随后直接去目标房间
    if (global.ntl_skip_frame >= round(12 * _fs))
    {
        var _tgt = asset_get_index("PLACE_LOGO");
        if (_skip_logo == 1) _tgt = asset_get_index("PLACE_MENU");      // ★ 用户要求：跳过报幕，直奔存档界面
        if (_tgt < 0 || _tgt == room) _tgt = asset_get_index("PLACE_MENU");
        if (_tgt < 0 || _tgt == room) _tgt = asset_get_index("PLACE_NAMING_JIKKEN");
        if (_tgt < 0 || _tgt == room)
        {
            ntl_log("skip", "[警告] 找不到传说之后的目标房间，跳过未完成");
            global.ntl_skip_phase = 0;
            global.ntl_skip_frame = 0;
            return 0;
        }
        global.ntl_skip_frame = 0;
        global.ntl_skip_phase = 0;
        global.ntl_skip_room = -1;
        ntl_log("skip", "传说已跳过 -> " + string(_tgt));
        room_goto(_tgt);
    }
    return 1;
}

// ---------- 报幕（PLACE_LOGO）：官方等 (30 * TARGET_FPS) / 15 帧后 room_goto(139 = PLACE_MENU) ----------
if (global.ntl_skip_frame >= round(24 * _fs))
{
    var _tgt2 = asset_get_index("PLACE_MENU");
    if (_tgt2 < 0 || _tgt2 == room) { _tgt2 = asset_get_index("PLACE_NAMING_JIKKEN"); }
    if (_tgt2 < 0 || _tgt2 == room)
    {
        ntl_log("skip", "[警告] 找不到报幕之后的目标房间，跳过未完成");
        global.ntl_skip_phase = 0;
        global.ntl_skip_frame = 0;
        return 0;
    }
    global.ntl_skip_frame = 0;
    global.ntl_skip_phase = 0;
    global.ntl_skip_room = -1;
    ntl_log("skip", "报幕已跳过 -> " + string(_tgt2));
    room_goto(_tgt2);
}
return 1;
