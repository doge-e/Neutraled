# 从零做一个属于你自己的章节

> 面向**零基础**：不需要懂 GML、不需要懂游戏引擎、不需要自己准备 `data.win`。
> 跟着做，你能在游戏里**走进一间自己造的屋子**，还能看到自己写的字画在屏幕上。

这份教程和第 1~7 步里的每条命令都是**可以直接复制粘贴**的。每步都写了「看到这个就成功了」。

---

## 第 0 步：先认识六个词（3 分钟）

| 词 | 意思 |
|---|---|
| **Neutraled / ntl** | mod 管理器。它把你的东西「叠」到游戏上，**不改动游戏原文件** |
| **章节 (chapter)** | 游戏里从上往下数的第 1/2/3/4/5 章。你要做的是**第 6 章之后的自定义章节** |
| **官方章节** | `chapter1_windows`、`chapter2_windows` … 这些目录，每个里面有一个 `data.win` |
| **data.win** | 整个章节的「数据包」：代码、地图、图片、字体全在里面。**不用你懂它**，脚手架会替你准备一份 |
| **mod** | 你做的东西。一个文件夹，里面有 `mod.json`（说明书）+ `gml/`（脚本） |
| **GML** | GameMaker 的脚本语言。你只要照着模板改几个字就行 |

你还只需要知道一件事：**这个脚手架默认是「独立章节」**——

* 它复制一份**官方第 1 章的原始数据**当底子（所以不需要你自己准备 `data.win`）；
* 它自带一张 640×480 的小房间地图（代码画出来的，不需要任何美术素材）；
* 它在游戏里注册成一个**新的章节条目**，有自己的名字、自己的存档目录。

---

## 第 1 步：准备（1 分钟）

按 `Win` 键 → 输入 `powershell` → 回车，打开 PowerShell，粘贴：

```powershell
cd E:\steam\steamapps\common\DELTARUNE\Neutraled
.\builder\bin\Release\net9.0\ntl-builder.exe --new-chapter help
```

看到这个就成功了（会列出两个模板和一堆可以当底子的 mod）：

```
游戏根: E:\steam\steamapps\common\DELTARUNE
用法：ntl-builder.exe --new-chapter <章节名字> [选项]
      ntl-builder.exe --new-chapter help            看这份帮助

选项：--author <作者>  --mode independent|overlay|fork  --chapter chapterN
      --slot N  --fork <mod>[:作者[:chapterN]]  --template room|empty  --force

可用模板（E:\steam\steamapps\common\DELTARUNE\Neutraled\templates）：
   - empty
   - room

可 fork 的现成章节（--fork 后面写 <mod目录名>[:作者[:chapterN]]）：
         可 fork 的 mod 章节目录：
           - AutoTest:Neutraled:chapter1   (id=neutraled.autotest.chapter1, chapter=chapter1)
           - AutoTest:Neutraled:chapter2   (id=neutraled.autotest.chapter2, chapter=chapter2)
           ... (最多列 40 行)
```

「可 fork 的现成章节」= 你机器上**已经装好的 mod 章节目录**，用 `--mode fork` 可以把其中一个整份复制成你自己的新章节
（详见第 8 步 B）。列表为空说明这台机器还没装过 mod 章节，那就用默认的 `independent` 模式。

> **提示**：`--new-chapter help` 任何时候都能再查一次用法。

---

## 第 2 步：一条命令生成章节（2 分钟）

```powershell
.\builder\bin\Release\net9.0\ntl-builder.exe --new-chapter "我的房间" --author 你的名字
```

看到这个就成功了：

```
===== 章节已生成 =====
  章节名   : 我的房间   （章节选择器里显示的就是它）
  顺序号   : 9     （章节选择器里的位置；改 mod.json 的 order 就能改）
  mod 目录 : Neutraled\mods\我的房间\你的名字\chapter1
  基底数据 : Neutraled\mods\我的房间\你的名字\data\data.win ← 官方 chapter1
  房间地图 : Neutraled/kristal-maps/ntl_<一串字母数字>_room.png + .map.json
  日志     : %LOCALAPPDATA%\DRTL<顺序号>_<短码>\Neutraled\dr-api.log   ← 章节进程（= 本 mod 的存档目录）
             %LOCALAPPDATA%\DELTARUNE\Neutraled\dr-api.log   ← 章节选择器/根阶段
下一步（一条一条复制执行）：
  1) ntl-builder.exe --deploy --chapter root --force           生成章节产物（平行时间线会自动一起生成）
  2) 启动 DELTARUNE（Steam）→ 章节选择器里翻到你的章节
  教程：Neutraled/docs/CHAPTER_DEV.md
```

