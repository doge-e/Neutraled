/// ntl_root_step() —— 顶层章节界面的输入处理（仅在官方选择器存在时生效）
// ---- 外部引擎章节（Kristal）的常驻等待 --------------------------------------
// ★★★ 2026-09-30 重写（用户实测事故）：旧版写下请求后**直接** park —— 静音 + 把窗口标题
//   改成「外部章节运行中」+ 吞掉全部输入，最长 45 分钟。用户只用 Steam 启动时根本没有
//   --watch-external 守候进程，启动请求永远没人消费 ⇒ 用户只能杀进程。现在分三段：
//   ① 确认门（≤120 帧 ≈2 秒）：请求文件被消费掉（守候进程读到就删，见 builder/Program.cs:1068）
//      或 Neutraled/external-running.txt 出现（守候进程拉起外部引擎时写，Program.cs:1120）
//      ⇒ 才算「外面真的有人」；确认期间**不静音、不改标题**，但选择器输入自该次修复起即冻结。
//   ② 确认不了 ⇒ 绝不 park：清标志、恢复音量与标题、删掉没人要的请求文件、给可见提示
//      （ext.no_watcher），输入照常可用。
//   ③ park 之后也有逃生口：按 Esc 立刻回到游戏；external-running.txt 一直没出现过时只等
//      600 帧（≈10 秒），出现过才用 45 分钟兜底。
//
// ★★★ 用户主诉修复（2026-10-03）：「选择章节后不应该还能够再在选择器进行操作，直到返回」
//   按下 Enter/Z 选定章节后**立刻上锁**（见文件末尾的确认分支）：导航 / 翻页 / 搜索 / 语言 /
//   确认全部冻结，直到 ① 回到选择器（park 正常退出 / 外部引擎退出）② 本次启动被取消
//   （没有守候进程 :86-95、运行时目录 15 秒仍不可写 :38-46）。
//   这里做一次自愈：既没有「启动中」也没有「后台等待中」⇒ 锁必然是开的，
//   杜绝任何异常路径把选择器永久锁死。
if (!variable_global_exists("ntl_launch_locked")) global.ntl_launch_locked = 0;
if (!variable_global_exists("ntl_launch_locked_n")) global.ntl_launch_locked_n = 0;
var _launchBusy = ((variable_global_exists("ntl_ext_launching") && global.ntl_ext_launching == 1)
    || (variable_global_exists("ntl_ext_running") && global.ntl_ext_running == 1));
