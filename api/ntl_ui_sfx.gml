/// ntl_ui_sfx(kind) —— 章节选择器**自己**的 UI 音效（移动 / 确认）
/// kind: "move"（上下移动、左右翻页）| "confirm"（Enter/Z 进入章节）
///
/// ★ 为什么必须自己播（2026-10-03 用户报「进入普通章节（包括官方）后返回音效消失」）：
///   选择器里原本听到的移动音效来自**官方开始屏 obj_screen_start**
///   （--dumpall 取证：gml_Object_obj_screen_start_Step_0.gml:33/:39 → audio_play_sound(7, 50, 0)）。
///   而官方 obj_CHAPTER_SELECT 只在 returning_0（全新启动）时才走 create_start_screen()；
///   从章节返回时启动参数带 returning_1 ⇒ gml_Object_obj_CHAPTER_SELECT_Create_0.gml:61-64
///   直接 _current_state = Value_4 → create_select_screen()，那个选择屏在我们的停用名单里
///   ⇒ 回来后官方一个音效都不发（外部章节 park 往返同理）。
///   解法：选择器自己发声，不再依赖官方对象是否在场；obj_screen_start 同时纳入停用名单
///   （见 api/ntl_root_step.gml 的 _takeMine），避免新老两个音源一起响（双声）。
///
/// 音效索引：名字优先（资源表换过也能命中），取不到再退回官方索引（7 = 移动、6 = 确认）；
///   两者都不可用（audio_exists 失败）时静默返回 0，绝不让音效问题影响导航。

if (!variable_global_exists("ntl_sfx_move"))
{
    var _mv = asset_get_index("snd_menumove");
    if (_mv < 0 || !audio_exists(_mv)) _mv = 7;
    var _cf = asset_get_index("snd_menuselect");
    if (_cf < 0 || !audio_exists(_cf)) _cf = 6;
    global.ntl_sfx_move = _mv;
    global.ntl_sfx_confirm = _cf;
    ntl_log("root", "[sfx] 选择器音效索引: move=" + string(_mv) + " confirm=" + string(_cf));
}

var _kind = (argument_count > 0) ? string(argument[0]) : "move";
var _idx = (_kind == "confirm") ? global.ntl_sfx_confirm : global.ntl_sfx_move;
if (_idx < 0 || !audio_exists(_idx)) return 0;
try { audio_play_sound(_idx, 50, 0); }
catch (e_sfx) { ntl_log("root", "[sfx] 播放失败: " + string(e_sfx)); }
return 1;
