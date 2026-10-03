/// ntl_audio_report() —— 音频诊断快照（只读）：定位「从外部章节返回后选择音效消失」
/// 记录三类转变到 dr-api.log（类别 aud）：
///   1) 声音开始/停止播放：#索引 名称 + master 增益
///   2) master 增益变化（带 park 标志）
///   3) park 状态变化（ext_running / paused / stopped / launching）
/// 首次调用枚举 0..2999 里真实存在的声音资源索引；之后每帧只做 audio_is_playing 查询。
/// 由 events/Step_1.gml 的 root 分支按 config 的 audio_sniff==1 调用（默认关闭）。
var _list = undefined;
if (!variable_global_exists("ntl_aud_idx"))
{
    var _l = ds_list_create();
    for (var _i = 0; _i < 3000; _i += 1)
    {
        var _ex = 0;
        try { if (audio_exists(_i)) _ex = 1; } catch (e_ex) { _ex = 0; }
        if (_ex == 1) ds_list_add(_l, _i);
    }
    global.ntl_aud_idx = _l;
    global.ntl_aud_live = ds_map_create();
    global.ntl_aud_master = -99;
    global.ntl_aud_park = -99;
    // ★ 2026-10-03：枚举时把**名字**一起写下来 —— 选择器自己的音效（api/ntl_ui_sfx.gml）
    //   用名字解析 asset_get_index("snd_menumove")，这里给出权威对照表（含确认音 6 号的真名）。
    var _names = "";
    var _ln = ds_list_size(_l);
    for (var _j = 0; _j < _ln; _j += 1)
    {
        var _sx = ds_list_find_value(_l, _j);
        var _nmx = "?";
        try { _nmx = string(audio_get_name(_sx)); } catch (e_nmx) { _nmx = "?"; }
        _names += (_nmx + "(" + string(_sx) + ") ");
    }
    ntl_log("aud", "[aud] 声音索引枚举: " + string(_ln) + " 个（扫描 0..2999）: " + _names);
}
_list = global.ntl_aud_idx;

var _fr = 0;
if (variable_global_exists("ntl_frames")) _fr = global.ntl_frames;

var _master = -1;
try { _master = audio_get_master_gain(); } catch (e_m) { _master = -1; }

var _er = 0; var _ep = 0; var _es = 0; var _el = 0;
if (variable_global_exists("ntl_ext_running")) _er = global.ntl_ext_running;
if (variable_global_exists("ntl_ext_paused")) _ep = global.ntl_ext_paused;
if (variable_global_exists("ntl_ext_stopped")) _es = global.ntl_ext_stopped;
if (variable_global_exists("ntl_ext_launching")) _el = global.ntl_ext_launching;
var _park = (_er * 1) + (_ep * 2) + (_es * 4) + (_el * 8);
var _flags = "run=" + string(_er) + ",paused=" + string(_ep) + ",stopped=" + string(_es) + ",launch=" + string(_el);

if (_master != global.ntl_aud_master)
{
    global.ntl_aud_master = _master;
    ntl_log("aud", "[aud] f=" + string(_fr) + " master=" + string(_master) + " " + _flags);
}
if (_park != global.ntl_aud_park)
{
    global.ntl_aud_park = _park;
    ntl_log("aud", "[aud] f=" + string(_fr) + " park 标志变化: " + _flags);
}

var _m = global.ntl_aud_live;
var _n = ds_list_size(_list);
var _cnt = 0;
for (var _k = 0; _k < _n; _k += 1)
{
    var _s = ds_list_find_value(_list, _k);
    var _p = 0;
    try { _p = audio_is_playing(_s); } catch (e_p) { _p = 0; }
    var _was = ds_map_exists(_m, _s);
    if (_p == 1)
    {
        _cnt += 1;
        if (!_was)
        {
            ds_map_add(_m, _s, 1);
            var _nm = "?";
            try { _nm = string(audio_get_name(_s)); } catch (e_n1) { _nm = "?"; }
            ntl_log("aud", "[aud] f=" + string(_fr) + " PLAY #" + string(_s) + " " + _nm + " master=" + string(_master) + " " + _flags);
        }
    }
    else if (_was)
    {
        ds_map_delete(_m, _s);
        var _nm2 = "?";
        try { _nm2 = string(audio_get_name(_s)); } catch (e_n2) { _nm2 = "?"; }
        ntl_log("aud", "[aud] f=" + string(_fr) + " STOP #" + string(_s) + " " + _nm2 + " master=" + string(_master));
    }
}
return _cnt;