if (!_launchBusy && global.ntl_launch_locked == 1)
{
    global.ntl_launch_locked = 0;
    global.ntl_launch_locked_n = 0;
    ntl_log("root", "[root] 选择器输入已解冻（没有启动 / 等待在进行）");
}
var _extParked = (variable_global_exists("ntl_ext_running") && global.ntl_ext_running == 1);
if (variable_global_exists("ntl_ext_launching") && global.ntl_ext_launching == 1)
{
    // 探针：确认后台分支真的被执行（排查"改了没生效"用）
    if (!variable_global_exists("ntl_ext_probe")) { global.ntl_ext_probe = 1; ntl_log("ext", "[ext] 探针：后台分支已接管，剩余帧 " + string(global.ntl_ext_wait)); }
    if (!variable_global_exists("ntl_ext_wait")) global.ntl_ext_wait = 20;
    global.ntl_ext_wait -= 1;
    // ★ 只在"尚未转入"时执行：以前这里只判 <= 0，归零后**每帧**都成立 →
    //   日志被刷爆（实测 3 秒 94 行）+ ntl_ext_frames 每帧清零（超时兜底永久失效）。
    if (!_extParked && global.ntl_ext_wait <= 0)
    {
        if (!variable_global_exists("ntl_ext_confirmed")) global.ntl_ext_confirmed = 0;
        if (!variable_global_exists("ntl_ext_req_frame")) global.ntl_ext_req_frame = global.ntl_frames;
        if (!variable_global_exists("ntl_ext_park_seen")) global.ntl_ext_park_seen = 0;
        if (!variable_global_exists("ntl_ext_req_seen")) global.ntl_ext_req_seen = 0;
        // ★ 用户主诉修复（2026-10-03「无存档进入 kristal 章节不应当提示，应当自动修复后继续」）：
        //   运行时目录不可写时，ntl_ext_launch 把启动请求**挂起**而不是弹提示（见 api/ntl_rt_stash.gml）。
        //   这里每进一次门控（约 20 帧）自动重修一次目录并重试落盘；成功即接回下面的正常确认流程。
        //   ⚠ 挂起期间必须跳过确认段，否则 120 帧后会把「请求还没写出去」误判成「没人消费」→ 误 toast。
        if (variable_global_exists("ntl_rt_pending_txt") && string_length(string(global.ntl_rt_pending_txt)) > 0)
        {
            var _rtRc = ntl_rt_retry();
            global.ntl_ext_wait = 20;
            if (_rtRc == 2)
            {
                // 兜底：≈15 秒仍修不好（连自动补建联接目标都失败）才给一次可见提示并放开输入
                ntl_rt_stash_clear();
                global.ntl_ext_launching = 0;
                global.ntl_ext_wait = 0;
                global.ntl_root_toast = ntl_t("ext.no_runtime");
                global.ntl_root_toast_frames = 600;
                ntl_log("root", "[toast] lang=" + string(global.ntl_lang) + " key=ext.no_runtime text=" + string(global.ntl_root_toast));
                ntl_log("ext", "[错误] 运行时目录 15 秒内仍不可写（自动修复失败），已放弃本次启动");
            }
        }
        else if (global.ntl_ext_confirmed != 1)
        {
            var _extReq = ntl_rt_path("launch-request.json");
            var _extSeen = 0;
            // ★ F-3（t25 复核）：**不能**把「请求文件不存在」直接当成「已被守候消费」——
            //   请求若从未落盘、被第三方清掉、或写到了别的工作目录，旧写法会立刻误判为已确认
            //   并 park，边界是最长 600 帧（≈10 秒）静音 + 误 toast。
            //   现在只认**确实观察到的消费迹象**：请求文件「先被看到存在、之后才不存在」
            //   （ntl_ext_launch.gml 写出并回读成功时会置 ntl_ext_req_seen = 1），
            //   或者守候进程写的 external-running.txt 出现。两者都没有 ⇒ 按未确认处理。
            var _extReqNow = 0;
            try { if (file_exists(_extReq)) _extReqNow = 1; } catch (e_extr1) { _extReqNow = 0; }
            if (_extReqNow == 1)
            {
                global.ntl_ext_req_seen = 1;
            }
            else if (global.ntl_ext_req_seen == 1)
            {
                _extSeen = 1;
                ntl_log("ext", "[ext] 启动请求已被守候进程消费（文件从存在变为不存在）");
            }
            try { if (file_exists(ntl_rt_path("external-running.txt"))) _extSeen = 1; } catch (e_extr2) { }
            if (_extSeen == 1)
            {
                global.ntl_ext_confirmed = 1;
                ntl_log("ext", "[ext] 已确认守候进程接手（请求被消费 / 外部引擎已启动），转入后台等待");
            }
            else
            {
                var _extWaited = global.ntl_frames - global.ntl_ext_req_frame;
                if (_extWaited < 0) _extWaited = 0;
                if (_extWaited <= 120)
                {
                    // 确认窗口内：不静音、不改标题（选择器输入自该次修复起已冻结 —— 用户选定章节后
                    // 不应再能操作选择器；这里不解除冻结，等 park 退出或上面的取消分支统一放开）
                    global.ntl_ext_wait = 0;
                }
                else
                {
                    global.ntl_ext_launching = 0;
                    global.ntl_ext_wait = 0;
                    try { audio_master_gain(1); } catch (e_extg1) { }
                    try { window_set_caption("DELTARUNE"); } catch (e_extc1) { }
                    try { if (file_exists(_extReq)) file_delete(_extReq); } catch (e_extd1) { }
                    global.ntl_root_toast = ntl_t("ext.no_watcher");
                    global.ntl_root_toast_frames = 300;
                    ntl_log("root", "[toast] lang=" + string(global.ntl_lang) + " key=ext.no_watcher text=" + string(global.ntl_root_toast));
                    ntl_log("ext", "[ext] 未检测到守候进程（启动请求无人消费 " + string(_extWaited) + " 帧）：已取消后台等待，保持输入可用");
                }
            }
        }
        if (variable_global_exists("ntl_ext_confirmed") && global.ntl_ext_confirmed == 1)
        {
            global.ntl_ext_running = 1;
            global.ntl_ext_frames = 0;
            global.ntl_ext_park_wd = working_directory;
            // ★ 用户主诉修复（2026-10-03「进入外部章节后章节选择器没有静音」）：
            //   这里只是第一道静音；真正的静音在下面的 park 常驻分支里每帧重申
            //   （audio_master_gain 会被游戏自己的音量逻辑覆盖，且失败原来被空 catch 吞掉）。
            try { audio_master_gain(0); } catch (e_exta1) { ntl_log("ext", "[ext] 进入后台等待时 audio_master_gain(0) 失败: " + string(e_exta1)); }
            try { window_set_caption(ntl_t("root.caption")); } catch (e_extk1) { }
            ntl_log("ext", "[ext] 转入后台等待：外部章节退出后会自动回到这里（不再重启进程）");
            // ---- ★ 用户主诉修复（2026-10-03）：park 开始时再兜一道 ----
            //   本局引擎的 external-running.txt 还没出现过（ntl_ext_park_seen != 1）⇒
            //   此刻若存在回程标记，只可能是残留（见 ntl_ext_launch.gml 的清理），删掉。
            if (global.ntl_ext_park_seen != 1)
            {
                try
                {
                    if (file_exists(ntl_rt_path("external-exited.txt")))
                    {
                        file_delete(ntl_rt_path("external-exited.txt"));
                        ntl_log("ext", "[ext] park 开始前清掉了残留回程标记（否则会立刻误判外部章节已退出）");
                    }
                }
                catch (e_extclr) { ntl_log("ext", "[ext] park 前清理回程标记失败: " + string(e_extclr)); }
            }
            _extParked = 1;
        }
    }
}
if (_extParked)
{
    if (!variable_global_exists("ntl_ext_frames")) global.ntl_ext_frames = 0;
    if (!variable_global_exists("ntl_ext_park_seen")) global.ntl_ext_park_seen = 0;
    global.ntl_ext_frames += 1;
    // ---- ★ 用户主诉修复（2026-10-03）：park 期间**每帧**强制静音 ----
    //   现象：进入外部章节后章节选择器的 BGM（AUDIO_STORY）还在响。
    //   原因：① 转入 park 时只静音了一次，游戏自己的音量逻辑会把 audio_master_gain 覆盖回去；
    //         ② 原来的 catch 是空的 ⇒ 静音失败在 dr-api.log 里完全无痕，无法排查。
    //   现在：每帧重申 audio_master_gain(0)，并 audio_pause_all()（暂停与增益无关，最可靠）；
    //   pause 不可用才退到 audio_stop_all()，退出时按走过的路原样撤销。
    if (!variable_global_exists("ntl_ext_mute_tried"))
    {
        global.ntl_ext_mute_tried = 0;
        global.ntl_ext_paused = 0;
        global.ntl_ext_stopped = 0;
        global.ntl_ext_mute_warn = 0;
    }
    try { audio_master_gain(0); } catch (e_extg0)
    {
        if (global.ntl_ext_mute_warn != 1) { global.ntl_ext_mute_warn = 1; ntl_log("ext", "[ext] audio_master_gain(0) 不可用: " + string(e_extg0)); }
    }
    if (global.ntl_ext_stopped != 1)
    {
        try
        {
            audio_pause_all();
            if (global.ntl_ext_mute_tried != 1)
            {
                global.ntl_ext_mute_tried = 1;
                global.ntl_ext_paused = 1;
                ntl_log("ext", "[ext] park 期间已暂停全部音频（外部章节运行期间静音）");
            }
        }
        catch (e_extp1)
        {
            if (global.ntl_ext_mute_warn != 2) { global.ntl_ext_mute_warn = 2; ntl_log("ext", "[ext] audio_pause_all 不可用，改用 audio_stop_all: " + string(e_extp1)); }
            try { audio_stop_all(); global.ntl_ext_stopped = 1; } catch (e_extp2)
            {
                if (global.ntl_ext_mute_warn != 3) { global.ntl_ext_mute_warn = 3; ntl_log("ext", "[ext] audio_stop_all 也不可用，只能靠 audio_master_gain(0): " + string(e_extp2)); }
            }
        }
    }
    var _extMarker = ntl_rt_path("external-exited.txt");
    var _extDone = 0;
    try { if (file_exists(_extMarker)) _extDone = 1; } catch (e_extm1) { _extDone = 0; }
    var _extEsc = 0;
    try { _extEsc = ntl_key_fire(27, 0, 0); } catch (e_exte1) { _extEsc = 0; }
    var _extRunMark = 0;
    try { if (file_exists(ntl_rt_path("external-running.txt"))) _extRunMark = 1; } catch (e_extm2) { }
    if (_extRunMark == 1) global.ntl_ext_park_seen = 1;
    var _extTimeout = 0;
    if (global.ntl_ext_park_seen != 1)
    {
        // 外部引擎压根没起来过：只等 ≈10 秒，不陪着耗 45 分钟
        if (global.ntl_ext_frames > 600) _extTimeout = 1;
    }
    else
    {
        if (global.ntl_ext_frames > 162000) _extTimeout = 1;
    }
    if (_extDone == 1 || _extTimeout == 1 || _extEsc == 1)
    {
        global.ntl_ext_launching = 0;
        global.ntl_ext_running = 0;
        global.ntl_ext_wait = 0;
        global.ntl_ext_confirmed = 0;
        try { if (file_exists(_extMarker)) file_delete(_extMarker); } catch (e_extm3) { }
        // ★ 用户主诉修复：把 park 期间的静音**原位撤销**（暂停→恢复；被 stop 掉的 BGM→按官方口径重起），
        //   失败一律写日志，不再用空 catch 吞掉。
        if (variable_global_exists("ntl_ext_paused") && global.ntl_ext_paused == 1)
        {
            try { audio_resume_all(); } catch (e_extr3) { ntl_log("ext", "[ext] audio_resume_all 失败: " + string(e_extr3)); }
            global.ntl_ext_paused = 0;
        }
        else if (variable_global_exists("ntl_ext_stopped") && global.ntl_ext_stopped == 1)
        {
            global.ntl_ext_stopped = 0;
            var _extStream = -1;
            try { _extStream = audio_create_stream("mus/AUDIO_STORY.ogg"); } catch (e_exts1) { _extStream = -1; }
            if (_extStream < 0) { try { _extStream = audio_create_stream(working_directory + "../mus/AUDIO_STORY.ogg"); } catch (e_exts2) { _extStream = -1; } }
            if (_extStream >= 0)
            {
                var _extInst = -1;
                try { _extInst = audio_play_sound(_extStream, 90, 1); } catch (e_exts3) { _extInst = -1; }
                if (_extInst >= 0)
                {
                    try { audio_sound_gain(_extInst, 0.95, 0); } catch (e_exts4) { }
                    if (variable_global_exists("currentsong")) { global.currentsong[0] = _extStream; global.currentsong[1] = _extInst; }
                    ntl_log("ext", "[ext] 已重新补起章节选择器 BGM（AUDIO_STORY.ogg）");
                }
                else ntl_log("ext", "[ext][警告] 回到章节选择器后 BGM 重启失败（audio_play_sound 失败）");
            }
            else ntl_log("ext", "[ext][警告] 回到章节选择器后 BGM 重启失败（打不开 mus/AUDIO_STORY.ogg）");
        }
        if (variable_global_exists("ntl_ext_mute_tried")) global.ntl_ext_mute_tried = 0;
        // ★ 用户主诉修复（2026-10-03）：回程时清掉滞留的按键边沿（窗口隐藏期间 GM 暂停，
        //   隐藏前按下的键会留到窗口恢复后才被读到 ⇒ 会误触发一次章节启动 / 误滚动）。
        ntl_key_reset();
        try { audio_master_gain(1); } catch (e_exta2) { ntl_log("ext", "[ext] audio_master_gain(1) 失败: " + string(e_exta2)); }
        try { window_set_caption("DELTARUNE"); } catch (e_extk2) { }
        if (_extEsc == 1)
        {
            // ★ F-2（t25/t32 复核）：Esc 必须清键，否则本帧官方对象的 Step 还会看到这次按下
            //   （读键走 ntl_key_fire，见 api/ntl_key_fire.gml:1-5；清键写法同本文件 :289 搜索退出、:420 退出确认）
            keyboard_clear(vk_escape);
            global.ntl_root_toast = ntl_t("ext.park_timeout");
            ntl_log("root", "[toast] lang=" + string(global.ntl_lang) + " key=ext.park_timeout text=" + string(global.ntl_root_toast));
            global.ntl_root_toast_frames = 240;
            ntl_log("ext", "[ext] 用户按 Esc 退出了后台等待 —— 已回到游戏");
        }
        else if (_extTimeout == 1)
        {
            global.ntl_root_toast = ntl_t("ext.park_timeout");
            ntl_log("root", "[toast] lang=" + string(global.ntl_lang) + " key=ext.park_timeout text=" + string(global.ntl_root_toast));
            global.ntl_root_toast_frames = 240;
            ntl_log("ext", "[ext] 后台等待超时（外部引擎没有出现）：已回到游戏");
        }
        else
        {
            ntl_log("ext", "[ext] 外部章节已退出 —— 已回到游戏（0 秒回程）");
        }
    }
    return 0;
}


