/// ntl_root_launch(slot) —— 启动当前页第 slot 个槽位对应的章节
var _k = argument[0];
var _items = ntl_root_page_items(global.ntl_ch_page);
if (_k < 0 || _k >= 7) return 0;
var _idx = _items[_k];
if (_idx < 0) return 0;
var _m = global.ntl_ch[_idx];
if (ds_map_find_value(_m, "enabled") != 1)
{
    ntl_log("root", "该章节无内容（未启用）");
    return 0;
}

var _kind = ds_map_find_value(_m, "kind");
var _order = real(ds_map_find_value(_m, "order"));
var _dir = ds_map_find_value(_m, "dir");

// ---- 外部引擎章节（Kristal 等自带运行时的项目）----
// 为什么这么做：Kristal 是 LÖVE 工程（main.lua + conf.lua + data/），没有 GameMaker data.win，
// 在本运行时里根本加载不了。与其移植引擎，不如"退出游戏 → 拉起它自己的引擎跑那个项目"：
// 插件加载、进度保存、通关判定全部是 Kristal 原生行为。
if (_kind == "external")
{
    // ★ 关键：官方章节选择器**自己也**在监听 Enter。外部条目的 dir 是空的，
    //   官方逻辑拿它去 game_change("") → 直接把游戏结束掉（这就是"常驻代码从不执行"的真凶）。
    //   所以在我们处理完之后，把确认键状态清干净，让官方那一帧什么也看不到。
    var _rc = ntl_ext_launch(_m);
    try { keyboard_clear(vk_enter); } catch (e) { }
    try { keyboard_clear(vk_space); } catch (e) { }
    try { keyboard_clear(ord("Z")); } catch (e) { }
    try { keyboard_clear(ord("X")); } catch (e) { }
    return _rc;
}

if (_kind == "timeline")
{
    // 必须传 get_chapter_switch_parameters()：其中的 "launcher" 参数决定
    // global.launcher → 音频路径为 working_directory + "../mus/"（不传会资源加载失败）
    ntl_log("root", "启动平行时间线: " + string(ds_map_find_value(_m, "id")) + " -> /" + _dir);
    var _params = "";
    try { _params = get_chapter_switch_parameters(); } catch (e) { _params = " launcher"; }
    game_change("/" + _dir, "-game data.win" + _params);
    return 1;
}

ntl_log("root", "启动章节 " + string(_order) + "（" + _kind + "）");
return ntl_goto_chapter(_order);
