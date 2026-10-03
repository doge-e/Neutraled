# 控制台（CONSOLE）

> **打开/关闭**：`F2` 开关，`Esc` 也可关闭。控制台不会暂停游戏（需要暂停用 `pause`）。
> **本文档已与源码全量核对（2026-09-28）**：与 `api/ntl_console_cmds_builtin.gml` 的注册表逐条一致 —— **64 条内置命令 / 7 个分类**（info 14 · state 9 · action 11 · power 5 · debug 16 · auto 9）；旧版本写的"内置 41 条 / 5 类"是过期数字（2026-09-29 复核：注册表实际 **64** 条，比 09-28 多出 `dry`）。
> 对照代码：`api/ntl_console_exec.gml`（分派）、`api/ntl_console_cmds_builtin.gml`（注册）、各 `api/ntl_console_*.gml`（实现）、`api/ntl_i18n_init.gml` + `api/ntl_i18n_out.gml`（文案/用法）。

---

## 一、快捷键与交互

| 操作 | 按键 | 说明 |
|---|---|---|
| 开关控制台 | `F2` | 打开时屏蔽游戏输入（不会误触游戏按键） |
| 关闭控制台 | `Esc` | 与 `quit` 同义（**只关控制台，不退出游戏**） |
| 执行输入行 | `Enter` | 空行忽略 |
| 滚动 | `↑` / `↓` | 逐行；`Shift+↑/↓` 快滚 10 行 |
| 翻页 | `PgUp` / `PgDn` | 14 行 |
| 首/尾 | `Home` / `End` | 跳到最早 / 回到底部（回到底部即恢复自动跟随） |
| 滚轮 | 鼠标滚轮 | 每次 3 行 |
| 翻历史 | `Ctrl+↑` / `Ctrl+↓` | 最近 100 条，持久化在 `Neutraled/console-history.txt` |
| 补全 | `Tab` | 见下方"已知限制" |
| 帮助 | `F1` | 等价于输入 `help` |
| 删除 | `Backspace` | 支持长按连删 |

- **着色**：`[错误]` 红、`[警告]` 黄、`> ` 回显的命令、`==` 分类标题各有颜色。
- **状态指示**：右上角 `[已滚行数/最大行数]`；回到底部时变绿。
- **过滤器生效时**标题栏追加 `[filter: xxx]`。
- 输出缓冲 **400 行**（源码 `api/ntl_console_log.gml:49-56`），超出会丢弃最早的行，并把**丢弃条数**记在 `global.ntl_console_dropped`（标题行与 `save` 导出的头部会显示，避免「导出其实是截断过的」而不自知）；`style lines <5-30>` 控制可见行数。

**已知限制（Tab 补全）**：补全逻辑在 `api/events/Step_1.gml`（控制台输入处理），它遍历注册表取**第一个**前缀匹配项 —— 同一前缀有多个候选时（例如 `s` 对应 `save/style/saves/setvar/...`）结果取决于 `ds_map` 键序，可能不稳定；唯一候选时正常。建议修法见本文第十节「已知限制 / 未决问题」。

---

## 二、输入解析规则

1. **命令与参数**：以第一个空格切分。`spawn obj_kris 100 200` → 命令 `spawn`，参数 `obj_kris 100 200`。
2. **大小写不敏感**（2026-09-28 起）：`Mods`、`HELP` 会被自动纠正成小写执行，并回显一行提示：
   ```
   > Mods
   命令名不区分大小写：Mods → mods
   已加载 12 个 mod / 已安装目录 30 个
   ```
   （只对内置命令生效；mod 命令带 `modid:cmd` 前缀，前缀大小写由 mod 决定，不做纠正。）
3. **别名展开**：`alias` 注册的别名在分派前展开，`;` 分隔多条命令。
4. **未识别的输入**给 4 行提示 + **最相近命令建议**，且**不隐式求值**（求值入口只有 `eval` / `exec`）：
   ```
   > profle
   [未知命令] profle
     · 输入 help 看命令列表
     · mod 命令要带前缀，例如 mymod:cmd
     · 想算一个表达式请用: eval profle
     · 你是不是想输入 profile？
   ```
5. **延时执行**：`sleep <帧数> <命令>` 把后续命令排进延时队列（`api/ntl_console_power_tick.gml:19-41` 每帧驱动），不阻塞游戏。
6. **破坏性命令要二次确认**（2026-09-28 起）：
   - `destroy <对象>` → 只报数量与确认方式，`destroy <对象> yes` 才真的销毁；
   - `crash` → 只提示，`crash yes` 或 `crash 1|2|3` 才真的触发崩溃。
   脚本（`.ntlcmd`）里的这两条命令同样要带 `yes`。

