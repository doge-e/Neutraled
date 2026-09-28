/// ntl_skip_intro() —— 按配置跳过章节内的开场演出（传说 / DELTARUNE 报幕）
/// 全部由 config.json 开关控制，默认关闭（不影响正常体验）。
/// 实现：识别开场房间名 → 等待 intro_delay 帧 → room_goto 到下一阶段房间。

if (!variable_global_exists("ntl_cfg")) return 0;
if (!is_real(global.ntl_cfg) || !ds_exists(global.ntl_cfg, ds_type_map)) return 0;
// 防御：scr_ntl_init 没跑到的进程里这两个 global 可能还不存在
if (!variable_global_exists("ntl_skip_room")) global.ntl_skip_room = -1;
if (!variable_global_exists("ntl_skip_frame")) global.ntl_skip_frame = 0;

var _skip_legend = ds_map_find_value(global.ntl_cfg, "skip_legend");
var _skip_logo = ds_map_find_value(global.ntl_cfg, "skip_logo");
// ★ F5 接线 auto_skip_intro：这个配置键以前**零消费点**（只在 ntl_config_load 里被读进来）。
//   语义 = "两种开场都跳过"的主开关，不必分别打开 skip_legend / skip_logo。
if (ds_map_find_value(global.ntl_cfg, "auto_skip_intro") == 1)
{
    _skip_legend = 1;
    _skip_logo = 1;
}
if (_skip_legend != 1 && _skip_logo != 1) return 0;

var _rn = "";
try { _rn = room_get_name(room); } catch (e) { _rn = ""; }
if (string_length(_rn) == 0) return 0;

var _isLegend = (_rn == "room_legend" || _rn == "room_legend_neo");
var _isLogo = (_rn == "PLACE_LOGO");
if (!_isLegend && !_isLogo) { global.ntl_skip_frame = 0; return 0; }
if (_isLegend && _skip_legend != 1) return 0;
if (_isLogo && _skip_logo != 1) return 0;

// 同一房间内累计等待帧
if (global.ntl_skip_room != room)
{
    global.ntl_skip_room = room;
    global.ntl_skip_frame = 0;
}
global.ntl_skip_frame += 1;

var _delay = ds_map_find_value(global.ntl_cfg, "intro_delay");
if (!is_real(_delay) || _delay < 0) _delay = 45;      // 与 ntl_config_load 的默认值一致
if (global.ntl_skip_frame < _delay) return 0;

// 目标：优先跳到菜单/存档选择房间
var _target = asset_get_index("PLACE_MENU");
if (_target < 0) _target = asset_get_index("PLACE_NAMING_JIKKEN");
if (_target < 0)
{
    ntl_log("skip", "[警告] 找不到目标房间，跳过未执行");
    global.ntl_skip_frame = 0;
    return 0;
}
if (_target == room) return 0;

ntl_log("skip", "跳过开场房间 " + _rn + " -> " + string(_target));
room_goto(_target);
return 1;