**这个 <一串字母数字> 是什么？** 游戏内部不能用中文当目录名/存档名，所以脚手架把你的章节名压成一个
固定的 ASCII 短码（同样的名字永远得到同样的短码）。它叫 **slug**，下面到处会用到它。

常用参数（都可以不写）：

| 参数 | 默认 | 说明 |
|---|---|---|
| `--author <作者>` | `Player` | 作者名。它就是 mod 目录里的二级文件夹名 |
| `--slot <N>` | 自动（现有最大 +1） | 章节顺序号。**改动它会改变章节在列表里的位置** |
| `--chapter chapterN` | `chapter1` | 复制哪一章的原始数据当底子（第 4 章有 135 MB，别乱用） |
| `--template room\|empty` | `room` | `room` 带一间小房间；`empty` 只给你一个最小骨架 |
| `--force` | 关 | 目标目录已存在时先删掉重建 |

---

## 第 3 步：生成的每个文件是干什么的（5 分钟）

用**记事本**或 **VS Code** 打开上面打印的 `mod 目录`，你会看到：

```
mod.json                                  说明书（Neutraled 靠它认出这是一个章节）
README.md                                 你这个章节的速查说明（脚手架生成的）
data\
  data.win                                ★ 章节底子（官方第 1 章的原始数据副本，12 MB）
gml\
  main.gml                                ★ 入口脚本：游戏进入这一章时执行一次
  ch_<slug>_hooks.gml                     挂事件回调（入口脚本调一次，live 桥再补一次；同帧去重）
  ch_<slug>_frame.gml                     每帧执行（判断「走出房间」「按了交互键」）
  ch_<slug>_draw.gml                      ★ 画 HUD（右下角那块面板就是它画的）
scripts\
  ch_<slug>_live.lua                      只有 2 行：在 live 的 on_init 里补挂一次订阅（可选，见下）
files\
  Neutraled\kristal-maps\
    ntl_<slug>_room.png                   房间底图（脚手架的代码画的，没有美术素材也能跑）
    ntl_<slug>_room.map.json              房间的碰撞、出生点、告示牌、出口定义
```

一句话说明它们的关系：

* `main.gml` 只做「准备工作」：设几个全局变量 → `ntl_player_test("地图id")` 把玩家放进房间 →
  调用 `..._hooks.gml` 把另外两个脚本挂到**每帧**和**每帧绘制**上；
* `..._frame.gml` 是「逻辑」：读玩家坐标、写日志、判定目标；
* `..._draw.gml` 是「画面」：画文字。**想改屏幕上的字，就改这个文件。**
* `..._live.lua` 是「补挂订阅的小桥」（**可选**）：因为它是一个 **Lua** 文件，你会好奇为什么混进来 ——
  原因见下面「为什么还有一个 Lua 文件」那一段。你不用碰它。

> **为什么文件名带 ch_<slug>？** GML 里脚本名 = 文件名。脚手架把短码写进文件名，
> 保证你的脚本不会和游戏自带脚本（或别人的 mod）重名。

> **中文注意**：游戏主字体只有 96 个 ASCII 字形，中文会整片画不出来。模板里已经用
> `asset_get_index("ntl_font_cjk")` 切到中性化自带的中文字体，并且**每次绘制都重设一次**。
> 你自己加绘制代码时也要这么做。