---

## 三、命令总账（64 条 / 7 类）

用 `cmds` 看全部（开头就报总数与分类数），`cmds <分类>` 只看一类，`help <命令>` 看单条用法。

### 信息类 info（14）

| 命令 | 用法 | 说明 |
|---|---|---|
| `help` | `help [命令名]` | 命令列表 / 查询某命令用法（支持模糊匹配） |
| `cmds` | `cmds [分类]` | 按分类列出所有命令 |
| `version` | — | 显示 Neutraled 版本 |
| `mods` | `mods [关键词]` | **列出本产物已加载的 mod（Id/Name/版本）+ 已安装目录数** |
| `hooks` | — | 列出所有 hook（脚本/内置/对象事件） |
| `api` | `api <关键词>` | 搜索游戏资源（脚本/对象/精灵/声音） |
| `vars` | `vars [前缀]` | 列出已知全局变量与当前值 |
| `profile` | — | 性能统计（帧率/缓存/Hook/对象） |
| `timing` | — | 运行时长与帧时间 |
| `modinfo` | `modinfo <名字或 Id 片段>` | 某个 mod 的详细信息（含磁盘目录与 live 层信息） |
| `res` | `res [untrack <类别> <id>]` | 资源监控报告（动态精灵等；`res untrack` 注销一条） |
| `whatis` | `whatis <名字>` | 查询一个名字：游戏资源 / mod 命名空间 / 内置 API |
| `save` | `save [名字]` | 把控制台输出保存到 `Neutraled/logs/<名字>.txt` |
| `style` | `style lines <5-30> \| alpha <0.3-1.0>` | 控制台外观（可见行数/透明度） |

### 状态类 state（9）

| 命令 | 用法 | 说明 |
|---|---|---|
| `room` | — | 当前房间信息（名称/尺寸/实例数/对象种类数） |
| `inst` | `inst [对象名]` | 实例统计（按对象分组计数） |
| `objs` | `objs [关键词]` | 列出当前房间所有对象名（可过滤） |
| `flags` | `flags [起始] [数量]` | 查看 `global.flag` 数组（默认 20 条，带总条数与翻页提示） |
| `player` | — | 玩家位置与朝向 |
| `maps` | `maps [关键词]` | 列出可用的 Kristal 地图（可过滤） |
| `saves` | — | 列出存档快照（最近 20 个） |
| `cache` | — | 部署缓存状态 |
| `world` | — | Kristal world 状态 |

### 操作类 action（11）

| 命令 | 用法 | 说明 |
|---|---|---|
| `goto` | `goto <1-7>` | 跳转到指定章节 |
| `loadmap` | `loadmap <地图名>` | 加载地图并放置玩家 |
| `spawn` | `spawn <对象> [x] [y]` | 创建对象实例（默认房间中心） |
| `destroy` | `destroy <对象> yes` | 销毁某对象的所有实例（**要先看数量确认**） |
| `setvar` | `setvar <对象> <变量> <值>` | 改任意实例的任意变量（同类实例多于 1 个时会提示只改了第一个） |
| `setflag` | `setflag <编号> <值>` | 设置游戏 flag（超范围会报出当前长度） |
| `screenshot` | `screenshot [名字] [延时秒数]` | 截图到游戏存档目录（带延时可截"过几秒才出现"的画面） |
| `reload` | — | 热重载 live 脚本 |
| `clear` | — | 清空控制台输出 |
| `quit` | `quit` | 关闭控制台（**不退出游戏**；退出游戏请关窗口） |
| `dry` | `dry [on\|off\|status]` | 破坏性命令的**干跑**开关（详见文末「dry」节） |

### 强力类 power（5）

| 命令 | 用法 | 说明 |
|---|---|---|
| `speed` | `speed <倍率>` | 游戏速度倍率（0.25–8）；`speed` 看当前，`speed 1` 还原 |
| `warp` | `warp <房间名>` | 直接跳到任意房间（比 `goto` 章节更激进），房间不存在会明确报错 |
| `hp` | `hp [值]` | 查看/设置玩家血量（自动探测 HP 变量；找不到会说明而不是瞎猜） |
| `god` | `god` | 无敌开关（每帧回满血） |
| `freeze` | `freeze <对象\|global> <变量> [值]` | 每帧把变量锁成某值（不填值=锁当前值）；`freeze list` / `freeze clear` |

### 调试类 debug（16）

