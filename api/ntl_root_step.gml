/// ntl_root_step() —— 顶层章节界面的输入处理（仅在官方选择器存在时生效）
// 外部引擎章节（Kristal）启动后的收尾：让出几帧把请求文件刷到盘上，再退出。
// 退出后由外面的启动器（GUI / --watch-external）看到请求文件并拉起 Kristal。
if (variable_global_exists("ntl_ext_launching") && global.ntl_ext_launching == 1)
{
    // 探针：确认常驻分支真的被执行（排查"改了没生效"用）
    if (!variable_global_exists("ntl_ext_probe")) { global.ntl_ext_probe = 1; ntl_log("ext", "[ext] 探针：常驻分支已接管，剩余帧 " + string(global.ntl_ext_wait)); }
    global.ntl_ext_wait -= 1;
    // ★ 只在"尚未转入"时执行一次：以前这里只判 <= 0，归零后**每帧**都成立 →
    //   日志被刷爆（实测 3 秒 94 行）+ ntl_ext_frames 每帧清零（45 分钟超时兜底永久失效）。
    if (global.ntl_ext_wait <= 0 && (!variable_global_exists("ntl_ext_running") || global.ntl_ext_running != 1))
    {
        // ★★ 不再 game_end()：**让 DELTARUNE 常驻**，Kristal 在它上面跑。
        //    以前是退出进程 → 守候走 Steam 重新拉起 → GameMaker 引擎初始化 ≈33 秒（这才是"退出后等很久"的真凶）。
        //    现在：外部引擎退出后由守候写一个标记文件，我们每帧检测到就把控制权还回来 —— 0 秒回程。
        global.ntl_ext_running = 1;
        global.ntl_ext_frames = 0;
        try { audio_master_gain(0); } catch (e) { }                 // 常驻期间静音，别和 Kristal 抢声音
        try { window_set_caption(ntl_t("root.caption")); } catch (e) { }
        ntl_log("ext", "[ext] 转入常驻等待：外部章节退出后会自动回到这里（不再重启进程）");
    }
    if (variable_global_exists("ntl_ext_running") && global.ntl_ext_running == 1)
    {
        global.ntl_ext_frames += 1;
        var _marker = "Neutraled/external-exited.txt";
        var _done = 0;
        try { _done = file_exists(_marker); } catch (e) { _done = 0; }
        var _timeout = (global.ntl_ext_frames > 162000);            // 45 分钟兜底，防止标记丢失时永久常驻
        if (_done || _timeout)
        {
            if (_done) { try { file_delete(_marker); } catch (e) { } }
            global.ntl_ext_running = 0;
            global.ntl_ext_launching = 0;
            try { audio_master_gain(1); } catch (e) { }
            try { window_set_caption("DELTARUNE"); } catch (e) { }   // 还原成原版标题（别留下 Neutraled）
            ntl_log("ext", _done ? "[ext] 外部章节已退出 —— 已回到游戏（0 秒回程）" : "[ext] 等待超时 —— 已回到游戏");
        }
    }
    return 0;
}


// ★★★ 控制台打开时，章节选择器完全不响应任何输入（包括 Enter/Z 进入章节）
if (variable_global_exists("ntl_console_open") && global.ntl_console_open) return 0;

var _sel_obj = asset_get_index("obj_CHAPTER_SELECT");
if (_sel_obj < 0) return 0;
// 官方对象已被我们销毁 → 不再要求它存在；只看章节表是否加载好（必须与销毁同时改！）
if (global.ntl_ch_loaded != 1) return 0;

// 禁用官方章节 UI 的输入（视觉被我们覆盖，输入由我们处理）
var _ui_obj = asset_get_index("obj_ui_chapter");
if (_ui_obj >= 0)
{
    var _cnt = instance_number(_ui_obj);
    for (var _i = 0; _i < _cnt; _i += 1)
    {
        var _ui = instance_find(_ui_obj, _i);
        if (_ui != noone)
        {
            try { _ui._input_enabled = false; } catch (e) { ntl_log("root", "[ntl] ntl_root_step.gml:20 关闭 _input_enabled 失败: " + string(e)); }
            try { _ui._enable_select = false; } catch (e) { ntl_log("root", "[ntl] ntl_root_step.gml:21 关闭 _enable_select 失败: " + string(e)); }
            try { _ui._enable_confirm = false; } catch (e) { ntl_log("root", "[ntl] ntl_root_step.gml:22 关闭 _enable_confirm 失败: " + string(e)); }
        }
    }
}

