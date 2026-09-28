// ★★★ 常驻等待（外部章节正在跑、游戏窗口已隐藏）：**在一切输入处理之前**就返回。
//   ⚠ 位置很关键：必须放在**控制台/F2/章节选择**等所有输入处理之前 ——
//     之前放在文件后半段（244 行），按键已经被前面的代码吃掉了，于是"输入漏进选择器"（用户实测）。
//   回程检测（external-exited.txt）在 ntl_root_step 的常驻分支里，所以必须仍然调用它。
var _ntl_extOn = (variable_global_exists("ntl_ext_running") && global.ntl_ext_running == 1);
if (!_ntl_extOn)
{
    // ★ 守候写的"外部引擎正在跑"标记：请求若不是本进程发起的（外部写请求 / 进程重启丢了状态），
    //   以前这里不屏蔽 → 玩家在 Kristal 里打字会**漏进章节选择**（用户实测，本次也复现了）。
    try { if (file_exists("Neutraled/external-running.txt")) _ntl_extOn = true; } catch (e) { _ntl_extOn = false; }
}
if (_ntl_extOn)
{
    // 常驻分支要靠 ntl_root_step 检测回程标记；只是"被外部占用"的情况直接屏蔽输入即可
    if (variable_global_exists("ntl_ext_running") && global.ntl_ext_running == 1) ntl_root_step();
    return 0;
}

// obj_ntl_core :: Step —— 每帧：控制台输入 → 事件广播 → 房间轮询
// ★ 控制台开关：F2（113）
//    历史教训：BeginStep 事件在本项目实测不被调用；keyboard_check_direct 对真实键盘不可靠。

if (!variable_global_exists("ntl_console_open")) global.ntl_console_open = false;
if (!variable_global_exists("ntl_console_input")) global.ntl_console_input = "";
if (!variable_global_exists("ntl_console_tick")) global.ntl_console_tick = 0;
if (!variable_global_exists("ntl_key_lastlog")) global.ntl_key_lastlog = 0;
if (!variable_global_exists("ntl_console_openkey_last")) global.ntl_console_openkey_last = 0;
if (!variable_global_exists("ntl_console_histkey_last")) global.ntl_console_histkey_last = 0;

// ---------- 1) 检测 F2 ----------
// ⚠ 只用 keyboard_check_direct（硬件状态，清不掉）+ 自己记边沿 ——
//   pressed/down 读的是 GM 按键状态，DELTARUNE 每帧 keyboard_clear_all 会清掉，表现为"要按好几下才开"
// ⚠ 必须过 ntl_has_focus()：keyboard_check_direct 是硬件状态，游戏在后台时在别的窗口按 F2
//   也会被当成"开控制台"（用户实测）。ntl_key_fire 同时自带按下沿（不再依赖会被清掉的 pressed）。
var _openKey = (ntl_has_focus() == 1 && ntl_key_fire(113, 0, 0) == 1) ? 113 : 0;

// 诊断：记录检测到的按键（最多每 30 帧一次）
global.ntl_console_tick += 1;
if (_openKey != 0 && (global.ntl_console_tick - global.ntl_key_lastlog) > 30)
{
    global.ntl_key_lastlog = global.ntl_console_tick;
    ntl_log("console", "[按键] F2 被检测到（pressed=" + string(keyboard_check_pressed(113)) +
            " down=" + string(keyboard_check(113)) +
            " direct=" + string(keyboard_check_direct(113)) + "）");
}

// 切换（按下沿触发）
if (_openKey != 0 && global.ntl_console_openkey_last == 0)
{
    global.ntl_console_open = !global.ntl_console_open;
    global.ntl_console_input = "";
    ntl_key_reset();   // 切换瞬间已按住的键不算新按下（否则开控制台会立刻滚一行/删一个字）
    keyboard_string = "";
    keyboard_lastchar = "";
    ntl_log("console", "控制台 " + (global.ntl_console_open ? "已打开" : "已关闭"));
}
global.ntl_console_openkey_last = _openKey;