> **怎么知道回调真的挂上了？** 每个脚本都会往日志里写一行自检（日志路径见第 7 步）：
> `main.gml` 与 `..._hooks.gml` 写「挂回调：on_frame=0 (脚本 …)、on_draw=0 (脚本 …)」——**号码是 -1 就说明没挂上**
> （`ntl_hook()` 自己也会留痕：成功打 `[hook] [订阅] on_frame（第 1 个订阅者）`，失败打 `[hook] [订阅失败] …`）；
> `..._live.lua` 写「live 桥：ch_<slug>_hooks() -> 1」（`-> 1` 表示两个回调都挂上了）；
> `..._frame.gml` 与 `..._draw.gml` 各写一行「每帧回调已被调用」「绘制回调已被调用」。
> **只有看到最后这两行，才说明你的画面/逻辑代码真的在跑**（而不是写了却没人调用）。
>
> **钩子必须用 `asset_get_index` 取脚本再挂**：模板里写的是
> `var _fn = asset_get_index("ch_<slug>_draw"); ntl_hook("on_draw", _fn);`
> 如果直接写裸脚本名（`ntl_hook("on_draw", ch_<slug>_draw)`），mod 脚本单独编译时可能解析不到，
> `ntl_hook` 会**静默**返回 -1、什么也不报，你的绘制代码一辈子都不会被执行。

> **为什么还有一个 Lua 文件？（2026-09-26 已修 —— 它不是绕 bug）**
> 这里**曾经**是 Neutraled 的一个加载顺序问题：`api/scr_ntl_init.gml` 先执行各 mod 的入口脚本
> （第 91 行 `ntl_run_mods()`，钩子在这时挂上），紧接着第 104 行调用 `ntl_hook_init()`，
> 而当时的 `api/ntl_hook_init.gml` 第 2 行是 `global.ntl_hooks = ds_map_create();` —— **把钩子表整个重建了**，
> 入口脚本挂的 `on_frame`/`on_draw` 会被丢掉（日志：`[hook] 已注册 0 个 hook（覆盖 0 个脚本）`）。
>
> **这一条已经修好了**（2026-09-26）：现在 `api/ntl_hook_init.gml:9` 是
> `if (!variable_global_exists("ntl_hooks")) global.ntl_hooks = ds_map_create();`（**幂等建表**），
> `:12-13` 用 `global.ntl_hook_reg_keys` 记住「注册表自己占用的 key」，`:39-50` **只按 key 精确重置这些槽位** ——
> mod 的事件订阅槽不在其中，所以**不会再被清空**。初始化跑完时会多打一行：
> `[hook] 事件订阅槽: 1 个（来自 mod 入口脚本的 ntl_hook()，未被初始化清空）`。
>
> **所以现在推荐的做法就是直接在入口脚本（或它调用的 hooks 脚本）里挂**：
> ```gml
> ntl_hook("on_frame", asset_get_index("ch_<slug>_frame"));
> ntl_hook("on_draw",  asset_get_index("ch_<slug>_draw"));
> ```
> 模板生成物里的 `gml/ch_<slug>_hooks.gml` 就是干这个的（入口脚本调用它一次）。
>
> **那 `scripts/ch_<slug>_live.lua` 还要不要？** 要，但它不再是「绕 bug」的必需品，而是两件事：
> ① 兼容 **2026-09-26 之前**的旧 api 产物（旧版才会清空订阅槽，那时的日志里没有 `事件订阅槽` 那一行）；
> ② live 运行时会多次广播 `on_init`（真机日志 `[live] [Step] on_init 已广播`），live 侧重载脚本时
> 这条线会把订阅补回来。两条路同时挂也不会双跑 —— 回调按帧号做了同帧去重。
> 不想要它：删掉 `scripts/` 目录与 `mod.json` 的 `scripts` 段，章节照常工作（前提是 api 是 09-26 之后的版本）。
> **回调代码仍然全部是 GML**，这个 Lua 文件只是一根「什么时候补挂」的引线。
>
> **⚠ 去重变量必须每个回调各用一个**（脚手架已经帮你分好了：`global.ntl_ch_<slug>_lastf` 给每帧回调、
> `global.ntl_ch_<slug>_lastd` 给绘制回调）。如果图省事两个回调共用一个去重变量，会出现这种情况：
> 同一帧里 **Step（每帧回调）先跑**、把它设成本帧号，紧接着 **Draw（绘制回调）后跑**、读到同一个帧号就以为
> 「这一帧已经跑过了」直接 `return` —— 结果是**绘制回调一帧都不会执行**，面板永远不出现，日志里也永远
> 没有「绘制回调已被调用」那一行。这个坑脚手架踩过，所以模板里有注释提醒。
>
> **加自己的回调时**：把代码写进 `gml/ch_<slug>_frame.gml`（每帧）或 `gml/ch_<slug>_draw.gml`（绘制）
> 就够了 —— 这两条线已经接通。也可以直接在 `main.gml` 里写
> `ntl_hook("on_frame", asset_get_index("ch_<slug>_frame"))`（2026-09-26 起这是推荐做法；
> 与 hooks 脚本两条路同时挂也不会双跑，回调有同帧去重）。

