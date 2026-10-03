# 项目状态与测试（PROJECT）

> **本篇包含**（按顺序）：
> - [Neutraled 状态](#neutraled-状态) — 项目状态
> - [Neutraled 测试指南](#neutraled-测试指南) — 测试指南（lint / doctor / smoke）

## Neutraled 状态

> 这份文件是**部署/自检的必需文件**之一（builder/SelfTest.cs 的必需清单引用了它），
> 同时也是「现在到底能做什么」的单一事实来源。改功能时顺手更新。

### 一句话

DELTARUNE 的 Everest 式 mod 管理器 / API：mod 平等叠加、**绝不修改原版文件**、
启动始终走 Steam（保留计时与云存档）、可一键还原。

- 游戏根：`E:\steam\steamapps\common\DELTARUNE`
- 项目区：`Neutraled/`（api / builder / gui / studio / mods / live / docs / _test）
- 日志：`%LOCALAPPDATA%\DELTARUNE\Neutraled\dr-api.log`

### 已验证的能力（都有实测证据）

| 能力 | 现状 |
|---|---|
| 核心注入 / 资源导入 / patches | 引导 prepend + 控制器 `obj_ntl_core` + 事件总线 |
| GML API | 289 个脚本通过冒烟测试 |
| Lua 解释器 | 词法/语法/求值/元表/OOP/require/标准库 |
| 函数 Hook | pre/post/override；**多 mod 可 hook 同一函数并共存** |
| Live 运行时 | 改 `.lua`/`.ntl` 重启即生效，0 部署 |
| 部署缓存 | 硬链接复用；幂等命中 ~1.5 s（六章）。「幂等」= **输入签名 + 产物内容指纹**都对得上（2026-09-29 起；产物被还原/替换过会打印「[重做] …」并重建，不再假跳过） |
| Tiled 地图转换 | 122/122 全部成功 |
| 地图运行时 | 加载/绘制/碰撞/相机/玩家移动/对象交互 |
| Kristal 兼容层 | 类系统（22 基类）/对象/敌人/LOVE 桥接 |
| 外部章节 | 成品程序（Kristal / 冰封帷幕）注册为章节，窗口隐藏 + 0.7 s 回程 |
| 安装器 | `--install` / `--uninstall`，沙箱往返实测 data.win **逐字节还原** |
| 中文字体 | 内置 **OFL 开源字体（Noto Sans SC/JP/KR/Symbols 2/Emoji）离屏渲染**的字形包：**4974 字形 / 4 张位图页**（2026-09-30 全量重渲；许可见 `fonts/OFL-NOTICE.txt`）；内容级自检保证「文本 × 字体」匹配 |
| 启动性能 | 冷启动 **2.94 s** 出窗口并接管章节选择器（修复前 22.39 s） |

### 全量回归（`Neutraled/_test/`）

| 套件 | 覆盖 |
|---|---|
| `fulltest-static.ps1` | 构建 / lint / doctor / smoke / conflicts / 部署全目标 / 逐章 verify / 内容级 / 缓存 / 导入幂等 / 打包 / 沙箱安装往返 / selftest / 只读取证矩阵 / 字体回归 / CLI 守卫 |
| `fulltest-game.ps1` | 逐章真机启动 + 截图 + 日志断言（章节推断、无 Code Error）+ 控制台交互（F2/输入/Enter/Ctrl+↑/Esc） |
| `fulltest-external.ps1` | 守候进程 / 融合 exe 控制台注入 / 选外部章节 / 焦点 / F2 / 输入隔离 / 回程 |

跑法：

```powershell
powershell -ExecutionPolicy Bypass -File _test\fulltest-static.ps1
powershell -ExecutionPolicy Bypass -File _test\fulltest-game.ps1     # 需独占游戏
powershell -ExecutionPolicy Bypass -File _test\fulltest-external.ps1 # 需独占游戏
```

结果落在测试输出目录（`*-results.json` + `evidence/` 截图 + `logs/`）。

最近一次全量：**static 55/55、game 7/7、external 6/6**（2026-09-25）。

> 测试方法学：给 LÖVE / SDL 窗口合成按键时**必须带扫描码**
> （`keybd_event(vk, (byte)MapVirtualKeyW(vk, 0), 0, …)`）。`bScan=0` 的合成键会被 SDL
> 直接忽略，症状是「焦点已抢到、`focused=True`，但按键毫无反应」。

### 内容级自检（`--content-check`）

抓的是「部署全绿、游戏里却一个字都画不出来」这类事故：把**游戏真正会显示的字符集合**
（章节 = `lang/lang_en.json` 的全部字符串值；root = i18n 词条 + `chapters.json` 章节名）
与**产物字体的字形集合**求覆盖，以 `fnt_main` 为准判定。

```
ntl-builder --content-check all [--out report.json]
ntl-builder --verify --chapter chapter1     # 已并入部署后自检 (f) 项
```

事故原型：汉化把文本换成中文，基底却选成原版 → `fnt_main` 只有 95 个 ASCII 字形
→ 覆盖率 3% → 判定失败。中文基底是 3580 字形 / 100%。

### 启动性能

| 阶段 | 修复前 | 修复后 |
|---|---|---|
| 游戏第一条日志 | 2.23 s | 2.20 s |
| `[ns]` 命名空间加载完 | 21.95 s | **2.26 s** |
| `[live]` 初始化 | 21.95 s | **2.30 s** |
| 窗口可见 + 接管章节选择器 | **22.39 s** | **2.94 s** |

根因：`Neutraled/api-registry.json` 是 **1 113 960 字节 / 20 480 行**，而
`api/ntl_ns_load.gml` 用 `_txt += file_text_read_string(_f)` 逐行拼接 ——
GameMaker 的字符串 `+=` **每次复制整个累积串（O(n²)）**，20480 行累计约 **10.9 GB** 内存拷贝。

修法：

- builder 另写**运行时精简注册表 `Neutraled/ns-registry.json`**（只含 `mods` 段，
  **14 802 字节 / 534 行**，缩小 75 倍），`api/ntl_ns_load.gml` 优先读它、回退到完整版；
- 完整版 `api-registry.json` 改成**单行紧凑 JSON**（`WriteIndented = false`）——
  控制台 `api <关键词>` 命令（`ntl_console_api.gml`）读的是同一个文件，原本同样会卡 20 秒；
- 幂等跳过加**产物齐全性检查**（`Program.cs` 的 `MissingProducts`），
  否则「新产物当前不存在」不改变部署签名 → 跳过永久生效、产物永远不生成；
- 注册表生成（第 5.5 步）从「自检之后」移到**自检之前**，否则自检 (e) 读不到刚生成的文件。

测量方式（日志由沙箱重定向到 `%LOCALAPPDATA%\DELTARUNE\Neutraled\dr-api.log`）：
杀进程 → 删日志 → `Start-Process 'E:\DELTARUNE.url'` → 每 30 ms 轮询日志行数与
`MainWindowHandle`，记录每条日志出现的相对时刻。

### 已知限制

- **Kristal 主引擎**（`src/kristal.lua` 状态机）未移植，当前是兼容桩
- **Lua 未实现**：coroutine / goto / 完整 `string.format`
- **解释器有执行预算**（1 万次以内循环安全）
- **深度重编译 mod**（dojo / LM）与汉化存在索引映射缺口
- 一章只能有**一个整包 `data.win` 基底**；多基底时必须用 `--base-mod` 指定
- 部署慢的大头在 UTMT 内核（载入/写盘/编译），不是可向量化的计算；**2026-09-28 复核**：单产物内最贵的是
  `Import #1`（21%）与「控制台屏蔽 + 增强补丁 + 引导注入」（16%），但**整章墙钟的最大项是产物个数**——
  1 个主产物 + 3 个平行时间线子产物，时间线一度每次部署全量重建（见 PIPELINE.md「产物个数优化」）
- **11 个 API 函数已实现但无调用点**（`ntl_skip_intro` / `ntl_console_line_pass` / `ntl_mod_data_load` /
  `ntl_set_lang_string` / `ntl_get_lang_string` / `ntl_api_version_check` / `ntl_hotreload_check` /
  `ntl_sprite_resolve` / `ntl_res_untrack` / `ntl_res_report` / `ntl_console_help_lookup`）——
  对应地，`config.json` 的 `skip_legend` / `skip_logo` / `intro_delay` 三键与控制台 `filter` 指令目前是空转；
  `ntl_call_host` 的 119 条分派里也没有 lang 条目，mod 够不到 `ntl_*_lang_string`。

### 已修事故（工程笔记）

| 症状 | 根因 | 修法 |
|---|---|---|
| 章节文字全空白（只剩打字指示器） | 汉化整包 `data.win` 基底没被选中 → `fnt_main` 只有 95 ASCII 字形，文本却是中文 | 基底选择确定性 + `--base-mod` 转发给并行 worker + 缓存签名纳入基底 `data.win` 指纹 + `--content-check` 内容级判据 |
| 同一份 `config.json` 在章节选择器与章节内读到两套内容（选择器 `lang=en`、章节内 `lang=zh`；跳过开关只在一边生效） | **root 产物带 GameMaker 文件沙箱**：bundle 路径的**写**落到存档区 `<game_save_id>`，**读**被存档区里的同名文件遮蔽（逐文件遮蔽），而章节产物没有沙箱 ⇒ 天然两份文件 | GML：`api/ntl_config_paths.gml` 列出两处，`api/ntl_config_load.gml` 两处都读（游戏根优先）并把游戏根那份镜像回存档区，`api/ntl_config_set_lang.gml` 两处都写；CLI：`builder/ConfigFile.cs` 每次 Save 镜像到存档区（`Paths.SaveMirrorConfigPath`） |
| 控制台永远英文，`config.json` 的 `lang=zh` 无效 | `api/events/Create_0.gml` 里 i18n **早于** `ntl_config_load()` | 在 i18n 之前补一次 `ntl_config_load()`（带 `variable_global_exists` 守卫） |
| 外部章节常驻时日志每帧刷屏、45 分钟超时兜底失效 | `api/ntl_root_step.gml` 的 `if (global.ntl_ext_wait <= 0)` 归零后每帧成立；且每帧 `ntl_ext_frames = 0` | 加 `&& (… ntl_ext_running != 1)` 只转入一次 |
| `--pack --out` / `--content-check --out` 静默不落盘 | `Program.cs` 里 `--out` 被声明两次，第二个是死代码 | 删重复分支，统一用 `packsOut` |
| 开机 22.4 s（原版秒开） | `api-registry.json` 1.06 MB 被 `ntl_ns_load.gml:18` 用 `+=` 逐行拼接 → O(n²) ≈ 10.9 GB 拷贝 | 新增精简 `ns-registry.json`（14 KB）+ 完整版改单行；幂等跳过加产物齐全性检查 |
| 层的 patch（整脚本覆盖）冲掉基底对同一对象的改动（汉化基底 + 60fps 层，91 个对象重叠） | patch 内容 = 官方基线 + 该 mod 改动，直接覆盖等于把基底的改动一起丢掉 | 部署期**行级三方合并**（`builder/PatchMerge.cs`：base=官方基线 / ours=基底 / theirs=patch，按基线坐标聚类，冲突保守取基底）+ `--merge-selftest` 17 组用例；报告 `Neutraled\cache\merge-<chapter>.txt` |
| dojo 的 **150 个 `gml_GlobalScript_dj_*` 函数**被判「无法表达」，整包 mod 只能当基底 | `LayerFromBase` 只认 `gml_Script_` 前缀；子条目（`____struct___N_…`）单独反编译报 `Expected code entry to be root level` | `StripScriptPrefix` 同时认两种前缀 → 层的 `gml/<函数名>.gml`；候选循环跳过 `code.ParentEntry != null` 的子条目 ⇒ 无法表达 150 → 8（只剩新对象事件） |
| 真机进 chapter4 直接 `Code Error`（`nat-24` 跑出 `shot 01-ingame … title=Code Error`） | 做可行性取数时留下的**探针层 `mods/dojo_merge_probe` 还 enabled**，带着 34 个引用 `obj_dojo` 的 patch 一起部署 | 取数完删探针目录**再重新部署**（已删 `mods/dojo_merge_probe`、`mods/pct_probe`）；探针产物一律当临时文件 |
| 真机进 chapter1 弹 `Code Error`（`Trying to read from undefined INI file at gml_Script_scr_84_init_localization`），`--gml-check` 全绿、抓不到 | 层的 `gml/*.gml` 注入的是**代码条目的函数体**；写成 `function 名(参数) { … }` 会被当成函数定义、另立一个**空壳脚本**（`<函数名>_gml_Script_<文件基名>`，0 指令）⇒ 运行期调用落到空壳上**返回 0 且不报错**，`ini_open(0)` 静默失败后 `ini_read_*` 才炸 | 新增 `builder/ScriptUnwrap.cs`：注入前按顶层 `function` 切分，每条脚本 = 函数名 + 裸函数体（参数改写成 `var <参数> = argument<i>;`，有默认值再补 `is_undefined` 兜底）；真机探针 `t_body` / `t_wrap` / `t_inner` / `dj_path` 由「0 / 抛异常」变为全部返回正确值，空壳脚本消失 |
| 多个 mod 都带 `gml/main.gml` 时只有第一个会跑（`mods run: 1/11`，其余 mod 静默不执行） |「是否入口」判在**去重后的脚本资源名**上：第二个 mod 的 `main` 变成 `ntl_<id>_main` ⇒ `isMain=false` ⇒ manifest 里 `entry` 为空 ⇒ `ntl_run_mods.gml` 里 `_entry == ""` 直接 `continue` | `Injector.cs` 段 0 改判**源码名**（`isMain0 = string.Equals(srcName, "main", …)`，登记的入口名仍用去重后的 `scriptName0`）；复验 `mods run: 3/11`，日志同时有 `-> main` 与 `-> ntl_layer_ntldbg_diag_main` |
| 「新增对象」型 mod（dojo 的 `obj_dojo`、percentage_color 的新资源）只能整包当基底，不能叠层 | 层格式只能表达 patch（已存在条目）+ `gml/`（函数），**新增对象 / 事件 / 房间**无处安放 | 实现「新增对象 + 事件 + 房间」注入（`Injector` 建 `UndertaleGameObject` + 事件 + 新代码条目；`LayerFromBase` 写 `objects/` + `rooms/` 并在 `mod.json` 声明）：dojo ch1 层 `新增对象合计: 新建 5 / 已存在 1 / 事件 14 / 跳过 0`，真机日志 `NTLDBG7-DOJO obj=353`、`dj_open()` 不抛异常 |
| 层代码按名字引用资源，运行期静默拿到 -1（pct 的 `sh_color_filter`、dojo 的 `obj_bullet_knight_crescentGenerator`），编译期全绿、只有玩到那一步才炸 | 层只带代码不带资源：`references.assets=inherit` 只对**被选中的那一个基底 mod** 生效（其余整包 mod 只贡献代码），资源包只覆盖精灵/音效/字体、**着色器没有通道** | 新增 `builder/AssetNameGuard.cs`：部署 2.8 段按层扫 `gml/` `patches/` `objects/` `raw/`，判据 A 查 `asset_get_index("X")` 一类字面量、判据 B 用该 mod 自己的 `ref/data.win` 核对；**只告警不中止**，也可单跑 `--asset-guard <data.win> <层目录[;…]>` |
| 着色器永远进不了产物（pct 滤镜 `sh_color_filter` 运行期静默 -1、`Shaders n=0`） | 资源通道只有精灵/音效/字体，`UndertaleShader` 没有任何复制路径；`inherit` 只对选中的那个基底生效 | 新增着色器资源包 `builder/ShaderPack.cs` + `--export-shaders`（写 `shaders/*.json`，`Injector` 2.42 段导入，同名替换/新名追加）⇒ 2026-09-27 复测 chapter1 产物 `Shaders n=1 sh_color_filter` |
| 层整脚本覆盖 Neutraled 内置补丁目标（`bossrushlayer` 的 ch5 `obj_darkcontroller_Draw_0` / `_Step_0`）会冲掉设置菜单注入点 | 三方合并只覆盖「基底改动过」的目标，保留目标不在集合里 ⇒ 以前只告警、仍然整体覆盖 | `BasePatchMerger.TryCreate(..., extraTargets)` 把层碰到的保留目标也纳入合并（base=官方基线 / ours=当前产物含内置注入 / theirs=层 patch）：内置注入点保留 + 层改动生效；产物级已验证（`sdump --code` 在 chapter5 产物里同时看到我们的 `ntl_settings_row_draw` / `ntl_settings_row_press` 与层的 `scr_bossrush_active` / `scr_bossrush_townreturn`） |
| 部署慢（`--deploy --chapter chapter1 --force` 60 s、`--deploy-all --force` 163-168 s，其中大量时间是「时间线明明没变却每次部署都重建」） | 时间线产物目录的运行时文件来自源章节目录整目录复制，把源章节的 **`.ntl-deploy-<源章节>.sig` 也复制了进去**，覆盖时间线自己的部署签名 ⇒ 比对必然不一致、永远重建；且 `--deploy-all` 的 6 个 worker 各自跑一遍全部时间线 = 6 路并发抢同一产物目录 | `Program.cs` 三处：①两段复制循环跳过 `.ntl-` 前缀的账本文件；②幂等判定从 1.5) 搬到 **2.1.5)**（章节注册表之后，时间线判定要用 `LastAllMods`/`LastRegistry`），跳过分支补上时间线检查；③`--deploy-all` 给 worker 转发 **`--no-timelines`**，时间线只由父进程部署一次；新增 `--force-timelines`（`--force` 只强制重建主产物）。实测 60/61 s → **85.2 s / 21.9 s**（第二次主产物 19.3 s + 3 行时间线 `[跳过]`）、`--deploy-all --force` 163-168 s → **124.6 s** |
| `dojoobj` 层只有 chapter1（ch2-5 的 dojo 缺新对象/房间，只能当整包基底） | 抽层脚本只对 ch1 跑过，ch2-5 从没抽（缺工作量，不是缺机制） | 照 pctobj 配方对 ch2-5 各跑一次 `--layer-from-base` + `--deploy`：每章 `patches/` 43（ch2）/59（ch3）/48（ch4）/45（ch5）、`gml/` 142 个脚本、`objects/` 8 个（4 新增对象 / 8 事件）、1 个空房间；五章真机按 `D`/点 `[D] DOJO` 都进道场，`DOJO_LOGS\dojo.log` 各开出新会话 |
| 层 vs 层：多个层 patch 同一个对象，后一个整脚本覆盖吃掉前一个（ch1 的 `gml_Object_DEVICE_MENU_Step_0` 被 60fps + dojo + pct 三方争用 ⇒ dojo 的 `dj_open()` 与 60fps 的 `global.fps_scale` 都没了，按 D 进不去道场、`DOJO_LOGS\dojo.log` 不再增长） | ①争用目标不在三方合并集合里；②更根本：patch 是 `group.QueueReplace(...)` **排队**写入，队列文本在 `data.Code` 里还看不到 ⇒ 第二个层拿到的 `ours` 是「第一个层之前」的旧文本，合并结果不含前一个层的改动 | `Injector.cs` 2.5 段统计 `patchOwners`，把 `owner > 1` 的目标并入 `BasePatchMerger.TryCreate(..., extraTargets)`（日志 `层间争用: N 个对象被多个层 patch，已全部改用三方合并`）；`BasePatchMerger` 新增 `_pending[target]` **链式合并**（后续层以前一个层的合并结果作 `ours`，`_curCache`/`_vanCache` 缓存反编译）⇒ ch1 产物同一代码条目 680 → **691 行**：第 3 行 `dj_open();`（dojo）、第 638 行 `scr_cf_init();`（pct）、第 682-683 行 `ONEBUFFER -= (1 / global.fps_scale);`（60fps）三者共存；真机已复验：full-mods chapter1 点 `[D] DOJO` 方框后 dojo 正常开局（`dojo.log` 新会话 + XANZO1 启动画面，见截图 `03-after-click.png`）。2026-09-28 00:38 在 **v7 全 mod 重部署后的产物**上复验：同一个条目仍是 **691 行 / instr 2093**、第 3 行 `dj_open();` + 第 638 行 `scr_cf_init();` + 第 682-683 行 `global.fps_scale`，`dojo.log` mtime=00:38:12 size=2336 开出新会话（截图里 XANZO1 启动画面带 pct 彩色滤镜 ⇒ dojo 层 + pct 层同时生效）。⚠ 冒烟要等在存档选择页停留 18 s 再操作（开发期脚本，不随包）：若像早期脚本那样先连打 26 次 Z，chapter1 载入快会被直接选定存档冲进游戏 ⇒ 拍到的画面是章节内游玩、`dojo.log` 不增长，**看着像产物坏了其实不是**。 |

