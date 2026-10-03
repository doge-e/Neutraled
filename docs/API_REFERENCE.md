# Neutraled API 参考

> 自动生成于 2026-10-03 10:38（从 api/*.gml 的注释块提取）
> 共 369 个脚本，其中 365 个有文档

---

## 其他（115）

### `ntl_api_version_check(modApiVersion)`

检查 mod 声明的 API 版本是否兼容

mod.json 可声明 "api_version": "1.0" —— 若主版本号不匹配则警告

### `ntl_apply_lang_overrides()`

把覆盖表写入 global.lang_map（须在语言加载完成后调用）

### `ntl_autoskip()`

顶层：按配置自动跳过章节选择器，直接进入目标章节

### `ntl_box()`

占位对象构造器（历史兼容；核心不依赖 struct）

### `ntl_call_host`

老式脚本（每个函数一个同名脚本资源）

### `ntl_call_ns(name, args)`

解析 "ns.func" 形式的依赖调用

返回 ds_map { found: 0/1, value: 结果 }（found=0 表示不是命名空间调用）

### `ntl_cfg_row(k)`

给 CONFIG 页第 k 项设置"该不该画"（返回 1/0 并写入 draw_set_alpha）。

窗口外的行必须**彻底透明**：第 0 项落到 yy+115 会压到标题「CONFIG」，
```gml
第 7 项(Back)在窗口未滚动时会画到 yy+395（超出原版框底 yy+410）。
```
每一项绘制前都要调用（含数值列），否则滚动时会看到"鬼影行"。
⚠ 踩坑：这里本来漏了 `var _k = argument[0];` ⇒ 运行期直接 Code Error
「Variable obj_darkcontroller._k not set before reading it」（真机 nat-13 第一次就崩在 CONFIG 页）。

### `ntl_cfg_scroll()`

设置(CONFIG)页当前的行窗口起点（0 或 1）。

原版 7 项 + 我们的「Mod 设置」= 8 项；可见窗口是 7 行（y = yy+150 … yy+360，行高 35）。
★ 用户 m11431 明确要求「不要伸缩设置窗口，可以设置滚动条来装下更多内容」⇒
```gml
菜单框回到原版高度（langopt([90,410,420],[85,412,422])），多出来的那一行靠滚动容纳：
光标在第 8 项(Back)时窗口下移一行，第 1 项(Master Volume)让位。
```

### `ntl_cfg_scrollbar_draw(xx, yy)`

CONFIG 页右侧的滚动条（8 项 / 可见 7 行）。

官方二级菜单没有滚动条；用户要求「不要伸缩窗口，用滚动条装下更多内容」（m11431）。
轨道 = 可见行区间（yy+150 … yy+384），滑块长度/位置按 可见÷总数 与 窗口起点 算。
始终画（不是"滚动后才出现"）：让玩家一眼看出下面还有内容（第 8 项 Back）。

### `ntl_chg_watch()`

game_change 看门狗：切换章节静默失败时按阶梯重试

背景（2026-09-30 用户实测）：从章节内菜单「回到章节选择」返回到 hub 之后，连续 30 次
Enter 都没能切走 —— 日志里每次都有 ntl_goto_chapter -> /chapterN_windows，但进程
一次都没换。game_change 没有返回值，失败时**不抛异常也不报错**，调用方因此永远以为
自己成功了（这就是「静默失效」）。
做法：调用方在 game_change 之前记录现场（ntl_chg_* 一串 global，含 working_directory /
program_directory / 目标目录 / 参数 / 序数 / 章节类型）；本函数由 events/Step_1.gml
每帧调用（任何进程都跑）。≥90 帧后本进程仍在运行 = 那一次 game_change 没生效，
于是按阶梯重试，每级打一张含运行现场的日志；全部失败就给可见提示并停手。
```gml
阶梯：① 官方入口 obj_CHAPTER_SELECT.launch_game(章节号)（只对官方章节；实例被停用时先激活）
```
② 备选前缀 "/"、""（相对当前目录）、"/../" 各试一次（game_change 的目录前缀随进程而异）
③ 都不行：toast「章节启动失败」+ 日志，清 pending 不再重试
防双重启动：①global 是进程级的 —— 真的切走了，新进程里根本没有 ntl_chg_pending；
②每次重试前比对 working_directory / program_directory，变了说明上下文已经换了；
③每级只试一次、每 90 帧才试一级（同一帧不会连发两个 game_change）；④失败后清标志。

### `ntl_config_load()`

读取 Neutraled/config.json 到 global.ntl_cfg

配置项（全部可选）：
auto_skip_selector : 自动跳过章节选择器，直接进入 auto_chapter
auto_chapter       : 自动进入的章节序号（1-7 官方；mod 章节用 auto_chapter_id）
auto_chapter_id    : 直接指定章节 id（如 timeline:4:mod:alt）
auto_skip_delay    : 跳过前的等待帧数（默认 90）
auto_skip_intro    : 尝试跳过章节内的开场演出（实验性）
debug_live         : 打开 live 脚本调试日志
skip_legend / skip_logo / intro_delay / lang
```gml
★ 2026-09-27：候选路径见 ntl_config_paths()（存档区 + 游戏根，root 产物受 GM 文件沙箱遮蔽）。
```
规则：**两处都读**，后读的（游戏根那份）覆盖先读的（存档区那份）；读完把游戏根那份原文
镜像回存档区 ⇒ 章节内改的语言/CLI 改的配置都能传到下一次启动的章节选择器。

### `ntl_config_paths()`

Neutraled/config.json 的候选路径数组（存档区在前、游戏根在后）

★ 为什么是两个路径（2026-09-27 真机探针取证，别再改回单路径）：
· 章节产物（chapterN_windows/data.win）读写的是游戏根 <program_directory>Neutraled/config.json；
· root 产物（游戏自带启动器 data.win）带 GameMaker 文件沙箱：
写 bundle 路径 → 实际落到存档区 <game_save_id>Neutraled/config.json；
读 bundle 路径 → 只要存档区存在同名文件就被它遮蔽（逐文件遮蔽：
同一次启动读 Neutraled/mods/ 仍命中真实 bundle，因为存档区没有 mods 目录）。
实测：root 阶段 program_directory 明明是游戏根，读 config.json 却拿到存档区那份的内容。
⇒ 两个阶段的读路径天然不同，所以：**两处都读**（后者 = 游戏根优先）、**两处都写**。

### `ntl_config_read_text(path)`

原样读回文本（文件不存在或读失败返回 ""）

### `ntl_config_set_lang(code)`

把语言码写回 Neutraled/config.json（保留其它字段）

纯文本级替换：游戏里没有 JSON 序列化，手写最稳。返回 1 成功 / 0 失败。
★ 2026-09-27：两份副本都写（存档区 + 游戏根，见 ntl_config_paths.gml 的沙箱说明）。
读原文取"存在的最后一个候选"（= 游戏根优先），写完把同一份文本写回所有候选，让两份收敛。
★ 2026-09-29 修复「重复 lang 键」：判据从「替换后文本没变」改成显式 _found —— 要写的语言码
与文件里的现值相同时，旧判据会误判成「没有 lang 字段」而走追加分支，写出第二个 lang 键；
builder 用 System.Text.Json 读重复键会抛 "An item with the same key has already been added.
Key: lang"，于是 --plugin-hooks / --lang-coverage / --plugin-list 等全部崩。
同一处顺手自愈历史遗留的第二份 lang 键（整对删除，连它的分隔逗号），写回的文件永远只有一份。

### `ntl_config_write_text(path, txt)`

写入文本并回读校验，返回 1 成功 / 0 失败

注意：root 产物受 GameMaker 沙箱影响，写 bundle 路径会落到存档区（见 ntl_config_paths.gml）。

### `ntl_draw_mods()`

广播 on_draw（mod 在 GUI 层绘制自己的东西）

### `ntl_dsmap_keys(map)`

取 ds_map 的 key 数组；空 map / 非法 map 一律返回空数组 []

```gml
★ 真机硬坑（2026-09-26 定位）：GameMaker 2023.6 的 ds_map_keys_to_array(空 map) 返回 undefined
（不是空数组），随后 array_length(undefined) 也是 undefined，于是
`array_length(_keys) - N` 会以 "DoSub :2: undefined value" 崩掉整局
```
（真机现场：root 进程 obj_init_pc Create → ntl_hook_init，开机即 Code Error）。
需要取 key 时一律用本函数，不要直接调 ds_map_keys_to_array。

### `ntl_e_env_get`

老式脚本（每个函数一个同名脚本资源）

### `ntl_e_env_set`

老式脚本（每个函数一个同名脚本资源）

### `ntl_e_new_env`

老式脚本（每个函数一个同名脚本资源）

### `ntl_emit(event_name, args_array)`

广播事件到所有订阅者（handler 以 args 单参调用）

### `ntl_ev`

老式脚本（每个函数一个同名脚本资源）

### `ntl_ex`

老式脚本（每个函数一个同名脚本资源）

### `ntl_ext_launch(chapterMap)`

启动"外部引擎章节"（Kristal 等项目）

背景：Kristal 是 LÖVE 工程（main.lua + conf.lua + data/），**没有 GameMaker data.win**，
在本运行时里加载不了它。与其移植引擎，不如让用户去玩它自己的引擎：
插件加载、进度保存、通关判定全部是 Kristal 原生行为，一点都不用仿。

★ 实测：本运行时**没有任何启动进程的内置函数**（execute_program / execute_shell /
os_start_process / url_open 在 data.win 字符串池里一个都不存在），所以 GML 自己拉不起程序。
做法改成：**写一个启动请求文件，然后退出游戏**；外面的启动器（GUI / --watch-external）
看到请求就把 Kristal 拉起来。
返回 1 = 已写出请求（调用方必须立刻 return，不要再做章节切换）

### `ntl_font_big()`

我们界面要用的「官方菜单字号」字体（mainbig，EmSize 24）。

为什么不用 ntl_font_cjk：那是内置字体包（EmSize 12），只有官方菜单字号的一半大
⇒ 用户反馈「mod设置及其二级菜单字体太小了」（m10511/m11431）。部署期 FontMerge 已把
CJK 字形补进 fnt_mainbig/fnt_main，所以直接用游戏自己的字体画中文就是官方字号。
```gml
用 asset_get_index(名字) 而不是硬编码索引：主字体索引随游戏语言变化（日文语境下
```
font_map 的 mainbig 变成 fnt_ja_mainbig，那个字体没补我们的字形），按名字取才稳。

### `ntl_font_main()`

游戏正文/说明行字号（main，EmSize 12，部署期已补 CJK 字形）。

二级菜单底部的说明行用它：官方那行本来就是小字体，用 mainbig 会超出菜单框宽度
（说明文案里有「本产物实际加载 0 个（磁盘上已安装 47 个）。」这类长句）。

### `ntl_get_lang_string(key)`

读取生效的文本（覆盖优先，其次 lang_map）

### `ntl_goto_chapter(chapter)`

直接切换章节

```gml
【已搬迁】官方选择器的 launch_game 内部就是 game_change(dir, "-game data.win" + params)，
```
这里改用我们**自己的章节表**（chapters.json → global.ntl_ch）拿 dir，不再依赖 obj_CHAPTER_SELECT。
这样章节选择就能完全由 Neutraled 掌控（官方对象不再监听 Enter，外部章节才能常驻秒回）。
返回 1 成功 / 0 失败（表里查不到时回退到官方对象）。

### `ntl_has_focus()`

游戏窗口是否在前台（我们所有 keyboard_check_direct 读键的总闸）

```gml
背景：DELTARUNE 每帧可能调用 keyboard_clear_all()，所以我们的输入一律用 keyboard_check_direct
```
（硬件状态，清不掉 GM 状态）；但硬件状态**不区分窗口** —— 游戏在后台时，玩家在别的窗口
打字也会被我们读进来（用户实测：控制台把游戏外的输入写了进去）。
```gml
判定顺序：os_is_paused()（游戏自己的 obj_time 用它判"切出去"）→ window_has_focus()（较新运行时才有）
```
→ 都不可用则返回 1（保持旧行为，绝不误伤正常输入）。
每帧只求值一次（缓存在 global.ntl_focus_now / ntl_focus_frame），并在状态翻转时记一行日志便于取证。

### `ntl_heart_sprite()`

官方二级菜单里那个「选中红心」的精灵索引。

★ 为什么必须按名字取、不能硬编码索引：**精灵索引每个章节都不一样** ——
chapter4 = 3695、chapter1 = 922（对照表见 docs/MANAGE.md §11.4）。
硬编码 3695 的版本在 chapter1 真机弹过（2026-09-27，用户 m12373 报错）：
ERROR in action number 1 of Draw Event for object obj_darkcontroller:
Trying to draw non-existing sprite.
at gml_Script_ntl_modmenu_page_draw
同一类坑的既有先例：ntl_font_big.gml / ntl_font_main.gml 也是按名字取字体
（主字体索引随语言变化，日文语境下 mainbig 会换成 fnt_ja_mainbig）。
返回 -1 = 一个都没找到（调用方必须能不吃 sprite 地画下去）。

### `ntl_hook(event_name, handler_script)`

订阅事件（handler 为脚本资源索引或函数）

返回订阅序号（失败返回 -1）

### `ntl_hotreload_check()`

检测 IDE 写入的 reload.flag，触发热重载 live 脚本

让开发者在 IDE 里改脚本 → 保存 → 游戏内立即生效（无需重启游戏）

### `ntl_is_ascii(s)`

全是 ASCII 返回 1（决定要不要切 CJK 字体：纯 ASCII 就用游戏自己的字体才合群）

### `ntl_is_root()`

当前进程是不是"顶层章节选择"的 root 进程？

判据（按可靠性排序）：
① 部署时写入的产物清单 <working_directory>Neutraled/scope.json 的 target == "root"
（builder 写 mods.json 时同目录一并写；这是唯一不靠"猜"的来源）
② 归一化后 working_directory == program_directory ⇒ 本进程跑的就是游戏根产物
⚠ 旧判据「working_directory 里不含子串 chapter」有两个洞（2026-09-26 审计）：
a) 安装路径本身含 "chapter"（例 E:\games\chapter_test\DELTARUNE）→ root 被判成章节进程
（选择器界面完全不画、autoskip 不跑）；
b) 产物目录名不含 "chapter"（独立章/时间线，如 ntl_timeline_4_test_timelineforest_forest）
→ 章节进程被判成 root → ntl_autoskip 会把玩家**弹去官方章节**（ntl_autoskip.gml:54-59）。
所以路径只作次选，且判据改成"与 program_directory 相等"而不是子串。

### `ntl_kb_guard_check(key)`

keyboard_check 的控制台守卫

### `ntl_kb_guard_direct(key)`

keyboard_check_direct 的控制台守卫

### `ntl_kb_guard_pressed(key)`

keyboard_check_pressed 的控制台守卫

### `ntl_kb_guard_released(key)`

keyboard_check_released 的控制台守卫

### `ntl_kb_guard_string()`

控制台打开时返回空字符串

### `ntl_kb_scan()`

逐键扫描本帧新输入的字符（返回字符串）

⚠️ 关键：DELTARUNE 每帧可能调用 keyboard_clear_all，会清掉 GM 的按键状态，
所以一律用 **keyboard_check_direct**（直接读硬件状态，清不掉）。
为了得到"按下一次"的语义，用 global.ntl_kb_prev 记录上一帧状态。

### `ntl_key_fire(key[, firstDelayUs, repeatUs])`

「本帧该触发一次吗」（自带按下沿 + 按住自动重复）

```gml
为什么不用 keyboard_check_pressed：DELTARUNE 每帧 keyboard_clear_all() 会把 GM 的按键状态清掉，
```
实测表现就是「按下了却没反应」（用户反馈：控制台 Backspace 常常按了不删字）。
为什么不用裸 keyboard_check_direct：它是硬件状态，**游戏不在前台时同样为真** ——
```gml
玩家在别的窗口打字会被读进来（用户实测）。所以统一过 ntl_has_focus() 这道门。
```
参数：firstDelayUs = 按住多久后开始重复（微秒；0 = 只在按下沿触发一次）
repeatUs     = 重复间隔（微秒；<= 0 = 不重复）
返回：0 或 1（本帧触发次数）

### `ntl_key_reset()`

把 ntl_key_fire 的按键状态**对齐到当前物理状态**

用途：切控制台、窗口重新获得前台、面板里执行完一次动作后调用，避免"切换那一刻已经按住的键"被当成新按下。
```gml
★ 不能直接 ds_map_clear()：清空之后，"此刻仍按住的键"下一帧又会被当成新按下 ——
```
实测踩过：面板里按一下 Z 切语言，因为 Z 还按着，之后每帧都触发一次 ⇒ 一次按键把 6 种语言整整转了一圈。
正确做法是把每个已跟踪的键写成"当前物理状态 + 计时器归零"：按住期间不再触发，松开后重新按下照常触发。

### `ntl_keynorm(s)`

匹配用归一化：转小写、只保留 a-z0-9（"Deltarune 60 FPS" → "deltarune60fps"）

用途：把 mods/ 目录名（ForestPatch、TimelineForest、deltarune_60_fps）
对上产物 mods.json 里的 Name/Id 段（"Forest Patch"、"test.timelineforest"、"Deltarune 60 FPS"）——
空格、下划线、连字符、点、大小写的差异全部抹平。非 ASCII（中文目录名）会被丢弃，不会误匹配。

### `ntl_kr_flag_key(args)`

从参数表里取出 flag 键

跳过表参数（方法调用的 self 或配置对象），返回第一个字符串或数字

### `ntl_kr_flag_value(args)`

从参数表里取出 flag 的值（最后一个非表参数）

### `ntl_log(tag, msg)`

写运行日志（相对路径，沙箱重定向到 %LOCALAPPDATA%\DELTARUNE\）

### `ntl_menu_add(id, label, value, action, desc, owner)`

往 Mod 设置面板主列表加一项（mod 开发者接口）

label/value 传文案，或传 "i18n:键名" 走 i18n 表；action 是脚本名（玩家按 Z/Enter 时调用），空串 = 只显示。
owner（可选，第 6 参）= 这个开关属于哪个 mod（如 "60fps_layer"）——
面板会把这些项收进分组标题「── <owner> ──」下面并缩进一档，
这样玩家一眼能看出哪些开关是 mod 的、哪个 mod 的（用户 m17504 的诉求）。
不传 owner 的项会归到通用组「模组选项」。
同 id 重复调用 = 覆盖（热重载不会出现两行）。例：
```gml
ntl_menu_add("mymod.speed", "i18n:mymod.speed", "2x", "mymod_toggle_speed", "i18n:mymod.d_speed", "mymod");
```

### `ntl_menu_clear()`

清空所有 mod 注册项（mod 重载时用）

### `ntl_menu_count()`

当前有几个 mod 注册的面板项

### `ntl_menu_entry(i)`

取第 i 个 mod 面板项的 ds_map（越界返回 -1）；面板内部用

### `ntl_menu_remove(id)`

撤掉自己加的面板项（1 = 删掉了，0 = 本来没有）

### `ntl_menu_run(i)`

触发第 i 个 mod 面板项的 action（面板内部用；1 = 真的调用了）

### `ntl_menu_run_script(name)`

按脚本名跑一个面板项的动作（mod 接口 ntl_menu_add 的内部实现）

返回 1 = 跑成功；0 = 找不到脚本（面板会显示「只读」提示，绝不静默失败）。
```gml
与 ntl_menu_run(下标) 的区别：分组标题插进来之后下标会漂，所以面板按脚本名调用。
```

### `ntl_menu_set_value(id, value)`

改自己那一项右侧的数值文案（1 = 改到了）

### `ntl_menu_text(s)`

面板文案解析："i18n:键名" 走 i18n 表，否则原样返回

### `ntl_modmenu_close()`

关闭面板：回到游戏的设置菜单页（submenu 30）

### `ntl_modmenu_count()`

当前视图几行（= ntl_modmenu_rows() 的长度，永远与 ntl_modmenu_row 一致）

### `ntl_modmenu_deploy()`

写重新部署请求，然后退出游戏交给守候进程

本运行时**没有任何启动进程的内置函数**（execute_program / execute_shell / os_start_process / url_open
```gml
在 data.win 的字符串池里都不存在），所以只能「写请求文件 + game_end()」，由 builder --watch-external 消费。
```
★ 只写 ASCII：GM 的 file_text_write_string 按 GBK 落盘，中文会乱码。

### `ntl_modmenu_find_action(act)`

当前视图里第一个 action == act 的行号（找不到返回 0）

用途：从语言列表返回主列表时要停在「界面语言」那一行，但不能写死下标 ——
主列表现在会插 mod 分组标题，下标会漂。

### `ntl_modmenu_goto(idx)`

面板章节视图：进入 global.ntl_ch[idx] 对应的章节

### `ntl_modmenu_lang_cycle([step])`

切到上/下一个可用语言（←→ 快切；step 省略 = +1）

### `ntl_modmenu_lang_set(code)`

把界面语言切到指定代码（语言列表 / ←→ 快切 都用它）

### `ntl_modmenu_loaded()`

本产物**实际加载**的 mod 清单（数组，元素 = struct: Id/Name/Version）

来源：builder 每次部署写在**产物目录**里的 <working_directory>Neutraled/mods.json
★ 2026-10-02（用户 m23281「显示 0 mod 加载」）真根因：builder 那时用**默认编码器**写 mods.json，
「冰封帷幕」「汉化组」等含中文的 mod 名被写成 \uXXXX 转义，而 **GameMaker 的 json_parse 吃不下 \uXXXX**
（老坑 15，chapters.json 已实测）⇒ json_parse 抛异常 ⇒ 本函数返回空数组 ⇒ 面板恒显示「已加载 0」。
已在 builder 侧修掉（Program.cs:2432 改用 Paths.Json）；这里保留双形态读取 + 转义计数诊断作兜底。
注意：**不是**「顶层数组读不出数组」——顶层数组形态本身没问题，见 ntl_json_diag 对照组。
读不到（老产物 / 外部章节 exe）返回空数组，调用方回退显示"已安装"数。
```gml
缓存：global.ntl_modmenu_loaded_cache（面板打开时由 ntl_modmenu_open() 清掉重读）。
```

### `ntl_modmenu_loaded_count()`

本产物**已加载**的 mod 数。

返回 -1 = 本产物没有 Neutraled/mods.json（老产物 / 外部章节 exe）⇒ 调用方回退显示"已安装"数。
```gml
每帧都可能被入口行调用，所以缓存（面板打开时由 ntl_modmenu_open() 清掉重读）。
★ 2026-10-02（用户 m23281「显示 0 mod 加载」）：旧实现 = file_exists 就 array_length(loaded())，
```
清单解析不出来时恒为 0（真机实测：文件在、11 条目、面板却报 0）。
现在：清单解析不出来时按原文里 "Id" 出现次数兜底；只有**文件不存在**才返回 -1。

### `ntl_modmenu_modcount()`

已装模组数量（设置菜单第 6 行右列显示用）

每帧都会调，所以缓存一次；面板打开时会刷新（见 ntl_modmenu_open.gml）。

### `ntl_modmenu_modinfo(dirname)`

把 mods/ 目录名对到「本产物已加载清单」：返回 [已加载(0/1), 显示名, 版本]

```gml
数据源：ntl_modmenu_loaded()（<working_directory>Neutraled/mods.json，deploy 时写）。
```
匹配规则见 ntl_keynorm（大小写/空格/下划线/点不敏感；Id 另外按 "." 分段逐段比，
例如目录 dojo 命中 Id "converted.dojo.xanzo1" 的第 2 段）。
结果缓存在 global.ntl_modmenu_modinfo_cache（面板打开 / 每次按键时清空）；
读不到清单（老产物、外部 exe）时一律返回「未加载」，绝不猜。

### `ntl_modmenu_mods()`

扫描 Neutraled/mods/ 下的模组目录（面板展示用）

返回已排序的字符串数组；扫描失败返回空数组（面板显示「没有已装模组」）。

### `ntl_modmenu_move(coord, dir)`

光标移动：跳过分组标题、两端循环

dir > 0 往下、dir < 0 往上。整屏都不可选（理论上不会发生）时原样返回，绝不死循环。

### `ntl_modmenu_open()`

打开 Mod 设置面板：切进游戏自己的二级菜单 id 51

★ 面板不是自绘遮罩，而是游戏 submenu 体系里的一页
（参考实现：mods/deltarune_60_fps 的 submenu 50「MOD SETTINGS」）：
```gml
绘制 = 注入 obj_darkcontroller 的 Draw 的 ntl_modmenu_page_draw(xx, yy)
输入 = 注入 obj_darkcontroller 的 Step 的 ntl_modmenu_page_step()
```
⇒ 坐标、字体（mainbig）、红心、滚动条、按键缓冲全部沿用游戏自己的那一套。
submenu id 选择：原版占 1-7 / 10-14 / 20-22 / 30-36，60fps 占 50 ⇒ 我们取 51。

### `ntl_modmenu_page_draw(xx, yy)`

Mod 设置页（submenu 51）的绘制：照游戏自己的二级菜单写

参考实现：mods/deltarune_60_fps（BadArtAdventure）的 submenu 50「MOD SETTINGS」页 ——
行高 35、标签列 _xPos、数值列 _selectXPos、选中用官方红心精灵（名字解析，y = yy+160+行*35）、
可见 5 行、yy+330 分隔线、yy+340 说明行。
★ 与参考实现的差别（用户 m11431 的要求）：
- 字体用游戏自己的 mainbig（EmSize 24，部署期已补 CJK 字形），不再切 ntl_font_cjk（只有一半大）；
- 说明行用游戏正文 main（EmSize 12）；
- 滚动提示改成**真正的滚动条**（参考页只有上下两个三角）；
- 官方素材没被拉伸：菜单框仍是原版高度（我们不画框，框由官方 Draw 画）。
★ 本轮（用户 m17504「mod 设置里没有处理长文本，设置选项无法分清是哪个 mod 的」）：
- 标签列/数值列都有列宽，超宽一律 ntl_text_fit 截断 + "..."（原来是裸 draw_text，直接压到隔壁列、
甚至顶穿面板右边框 —— 章节视图/模组视图的截图是铁证）；
- 分组标题行（kind 1）用灰色画，没有数值；模组项（kind 2）缩进一档；
- 右上角画「当前 / 总数」；被截断的那一行把**全名回显到说明行**（截断不丢信息）；
```gml
- 说明行过 ntl_text_fit_lines(..., 470, 3)，再长的说明也不会画到菜单框外面。
```
由 builder 注入 obj_darkcontroller 的 Draw（builder/Injector.cs）。
★ 绘制状态（字体/颜色/对齐/alpha/纹理过滤）必须完整保存恢复，否则会把后面的界面染坏。

### `ntl_modmenu_page_step()`

Mod 设置页（submenu 51）的输入：照游戏自己的二级菜单写

由 builder 注入 obj_darkcontroller 的 Step（menuno==5 块里、与 submenu==34 同级，每帧跑）。
键位与游戏一致：↑↓ 移光标（movenoise + 循环）、Z/Enter 确认（button1_p + onebuffer<0）、
X 返回（button2_p + twobuffer<0，回设置菜单）；语言行额外支持 ←→ 快切
（官方 submenu 36 的边框选择也是这个做法）。
★ onebuffer/twobuffer 是游戏自己的按键缓冲：这里必须沿用，否则同一次按键会在
打开面板的那一帧被面板自己再吃一次。
★ 本轮（用户 m17504）改动：
```gml
- 光标移动走 ntl_modmenu_move()：跳过分组标题（纯排版行不可选），不再靠写死的行号；
```
- 确认动作按**行自带的 action** 分派（"chapters"/"mods"/"lang"/"deploy"/"close"/"script:<名字>"），
不再写死 _coord == 0/1/2/3 —— 主列表里插了分组标题之后，下标会漂；
- mod 注册项按**脚本名**调用（ntl_menu_run_script），不再用下标算（分组标题会让下标错位）；
- 每次按键后清空行缓存（开关值/计数会变，不能让缓存骗人）。

### `ntl_modmenu_row(i)`

当前视图第 i 行的数据：[标签, 数值, 说明, 变灰, kind, action]

```gml
行列表的唯一来源是 ntl_modmenu_rows()（顺序/文案/数量只定义在那里，改行不会漏改绘制或输入）；
```
本函数只负责按下标取一行。越界返回空数组。
老调用方只读前 4 个元素（标签/数值/说明/变灰），新增的 kind/action 在末尾，向后兼容。

### `ntl_modmenu_rows()`

当前视图的完整行列表（**唯一来源**：绘制 / 输入 / 计数都读它）

每行 = [标签, 数值, 说明, 变灰, kind, action]：
kind   0 = 普通项（可选）  1 = 分组标题（纯排版，光标跳过）  2 = 模组项（可选的普通项，缩进一档）
action 主视图按 Z 时干什么：
"chapters" / "mods" / "lang" / "deploy" / "close" = 面板内置动作
"script:<脚本名>" = mod 用 ntl_menu_add 注册的开关（跑那个脚本）
"" = 没有动作（分组标题）
子视图（chapters/mods/langs）的输入由各自的按键分支处理，这里统一留 ""。
★ 用户 m17504「设置选项无法分清是哪个 mod 的」的修法：mod 项不再与内置行混排，
而是插在分组标题「── <mod 名> ──」下面并缩进一档。
缓存：global.ntl_modmenu_rows_cache = [签名, 行数组]；按键确认 / 打开面板 / 切语言时清空
（行内容会随开关状态变，别让缓存骗人）。

### `ntl_modmenu_selectable(i)`

第 i 行能不能被光标选中（分组标题只用来排版，必须跳过）

行结构见 ntl_modmenu_rows：kind == 1 就是分组标题；越界 / 空行返回 0。

### `ntl_modmenu_slot(view)`

面板视图 → 游戏 submenucoord 的槽位

每个视图各占一个槽（和游戏给每个子菜单一个 submenucoord[id] 的做法一致）：
main=51 / chapters=52 / langs=53 / mods=54；51 同时是「面板开着」的 submenu id。

### `ntl_modmenu_step()`

面板的计时器与状态同步（由 api/events/Step_1.gml 每帧调用）

★ 面板自己的按键**不在这里**：面板已经是游戏 submenu 体系里的一页（id 51），
```gml
输入由注入 obj_darkcontroller 的 ntl_modmenu_page_step() 处理（与游戏同帧、同一套按键缓冲）。
```
这里只做三件事：消息倒计时、部署退出倒计时、把 global.ntl_modmenu_open 与真实的 submenu 对齐。

### `ntl_p_add`

老式脚本（每个函数一个同名脚本资源）

### `ntl_p_and`

老式脚本（每个函数一个同名脚本资源）

### `ntl_p_block`

老式脚本（每个函数一个同名脚本资源）

### `ntl_p_cmp`

老式脚本（每个函数一个同名脚本资源）

### `ntl_p_err`

老式脚本（每个函数一个同名脚本资源）

### `ntl_p_expect_op`

老式脚本（每个函数一个同名脚本资源）

### `ntl_p_expr`

老式脚本（每个函数一个同名脚本资源）

### `ntl_p_if`

老式脚本（每个函数一个同名脚本资源）

### `ntl_p_is`

老式脚本（每个函数一个同名脚本资源）

### `ntl_p_is_op`

老式脚本（每个函数一个同名脚本资源）

### `ntl_p_let`

老式脚本（每个函数一个同名脚本资源）

### `ntl_p_mul`

老式脚本（每个函数一个同名脚本资源）

### `ntl_p_new`

老式脚本（每个函数一个同名脚本资源）

### `ntl_p_next`

老式脚本（每个函数一个同名脚本资源）

### `ntl_p_node`

老式脚本（每个函数一个同名脚本资源）

### `ntl_p_or`

老式脚本（每个函数一个同名脚本资源）

### `ntl_p_peek`

老式脚本（每个函数一个同名脚本资源）

### `ntl_p_primary`

老式脚本（每个函数一个同名脚本资源）

### `ntl_p_program`

老式脚本（每个函数一个同名脚本资源）

### `ntl_p_statement`

老式脚本（每个函数一个同名脚本资源）

### `ntl_p_unary`

老式脚本（每个函数一个同名脚本资源）

### `ntl_p_while`

老式脚本（每个函数一个同名脚本资源）

### `ntl_product_scope()`

本进程所在产物的「部署目标名」：root / chapter1..5 / 独立章（时间线的 srcChapter）

来源：部署时由 builder 写进**每个产物目录**的 <working_directory>Neutraled/scope.json：
{ "version": 1, "target": "chapter1" }（builder/Program.cs 写 mods.json 的同一处）

⚠ 为什么必须靠文件而不是靠猜（2026-09-26 审计 + 真机日志）：
- 所有产物共用同一个 exe ⇒ program_directory 恒为游戏根，只有 working_directory 能区分产物；
- 旧代码按「路径里第一个 chapter 后面的数字」猜作用域，时间线产物目录名
```gml
ntl_timeline_9_ntl_chapter_c3a8e36c4_c3a8e36c4 里 chapter 后面是下划线 → 猜不出来 →
```
退回 config.auto_chapter，而 auto_chapter 从不随产物更新 ⇒ 载入别的产物的 mod 脚本
（桥函数不在本产物 → [live] [错误] 未知函数: <slug>_hooks），auto_chapter=0 时则一个都不载入；
```gml
- 同一份清单也让 ntl_is_root() 不再依赖「路径里含不含 chapter」这种子串判据。
```

读不到（外部章节 exe / 旧部署产物）返回 ""，调用方自行兜底。

### `ntl_require_lua(moduleName)`

供 NTL Script 调用的 Lua 模块加载

返回 ds_map { ok: 0/1, value: 模块返回值 }

### `ntl_root_data()`

加载章节注册表到运行时状态

```gml
全局：ntl_ch (数组，元素为 ds_map: id/order/name/kind/source/author/dir/mods/enabled)
ntl_ch_pages / ntl_ch_page / ntl_ch_sel / ntl_ch_search / ntl_ch_filtered
```

### `ntl_root_draw()`

顶层章节选择界面（覆盖官方 UI）

### `ntl_root_filter()`

构建当前搜索条件下的"显示列表"

global.ntl_ch_display: 数组，元素 = 章节索引（>=0）或 -1（空槽/无内容）
组成：官方 1..7 槽位（含空槽灰显）+ 全部 mod 章节（按序号排序）

### `ntl_root_lang_cycle()`

章节选择器按 L：切到下一个可用语言、立刻生效并写回 config.json

### `ntl_root_launch(slot)`

启动当前页第 slot 个槽位对应的章节

### `ntl_root_page_items(page)`

该页 7 个槽位对应的章节索引（-1 = 空）

### `ntl_root_pages()`

总页数（每页 7 项，基于显示列表长度）

### `ntl_root_step()`

顶层章节界面的输入处理（仅在官方选择器存在时生效）

---- 外部引擎章节（Kristal）的常驻等待 --------------------------------------
★★★ 2026-09-30 重写（用户实测事故）：旧版写下请求后**直接** park —— 静音 + 把窗口标题
改成「外部章节运行中」+ 吞掉全部输入，最长 45 分钟。用户只用 Steam 启动时根本没有
--watch-external 守候进程，启动请求永远没人消费 ⇒ 用户只能杀进程。现在分三段：
① 确认门（≤120 帧 ≈2 秒）：请求文件被消费掉（守候进程读到就删，见 builder/Program.cs:1068）
或 Neutraled/external-running.txt 出现（守候进程拉起外部引擎时写，Program.cs:1120）
⇒ 才算「外面真的有人」；确认期间**不静音、不改标题、不吞输入**。
② 确认不了 ⇒ 绝不 park：清标志、恢复音量与标题、删掉没人要的请求文件、给可见提示
（ext.no_watcher），输入照常可用。
③ park 之后也有逃生口：按 Esc 立刻回到游戏；external-running.txt 一直没出现过时只等
600 帧（≈10 秒），出现过才用 45 分钟兜底。

### `ntl_run_mods()`

依次执行各 mod 入口脚本

### `ntl_screenshot(name)`

让游戏自己保存一张截图（自动化验证用）

文件落在游戏沙箱目录（game_save_id）；返回实际调用结果。

### `ntl_set_lang_string(key, text)`

注册文本覆盖并**立刻**写入 global.lang_map

旧行为：只写 global.ntl_lang_overrides，等 Step 里那次 ntl_apply_lang_overrides 才生效；
而那次只在第 2 帧跑一遍 —— 晚于它的 mod（热重载/late on_init）永远改不到游戏文字。

### `ntl_settings_row_draw(x, selx, y)`

在游戏自己的设置菜单里画「Mod 设置」这一行

★ 位置与样式完全照 mods/deltarune_60_fps 的「Mod Settings」：入口行插在原版第 6 行，
原版「Return to Title」「Back」由 builder 顺移一位（见 builder/Injector.cs 2.6 / 2.9）。
```gml
★ 选中红心不归我们画：官方红心公式 y = yy+160+(coord-滚动)*35 在 coord 5 时正好落在本行。
```
字体（m11431）：改用游戏自己的 mainbig（EmSize 24，部署期 FontMerge 已补 CJK 字形）——
与上下官方行同字号。旧版只要出现非 ASCII 就切 ntl_font_cjk（EmSize 12，只有一半大），
用户反馈「mod设置及其二级菜单字体太小了」指的就是这个。
★ 必须完整保存/恢复绘制状态：漏了颜色会把游戏的光标心形染成黄色（实测踩过）。

### `ntl_settings_row_press()`

设置菜单里接管第 6/7/8 行（coord 5/6/7）的确认键

由 builder 注入 obj_darkcontroller 的 Step（button1 分支内、原版 == 0 判断之前）：
```gml
if (ntl_settings_row_press()) { } if (global.submenucoord[30] == 0)
```
★ 为什么要有这个函数：入口行插在原版第 6 行（coord 5）——和 mods/deltarune_60_fps 的
「Mod Settings」一样，原版「Return to Title」「Back」被顺移到 coord 6 / 7。
与其在三处改原版比较值，不如把 5/6/7 三行的分派收在这里一处维护
（原版那两个判断已被改成 105/106，永不命中，见 builder/Injector.cs）。
coord 5 = 打开 Mod 设置面板；coord 6 = 原版 Return to Title；coord 7 = 原版 Back。
返回值只是「已接管」的记号；调用方后面照常走原版分支（原版分支里没有 5/6/7，不会再动手）。

### `ntl_skip_intro()`

按配置跳过章节内的开场演出（传说 / DELTARUNE 报幕）

全部由 config.json 开关控制，默认关闭（不影响正常体验）。

★ 2026-10-02 修复（用户报「跳过传说后音乐没有跟上」）
```gml
旧实现只做 room_goto(PLACE_MENU)，把官方跳过里的**音频收尾**整段丢了：
· obj_legend_Draw_0 末尾（官方跳过）：mus_volume(global.currentsong[1], 0, 15) 让传说 BGM 淡出，
19 帧后 snd_free(global.currentsong[0]) 释放音频流、global.flag[6] = 0，20 帧后 room_goto(137 = PLACE_LOGO)；
· PROCESS_LOGO_Draw_0 末尾（官方跳过）：snd_volume(NOISE, 0, 20) 让报幕音淡出，再 room_goto(139 = PLACE_MENU)。
```
旧实现两者都没做 ⇒ legend.ogg 一路播进存档界面（音乐与画面脱节），flag[6] 还残留 1。

★ 2026-10-02 用户追加要求：「直接播放存档界面音乐，跳过 legend.ogg」
⇒ 传说分支不再等 19/20 帧做官方淡出序列，而是**当场停掉 legend.ogg**（并复位 flag[6]），
屏幕黑场一完成就直接进 PLACE_MENU（存档界面）—— 那里由官方 DEVICE_MENU 起播 AUDIO_STORY.ogg。
（skip_logo 没开时，仍按官方路径去 PLACE_LOGO 看报幕。）
存档界面音乐兜底：DEVICE_MENU 的 snd_init 走 global.ntl_stream_cache；若缓存里的持有者已被
snd_free_all 销毁，旧版 Injector 缓存的是**死流句柄** ⇒ 音乐静音。这里跳过去之后等 ~1 秒确认
currentsong[1] 没在播，就按官方口径自己补起一条 AUDIO_STORY.ogg 流（只做一次）。
只用**核心函数 + asset_get_index 按名查找**，不依赖各章可能不存在的同名脚本（mus_volume / snd_free 等）。

### `scr_ntl_init`

============================================================

Neutraled 引导 —— builder 会把它 prepend 到 obj_initializer2 Create 最前
职责：防御性全局初始化 → 运行时状态 → 控制器创建 → mod 清单 → 入口
约束：老式脚本；每个函数都是独立同名脚本资源；禁用 struct/constructor
============================================================

---

## Lua 解释器（83）

### `ntl_lua_assign_target(env, node, value)`

给变量/字段/索引赋值

### `ntl_lua_binop(op, a, b)`

二元运算

### `ntl_lua_cache_stats()`

返回编译缓存的命中统计

### `ntl_lua_call(fn, args)`

调用函数（Lua 函数 / 宿主函数字符串）

### `ntl_lua_chunk_reg()`

取（必要时创建）load 产生的代码块注册表

### `ntl_lua_chunk_run(name, args)`

执行一个 load 出来的代码块

### `ntl_lua_coroutine(op, ...)`

coroutine 库


⚠️ 实现说明：
NTL 的 Lua 是**树遍历解释器**，没法真正保存/恢复执行位置，
所以这里提供的是**语义兼容**版本：

```gml
create(f)      → 返回一个协程对象（内部记住函数）
resume(co, ..) → **直接执行完函数**，返回 (true, 返回值...)
```
如果函数里调用了 yield，那些值会被收集起来
```gml
yield(v)       → 记录一个 yield 值，然后**继续执行**（不真的暂停）
status(co)     → "suspended" / "dead"
wrap(f)        → 返回一个函数，调用它等于 resume
running()      → 返回当前协程（若无则 nil）
isyieldable()  → 恒 false（因为我们不真暂停）
```

对"只是想用协程语法写顺序逻辑"的代码（如 Kristal 的过场动画）**完全够用**；
对"真的需要交错执行"的代码（如自制调度器）语义会有差异。

### `ntl_lua_coroutine_call(args)`

数组形式的 coroutine 调用包装

args[0] = 操作名（create/resume/...），args[1..] = 实际参数

### `ntl_lua_ctrl(kind, vals)`

控制流信号（return / break）

### `ntl_lua_debug(op, ...)`

debug 库（最小可用集）

### `ntl_lua_env_declare(env, name, value)`

声明局部变量

### `ntl_lua_env_find(env, name)`

一次走链完成"查找 + 取值"


优化历史：
1) 原来调用方要 env_has + env_get 走两遍作用域链
2) 现在这一层也不再用 ds_map_exists + ds_map_find_value（两遍哈希查找），
改成只调一次 ds_map_find_value：
- 键存在且值非 nil   → 返回 [1, value]
- 键不存在           → 返回 undefined → [0, undefined]
- 键存在但值是 nil   → 返回 undefined → 当"不存在"处理
Lua 语义里 nil 与"不存在"等价，所以第三步合并是安全的。

```gml
返回 [found(0/1), value]
```

### `ntl_lua_env_get(env, name)`

沿作用域链查找变量

### `ntl_lua_env_has(env, name)`

变量是否已定义

### `ntl_lua_env_new(parent)`

新建作用域（parent 为 undefined 时为全局作用域）

### `ntl_lua_env_pool_get(parent, key)`

从池里取一个可复用的子环境


★ 为什么需要：ntl_lua_ev_stat 的 if / do 分支原本每次执行都
```gml
ntl_lua_env_new()（= ds_map_create()），在循环体里会反复创建且不释放。
```
实测 fornum 的环境复用让累加基准从 2.251s 降到 1.894s。

池的键 = 父环境句柄 + 用途标记，保证：
- 同一父环境下的同种块复用同一个子环境
- 不同父环境（嵌套调用）互不干扰

### `ntl_lua_env_set(env, name, value)`

就近赋值

### `ntl_lua_ev_block(env, stmts)`

顺序执行语句块，遇控制流信号立即返回

★ 崩溃隔离：最外层包 try/catch，任何解释器内部异常都被拦住，不会崩溃游戏。
用 global.ntl_lua_depth 计数，只有最外层才建立 try/catch（避免递归时的性能开销）。

### `ntl_lua_ev_stat(env, node)`

单条语句求值

### `ntl_lua_eval_args(env, argNodes)`

求值参数列表（最后一个表达式若为多值则展开）

### `ntl_lua_ex(env, node)`

表达式求值

### `ntl_lua_fastpath(path)`

取得（并缓存）某个 Lua 文件的编译结果

性能优化：hook / 对象事件这类"每帧可能要跑"的脚本，
编译结果（AST）按文件路径 + 修改时间缓存，避免重复读盘与解析。

返回 ds_map { ok, ast, env_factory }；失败返回 undefined

### `ntl_lua_fn_host(name)`

宿主函数值（字符串形式）

### `ntl_lua_fn_new(node, env)`

由 function 节点创建函数值

### `ntl_lua_getmetatable(t)`

取元表（无则 undefined）


⚠️ 关键：ds_map_exists 对**无效句柄**会抛 "Data structure with index does not exist"，
不是返回 false。所以必须先用 ds_exists 确认句柄有效。
（os.clock 触发过这个问题：取 os 表的元表时句柄失效）

### `ntl_lua_host(name, args)`

Lua 标准库的宿主实现（由 ntl_call_host 分派 __lua_* 调用）

### `ntl_lua_index_get(t, k)`

带 __index 的取值

### `ntl_lua_index_set(t, k, v)`

带 __newindex 的赋值

### `ntl_lua_io(op, ...)`

io 库最小兼容

Kristal 偶尔用 io.write 做调试输出，这里映射到日志

### `ntl_lua_is_ctrl(v)`

是否控制流信号

### `ntl_lua_is_fn(v)`

是否 Lua 函数值

⚠️ 关键：ds_map_exists 对无效句柄会抛 Code Error（不是返回 false），
所以必须先查"Lua 对象登记表"确认它是真的 Lua 表，再试探函数标记键。

### `ntl_lua_is_kw(name)`

Lua 关键字判断

### `ntl_lua_is_stdlib(name)`

是否 Lua 标准库函数名

### `ntl_lua_is_table(v)`

精确判断是否为 Lua 表

GML 的 ds_map 句柄是实数，与普通数字无法区分；因此所有 Lua 表在创建时登记到
global.ntl_lua_tables，这里查登记表来判定（比试探键名可靠）

### `ntl_lua_key(k)`

把 Lua 表键规范成 ds_map 键字符串（数字键与字符串键不冲突）

### `ntl_lua_key_decode(dsKey)`

反向解码（用于 pairs 遍历）

### `ntl_lua_load(chunk, chunkname)`

编译一段 Lua 源码（load / loadstring）


★ 返回的是宿主函数名字符串（如 "__loaded_3"），不是手工构造的函数值：
函数值经 Lua 环境传递后在某些路径会失真（求值正确但传不回去），
返回字符串则由既有的「未定义函数名 -> 宿主」机制天然驱动。

### `ntl_lua_meta_binop(op, a, b)`

尝试用元方法处理二元运算（成功返回 ds_map{ok:1,value:...}）

### `ntl_lua_meta_unop(op, a)`

一元元方法（__len / __unm / __tostring）

### `ntl_lua_metamethod(t, name)`

取元方法（如 "__index"），无则 undefined

### `ntl_lua_p_accept(s, text)`

若匹配则消费并返回 1

### `ntl_lua_p_add(s)`

+ -

### `ntl_lua_p_args(s)`

函数调用参数：'(' explist ')' | table | string

### `ntl_lua_p_block(s)`

语句序列，直到块结束关键字

### `ntl_lua_p_chunk(s)`

解析整个 chunk，返回语句数组（错误写入 s.err）

### `ntl_lua_p_cmp(s)`

== ~= < > <= >=

### `ntl_lua_p_concat(s)`

.. 右结合

### `ntl_lua_p_err(s, msg)`

记录解析错误

### `ntl_lua_p_errmsg(s)`

取解析错误

### `ntl_lua_p_expect(s, text)`

期望指定关键字/运算符

### `ntl_lua_p_expr(s)`

表达式入口

### `ntl_lua_p_funcbody(s, node)`

( parlist ) block end

### `ntl_lua_p_is(s, text, offset=0)`

当前 token 是否为指定关键字/运算符

### `ntl_lua_p_mul(s)`

* / // %

### `ntl_lua_p_new(tokens)`

创建 Lua 解析器状态

### `ntl_lua_p_next(s)`

消费并返回当前 token

### `ntl_lua_p_node(type)`

新建 AST 节点

### `ntl_lua_p_node_at(s, type)`

新建带行号的 AST 节点

### `ntl_lua_p_or(s)`

最低优先级（左结合）

### `ntl_lua_p_peek(s, offset=0)`

看第 offset 个 token（不消费）

### `ntl_lua_p_pow(s)`

^ 右结合，优先级高于一元

### `ntl_lua_p_prefix(s)`

变量 / 括号 / 索引 / 字段 / 调用 / 方法调用

### `ntl_lua_p_simple(s)`

字面量 / 表 / 函数 / 前缀表达式

### `ntl_lua_p_stat(s)`

单条语句

### `ntl_lua_p_table(s)`

{ fieldlist }，字段：无键值 / Name=exp / [exp]=exp

### `ntl_lua_p_unary(s)`

not # - ~

### `ntl_lua_require(name)`

Lua 模块加载（require 的宿主实现）

查找顺序：<mod_dir>/lib/<name>.lua → <mod_dir>/lib/<name>/init.lua
→ <mod_dir>/<name>.lua → <mod_dir>/scripts/<name>.lua
→ <mod_dir>/libraries/**（递归）
→ <游戏根>/Kristal-main/<name>.lua（Kristal 引擎源码）
结果缓存到 global.ntl_lua_modules（同一模块只执行一次）

### `ntl_lua_rt_err(msg)`

记录运行时错误，并带上"当前执行上下文"

需要 global.ntl_ctx_module / ntl_ctx_event 来定位（由 run/require/emit 设置）

### `ntl_lua_setmetatable(t, mt)`

设置元表（mt 可为 undefined 清除）

### `ntl_lua_stdlib()`

注册标准库到全局环境（Lua ↔ GML 兼容层的核心）

### `ntl_lua_stdlib2()`

元表 / 模块 / 常用补充库注册

### `ntl_lua_table_count(t)`

有效键数量（不含内部键）

### `ntl_lua_table_get(t, k)`

取值（不存在返回 undefined）

### `ntl_lua_table_keys(t)`

返回表的所有键（GML 数组，跳过内部键）


★ 关键：ntl_lua_key 会给键加类型前缀（字符串 "s"、数字 "n"、布尔 "b"），
这里必须**还原成 Lua 层能直接用的形式**，否则拿回去 table_get 会被再加一次前缀。

### `ntl_lua_table_len(t)`

# 运算

### `ntl_lua_table_new()`

新建 Lua 表（用 ds_map 承载，含数组长度缓存）

### `ntl_lua_table_register(handle)`

把新建的 Lua 表登记到全局表注册表

### `ntl_lua_table_set(t, k, v)`

设值，并维护数组边界

### `ntl_lua_tok_push(list, type, value, line)`

追加一个 token

### `ntl_lua_tonumber(v)`

数字转换（失败返回 undefined）

### `ntl_lua_tostring(v)`

Lua 风格字符串化

注意：GML 里 1 == true 为真，必须先判断表/函数，再判断 real

### `ntl_lua_truthy(v)`

Lua 真值判断（只有 nil 与 false 为假）

### `ntl_lua_unop(op, a)`

一元运算

---

## 控制台（44）

### `ntl_console_action(cmd, rest)`

操作类命令实现

### `ntl_console_alias(aliasName, expansion)`

定义命令别名

alias ff "goto 4; sleep 60; screenshot"
之后输入 ff 就等同执行那一串

### `ntl_console_api(keyword)`

在 API 注册表里搜索函数/对象/资源

数据源：Neutraled/api-registry.json（部署时生成，含游戏原有的 711 函数 / 1577 对象 / 5888 精灵 等）

### `ntl_console_auto(cmd, rest)`

自动化类命令实现

### `ntl_console_autorun(path)`

执行启动脚本（每行一条控制台命令，# 开头为注释）

默认路径：Neutraled/autorun.console（存档区）——给 mod 开发者做可复现的测试场景用

### `ntl_console_bind(keyName, command)`

把命令绑定到按键（在控制台打开时生效）

bind F5 "profile"
bind F6 "reload"

### `ntl_console_binds_check()`

每帧检查按键绑定（控制台打开时）

### `ntl_console_capture(on)`

控制台输入屏蔽（改用游戏自身的 kbdBlocked 机制）


关键发现：DELTARUNE 自己就有输入屏蔽开关：
```gml
function sunkus_kb_block() { global.kbdBlocked = true; }
function sunkus_kb_check(arg0) { return global.kbdBlocked ? false : keyboard_check(arg0); }
```
所以只需在 BeginStep 里设 global.kbdBlocked = true，整个游戏的输入就被拦住了。
本函数保留作为兼容入口（不再做键盘重映射）。

### `ntl_console_cmds(catFilter)`

按分类列出所有命令

### `ntl_console_cmds_builtin()`

注册所有内置命令（ntl 的，无前缀）


分类：info（信息）/ state（状态）/ action（操作）/ debug（调试）/ auto（自动化）

★ 范围解析：内置命令直接用名字，mod 命令必须带 "modid:" 前缀

### `ntl_console_debug(cmd, rest)`

调试类命令实现

### `ntl_console_draw()`

GUI 层绘制控制台面板（支持滚动、滚动条、状态行）

### `ntl_console_dry(rest)`

dry [on|off]：破坏性命令的干跑（演练）开关

★ 反人类修复（用户建议）：goto / loadmap / warp / spawn / destroy / setvar / setflag
以前一敲就生效（destroy 能一次干掉几十个实例），想先看看"会发生什么"完全没办法。
打开干跑后，这些命令照样做参数校验（房间不存在/对象不存在照样报错），
但只打印「将要执行 …」，一个字节的游戏状态都不改。

### `ntl_console_dry_block(cmd, detail)`

破坏性命令的统一闸门

调用方式（放在"真正改状态"那一步之前）：
```gml
if (ntl_console_dry_block("warp", "跳到房间 " + _rn)) return 0;
```
返回：1 = 当前是干跑状态（已打印预览，调用者必须直接返回、不要执行）
0 = 正常状态（调用者继续执行）
注意：参数校验要放在本调用之前 —— 干跑时也要能查出"房间不存在"这类错误。

### `ntl_console_eval(expr)`

用 Lua 求值任意表达式（开发调试利器）

```gml
例：eval 1+2 / eval Kristal.getFlag("wr_set") / eval ntl_sprite_resolve(5)
```

### `ntl_console_exec(cmdline)`

解析并执行一条控制台命令


★ 范围解析（用户要求）：
1) 别名（alias 定义的）优先
2) 内置命令（ntl 的）—— 不需要前缀
3) mod 命令 —— 必须带 "modid:cmd" 前缀，各自在命名空间里
4) 都不是 → 打"未识别的命令"并提示用 eval <表达式>（★ 不自动求值；求值入口只有 eval）

### `ntl_console_exec_lua(src)`

执行一段 Lua 代码（可多条语句；与 eval 的区别：这里不求值表达式）

```gml
例：exec ntl_console_log("hi")   /   exec local a = 1  （eval 则是 eval 1+2）
```

### `ntl_console_filter(mode)`

日志过滤（只看某类输出）

mode: "all" / "error" / "warn" / "cmd"

### `ntl_console_help(query)`

显示命令列表 / 查询某命令用法

### `ntl_console_help_lookup(name)`

查询某个名字：是游戏资源？mod 函数？内置 API？

### `ntl_console_history_load()`

从文件恢复命令历史

### `ntl_console_history_save()`

把命令历史存到文件（下次启动可恢复）

★ 返回值改为**写入的完整路径**（没有历史时返回 ""）。
旧版返回 0/1、调用方硬编码一句相对路径提示，用户根本不知道文件在哪。

### `ntl_console_hp_fill()`

把 HP 拉满（god 每帧调用；找不到就静默）

★ 2026-09-28 实测事故修复（god 崩机）：本函数由 ntl_console_power_tick 在 Step 事件里每帧调用，
那里 try/catch 兜不住 ⇒ 任何抛错都会让游戏弹 Code Error 直接死。现在整体包 try/catch，
最坏情况只是"这次没拉满"，绝不带走游戏；非数值的 HP/MAXHP 变量一律忽略。

### `ntl_console_hp_find()`

best-effort 找玩家 HP 变量（hp / god 命令用）

```gml
返回 ds_map{ kind(global|inst), obj, name, maxname, where } 或 -1（找不到）
```

### `ntl_console_hp_get()`

读当前 HP（找不到返回 -1）

### `ntl_console_hp_set(v)`

写 HP（成功返回 1）

### `ntl_console_info(cmd, rest)`

信息类命令实现（mods/hooks/modinfo/timing）

### `ntl_console_key_vk(keyName)`

键名 → 虚拟键码

### `ntl_console_line_pass(line)`

该行是否通过当前过滤器

### `ntl_console_log(text [, force])`

输出到控制台面板（按换行拆分，多行不重叠）

可选 force=1：无视当前日志过滤器强制输出（filter 命令自己的回显用它）

### `ntl_console_mod_dir(id, name)`

反查某个已加载 mod 在 Neutraled/mods/ 下的目录名（找不到返回 ""）

```gml
复用 ntl_modmenu_modinfo()（面板用的同一套匹配规则：大小写/空格/下划线/点不敏感 + Id 分段）
```

### `ntl_console_power(cmd, rest)`

强力指令（玩家向 + 开发者向）分派

返回 1 = 已处理（调用方直接 return）

玩家向：speed 游戏速度 / warp 直跳房间 / hp 血量 / god 无敌
开发者向：pause resume step 单帧 / dump 房间快照 / watch 变量跟踪 / crash 崩溃隔离自测 / autorun 脚本

### `ntl_console_power_tick()`

强力指令的每帧驱动（god / 单帧推进 / watch）

### `ntl_console_profile()`

显示运行时性能统计（找出谁拖慢了游戏）

### `ntl_console_register(name, desc, fn, usage, cat, handler)`

注册控制台命令


参数：
name    : 命令名。内置命令直接用名字（help / mods / eval）；
mod 注册时自动加前缀 "modid:"（由 ntl_console_register_mod 处理）
desc    : 一句话说明
fn      : 脚本资源索引（-1 = 由 ntl_console_exec 内部分派）
usage   : 参数提示（可选），如 "goto <章节号>"
cat     : 分类（可选）：info / state / action / debug / auto / mod
handler : "builtin"（默认）或 "lua:<路径>"（mod 提供的脚本）

注册表结构：global.ntl_console_cmds[name] = ds_map{ desc, fn, usage, cat, scope, mod, handler }

### `ntl_console_register_mod(name, desc, handler, usage)`

mod 注册控制台命令


★ 范围解析规则（用户要求）：
- **ntl 内置命令不需要前缀**：直接 help / mods / eval
- **mod 命令必须带命名空间前缀**：my.mod:heal
- 执行时：**先查内置**，找不到再按 "modid:name" 查 mod 命令
- 同名冲突：不同 mod 的命令互不影响（各自在自己的命名空间里）

用法（mod 的 main.lua）：
```gml
ntl_console_register_mod("heal", "恢复满血", "console/heal.lua", "heal [目标]")
```

之后玩家可以在控制台输入：
> my.mod:heal            （带前缀）
> help my.mod:heal       （查用法）

### `ntl_console_run(path)`

执行一个命令脚本文件


★ 自动化（用户要求）：把常用操作写成脚本，一条命令批量执行。

脚本格式（.ntlcmd / .txt）：
# 以 # 或 // 开头的是注释
log 开始自动化
goto 4
sleep 60
screenshot after_load
```gml
eval Kristal.getFlag("wr_set")
```

路径解析顺序：
1) 绝对路径 / 相对游戏目录
2) Neutraled/scripts/<名字>
3) 当前 mod 目录下的 scripts/<名字>

### `ntl_console_run_mod_cmd(rec, fullName, rest)`

执行一个 mod 注册的控制台命令

### `ntl_console_save(name)`

把控制台全部输出保存到文件

便于把日志发给别人排查

### `ntl_console_similar(a, b)`

命令名相似度打分（给"你是不是想输入 xxx"用；越大越像，0 = 不像）

### `ntl_console_state(cmd, rest)`

状态类命令实现

### `ntl_console_style(key, value)`

控制台外观设置

key: lines（显示行数 5-30）/ alpha（透明度 0.3-1.0）/ font（字号缩放，暂不支持）

### `ntl_console_timed_exec(cmdline)`

执行命令并显示耗时

用于调试"为什么这条命令这么慢"

### `ntl_console_vars(prefix)`

列出全局变量的当前值（便于调试）

---

## mod 互操作（21）

### `ntl_asset_export(kind, name, value)`

把一个**游戏资源**导出给其他 mod 使用


kind: "sprite" / "sound" / "object" / "map" / "path" / "data"

用法（mod A）：
```gml
local spr = ntl_sprite_from_file(__mod_dir .. "/kris_custom.png")
ntl_asset_export("sprite", "kris_custom", spr)
```

其他 mod 就能：
```gml
local spr = ntl_asset_import("a.mod", "kris_custom")
draw_sprite(spr, 0, x, y)
```

### `ntl_asset_import(modId, name, kind)`

取得其他 mod 导出的资源


用法（mod B）：
```gml
local spr = ntl_asset_import("a.mod", "kris_custom")              -- 自动找
local spr = ntl_asset_import("a.mod", "kris_custom", "sprite")    -- 指定类型
```

### `ntl_asset_list()`

列出所有可用的共享资源（Lua 表）

### `ntl_mod_data_get(key)`

读 mod 间共享的持久数据

### `ntl_mod_data_load()`

从磁盘恢复 mod 共享数据

### `ntl_mod_data_save()`

把 mod 共享数据存盘

### `ntl_mod_data_set(key, value)`

mod 间共享**持久数据**（会存盘）


和 ntl_shared_set 的区别：
shared_set → 内存里的共享变量（重开游戏就没了）
data_set   → 存到磁盘（Neutraled/mods-shared.json），重开还在

用途：mod 之间共享进度、解锁状态、配置

### `ntl_mod_emit(eventName, data)`

跨 mod 事件广播


用法：
```gml
发送方：  ntl_mod_emit("boss_defeated", { name = "snowlver" })
接收方：  ntl_mod_on("boss_defeated", function(d) print(d.name) end)
```

事件名建议加 mod 前缀避免碰撞："mymod:boss_defeated"

### `ntl_mod_export(name, value)`

把函数/常量导出给其他 mod 使用


用法（在 mod 的 main.lua 里）：
```gml
function my_api(a, b) return a + b end
ntl_mod_export("add", my_api)
ntl_mod_export("VERSION", "1.2.0")
```

其他 mod 就能：
```gml
local them = ntl_mod_require("my.mod")
them.add(1, 2)      -- 3
```

### `ntl_mod_ids()`

只返回 mod id 的数组（Lua 表形式，便于 # 和 ipairs）

### `ntl_mod_import(modId, only)`

把一个 mod 的导出表**导入到当前环境**


和 ntl_mod_require 的区别：
```gml
require → 返回表：  local a = ntl_mod_require("x"); a.fn()
import  → 直接注入：ntl_mod_import("x"); fn()
```

用法（mod 的 main.lua）：
```gml
local n = ntl_mod_import("frostveil.core")
```
-- 或只导入指定名字
```gml
local n = ntl_mod_import("frostveil.core", { "heal", "damage" })
```

### `ntl_mod_list()`

列出所有已注册的 mod（互操作视角）

★ 返回 Lua 表（数组形式），Lua 里可以用 #mods 和 ipairs 遍历

### `ntl_mod_load(mod, slot)`

从独立存档槽读回

### `ntl_mod_on(eventName, fn)`

订阅跨 mod 事件

### `ntl_mod_register(id, name, version, entry_script_name)`

由 builder 内联清单调用

### `ntl_mod_registry_init()`

mod 互操作注册表初始化


目标（用户要求）：**mod 可以对自己不冲突的任何 mod 完全支配 / 任意联动**

三类能力：
```gml
1) 跨 mod 调用      ntlm("other.mod"):func(args)      —— 直接调别人的函数
2) 跨 mod 事件      ntl.mod_emit("event", data)       —— 广播/监听
3) 跨 mod 共享状态  ntl.shared_set/get(key, value)    —— 共享数据总线
```

安全性：所有跨 mod 调用都记录来源，出问题时能定位到"谁调了谁"。

### `ntl_mod_require(modId)`

取得另一个 mod 的导出表（用于跨 mod 调用）


用法（Lua）：
```gml
local other = ntl_mod_require("frostveil.core")
other.do_something(1, 2)
```

```gml
返回一个 Lua 表：包含该 mod 通过 ntl_mod_export() 注册的函数与常量。
```
若 mod 不存在或未启用 → 返回 nil（调用方应判空）

### `ntl_mod_save(mod, slot)`

mod 独立存档槽

### `ntl_mod_scripts_load()`

加载 mods/ 下所有 mod 自己的 Lua 脚本

★ 这一步是 **mod 互操作** 的前提：没有它，mod 的 main.lua 根本不会跑。

修复历史：
```gml
1) 章节检测原来用 while 数数字，结果得到 "chapter4_"(多下划线) → 改为直接用配置的章节号
```
2) 目录枚举原来只靠 file_find_first → 改为多级回退
3) 叶子目录原来按 [_chap, "root"] 加载（把 root 当通配符）→ 章节进程会加载 root 作用域
的 mod 脚本，而 root 叶子的 live 桥只编译进 root 产物 → `[错误] 未知函数: <slug>_hooks`。
现改为只加载当前目标 [_chap]：root 是目标名，不是「对所有章节生效」（见下面 :66 的注释）。
4) 作用域来源以前只靠路径猜（时间线产物猜不出 → 退回 config.auto_chapter，而它从不随产物
更新）→ 现在优先读部署时写入本产物的 Neutraled/scope.json（ntl_product_scope），
路径与 config 只作兜底。

### `ntl_shared_get(key)`

读取跨 mod 共享状态

### `ntl_shared_set(key, value)`

跨 mod 共享状态总线（任意 mod 可读写）

用途：mod A 设置一个状态，mod B 读取并做出反应（无需互相依赖代码）

---

## 国际化（17）

### `ntl_i18n_init()`

初始化中英双语（**只覆盖 Neutraled 新增的内容**，游戏原版文字不动）


语言来源（优先级）：
1) config.json 的 "lang": "zh" | "en" | "auto"
2) auto → 跟随游戏语言（global.lang）
3) 缺省 → zh

### `ntl_i18n_out(zh_map, en_map)`

运行时输出文案表（第二批）

```gml
背景: ntl_i18n_init() 里的第一批 168 个键只覆盖控制台外壳/命令说明/分类/HUD；
```
而各命令的【输出行】、章节选择器 HUD、Lua 错误解释等都是硬编码中文 —— lang=en 时露中文。
```gml
本文件把这些文案集中成键，由 ntl_i18n_init() 在建完 _zh/_en 之后调用一次注册。
占位符: {1}{2}{3}... 用 ntl_ts(key, [值, 值...]) 替换（见 ntl_ts.gml）。
```
★ 中文文案必须与改前的硬编码中文逐字一致（三套件/bootlog 依赖这些中文串做断言）。

### `ntl_lang_list()`

可用语言码列表：内置 zh/en + Neutraled/lang/lang_<code>.json 外部包

顺序：zh 在最前（界面中文原文），外部包按字母序，en 在最后。章节选择器按 L 用这个顺序循环。

### `ntl_lang_name(code)`

语言的母语名字（面板语言列表用；认不出来就回大写代码）

### `ntl_lang_pack(code)`

加载**外部语言包**（Neutraled/lang/lang_<code>.json），支持任意语言。


文件格式（与 builder 侧 LangPacks 共用同一份规格）：
{ "code": "ja", "name": "日本語", "map": { "中文原文": "译文", ... } }

语义：
```gml
1) 语言包的 key 是**中文原文**（builder 的 L() 参数、GML 表里中文那一侧的值）；
```
2) 本函数把「符号键 → 中文」的 zh 表和语言包的「中文 → 译文」map 合成一张新表（符号键 → 译文），
注册进 global.ntl_i18n[code] —— 于是 ntl_t / ntl_ts / ntl_tf 一行都不用改就能说任意语言；
3) 查不到的条目**回退中文**（宁可显示中文，也不能留空白）；
4) zh / en 不走这里（它们是硬编码表，更准）。

返回值：1 = 已加载并注册（幂等：同语言重复调用直接返回 1）；0 = 缺文件 / 解析失败（调用方保持原语言）。

### `ntl_lang_set(lang)`

切换语言（zh / en / auto）

★ F5 接线：同时提供文本覆盖子命令（ntl_set_lang_string / ntl_get_lang_string 的可观测入口）
lang set <key> <文本>   写一条覆盖并立刻生效
lang get <key>          读一条文本（覆盖优先，其次 lang_map）

### `ntl_t(key)`

取当前语言的文案（只用于 Neutraled 新增的内容）

找不到时返回 key 本身（便于发现缺翻译）

### `ntl_text_fit(text, maxw)`

单行宽度适配：宽度超过 maxw 就按字符截断并在尾巴补 "…"

为什么需要：面板的标签列/数值列都是固定列宽，而 draw_text 没有宽度限制 ——
长名字会直接压到隔壁列上（用户 m17504 的截图：章节视图里 "Chapter 1 The Beginning" 与「官方」叠成一团，
模组视图里 "deltarune_but_it_s__percentage___color" 顶穿面板右边框）。
```gml
必须在 draw_set_font(要用的字体) 之后调用。maxw <= 0 = 不做限制，原样返回。
```

### `ntl_text_fit_lines(text, w, maxlines)`

说明区多行适配：整段折行高度超过 maxlines 行就砍尾巴补 "…"

```gml
为什么需要：说明区是 draw_text_ext(x, y, txt, 18, 470) —— 会自动折行但**没有行数上限**，
```
长说明（例如带 mod 全名的回显）会把文字画到菜单框外面去。
这里按"折行后的真实高度"收敛，保证永远装得进说明区。
```gml
必须在 draw_set_font(说明行字体) 之后调用。maxlines < 1 按 1 行处理；w <= 0 不做限制。
```

### `ntl_tf(key, v1, v2)`

取文案并替换 {n} / {1} / {2} 占位符

### `ntl_theme_c(name)`

取主题颜色（GameMaker BGR 整数）；没有主题文件时返回内置默认值。

默认值 = 改造前 draw_set_color 里写死的那些常量 ⇒ 未装主题时像素级不变。
首次调用会懒加载 Neutraled/console-theme.json（省掉在 scr_ntl_init 里插一行）。

### `ntl_theme_load()`

读取 Neutraled/console-theme.json（由 ntl-builder --theme-use 写出）


文件格式（与 builder 侧 Themes.cs 一致）：
{ "schema":1, "id":"dark", "name":"深色（默认）",
"colors":    { "bg":"#101014", ... 11 键 ... },
```gml
"colors_bgr":{ "bg":1315856, ... 同键 → (b<<16)|(g<<8)|r ... } }
```

为什么用 colors_bgr：GameMaker 的颜色是 BGR 整数，游戏侧直接取整数即可，
不必在 GML 里解析十六进制（也就没有解析失败的可能）。

缺失/损坏一律**保持内置默认**（等于改造前的配色），所以没装主题的机器像素级不变。
返回值：1 = 读到主题文件；0 = 没有/不可用（默认配色）。

### `ntl_tok`

老式脚本（每个函数一个同名脚本资源）

### `ntl_tok_is_alnum`

老式脚本（每个函数一个同名脚本资源）

### `ntl_tok_is_alpha`

老式脚本（每个函数一个同名脚本资源）

### `ntl_tok_is_digit`

老式脚本（每个函数一个同名脚本资源）

### `ntl_ts(key, values)`

取文案并把 {1}{2}{3}... 替换成 values 里的元素（下标 0 对应 {1}）

为什么不用 ntl_tf：ntl_tf 的参数顺序是 {n}{1}{2}{3}（最多 4 个）且容易错位；
本函数按数组顺序替换、个数不限，控制台输出改写成"单条模板"时用它。
```gml
用法: ntl_console_log(ntl_ts("speed.cur", [string(_cur), string(_orig)]));
```

---

## Hook 系统（13）

### `ntl_bh_invoke(rec, args, origValue)`

调用一个内置函数 hook

返回 [handled, value]

### `ntl_bh_run(funcName, mode, args, origValue)`

内置函数 Hook 的运行时分派

```gml
返回数组 [handled(0/1), value]
```
handled=1 → 包装函数直接返回 value，不再调用原函数

### `ntl_hook_count(event_name)`

某事件的订阅者数量

### `ntl_hook_init()`

初始化 hook 系统（从 hook-registry.json 读取）


```gml
★ 缺陷 1 修复（1/2）：以前这里是**无条件** global.ntl_hooks = ds_map_create()。
```
而 global.ntl_hooks 是"函数 hook + 事件订阅"共用表：mod 入口脚本在
```gml
scr_ntl_init.gml:91 ntl_run_mods() 里已经用 ntl_hook() 订阅了事件，
```
本函数在 scr_ntl_init.gml:104 才跑 —— 于是 mod 的订阅被整体抹掉
```gml
（真机现象：ntl_hook() 返回 0 却一次不跑 + 日志「已注册 0 个 hook（覆盖 0 个脚本）」）。
```
现在改为幂等初始化；注册表自己的槽位在下面按 key 精确重置。

幂等契约：重复调用只重置「上一次注册表写入过的 key」（global.ntl_hook_reg_keys），
mod 自己订阅的事件槽不在其中 —— 入口脚本里可以直接订阅，不必等本函数跑完。

### `ntl_hook_list(script)`

取某脚本的 hook 列表（无则 undefined）

### `ntl_hook_load(handler, modDir, source)`

加载 hook 脚本（按需缓存源码）

返回可用于执行的源码字符串；已缓存则直接返回

### `ntl_hook_plan(scriptName)`

生成某个函数的 hook 执行计划


★ 自动解决冲突：多个 mod hook 同一函数时，不再"谁覆盖谁"，
而是**按声明顺序链式执行**（pre → post），互不干扰。

返回：数组，每项是 ds_map { mod, mode, handler, order }

mode 语义：
pre      —— 原函数之前执行（可改参数）
post     —— 原函数之后执行（可改返回值）
override —— 独占（只有它能跑；有 override 时其余 pre/post 仍会执行但原函数不跑）

### `ntl_hook_run(script, mode, args)`

运行某脚本在指定模式的 hook

返回 ds_map { handled: 0/1, value: 结果 }

### `ntl_hook_run_lua(src, name, args, script, mode)`

执行 Lua hook

```gml
hook 脚本约定：可定义 function hook(args, script, mode) 或直接写顶层代码
```

### `ntl_hook_run_ntl(src, name, args, script, mode)`

执行 NTL Script hook

### `ntl_hook_summary()`

输出 hook 冲突摘要（哪些函数被多个 mod 改了）

### `ntl_oev_init()`

对象事件 Hook 的注册表初始化

数据源：Neutraled/object-hooks.json（部署时由 builder 生成，含被包装的对象事件代码块名）

### `ntl_oev_run(objName, eventName, self, mode)`

对象事件 Hook 的运行时分派

返回 [handled, value]：
handled=1 → 包装直接返回 value，跳过原事件代码（仅 override 模式）
handled=0 → 继续执行原事件代码

---

## live 运行时（11）

### `ntl_live_compile(path, src, cache)`

编译脚本为 AST 并缓存

```gml
cache: ds_map(path -> AST)。命中缓存时直接返回，避免每帧重新解析。
```

### `ntl_live_emit`

老式脚本（每个函数一个同名脚本资源）

### `ntl_live_file_read`

老式脚本（每个函数一个同名脚本资源）

### `ntl_live_init`

初始化 live 运行时系统  [BUILD-2026-09-21-0700]

1) 从 live/index.json 加载 live mod
2) 从 mods/ 加载 mod 自己的 Lua 脚本（mod 互操作的前提）
3) 执行 on_init

### `ntl_live_live_dir`

老式脚本（每个函数一个同名脚本资源）

### `ntl_live_reload`

老式脚本（每个函数一个同名脚本资源）

### `ntl_live_run_ast(ast, env, label)`

执行已编译的 AST

### `ntl_live_run_lua(astNode, env, label)`

执行 Lua AST（由 emit 按文件扩展名调用）

### `ntl_live_run_source(src, env, label)`

编译并执行一段脚本（一次性，不缓存）

### `ntl_live_sort_by_deps()`

按 mod 之间的依赖关系重排加载顺序


目的：**mod 互操作**要求被依赖的 mod 先初始化。
例如 InteropB 依赖 InteropA，则 A 的 on_init 必须先跑（A 先导出 API）。

算法：简单拓扑排序（dependencies 里声明的 id 必须先于自己）
- 读取每个 mod 目录下的 mod.json 的 dependencies
- 未在依赖图中的 mod 保持原相对顺序（稳定）
- 检测到环时保持原顺序并记日志（不阻塞）

### `ntl_live_split_args(s)`

按空格分割参数（支持 "引号包裹" 的参数）

---

## 地图（10）

### `ntl_map_collide(mapMap, x, y, w, h)`

与地图碰撞矩形做 AABB 检测

返回 1 = 碰撞，0 = 可行走

### `ntl_map_draw(mapMap, camX, camY)`

绘制地图（整图，按相机偏移）

地图数据由 ntl_map_load 返回

### `ntl_map_draw_chunked(map, camX, camY, viewW, viewH)`

分块绘制（只加载视野内的块）

### `ntl_map_load(id)`

加载 Kristal 转换后的地图（整图 PNG + 数据 JSON）

数据位置：Neutraled/kristal-maps/<id>.png 与 <id>.map.json
返回 ds_map：{ ok, id, sprite, width, height, tileW, tileH, collision[], groups[], props }

### `ntl_map_load_chunked(id)`

分块加载大地图（避免一次性把整图塞进显存）


原理：把地图按 1024x1024 切成若干块，只加载当前视野附近的块。
对于 2400x1120 这类地图收益不大，但对 8000x8000 的超大地图是必需的。

返回 ds_map（与 ntl_map_load 兼容，多一个 "chunks" 字段）

### `ntl_map_object_named(map, objectName)`

在所有对象组里按名字查找对象

返回 ds_map（含 name/type/x/y/w/h/point/prop:*），找不到返回 undefined

### `ntl_map_objects(map, groupName)`

取某对象组的对象列表（ds_map 数组）

组名如 "markers" / "objects" / "collision"

### `ntl_map_objects_near(map, x, y, radius)`

找出玩家附近的可交互对象

返回数组：[{obj, dist, name, type, x, y, props}]

### `ntl_map_test()`

最小闭环测试：加载一张 Kristal 地图并显示状态

### `ntl_map_verify_all()`

批量验证所有转换后的地图

逐张加载并检查：尺寸/碰撞块数/对象组数，输出到日志

---

## Kristal 兼容（9）

### `ntl_kristal_battle_host(name, args)`

战斗/对话桥接的宿主实现

### `ntl_kristal_battle_init()`

Kristal 战斗/对话系统桥接


Kristal 的战斗 API：
Game.battle                     当前战斗
```gml
Game.battle:getEnemy(name)      取敌人
```
Game.battle.enemies             敌人列表
```gml
Game.battle:addWave(wave)       加一波
Game.battle:setState(state)
Game:startEncounter(encounter)  开始遭遇战
Game.world:startCutscene(fn)    开始过场
```

对话 API：
```gml
Text(text, x, y)                创建文本对象
Game.world:say(...)             显示对话
```
Textbox                         文本框

### `ntl_kristal_class_host(name, args)`

类系统宿主实现（__kr_Class / __kr_new_instance 等）

### `ntl_kristal_class_init()`

Kristal 类系统与基类（Object / Sprite / Text / EnemyBattler 等）

```gml
用 Neutraled 的 Lua 元表实现 Kristal 的 Class(include, id) 约定：
local MyClass, super = Class(Object)
function MyClass:init() super.init(self) ... end
```

### `ntl_kristal_frame()`

每帧调用 Kristal mod 的回调（postUpdate / update）

由 Step_1 的每帧流程调用，让 mod 的逐帧逻辑真正运行

### `ntl_kristal_host(name, args)`

Kristal 兼容层的宿主实现（__kr_* 分派）

### `ntl_kristal_init()`

Kristal 引擎兼容层（内含 world / battle 桥接）

注册 Kristal 运行时的关键全局对象（Kristal / Game / Registry / Assets / Mod / Music / Input / Camera / Draw）
目标：让 Kristal mod 的 Lua 脚本在 Neutraled 里能加载并执行大部分逻辑

### `ntl_kristal_world_host(name, args)`

Game.world 方法的宿主实现

### `ntl_kristal_world_init()`

把 Kristal 的 Game.world 映射到 Neutraled 的对象管理器


Kristal 的关卡系统核心 API：
```gml
Game.world:addChild(obj)          加入场景
Game.world:removeChild(obj)       移除
Game.world:getCharacter(name)     取得队伍角色
```
Game.world.characters             角色列表
```gml
Game.world:hasCharacter(name)
Game.world:getSolid(x, y)         碰撞查询
Game.world:detect(x, y, w, h)     区域检测
```

我们把它接到：
- ntl_obj_*（对象实例管理器）
- 地图碰撞系统（ntl_map_collide）

---

## 工具函数（7）

### `ntl_dir_list(path)`

列出一个目录下的子目录名

注意：GM 的 file_find_first 对绝对路径支持有限，这里做双重尝试

### `ntl_ensure_dir(path)`

确保路径所在目录存在（GM 不会自动建目录）

传入文件路径或目录路径都可以：只对"看起来像文件"的去掉最后一段

### `ntl_file_list(path, pattern)`

列出目录下的文件

### `ntl_json_diag()`

一次性诊断：本产物 Neutraled/mods.json 的读取/解析链路（排 "已加载 0"）。

真根因（2026-10-02 已定，见 builder/Program.cs:2432 注释）：C# 默认编码器把中文 mod 名写成 \uXXXX，
而 GameMaker 的 json_parse 吃不下 \uXXXX ⇒ 整个清单解析失败 ⇒ 面板恒「已加载 0」。
这条日志仍保留，用于**下次真机一键定性**：打印 转义个数 / 解析计数 / 原文头部 + 合成对照组
（对照组含一例 \uXXXX 转义样本，可直接看到 json_parse 是否吃得下）。1.0.1 发布前可删。

### `ntl_json_esc(s)`

JSON 字符串转义（GM 的 json_encode 只接受 real，不能转字符串）

### `ntl_string_join_ext(sep, arr)`

数组连接成字符串

### `ntl_string_replace_all(s, find, repl)`

全局替换（GM 没有内置的 string_replace_all）

---

## 性能/资源（7）

### `ntl_perf_noop()`

空函数（性能测试用）

### `ntl_perf_record(modName, ms)`

记录一次 mod 的执行耗时

用于 profile 命令的"逐 mod 耗时排行"

### `ntl_perf_test()`

性能基准：对比 GML 原生与 Lua 解释器的开销

结果写入日志（[perf] 前缀）供外部对比

### `ntl_perf_time()`

性能测试用的时间源（毫秒）

### `ntl_res_report()`

资源使用报告（泄漏检测）

★ F5 接线：报告是用户主动敲 res 触发的 → 全部 force=1，不受 filter 过滤器影响

### `ntl_res_track(kind, id, tag)`

登记动态创建的资源（用于泄漏检测）

kind: "sprite" / "surface" / "sound" / "buffer" / "ds"

### `ntl_res_untrack(kind, id)`

注销资源（释放时调用）

---

## 实例操作（6）

### `ntl_inst_all_objs()`

列出当前房间的所有对象名（mod 可以遍历并操作任意对象）

### `ntl_inst_create(objName, x, y, depth)`

运行时创建任意对象的实例

mod 用它可以在游戏里"凭空生成"东西（NPC、道具、特效…）

### `ntl_inst_destroy(inst)`

销毁实例（mod 可以移除游戏里的任何东西）

### `ntl_inst_find(objName)`

按名字找对象的所有实例（返回数组）

### `ntl_inst_get(inst, varName)`

读取任意实例的任意变量

### `ntl_inst_set(inst, varName, value)`

设置任意实例的任意变量（完全支配）

---

## 对象（6）

### `ntl_obj_count(objName)`

统计某对象名的实例数量

### `ntl_obj_count_now(objName)`

统计某对象名当前"活跃"的实例数

与 ntl_obj_count 的区别：跳过已标记销毁的实例

### `ntl_obj_draw(camX, camY)`

绘制所有 Kristal 对象实例

用 GM 基本图形（颜色由 Lua 侧 color 字段决定，缺省蓝色）

### `ntl_obj_init()`

初始化 Kristal 对象实例管理器

### `ntl_obj_spawn(classPath, x, y, opts)`

实例化一个 Kristal 对象

classPath: Lua require 路径（如 mods.xxx.scripts.objects.Foo）
x, y: 初始位置
opts: ds_map（可选，写入实例字段）
返回实例 ds_map（含 class/inst/x/y/vx/vy/visible/lua），失败返回 undefined

### `ntl_obj_step()`

每帧更新所有 Kristal 对象实例

---

## 玩家（5）

### `ntl_player_draw()`

绘制玩家（用 GM 基本图形，无需外部素材）

### `ntl_player_init(x, y)`

初始化 Neutraled 玩家（用于 Kristal 地图导航演示）

### `ntl_player_interact(map)`

玩家与地图对象交互（按 E/Z 触发最近的）

触发时把对象信息写进 global.ntl_interact_* 供 live/脚本读取，并调用 on_interact 事件

### `ntl_player_step(mapHandle)`

玩家每帧更新（移动 + 碰撞 + 相机）

按键：方向键 / WASD；碰撞用 ntl_map_collide

### `ntl_player_test(mapId)`

载入地图并放置玩家（最小闭环演示）

---

## LOVE2D 桥接（3）

### `ntl_love_event(op, ...)`

love.event 最小兼容

### `ntl_love_host(name, args)`

LOVE2D API 的宿主实现（由 ntl_lua_host 分派 __love_*）

### `ntl_love_init()`

把 LOVE2D API 桥接到 GameMaker（供 Kristal 等 Lua 引擎使用）

注册 global.ntl_lua_globals.love，以及 utf8 / 常用全局

---

## 资源操作（3）

### `ntl_sprite_from_file(path)`

运行时从 PNG 文件创建精灵（无需重新部署）

返回 sprite 索引，失败返回 -1

### `ntl_sprite_replace(targetSpriteName, pngPath)`

运行时替换某个精灵的图像

让 mod 可以"换皮"而不需要重新部署

### `ntl_sprite_resolve(spriteIdx)`

查询精灵是否被替换过（返回替换后的索引）

---

## 错误处理（2）

### `ntl_err_friendly(err, ctxFile, ctxLine)`

把 Lua 的英文报错翻译成中文 + 给出修改建议

返回形如：
[文件:行] 调用了不存在的函数
```gml
Lua:  attempt to call a nil value (global 'foo')
```
建议:  检查函数名拼写；或用 pcall 包住调用

### `ntl_err_text(e)`

把 try/catch 捕获到的异常变成一行可读文本

背景：GMS2 的运行时异常是个结构体（message / longMessage / stacktrace / script / line），
```gml
直接 string(e) 会得到 8 行 JSON，控制台里既难读又白占缓冲区。
这里优先取 message，取不到再退回 string(e)，并把换行折成空格。
```

---

## 主循环（2）

### `ntl_loop_emit(phase)`

主循环各阶段调用挂载的 mod 脚本

返回 1 = mod 已接管（调用方应跳过默认行为）

### `ntl_loop_hook(phase, handler)`

主循环接管

phase: "step" / "draw" / "begin" / "end"
mod 用它可以完全掌控每一帧（比如替换整个渲染流程）
handler 为 "" 时取消注册

---

## 命名空间（1）

### `ntl_ns_load()`

从 api-registry.json 构建命名空间映射

注意：一律用 variable_struct_get 读取 JSON 字段（struct.field 形式在 UTMT 编译下不可靠）
global.ntl_ns       : ds_map "ns.func" -> 实际脚本资源名
global.ntl_ns_const : ds_map "ns.CONST" -> 值
global.ntl_mods_info: 数组 [{id,name,author,ns}]

---

## 附录：无文档脚本（4）

这些是内部实现，一般不直接调用：

- **Lua 解释器**：4 个 — ntl_lua_compile, ntl_lua_p_and, ntl_lua_run, ntl_lua_tok