| 命令 | 用法 | 说明 |
|---|---|---|
| `pause` | — | 冻结游戏逻辑（控制台仍可用） |
| `resume` | — | 恢复游戏逻辑 |
| `step` | `step [帧数]` | 冻结状态下单帧推进 |
| `dump` | `dump [名字]` | 导出当前房间快照到 `Neutraled/logs/` |
| `watch` | `watch <对象> <变量> [帧数]` | 逐帧跟踪某实例变量并写日志（不刷屏） |
| `crash` | `crash yes`（或 `crash 1\|2\|3`） | 故意触发运行时错误（验证崩溃隔离，**需确认**） |
| `eval` | `eval <表达式>` | 求值 Lua 表达式，如 `eval 1+2`、`eval Kristal.getFlag("x")` |
| `exec` | `exec <代码>` | **执行一段 Lua 代码**（可多语句；与 eval 不同：不求值结果） |
| `err` | `err [clear]` | 显示/清空最近的 Lua 错误（最多 10 条） |
| `ctx` | — | 当前执行上下文（哪个 mod / 哪个事件） |
| `trace` | `trace on\|off` | 切换 Lua 语句级跟踪 |
| `log` | `log <文字>` | 写一行到日志 |
| `budget` | `budget [数值]` | 查看/设置 Lua 执行预算 |
| `lang` | `lang zh\|en\|auto` | 切换语言 |
| `filter` | `filter all\|error\|warn\|cmd` | 日志过滤（只看某类输出） |
| `timeit` | `timeit <命令>` | 执行命令并显示耗时 |

### 自动化类 auto（9）

| 命令 | 用法 | 说明 |
|---|---|---|
| `run` | `run <脚本路径>` | 执行命令脚本文件（`.ntlcmd`） |
| `batch` | `batch <脚本路径>` | 同 run |
| `alias` | `alias <别名> <命令>` | 定义命令别名（`;` 分隔多条） |
| `bind` | `bind <键名> <命令>` | 把命令绑定到按键（控制台打开时生效） |
| `macro` | `macro start\|stop\|play\|show` | 录制/回放命令序列 |
| `sleep` | `sleep <帧数> <命令>` | 延时执行命令（复用延时队列，游戏不卡） |
| `loop` | `loop <次数> <命令>` | 重复执行命令 N 次（上限 1000） |
| `autorun` | `autorun [文件]` | 执行启动脚本（默认 `Neutraled/autorun.console`） |
| `hist` | `hist [clear\|save]` | 查看/清空/导出命令历史 |

### mod 自定义命令（mod）

mod 用 `ntl_console_register_mod(...)` 注册的命令，玩家输入时要带命名空间前缀 `modid:cmd`（见 §六）。`cmds mod` 只看这一类。

---

## 四、几个关键命令的输出样例

**`mods`（用户主诉：以前只列 1 个）** —— 现在读的是本产物真正加载的清单 `<运行目录>Neutraled/mods.json`，并同时报磁盘安装数：

```
> mods
已加载 12 个 mod / 已安装目录 30 个
  1. Deltarune 60 FPS  [converted.deltarune_60_fps.badartadventure]  v2.0.0
  2. 60fps_layer  [layer.60fps_layer.badartadventure]  v1.0.0
  3. DOJO  [converted.dojo.xanzo1]  v0.1.0
  ...
运行时脚本层（live）: 1
  [live/] xxx
单个 mod 详情: modinfo <名字或 Id 片段>
```

- 清单超过 30 条时只显示前 30 条，并提示 `…… 还有 N 条未显示（用 mods <关键词> 过滤）`。
- `mods dojo` 只列名称/Id/版本里含 `dojo` 的项。
- 本产物没有 `mods.json`（老产物 / 外部章节 exe）时回退成：`本产物没有 mods.json（老产物/外部 exe）：只能列出已安装目录 30 个`。

**`modinfo`（以前查不到已加载 mod）**：`modinfo dojo` 现在先查已加载清单（打印显示名/Id/版本/磁盘目录），再列 live 层与该 mod 的 hook。

**`flags`（带总条数与翻页提示）**：

```
> flags
global.flag[0..19] / 共 1024 条
flag[0] = 0
...
  继续看：flags 20 20
```

**`cmds`（开头就报总数）**：

```
> cmds
共 64 条命令 / 7 个分类（看某类：cmds <分类>）

== 信息类 (14) ==
  api   api <关键词>
      搜索游戏资源（脚本/对象/精灵/声音）
...
共 64 条命令
提示: help <命令> 看详细用法；mod 命令带前缀如 mymod:cmd
```