---

## Neutraled 测试指南

> **一条命令验证 NTL 是否正常。**

---

### 🚀 最快上手

```
双击  neutraled\test.bat
```

或者命令行：

```powershell
cd E:\steam\steamapps\common\DELTARUNE\Neutraled
.\test.bat                  # 快速检查（不启动游戏，约 30 秒）
.\test.bat --deploy          # 含部署
.\test.bat --full            # 完整测试（含启动游戏 + mod 联动验收，约 2 分钟）
```

---

### 📋 测试包含什么

| # | 检查 | 说明 |
|---|---|---|
| 1 | **lint** | 静态检查（17 条规则，分 `Lint.cs` / `LintRules2/3/4/5.cs` 五批） |
| 2 | **doctor** | 7 类自检（见下） |
| 3 | **smoke** | 289 个 API 冒烟测试 |
| 4 | mod 列表 | 已安装 mod |
| 5 | 存档快照 | 备份状态 |
| 6 | Kristal 验证 | 780 个脚本的兼容性 |
| 7 | 部署（可选） | `--deploy` |
| 8 | 游戏内测试（可选） | `--full` |

---

### 🔍 doctor 的 7 类检查

| # | 检查 | 抓什么 |
|---|---|---|
| 1 | **宿主函数完整性** | `ntl_lua_fn_host("__x")` 注册了但没人接 → 静默返回 undefined |
| 2 | **脚本完整性** | 文件名 ≠ 函数名 → 跨脚本调用静默失败 |
| 3 | **不可达代码** | 顶层 `return` 之后还有代码 → 永不执行 |
| 4 | **变量定义** | 用了未声明的局部变量（`_k` / `_a4` / `_a0`…）→ 运行时弹 Code Error |
| 5 | **部署一致性** | root 与章节时间差过大 → 跑的是旧代码 |
| 6 | **mod 加载** | mod.json 声明的脚本文件是否存在 |
| 7 | **空 catch** | 异常被静默吞掉 → 不报错但不工作 |