---

## 第 4 步：部署进游戏（3 分钟）

```powershell
.\builder\bin\Release\net9.0\ntl-builder.exe --deploy --chapter root --force
```

（**要关掉游戏再跑**，否则 exe 被占用会报错。）

看到这几行就成功了：

```
===== 平行时间线 / 引用章: 1 个 =====
--- timeline:9:ntl_chapter_<slug>:<slug> 「我的房间」 <- 自带data（运行时文件取自 chapter1）+ 1 个 mod ---
  运行时文件复制: 43 个
  [存档] 独立存档名: DELTARUNE -> DRTL9_xxxxxxxx（FileName + Name）
  存档目录联接: C:\Users\<你>\AppData\Local\DRTL9_xxxxxxxx -> ...\Neutraled\saves\DRTL9_xxxxxxxx
  章节注册表: 10 条 -> ...\Neutraled\chapters.json
===== 时间线部署完成: 1/1 =====
```

这次部署**干了三件事**：

1. 在游戏根目录生成了一个**新的章节目录**（名字形如 `ntl_timeline_9_...`），里面有独立的 `data.win`；
   目录名里必须带 `chapter` 字样，否则游戏会以为自己还在主菜单。
2. 把新章节写进 `Neutraled\chapters.json`（游戏里的章节列表就是读这个文件）。
3. 给这个章节准备**独立存档目录**（所以你的章节存档不会和官方章节串味）。

> **每次改了 `gml/` 或地图之后，都要重新跑一次这条命令**，游戏里才会看到变化。

> **还要不要一直加 `--force`？现在不必了（2026-09-28 起）** 部署的"内容没变就跳过"看的签名里，
> 除了 mod 的 id + 版本号 + 基底 data.win 指纹，还有**你 mod 目录里每个文件的相对路径 + 大小 + 修改时间**
> （含 `gml/*.gml`、地图、`ref/data.win`；自动排除 `.backup*`/`*.bak*`/`.tmp`/`bin`/`obj`，否则部署自己写的备份会让签名每轮都变）。
> 所以改完脚本直接 `--deploy` 就会重做，只有"签名没变但你就是想重建"时才需要 `--force`。
> （早期版本的签名不含 `gml/*.gml`，那时确实必须一直加 `--force` —— 这条经验现在过期了。）
> 平行时间线子产物同理按签名跳过；要把它们也一起强制重建，加 `--force-timelines`。

---

## 第 5 步：进游戏看效果（3 分钟）

先确认没有别的 DELTARUNE 开着，然后**必须从 Steam 启动**（直接双击 exe 可能起不来）：

```powershell
Start-Process 'E:\DELTARUNE.url'
```

关掉开场画面后是**章节选择器**，用键盘操作：

| 键 | 作用 |
|---|---|
| ↑ / ↓ | 同一页里换一个章节 |
| ← / → | 翻页（每页 7 个章节） |
| 回车 或 Z | 启动选中的章节 |

**看到这个就成功了**：屏幕上出现一间灰蓝色的小房间，右下角有一块半透明面板，写着

```
<你的章节名> · Neutraled 自定义章节
第 123 帧    地图 ntl_<slug>_room    order <顺序号>    基底 <基础章节号>
方向键 / WASD 移动     E / Z 交互     F2 控制台
从右墙上的金色缺口走出去试试  →
x=100 y=100   交互 0 次   最近对象 Spawn
改 gml/ch_<slug>_draw.gml 就能改这块 HUD    日志 %LOCALAPPDATA%\DRTL*\Neutraled\dr-api.log
```

> 上面只是示例：**章节名**就是你 `--new-chapter` 时给的名字，`order` / `基底`
> 对应第 4 步输出里的「顺序号」与它用的官方基础章节。