// ★ 用户主诉修复（2026-10-03）收尾：离开章节选择器时，把被我们压到 0 的背景音乐增益还原。
//   只影响那一个音乐实例；选择音效从未被我们碰过，所以不需要任何恢复动作。
if (variable_global_exists("ntl_sel_bgm_muted") && global.ntl_sel_bgm_muted == 1)
{
    var _sel_left = (asset_get_index("obj_CHAPTER_SELECT") < 0 || global.ntl_ch_loaded != 1);
    if (_sel_left)
    {
        var _sel_bgm_restore = 1;   // 官方 start_bgm 的起始增益就是 1
        if (variable_global_exists("ntl_sel_bgm_gain")) _sel_bgm_restore = global.ntl_sel_bgm_gain;
        try
        {
            if (variable_global_exists("ntl_sel_bgm_i") && global.ntl_sel_bgm_i >= 0)
            {
                audio_sound_gain(global.ntl_sel_bgm_i, _sel_bgm_restore, 0);
            }
        }
        catch (e_selbgm9) { }
        global.ntl_sel_bgm_muted = 0;
        global.ntl_sel_bgm_i = -1;
        ntl_log("root", "[root] 已离开章节选择器：背景音乐增益还原为 " + string(_sel_bgm_restore));
    }
}

var _sel_obj = asset_get_index("obj_CHAPTER_SELECT");
if (_sel_obj < 0) return 0;
// 官方对象已被我们停用（非破坏性接管）→ 不依赖它的实例；只看章节表是否加载好
if (global.ntl_ch_loaded != 1) return 0;
// ★★★ 用户主诉修复（2026-10-03）：章节选择器里**除「选择音效」外全部静音**
//   用户原话：「我觉得你可以让章节选择器除了选择音效外的所有声音静音」。
//   官方 root 启动器的音乐口径（用 ntl-builder --dump gml_Object_obj_CHAPTER_SELECT_Create_0 实测反编译得到）：
//       start_bgm = function() { if (global.bgm == -4 || !audio_is_playing(global.bgm)) global.bgm = audio_play_sound(8, 15, 1); };
//       stop_bgm  = function() { if (global.bgm != -4) { audio_stop_sound(global.bgm); global.bgm = -4; } };
//       show_transition() 里：audio_sound_gain(global.bgm, 0, 500); audio_play_sound(sound_file, 50, 0, volume);  ← 这里的是选择音效
//   ⇒ 章节选择器的背景音乐实例就是 **global.bgm**（声音资源 8，循环，起始增益 1）；
//     而「选择音效」是 show_transition() 另起的一次性实例（sound_file），**不共用 global.bgm**，
//     所以只压 global.bgm 的增益即可做到「只留选择音效」——这正是用户要的效果。
//   绝不动 audio_master_gain(0) / audio_pause_all()：那会把选择音效一并掐掉。
//   每帧重申：房间切换 / 设置菜单 / 官方重新 start_bgm 都会把增益覆盖回去。
//   （2026-10-03 修正：早期版本读的是 global.currentsong[1]，那是**章节内**的音乐口径，root 里不存在 ⇒ 静音从未生效。）
if (!variable_global_exists("ntl_sel_bgm_muted")) global.ntl_sel_bgm_muted = 0;
if (!variable_global_exists("ntl_sel_bgm_warn")) global.ntl_sel_bgm_warn = 0;
if (!variable_global_exists("ntl_sel_bgm_i")) global.ntl_sel_bgm_i = -1;
if (variable_global_exists("bgm"))
{
    var _sel_bgm = -4;   // 官方用 -4 表示「没有背景音乐」
    try { _sel_bgm = global.bgm; } catch (e_selbgm0) { _sel_bgm = -4; }
    var _sel_play = 0;
    if (_sel_bgm != -4)
    {
        try { _sel_play = audio_is_playing(_sel_bgm); } catch (e_selbgm1) { _sel_play = 0; }
    }
    if (_sel_play == 1)
    {
        try
        {
            if (global.ntl_sel_bgm_muted != 1)
            {
                var _sel_g0 = 1;   // audio_play_sound(8, 15, 1) 的起始增益就是 1
                try { _sel_g0 = audio_sound_get_gain(_sel_bgm); } catch (e_selbgm3) { _sel_g0 = 1; }
                if (_sel_g0 <= 0) _sel_g0 = 1;   // 别把上一次残留的 0 当成「原增益」
                global.ntl_sel_bgm_gain = _sel_g0;
                global.ntl_sel_bgm_muted = 1;
                ntl_log("root", "[root] 章节选择器：背景音乐已静音（global.bgm=" + string(_sel_bgm) + "，原增益 " + string(_sel_g0) + "），选择音效保留");
            }
            global.ntl_sel_bgm_i = _sel_bgm;
            audio_sound_gain(_sel_bgm, 0, 0);
        }
        catch (e_selbgm2)
        {
            if (global.ntl_sel_bgm_warn != 1)
            {
                global.ntl_sel_bgm_warn = 1;
                ntl_log("root", "[root] 章节选择器静音失败（audio_sound_gain）: " + string(e_selbgm2));
            }
        }
    }
    else
    {
        // 音乐此刻没在播（例如 show_transition 已经淡出 / stop_bgm）→ 清掉静音状态，等它重新 start_bgm 时再压
        global.ntl_sel_bgm_muted = 0;
        global.ntl_sel_bgm_i = -1;
    }
}