// ---------- 1.5) 强力指令的每帧驱动 ----------
//   god 回血 / 冻结下的单帧推进 / watch 变量跟踪
//   （bind 的按键检查以前**从没被调用**过 —— 顺手接上，否则 bind 命令是死的）
if (global.ntl_console_open) ntl_console_binds_check();
ntl_console_power_tick();

// ---------- 2) 控制台打开时：读输入 + 屏蔽游戏 ----------
var _foc = (global.ntl_console_open == 1) ? ntl_has_focus() : 0;   // 失焦时控制台不读任何键
if (global.ntl_console_open)
{
    // ===== 滚动浏览（↑/↓ 逐行、PgUp/PgDn 翻页、Home/End 跳顶底）=====
    // ⚠ Ctrl/Shift 判定必须用 keyboard_check_direct —— 用 keyboard_check 时被清成 0，
    //   于是 _ctrl 恒假 → "Ctrl+↑ 调不出历史命令"（用户实测）；Shift 快滚同理失效。
    var _ctrl = (_foc == 1) && (keyboard_check_direct(vk_control) || keyboard_check_direct(vk_rcontrol));
    var _sh = (_foc == 1) && (keyboard_check_direct(vk_shift) || keyboard_check_direct(vk_rshift));
    // Ctrl 按住时 ↑/↓ 是"历史"不是"滚动" —— 否则按 Ctrl+↑ 画面滚动、输入行还是空的（用户实测的"滚动出现了问题"）
    if (!_ctrl)
    {
        if ((_foc == 1) && (keyboard_check_pressed(vk_up) || keyboard_check_direct(vk_up)))
        {
            if (_sh) global.ntl_console_scroll += 10;      // Shift+↑ 快滚
            else global.ntl_console_scroll += 1;
            global.ntl_console_autoscroll = (global.ntl_console_scroll == 0) ? 1 : 0;
        }
        if ((_foc == 1) && (keyboard_check_pressed(vk_down) || keyboard_check_direct(vk_down)))
        {
            if (_sh) global.ntl_console_scroll -= 10;
            else global.ntl_console_scroll -= 1;
            if (global.ntl_console_scroll <= 0) { global.ntl_console_scroll = 0; global.ntl_console_autoscroll = 1; }
        }
    }
    if (ntl_key_fire(vk_pageup, 0, 0) == 1)  { global.ntl_console_scroll += 14; global.ntl_console_autoscroll = 0; }
    if (ntl_key_fire(vk_pagedown, 0, 0) == 1)
    {
        global.ntl_console_scroll -= 14;
        if (global.ntl_console_scroll <= 0) { global.ntl_console_scroll = 0; global.ntl_console_autoscroll = 1; }
    }
    if (ntl_key_fire(vk_home, 0, 0) == 1)
    {
        var _ln = ds_list_size(global.ntl_console_lines);
        global.ntl_console_scroll = max(0, _ln - 14);
        global.ntl_console_autoscroll = 0;
    }
    if (ntl_key_fire(vk_end, 0, 0) == 1) { global.ntl_console_scroll = 0; global.ntl_console_autoscroll = 1; }
    // 滚轮
    var _mw = (_foc == 1) ? (mouse_wheel_down() - mouse_wheel_up()) : 0;
    if (_mw != 0) { global.ntl_console_scroll += _mw * 3; if (global.ntl_console_scroll <= 0) { global.ntl_console_scroll = 0; global.ntl_console_autoscroll = 1; } }

    // ===== 命令历史（Ctrl+↑/↓）=====
    // 边沿自己记（direct + prev）—— keyboard_check_pressed 会被 keyboard_clear_all 清掉，按住又会每帧连跳
    var _upNow = (_foc == 1) && keyboard_check_direct(vk_up);
    var _dnNow = (_foc == 1) && keyboard_check_direct(vk_down);
    if (_ctrl)
    {
        var _hist = global.ntl_console_history;
        var _hn = array_length(_hist);
        if (_hn > 0)
        {
            var _moved = 0;
            if (_upNow && global.ntl_console_histkey_last != 1)
            {
                global.ntl_console_hist_idx = min(global.ntl_console_hist_idx + 1, _hn);
                _moved = 1;
            }
            if (_dnNow && global.ntl_console_histkey_last != 2)
            {
                global.ntl_console_hist_idx = max(global.ntl_console_hist_idx - 1, 0);
                _moved = 1;
            }
            if (_moved == 1)
            {
                var _idx = _hn - global.ntl_console_hist_idx;
                if (global.ntl_console_hist_idx == 0) global.ntl_console_input = "";
                else if (_idx >= 0 && _idx < _hn) global.ntl_console_input = string(_hist[_idx]);
            }
        }
    }
    global.ntl_console_histkey_last = _upNow ? 1 : (_dnNow ? 2 : 0);

    // ===== Tab 补全 =====（补命令名）
    if (ntl_key_fire(vk_tab, 0, 0) == 1)
    {
        var _cur = global.ntl_console_input;
        if (string_length(_cur) > 0)
        {
            var _done = 0;
            if (variable_global_exists("ntl_console_cmds"))
            {
                var _ck = ntl_dsmap_keys(global.ntl_console_cmds);
                for (var _ci = 0; _ci < array_length(_ck); _ci += 1)
                {
                    var _cname = string(_ck[_ci]);
                    if (string_pos(string_lower(_cur), string_lower(_cname)) == 1)
                    {
                        global.ntl_console_input = _cname;
                        _done = 1;
                        break;
                    }
                }
            }
            // 不是命令名 → 补文件路径（run 命令用）
            if (_done == 0 && string_pos("run ", _cur) == 1)
            {
                var _files = ntl_file_list(program_directory + "Neutraled/scripts/", "*.ntlcmd");
                if (array_length(_files) > 0)
                {
                    var _nm = string_replace(string(_files[0]), ".ntlcmd", "");
                    global.ntl_console_input = "run " + _nm;
                }
            }
        }
        else
        {
            // 空输入时 Tab 显示提示
            ntl_console_log(ntl_t("msg.see_help"));
        }
    }

    // ===== 字符输入 =====（放在滚动键之后，避免方向键被当字符）=====
    var _typed = ntl_kb_scan();
    if (string_length(_typed) > 0) global.ntl_console_input += _typed;

    // ===== 功能键 =====（各自独立判断，不用 else-if 链）
    // 顺序：Enter 最优先（提交），Esc 次之，其余各自独立
    var _hitEnter = (ntl_key_fire(vk_enter, 0, 0) == 1);
    // ⚠ Esc 必须和 Enter/Up/Down 一样带 keyboard_check_direct 回退：
    //   注入式按键（测试脚本 keybd_event）与本分支末尾的 keyboard_clear(vk_escape) 会把 pressed 边沿抹掉，
    //   实测结果就是控制台按 Esc 关不掉（fulltest-game 的 console-root 断言 closed 失败，2026-09-26）。
    var _hitEsc   = (ntl_key_fire(vk_escape, 0, 0) == 1);
    // ★ Backspace：自带按下沿 + **按住自动重复**（首 0.4 秒后每 0.06 秒删一个字符）。
    //   旧实现用 keyboard_check_pressed/keyboard_check —— 前者被 keyboard_clear_all 清掉（"按了不删字"），
    //   后者按住也不重复（"长按不能连续删除"），两条都是用户实测反馈。
    var _hitBack  = (ntl_key_fire(vk_backspace, 400000, 60000) == 1);
    var _hitF1    = (ntl_key_fire(vk_f1, 0, 0) == 1);

    if (_hitEnter || _hitEsc) global.ntl_console_input2 = global.ntl_console_input;   // 占位，保持结构

    if (_hitEnter && string_length(string_trim(global.ntl_console_input)) == 0)
    {
        // 输入缓冲为空（或只有空格）时按 Enter：**什么都不做** —— 不提交、不清屏、不写日志。
        // （用户反馈：以前空缓冲也会提交一条空指令进去）
        _hitEnter = 0;
    }

    if (_hitEnter)
    {
        var _cmd = global.ntl_console_input;
        global.ntl_console_input = "";
        global.ntl_console_scroll = 0;                 // 执行后回到最底
        global.ntl_console_autoscroll = 1;
        if (string_length(_cmd) > 0)
        {
            var _h = global.ntl_console_history;
            if (array_length(_h) == 0 || string(_h[array_length(_h) - 1]) != _cmd)
            {
                array_push(_h, _cmd);
                if (array_length(_h) > 100) array_delete(_h, 0, 1);
            }
            global.ntl_console_history = _h;
            global.ntl_console_hist_idx = 0;
            ntl_console_exec(_cmd);
        }
        else ntl_console_log(">");
    }
    else if (_hitEsc)
    {
        global.ntl_console_open = false;
        global.ntl_console_input = "";
        ntl_console_capture(false);
        keyboard_clear(vk_escape);   // 别把这次的 Esc 漏给游戏（章节里会开暂停菜单）
        ntl_log("console", "控制台已关闭 (Esc)");
    }
    else if (_hitBack)
    {
        var _l = string_length(global.ntl_console_input);
        if (_l > 0) global.ntl_console_input = string_copy(global.ntl_console_input, 1, _l - 1);
    }
    else if (_hitF1)
    {
        ntl_console_exec("help");
    }

    // 屏蔽游戏输入
    global.kbdBlocked = true;
    keyboard_clear(vk_enter);
    keyboard_clear(vk_escape);
    keyboard_clear(vk_backspace);
    keyboard_clear(vk_up);
    keyboard_clear(vk_down);
    keyboard_string = "";
    keyboard_lastchar = "";
}
else
{
    global.kbdBlocked = false;
    ntl_kb_scan(true);
}