#### 为什么是这 7 类

2026-09-21 一天修了 12 个 bug，**其中 9 个是「静默失效」** ——
不报错、不崩溃，但功能就是不工作，平均每个要花 20+ 轮排查。

这 7 类检查覆盖了那 9 个 bug 的**全部成因**：

```
❌ 之前                                        ✅ 现在
─────────────────────────────────────────────  ──────────────────
ntl_lua_fn_host 注册了但 ntl_lua_host 没分支    → doctor #1 抓到
ntl_lua_fn_new 忘了登记                          → smoke 抓到
stdlib2 第 31 行提前 return                      → doctor #3 抓到
ntl_call_host 缺 _a0/_a1/_a2                     → doctor #4 抓到
ntl_cfg_row 漏 var _k = argument[0]               → doctor #4 / lint 16 抓到
ntl_love_host 只声明 _a0.._a3 却用了 _a4          → lint 16 抓到（doctor #4 同源）
ntl_modmenu_page_draw 红心写死 3695（chapter4 索引）→ lint 17 抓到（chapter1 必弹 Code Error）
只部署了 chapterN                                → doctor #5 抓到
json_encode 类型错误被空 catch 吞                → doctor #7 抓到
```

---

### 📊 当前状态（实测）

```
lint:    ✅ 没有发现问题
doctor:  ✅ 0 错误 / 3 警告 / 4 提示
         [宿主] ✅ 119 个宿主函数都有对应实现
         [不可达] ✅ 没有发现 return 后代码
         [变量] ✅ 未发现明显的未定义变量
         [mod] ✅ 25 个 mod，3 个有脚本
smoke:   ✅ 所有 API 都健康  289/289
```