// ★★★ 控制台打开时，章节选择器完全不响应任何输入（包括 Enter/Z 进入章节）
if (variable_global_exists("ntl_console_open") && global.ntl_console_open) return 0;

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

// ★★★ 接管官方章节选择器（2026-09-30 重写：**非破坏性** + 每帧复查）
//   为什么不再 instance_destroy()：官方控件是共享的 —— obj_ui_choice 同时被标题菜单
//   obj_screen_start、存档/设置等界面使用。旧版把它连同选择屏一起销毁，标题菜单随后
//   每帧引用死实例，用户实测报错：
//     ERROR in action number 1 of Step Event0 for object obj_screen_start:
//     Unable to find instance for object index 100008
//   —— 这就是「无存档启动就报错」和「存档界面按钮错乱」的成因。
//   为什么改成每帧复查：旧版用 ntl_sel_taken / ntl_takeover_done 一次性守卫；官方层级
//   一旦被重建（room_restart、从章节内菜单回 hub），我们再也不会去停用它，官方 UI 于是
//   活回来跟我们叠着画（用户实测：hub 里连按 30 次 Enter 全部静默无效）。
//   现在只做一件事：把「选择屏专属」的官方实例停用（instance_deactivate_object，不销毁、
//   随时可恢复），**obj_ui_choice 完全不碰**。幂等：每帧复查，状态变了才打日志。
// ★ 2026-10-03：官方「开始屏」obj_screen_start 也纳入停用 —— 它是**官方移动音效的唯一音源**
//   （gml_Object_obj_screen_start_Step_0.gml:33/:39 → audio_play_sound(7,50,0)）。停用它 + 我们
//   自己播（api/ntl_ui_sfx.gml）⇒ 一次按键只响一声；而且从章节返回时（returning_1，官方根本
//   不创建这个屏，见 obj_CHAPTER_SELECT_Create_0.gml:61-64）音效照常存在。
var _takeMine = ["obj_CHAPTER_SELECT", "obj_screen_select", "obj_screen_select_footer",
                 "obj_screen_select_list", "obj_ui_chapter", "obj_screen_start"];
