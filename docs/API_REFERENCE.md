# Neutraled API 参考

> 自动生成于 2026-09-21 23:54（从 api/*.gml 的注释块提取）
> 共 293 个脚本，其中 289 个有文档

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

## 其他（62）

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

### `ntl_config_load()`

读取 Neutraled/config.json 到 global.ntl_cfg

配置项（全部可选）：
auto_skip_selector : 自动跳过章节选择器，直接进入 auto_chapter
auto_chapter       : 自动进入的章节序号（1-7 官方；mod 章节用 auto_chapter_id）
auto_chapter_id    : 直接指定章节 id（如 timeline:4:mod:alt）
auto_skip_delay    : 跳过前的等待帧数（默认 90）
auto_skip_intro    : 尝试跳过章节内的开场演出（实验性）
debug_live         : 打开 live 脚本调试日志

### `ntl_draw_mods()`

广播 on_draw（mod 在 GUI 层绘制自己的东西）

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

### `ntl_get_lang_string(key)`

读取生效的文本（覆盖优先，其次 lang_map）

### `ntl_goto_chapter(chapter)`

直接切换到指定章节（跳过章节选择器的按键操作）

返回 1 成功 / 0 失败。实现：调用 obj_CHAPTER_SELECT 的 launch_game（内部走 game_change）。

### `ntl_hook(event_name, handler_script)`

订阅事件（handler 为脚本资源索引或函数）

返回订阅序号（失败返回 -1）

### `ntl_hotreload_check()`

检测 IDE 写入的 reload.flag，触发热重载 live 脚本

让开发者在 IDE 里改脚本 → 保存 → 游戏内立即生效（无需重启游戏）

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

### `ntl_kr_flag_key(args)`

从参数表里取出 flag 键

跳过表参数（方法调用的 self 或配置对象），返回第一个字符串或数字

### `ntl_kr_flag_value(args)`

从参数表里取出 flag 的值（最后一个非表参数）

### `ntl_log(tag, msg)`

写运行日志（相对路径，沙箱重定向到 %LOCALAPPDATA%\DELTARUNE\）

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

### `ntl_root_launch(slot)`

启动当前页第 slot 个槽位对应的章节

### `ntl_root_page_items(page)`

该页 7 个槽位对应的章节索引（-1 = 空）

### `ntl_root_pages()`

总页数（每页 7 项，基于显示列表长度）

### `ntl_root_step()`

顶层章节界面的输入处理（仅在官方选择器存在时生效）

★★★ 控制台打开时，章节选择器完全不响应任何输入（包括 Enter/Z 进入章节）

### `ntl_run_mods()`

依次执行各 mod 入口脚本

### `ntl_screenshot(name)`

让游戏自己保存一张截图（自动化验证用）

文件落在游戏沙箱目录（game_save_id）；返回实际调用结果。

### `ntl_set_lang_string(key, text)`

注册文本覆盖（下一帧应用到 global.lang_map）

### `ntl_skip_intro()`

按配置跳过章节内的开场演出（传说 / DELTARUNE 报幕）

全部由 config.json 开关控制，默认关闭（不影响正常体验）。
实现：识别开场房间名 → 等待 intro_delay 帧 → room_goto 到下一阶段房间。

### `scr_ntl_init`

============================================================

Neutraled 引导 —— builder 会把它 prepend 到 obj_initializer2 Create 最前
职责：防御性全局初始化 → 运行时状态 → 控制器创建 → mod 清单 → 入口
约束：老式脚本；每个函数都是独立同名脚本资源；禁用 struct/constructor
============================================================

---

## 控制台（32）

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

### `ntl_console_eval(expr)`

用 Lua 求值任意表达式（开发调试利器）

```gml
例：eval 1+2 / eval Kristal.getFlag("wr_set") / eval #ntl_mod_list()
```

### `ntl_console_exec(cmdline)`

解析并执行一条控制台命令


★ 范围解析（用户要求）：
1) 别名（alias 定义的）优先
2) 内置命令（ntl 的）—— 不需要前缀
3) mod 命令 —— 必须带 "modid:cmd" 前缀，各自在命名空间里
4) 都不是 → 尝试作为 Lua 表达式求值（方便调试）

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

### `ntl_console_info(cmd, rest)`

信息类命令实现（mods/hooks/modinfo/timing）

### `ntl_console_key_vk(keyName)`

键名 → 虚拟键码

### `ntl_console_line_pass(line)`

该行是否通过当前过滤器

### `ntl_console_log(text)`

输出到控制台面板（按换行拆分，多行不重叠）

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

### `ntl_shared_get(key)`

读取跨 mod 共享状态

### `ntl_shared_set(key, value)`

跨 mod 共享状态总线（任意 mod 可读写）

用途：mod A 设置一个状态，mod B 读取并做出反应（无需互相依赖代码）

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

## 国际化（8）

### `ntl_i18n_init()`

初始化中英双语（**只覆盖 Neutraled 新增的内容**，游戏原版文字不动）


语言来源（优先级）：
1) config.json 的 "lang": "zh" | "en" | "auto"
2) auto → 跟随游戏语言（global.lang）
3) 缺省 → zh

### `ntl_lang_set(lang)`

切换语言（zh / en / auto）

### `ntl_t(key)`

取当前语言的文案（只用于 Neutraled 新增的内容）

找不到时返回 key 本身（便于发现缺翻译）

### `ntl_tf(key, v1, v2)`

取文案并替换 {n} / {1} / {2} 占位符

### `ntl_tok`

老式脚本（每个函数一个同名脚本资源）

### `ntl_tok_is_alnum`

老式脚本（每个函数一个同名脚本资源）

### `ntl_tok_is_alpha`

老式脚本（每个函数一个同名脚本资源）

### `ntl_tok_is_digit`

老式脚本（每个函数一个同名脚本资源）

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

### `ntl_res_track(kind, id, tag)`

登记动态创建的资源（用于泄漏检测）

kind: "sprite" / "surface" / "sound" / "buffer" / "ds"

### `ntl_res_untrack(kind, id)`

注销资源（释放时调用）

---

## 工具函数（6）

### `ntl_dir_list(path)`

列出一个目录下的子目录名

注意：GM 的 file_find_first 对绝对路径支持有限，这里做双重尝试

### `ntl_ensure_dir(path)`

确保路径所在目录存在（GM 不会自动建目录）

传入文件路径或目录路径都可以：只对"看起来像文件"的去掉最后一段

### `ntl_file_list(path, pattern)`

列出目录下的文件

### `ntl_json_esc(s)`

JSON 字符串转义（GM 的 json_encode 只接受 real，不能转字符串）

### `ntl_string_join_ext(sep, arr)`

数组连接成字符串

### `ntl_string_replace_all(s, find, repl)`

全局替换（GM 没有内置的 string_replace_all）

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

## 错误处理（1）

### `ntl_err_friendly(err, ctxFile, ctxLine)`

把 Lua 的英文报错翻译成中文 + 给出修改建议

返回形如：
[文件:行] 调用了不存在的函数
```gml
Lua:  attempt to call a nil value (global 'foo')
```
建议:  检查函数名拼写；或用 pcall 包住调用

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