// ===== 以下是原有 Step 内容 =====
global.ntl_frames += 1;

// ===== F5 接线：开场跳过 + live 热重载 =====
// ★ 开场跳过（config: auto_skip_intro / skip_legend / skip_logo）
//   未开启时每帧只做 3 次 ds_map 查询，不做任何字符串/磁盘操作
if (variable_global_exists("ntl_cfg") && is_real(global.ntl_cfg) && ds_exists(global.ntl_cfg, ds_type_map))
{
    var _skipOn = 0;
    if (ds_map_find_value(global.ntl_cfg, "auto_skip_intro") == 1) _skipOn = 1;
    else if (ds_map_find_value(global.ntl_cfg, "skip_legend") == 1) _skipOn = 1;
    else if (ds_map_find_value(global.ntl_cfg, "skip_logo") == 1) _skipOn = 1;
    if (_skipOn == 1)
    {
        try { ntl_skip_intro(); } catch (e) { ntl_log("skip", "[错误] ntl_skip_intro 异常: " + string(e)); }
    }
}

// ★ 热重载：ntl_hotreload_check 内部每 60 帧才做一次 file_exists（不是每帧磁盘 IO）
try { ntl_hotreload_check(); } catch (e) { ntl_log("hot", "[错误] ntl_hotreload_check 异常: " + string(e)); }