**`destroy` / `crash`（二次确认）**：

```
> destroy obj_darkcontroller
这会销毁 6 个 obj_darkcontroller 实例（不可撤销）。确认请再输入：destroy obj_darkcontroller yes

> crash
crash 会故意让游戏崩溃（测试崩溃隔离用）。确认请再输入：crash yes（或 crash 1|2|3）
```

---

## 五、脚本自动化

#### 脚本格式（`.ntlcmd`）

```
# 以 # 或 // 开头的是注释
log ===== 自动化开始 =====
version
profile
goto 4
sleep 60 screenshot after_load
exec ntl_console_log("来自脚本")
```

#### 路径解析顺序

1. 绝对路径 / 相对游戏目录
2. `Neutraled/scripts/<名字>`
3. 当前 mod 目录下的 `scripts/<名字>`

（省略扩展名时会自动补 `.ntlcmd`；单文件上限 500 行）

#### 示例

```
> run demo          # 执行 Neutraled/scripts/demo.ntlcmd
```

#### 别名与绑定

```
> alias ff goto 4; sleep 60; screenshot    # 定义别名（; 分隔多条）
> ff                                        # 执行
> bind F5 profile                           # F5 触发 profile
> bind F6 reload                            # F6 热重载
```

#### 宏录制

```
> macro start      # 开始录制（之后的命令会被记录）
> goto 4
> profile
> macro stop       # 停止
> macro show       # 查看内容
> macro play       # 回放
```

#### 启动脚本 `autorun`

放到存档区 `Neutraled/autorun.console`，**游戏启动时自动执行**（逐条容错：一条失败不会让游戏崩，会记进日志）。

```text
# 例：进游戏、等 7 秒、探测 HP、导出快照
goto 1
sleep 420
hp
dump ingame
save ingame
```

- `sleep <帧数>` 把**后续命令**排进延时队列，到点执行（交互态同样可用）；
- `save <名字>` 把控制台全部输出导出到 `Neutraled/logs/<名字>.txt`，排查时直接发这个文件；
- 注意：**切换章节 = 换进程**，root 进程里排的延时命令会随进程结束；要在章节内验证就部署该章节并让它自己启动。

---

## 六、mod 添加命令

#### 注册（在 mod 的 `main.lua` 里）

```lua
ntl_console_register_mod(
    "heal",                              -- 命令名
    "恢复全队满血",                       -- 说明
    "console/heal.lua",                   -- 处理脚本（相对 mod 目录）
    "mymod:heal [角色]"                   -- 用法提示
)
```

玩家就能输入 `mymod:heal` / `mymod:heal susie`，也能 `help mymod:heal` 查用法。

#### 处理脚本（`console/heal.lua`）

```lua
-- arg 是命令行后面的参数（字符串）；args 是分割好的参数表
ntl_console_log("正在恢复...")

local target = "kris"
if arg ~= nil and arg ~= "" then target = arg end

-- 可以调用现成的内置命令（与玩家手输等价）
ntl_console_exec("player")
ntl_console_exec("screenshot heal_done")

-- 也可以用现成的 API
local inst = ntl_inst_find("obj_kris")
if #inst > 0 then
    ntl_inst_set(inst[1], "hp", 999)
end
```

#### 范围隔离

| mod A 注册 | mod B 注册 | 结果 |
|---|---|---|
| `a:heal` | `b:heal` | 互不冲突，各自独立 |
| `a:info` | — | 只有 A 有 info |

（内置命令永远不带前缀，所以 mod 不能覆盖 `help` / `mods` 等。）

---

## 七、mod 调用现成命令

```lua
ntl_console_exec("profile")            -- 执行一条控制台命令
ntl_console_exec("goto 4")
ntl_console_exec("spawn obj_kris 100 200")
ntl_console_run("demo")                -- 执行命令脚本文件
ntl_console_log("我的 mod 在做事情")     -- 往控制台写一行（玩家按 F2 能看到）
```

典型用法（自动化自检）：

```lua
function run_selftest()
    ntl_console_log("===== 自检开始 =====")
    ntl_console_exec("room")
    ntl_console_exec("profile")
    ntl_console_exec("screenshot selftest")
    ntl_console_log("===== 自检结束 =====")
end
```

---

## 八、快速上手

```
按 F2 打开控制台

> cmds                  看所有命令（64 条 / 7 类）
> cmds mod              只看 mod 提供的命令
> help goto             查 goto 的用法
> mods                  看本产物已加载的 mod
> goto 4                跳到第 4 章
> profile               看性能
> eval 1+2              算个数
> exec ntl_console_log("hi")   跑一段 Lua
> run demo              跑自动化脚本
> sleep 60 screenshot shot1    60 帧后截图
> alias t profile       定义别名 t
```