#### 已知的 3 个警告（都不阻塞）

| 警告 | 说明 |
|---|---|
| 部署时间差 199 小时 | chapter1/2/3/5 是 9/13 的旧部署，属正常 |
| 37 处空 catch | 大多是有意的（mod 崩溃隔离），关键路径已有日志 |
| — | — |

---

### 🎮 游戏内测试

启动游戏后按 **F2** 打开控制台：

```
> profile        看性能（含逐 mod 耗时 + 资源统计）
> mods           看已加载的 mod 和互操作注册表
> hooks          看所有 hook（函数/内置/对象事件）
> eval 1+2       求值
> cmds           看全部 64 条命令
```

#### 自动按键测试（无人值守）

```powershell
powershell -ExecutionPolicy Bypass -File _test\autotest.ps1 `
    -Actions "f2:1500,c:150,m:150,d:150,s:150,enter:3000" `
    -Shot out.png
```

支持：`f1-f5` / `a-z` / `0-9` / `enter esc space tab backspace` / 方向键 / `shift ctrl alt`
（会自动把输入法切成英文）

---

### 🛠 单独运行某一项

```powershell
$exe = ".\builder\bin\Release\net9.0\ntl-builder.exe"

& $exe --lint                        # 静态检查
& $exe --doctor                      # 自检
& $exe --smoke                       # API 冒烟
& $exe --mod-list                    # mod 列表
& $exe --save-list                   # 存档快照
& $exe --validate-kristal <目录>       # Kristal 验证
& $exe --deploy --chapter root       # 部署（两个目标都要）
& $exe --deploy --chapter chapter4
& $exe --selftest --no-launch        # 端到端自检
```