// ★ mod 脚本加载（延迟到第 2 帧，此时所有系统都已就绪）
//   放在这里而不是 ntl_live_init 里，是因为 Step 事件已验证可用
if (global.ntl_frames == 2 && !variable_global_exists("ntl_mod_scripts_done"))
{
    global.ntl_mod_scripts_done = 1;
    ntl_log("live", "★★★★★ STEP 内加载 mod 脚本 ★★★★★");
    try
    {
        var _n3 = ntl_mod_scripts_load();
        ntl_log("live", "[Step] mod 脚本加载完成: " + string(_n3) + " 个");
        // ★ 只有本帧**真的**加载了新 mod 时才再广播一次 on_init：
        //   Create 阶段 ntl_live_init() 已经广播过一遍，无条件再广播会让同一份 on_init 每进程跑两遍
        //   （真机实测：同一份 Lua on_init 出现两次；在 on_init 里 ntl_hook() 的 mod 还会订阅两次）。
        if (_n3 > 0)
        {
            ntl_live_emit("on_init", 0);
            ntl_log("live", "[Step] on_init 已广播（本帧新加载 " + string(_n3) + " 个 mod）");
        }
        else ntl_log("live", "[Step] 本帧没有新 mod，跳过 on_init 重复广播");
    }
    catch (e2) { ntl_log("live", "[Step] mod 脚本加载异常: " + string(e2)); }
}