> **懒得点选择器？** 打开 `Neutraled\config.json`（**游戏根**那份），把这两行写进去，游戏启动就会直接进你的章节
> （`<章节id>` 从 `Neutraled\chapters.json` 里抄，形如 `timeline:9:...`）：
> ```json
> "auto_skip_selector": true,
> "auto_chapter_id": "<章节id>"
> ```
>
> 改完**不用手动同步**：启动时游戏会把游戏根那份镜像到存档区 `%LOCALAPPDATA%\DELTARUNE\Neutraled\config.json`。
> 章节选择器跑在**带 GameMaker 文件沙箱的 root 产物**里，它只看得到存档区那份（两份副本的来龙去脉见
> [MANAGE.md](MANAGE.md) 的「config.json 的两份副本」）。

---

## 第 6 步：改一行属于你自己的字（5 分钟）

用记事本打开 `gml\ch_<slug>_draw.gml`，找到这一行（大约在第 26 行）：

```gml
draw_text(24, 346, "我的房间 · Neutraled 自定义章节");
```

把引号里的字改成你想要的，例如：

```gml
draw_text(24, 346, "这是我做的第一章 —— 你好，世界！");
```

**保存**，关游戏，再跑一次第 4 步那条部署命令，重新进章节 → 文字变成你的了。

> 想改数字、改颜色、加一行，都在这个文件里试。改坏了也不要紧：`--new-chapter ... --force`
> 可以重新生成一份干净的（会覆盖你所有的改动，注意备份）。

---

## 第 7 步：走出房间（3 分钟）

按住 **→**（或 `D`）一直往右走。右墙中间有一段**金色缺口**，穿过去以后：

* 右下角那行提示会变成绿色的 `>>> 你走出房间了！站在出口按 E 完成这一章`；
* 同一瞬间，游戏自己截了一张图，并在日志里写了一行。

站在出口按 **E**（或 `Z`）→ 提示变成 `★ 完成：走出了房间（x 到 602）并在出口按了 E`。

**证据在哪：**