if (!variable_global_exists("ntl_takeover_insts")) global.ntl_takeover_insts = 0;
if (!variable_global_exists("ntl_takeover_frame")) global.ntl_takeover_frame = global.ntl_frames;
var _takeNow = 0;

// ★ F-5（t25 复核）：官方选择器上的输入开关，只有在实例**确实声明**了它们时才写 ——
//   而且必须写在**停用之前**：GM 的 with() 会跳过已停用实例，写在下面停用循环之后就等于没写
//   （旧写法把 with() 放在停用循环之后，每帧都等于空转）。官方 obj_CHAPTER_SELECT 的 Create 事件里没有这几个
//   变量（--probe-code 核对见 release-docs/BATCH-FIX-F2-F7.md），旧写法直接赋值 ⇒ GM 静默
//   新建变量：看起来关了输入，实际什么都没关。真正的第一道防线是下面的 instance_deactivate_object
//   （停用后它的 Step 与输入都不跑）。
global.ntl_takeover_decl = 0;
with (_sel_obj)
{
    if (variable_instance_exists(id, "_input_enabled")) { _input_enabled = false; global.ntl_takeover_decl += 1; }
    if (variable_instance_exists(id, "_enable_select")) { _enable_select = false; global.ntl_takeover_decl += 1; }
    if (variable_instance_exists(id, "_enable_confirm")) { _enable_confirm = false; global.ntl_takeover_decl += 1; }
    if (variable_instance_exists(id, "_enable")) { _enable = false; global.ntl_takeover_decl += 1; }
    if (variable_instance_exists(id, "_active")) { _active = false; global.ntl_takeover_decl += 1; }
}
if (!variable_global_exists("ntl_takeover_decl_logged")) global.ntl_takeover_decl_logged = 0;
if (global.ntl_takeover_decl_logged == 0)
{
    global.ntl_takeover_decl_logged = 1;
    ntl_log("root", "[root] 官方选择器上确实声明的输入开关：" + string(global.ntl_takeover_decl)
        + " 个（官方未声明的变量已按 F-5 跳过写入，不再凭空新建）");
}

