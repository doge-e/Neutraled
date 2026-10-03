# 转换与部署管线（PIPELINE）

> **本篇包含**（按顺序）：
> - [让 mod 走快速通道：作者适配声明（`ntl_adapt`）](#让-mod-走快速通道作者适配声明ntl_adapt) — 作者适配声明（ntl_adapt）快速通道
> - [导入传统 mod（自动转成 Neutraled 格式）](#导入传统-mod自动转成-neutraled-格式) — 导入传统 mod
> - [多个整包 mod 如何共存（三条路线）](#多个整包-mod-如何共存三条路线) — 多个整包 mod 共存的三条路线
> - [中文字体（Neutraled 自带字体包）](#中文字体neutraled-自带字体包) — 中文字体包
> - [Neutraled 部署缓存](#neutraled-部署缓存) — 部署缓存
> - [Neutraled 解释器性能优化记录](#neutraled-解释器性能优化记录) — 解释器性能优化记录

## 让 mod 走快速通道：作者适配声明（`ntl_adapt`）

> 一句话：你在 `mod.json` 里如实写下「这个 mod 用到了哪些能力」，Neutraled 就能跳过用不上的昂贵步骤。
> **声明得越充分 → 转换/部署越快**；不写就一律走最保守的完整通道（功能不受影响）。

### 为什么需要作者声明

Neutraled 无法从外部可靠判断一个 mod「有没有依赖控制台输入屏蔽」「是不是只改资源」——猜错会静默出错。
所以把这件事交给最清楚的人：mod 作者。**声明与实际不符时 Neutraled 会拒绝该条并响亮警告**，
因此谎报不会带来错误产物，只会退回完整通道（宁可慢，不可错）。

### 怎么写

在 `mod.json` 顶层加一段（全部字段可选）：

```json
{
  "id": "yourmod.you",
  "name": "Your Mod",
  "ntl_adapt": {
    "level": "fast",
    "chapter": 5,
    "assets_only": true,
    "no_hooks": true,
    "no_console_input": true,
    "notes": "2026-09-25 作者自测：快速档下通关正常"
  }
}
```

最省事的写法是只写 `"level": "fast"`（= 下面三项全开），但要**自己确认**确实成立。

### 每项声明对应什么

| 字段 | 含义 | 写对了能省什么 |
|---|---|---|
| `chapter` | 这个 mod 属于第几章（0 = root） | 导入裸 xdelta 时**跳过逐章探测**（约省 5 秒/包） |
| `assets_only` | 只改资源 / `files/` 覆盖，**不带**任何 `patches` / `find_replace` | 跳过代码补丁扫描（省得不多，但让适配程度可见） |
| `no_hooks` | 没有声明任何 `hooks` | 同上（声明性） |
| `no_console_input` | 该 mod 在「跳过控制台输入屏蔽」的快速档下功能正常 | **关键项**：当**所有**启用 mod 都这么声明时，自动跳过部署里最贵的一步 |
| `level` | `"fast"` = 上三项全开 | 便捷总开关 |
| `notes` | 备注（只记录，不参与判断） | — |

### 加速是怎么生效的（不是单方面相信作者）

1. **逐条校验**：声明 `assets_only` 却带着 patch、声明 `no_hooks` 却有 hooks、`chapter` 与实际不符 →
   忽略该条并打印 `[适配] ... → **忽略该声明**（按完整通道）`。
2. **集体判定**：`no_console_input` 只在**全部启用 mod** 都声明时才触发快速档；有一个没声明就走完整档。
3. **可覆盖**：玩家可用 `--full-deploy` 强制完整档（作者声明不会越过玩家的选择）。
4. **可看见**：每次部署打印适配报告：

```
  [适配] Your Mod: 仅资源 / 无hook / 不依赖控制台输入屏蔽
  适配汇总: 6/6 个 mod 带适配声明；其中 6 个声明可在快速档运行（其余按完整通道）
  [适配] ✅ 全部启用 mod 都声明兼容快速档 → 自动走快速通道（跳过输入重定向，约省 20 秒）
```

### 代价要说清楚

快速档跳过的「输入函数重定向」，作用是**控制台打开时让游戏忽略键盘**。
跳过之后控制台仍能正常打开使用，但打字时游戏自己的 `keyboard_check_direct` 仍会读到按键 ——
也就是**开着控制台打字可能同时操作角色**。存档安全与 mod 功能本身不受影响。

### 实测收益（chapter5，229MB 基底 + 6 个 mod，本机）

| 通道 | 耗时 |
|---|---|
| 完整档 | ~44 s |
| 快速档（全部 mod 声明） | ~39 s |
| 缓存命中（什么都没改） | 0.9 s |

> 输入重定向第一次要 ~20 秒；已按基底指纹缓存扫描结果，第二次起只要 3–5 秒，
> 所以快速档在「第一次部署」时收益最大。

### 相关命令

```powershell
ntl-builder.exe --deploy --chapter chapter5                 # 依据 mod 声明自动选通道
ntl-builder.exe --deploy --chapter chapter5 --full-deploy   # 强制完整档（忽略声明）
ntl-builder.exe --deploy --chapter chapter5 --fast-deploy   # 强制快速档
ntl-builder.exe --deploy --chapter chapter5 --self-check    # 部署后结构化自检（大产物约 +9 秒）
```

---

## 导入传统 mod（自动转成 Neutraled 格式）

> 目标：让"必须改原版文件"的传统 mod 变成 Neutraled 的 mod —— **不碰原版、多 mod 平等共存**。

### 一条命令

```powershell
ntl-builder.exe --import-mod <包目录 | zip | xdelta | data.win>
```

自动识别包形态，不需要你告诉它是哪种生态：

| 包形态 | 识别依据 | 转换结果 |
|---|---|---|
| **Modding.xml 包** | `modding.xml`（**允许多个根元素**——真实世界的野格式） | 按 xml 的 patch→to 目标分章：xdelta → `ref/data.win`，override → `files/` |
| **Deltamod 包** | `meta.toml` + `patches/` | `files/` | 文件名/父目录推断章节 |
| **mod_config.json 包** | `mod_config.json` 的 `files[*].data_file_path` | 按 key（`deltarune_4`）/ 目录（`chapter_4`）分章 |
| **裸 xdelta** | `*.xdelta` | 文件名→父目录→**逐章探测**（试官方基线，成功即采用） |
| **裸 data.win** | `data.win` | 按**体积接近度**与各章官方基线比对猜章节（打印全部候选与差异百分比） |
| **Kristal mod** | `mod.lua` / `engineVer` / `scripts/`+`assets/` | 转交 `--import-kristal`：精灵/声音自动转换，Lua 逻辑列出待手工迁移清单 |
| **原生 Neutraled mod** | `mod.json` | 转交 `--mod-install` |

#### 一条龙：直接从 GameBanana 下载并转换

```powershell
ntl-builder.exe --fetch-mod <gamebananaId> [--model Mod|Wip] [--dry-run]
```

```
===== 下载并导入 GameBanana mod 578012 =====
  [0] storynarrators_test.zip  (10537 KB)  MOD TESTING
  [1] storynarrators.zip       (10471 KB)  ONLY LIBRARY
  （多个文件时取最大的那个作为主包）
  下载: storynarrators_test.zip  10537 KB
    curl 第 1 次: 10537 KB / 10537 KB
  → 检测到 Kristal 结构（mod.lua / engineVer / scripts+assets）：转交 --import-kristal
```

**为什么下载走 `curl.exe` 而不是 .NET HttpClient**：本机实测 HttpClient 下载路径不可靠
（带系统代理时 0 字节卡住、禁用代理时 API 挂死），而 curl 直连正常。
所以下载通道是 `curl.exe` + 浏览器 UA + Referer + **断点续传**`-C -` + **体积校验** + 最多 4 次重试；
体积不符时**拒绝导入残缺包**（不是硬着头皮解压一个坏 zip）。API（搜索/文件列表）仍走 HttpClient。

**注意**：`Kristal` 在 GameBanana 上是**独立游戏条目**（game id **16251**），不在 DELTARUNE(6755) 下。

#### 参数

| 参数 | 作用 |
|---|---|
| `--dry-run` | 只分析不写盘：打印分章计划 + 校验和预检 + 资源差异 |
| `--name <名>` / `--author <作者>` | 覆盖包内元数据 |
| `--chapter <ch>` | 章节推断不出来时强制指定（对"裸 data.win"尤其有用） |
| `--mod-force` | 校验和与官方不符时仍强制尝试 |

#### 版本预检（重要）

`meta.toml` / `meta.json` 里的 `[[neededFiles]]` 校验和会与游戏目录下的**官方备份** `backup/` 比对：

```
--- chapter5 ---
  校验和 ✓（chapter5_windows/data.win 与清单期望一致）
  基线: chapter5_windows/data.win
  应用 xdelta: chapter5.xdelta
  产物: 157 MB  对象=1739 代码=18399 精灵=8538 声音=765
```

不符时会明确说"补丁基于其它游戏版本"并跳过该章，而不是让 xdelta 抛一句看不懂的错。

### 产物格式

```
mods/<name>/<author>/<chapterN|root>/
    mod.json            ← id/name/author/version + references{source, assets}
    ref/data.win        ← 整包 data.win（references.source，作为该章基底）
    files/**            ← override 资源（原样覆盖到章节目录）
mods/<name>/<author>/.ntl-import.json   ← 导入记录（来源/格式/包 id/各章结果）
```

### 共存规则（一章只能有一个 data.win 基底）

一个章节**只能有一个整包 data.win 基底**。多个 mod 都声明 `references.assets=inherit` 时，
Neutraled 会**响亮报告**而不是静默丢弃：

```
[2/5] [警告] 4 个 mod 提供整包 data.win，本章只能用 1 个基底：
        ✔ 生效  converted.deltarune_60_fps.badartadventure
        ✘ 被忽略  converted.deltarune_but_it_s__percentage___color.steccca6663
        ✘ 被忽略  converted.dojo.xanzo1
        ✘ 被忽略  converted.hanhua.hanhuazu
        → 想换基底：--base-mod <id>；其余 mod 的脚本/资源/files 覆盖仍会生效
```

同一份冲突也会写进 `conflicts.json`（GUI 可读）。

#### 什么能叠加，什么不能

| 能力 | 能否多 mod 共存 |
|---|---|
| Lua 脚本 / hooks（pre/post） | ✅ 任意多个（同一函数也行，依次执行） |
| `gml/` 脚本、`references.codes` | ✅ 不同代码对象可共存；改同一对象需 `references.override` |
| `files/**` 外部文件覆盖 | ✅ 任意多个（按加载顺序后写覆盖） |
| 精灵/声音/字体资源包 | ✅ 任意多个 |
| **整包 data.win** | ❌ **每章只能一个**（用 `--base-mod` 指定） |

### `--extract-diff`：把整包 mod 抽成"差异层"（**会拒绝不安全的情况**）

```powershell
ntl-builder.exe --extract-diff <mod 的 ref/data.win> --chapter chapter5 --name BossRush --author PrimeStrat
```

它会对比官方基线，把"这个 mod 改了哪些代码对象"抽成 `references.codes` 层，从而叠加到任意基底上。

**但真实世界的 Deltamod / GM3P 包大多会被拒绝**，因为它们在打包时**重建了整个资源池**：

```
[拒绝生成] 该包在打包时**重建了资源池**，字节码里的资源索引不可移植：
  字符串 93412→93690 / 函数 6902→6910 / 变量 42836→43088 / 对象 1735→1735
  受影响指令示例（索引位移）:
    gml_GlobalScript_scr_rhythmgame_beatcheck: [2] ValueInt -1610610896->-1610610900
    gml_GlobalScript_c_soundplay_wait: [0] TypeInst 28->27
```

`push.bltn` / `call` / `B` 这类指令里的资源索引（函数/变量/字符串/内置变量）整体位移了。
把这种代码对象复制进别的基底，索引会指向**错误的资源** —— 功能静默错乱，比不合并更糟。
所以工具**拒绝生成**（`--mod-force` 可强制，自担风险）。

这不是偷懒，是**不产出坏层**。正确做法是把它作为该章基底，其余用脚本/资源包/files 叠加。

### 实测结果（8 个真实 GameBanana mod，2026-09-23）

| mod | 形态 | 结果 |
|---|---|---|
| dojo_mod v0.1.0 | modding.xml + meta.toml + patches/ + files/ | ✅ 6 章全转（root+ch1-5），162 个 override 文件，6/6 校验和 ✓ |
| battlefrontierbossrush v1.3.0 | modding.xml（含 `<?xml?>` + `<patches>` 根） | ✅ chapter5 |
| deltarune_60fps v2.0.0 | modding.xml（**多根元素**） | ✅ 6 章全转 |
| Deltarune [percentage] [color] v1.3.1 | mod_config.json + `chapter_N/` | ✅ 5 章全转 |
| blockless_mantle_holder_arena | **裸 data.win**（126 MB） | ✅ 体积判定 chapter3（差异 0.00% vs 次选 4.36%） |
| friend_inside_yellow | 裸 xdelta（无清单） | ✅ **自动探测出 chapter5**（与 README 一致） |
| ice-e_eram_mod | 裸 xdelta + DeltaPatcher | ✅ **自动探测出 chapter3**（与 INSTRUCTIONS 一致） |
| everyitemmod 4.1 | UTMT `.csx` C# 脚本 | ⛔ 明确诊断 + 给出两步可行路径（不假装支持） |

#### Kristal 生态（GameBanana game **16251**，与 DELTARUNE 分开）

| 包 | 类型 | 结果 |
|---|---|---|
| KanaCole v1.0.0 | Kristal **项目**（mod.json + mod.lua + assets/ + scripts/ + libraries/） | ✅ 转成 `~Chapter:1:KanaCole` 独立章节（7 精灵 / 1 声音）+ 打印 18 个待手工迁移的 Lua 脚本清单 |
| StoryNarrators v1.0.0 | Kristal 项目 | ✅ 转成 `~Chapter:1:StoryNarrators Test`（7 精灵 / 1 声音）+ 转换报告 |
| Flowery Party Member | Kristal **插件型 mod**（只有 scripts/ + assets/） | ⛔ 明确说明：必须挂在 Kristal 项目下运行，不能独立转换（不假装支持） |
| X-Slash Recreation Library | Kristal 库（lib.lua + lib.json） | ⛔ 库文件，需要宿主项目 |

**两类的区别很重要**：Kristal **项目**有自己的 mod.json（chapter/map/party），能转成 Neutraled 的独立章节；
Kristal **插件 mod** 只是往宿主项目里塞内容，脱离项目没有意义 —— 工具会直接说清楚，而不是产出一个永远不会生效的壳。

> Kristal 的 Lua 逻辑（`scripts/**/*.lua`）**不能自动转成 GML/NTL**（语言不同）。
> 转换报告会逐条列出待迁移文件；资源（精灵/声音）已自动转换，可直接在游戏里用。

### 常见问题

**Q：为什么 `--import-mod` 说我的包"不能自动转换"？**
A：包里只有 UTMT `.csx`/`.dll` 这类需要在 UndertaleModTool 里运行的注入脚本 —— 没有成品 `data.win`。
按提示用 UTMT 跑一次得到 `new.win`，再 `--import-mod new.win --chapter chapterN` 即可。

**Q：导入后 mod 没生效？**
A：看部署日志的 `[2/5] [警告] N 个 mod 提供整包 data.win` —— 很可能它被别的基底顶掉了。
用 `--base-mod <id>` 指定它当基底，或把它改成脚本/hook/资源包形态。

### Kristal 项目 → 可玩章节（外部引擎章节）

#### 为什么不能直接跑

Kristal 是 **LÖVE 工程**（`main.lua` + `conf.lua` + `data/`），**没有 GameMaker 的 data.win**，
在 DELTARUNE 的运行时里根本加载不了。移植引擎（`src/kristal.lua` 那套状态机）不现实。

#### 采用的方案：外部引擎章节

```
章节选择里选中它
   → 游戏写 Neutraled/launch-request.json（exe / args / cwd / name）
   → 游戏退出（game_end）
   → GUI 起的守候进程（--watch-external）看到请求
   → 拉起 LÖVE + Kristal 引擎跑那个项目
```

**好处**：插件加载、进度保存、通关判定**全部是 Kristal 原生行为**，一点都不用仿。

#### 为什么绕这么一圈（硬限制）

实测这个 GameMaker 运行时**没有任何启动进程的内置函数**：`execute_program` / `execute_shell` /
`os_start_process` / `url_open` 在 data.win 字符串池里**一个都不存在**，所以 GML 自己拉不起程序。

#### 用法

```powershell
# 1) 把 Kristal 引擎放到 <游戏根>\Kristal-main，LÖVE 运行时放到 Neutraled\tools\love\
#    2) 导入 Kristal 项目（自动登记外部章节 + 把项目装进引擎的 mods/）
ntl-builder.exe --import-mod <Kristal 项目目录> --name <名字> --author <作者>
# 3) 部署（会生成 Kind=external 的章节条目，并把项目复制进 <引擎>\mods\）
ntl-builder.exe --deploy --chapter root
# 4) 从 GUI 点「部署并启动」（GUI 会同时起守候进程），进游戏后在章节选择里选它
```

导入产物：

```
mods/<名字>/<作者>/chapterN/
  ├── mod.json              含 kristal_external { exe, args, cwd, project, engine_mods, mod_name }
  ├── kristal/              原项目完整副本（自包含、可随 mod 搬走）→ 部署时装进引擎 mods/
  ├── sprites/ sounds/      资源包（也能叠加到别的基底上）
  └── CONVERSION.txt
```

#### 实测记录（2026-09-25）

```
[root] 章节注册表已加载: 8 条，最大章节 7
[auto] 自动启动外部章节: timeline:1:kristal_kanacole:kanacole
[ext]  请求内容回读: 226 字符
[ext] 外部章节「Kanacole」: 已写出启动请求，退出游戏交给启动器
▶ 外部章节「Kanacole」→ 启动 ...\Neutraled\tools\love\love.exe ...\Kristal-main
✅ 已启动
DELTARUNE exited: True      ← 游戏正常退出
KRISTAL RUNNING: pid=2196 title=Frostveil   ← Kristal 跑起来了
```

章节选择界面：官方第 1-7 章在第 1 页，**Kristal 章节在第 2 页**（右上角 `1 / 2`）。

---

## 多个整包 mod 如何共存（三条路线）

> 背景：`data.win` 型 mod 之间天然互斥 —— 一章只能有一个基底。
> 本文给出三条**实测过**的共存路线，以及各自的边界。

### 路线 1：基底（最简单，但每章只能一个）

`references.assets = inherit` + `ref/data.win` → 它就是这一章的基底。
多个 mod 抢基底时 Neutraled **响亮报告**（写进 `conflicts.json`），用 `--base-mod <id>` 指定谁当基底。

其余 mod 的 **脚本 / Lua hook / GML patch / 资源包 / `files/` 覆盖** 仍然照常生效 ——
这才是「功能不冲突的 mod 全部同时运行」的主力。

### 路线 2：源码级差异层 `--layer-from-base`（推荐）

```powershell
ntl-builder.exe --layer-from-base <mod 的 ref/data.win> --chapter chapter5 --name BossRush --author PrimeStrat [--dry-run]
```

#### 为什么不用字节码层

字节码层（`--extract-diff`）会死在**资源池索引不可移植**上：Deltamod / GM3P 打包会重建资源池，
实测 60fps 的字符串池 +278 / 函数池 +8 / 变量池 +252，所有索引型操作数全体位移。

#### 本路线的三步

1. **指令级差异** → 候选集合（快，但含大量索引位移噪声）
2. **反编译候选对象，比较反编译文本** —— 索引位移不会改变反编译文本（索引已被解析成名字），
   只有真实语义改动才会 → 噪声自动过滤
3. 真实改动的对象 → 导出成 `patches/*.gml` + `mod.json` 的 patches 段；
   **新增函数**（`gml_Script_*` / `gml_GlobalScript_*`）→ 导出成 `gml/<函数名>.gml`
   → 部署时由**目标基底自己重新编译**，索引由目标分配，索引问题彻底消失

#### 实测数字（chapter5，官方基线）

| mod | 指令级候选 | 纯元数据差异（已排除） | 真实改动 | 索引噪声 | 反编译失败 |
|---|---|---|---|---|---|
| deltarune_60fps | 6040 + 52 新增 | 12016 | **1796** | 4288 | 8 |
| friend_inside_yellow | 2037 | 16021 | **5** | 2032 | 0 |
| battle_frontier_boss_rush | 4295 + 6415 新增 | 13763 | 5367 | 4240 | 1103 |

#### ⚠ 能力边界：改代码 + 加函数 + **新增对象/事件/房间**都能分层（2026-09-27 起）

BossRush 的 5367 个"真实改动"拆开看是这样：

| 类别 | 数量 | 能否用层表达 |
|---|---|---|
| **改动**已存在的对象（反编译成功） | **55** | ✅ 写进 `patches/` |
| **新增**对象/事件/房间（`gml_Object_*`、`room_*`） | **5312** | ✅ 写进 `objects/` + `rooms/`（见下「新增对象 / 事件 / 房间注入」）；**精灵图等资源仍要 `--export-packs`** |
| 反编译失败 | **1103** | ❌ 拿不到源码 |

**两种函数前缀都能加**（2026-09-27 修）：`gml_Script_<名>` 与 `gml_GlobalScript_<名>` 都路由进层的
`gml/<名>.gml`（`LayerFromBase.StripScriptPrefix`）。修之前后者被判"无法表达" —— dojo 的
**150 个 `dj_*` 函数全部是 `gml_GlobalScript_`**，所以这条路由是 dojo 能否层化的分水岭。
同一次还修掉：`gml_Script____struct___N_…` 这类**子条目**（`code.ParentEntry != null`）不再单独反编译
（单独反编译报 `Expected code entry to be root level`），其源码由父条目内联输出。

实测（dojo，chapter1）：`指令级候选: 改动 633 + 新增 151 = 784（另排除 660 个纯元数据差异、577 个子条目）`、
`源码级过滤: 真实改动 187 / 索引噪声 596 / 反编译失败 1` ⇒ 产物 **patches 34 + gml 142 + 无法表达 8 + 撞车 3**；
「无法表达」从 **150 掉到 8**，且这 8 个全是**新增对象事件**（`gml_Object_obj_dojo_Create_0` / `_Draw_0` / `_Other_4` /
`_Step_0` / `obj_dojo_act_projectile_Step_0` / `obj_dojo_arena_back_Draw_0` / `obj_dojo_back_Create_0` / `_Draw_0`）；
反编译失败从 191 掉到 1（`gml_Object_obj_dojo_Draw_64 ← 反编译结果为空`）。
抽检 `gml/dj_act_timing_profile.gml`（1381 字符）内容完整（`function dj_act_timing_profile(arg0, arg1) { switch (arg0) { case "timed_attack": return { beats: 1, span: 44, perfect: 2, good: 6 }; … } }`）
⇒ 父条目反编译把嵌套 struct 一起带出来了。

同样的形状出现在 percentage_color（chapter1：`真实改动 117 / 索引噪声 670 / 反编译失败 2` ⇒ 12 patches + 94 gml + **无法表达 6**）。

2026-09-27 补齐：`mods\pctobj\probe` 已抽出 **chapter1-5** 五层（各章 `patches` 12 / 6 / 6 / 38 / 7，`gml` 94-97，`objects` 6），五章产物都含 pct 的新对象 `obj_colorfiltermenu`（5 个事件：`Create_0` / `Draw_75` / `KeyPress_27` / `Other_5` / `Step_0`）与整套 `scr_cf_*` 脚本；
逐章真机复验（开发期逐章冒烟脚本，不随包；带 `-Chapter N`，日志 `ch<N>-shots\run-log.txt`）：**5/5 章节正常启动、无 Code Error**，ch3 传说卡显示中文「光与暗相互调和，」，ch5 存档界面显示 pct 皮肤「DELTARUNE BUT IT'S COLORFUL!」⇒ 层的代码 + 新对象在真机生效。

**结论（2026-09-27 更新：已实现）**：`--layer-from-base` 现在能表达三类改动 ——
① **修改**现有逻辑（`patches/`）；② **加函数库**（`gml/`，`gml_Script_` / `gml_GlobalScript_` 两种前缀都认）；
③ **新增对象 / 事件 / 房间**（`objects/` + `rooms/`：`Injector` 建 `UndertaleGameObject` + 事件条目，
把 GML 编译进新代码条目；`LayerFromBase` 把新对象/事件/房间写进 `mod.json` 声明）。
于是 dojo 的 `obj_dojo` 入口对象与 `room_dojo` 可以当成**层**叠在汉化基底上，不必再整包当基底；
percentage_color 的新增部分同理（**精灵图等资源仍要靠 `--export-packs`**，层只带代码）。

实测（dojo，chapter1）：部署日志 `新增对象合计: 新建 5 / 已存在 1 / 事件 14 / 跳过 0`；
真机日志（`%LOCALAPPDATA%\DELTARUNE\Neutraled\dr-api.log`）`NTLDBG7-DOJO obj=353` ⇒ 新增对象在运行期
能被 `asset_get_index("obj_dojo")` 取到、事件代码确实跑在游戏里。

实测：把 bfr 停用、只留它的 55 个 patch 层时，chapter5 的产物从 **229 MB 掉到 158 MB** —— 它的 1851 个精灵没了。
资源部分要靠 `--export-packs` 补（见下）。

> friend_inside_yellow 表面上看改了 2037 个对象，实际只改了 **5 个** —— 其余全是重打包产生的索引噪声。

产物：`mods/<name>/<author>/<chapter>/{mod.json, patches/*.gml, gml/*.gml, objects/*.gml, rooms/, LAYER_REPORT.txt}`；
报告里有**改动样例**（基线行 → 改后行）供人工核对，以及反编译失败清单。

#### ⚠ 层的新脚本必须是**裸函数体**（`ScriptUnwrap`，2026-09-27 修）

`gml/<名>.gml` 注入的是「代码条目的**函数体**」，不是一份 GML 源文件。写成
`function 名(参数) { … }` 会被 GameMaker 当成**函数定义**、另立一个空壳脚本（脚本名
`<函数名>_gml_Script_<文件基名>`、指令数 0），运行期 `dj_path("true_config.ini")` 之类调用就落到空壳上
**返回 0 且不报错**，再往下 `ini_open(0)` 静默失败 ⇒ 下一次 `ini_read_*` 抛
`Trying to read from undefined INI file at gml_Script_scr_84_init_localization`（Code Error 对话框；
`--gml-check` 抓不到，纯运行时错误）。

实测（真机探针，同一个诊断层放三个脚本）：

| 写法 | 修前 | 修后 |
|---|---|---|
| `return "BODY:" + string(argument0);`（裸体 + `argument0`） | `t_body=[BODY:x]` ✅ | 同 ✅ |
| `function t_wrap(arg0) { … }`（包裹，函数名 == 文件基名） | `t_wrap=[0]` ❌ + 空壳 `t_wrap_gml_Script_t_wrap` | `t_wrap=[WRAP:y]` ✅ |
| `function t_inner(arg0) { … }`（包裹，函数名 ≠ 文件基名） | 调用直接抛异常 ❌ | `t_inner=[MM:z]` ✅ |

修法：`builder/ScriptUnwrap.cs` 注入前把源码切成**每个顶层 `function` 一条脚本**（脚本名 = 函数名），
函数头换成 `var <参数> = argument<i>;`（有默认值时补 `if (is_undefined(<参数>)) { <参数> = <默认值>; }`）；
先把字符串/注释掩成空格再找 depth==0 的 `function`，定义外的顶层代码按文件基名保留一条。
dojo 的 142 个 `dj_*` + percentage_color 的 94 个脚本因此全部落地（部署日志 `脚本剥壳: 238 个文件 / 252 条脚本`），
`dj_path("true_config.ini")` 现在返回真实路径。`Neutraled/api/*.gml`（`ntl_log` 等）本来就是裸体形态，不受影响。

#### ⚠ 多个层都带 `gml/main.gml` 时，每个都要有自己的入口

层的入口约定是 `gml/main.gml`；注入时脚本资源名要去重（第二个 mod 的 `main` 变成 `ntl_<id>_main`），
旧代码把「是否入口」判在**去重后的名字**上 ⇒ 只有第一个 mod 的 main 会被登记，其余 mod 的 `entry` 为空、
`ntl_run_mods.gml` 里 `_entry == ""` 直接跳过（实测 `mods run: 1/11`，日志只有一个 `-> main`）。
2026-09-27 改为按**源码名**判定（`Injector.cs` 段 0），现在 `mods run: 3/11`，日志同时出现
`[mod] load layer.60fps_layer.badartadventure -> main` 与 `[mod] load layer.ntldbg.diag -> ntl_layer_ntldbg_diag_main`。

#### ⚠ 层只带代码不带资源：`--asset-guard` 资源名守卫（2026-09-27 起）

层的 `gml/` / `patches/` / `objects/` 里都是**代码**；精灵、音效、着色器这些资源不进层。
代码按名字引用资源时，若产物里没有这个名字，两种写法的表现完全不同：

- **裸标识符**（`spr_heart`）：编译期 unknown identifier，部署直接失败 —— 看得见；
- **字符串形式**（`asset_get_index("sh_color_filter")`）：编译期一路通过，运行期静默返回 **-1** ——
  特效、声音悄悄没了，或者要等某个房间、某个 boss 才炸。

部署时（`Injector` 2.8 段，产物资源集合定型之后）自动跑这条守卫，也可以单独跑：

```
ntl-builder.exe --asset-guard <data.win> "<层目录[;目录…]>"
```

两条判据：

- **字符串型（无假阳性）**：`asset_get_index` / `sprite_exists` / `sound_exists` / `audio_exists` /
  `font_exists` / `background_exists` / `shader_exists` / `script_exists` / `room_exists` / `object_exists` …
  的字面量参数不在产物里 ⇒ 报出名字与 `文件:行`；
- **声明型**：mod 自带 `ref/data.win`（整包 mod / 转换产物，≤128 MB）时，代码里「像资源名（`spr_` `snd_` `obj_`
  `sh_` … 前缀）+ 产物里没有 + 该 mod 自己的 data.win 里确实有」的标识符 ⇒ 报出
  「该 mod 的 ref/data.win 里有这个名字」。纯层（没有 ref/data.win）不做这条，
  否则会把局部变量、函数名当成资源名报出来。

**只告警，不中止**（资源缺失往往是 mod 自己的选择，不是部署错误）。实测（2026-09-27，chapter1 产物）：

```
    [警告] layer.dojoobj.probe: 代码引用了产物里不存在的资源名 1 个
        obj_bullet_knight_crescentGenerator ← gml/dj_signature_start.gml:67（asset_get_index 字面量）
    [警告] layer.pctobj.probe: 代码引用了产物里不存在的资源名 4 个
        sh_color_filter ← gml/scr_cf_refresh_shader_assets.gml:3（asset_get_index 字面量）
        sh_picker_wheel ← gml/scr_cf_refresh_shader_assets.gml:4（asset_get_index 字面量）
  资源名守卫: 2 个 mod / 5 个资源名（**只告警不中止**）
```

这两条都是真的：`sh_color_filter` 是 percentage_color 的**滤镜着色器**（`gml/scr_cf_draw_composite.gml:52 shader_set(…)`），
它只存在于 pct 自己的 `ref/data.win`（`sdump --res` 数到 `Shaders n=1`），而**产物里 `Shaders n=0`** ——
因为 `references.assets: inherit` 只在「这个 mod 当基底」时生效（见「共存规则」）。
`obj_bullet_knight_crescentGenerator` 则是 dojo 在 `pattern == "knight_crescents"` 分支里**没有守卫**的
`instance_create_depth(480, 160, 0, asset_get_index("…"))`（连 dojo 自己的 chapter1 data.win 里都没这个对象，属上游遗留）。

**不误报拼接出来的名字（2026-09-27 修）**：`asset_get_index("spr_" + checkstring + "_idle")` 这种运行期才成形的写法，
字面量只是名字片段，守卫跳过并在汇总里报出跳过数量（`（另有 7 处拼接出来的名字，如 "spr_" + x，运行期才成形，不判）`）。
判据是「字面量紧邻（跳过空白后）`+`」，实现见 `builder/AssetNameGuard.cs` 的 `IsConcatenated`。
修之前 `60fps_layer` 的 chapter2/3/4 会被误报成缺 `spr_` / `snd_speak_and_spell_`（`patches/gml_Object_obj_spritecomparer_Draw_0.gml:24` 等）。

要真正把资源带进产物：`--export-packs`（精灵 / 音效 / 字体，见「路线 3」）；**着色器现在也能随层/包带走**（`shaders/`，见下一节）。

#### ✅ 着色器资源包 `shaders/`（`--export-shaders`，2026-09-27 起）

`--export-packs` 只覆盖精灵 / 音效 / 字体，**着色器（`UndertaleShader`）原先没有任何通道** —— 非基底的整包 mod 只能贡献代码，
于是 percentage_color 的滤镜始终拿不到 `sh_color_filter`（运行期 `asset_get_index` 返回 -1，滤镜静默失效）。

现在着色器也是一种资源包：目录 `<mod 章节目录>/shaders/*.json`，部署时由 `builder/Injector.cs` 的 **2.42 段**
（内置字体包之后、2.45 references 复制之前）导入，日志打在 `着色器资源包: <mod id>` 之后。

```
ntl-builder.exe --export-shaders "<源 data.win>" "<输出目录（mod 章节目录）>" [--base "<官方基线 data.win>"]
```

- 带 `--base` 时只导出「基线没有 / 与基线不同」的着色器（与 `--export-packs` 同款语义）；不带就全导。
- JSON 字段对齐 UTMT 的 `ImportShaders.csx`：`Name` / `Type`（`GLSL_ES` / `GLSL` / `HLSL9` / `HLSL11` / `PSSL` / `Cg_PSVita` / `Cg_PS3`）/ 六份源码串 /
  `VertexShaderAttributes` / 八份原始字节的 base64（`HLSL11_VertexData` 等，`null` = 该平台无数据）。
- 导入时**同名替换、新名字追加**：`着色器导入: 新增 1 / 替换 0 / 跳过 0` → `着色器导入合计: 1`。
- 字符串必须重映射（`sh.Name = data.Strings.MakeString(def.Name)`）—— `UndertaleString` 是每个 data 文件各自的，不能跨文件搬。
- 新的 `shaders/*.json` 会自动让部署缓存失效（`Cache.cs` 的 `AppendModDirFingerprint` 把 mod 目录所有文件的相对路径+大小+时间都算进签名）。

实测（2026-09-27）：percentage_color 的层已带 `shaders/`（chapter1 只有 1 个 `sh_color_filter`——pct 的 ch1 包里就只有这一个；
chapter2-5 各 6 个：`shd_hue` / `shd_grayscale` / `shd_dissolve` / `shd_fade` / `shd_hsl` / `sh_color_filter`），
部署后 `sdump --res sh_` 在 chapter1 产物里数到 `Shaders n=1 sh_color_filter`（改前 `n=0`），守卫对 pct 的告警 4 → 3 ——
剩下的 `sh_picker_wheel` / `sh_ui_panel` / `sh_ui_blur` 连 pct 自己的包里也没有（全工程只被赋值、没有使用点，属上游遗留），不影响滤镜。

> 工具：`sdump`（开发期工具，不随包）现有三种用法 —— `sdump <data.win> [关键字]`（脚本/代码条目名与重名、空壳检查）、
> `sdump <data.win> --res [关键字]`（26 类命名资源的数量与命中名）、
> `sdump <data.win> --code <条目名> [关键字]`（**反编译某个代码条目并逐行打印/过滤**，2026-09-27 加；
> 注意 `--probe-code` 只给指令级取证、`sdump <data.win> <关键字>` 只匹配条目名，都看不到源码，源码级验证要用 `--code`）。
> 实现要点：`GlobalDecompileContext` 在 **`UndertaleModLib.Decompiler`** 命名空间（不是 `Underanalyzer.Decompiler`，那个只提供 `DecompileContext` / `IDecompileSettings`），
> sdump 的 csproj 加了 `<Reference Include="Underanalyzer"><HintPath>…\builder\bin\Release\net9.0\Underanalyzer.dll</HintPath></Reference>`。
> csproj 还把 `OutputPath` 直接指向 `E:\steam\steamapps\common\DELTARUNE\Neutraled\builder\bin\Release\net9.0\`（`AppendTargetFrameworkToOutputPath=false`），
> 所以跑法固定是 `dotnet "…\Neutraled\builder\bin\Release\net9.0\sdump.dll" <data.win> --code <条目名> [关键字]` —— **data.win 在前、`--code` 在中间**；
> 顺序写错（`--code` 当第一个参数）会报 `Could not find file 'E:\--code'`，别误判成"dll 是旧版"。
#### ⚠ 层不许覆盖 Neutraled 自己补丁的对象（`patch_skip`）

Neutraled 的菜单 / 文本注入是**文本级 find/replace**，注入点是一批固定对象（`obj_darkcontroller` 的
`Draw_0` / `Step_0`、`obj_writer_Draw_0`、`obj_custommenu_Create_0`、`obj_savemenu_Draw_0`、
`scr_texttype`、`snd_play` / `snd_init` / `mus_loop` / `mus_play`）。
`--layer-from-base` 产出的 patch 是**整脚本覆盖**，一旦盖住这些目标，Neutraled 的注入就落在被丢弃的旧文本上。

实例（2026-09-27，60fps 层）：`gml_Object_obj_darkcontroller_Draw_0` / `_Step_0` 被层覆盖后，
我们自己的 find/replace 再往里插一行、再搬行 ⇒ 产物里 `draw_text(_xPos, yy + N)` 出现
`395:4`（4 行叠在同一个 y）与 `360:2`，CONFIG 页同时冒出层的 `Mod Settings / Game Speed / Fast Text Skip`
与我们的 `ntl_settings_row_draw(`。

修法（builder 已内置，两边都改）：

- `Injector.ReservedPatchTargets`（10 个目标）+ `Injector.IsReservedPatchTarget(string)`：patch 阶段命中就打印
  `[警告] {mod}: {target} 是 Neutraled 内置补丁目标…（应改用 patch_skip）`。
- `--layer-from-base` 命中保留目标时**不再产出 patch 文件**，改写入 mod.json 的 `patch_skip`，
  报告与控制台各给一条 `[撞车]` 提示。
- 代价：层对这些对象的改动**不生效** —— 60fps 自己的 CONFIG 页（Game Speed / Fast Text Skip / 摇杆平滑 / 360 Gerson）
  进不去了，只能保持默认（帧率 60 由层保留的 `__init_global` patch 提供）。

实测（2026-09-27，60fps 六章层，patch 数 ch1 422 / ch2 1589 / ch3 1841 / ch4 1717 / ch5 1749 / root 8）：
`patch_skip` 之前产物里有两套菜单文案；之后 `Mod Settings / Game Speed / Fast Text Skip / Overworld Stick`
**全部消失**，行分布回到 `150 / 185 / 220 / 255 / 290` + `360`（Return to Title）+ `395`（Back），
而 60fps 本体仍在（`room_speed = 60`、`fps_scale`、`scr_fps_lerp_rate(`）⇒ 该分的分开了。
**补丁（2026-09-27）：已经产出过这类 patch 的层不再只能告警。** `--layer-from-base` 现在会跳过保留目标，但手写或早期版本产出的层里
可能已经带着它们（`layer.bossrushlayer.primestrat` 的 chapter5 就整脚本覆盖了 `gml_Object_obj_darkcontroller_Draw_0` / `_Step_0`，各 ~60-70 KB）。
这种情况改为**三方合并**：`BasePatchMerger.TryCreate(..., extraTargets)` 把「层真的碰到的保留目标」纳入合并集合
（base = 官方基线 / ours = 当前产物（已含 Neutraled 内置注入）/ theirs = 层 patch），冲突仍保守取 ours ⇒
**内置注入点保留、层自己的改动也进去**，日志：`[合并] {mod}: {target} 是 Neutraled 内置补丁目标，已改用三方合并（内置注入点保留）`。
（没有 `gameRoot` / `chapter` 或缺少官方基线备份时退回原来的整体覆盖 + `[警告] … 应改用 patch_skip`。）

产物源码级验证（2026-09-27，chapter5，`sdump <data.win> --code <条目名> [关键字]`）：
`gml_Object_obj_darkcontroller_Draw_0`（1952 行）里第 161 行是 `ntl_settings_row_draw(_xPos, _selectXPos, (yy + 325) - _ntl_cfg_off);`，
第 299 / 338 行是层的 `draw_set_color((room == 54 || scr_bossrush_active()) ? 8421504 : 16777215);`；
`gml_Object_obj_darkcontroller_Step_0`（1805 行）里第 245 行 `ntl_settings_row_press()`、第 482 行 `ntl_modmenu_page_step()`，
第 333 / 367 行是层的 `scr_bossrush_townreturn();` ⇒ **内置注入点与层的改动同时存在于产物**（修前层会整脚本盖掉它们）。

真机验证（2026-09-27，chapter1，开发期自动化按键脚本，不随包）：进游戏后 `C → →×3 → Z` 第一轮就命中设置页，
dr-api.log 留下 `[设置页] 已进入：我们的行已绘制（submenu=30 coord30=0）` 与 `[menu] 打开 Mod 设置面板（submenu=51 coord30=5）`；
红心探针同一行附带 `room_speed=60 fps=60 fps_real=285.71` ⇒ **层在真机确实生效**（原版 chapter1 的 `room_speed` 是 30）。
截图 `02-config-page.png`（CONFIG 页只有我们那 7 行、无叠行）
与 `04-panel.png`（MOD 设置面板正常打开，红心在「章节选择」前），全程无 Code Error。

#### ⚠ 层 vs 层：多个层 patch 同一个对象（2026-09-27 修）

两个层都改同一个对象时，部署按 mod 顺序**整脚本覆盖**，前一个层的改动被静默吃掉。
实例：full-mods 版本下 chapter1 的 `gml_Object_DEVICE_MENU_Step_0` 被 `layer.60fps_layer.badartadventure`
+ `layer.dojoobj.probe` + `layer.pctobj.probe` 三方争用 —— 产物里只剩最后加载的 pct 版本（680 行），
dojo 的 `dj_open()` 菜单入口与 60fps 的 `global.fps_scale` 都没了（按 D 进不去道场、`DOJO_LOGS\dojo.log` 不再增长）。

**修法分两步（缺一不可）**：

1. **把争用目标纳入三方合并**：`Injector.cs` 2.5 段先统计每个 patch 目标被几个层改（`patchOwners`），
   把 `owner > 1` 的目标也加进 `BasePatchMerger.TryCreate(..., extraTargets)` 的集合；
   日志 `层间争用: {N} 个对象被多个层 patch，已全部改用三方合并` + 每个目标一行
   `[合并] {K} 个层都改 {target}，改用三方合并（避免后一个层整脚本覆盖前一个）`。
2. **链式合并（关键）**：patch 是 `group.QueueReplace(...)` **排队**写入的，队列里的文本在 `data.Code` 里还看不到
   —— 于是第二个层来合并时 `ours` 仍是「第一个层之前」的旧文本，合并结果不含前一个层的改动，
   最后入队的层照样吃掉前面全部。`BasePatchMerger` 现在自己记一份 `_pending[target] = 这次会写成的文本`，
   后续层以它作为 `ours` 继续链下去（`_curCache` / `_vanCache` 顺带缓存反编译结果）。

产物验证（2026-09-27 chapter1，2026-09-28 凌晨在**全 mod 重部署后的产物**上复验，命令 `sdump "E:\steam\steamapps\common\DELTARUNE\chapter1_windows\data.win" --code gml_Object_DEVICE_MENU_Step_0 <关键字>`）：
同一个条目从 680 行变成 **691 行**，第 3 行 `dj_open();`（dojo）、第 638 行 `scr_cf_init();`（pct）、
第 682-683 行 `ONEBUFFER -= (1 / global.fps_scale);`（60fps）—— **三个层的改动同时在**。

争用普查（2026-09-27，全 mod 启用，按 `patches/*.gml` 文件名统计）：root 0 / chapter1 **13**（共 452 个 patch）/
chapter2 **4**（1591）/ chapter3 **5**（1843）/ chapter4 **6**（1749）/ chapter5 **36**（1779）；
chapter1 的 `DEVICE_MENU_Draw_0` / `DEVICE_MENU_Step_0` / `obj_time_Create_0` 是三方争用，chapter5 大多是 60fps 层与 bossrush 层。

真机验证（2026-09-27，full-mods 版本 chapter1）：启动后停在 DEVICE 菜单（存档选择页），
点右上角 `[D] DOJO` 方框（或按 `D`）⇒ dojo 正常开局，`DOJO_LOGS\dojo.log` 出现新会话
`INIT chapter 1 → SESSION START chapter=1 → INTRO start → [1|20] ROOM room_dojo battle=0 → INTRO end`，
截图 `03-after-click.png` 是 dojo 的 XANZO1 启动画面；
同一屏还能看到 pct 的标题皮肤「第1章 DELTARUNE BUT IT'S PERCENTAGE COLOR」与 `[D] DOJO` 提示共存 ⇒
**争用修复在真机成立**。复现脚本（开发期，不随包；纯 ASCII，`powershell -File … -Chapter 1`，约 75 s）。

复验（2026-09-28 00:38，**全 mod 重部署（v7）之后**）：产物级三标记仍在（同一命令，条目 **691 行 / instr 2093**，第 3 行 dj_open();、
第 638 行 scr_cf_init();、第 682-683 行 global.fps_scale），真机 DOJO_LOGS\dojo.log mtime=**00:38:12** size=2336 开出新会话
「[1|0] INIT chapter 1 → SESSION START chapter=1 → INTRO start → [1|20] ROOM room_dojo battle=0 → INTRO end」，
截图同上路径（这次 XANZO1 启动画面带 pct 彩色滤镜 ⇒ dojo 层与 pct 层同时生效）。

> ⚠ 冒烟脚本的坑（2026-09-28）：脚本的 phase 1 会**连打 26 次 Z**（每次间隔 1.6 s）——
> chapter1 载入快，这串 Z 会直接把存档选定、冲进游戏，等按 D 时存档选择（DEVICE）菜单早没了 ⇒ 拍到的 after-click 是章节内游玩画面、
> dojo.log 没有新会话（**看起来像产物坏了，其实不是**）。改用它的一个变体（同样不随包：只把 phase 1 换成「等 18 s 让菜单自己出现」）即稳定复现；
> 大章节（ch2-5，data.win 大、载入慢）用旧脚本也能过，因为 Z 被开场阶段吃掉。

`--lint` / `--doctor` 的 `**patch 冲突**：{K} 个 mod 都整脚本覆盖 '{target}'` 告警仍会报（它描述的是「声明了同一个目标」这个事实），
但基底 ≠ 官方基线且有官方基线备份时，实际结果是**合并**而不是丢弃；缺备份时退回整体覆盖 + `[警告] {K} 个层都改 {target}…（缺官方基线备份，无法三方合并）`。

#### 基底不是官方基线时：patch 与基底做三方合并

patch 文件是**整脚本覆盖**（内容 = 官方基线 + 该 mod 的改动）。基底本身不是官方基线时（汉化整包、dojo 当基底…），
直接覆盖会把**基底对同一对象的改动一起冲掉** —— 两边都改过 `obj_battlecontroller_Step_0` / `obj_heart_Step_0`
这类核心对象时后果尤其明显。

修法（builder 已内置）：部署进入 patch 阶段前建 `BasePatchMerger`（`Neutraled\builder\PatchMerge.cs`），
对每个 patch 目标做**行级三方合并**：base = 官方基线源码（`backup/<chapter>_windows/data.win` 反编译），
ours = 基底当前源码，theirs = patch 源码。各自求基线↔单边的改动块（hunk），再按**基线坐标**聚成组：
一组里只有一边动过 ⇒ 用那一边；两边都动过且不同 ⇒ 记 1 处冲突并**保守取基底（ours）**。

三道放行（这三类直接原样用 patch，不做合并）：

- 目标不在「基底改动集合」里（基底没碰过它）；
- `PreWriteRepairs.SameInstructions` 判定基底只是**索引位移**；
- base / ours / theirs 归一后文本相同。

日志与报告：部署日志一行 `三方合并: N 个对象（冲突 M 处）/ 失败 K 个`，逐对象明细写
`Neutraled\cache\merge-<chapter>.txt`（`[合并] … 改动块 N 处，无冲突` / `[冲突] …（已保守保留基底版本）`）。

实测（2026-09-27，汉化当基底，六章全部 0 冲突 0 失败）：

| 章节 | root | ch1 | ch2 | ch3 | ch4 | ch5 | 合计 |
|---|---|---|---|---|---|---|---|
| 合并对象 | 3 | 4 | 9 | 30 | 18 | 27 | **91** |

chapter4 样例：`gml_Object_obj_titan_enemy_Step_0：改动块 87 处，无冲突`、`obj_holywatercooler_enemy_Step_0：28 处`、
`obj_round_evaluation_Draw_0：26 处`、`DEVICE_MENU_Draw_0：18 处`。

反例（同一天，**故意把 dojo 设成基底**做压力测试，chapter1）：`mod patches: 456 覆盖`、`三方合并: 44 个对象（冲突 6 处）/ 失败 0 个`，
冲突示例 `[合并冲突] gml_Object_obj_grazebox_Collision_obj_collidebullet: 1 处（保留基底版本）`、`gml_Object_obj_heart_Step_0: 5 处`
⇒ 冲突时宁可少一个帧率微调，也不能把基底的改动冲掉。

自检：`ntl-builder.exe --merge-selftest`（17 组用例：两侧未改 / 单边改 / 两侧改不同位置 / 同行冲突取基底 / 同点插入 /
首尾插入 / 删除撞改动 / 空 base|patch|ours / 2200 行大文件走唯一行锚点分支 / 行数守恒）。

边界：三方合并只处理「patch 覆盖的对象」与「基底改动」的重叠，**不解决层新增对象**（见上节）。

#### 层自己的设置页：改用 NTL mod 接口（放进层的 `gml/`）

被 `patch_skip` 让出去的那些对象里，往往就包含层**自己的设置页**（60fps 的 `submenu == 50` 页）。
层抢不回那一页 —— 正确做法是把开关**搬进 Neutraled 的「Mod 设置」面板**：

```
mods/<层名>/<作者>/<章节>/gml/*.gml    # 文件名 = 脚本名；名为 main.gml 的是入口（游戏启动时执行一次）
```

入口里调 `ntl_menu_add(id, 标签, 数值, 动作脚本名, 说明)` 注册行，动作脚本里改状态后
`ntl_menu_set_value(id, ...)` 刷新数值 —— 接口语义见 [MODMENU-API.md](MODMENU-API.md)（§5 就是 60fps 这一例）。


实测（2026-09-27，60fps 层，每章 12 个 .gml）：部署日志出现
`mod 脚本: 16 个（入口: main, ntl_ntl_chapter_c3a8e36c4_main）`、`mod patches: 422 覆盖 / 0 局部替换 / 0 跳过`；
真机（chapter4，开发期自动化按键脚本，不随包）打开面板后 dr-api.log 留下：

```
[mod] load layer.60fps_layer.badartadventure -> main
[menu-api] [接口] 面板项已注册: fps.ow_stick / fps.bt_stick / fps.debug / fps.gerson / fps.speed / fps.textskip
[menu-api] [接口] 触发 fps_ntl_toggle_ow / fps_ntl_toggle_bt / fps_ntl_toggle_textskip
[ui] [面板] 红心精灵解析: spr_heart=3695(16x16) … ⇒ 选用 index=3695 name=spr_heart room_speed=60 fps=60 fps_real=259.88
```

截图 `05-fps-row1-ow.png`（`地面 360 度摇杆  关`）与
`06-fps-row1-toggled.png`（按 Z 后变 `渐进`，底部说明行中文正常）⇒ 开关回到了玩家手上。

两个细节：

* 层的 `gml/` 里文案要写成**字符串字面量**（部署期字体补全会扫 `mods/**/gml/*.gml`，运行时拼的字符串扫不到 ⇒ 画出来是空洞）；
* 若别的 mod 已经有 `gml/main.gml`，部署时打印 `[警告] 脚本名冲突 main -> ntl_<modid>_main`（清单用改名后的名字，仍会执行）。

> chapter5 的 44 个「无法表达」是层新增的**非脚本代码入口**：一半是**已存在对象的新事件**
> （`Draw_76` / `Draw_77` = Pre-Draw / Post-Draw，`Step_1` = Begin Step），一半是**新对象的事件**。
> patch 要求目标已存在、`gml/` 只能加函数，所以 ch5 的平滑镜头 / 横版段落那部分仍要靠整包基底表达。
>
> ⚠ 探针层的坑（2026-09-27 真机踩到）：用 `--layer-from-base --name <探针>` 做可行性取数时，
> 产物会**落成一个 enabled 的 mod**（`mods/<探针>/<作者>/<章节>/`）。它带着半截 mod 参与下一次部署
> （dojo 的 34 个 patch 引用了不存在的 `obj_dojo`）⇒ 真机直接 `Code Error`。
> **取数完必须删掉探针目录再重新部署**（已删 `mods/dojo_merge_probe`、`mods/pct_probe`）。
### 路线 3：资源包导出 `--export-packs`

```powershell
ntl-builder.exe --export-packs <mod 的 ref/data.win> --chapter chapter5 [--out <目录>]
```

把 data.win 里**相对官方基线新增或改动**的精灵 / 声音 / 字体导出成 Neutraled 资源包
（`sprites/*.json + PNG`、`sounds/*.json + 音频`、`fonts/*.json + 字形`），
再由 `SpriteImport` / `SoundImport` / `FontImport` 导入到**任意基底** ——
资源不涉及索引移植，天然可叠加。

典型用例：BossRush 这类「改代码 + 加 1851 个精灵」的整包 mod，
用 `--layer-from-base` 拿走行为层、用 `--export-packs` 拿走资源层，就不必再独占基底。

#### 端到端验证（2026-09-25 实测）

目标：让**两个整包 data.win mod 同时生效**（以前物理上不可能）。

| 步骤 | 结果 |
|---|---|
| 1. 把 `friend_inside_yellow` 的 2037 个候选跑源码级过滤 | **真实改动只有 5 个**（2032 索引噪声，0 反编译失败） |
| 2. 停用它的整包 mod（不再抢基底） | — |
| 3. 部署 chapter5（基底换成 **BossRush** 的 data.win） | `mod patches: 5 覆盖 / 0 跳过`，编译成功，产物 229 MB |
| 4. 启动游戏进 chapter5 | **零错误**：`当前章节: chapter5` / 7 个 hook / Kristal 层就绪 |

5 个 patch 都是真实游戏对象（反编译得到、重新编译进基底）：
`scr_item_to_doki`、`scr_weaponinfo`、`obj_blue_enemy_Other_22`、`obj_ch5_DWCL03_Step_0`、`obj_dw_fcastle_cafe_Create_0`

#### 资源包导出实测（BossRush，chapter5）

```
精灵: 导出 1851 / 声音: 导出 150（另 1 个无内嵌数据跳过）
导出完成: 合计 2001 个资源 -> packs-bfr/（sprites 10109 文件 / sounds 300 文件）
```

与已知增量（+1851 精灵 / +151 声音）**完全吻合**（那 1 个声音没有内嵌数据，是外部 audiogroup，正确跳过）。

三种情形的实测语义：
| 场景 | 结果 |
|---|---|
| 文件 vs 自己 | 明确提示「基线与源是同一个文件 → 按全量导出」→ 导出该文件全部资源（启动器：8 精灵 + 9 声音 + 10 字体） |
| dojo vs 官方 chapter5 | **0 导出 / 8538 未变**（dojo 确实不新增资源，行为正确） |
| BossRush vs 官方 chapter5 | **1851 精灵 + 150 声音** |

#### 实测案例（2026-09-27）：两种「整包 mod → 可叠加」形态

**① 纯代码改动 → 源码级差异层**（`blockless_mantle_holder_arena`，chapter3）

```powershell
ntl-builder.exe --layer-from-base "Neutraled\mods\blockless_mantle_holder_arena\unknown\chapter3\ref\data.win" --chapter chapter3 --name blocklesslayer --author unknown
```

| 指标 | 结果 |
|---|---|
| 指令级候选 | 791（另排除 8471 条纯元数据差异） |
| 真实改动 / 索引噪声 / 反编译失败 | **1 / 790 / 0** |
| 产物 | `Neutraled\mods\blocklesslayer\unknown\chapter3\`（id `layer.blocklesslayer.unknown`、patches 1） |
| 唯一 patch | `patches\gml_Object_obj_shadow_mantle_bg_Create_0.gml`（整脚本覆盖：`tile_grid[i][j] = 1;` 网格 = 无砖块版竞技场） |

生效取证用 `--probe-code`（读的是**已部署产物**，不是 mod 的 ref）：
`gml_Object_obj_shadow_mantle_bg_Create_0` 指令数 **原版 385 → 层 287 → 已部署 chapter3_windows\data.win 287**；
同一次部署日志里 `mod patches: 1842 覆盖`= 60fps 层 1841 + blockless 层 1。

**② 纯资源改动 → 资源包**（`ice_e_eram_mod`，chapter3）

该 mod 的 `--extract-diff` 报「相对基线没有任何字节码差异」（代码对象 9262=9262、四类池全等、精灵/声音/房间/字体数量全等）；
字符串多重集比对才看出实质：mod 比原版多 60 个可打印串，全是 PNG 块标记（`IHDR`/`sRGB`/`PLTE`/`tEXtSoftware`/**`Paint.NET 5.1.12`**）⇒ 内容就是**被替换的贴图**。

```powershell
ntl-builder.exe --export-packs "Neutraled\mods\ice_e_eram_mod\unknown\chapter3\ref\data.win" --chapter chapter3
```

导出 **11 个资源**（`spr_board_imonfire`、`spr_shadow_mantle_bomb/dash/fire/idle…`；声音 0 导出 / 401 未变 / 115 无内嵌数据，字体 0 导出 / 17 未变）。
把 `sprites/`（51 个文件）放到该 mod 的 `mod.json` 旁边后部署，chapter3 日志：
`精灵资源包: converted.ice_e_eram_mod.unknown` / `精灵: 0 新增 / 40 帧替换 / 0 跳过（纹理页 1 张）` / `精灵导入合计: 40`
⇒ **整包 mod 的贴图改动叠加到了汉化基底上**（基底仍是 `converted.hanhua.hanhuazu`，无需独占）。

> ⚠ `--export-packs` 的默认输出目录由**源路径**推断：源写成 `<mods>\<name>\<author>\<chapter>\ref\data.win` 时，
> 路径里的 `ref` 会被当成作者目录 ⇒ 资源落到 `Neutraled\mods\ref\converted\<chapter>`（一个不会被加载的孤儿目录）。
> 要么显式 `--out <目录>`，要么把导出的 `sprites/`/`sounds/`/`fonts/` 搬到目标 mod 的 `mod.json` 旁边。
#### 往返一致性（导出 → 用真实导入器灌回 → 逐项 + 逐像素比对）

用临时验证工程（`packtest.exe roundtrip`）实测，**修复后 100% 无损**：

| 指标 | 修复前 | 修复后 |
|---|---|---|
| 精灵 `exact_match` | 650 / 1851 | **1851 / 1851** |
| 精灵 `meta_diff`（帧数/尺寸不符） | **999** | **0** |
| 精灵 `pixel_diff` | 532 | **0** |
| 声音 `exact_match` | 150 / 150 | **150 / 150**（字节全同） |

为此修掉三个**导入器**的 bug（导出侧本来就是无损的 —— 用 FNV-1a 比了 2 亿字节、差异 0）：

1. **多帧新精灵只剩第 0 帧**（`SpriteImport`）：第 0 帧建好精灵后，第 1 帧的 `FirstOrDefault(name)` 立刻命中它 →
   走进「已有精灵」分支 → `frameIdx < Textures.Count` 为假 → 帧被静默丢弃（追加分支对新精灵永不可达）。
   → 帧落位改成 替换 / 追加 / 越界 三态。**999 个多帧精灵因此恢复。**
2. **贴图合成语义**：`Composite(Over)` 会归一化 alpha=0 处的隐藏 RGB 并做 alpha 舍入 →
   改用 `Composite(Copy)`（帧之间不重叠，Copy 语义安全），像素差 532 → **0**。
3. **字体 Shift 的可空语义**：`Shift != 0 ? Shift : w` 会把真实 Shift=0 的字形改成字形宽度 → `Shift` 改 `int?`。

> 已知瑕疵：0 尺寸字形会写成 1x1 透明占位（保住帧号，绝不静默丢帧）；字体导入是单页 4096²，字形超页会截断（导入器有警告）。

### 字节码复制的保真度（`--verify-refcopy`）

用**反编译器当预言机**：从源文件反编译 → `RefCopy` 复制进目标 → 在目标里再反编译 → 文本比对。

```powershell
ntl-builder.exe --verify-refcopy <源 data.win> <目标 data.win>
```

实测（60fps → 官方 chapter5 基线，60 个样本）：

| 修复前 | 修复后 |
|---|---|
| 保真 52 / **文本不一致 4** / 失败 4 | 保真 **56** / **不一致 0** / 失败 4 |

#### 修复记录：两个真 bug

1. **同名条目不替换** —— `RefCopy` 用 `CreateEmptyEntry` 新建条目，导致目标里出现同名重复对象；
   而部署与游戏按 `Code.ByName()` 取**第一个**（旧的、没被改的）→ `references.codes` 的改动**静默失效**。
   现改为复用同名条目，并统计替换了几个。
2. **double 常量被打坏** —— `UndertaleInstruction` 的 `ValueInt/ValueLong/ValueDouble` 共享同一段 8 字节存储，
   而 **`ReferenceType` 的 setter 会重写这段存储**。原代码先把值设好、后设 `ReferenceType`，
   于是 `0.7 → 8.49E-315`（denormal 垃圾）、`0.1 → 约等于 0`。
   现改为**元数据字段先设、值字段最后设**（可用 `--probe-selftest` 复现）。

#### 残余边界（诚实说明）

剩余 4/60 失败全部是**控制流类**（`B` / 分支密集的对象），表现为目标里反编译报
`unresolved branch` / `Unexpected branch when building AST`。
实测这些对象**字段逐条完全一致**，说明问题在跳转链编码与源文件布局绑定；
把 `JumpOffset` / `Extra` / `SwapExtra` 归零会让保真度掉到 **0/60**（更糟），所以必须原样照抄。

→ **这类对象请走路线 2（`--layer-from-base`）**：源码级重编译不受编码影响。

### 冲突兜底：`--conflicts`（不部署也能查）

```powershell
ntl-builder.exe --conflicts --chapter chapter5
# 退出码：0 = 无冲突 / 2 = 有 Error 级冲突   ← CI / GUI 友好
```

检查四类问题并把结果写进 `conflicts.json`：

| 类别 | 级别 | 以前的行为 |
|---|---|---|
| 多个整包基底抢基底 | Error | 静默取第一个 |
| **多个 mod 整脚本 patch 同一对象** | Error | **静默 last-wins（现已响亮报告 + 给出 Lua hook 兜底建议）** |
| 多个 mod 引用复制同一对象且无 override | Error | 已报告 |
| patch 与 hook 打在同一目标 | Warn | 未检查（现补上） |

> **关于"Lua hook 兜底"的诚实说明**：整脚本 patch 之间**无法自动合并**（两个 mod 各自给出一份完整实现）。
> 工具能做的是：① 不再静默丢弃；② 明确指出是哪个对象、哪些 mod；③ 建议把其中一个改成 Lua hook（`pre`/`post`，可调 `ntl_orig_<目标>`）。
> 真正的合并需要人看懂两边的语义差异 —— 这一步不能也不该自动化。

### 诊断工具

| 命令 | 用途 |
|---|---|
| `--probe-code <data.win> <对象名>` | 打印指令的**真实字段类型与解析目标**（判断哪些操作数是索引） |
| `--probe-selftest` | 复现 `ValueInt/ValueLong/ValueDouble/ReferenceType` 的赋值顺序陷阱 |
| `--verify-refcopy <源> <目标>` | 反编译器当预言机，量化跨资源池复制的保真度 |
| `--diff-verbose` | 打印前 12 个真实改动对象 |
| `--conflicts [--chapter ch]` | 只查冲突不部署（退出码 2 = 有冲突，CI 友好） |

---

## 中文字体（Neutraled 自带字体包）

> DELTARUNE 自带字体只有 ASCII（`fnt_main` **96 个字形**），Neutraled 控制台 / 章节选择界面里的中文会**整片画不出来**。
> 解决：Neutraled 自带一个**多语言字形包**（`Neutraled/fonts/ntl_font_cjk.json` + `ntl_font_cjk_o1..o4.png`），
> 部署时自动注入每个章节；字形全部由 **SIL Open Font License 1.1** 的开源字体（Google Noto 家族）**离屏渲染**。

### 字体来源（2026-09-29 起：OFL 开源字体）

| 位图页 | 源字体 | 字形数 | 内容 |
|---|---|---|---|
| `ntl_font_cjk_o1.png` | Noto Sans SC | 4689 | 主表：拉丁/希腊/西里尔扩展、标点、汉字、全角符号 |
| `ntl_font_cjk_o2.png` | Noto Sans KR | 3 | 语言名「한국어」 |
| `ntl_font_cjk_o3.png` | Noto Sans Symbols 2 | 232 | 符号 / 箭头 / 方框绘制 |
| `ntl_font_cjk_o4.png` | Noto Emoji | 50 | emoji 区符号（↩ ↪ ♈ … ⛽ ⛺）、ℹ ✅ ❌ |

- **合计 4974 字形 / 4 页**（2026-09-30 全量重渲：修全角标点错位后重渲，页数 6 → 4）。
  重渲时 **Noto Sans JP 没有产出字形**（剩余字符已被 SC 主表覆盖），所以没有生成 JP 页——它不是被删，是没轮到它补字。
  对照表与逐页来源（本地 TTF 的 SHA-256、上游 URL、版权行、产物溯源）见 `Neutraled/fonts/OFL-NOTICE.txt` 第二节。

- 包格式：`EmSize=12`、`LineHeight=18`、`Page=""`（**多 sheet 模式**，导入端按每个字形的 `Sheet` 重新拼页，见 `builder/FontImport.cs:42-48`、`:94-131`）。
- **许可**：每张页的源字体版本 / 上游文件 / SHA-256 / 版权行，以及随包分发的 **6 份 OFL 全文**（`fonts/ofl/`），
  见 `Neutraled/fonts/OFL-NOTICE.txt`。**发布包必须带上这两样**（换字体＝换许可义务）。
- 历史（已废弃）：早先用的是「从汉化 mod 的 `fnt_main` 原样搬运 3580 字形」的**专有来源**方案；
  该路径已由 OFL 渲染取代，旧的 `ntl_font_ja/ko/latin/scripts/sym/wave_sheet0.png` 已删除。

~~~powershell
# 渲染（每个字体一轮；字符表 = 产物真正会用到的文本，与部署自检同口径）
ntl-builder.exe --make-cjk-font <输出目录> --ttf <NotoSansSC-VF.ttf> --charset list --chars <字符表文件> --font-name ntl_font_cjk

# 合并进 fonts/ntl_font_cjk.json（Page 置空 → 多 sheet 模式；sheet 改名 ntl_font_cjk_oN.png）

# 复核
ntl-builder.exe --font-probe chapter4_windows\data.win   # 字形数与字形表自检（逆序对应应为 0）
node <开发期脚本>/font-audit.mjs                        # 缺字 / 空白格门禁（退出码 1 = 有缺口）
~~~

### 关键设计：字形坐标以 JSON 为准，位图只是「画布」

早期版本把字形重新打包到新 sheet，结果出现「间距不对 + 引号状杂点」：
**空格字形指向 sheet 的 (0,0)，而 (0,0) 正好落在某个汉字格上** → 每个空格都画出一小条汉字。

现在两条路径都不会踩这个坑：

1. **从 TTF 渲染**（当前发布包走这条）：按字符集重新排版新页，坐标由渲染器写进 JSON，不存在「指向别人格子」的问题；
2. **从 `data.win` 搬字形**（`--make-cjk-font --from-font <win> --source-font fnt_main`，给像素风字体用）：
   **源纹理页原样导出成 PNG 当字体页，字形坐标一个都不改**。

两条路径都带**墨迹门禁**（`builder/CjkFont.cs` 的 `HasInkIn`）：源字体里没有这个字时渲染出来的是一张**全透明格**，
留下它只会让上层以为「这个字有了」、画面上却是一片空白 —— 所以渲染期就丢掉并打印样例（`builder/CjkFont.cs:172-241`、`:341-342`）。

### 用到中文字体的地方

| 位置 | 说明 |
|---|---|
| `ntl_console_draw` | 控制台（标题/输出/提示行） |
| `ntl_root_draw` | **章节选择界面**（标题/页码/搜索/列表/底部按键提示）—— 这里以前用的是游戏 `fnt_main`，所以中文全空白 |

两处都额外做了 `gpu_set_texfilter(false)`：像素字体被线性过滤会发糊，非整数缩放下字距看着不匀。

### 部署侧

- `--deploy` **自动导入** `Neutraled/fonts/*.json`（与 mod 无关，每个章节都注入）
- **多 sheet 模式**：JSON 里 `Page=""`、每个字形带 `Sheet` 名 ⇒ 导入端把各 sheet 拼回一张纹理页
  （`builder/FontImport.cs:94-131`，单页上限 4096；找不到 sheet 会打印 `[警告] {0}: 找不到页图 {1}` 并跳过）
- 缺字由**部署期补字**兜底：只补产物真正会用到的字符（见 `docs/MANAGE.md` §11.2）

### 覆盖率

- 本包按**实际用到的字符集**渲染，所以对当前文案基本不缺；但**没有渲染到的字符在游戏里仍会空白**（不是方框、不是报错）。
- 判定口径：部署自检的「内容级文本 × 字体」检查（目标「缺 0」）+ 开发期字体审计脚本 `font-audit.mjs`（不随包）的空白格门禁。
- 换字体 / 加语言时**不要只改 JSON**：位图页与坐标表必须成套生成。

### 踩过的坑

1. **重排字形 → 空格指向 (0,0) 撞上汉字格**，出现引号状杂点与字距错乱 → 改为原样搬运。
2. **源坐标与目标坐标必须分开存**（早期版本读像素时用了目标坐标，整页全空）。
3. **度量照抄源字体**（Ascender/LineHeight 源里是 0 就写 0），自己编会让字形垂直位置偏移。
4. **像素字体的空格常过窄**（实测 shift=3）→ 加宽到半宽，否则英文看着像没空格。
5. **逐字 `label:` 渲染极慢**（ImageMagick 每次重读 9.5MB 字体）→ 汉字按每行 64 字批量渲染再切格。
6. **Magick.NET 14 的 API 与老教程差别大**（`Drawables`/`FontTypeMetrics` 不可用、`RePage`→`ResetPage`、`int` 不隐式转 `uint`）。

---

## Neutraled 部署缓存

### 目的

部署一次要把所有启用的 mod 注入 `data.win`（单章节 30~60 秒 + 128 MB 写盘）。
缓存把"某组配置的部署产物"存下来，下次配置相同时**直接复用**（实测 **21.1 秒 → 0.8 秒**）。

### 命中条件（全部相同）

| 维度 | 说明 |
|---|---|
| **Neutraled 版本** | API/注入器版本；升级后旧缓存自动失效 |
| **游戏版本** | 用 `backup/data.win` 的大小+时间做指纹（**不能用顶层 data.win**，它会被部署改写） |
| **目标章节** | root / chapter4 / 平行章节 各自独立 |
| **启用的 mod 集合** | 每个 mod 的 id + 版本，按 id 排序（与勾选顺序无关） |

签名 = `sha256(ntl版本 | 游戏版本 | 章节 | 排序后的 mod@版本列表)`

### 目录结构

```
Neutraled/cache/
  index.json              缓存索引（签名 / 大小 / 最后使用 / 命中次数 / 上限）
  <签名前16位>/
    manifest.json         该缓存的元数据
    data/root/data.win
    data/chapter4/data.win
    data/<目标>/products/ 部署时生成的注册表（chapters.json / hook-registry.json / api-registry.json）
```

### 使用

#### 命令行

```bash
# 查缓存 → 命中则硬链接复用；未命中则部署 + 存缓存；最后由 Steam 拉起游戏
ntl-builder.exe --launch chapter4

# 设置缓存上限（MB，默认 4096）
ntl-builder.exe --launch chapter4 --cache-max 8192

# 列出缓存
ntl-builder.exe --cache-list

# 清空缓存
ntl-builder.exe --cache-clear

# 查询当前配置是否有缓存（JSON 输出，GUI 用）
ntl-builder.exe --cache-applied --chapter chapter4
```

#### GUI

- **「部署并启动」** → 自动走缓存路径（命中则秒开）
- **「缓存管理」** → 查看缓存列表 / 清空 / 设置上限
- 勾选 mod 后若与上次启动的配置不同，状态栏提示 **"配置已变更，重启游戏后生效"**

### 关键设计

#### 1. 硬链接应用（0 秒 0 额外空间）
命中后不是复制 128 MB，而是 `CreateHardLink`（Win32 API）把缓存文件链接到章节目录。
同分区时瞬间完成且不占额外空间；跨分区或失败时自动退回复制。

#### 2. 始终由 Steam 启动
缓存只负责"把 data.win 准备好"，游戏仍由 `steam://rungameid/1671210` 拉起
→ **Steam 计时 / 云存档 / 成就全部正常**。

#### 3. LRU 淘汰
超过上限时按"最后使用时间"从旧到新删除，直到低于上限。

### ⚠️ 踩坑记录

1. **游戏版本指纹不能用顶层 data.win**
   部署本身会改写它 → 时间戳变化 → 每次签名都不同 → 缓存永远不命中。
   必须用 `backup/data.win`（原版快照，部署不动）。
2. **mod 字典要去重**：同一个 mod id 可能出现在多个章节条目里，
   `ToDictionary` 会抛 `An item with the same key has already been added`，要手动赋值覆盖。

---

## Neutraled 解释器性能优化记录

> 三轮优化，累计提升 **37-40%**，并修掉一个**内存泄漏**。

---

### 基准数据（游戏内实测，每项取首次运行值）

| 项目 | 原始 | 优化1 | 优化2 | 优化3 | **总提升** |
|---|---|---|---|---|---|
| 2 万次累加 | 3.03s | 2.391s | 2.251s | **1.894s** | **-37.5%** |
| 3 千次函数调用 | 1.063s | 0.982s | 0.888s | **0.639s** | **-40.0%** |
| 2 千次表写入 | 0.345s | 0.322s | 0.345s | **0.228s** | **-33.9%** |
| 1 千次字符串 | 0.232s | 0.233s | 0.241s | **0.206s** | **-11.2%** |

对比 GML 原生：10 万次累加 = 53ms（NTL 的 Lua 是纯 GML 树遍历解释器，慢是结构性的）

---

### 优化 1：消除纯数字运算的元方法查询

**问题**：ntl_lua_binop 每次都对元方法做查询（创建 + 查 + 销毁 ds_map），
而 `1 + 2` 这种纯数字运算根本用不到元方法。

```gml
// 优化前
if (is_real(_a) || is_real(_b)) {
    var _mm = ntl_lua_meta_binop(_op, _a, _b);   // 创建 + 查 + 销毁 ds_map
    ...
}

// 优化后：两边都是数字 → 直接算
if (is_real(_a) && is_real(_b)) {
    var _c0 = string_copy(_op, 1, 1);
    if (_c0 == "+") { if (string_length(_op) == 1) return _a + _b; }
    ...
}
```

**效果**：2 万次累加 3.03s -> 2.391s（-21%）

---

### 优化 2：内联到求值器（省一次函数调用）

**问题**：ntl_lua_ex 的 binop 分支拿到值后还要调 ntl_lua_binop（函数调用 + 参数打包）。

**修复**：数字运算直接在 ntl_lua_ex 里算完，不进入 ntl_lua_binop。

**效果**：2.391s -> 2.251s，函数调用 0.982s -> 0.888s

---

### 优化 3：循环环境复用（最大收益，同时修内存泄漏）

**问题**（严重）：

```gml
// ntl_lua_ev_stat.gml:188（原代码）
while ((_step > 0 && _i3 <= _to) || (_step < 0 && _i3 >= _to))
{
    ntl_lua_env_set(_loopEnv, ds_map_find_value(_n, "name"), _i3);
    var _r3 = ntl_lua_ev_block(ntl_lua_env_new(_loopEnv), ...);   // 每轮建新环境！
}
```

ntl_lua_env_new = ds_map_create()，**从不释放**。
2 万次循环 = 2 万个 ds_map 泄漏。GML 没有 GC，这类泄漏会累积到游戏卡死。

**修复**：

```gml
var _nameNode = ds_map_find_value(_n, "name");
var _bodyNode = ds_map_find_value(_n, "body");
var _bodyEnv = ntl_lua_env_new(_loopEnv);    // 只创建一次
while (...)
{
    ntl_lua_env_set(_loopEnv, _nameNode, _i3);
    ds_map_clear(_bodyEnv);                   // 清空局部变量
    ds_map_add(_bodyEnv, "_ntlp", _loopEnv);  // 保留父链
    var _r3 = ntl_lua_ev_block(_bodyEnv, _bodyNode);
}
```

`while` 循环同样处理。

**效果**：2.251s -> **1.894s**，函数调用 0.888s -> **0.639s**

---

### 已发现的同类问题（待优化）

`ntl_lua_ev_stat` 的 if / do 分支也在每次执行时新建环境：

```gml
if (_k == "do") return ntl_lua_ev_block(ntl_lua_env_new(_env), ds_map_find_value(_n, "body"));
if (ntl_lua_truthy(ntl_lua_ex(_env, _conds[_h])))
    return ntl_lua_ev_block(ntl_lua_env_new(_env), _blocks[_h]);
```

这两个在循环体里会被反复执行，同样有泄漏。需要环境池化方案。

---

### 优化方法论（可复用）

1. **先测量，再优化** —— 没有基准数据的优化都是猜测
   - 建立游戏内基准脚本（用 os.clock，注意要毫秒精度）
2. **找每轮迭代的开销** —— 循环体里的重复分配最致命
3. **区分「慢」和「漏」** —— ds_map_create 不释放既是性能问题也是稳定性问题
4. **内联热点路径** —— GML 函数调用 + 参数打包开销不小
5. **改一处就测一次** —— 中间数据能定位是哪一步起作用
---

### 优化 4-6：全部循环/块的环境池化（Round 5-6）

#### 最终性能

| 项目 | 原始 | 最终 | 总提升 |
|---|---|---|---|
| 2 万次累加 | 3.03s | **1.862s** | **-38.5%** |
| 3 千次函数调用 | 1.063s | **0.652s** | **-38.7%** |
| 2 千次表写入 | 0.345s | 0.311s | -9.9% |
| 1 千次字符串 | 0.232s | 0.355s | (负载波动) |

#### 消除的 5 处 ds_map 泄漏

| 位置 | 泄漏量（2 万次循环） |
|---|---|
| fornum | 2 万个 ds_map |
| while | 同上 |
| if / do | 同上 |
| forin | 同上 |
| repeat | 同上 |

修复方式：环境创建一次，每轮 ds_map_clear + 重设 _ntlp 父链复用。
if/do 用新增的 ntl_lua_env_pool_get.gml（按 父环境|用途 索引）。

#### 验证

```
联动验收: 20 项全过
错误检查: 无错误
```

#### 为什么这类泄漏危险

GML 没有垃圾回收。ds_map_create() 出来的句柄不 ds_map_destroy 就永远占着内存。
短测试（几千次循环）看不出来，但 Kristal 的过场动画/战斗循环动辄几十万次迭代，
累积起来会让游戏越来越卡直到崩溃。

#### 剩余优化空间（已识别）

1. ntl_lua_ex 的 switch 用字符串比较（AST 数字 opcode 可再提升 20-30%）
2. ntl_lua_env_find 的 ds_map_exists + ds_map_find_value 双重调用
3. 50 个 lint 警告（ds_map 守卫，理论风险）

### 实测基准（2026-09-25，本机 E: 盘，墙钟时间）

#### 部署

| 场景 | 耗时 |
|---|---|
| 缓存命中（日常点「部署并启动」） | **0.9 s** |
| 冷启动同一章（部署+存缓存） | 32.9 s |
| `--deploy root`（3 MB） | 4.1 s |
| `--deploy chapter1`（13 MB） | 11.1 s |
| `--deploy chapter4`（126 MB） | 29.9 s |
| `--deploy chapter5`（229 MB 基底 + 6 mod） | **44.4 s**（优化前 64.4 s） |
| `--conflicts`（只查不部署） | 0.4 s |

chapter5 的 44.4 s 构成：mod 扫描+外部章节安装 6.8 s / 注入（载入+编译）11.5 s / 写盘 21 s / 其余 5 s。

#### 转换（一次性）

| 操作 | 耗时 |
|---|---|
| Kristal 项目（319 文件） | 2.0 s |
| 裸 xdelta（含自动探测章节） | 6.7 s |
| 六章整包（dojo，162 override） | 23.7 s |
| `--export-packs` 1851 精灵+150 声音 | 292 s（PNG 编码为主，未并行化） |
| `--layer-from-base`（6092 候选） | ~30 s（反编译为主） |

#### 已做的优化

1. **输入函数扫描缓存**（按基底代码布局指纹）：20.1 s → 1.2 s，整章 −20 s
2. **写盘改原子改名**（`File.Move` 取代 `File.Copy`）：少一次 229 MB 全量拷贝
3. **部署缓存**（硬链接复用）：整章 32.9 s → 0.9 s

#### 并行与确定性优化（2026-09-25）

| 优化 | 前 | 后 | 验证方式 |
|---|---|---|---|
| 输入函数扫描缓存（按基底指纹） | 20.1 s | 1.2 s | 替换数/代码块数与优化前完全一致（412/120） |
| 导出 PNG 并行编码（8 核） | 292 s | ~243 s | 并行 vs 串行**逐字节对照 10409 文件 0 差异** |
| 导出产物确定性（Strip 元数据） | 每次字节都不同 | 完全可复现 | 同上（此前因时间戳元数据 8258 个 PNG 全不一致） |
| 外部章节增量复制 | 每次搬 319 个文件 | 跳过未变 | 日志：更新 0 / 未变跳过 319 |
| 部署后自检（安全网） | 无 | chapter5 +5.4 s / root +0.17 s | 5 项检查 + 6 个负向用例 |

**通道对照（chapter5，229MB + 6 mod）**：完整档 **45.3 s** / 快速档 **39.9 s**（扫描缓存已热）；
冷缓存时快速档约省 20 s。

#### 按 mod 适配程度选通道（`ntl_adapt`）

mod 作者在 `mod.json` 写 `ntl_adapt` 声明自己的适配程度（见 docs/PIPELINE.md）：
`chapter` / `assets_only` / `no_hooks` / `no_console_input` / `level:fast`。
**声明与内容不符会被拒绝并警告**；`no_console_input` 需**全部启用 mod** 都声明才触发快速档；
玩家可用 `--full-deploy` 强制完整档。

#### 产物个数优化（2026-09-28）：瓶颈不是某一阶段，而是**产物个数**

起因：用户问「部署怎么这么慢」。先量后改 —— `builder/PhaseTimer.cs` 落地，`Program.cs` 插 9 个粗粒度 mark、
`Injector.cs` 插 7 + 6 个细粒度 mark（都在 `--deploy` 的输出里，正常部署都能看到）。

**两章实测（单章、`--force`）**：

| 阶段 | chapter1 | chapter5 |
|---|---|---|
| 1/5 备份 + 扫描/排序 mods | 0.6 s | 0.5 s |
| 2/5 选基底 + 加载基底 data.win | 1.4 s | 3.6 s |
| 3.5/5 计算基底改动集合 | 0.2 s | 0.2 s |
| 0-2.41 预扫描 + api/mod 脚本 + 精灵/音效/字体包 | 3.5 s | 4.3 s |
| 2.5 patches + 三方合并 | 1.8 s | 10.9 s |
| 2.8 资源名守卫 | 0.2 s | 0.6 s |
| 注入核心收尾（3-7 段） | 8.1 s | 35.3 s |
| 5/5 写入 data.win + 清单 | 2.8 s | 9.3 s |
| **主产物合计** | **18.6 s** | **65.1 s** |

chapter1 再拆细：`4 首次编译 (Import #1)` 4.1 s（21%）、`5.35-6 控制台屏蔽 + 增强补丁 + 引导注入` 3.0 s（16%）、
`7 二次编译 (Import #2)` 1.0 s（二次编译只编新入队的项，不是全量重编）。`--fast-deploy` 对比：61 s → 59 s，
**几乎无收益**（输入扫描早已是「指令级结构预筛 + `cache/inputscan-<sha256>.txt`」，实测只剩 0.1-0.2 s）。

★ 真正的账：**每章要建 1 主产物 + 3 个平行时间线子产物**。chapter1 的`--deploy` wall 60 s = 主 18.6 + 时间线
11.1 + 10.8 + 9.5 ≈ 50 s（时间线占一半以上）；`--deploy-all --force` 的 6 个 worker **各跑一遍全部时间线** =
同一批产物被 6 个进程并发重建、互相覆盖 —— 既慢又危险。时间线由 `DeployTimelines` 串行调用
`Deploy(gameRoot, srcChapter, outDir, modsFor, baseWinOverride, saveName)`（`Program.cs:2451-2580`）。

**真根因（`.ntl-deploy-<章节>.sig` 被搬走）**：时间线产物目录里的运行时文件来自源章节目录的整目录复制
（`Program.cs:1943-1968`，跳过 `data.win`），它把源章节目录的 **`.ntl-deploy-chapter1.sig` 也一起复制进
时间线目录**，覆盖掉时间线自己的签名 ⇒ 时间线每轮比对都不一致 ⇒ **永远重建**（这正是 `--force` 重部署
155-167 s 里最大的一块白干）。判据：`ntl_timeline_9_...\.ntl-deploy-chapter1.sig` 的内容曾经与
`chapter1_windows\.ntl-deploy-chapter1.sig` **完全相同**，而时间线自己的签名应当不同。

**修法（三处，都在 `builder/Program.cs`）**：

1. **复制循环跳过账本文件**：文件循环 `if (name.StartsWith(".ntl-", StringComparison.Ordinal)) continue;`、
   递归目录循环 `if (Path.GetFileName(f).StartsWith(".ntl-", StringComparison.Ordinal)) continue;`。
2. **跳过判定搬家**：从 1.5) 挪到 2.1.5)（章节注册表之后）—— 跳过路径也要给时间线过签名，而
   `DeployTimelines` 依赖 2.1) 赋值的 `LastAllMods` / `LastRegistry`（1.5 位置它们是空的）；
   跳过分支里补上 `DeployTimelines` 调用，否则主产物一跳过、时间线就永远不被检查。
3. **时间线只由父进程部署一次**：`--deploy-all` 给 6 个 worker 转发 `--no-timelines`，父进程在
   `RunParallel` 之后自己扫 mods + 建注册表 + `DeployTimelines(gameRoot, "chapter1")`。

**语义变化**：`--force` 现在只强制重建**主产物**；时间线按内容签名跳过（签名含它自己的 mod 子集 +
基底 data 的「路径+大小+mtime」指纹）。要连时间线一起强制重建，用新增的 **`--force-timelines`**。
（首次升级后签名公式变了，时间线会重建一次，之后稳定。）

**实测（全 mod 版，同一台机，v6 = 只修账本复制，v7 = 时间线改由父进程统一部署）**：

| 命令 | 改动前（v5） | v6 | v7 |
|---|---|---|---|
| `--deploy --chapter chapter1 --force`（第二次，主产物重建 + 时间线跳过） | 60-61 s | 21.9 s | **20.1 s** |
| `--deploy-all --force` | 163.0-168.1 s | worker 阶段 124.3 s | worker 阶段 **91.4 s**、整轮 **92.3 s** |
| `--deploy-all`（无 force，稳态） | 1.6 s | 2.9 s | 2.5 s |

证据：chapter1 第二次的日志是「主产物 19.3 s + 3 行
`[跳过] E:\...\ntl_timeline_*\data.win 内容未变（签名 …）—— 产物已是这个内容，无需重新部署`」；

> ★ **2026-09-29 起幂等判定不只看输入签名**：`.ntl-deploy-<章节>.sig` 是**两行**（第 1 行输入签名 = api 版本 + mods + 外部签名 + 基底指纹；第 2 行 = **产物内容指纹**，SHA-256 前 16 字节 + 字节数）。
> 两行都对得上（且产物齐全）才跳过；否则打印 `[重做] … 输入未变（签名 …），但产物内容对不上（记录 … / 当前 …）—— 产物被还原或替换过，必须重新部署` 并重建。
> 修前踩过的坑：`--restore-chapter` 还原产物后再 `--deploy-all` 会 6 项各 ~2.1 s 全打印「[跳过] 内容未变」，游戏里却没有面板与字体补全（`builder/Program.cs:2093-2125`）。
`--deploy-all --force` 的关键路径仍是 chapter5（v7 里 91.3 s），父进程再花约 1 s 检查 3 个时间线（全跳过），
日志里有 `===== 平行时间线（由父进程统一部署，worker 已跳过） =====`。

> 注意：`build=` 进签名 ⇒ **改了 builder 二进制，时间线会重建一次**（v7 首轮整轮 139.8 s 里含这次重建：
Forest / Lab 各约 11 s、timeline:9 约 10 s），之后恢复稳态。