| 东西 | 位置 |
|---|---|
| 日志 | **章节自己的日志**：`%LOCALAPPDATA%\DRTL<顺序号>_<短码>\Neutraled\dr-api.log`（就是你的存档目录；不知道是哪个就在 `%LOCALAPPDATA%` 里找最新的 `DRTL*` 文件夹）。根目录/选择器阶段的日志在 `%LOCALAPPDATA%\DELTARUNE\Neutraled\dr-api.log` |
| 游戏自己截的图 | 你的章节**存档目录**里（`...\Neutraled\saves\DRTL9_xxxxxxxx\`），文件名 `ch_<slug>_out_of_room.png`、`ch_<slug>_done.png` |
| 游戏内控制台 | 按 **F2** 打开/关闭（可以敲命令、看日志） |

日志里应该能找到这两行：

```
[chapter] [我的房间] 玩家走出了房间 @(610,220)
[chapter] [我的房间] ★ 完成：走出房间并在出口交互（ntl_<slug>_room）
```

---

## 第 8 步（可选）：拿现成的东西当底子

### A. 以**官方某章**为底子

```powershell
.\builder\bin\Release\net9.0\ntl-builder.exe --new-chapter "我的第二章" --chapter chapter2 --slot 10
```

只是换了底子数据（`--chapter chapter2`）；玩法、房间、脚本完全一样。

### B. 以**已安装的 mod 章节**为底子 fork

先看有什么可以 fork：

```powershell
.\builder\bin\Release\net9.0\ntl-builder.exe --new-chapter help
```

然后（`--fork` 会自动切到 fork 模式）：

```powershell
.\builder\bin\Release\net9.0\ntl-builder.exe --new-chapter "我的分支" --fork AutoTest:Neutraled:chapter1
```

它会把那个章节的 `mod.json / gml / sprites / sounds / data` 整个复制一份，改掉 id / 名字 / 作者，
并保留它自带的 `data.win`（所以 fork 出来的章节和原章节**内容一样、但互不影响**）。

### C. 叠加到**官方第 N 章**上（不产生新章节条目）

```powershell
.\builder\bin\Release\net9.0\ntl-builder.exe --new-chapter "我的补丁" --mode overlay --chapter chapter3
```

`overlay` 模式不生成新章节，而是让脚本**在官方第 3 章里生效**（`.\ntl-builder.exe --deploy --chapter chapter3 --force`）。
这时模板的 HUD 会画在官方章节上面，用来做「改造官方章节」的 mod。

---

## 排错表（照着查）

| 症状 | 原因 | 怎么办 |
|---|---|---|
| 章节列表里**没有**我的章节 | 没部署 / 部署失败 | 重跑第 4 步；检查 `Neutraled\chapters.json` 里有没有你的名字 |
| 进了章节**黑屏或没反应** | 地图没进产物目录 | 检查 `<游戏根>\ntl_timeline_*\Neutraled\kristal-maps\` 里有没有 `ntl_<slug>_room.png` 和 `.map.json` |
| 面板不出现，但游戏能玩 | 脚本没进 data.win | 部署日志里应该有 `mod 脚本: 4 个（入口: main）`；如果是 0 个，说明 `gml/` 位置不对（必须在 `gml/` 子目录里） |
| 房间能走，但**面板和自检日志都没有** | 回调没挂上（脚本没进产物，或 `asset_get_index` 没解析到） | 日志里搜 `挂回调：on_frame=` 与 `[hook] [订阅]`：号码是 `-1` 就是没挂上。★ 若日志里出现 `[hook] 已注册 0 个 hook（覆盖 0 个脚本）` 且**没有** `[hook] 事件订阅槽: N 个…` 那一行，说明用的是 **2026-09-26 之前**的 api 部署 → 重跑第 4 步（加 `--force`）；来不及重部署就先靠 `mod.json` 的 `"scripts": { "on_init": "scripts/ch_<slug>_live.lua" }` 那条 Lua 桥兜底 |
| 中文显示成方块/空白 | 没切中文字体 | 每次 `draw_text` 前都要 `draw_set_font(asset_get_index("ntl_font_cjk"))` |
| 改了脚本没变化 | 没重新部署 | 关游戏 → 重跑第 4 步 |
| `--deploy` 报文件占用 | 游戏还开着 | 关掉游戏 |
| 生成时报「目录已存在」 | 同名章节已经有了 | 加 `--force`（**会删掉原目录**） |
| 章节顺序号冲突 | 两个章节用了同一个 `--slot` | 换一个号，或干脆不写 `--slot`（自动取最大 +1） |
| 日志写着写着**就不再新增**了（游戏还活着） | 有别的脚本每隔几秒去读这个日志文件 | `api/ntl_log.gml` 里的 `global.ntl_log_ok` 是**一次性开关**：只要有一次打开文件失败，之后所有日志都只进调试输出、不再写文件。写自动化测试时读日志要用共享方式读（`[IO.File]::Open($p,'Open','Read','ReadWrite')`），或者干脆别读 |
| 完全不知道哪出错了 | — | 先看日志 `%LOCALAPPDATA%\DELTARUNE\Neutraled\dr-api.log`（章节里则看存档目录里的那份，见第 7 步；游戏内按 **F2** 也能看） |

日志路径（记不住就复制这两段）：

```powershell
# 根目录 / 章节选择器阶段的日志
notepad "$env:LOCALAPPDATA\DELTARUNE\Neutraled\dr-api.log"

# 你自己那一章里的日志（章节一启动就换到存档目录了）
gci "$env:LOCALAPPDATA\DRTL*\Neutraled\dr-api.log" | sort LastWriteTime -Desc | select -First 1 -Expand FullName
```

---

## 速查：我做一章要敲的命令

```powershell
cd E:\steam\steamapps\common\DELTARUNE\Neutraled

# 1. 生成（只要做一次）
.\builder\bin\Release\net9.0\ntl-builder.exe --new-chapter "我的房间" --author 你的名字

# 2. 改了 gml/ 或地图之后：部署（关着游戏跑）
.\builder\bin\Release\net9.0\ntl-builder.exe --deploy --chapter root --force