for (var _takeI = 0; _takeI < array_length(_takeMine); _takeI += 1)
{
    var _takeOid = asset_get_index(_takeMine[_takeI]);
    if (_takeOid < 0) continue;
    var _takeN = instance_number(_takeOid);
    if (_takeN <= 0) continue;
    _takeNow += _takeN;
    try { instance_deactivate_object(_takeOid); } catch (e_take) { ntl_log("root", "[警告] 停用 " + _takeMine[_takeI] + " 失败: " + string(e_take)); }
}
if (_takeNow > 0 && (_takeNow != global.ntl_takeover_insts || (global.ntl_frames - global.ntl_takeover_frame) > 60))
{
    ntl_log("root", "[root] 已停用 " + string(_takeNow) + " 个官方选择器实例（非破坏性，可用 instance_activate_object 恢复）");
    global.ntl_takeover_insts = _takeNow;
    global.ntl_takeover_frame = global.ntl_frames;
}
// ★ F-6（t25 复核）：obj_ui_choice 的处置结论必须进日志 —— 它是被标题菜单 obj_screen_start
//   与存档/设置界面共享的控件 ⇒ 我们**绝不销毁、也不停用**（旧版销毁它，标题菜单随后每帧
//   引用死实例：Unable to find instance for object index 100008）。
//   这里只做**只读**统计（实例数 + 其中可输入的数量），让 F-1「是否与官方控件争抢输入」
//   能在一次真机运行里判定：任何一帧出现「可输入 > 0」即说明确实存在输入竞争者。
var _choiceObj = asset_get_index("obj_ui_choice");
var _choiceN = 0;
var _choiceIn = 0;
if (_choiceObj >= 0)
{
    var _choiceCnt = instance_number(_choiceObj);
    for (var _ci = 0; _ci < _choiceCnt; _ci += 1)
    {
        var _chc = instance_find(_choiceObj, _ci);
        if (_chc == noone) continue;
        _choiceN += 1;
        try { if (variable_instance_exists(_chc, "_input_enabled") && _chc._input_enabled == true) _choiceIn += 1; }
        catch (e_chc)
        {
            // 只读统计失败不影响接管；只报一次（避免每帧刷日志）
            if (!variable_global_exists("ntl_choice_read_err"))
            {
                global.ntl_choice_read_err = 1;
                ntl_log("root", "[ntl] obj_ui_choice 可输入状态读取失败（只读统计，不影响接管）: " + string(e_chc));
            }
        }
    }
}
if (!variable_global_exists("ntl_takeover_choice_n")) global.ntl_takeover_choice_n = -1;
if (!variable_global_exists("ntl_takeover_choice_in")) global.ntl_takeover_choice_in = -1;
if (_choiceN != global.ntl_takeover_choice_n || _choiceIn != global.ntl_takeover_choice_in)
{
    ntl_log("root", "[root] obj_ui_choice 不触碰（与标题菜单/存档界面共享）：实例 " + string(_choiceN)
        + " 个，其中可输入 " + string(_choiceIn) + " 个");
    global.ntl_takeover_choice_n = _choiceN;
    global.ntl_takeover_choice_in = _choiceIn;
}