// ★★ 接管选择器：**停用官方 obj_CHAPTER_SELECT 对象**。
//   原因：它用 keyboard_check_direct 直接读键盘（绕开按键状态，keyboard_clear 拦不住），
//   选中"外部章节"时会走它自己的逻辑 —— 外部条目的 dir 是空的，于是 game_change("") 把整个游戏切走/结束，
//   表现为"点了外部章节后游戏直接没了"（我们的常驻代码因此永远执行不到，探针一直不打印）。
//   停用它之后：官方 UI 不再抢输入，章节导航完全由 ntl_root_* 负责（界面本来就是我们画的）。
if (!variable_global_exists("ntl_sel_taken"))
{
    global.ntl_sel_taken = 1;
    // ★★★ 顶替原版（2026-09-25 第三次尝试 —— 这次是**整条层级**一起接管）：
    //   只销毁 obj_CHAPTER_SELECT 会让它的下游（obj_screen_select / obj_ui_choice / obj_ui_chapter / ...）
    //   找不到实例而每帧 Code Error（实测 gml_Object_obj_ui_choice_Step_0: Unable to find instance for object index 100001）。
    //   → 把整条官方选择器 UI 层级**一起销毁**，就没有任何对象还引用它了。
    //   必须与「同时去掉 ntl_root_draw / ntl_root_step / ntl_root_launch 里的 instance_number(_sel_obj) 守卫」一起做。
    if (variable_global_exists("ntl_takeover_done") == 0)
    {
        global.ntl_takeover_done = 1;
        var _kill = ["obj_CHAPTER_SELECT", "obj_screen_select", "obj_screen_select_footer",
                     "obj_screen_select_list", "obj_ui_chapter", "obj_ui_choice"];
        var _killed = 0;
        for (var _ki = 0; _ki < array_length(_kill); _ki += 1)
        {
            var _oid = asset_get_index(_kill[_ki]);
            if (_oid < 0) continue;
            try
            {
                with (_oid) { instance_destroy(); }
                _killed += 1;
            }
            catch (e) { ntl_log("root", "[警告] 销毁 " + _kill[_ki] + " 失败: " + string(e)); }
        }
        ntl_log("root", "[root] 已顶替原版章节选择器：销毁 " + string(_killed) + " 个官方对象");
    }
    // 双保险：官方"选择器"对象自己也有输入开关（之前只关了 obj_ui_chapter 的）
    try
    {
        with (_sel_obj)
        {
            try { _input_enabled = false; } catch (e2) { }
            try { _enable_select = false; } catch (e2) { }
            try { _enable_confirm = false; } catch (e2) { }
            try { _enable = false; } catch (e2) { }
            try { _active = false; } catch (e2) { }
        }
    } catch (e) { }
    ntl_log("root", "[root] 已接管章节选择器（停用官方 UI 对象）");
}

var _pages = ntl_root_pages();
ntl_root_filter();

// ---- 搜索模式 ----
if (global.ntl_ch_search_mode == 1)
{
    // 逐键扫描（不依赖被游戏占用的 keyboard_string）
    var _ks = ntl_kb_scan();
    if (string_length(_ks) > 0)
    {
        global.ntl_ch_search += _ks;
        keyboard_string = "";
        ntl_root_filter();
    }
    if (ntl_key_fire(8, 400000, 60000) == 1)     // Backspace
    {
        var _len = string_length(global.ntl_ch_search);
        if (_len > 0) global.ntl_ch_search = string_copy(global.ntl_ch_search, 1, _len - 1);
        ntl_root_filter();
    }
    // 只有 Esc 退出搜索（关键词保留，过滤继续生效）
    if (ntl_key_fire(27, 0, 0) == 1)
    {
        global.ntl_ch_search_mode = 0;
        keyboard_string = global.ntl_ch_search;   // 关键词保留，不同步给游戏
        keyboard_clear(vk_escape);
        return 1;
    }
    // Enter 仍可进入选中的章节（进入前退出搜索模式）
    if (ntl_key_fire(13, 0, 0) == 1)
    {
        global.ntl_ch_search_mode = 0;
        keyboard_string = "";
        keyboard_clear(vk_enter);
        // 落到下面的导航逻辑处理 Enter 进入
    }
    else
    {
        return 1;   // 搜索模式下其它按键不触发导航
    }
}