---

## 九、2026-09-28 审计修正摘要

| 症状 | 修法 | 位置 |
|---|---|---|
| `mods` 只列 1 个 mod | 改读本产物 `Neutraled/mods.json`（`ntl_modmenu_loaded()`）+ 磁盘目录数，支持关键词过滤与 30 条截断提示 | `api/ntl_console_info.gml`、新建 `api/ntl_console_mod_dir.gml` |
| `modinfo` 查不到已加载 mod | 先查已加载清单再查 live 层 | `api/ntl_console_info.gml` |
| `save/style/timeit/hist` 能用但 help/cmds 里看不到 | 补注册（59 → 63 条；2026-09-29 又加入 `dry` ⇒ 现 **64 条**），与 i18n 的 `cmd.*` 组（现 64 组）完全对齐 | `api/ntl_console_cmds_builtin.gml` |
| `exec` 与 `eval` 是同一个实现 | `exec` 改为真正执行 Lua 代码块（`ntl_console_exec_lua`） | `api/ntl_console_exec.gml`、新建 `api/ntl_console_exec_lua.gml` |
| `sleep` 交互态只打提示 | 真的排队延时执行 | `api/ntl_console_exec.gml` |
| `macro` / `loop` 注册了却无效 | 分派时传错子命令名（`macro_split`/`loop_split`） | `api/ntl_console_exec.gml` |
| `Mods` 大小写敏感且无提示 | 内置命令小写重试 + 提示 | `api/ntl_console_exec.gml` |
| 打错命令没有任何线索 | 最相近命令建议 | 新建 `api/ntl_console_similar.gml` |
| 长列表刷屏/看不到总数 | `cmds/api/vars/inst/objs/maps/saves/flags` 加总数、40 条上限、翻页/过滤提示 | `api/ntl_console_state.gml` 等 |
| `destroy` / `crash` 无确认 | 二次确认（`yes`） | `api/ntl_console_action.gml`、`api/ntl_console_power.gml` |
| `setvar` 只改第一个实例却不说 | 明确提示实例数 | `api/ntl_console_action.gml` |
| `hist` 全英文硬编码 | 全部走 i18n，`hist clear` 同时落盘 | `api/ntl_console_exec.gml` |

---

## 十、已知限制 / 未决问题

1. **Tab 补全**在多候选时结果取决于 `ds_map` 键序（逻辑在 `api/events/Step_1.gml:139-175`）。建议修法：先 `ntl_dsmap_keys` 排序，再按前缀收集候选并循环切换 + 列出候选。
2. `ntl_mod_require`（取别的 mod 的导出表）仍报"调用了不存在的函数"；已确认 GML 侧分派存在（`api/ntl_call_host.gml:93`），待查 `ntl_lua_ev_stat.gml` 的名字解析。
3. `maps`/`saves`/`cache` 用的是 `program_directory`（游戏根），与运行目录 `working_directory` 不同的部署方式下可能看不到文件。
4. `inst`/`objs` 等列表默认上限 40 条，只给"用关键词缩小范围"的提示，没有 `--more` 式的逐页翻（`flags` 支持按起点翻页）。

---

### 📄 相关文档

| 文档 | 内容 |
|---|---|
| `docs/CONSOLE.md` | 本文件：控制台命令、交互与自动化 |
| `docs/THEMES-LANGS.md` | 中英双语与主题 |

### dry —— 破坏性命令的干跑（演练）开关

```
dry on      打开干跑：破坏性命令只报告会发生什么，不改动游戏
dry off     关闭干跑，命令恢复真实执行
dry         查看当前状态
```

打开后受影响的命令：`goto` / `loadmap` / `warp` / `spawn` / `destroy` / `setvar` / `setflag`。

- 干跑时**照样做参数校验**：`warp nonexistent` 一样报「房间不存在」，`spawn no_such_obj` 一样报「对象不存在」。
- 干跑只打印 `[干跑] 将要执行: …`，一个字节的游戏状态都不改；`setvar` / `setflag` 还会把**原值**一起报出来。
- `destroy` 在干跑下只报数量，不需要再打 `yes`（反正不会真销毁）。
- 打开时控制台标题栏会常驻显示 `[干跑中]`，避免误以为命令失灵。
- 典型用法：先在干跑下把命令试一遍确认语法与影响面，再 `dry off` 真执行。