# 3. 进游戏
Start-Process 'E:\DELTARUNE.url'
```

---

## 附：脚手架到底做了什么（想知道原理再看）

1. 在 `Neutraled\mods\<章节名>\<作者>\chapter<N>\` 建目录。
   * 叶子目录名**必须**是 `chapter1`~`chapter7`，或 `root`（只对章节选择器/根进程生效）。
     ⚠ 叶子名 = **这个 mod 进哪个产物**（`root` 是目标名，**不是**「对所有章节生效」的通配符）：
     构建器用 `effChapter == 目标名` 精确匹配（`builder/Mods.cs:357-359`，合法叶子见 `builder/Mods.cs:454-456`），
     运行时按同一目标名找 `mods/<mod>/<作者>/<目标>/mod.json`（产物作用域写在产物目录的 `Neutraled/scope.json`）；
   * `mod.json` 里的 `chapters` 声明写成**时间线条目**：
     ```json
     { "timeline": true, "order": 9, "name": "我的房间", "slug": "c3a8e36c4" }
     ```
     它告诉 Neutraled「这是一个自带存档、自带 data.win 的平行章节」。
2. 复制一份**官方原版数据**到 `<mod>\data\data.win`。
   Neutraled 找自带数据的顺序是：`data/<章节名>/data.win` → `data/data.win`。脚手架统一用后者，
   这样**改章节显示名不会把数据弄丢**。
3. 把模板里的 `*.tmpl` 展开（替换 `{{NAME}}`、`{{SLOT}}`、`{{PREFIX}}` 等占位符）成真正的文件。
   文件名里的占位符也会被替换 —— 这就是 `{{PREFIX}}_frame.gml.tmpl` → `ch_xxxx_frame.gml` 的来历。
4. 生成房间底图和地图 JSON，**同时写两份**：
   * `<mod>\files\Neutraled\kristal-maps\`（部署时会整棵树覆盖进章节产物目录 ← 真正生效的那份）
   * `<Neutraled>\kristal-maps\`（开发时手动看地图用）
5. 生成的入口脚本里带一段**守卫**：

   ```gml
   if (string_pos("ntl_timeline_", string_lower(string(working_directory))) <= 0) { ... exit; }
   ```

   因为同名的 `gml/*.gml` 会在 `--deploy-all` 时被注入到**官方那一章**里去；
   守卫保证它只在「自己的章节产物」里才初始化，不会把官方章节搞乱。

6. （写到一半想调 API 时再看）**Neutraled 的宿主接口参数是一个数组**，不是变参：

   ```gml
   ntl_call_host("set_lang_string", ["ui.probe", "PROBE-ZH"]);   // ✓ 对
   ntl_call_host("set_lang_string", "ui.probe", "PROBE-ZH");     // ✗ 静默全空，还不报错
   ```

   原型在 `api/ntl_call_host.gml:2-5`：`argument[0]` 是函数名、`argument[1]` 是参数**数组**。写成变参会
   `array_length` 得到 0，于是每个分派臂都收到空参数、日志上看起来"调用成功了"。

7. 为什么生成物里有一个 Lua 文件（历史 + 现状）：2026-09-26 之前的 `api/ntl_hook_init.gml` 第 2 行会
   **无条件**重建 `global.ntl_hooks`，把入口脚本挂的订阅整体丢掉（`ntl_run_mods()` 在 `api/scr_ntl_init.gml:91`，
   `ntl_hook_init()` 在 `:104`）。现在该函数是幂等的：`:9` 只在表不存在时建表、`:12-13` 记 key、
   `:39-50` 只按这些 key 精确重置 —— **入口脚本直接挂就是推荐路径**；Lua 桥保留只为兼容旧 api 产物
   与 live 重载时补挂订阅（同帧去重保证不双跑）。相关源码位置：`api/ntl_hook_init.gml:9/12-13/39-50/119-120`、
   `api/ntl_hook.gml:18`（订阅留痕）、`api/scr_ntl_init.gml:91`（`ntl_run_mods()`）、`:104`（`ntl_hook_init()`）、
   `api/ntl_live_init.gml:114`（要 `scripts` 键）、`:123`（mod 目录）、`:145`（`on_init`）、
   `api/ntl_live_emit.gml:28`（`dir + 相对路径`）、`api/ntl_call_host.gml:336`（Lua 调任意 GML 脚本的兜底）。
