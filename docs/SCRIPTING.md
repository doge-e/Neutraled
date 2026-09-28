# 脚本与联动（SCRIPTING）

> **本篇包含**（按顺序）：
> - [跨 mod 联动完全打通（2026-09-21 下午）](#跨-mod-联动完全打通2026-09-21-下午) — 跨 mod 联动 API
> - [Neutraled Lua 开发指南](#neutraled-lua-开发指南) — Lua mod 开发指南
> - [Neutraled Live Script（运行时 mod）](#neutraled-live-script运行时-mod) — Live Script 运行时 mod

## 跨 mod 联动完全打通（2026-09-21 下午）

> 你的核心要求：「mod 也要能够对和他不冲突的任何 mod 完全支配或者说任意联动」
>
> **现在实现了。**

---

### 实测证据（游戏内运行）

```
[InteropB] ========== 联动验收 ==========
[InteropB] [1/6] 取导出表        OK
[InteropB] [2/6] 调用函数        add(20,22)=42
[InteropB] [3/6] 带参调用        greet=你好，世界
[InteropB] [4/6] 读常量          VERSION=1.2.0
[InteropB] [5/6] 共享总线        =100
[InteropB] [6/6] 事件监听        OK version=1.2.0
[InteropB] ========== 验收结束 ==========
```

**两个独立 mod，互相调用函数、读常量、监听事件。**

---

### 真正的根因：ntl_lua_fn_new 忘了登记函数

```gml
// 修之前
var _fn = ds_map_create();
ds_map_add(_fn, "_ntlfn", 1);
return _fn;                    // 没有登记！

// 修之后
var _fn = ds_map_create();
ds_map_add(_fn, "_ntlfn", 1);
ntl_lua_table_register(_fn);   // 关键
return _fn;
```

#### 为什么难查

`ntl_lua_is_fn` 出于安全考虑（防 ds_map_exists 对无效句柄抛 Code Error），
要求值必须在 `ntl_lua_tables` 登记表里才认定为函数。

而 `ntl_lua_fn_new` 用的是裸 `ds_map_create()`，从来没登记过。

#### 症状链条（全是看起来正常但结果不对）

| 步骤 | 表现 |
|---|---|
| 1 | 表里确实存着函数（type(a.add) = number） |
| 2 | is_fn 判定失败，走 is_real 分支 |
| 3 | 查 __call 元方法，没有，返回 undefined |
| 4 | local sum = a.add(20,22) 赋值为 nil |
| 5 | tostring(sum) 时变量未定义，回退成 __host:sum |
| 6 | 日志显示 add(20,22) = __host:sum（像字符串拼接 bug） |

---

### 新增的联动能力

| API | 作用 |
|---|---|
| ntl_mod_export(name, fn) | 导出函数/常量（**函数可被跨 mod 调用**） |
| ntl_mod_require(modId) | 取对方的导出表 |
| **ntl_mod_import(modId)** | **新：直接把对方 API 注入自己环境**，之后直接调 fn() |
| ntl_mod_emit / ntl_mod_on | 跨 mod 事件广播/监听 |
| ntl_shared_set / get | 内存共享总线 |
| **ntl_mod_data_set / get** | **新：持久共享数据**（存 mods-shared.json） |
| **ntl_mod_ids()** | **新：返回 Lua 表**（可用 # 和 ipairs） |
| **ntl_hook_summary()** | **新：hook 冲突摘要** |
| **ntl_hook_plan(name)** | **新：hook 执行计划**（自动 pre→post 排序） |
| ntl_console_register_mod | mod 注册控制台命令（带命名空间） |

---

### 依赖自动启用（builder 侧）

**问题**：玩家启用了 X，但忘了启用它依赖的 Y，一堆报错。

**方案**：部署时分析依赖图，被依赖的 mod 自动启用。

```
依赖自动启用: 2 个
  + FrostveilCore（被 Frostveil 依赖）
  + SharedLib（被 FrostveilCore 依赖）
缺失依赖: 1 个
  ! MyMod 依赖 not.installed（未安装）
```

支持多层依赖迭代 + 循环检测。

---

### hook 冲突自动解决

**以前**：两个 mod hook 同一函数需要手动裁决。
**现在**：自动链式执行（pre → 原函数 → post）。

```
  [多 mod] snd_play  <-- Frostveil(pre) + MyMod(post)
共 5 个 hook，其中 1 个函数被多个 mod 修改
（已自动链式执行，无需手动裁决）
```

---

### 完整能力矩阵

| 需求 | API |
|---|---|
| A 提供函数给 B 用 | ntl_mod_export + ntl_mod_require / ntl_mod_import |
| A 通知 B 发生了某事 | ntl_mod_emit + ntl_mod_on |
| A 和 B 共享临时状态 | ntl_shared_set / get |
| A 和 B 共享持久数据 | ntl_mod_data_set / get |
| A 给 B 加控制台命令 | ntl_console_register_mod |
| A 和 B 都改同一函数 | 自动链式执行（ntl_hook_plan） |
| A 依赖 B | 自动启用 B（AutoEnable） |
| B 发现有哪些 mod | ntl_mod_ids / ntl_mod_list |

---

### 本轮修的 bug 总览

| # | Bug | 影响 |
|---|---|---|
| 1 | ntl_lua_fn_new 没登记函数 | **跨 mod 函数调用全废** |
| 2 | case var 的 nil 直接返回 | 报「调用了不存在的函数」 |
| 3 | ntl_call_host 缺 _a0/_a1/_a2 | 23 处分派静默失效 |
| 4 | Create_0 重建 global | 控制台永远空白 |
| 5 | 功能键 else-if 链 | Enter 失效 |
| 6 | ntl_mod_list 返回 GML 数组 | Lua 的 # 认不出 |
| 7 | console_register_mod 越界读 _args[2] | 宿主异常 |

---

### 文档索引

| 文档 | 内容 |
|---|---|
| docs/SCRIPTING.md | **跨 mod 联动完整指南** |
| docs/CONSOLE.md | 控制台交互（滚动/历史/补全） |
| docs/CONSOLE.md | 命令系统（范围解析/自动化） |
| docs/THEMES-LANGS.md | 中英双语 |
| E:/CLAUDE.md | 项目上下文（新增 5 条坑） |

---

## Neutraled Lua 开发指南

> Neutraled 内置一个纯 GML 实现的 **Lua 解释器**，mod 可以直接用 `.lua` 写脚本，
> 与 NTL Script 并存、共用同一事件循环，并可通过 **hook 机制**改变游戏函数行为。

---

### 1. 最快的开始

在 mod 的章节目录里建一个 live mod（**改文件即生效，无需重新部署 data.win**）：

```
Neutraled/live/MyLuaMod/
    mod.json
    main.lua
```

`Neutraled/live/MyLuaMod/mod.json`
```json
{
  "name": "MyLuaMod",
  "author": "你的名字",
  "version": "1.0.0",
  "scripts": {
    "on_init": "main.lua",
    "on_frame": "tick.lua"
  }
}
```

`main.lua`
```lua
print("Hello from Lua!")
local t = {1, 2, 3, name = "Neutraled"}
print("表长度 = " .. #t .. "，名字 = " .. t.name)
```

最后把 mod 名加进 `Neutraled/live/index.json`：
```json
{ "mods": ["MyLuaMod"] }
```

**事件**：`on_init` / `on_ready` / `on_frame`（`arg` = 帧数）/ `on_draw` / `on_room_load`（`arg` = 房间号）/ `on_battle_start` / `on_battle_end`

---

### 2. 语言支持

| 类别 | 支持内容 |
|---|---|
| **变量** | 全局 / `local` / 多重赋值 `a, b = 1, 2` |
| **数据类型** | nil / boolean / number / string / table / function |
| **表** | `{1,2,3}` · `{k=v}` · `{[expr]=v}` · `t[i]` · `t.k` · `#t` |
| **运算** | `+ - * / // % ^ ..` · 比较 · `and or not` · `#` |
| **控制流** | `if/elseif/else` · `while` · `repeat/until` · 数值 `for i=a,b,c` · 泛型 `for k,v in pairs(t)` · `break` |
| **函数** | `function f()` · `local function` · 匿名函数 · 变长参数 `...` · 多返回值 · 递归 |
| **OOP** | `setmetatable` / `__index` / `__newindex` / `__call` / `__add` 等全套元方法；`function T:m()` 自动带 `self` |
| **模块** | `require("a.b")` —— 支持 mod 的 `lib/` `scripts/` `libraries/` 与 Kristal 引擎路径，带缓存 |

**标准库**：`print tostring tonumber type pairs ipairs rawget rawset error assert pcall unpack select next rawequal require`、
`math.*`、`string.*`、`table.*`、`os.time/date`、`io.write`、`love.*`（见下）

**未实现的 Lua 特性**：协程（coroutine）、goto、位运算元方法、完整 `string.format`（仅原样返回）

---

### 3. 与游戏/GML 互调

Lua 里**未定义的全局名会自动回退为宿主函数名**，调用时动态解析到游戏脚本：

```lua
-- 直接调用游戏函数（等价于 GML 里调用同名脚本）
snd_play(1, 1, 1)

-- 或显式调用
ntl.call("scr_screenshot", "shot.png")
```

Neutraled 提供的桥接（可直接用）：
| 名字 | 作用 |
|---|---|
| `ntl.log(msg)` | 写日志 |
| `ntl.screenshot(name)` | 游戏内截图 |
| `ntl.goto_chapter(n)` | 跳章节 |
| `ntl.call(脚本名, ...)` | 动态调用任意脚本 |
| `Kristal.getFlag(id)` / `Kristal.setFlag(id, v)` | 读写游戏 flag |
| `Assets.get(资源名)` | 按名解析精灵/声音/字体 |
| `Music.play(名)` / `Music.stop()` | 音乐控制 |
| `Input.isConfirm()` / `isCancel()` / `isMenu()` | 按键查询 |
| `love.timer.getTime()` / `love.graphics.getWidth()` 等 | LOVE2D API |
| `gml.string(x)` / `gml.floor(x)` / `gml.len(s)` / `gml.inst_count("obj")` … | **GML 口径直通表**（73 个名字，见下） |

> ⚠ **别在 Lua 里写 GML 的 `string(x)`**：`string` / `math` / `table` 这些全局名已被 Lua 标准库占用，
> GML 的同名包装被遮蔽 → `attempt to call a non-function value`。
> 要 GML 语义用 `gml.string(x)` / `gml.real(x)` / `gml.floor(x)` / `gml.inst_count("obj_ntl_core")`；
> 要 Lua 语义用 `tostring(x)` / `tonumber(x)` / `math.floor(x)`。两套都在，各自显式。
> `gml.*` 覆盖：类型/数学（string real floor ceil round sign abs sqrt power min max clamp lerp random irandom）、
> 字符串（len str_len str_sub str_find str_upper str_lower str_split str_contains str_char）、
> 数组/表/map（arr_* map_*）、实例/对象/房间（inst_* obj_spawn obj_count room_width_v room_height_v）、
> 绘制（draw_*）、文件/JSON/全局变量（file_read file_write json_parse_safe log get_global set_global var_get var_set）、
> 杂项（skip_intro sprite_resolve sprite_replace sprite_from_file perf_time whatis res_report res_untrack）。

---

### 4. 函数 Hook（**核心能力**）

用 Lua **拦截、扩展、或完全接管游戏的任意 GML 函数**。声明在 **编译期 mod** 的 `mod.json`：

```json
{
  "id": "my.hookmod",
  "name": "My Hook Mod",
  "author": "You",
  "enabled": true,
  "hooks": [
    { "script": "snd_play",  "mode": "override", "handler": "hooks/snd.lua" },
    { "script": "scr_text",  "mode": "pre",      "handler": "hooks/text.lua" },
    { "script": "scr_save",  "mode": "post",     "handler": "hooks/save.lua" }
  ]
}
```

| mode | 时机 | 返回值语义 |
|---|---|---|
| `pre` | 原函数**执行前** | 返回非 nil → **短路**（不执行原函数，直接返回该值） |
| `post` | 原函数**执行后** | 返回非 nil → **替换返回值** |
| `override` | **完全接管** | 返回 nil → 回落到原实现 |

`hooks/snd.lua`
```lua
-- args 是参数表（args[1] 起）；也可用 a1/a2/...；另有 script_name / hook_mode / argc
print("播放音效: " .. tostring(args[1]))
return nil    -- 放行，继续执行原实现
```

**完全接管示例**（把某个音效静音）：
```lua
if args[1] == 419 then
    return 0      -- 直接返回，不再播放
end
return nil        -- 其它音效放行
```

> Hook 需要 **重新部署** 对应的 data.win（编译期包装），部署后 `--deploy --chapter chapterN`。

---

### 5. `data + lua` 工作流（推荐）

| 想做的事 | 用什么 |
|---|---|
| 改代码逻辑、替换函数行为 | **Hook**（`hooks` 声明 + Lua） |
| 加新资源（精灵/声音/字体） | 编译期 mod 的 `sprites/` `sounds/` `fonts/` |
| 每帧/事件驱动的行为 | **Live Lua**（`on_frame` 等） |
| 改数值、读状态 | Live Lua + `Kristal.getFlag` / `ntl.call` |
| 新增完整章节 | `~Chapter:N:名字` + 自带 data |

**典型组合**：编译期 mod 提供资源 + hook 包装；live Lua 做运行时的动态调整（改完即生效）。

---

### 6. 调试

- 日志：`%LOCALAPPDATA%\DELTARUNE\Neutraled\dr-api.log`（Lua 输出前缀 `[lua]`）
- `print(...)` 多参数用 tab 分隔
- 出错会打印 `[lua] [xxx.lua] 运行错误: ...`
- IDE：`Neutraled/studio/bin/Release/net9.0-windows/ntl-studio.exe`（语法高亮 + 补全 + 日志）

---

### 7. 已知限制

| 限制 | 说明 |
|---|---|
| 无协程 | `coroutine.*` 未实现 |
| `string.format` 简化 | 只返回第一个参数，不做格式化 |
| 无 IO 库 | `io.*` 仅有 `write`（映射到日志） |
| 表键类型 | 数字键与字符串键内部用前缀隔离，`#t` 只统计连续整数键 |
| Kristal 引擎层 | 工具库（Class/TableUtils/...）已可加载；`Kristal` 主引擎（状态机/渲染）仍需继续移植 |




---

### 8. Kristal 集成（实测）

Neutraled 的 Lua 解释器可以直接加载并运行 **Kristal 引擎与 mod 的 Lua 代码**。

#### 已实测通过的内容

| 项目 | 结果 |
|---|---|
| Kristal 引擎工具库 | `Class` / `TableUtils` / `StringUtils` / `MathUtils` / `ColorUtils` / `ClassUtils` / `Vector` / `JSON` / `Ease` 全部加载成功 |
| **冰封帷幕（Frostveil）mod** | `mod.lua` 的 `init` / `postInit` 完整执行；子弹脚本 `a_heart` / `basicbullet` / `bigsnowball` / `bulletice` 加载成功 |
| **每帧回调** | `Mod:postUpdate()` 由游戏主循环每帧调用（实测 1080 帧调用 1079 次） |
| **flag 系统** | `Game:getFlag/setFlag/addFlag` 支持命名 flag 与原版数字 flag |

#### 使用方式

```lua
-- 加载 Kristal mod 入口（路径用点号：scripts/data/actors → scripts.data.actors）
local modDef = require("mods.chapter5wr_windows.mod")

-- 按 Kristal 的方式初始化
Mod.info = { name = "Frostveil", id = "chapter_5_weird" }
Mod.name = "Frostveil"
Mod:init()
Mod:postInit()

-- 之后 postUpdate 会被每帧自动调用（无需自己挂）
```

#### 可用的 Kristal API（兼容层）

```
Kristal.{ version, Config, getPresence, getFlag, setFlag, playSound, playMusic, States }
Game.{ world, party, money, getFlag, setFlag, addFlag, setBorder, alert }
Game.world.{ can_open_menu, map, timer:after/tween/during, hasCutscene, stage }
Assets.{ get, exists }         → 桥接到 GM 的 asset_get_index
Registry.{ get, set, has, getAll }
Music.{ play, stop, pause, resume, setVolume }
Input.{ isDown, isConfirm, isCancel, isMenu }
Camera / Draw / Mod / love.*
```

#### 尚不支持

- `Kristal` 主引擎（`src/kristal.lua`，72KB 状态机/渲染/资源管理）未移植，当前为兼容桩
- `World` / `Stage` / `Object` 等运行时对象需要真正的渲染循环支撑
- `Game.world.cutscene` 等剧情对象未实现

---

## Neutraled Live Script（运行时 mod）

> 让 mod 只用**文本脚本**就能运行：把脚本放进游戏目录的 `Neutraled/live/`，
> **重启游戏即生效**，不需要重新部署 `data.win`（省掉 30-60 秒编译）。

---

### 1. 为什么能做到（引擎能力边界）

GameMaker **不允许**在运行时加载新代码或创建新资源，但实测（CapProbe）确认这些能力存在：

| 能力 | 实测结果 |
|---|---|
| 读文件 | `working_directory`（章节目录）/ `program_directory`（游戏根）均可读 |
| 按名解析资源 | `asset_get_index("snd_play")` → 有效索引 |
| **动态调用** | `script_execute(索引, args...)` → 真的执行 |
| 改变量 | `variable_global_set` / `variable_instance_set` 按名字读写 |
| 数据 | `json_parse` / ds_map / ds_list / 数组 / surface / buffer |

Neutraled 把**解释器**（词法→语法→求值）预编译进游戏，于是"改文本即改行为"成为可能。

---

### 2. 目录结构

```
E:\steam\steamapps\common\DELTARUNE\Neutraled\
  live\
    index.json                  <- 要加载哪些 live mod
    HelloLive\
      mod.json                  <- 元数据 + 事件绑定
      init.ntl
      frame.ntl
```

**index.json**
```json
{ "mods": ["HelloLive", "MyMod"] }
```

**HelloLive/mod.json**
```json
{
  "name": "HelloLive",
  "author": "you",
  "version": "1.0.0",
  "scripts": {
    "on_init": "init.ntl",
    "on_frame": "frame.ntl",
    "on_room_load": "room.ntl"
  }
}
```

---

### 3. 语法

```javascript
// 注释（也支持 /* ... */）
let hp = 100;              // 变量声明
hp = hp + 10;              // 赋值（支持 += -= *= /=）
name = "Kris";
flag = (hp > 100) && (name == "Kris");

if (hp >= 100) { log("满血"); } else { log("受伤"); }

i = 0;
while (i < 3) { log("i=" + string(i)); i = i + 1; }

snd_play(1, 1, 1);          // 直接调用游戏函数（见 §6）
```

**运算符**：`+ - * / %`、`== != < > <= >=`、`&& || !`、括号、字符串 `+` 拼接
**类型**：数字、字符串、数组、字典、undefined

---

### 4. 事件

| 事件 | 触发时机 | `arg` 内容 |
|---|---|---|
| `on_init` | 脚本加载后 | 0 |
| `on_ready` | 语言表就绪 | 0 |
| `on_frame` | 每帧 | 帧计数 |
| `on_draw` | GUI 绘制层 | 0 |
| `on_room_load` | 房间切换 | 新房间号 |
| `on_battle_start` / `on_battle_end` | 进入/离开战斗 | 0 |

脚本里用 `arg` 读取事件参数；`mod_name` 是本 mod 名。

---

### 5. 内置函数

**日志 / 调试**：`log(msg)` · `debug_on()` · `debug_off()` · `version()`

**全局 / 实例变量**：
`get_global(name)` · `set_global(name, value)` · `var_get(inst, name)` · `var_set(inst, name, value)`
`inst_all(obj_name)` · `inst_nth(obj_name, i)` · `inst_count(obj_name)` · `inst_first(obj_name)` · `inst_exists(inst)`

**文件 / 数据**：`file_read(path)` · `file_write(path, text)` · `json_parse_safe(text)`

**数组**：`arr_new()` `arr_push(a,v)` `arr_get(a,i)` `arr_set(a,i,v)` `arr_len(a)` `arr_pop(a)` `arr_join(a,sep)`

**字典**：`map_new()` `map_set(m,k,v)` `map_get(m,k)` `map_has(m,k)` `map_del(m,k)` `map_keys(m)`

**字符串**：`str_len` `str_sub(s,pos,len)` `str_find` `str_upper` `str_lower` `str_char` `str_split` `str_contains` · `len()`

**数学**：`floor` `ceil` `round` `abs` `min` `max` `sqrt` `power` `sign` `clamp` `lerp` `random` `irandom`

**类型**：`string()` `real()`

---

### 6. 调用游戏 / Neutraled 函数（核心能力）

脚本里写的函数名，若不在内置表里，会**按名字动态解析**：

```javascript
snd_play(1, 1, 1);              // 播放音效（返回音频句柄）
ntl_log("mymod", "hello");      // 调用 Neutraled API
ntl_goto_chapter(4);            // 直接切章节
ntl_screenshot("shot.png");     // 截图
```

支持 0–8 个参数；解析失败会记录 `[错误] 未知函数: 名字`。

---

### 7. 示例：进入房间时提示并播音效

**room.ntl**
```javascript
log("进入房间 " + string(arg));
if (arg == 76) {
    n = arr_len(inst_all("obj_ntl_core"));
    log("控制器实例数 = " + string(n));
    snd_play(1, 1, 1);
}
```

---

### 8. 限制与注意事项

- **不能定义函数**（解释器不解析 `function`）；用变量 + 多个事件脚本代替
- **不能新建资源**（精灵/声音/对象）——那类 mod 仍需编译期注入
- 执行有**预算限制**（防死循环）；`while` 最多 10 万次
- 脚本**首次执行时编译**，之后走 AST 缓存（每帧开销很低）
- 修改脚本后需**重启游戏**生效；`ntl_live_reload()` 可在运行时重载
- 中文 / UTF-8 正常支持；调试时调用 `debug_on()` 会输出解析与执行细节

---

### 9. 与编译期注入的关系

| 需求 | 方案 |
|---|---|
| 逻辑 / 数据 / 状态调整 | **Live Script**（运行时，改文件即生效） |
| 新增精灵 / 声音 / 字体 | 编译期注入（资源包） |
| 替换游戏脚本字节码 | 编译期注入（patches / references） |
| 大型资源型 mod | 编译期注入（assets: inherit 基底） |

两者共存：重型 mod 走注入，轻型 mod 走运行时。