// 首帧之后应用文本覆盖（此时语言表已加载完成）
if (!global.ntl_ready && global.ntl_frames > 1)
{
    global.ntl_ready = true;
    var _n = ntl_apply_lang_overrides();
    ntl_log("core", "lang overrides applied: " + string(_n));
    ntl_emit("on_ready", []);
    ntl_live_emit("on_ready", 0);
}

// 房间变化
if (room != global.ntl_room_last)
{
    var _old = global.ntl_room_last;
    global.ntl_room_last = room;
    ntl_emit("on_room_load", [room, _old]);
    ntl_live_emit("on_room_load", room);

    // ★ F5 接线：把 ntl_mod_data_set 改过的共享数据在房间切换时落盘
    //   消费者 ntl_mod_data_save.gml:3 先查 global.ntl_mod_data_dirty：
    //   没改过时直接 return 0 —— 这里不产生任何磁盘 IO，也不是每帧调用。
    try { ntl_mod_data_save(); }
    catch (e) { ntl_log("mod", "[共享数据] 自动存盘失败: " + string(e)); }
}

// Mod 设置：面板是游戏 submenu 51 的一页（原生二级菜单通道），按键在 obj_darkcontroller
// 的注入代码里（builder/Injector.cs）。这里只跑计时器与状态同步。
ntl_modmenu_step();

// 顶层章节界面
if (variable_global_exists("ntl_ext_launching") && global.ntl_ext_launching == 1 && !variable_global_exists("ntl_probe_step1")) { global.ntl_probe_step1 = 1; ntl_log("ext", "[ext] Step_1 探针：外部章节请求后 Step_1 仍在运行"); }
// ★ 只在 root 进程里跑章节选择器的逻辑：章节进程里跑会抢输入、还会让存档界面被我们的界面干扰
if (ntl_is_root() == 1)
{
    ntl_autoskip();  // 按配置自动跳过章节选择器（含外部引擎章节）
    ntl_root_step();
}

// Neutraled 地图导航：玩家移动 + 相机 + 对象交互
if (variable_global_exists("ntl_player") && variable_global_exists("ntl_test_map"))
{
    try { ntl_player_step(global.ntl_test_map); } catch (e) { ntl_log("player", "[ntl] events/Step_1.gml:236 ntl_player_step 捕获: " + string(e)); }
    try { ntl_player_interact(global.ntl_test_map); } catch (e) { ntl_log("player", "[ntl] events/Step_1.gml:237 ntl_player_interact 捕获: " + string(e)); }
}

// Kristal 对象实例更新
if (variable_global_exists("ntl_objects"))
{
    try { ntl_obj_step(); } catch (e) { ntl_log("obj", "[ntl] events/Step_1.gml:243 ntl_obj_step 捕获: " + string(e)); }
}

// Kristal mod 每帧回调
ntl_kristal_frame();

// 主循环接管（mod 可完全掌控每一帧）
try { ntl_loop_emit("step"); } catch (e) { ntl_log("loop", "[ntl] events/Step_1.gml:250 ntl_loop_emit(step) 捕获: " + string(e)); }

// 帧事件广播
ntl_emit("on_frame", [global.ntl_frames]);
ntl_live_emit("on_frame", global.ntl_frames);