// ---- 导航 ----
// ★ 循环时**跳过空槽**（第 6/7 格是「无内容」；旧逻辑硬编码 7 格会停在上面）
//   用绘制代码同款 API ntl_root_page_items(page)：7 个槽 → 章节索引（-1 = 空槽）
if (ntl_key_fire(38, 250000, 90000) == 1)        // Up
{
    var _itU = ntl_root_page_items(global.ntl_ch_page);
    if (array_length(_itU) < 7)
    {
        global.ntl_ch_sel -= 1;
        if (global.ntl_ch_sel < 0) global.ntl_ch_sel = 6;
    }
    else
    {
        var _sU = global.ntl_ch_sel;
        var _skippedU = 0;
        for (var _gU = 0; _gU < 7; _gU += 1)
        {
            var _pU = _sU;
            _sU -= 1;
            if (_sU < 0) _sU = 6;
            var _ixU = _itU[_sU];
            if (_ixU >= 0)
            {
                var _mU = global.ntl_ch[_ixU];
                // 有内容 = 已部署(enabled) 或 非官方章节（与 draw 的判定一致）
                if (ds_map_find_value(_mU, "enabled") == 1 || ds_map_find_value(_mU, "kind") != "official") break;
            }
            _skippedU += 1;
        }
        if (_skippedU > 0) ntl_log("root", "[root] 上移跳过了 " + string(_skippedU) + " 个空槽");
        global.ntl_ch_sel = _sU;
    }
}
if (ntl_key_fire(40, 250000, 90000) == 1)        // Down
{
    var _itD = ntl_root_page_items(global.ntl_ch_page);
    if (array_length(_itD) < 7)
    {
        global.ntl_ch_sel += 1;
        if (global.ntl_ch_sel > 6) global.ntl_ch_sel = 0;
    }
    else
    {
        var _sD = global.ntl_ch_sel;
        var _skippedD = 0;
        for (var _gD = 0; _gD < 7; _gD += 1)
        {
            _sD += 1;
            if (_sD > 6) _sD = 0;
            var _ixD = _itD[_sD];
            if (_ixD >= 0)
            {
                var _mD = global.ntl_ch[_ixD];
                if (ds_map_find_value(_mD, "enabled") == 1 || ds_map_find_value(_mD, "kind") != "official") break;
            }
            _skippedD += 1;
        }
        if (_skippedD > 0) ntl_log("root", "[root] 下移跳过了 " + string(_skippedD) + " 个空槽");
        global.ntl_ch_sel = _sD;
    }
}
if (ntl_key_fire(37, 250000, 120000) == 1)        // Left
{
    global.ntl_ch_page -= 1;
    if (global.ntl_ch_page < 0) global.ntl_ch_page = _pages - 1;
}
if (ntl_key_fire(39, 250000, 120000) == 1)        // Right
{
    global.ntl_ch_page += 1;
    if (global.ntl_ch_page >= _pages) global.ntl_ch_page = 0;
}

// ---- 搜索开关 ----
if (ntl_key_fire(70, 0, 0) == 1)        // F
{
    global.ntl_ch_search_mode = 1;
    keyboard_string = global.ntl_ch_search;
    return 1;
}

// ---- 语言切换（L：循环 zh → 外部语言包 → en）----
if (ntl_key_fire(76, 0, 0) == 1)        // L
{
    ntl_root_lang_cycle();
    return 1;
}

// ---- 退出游戏（Esc：第一次提示，3 秒内再按一次才真的退）----
if (!variable_global_exists("ntl_quit_armed")) global.ntl_quit_armed = 0;
if (!variable_global_exists("ntl_quit_frames")) global.ntl_quit_frames = 0;
if (!variable_global_exists("ntl_quit_pending")) global.ntl_quit_pending = 0;
if (ntl_key_fire(27, 0, 0) == 1)        // Esc
{
    if (global.ntl_quit_pending == 0)
    {
        if (global.ntl_quit_armed == 1)
        {
            global.ntl_quit_armed = 0;
            global.ntl_quit_pending = 24;         // 让「正在退出游戏…」显示约 0.8 秒再退
            ntl_log("root", "[root] 玩家确认退出游戏（Esc 两连）");
        }
        else
        {
            global.ntl_quit_armed = 1;
            global.ntl_quit_frames = 90;          // ≈3 秒
            ntl_log("root", "[root] 退出确认：3 秒内再按一次 Esc 退出游戏");
        }
    }
    keyboard_clear(vk_escape);
    return 1;
}
if (global.ntl_quit_armed == 1)
{
    global.ntl_quit_frames -= 1;
    if (global.ntl_quit_frames <= 0)
    {
        global.ntl_quit_armed = 0;
        ntl_log("root", "[root] 退出确认已超时（取消）");
    }
}
if (global.ntl_quit_pending > 0)
{
    global.ntl_quit_pending -= 1;
    if (global.ntl_quit_pending <= 0)
    {
        ntl_log("root", "[root] 退出游戏");
        game_end();
    }
}

// ---- 确认 ----
// 官方对象已销毁 → Enter/Z 不会再被抢走，普通章节和外部章节都可以直接按 Enter/Z
if (ntl_key_fire(13, 0, 0) == 1 || ntl_key_fire(90, 0, 0) == 1)
{
    ntl_root_launch(global.ntl_ch_sel);
    return 1;
}
return 1;