var _pages = ntl_root_pages();
ntl_root_filter();

// ---- ★★★ 用户主诉修复（2026-10-03）：选定章节后，选择器不再接受任何操作 ----
//   触发与释放全在外部章节那两段里（顶部自愈 + park 退出 + 取消分支），这里只负责冻结。
//   冻结期间也不积累按键边沿（ntl_key_reset）：窗口被守候进程隐藏 / 恢复时，
//   残留的按下沿不能变成一次导航或又一次启动（历史事故：一次 Enter 连拉两个外部引擎）。
if (global.ntl_launch_locked == 1)
{
    if (global.ntl_launch_locked_n == 0)
    {
        ntl_log("root", "[root] 已选定章节：选择器输入已冻结（回到选择器后自动恢复）");
    }
    global.ntl_launch_locked_n += 1;
    ntl_key_reset();
    return 0;
}

// ---- 搜索模式 ----
if (global.ntl_ch_search_mode == 1)
{
    // 逐键扫描（不依赖被游戏占用的 keyboard_string）
    // ★ t38：字符扫描每帧**只在 api/events/Step_1.gml 的控制台关闭分支做一次**，这里消费帧缓存。
    //   在本函数里再调 ntl_kb_scan()（修改前此处正是这样写的，见 api/events/Step_1.gml:342-347 的说明）
    //   会读到已被同帧重置的 prev（按下沿恒为假）⇒ 永远没有字符；
    //   缓存由 Step_1 每帧重写（控制台打开 / 外部章节常驻时置空），这里消费后立即清空，
    //   保证同一批字符不会被多帧重复计入。
    // ★ t39 F-3（1.0.1）：先用 is_string 防御再取；缓存被外部写成非字符串时不再 stringify 出奇怪文本。
    //   ★ t39 F-2（1.0.1 决策）：**不改成累加**。单次消费/清空是 t38 acceptance ③④ 的硬约束
    //   （api/events/Step_1.gml 每帧重写缓存、这里消费后立即清空），改成累加会重新打开「同批字符多帧重复计入」的口子；
    //   代价是搜索模式关闭那一帧的按键会被丢掉（需极快连打才偶发，低危）。
    var _ks = "";
    if (variable_global_exists("ntl_kb_frame_chars") && is_string(global.ntl_kb_frame_chars)) _ks = global.ntl_kb_frame_chars;
    global.ntl_kb_frame_chars = "";   // 一帧只消费一次
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
    ntl_ui_sfx("move");   // ★ 自己发声，不依赖官方开始屏（api/ntl_ui_sfx.gml）
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
                // 有内容 = 已部署(enabled) 或 外部引擎章节（与 draw 的判定一致）
                // 修复 2026-09-29：旧条件把「非官方」一律当成有内容，于是未部署的平行时间线
                //   （enabled=0 / kind=timeline，画面上是灰字 无内容）会把光标停住，按 Enter 毫无反应。
                var _kU = string(ds_map_find_value(_mU, "kind"));
                if (ds_map_find_value(_mU, "enabled") == 1 || (_kU != "official" && _kU != "timeline")) break;
            }
            _skippedU += 1;
        }
        if (_skippedU > 0) ntl_log("root", "[root] 上移跳过了 " + string(_skippedU) + " 个空槽");
        global.ntl_ch_sel = _sU;
    }
}
if (ntl_key_fire(40, 250000, 90000) == 1)        // Down
{
    ntl_ui_sfx("move");   // ★ 自己发声（api/ntl_ui_sfx.gml）
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
                // 有内容 = 已部署(enabled) 或 外部引擎章节（与 draw 的判定一致）
                // 修复 2026-09-29：同上移，未部署的平行时间线不再停光标。
                var _kD = string(ds_map_find_value(_mD, "kind"));
                if (ds_map_find_value(_mD, "enabled") == 1 || (_kD != "official" && _kD != "timeline")) break;
            }
            _skippedD += 1;
        }
        if (_skippedD > 0) ntl_log("root", "[root] 下移跳过了 " + string(_skippedD) + " 个空槽");
        global.ntl_ch_sel = _sD;
    }
}
if (ntl_key_fire(37, 250000, 120000) == 1)        // Left
{
    ntl_ui_sfx("move");   // ★ 翻页音也由自己发（官方同用 7 号音效）
    global.ntl_ch_page -= 1;
    if (global.ntl_ch_page < 0) global.ntl_ch_page = _pages - 1;
}
if (ntl_key_fire(39, 250000, 120000) == 1)        // Right
{
    ntl_ui_sfx("move");   // ★ 翻页音也由自己发
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
// ★ t35 F5（t30 真机 ③）：窗口以前**写死 90 帧** —— 原版 chapter1 的 room_speed=30 时 ≈3 秒，
//   但 60 FPS 差异层（mods/60fps_layer）生效时 room_speed=60（docs/PIPELINE.md:566 实测
//   `room_speed=60 fps=60`）⇒ 窗口只剩 ≈1.5 秒：t30 日志行 1604/1605/1606/1607 正是
//   「确认→超时→确认→超时」四连，两次 Esc 相隔约 2 秒全落在窗外，游戏不退出。
//   现在按**真实帧率**换算：窗口帧数 = max(1, round(_qsecs × fps))，任何帧率下都 ≈3 秒。
// ★ 1.0.1 复核（t36 F-1）：复核意见担心「arm 与倒数同帧」⇒ 窗口只有 N-1 帧；实际不会：
//   arm 分支在「keyboard_clear(vk_escape); return 1;」处就返回，倒数块在 arm 帧根本不执行
//   ⇒ 可用窗口 = N 帧（N = max(1, round(3 × fps))）再加上 arm 帧本身，恒 ≥ 标称 3 秒（30fps ≈ 3.03s）。
//   结论：不改倒数逻辑，标称「3 秒」是保守口径（30fps 2.97s / 60fps 2.98s 的说法不成立）。
if (!variable_global_exists("ntl_quit_armed")) global.ntl_quit_armed = 0;
if (!variable_global_exists("ntl_quit_frames")) global.ntl_quit_frames = 0;
if (!variable_global_exists("ntl_quit_pending")) global.ntl_quit_pending = 0;
if (ntl_key_fire(27, 0, 0) == 1)        // Esc
{
    if (global.ntl_quit_pending == 0)
    {
        // 真实帧率（兜底与 api/ntl_console_action.gml:112-114 同口径：异常/非正数一律按 30）
        var _qfps = 30;
        try { _qfps = game_get_speed(gamespeed_fps); } catch (e_qfps) { _qfps = 30; }
        if (!is_real(_qfps) || _qfps <= 0) _qfps = 30;
        if (global.ntl_quit_armed == 1)
        {
            global.ntl_quit_armed = 0;
            // 「正在退出游戏…」也按真实帧率换算（写死 24 帧在 60 fps 下只有 0.4 秒，看不清）
            global.ntl_quit_pending = max(1, round(0.8 * _qfps));
            ntl_log("root", "[root] 玩家确认退出游戏（Esc 两连）");
        }
        else
        {
            var _qsecs = 3;                       // 唯一时长真值：窗口、日志、提示都从它来
            global.ntl_quit_armed = 1;
            global.ntl_quit_frames = max(1, round(_qsecs * _qfps));
            var _qmsg = "[root] 退出确认：" + string(_qsecs) + " 秒内再按一次 Esc 退出游戏（窗口 " + string(global.ntl_quit_frames) + " 帧 @ " + string(_qfps) + " fps）";
            ntl_log("root", _qmsg);
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
// 官方章节选择器已被我们**停用**（非破坏性接管、可用 instance_activate_object 恢复，见本文件
//   :170-262）→ Enter/Z 不会再被抢走，普通章节和外部章节都可以直接按 Enter/Z
if (ntl_key_fire(13, 0, 0) == 1 || ntl_key_fire(90, 0, 0) == 1)
{
    // ★★★ 用户主诉修复（2026-10-03）：**选章确认音不再播放** —— 选择章节之后不该有任何音效。
    //   以前这里先播 ntl_ui_sfx("confirm")（当时的理由：外部章节会立刻 park 静音 + 隐藏窗口，
    //   放在 ntl_root_launch 之后就听不到），现在整声去掉，确认键只负责启动。
    //   只有「没有真的启动」的情况才发一声提示音：章节无内容见 api/ntl_root_launch.gml 的未启用分支。
    //   上下移动/翻页的 ntl_ui_sfx("move")（本文件 :548/:582/:614/:620）不受影响。
    // ★★★ 用户主诉修复（2026-10-03）：从这一刻起选择器不再响应任何操作，
    //   直到回到选择器（park 退出）或本次启动被取消（见文件顶部自愈 + :86-95 / :38-46）。
    global.ntl_launch_locked = 1;
    global.ntl_launch_locked_n = 0;
    ntl_root_launch(global.ntl_ch_sel);
    // ★ 用户主诉修复（2026-10-03）：启动后立刻清掉 Enter/Z 的按下沿与按下状态。
    //   实测事故：外部章节请求写出后窗口被守候进程隐藏，GM 在窗口隐藏时会暂停 ⇒ 这次按下
    //   一直留在按键状态里，等 park 结束、窗口恢复时**又触发一次启动**（一次 Enter 连拉两个
    //   外部引擎：日志里 Kanacole 之后又冒出 Frostveil）。ntl_key_reset 清我们自己的边沿表，
    //   keyboard_clear 清 GM 状态（同 :195 / :380 的写法）。
    keyboard_clear(vk_enter);
    keyboard_clear(90);
    ntl_key_reset();
    return 1;
}
return 1;
