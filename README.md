# DRMapEditor / ntl-mapedit

DELTARUNE（GameMaker 2023.6 / data.win）的房间编辑器：GUI + 命令行。用于查看与修改房间、图层、实例、瓦片、背景层属性，并把改动写回 data.win；同时支持在地图上给实体绑定战斗、纯对话、简单剧情，以及编辑整包 mods.json 清单。参考实现是 Celeste 的 Loenn，按 DELTARUNE 的数据模型重做（DR 没有 Celeste 的 map 级 bin，也没有 autotile 元数据）。

- 工程目录：`E:/aiwork/out/DRMapEditor/`
- 源码：`src/ntl-mapedit/`
- 参考：`ref/loenn-src/`（Loenn 的 tools/*.lua，只读参考）
- 产物/测试输出：`tmp/`
- 依赖：.NET 9（`ntl-mapedit.csproj` 为 `net9.0-windows`，WinForms），UndertaleModLib（项目自带的引用 DLL，见 csproj）

## 构建 / 运行

```powershell
& "C:/Program Files/dotnet/dotnet.exe" build E:/aiwork/out/DRMapEditor/src/ntl-mapedit/ntl-mapedit.csproj -c Release
$exe = 'E:/aiwork/out/DRMapEditor/src/ntl-mapedit/bin/Release/net9.0-windows/ntl-mapedit.exe'
& $exe gui 'E:/steam/steamapps/common/DELTARUNE/backup/chapter1_windows/data.win'
```

> 构建前先 `Get-Process ntl-mapedit | Stop-Process -Force`：GUI 进程会锁住 exe，导致 MSB3021/MSB3027。

测试统一用离线副本，**不要指向游戏正在用的 data.win**：

```powershell
& $exe document-test  'E:/steam/steamapps/common/DELTARUNE/backup/chapter1_windows/data.win' 'E:/aiwork/out/DRMapEditor/tmp/doc-test-out.win'
& $exe structure-test 'E:/steam/steamapps/common/DELTARUNE/backup/chapter1_windows/data.win' 'E:/aiwork/out/DRMapEditor/tmp/gui-test/ch1-struct-test.win'
& $exe stress-test    'E:/steam/steamapps/common/DELTARUNE/backup/chapter1_windows/data.win' 'E:/aiwork/out/DRMapEditor/tmp/gui-test/ch1-stress.win'
& $exe battle-test    'E:/steam/steamapps/common/DELTARUNE/backup/chapter1_windows/data.win' 'E:/aiwork/out/DRMapEditor/tmp/battle-test'
& $exe manifest-test
```

## GUI 功能

- 左侧：房间列表（名字 + `宽×高`，带过滤框）、图层列表（点眼睛图标切换可见性）。
- 中间：画布（滚轮缩放、`F` 适应窗口、拖拽平移、网格/辅助线显示开关、`V/H` 等工具）。
- 右侧：属性面板（可滚动），按当前选择切换：
  - **房间**：名称、宽度/高度（16–10000，提交后左侧列表/标题/状态栏同步）、背景色、绘制背景色。
  - **目标图层**：名称/类型只读、深度（改完自动按深度重排图层）、偏移 X/Y、图层可见。
  - **背景层**：精灵只读 + 「选择背景精灵…」、背景可见/水平平铺/垂直平铺/拉伸铺满、背景色。
  - **图块**（用瓦片工具画/替换后，或 Alt+点击拾取）：X/Y、宽/高、源 X/Y、缩放 X/Y、颜色、Tile ID。
  - **单选实例**：顶部依次「战斗」段（未绑定 →「为此实体新建战斗…」；已绑定 →「编辑战斗…」「解除绑定」）、「对话」段（「为此实体新建纯对话…」/「编辑对话…」「解除绑定」）、「剧情」段（「为此实体新建剧情…」/「编辑剧情…」「解除绑定」），然后是 X/Y、缩放 X/Y、旋转、图像帧、颜色（可编辑）。
  - **多选实例**：批量删除、「对齐 / 分布」8 个按钮（左/中X/右/顶/中Y/底/分布X/分布Y）。
  - **底部**：瓦片笔刷列表 + 「选择图块来源…（精灵帧内选区域）」+「瓦片调色板…（点格子选笔刷）」+ 对象列表（选择后进入放置工具）。
- **资源浏览器（只读，`F4`）**：视图 → 资源浏览器…，左侧 12 类资源（房间 / 对象 / 精灵 / 背景瓦片集 / 字体 / 音效 / 脚本代码 / 着色器 / 序列 / 时间线 / 纹理页 / 路径），右侧列表 + 底部完整字段。之前这些资源（尤其字体、音效、数万段代码、着色器、路径）完全没有入口；现在可以查名字、数量、尺寸、EmSize/字形数、文件/音量、代码字节/指令数、着色器各语言源码长度等，方便复制 id 或判断资源是否存在。只读：不写 data.win。`resource-browser-test` 用同一套 `ResourceCatalog` 做无 GUI 自检。

### 画布工具

| 工具 | 按键 | 行为 |
|---|---|---|
| 选择 | `V` | 点选/框选实例；拖动移动；方向键微移（Shift = 8px） |
| 平移 | `H` | 拖动画布（空格临时平移） |
| 放置 | `B` | 把对象列表里选中的对象放到房间 |
| 擦除 | `X` | 点/框选删除实例 |
| 瓦片 | `T` | 左键拖动连续绘制、右键连续擦除、`Alt+点击` 拾取图块 |
| 矩形 | `R` | 拖出矩形整片填充；右键整片擦除 |
| 直线 | `L` | 拖出直线整片填充；右键整片擦除 |
| 圆形 | `C` | 圆心拖出半径（取两轴较大者）整片填充；右键擦除 |
| 椭圆 | `E` | 拖出包围盒整片填充；右键擦除 |
| 油漆桶 | `K` | 连通区域整片替换；右键整片擦除 |
| 瓦片选择 | `S` | 点选/框选图块、拖动移动、`Ctrl+C/X/V` 复制粘贴、`Delete` 删除 |

瓦片笔刷：整帧精灵、帧内任意矩形子区域、或调色板里某个格；调色板是非模态窗口，点格子即换笔刷，可边看边画。

### 快捷键（完整）

- 文件/编辑：`Ctrl+O` 打开、`Ctrl+S` 保存、`Ctrl+Shift+S` 另存、`Ctrl+Z` 撤销、`Ctrl+Y`/`Ctrl+Shift+Z` 重做。
- 选择：`Ctrl+A` 全选（瓦片选择工具下是「全选图块」）、`Ctrl+I` 反选、`Ctrl+C/X/V/D` 复制/剪切/粘贴/克隆、`Delete` 删除选中、`Esc` 清空选择。
- 房间：`Ctrl+N` 新建（复制当前房间）、`Ctrl+Shift+N` 新建空白房间、`Ctrl+Shift+D` 直接复制房间、`F2` 重命名、`Ctrl+Delete` 删除房间。
- 图层：`Ctrl+Shift+L` 新建实例层、`Ctrl+Shift+T` 新建瓦片层、`F3` 重命名、`Ctrl+Shift+↑/↓` 上移/下移图层。
- 视图：滚轮缩放、`Ctrl+0` 1:1、`F` 适应、`G` 网格、`F5` 重绘、`F4` 资源浏览器（只读）。
- 工具：`V H B X T R L C E K S`（同上表）。
- 实体脚本：`Ctrl+B` 战斗、`Ctrl+Shift+B` 纯对话、`Ctrl+Shift+K` 简单剧情（都要先选中一个实例）。

## 战斗编辑（绑定地图实体）

战斗不是单独工具，而是地图实体属性：在地图上选中一个实例 → 右侧属性面板顶部「战斗」→「为此实体新建战斗…」（等价快捷键 `Ctrl+B`）。触发房间/坐标直接取自该实体（保存时写入 sidecar），不用手填。

- 战斗编辑对话框分页：
  - **基本**：id、名称、类型（`encounter` = 进原版战斗界面 / `overworld` = 地图上直接躲弹幕）、启用、开场文字、回合上限（0 = 不限，达到后判胜利）、循环最后一回合、初始紧张度、战斗前延迟帧数。
  - **敌人**（encounter）：最多 3 个敌人；对象（data.win 里的敌人对象或生成的 `ntlb_*` 自定义敌人）、名称、HP/AT/DF/EXP/Gold、宽恕值/上限、状态、评论、ACT（逗号分隔）。
  - **回合 / 弹幕**：回合列表（增删/上下移），每回合有文字、持续帧数、弹幕组（type / 数量 / ratio / 伤害(0 = AT×5) / timermax / joker / side / graze / time / 随机 type 列表 / ratio 浮动 / 延迟）。随机 type 非空时每回合从列表里随机挑一个模板。
  - **Overworld 弹幕**：模式（danmaku / encounter）、持续帧数、接触伤害、敌人半径、敌人精灵，以及弹幕 pattern 列表（kind `rain|ring|aim|wall` / interval / count / speed / spread / damage / life / angle / 随机角度/数量/速度）。
  - **触发 / 结局**：绑定房间/实例（只读）、需要 flag、触发半径（0 = 进房间即触发）、只触发一次；胜利与失败的 setFlags / clearFlags / healPercent / gotoRoom / startBattle / message。
- 「触发 / 结局」页还有：**触发方式**（`auto` = 条件满足自动开战；`interact` = 还要按 Z/Enter，并用原版 `obj_mainchara` 四向交互矩形判定）、**跨重启只触发一次**（`persistOnce`，写 Neutraled 的 mods-shared.json，重启游戏也不重复）；**初始紧张度**运行时在战斗首帧写入 `global.tension`（上限 250）；**开局文字**会作为屏幕文本提示显示；**队伍全灭判失败**并执行 defeat effects（同时交给原版 game over）；结束 effects 的 `message` 现在也会以屏幕文本显示（约 150 帧）。
- 保存主文档时，战斗/对话/剧情/设置配置写到 **`<data.win>.ntlbattle.json`**（sidecar，和地图文件放一起），不污染 data.win。
- 菜单 **战斗 → 生成战斗 mod…**：先弹窗选部署叶子（root/chapter1..5），再把全部配置导出成可部署的 Neutraled mod（默认写到 `游戏根/Neutraled/mods/battle.demo/<author>/<leaf>/`），包含 `mod.json`、`gml/`（战斗/弹幕/overworld/对话/剧情/设置/控制台命令的运行时函数 + 内嵌数据 `ntlb_data.gml` + `main.gml`）、`objects/`（敌人/子弹/overworld 控制器事件文件）、`files/Neutraled/ntl_battle/battles.json`、README。
- 生成 mod 的运行时：`ntl_hook("on_frame", ...)` 检查触发器（房间 + flag + 半径 + 触发方式，一次性走 `ntlb_once_seen/mark`：进程内 ds_map + 可选跨重启持久化）→ 设 `global.monster*` 后创建原版 `obj_encounterbasic` + `obj_battleback` 走完整原版战斗；每回合按配置创建原版 `obj_dbulletcontroller` 并设置 type/ratio/timermax/damage 等；回合上限/全灭判胜利 → 应用剧情 effects（flag/heal/传送/接下一场战斗/消息）。overworld 模式则生成自己的控制器与子弹对象，命中调 `scr_damage_all_overworld()`，计时/HP 结束。
- 命令行：`ntl-builder --deploy --chapter <N> --self-check` 即可把生成的 mod 编译进对应章节产物。
- **弹幕 type 是「目标章节」原版 `obj_dbulletcontroller` 的模板编号**：不同章节同号形状不同（章节 2 的 26/27 是 Rouxls 专用，别的章节用会崩）。跨章节最稳是 `0`（红心周围环形）与 `1`（上下边缘）；章节专用模板按该章反编译挑选。
- 自定义敌人对象由生成器提供（`ntlb_<modid>_enemy`，含标准敌人变量与 `Other_22` 事件），不依赖原版敌人；原版敌人也可直接填对象名（需该章节产物里存在）。

## 纯对话编辑（绑定地图实体）

和战斗一样，交互后纯对话也是地图实体属性：选中实例 → 属性面板「对话」段 →「为此实体新建纯对话…」，或 `Ctrl+Shift+B`。触发房间/坐标取自实体，运行时由生成 mod 在玩家进入半径（或走到触发点）时创建原版 `obj_dialoguer`（位置由它自己按视角算）并逐行翻页。

- 对话编辑对话框字段：对话 ID/名称/启用、**触发方式**（`interact` = 靠近按 Z；`area` = 进半径自动播放）、说话人、文本行（多行，每行一句，Z 翻页，末行后自动结束）、需要 flag、触发半径（默认 64）、只触发一次、**跨重启只触发一次**、结束效果（设置/清除 flag、治疗百分比、传送到房间、结束提示文字；结束提示现在会上屏）。
- **说话人**：原版对白框没有名字栏，生成 mod 把 `说话人：` 前缀拼在第一行正文前；留空则只显示正文。
- `interact` 模式现在用原版 `obj_mainchara` 的四向交互矩形近似判定（见 `ntlb_in_interact_zone`），比纯半径更贴近原版手感。
- 文本行不能含 `%`（原版 `obj_writer` 的分页符）；ID 只允许字母/数字/下划线/点/短横线。
- 菜单「战斗 → 全部对话…」可列出并跳转。运行时控制台：`dialogue list` / `dialogue start <id>` / `dialogue stop`。
- **真机坑**：原版 `obj_writer` Create 会无条件读 `global.msg[0..99]`，生成器必须把消息数组补到 100 行，否则 `Variable Index [3] out of range` 崩（已在 `ntlb_dialogue_build_msgs` 修好）。

## 简单剧情编辑（绑定地图实体）

选中实例 → 属性面板「剧情」段 →「为此实体新建剧情…」，或 `Ctrl+Shift+K`。适合「进入区域/交互实体后，让人物移动、转向、定时换动作，并穿插对话」这类脚本化演出。

- 触发方式：`interact`（靠近后按 Z/Enter，用原版四向交互矩形判定）或 `area`（进入触发半径自动播放）；触发房间/坐标取自绑定实体，另有需要 flag、半径、只触发一次、**跨重启只触发一次**。
- 步骤（`steps`，按顺序播放，共 10 种）：`wait` 等待帧数 / `move` 移动到 X,Y（按帧数线性插值）/ `face` 转向（down/right/up/left）/ `action` 换成指定精灵帧与动画速度 / `dialogue` 显示多行对话（Z 翻页）/ `flag` 设置或清除 flag / `message` 显示一行提示文字（日志 + 屏幕文本）/ **`sound` 播放音效**（音效资源名 + 增益 0..4，`frames` 当前未用）/ **`shake` 屏幕震动**（`frames` = 持续帧数，`X` = 强度，默认 20 帧 / 4 像素）/ **`waitinput` 等待玩家按键**（`frames` 之后接受 Z/Enter）。
- 角色（`actor`）：`player` = 玩家（obj_mainchara，用原版朝向精灵），`trigger` = 本段剧情绑定的那个实体（按坐标找最近实例，转向用 image_xscale 镜像）。
- 结束效果：setFlags / clearFlags / healPercent / gotoRoom / message（与战斗、对话共用 `ntlb_apply_effects`）。
- 运行时：`ntlb_cutscene_tick` 每帧推进（移动用 lerp，对话等 `obj_dialoguer` 消失再继续），播放期间 `global.interact = 1` 屏蔽其他交互；控制台 `cutscene list` / `cutscene start <id>` / `cutscene stop`。
- 真机验证：章节 2 用控制台手动开演（5 步：转向 → 移动 → 换精灵 → 2 行对话 → 设 flag 904）与「进入 room_dw_city_hacker 自动触发」两条路径都跑通，日志见 `[ntlb] [触发] 剧情 …` / `[ntlb] [剧情] 开始/结束`，截图 `tmp/cut-live-*.png`、`tmp/cut-area-trigger.png`。

## Mod 设置项（游戏内「Mod 设置」面板）

菜单 **战斗 → Mod 设置项…** 编辑生成 mod 的设置项，部署后出现在游戏内「设置 → Mod 设置」面板，并可在控制台用 `setting list` / `setting set <id> <value>` 操作；值持久化在 `mods-shared.json`（`ntl_mod_data_set/save`），重启后读回。

- 每项字段：id（`^[A-Za-z0-9_.-]{1,48}`）、显示名、类型（`bool` / `choice` / `int`）、默认值、功能绑定（`none` / `battlesEnabled` / `overworldEnabled` / `dialoguesEnabled` / `cutscenesEnabled` / `barrageDamagePercent`）、choice 选项（`label=value`，逗号分隔）、min/max/step、面板里显示的说明。
- **功能绑定实时生效**：`battlesEnabled=false` 时 `battle start` 直接拒绝（日志 `已被 mod 设置项禁用（battlesEnabled），不启动`）；`overworldEnabled` / `dialoguesEnabled` / `cutscenesEnabled` 同理；`barrageDamagePercent` 按百分比缩放弹幕伤害。同一个 feature 被多项绑定时取第一个并给警告。
- 生成器会为每个设置项生成 `gml/ntlb_<modid>_setting_<i>.gml` 面板动作，并内嵌进 `ntlb_data.gml`；设置项也写进 `files/Neutraled/ntl_battle/battles.json` 方便人编辑。

## mods 清单（mods.json，类似 Everest.yaml）

菜单 **战斗 → mods 清单（mods.json）…** 打开整包清单编辑器：

- 表格列：启用、id、名称、作者、版本、章节、路径、依赖（逗号）、加载在其后、加载在其前、说明；支持添加/删除/上移/下移。
- 「从 mods 目录导入…」扫描 `Neutraled/mods/**/mod.json` 把 id/name/author/version/enabled/chapter 拉进清单（已在清单里的 id 跳过），路径用相对 mods 目录的 `/` 分隔。
- 打开… / 保存 / 另存为…；保存前自动备份 `<清单>.bak`；底部实时校验（重复 id / 缺 id = 红色错误，依赖指向清单外 = 橙色警告）。
- 默认路径 = `游戏根/Neutraled/mods.json`。注意：部署时 builder 会重写运行期的产物清单，这份是**源侧整包清单**（元信息/启停/依赖/加载顺序），与部署产物里的 `mods.json` 不是同一层。

## CLI 命令

| 命令 | 说明 |
|---|---|
| `gui [data.win]` | 打开编辑器 |
| `rooms <data.win>` | 列出房间 |
| `inspect <data.win> <room>` | 逐层 dump 房间结构 |
| `room-stat <data.win> <room>` | 房间汇总：`size=WxH layers=.. instances=.. tiles=..`、`instColor[#RRGGBB:n]`、逐层 `depth/offset/visible`、背景层 `sprite/visible/tiledH/tiledV/stretch/color`、瓦片 `tiles=N src[名字:数] last[w= h= sx= sy= color= scale=]` |
| `room-inst <data.win> <room>` | 实例清单：`id= x= y= spr= scale= color=`（GUI e2e 用它断言对齐/颜色写盘） |
| `render <data.win> <room> [scale] [out.png]` | 渲染房间为 PNG |
| `document-test <src> <dst>` | 编辑模型全量测试（房间/图层/实例/瓦片画笔/剪贴板/图层属性/子区域/调色板/圆椭圆/对齐分布/房间尺寸/保存重载）；基线层数/实例数按当章实测（不再写死），ch1..ch5 每章 **194 PASS / 0 FAIL** |
| `structure-test <src> <dst>` | 房间结构复制/删除/排序往返，当前 PASS |
| `stress-test <src> <dst>` | 大房间冒烟：room_town_north 1800×240 / 1506 图块渲染 + 绘制 270 格 + 擦除 135 + 保存 + 重载校验 |
| `roundtrip <src> <dst>` | data.win 读写往返 |
| `edit-test` / `tile-test` | 早期编辑/瓦片测试 |
| `types` / `io` / `delegates` | 反射 UndertaleModLib API（排障用） |
| `battle-test [data.win] [outDir]` | 战斗/对话/剧情/设置项自检（**76 项**）：示例工程（encounter 3 回合 + 随机弹幕 / overworld 3 种 pattern / 1 段纯对话 / 1 段 8 步剧情含 sound·shake·waitinput / 4 个设置项）、sidecar 往返（含 triggerMode/persistOnce/mode/sound gain）、Validate（新步骤类型与音效参数校验）、choice 缺选项报错、生成 mod 的 gml/objects/data 数量（对象事件 13 个含文本对象）、`ntlb_*` 新函数（once / in_interact_zone / show_text / text_create·step·draw）、设置面板动作文件数、内嵌 JSON 对话/设置/剧情、mod.json 入口、JSON 可反解析 |
| `battle-demo [data.win] [modsDir] [leaf]` | 生成演示用战斗+对话+剧情+设置 mod（默认 leaf=chapter1，真机验证用 chapter2），可直接 deploy |
| `manifest-test [modsDir] [outJson]` | mods 清单逻辑自检（8 项）：扫描真实 mods、id 唯一、校验、写盘/重读、.bak、重复与缺 id 检出 |
| `resource-browser-test <data.win>` | 资源浏览器只读自检：12 类资源逐类计数 + 抽样名称/概要/详情非空 + 源文件 SHA256 不变（ch1 537 项 PASS、ch2 645、ch5 714） |
| `render-stats <data.win> <room> [scale] [out.json]` | 渲染覆盖统计（逐层绘制数/unsupported/黑像素占比）；ch5 room_town_north 黑像素 1.06%、ch4 room_dw_castle_town 48.96%（残黑是 TILES 层真实美术，覆盖 70.00%、unsupported 0） |

## 测试与证据

- `document-test`：ch1..ch5 每章 **194 PASS / 0 FAIL**（A2 修复后由 lead 独立复跑；日志 `tmp/lead-verify-doctest`）。
- `resource-browser-test`：ch1 **537** / ch2 **645** / ch5 **714** 项全 PASS，测试前后源 data.win SHA256 与大小不变。
- `battle-test`：**76 PASS / 0 FAIL**（日志 `tmp/battle-test.log`；最近一次跑在 `tmp/selftest-verify`）。
- `manifest-test`：8 项 PASS（真实 mods 目录扫到 38 个 mod、校验 0 错误、重读 38/38、重复 id 检出 2 条；日志 `tmp/manifest-test.log`）。
- `structure-test`：PASS，输出 `tmp/gui-test/ch1-struct-test.win` = 13,075,564 B，147 房间。
- `stress-test`：PASS（日志 `tmp/stress-test.log`）：渲染 1665 ms（首次，含贴图缓存预热）→ 编辑后 118 ms，绘制 270 格 3 ms，擦除 2 ms，保存 597 ms，重载 300 ms，图块数/实例数/图层数/房间数全部一致。
- GUI 端到端（真写盘，磁盘 `room-stat` / `room-inst` / sidecar 断言）脚本在 `tmp/`：
  - `gui-tile-e2e.ps1` 矩形；`gui-tile-tools-e2e.ps1` T/L/K；`gui-tile-sel-e2e.ps1` 瓦片选择/复制；`gui-asset-e2e.ps1` 图块来源/背景精灵；`gui-region-e2e.ps1` 子区域；`gui-palette-e2e.ps1` 调色板；`gui-circle-e2e.ps1` 圆/椭圆；`gui-resize-e2e.ps1` 房间尺寸；`gui-edit-e2e.ps1` 图块颜色/缩放 + 实例颜色 + 多选左对齐/撤销。
  - `gui-battle-e2e.ps1`：选房间 → 选对象 → 画布放置实例 → 属性面板「为此实体新建战斗…」→ 类型改 overworld → 保存 → 主窗口保存 → 断言 sidecar `room/instanceId/kind/trigger`（**PASS**）。
  - `gui-manifest-e2e.ps1`：沙箱 `DELTARUNE_DIR` → 打开 mods 清单 → 断言表格 3 行（预置 1 + 扫描 2）→ 保存 → 断言 JSON 含 3 个 id + `.bak`（**PASS**）。
  - `gui-generate-e2e.ps1`（新流程）：沙箱下菜单「生成战斗 mod…」→ 弹「选择部署叶子」对话框（ComboBox 选 chapter2）→ 点「生成」→ 读结果 MessageBox → 断言产物 `mods/battle.demo/doge-e/chapter2`：80 文件 / gml 64 / mod.json main=main / battles.json 1 场（**PASS**）。
  - `gui-battle2-e2e.ps1`：改触发方式 interact + 勾「跨重启只触发一次」→ 断言 sidecar `triggerMode=interact`、`persistOnce=True`（**PASS**）。
  - `gui-dialogue2-e2e.ps1`：写三行文本 + 触发方式 area + Speaker + 跨重启 once → 断言 sidecar `mode=area`、`speaker=Susie`、`persistOnce=True`（**PASS**）。
  - `gui-cutscene2-e2e.ps1`：四步 dialogue/sound/shake/waitinput + 半径/结束 flag/跨重启 once → 断言 sidecar `steps=4`、`mode=area`、`radius=80`、`endFlags=905`、`persistOnce=True`（**PASS**）。
  - `gui-manifest-leaf-e2e.ps1`：清单表格新增「部署叶子」列 → 改 pre.mod=chapter2、导入的 demoA/B=chapter3（从路径推断）→ 保存 → 断言 JSON `leaf` 字段 + `.bak`（**PASS**）。
  - `gui-dialogue-e2e.ps1`：放置实例 → `Ctrl+Shift+B` → UIA 按 Edit 索引写 id/名称/三行文本 → 保存 → 断言 sidecar `dialogues[0]` 的 room/instanceId/lines/trigger（**PASS**）。
  - `gui-settings-e2e.ps1`：菜单「Mod 设置项…」→ DataGridView 改 id/显示名 → 保存 → 主窗口保存 → 断言 sidecar `settings[0].id`（**PASS**）。
  - `gui-cutscene-e2e.ps1`：放置实例 → `Ctrl+Shift+K` → UIA 写 id/名称/半径/结束 flag + 表格里填 dialogue 行文本 → 触发方式改 area → 保存 → 断言 sidecar `cutscenes[0]` 的 room/instanceId/mode/steps/trigger/endFlags（**PASS**）。
- 战斗引擎真机验证（章节 2，真实存档）：按键驱动进入战斗 → 日志 `[ntlb] [战斗] 开始遭遇战: selftest_encounter`、`[ntlb] [敌人] 槽 0 = Bloxer（HP 80）`、`槽 1 = Hathy（HP 60）` → 逐回合 `[ntlb] [弹幕] 第 1/2/3 回合` → `[ntlb] [战斗] 达到回合上限 3，判定胜利` → `[ntlb] [剧情] 自检胜利` → `[ntlb] [战斗] 结束（胜利）`，全程无 Code Error；截图见 `tmp/battle-live-*.png`、`tmp/battle-after-victory.png`。overworld 弹幕同轮验证（420 帧结束、英雄掉血）。
- 设置项/对话真机验证（章节 2，真实存档）：`[ntlb] [设置] 已注册面板项: battle.enabled = 开` 等 4 项；`setting set battle.enabled 0` 后 `battle start` 被拒（`已被 mod 设置项禁用（battlesEnabled）`），改回 1 后正常开战；重启后 `barrage.damage = 50` 被持久化读回；纯对话 `[ntlb] [对话] 开始: selftest_dialogue（3 行）` → 翻页 → `[对话] 结束` + `[剧情] 自检对话结束`。截图 `tmp/battle-settings-live.png`、`tmp/battle-dialogue-live2.png`、`tmp/battle-dialogue-end2.png`。
- 战斗触发/初始紧张度/胜利链路（章节 2，真实存档）：进入房间触发 `[ntlb] [触发] 战斗 selftest_encounter` → `[战斗] 开始遭遇战` → `[战斗] 初始紧张度 = 50`（首帧 controller 存在时写入）→ 逐回合 `[弹幕] 第 1/2 回合` → `[战斗] 达到回合上限 2，判定胜利` → `[剧情] 自检胜利` + `[文本] 自检胜利` → `[战斗] 结束（胜利）`；截图 `tmp/live-a3.png` 起、`tmp/live-c3.png`（FIGHT 菜单）。
- 全灭判负（章节 2）：`[战斗] 队伍全灭，判定失败` → `[剧情] 自检失败` → `[文本] 自检失败` → `[战斗] 结束（失败）` → 原版 GAME OVER（截图 `tmp/live-c4.png` 黑屏白字「自检失败」）。
- 跨重启 once（章节 2）：对话 `persistOnce` 后 `mods-shared.json` 出现 `ntlb.dlg:selftest_dialogue=1`；下一进程再进房间 area 对话不再触发（`count-dialogue-trigger=0`）。
- 剧情 steps 真机（章节 2）：`[剧情] 音效 snd_text（增益 0.50）` → `[剧情] 震动 4 像素（20 帧）` → `[剧情] 等待按键（Z/Enter）` → `[剧情] 结束`，8 步完整播出（截图 `tmp/live-d1.png`、`tmp/live-d2.png`）。
- 剧情真机验证（章节 2）：控制台 `cutscene start selftest_cutscene` → `[ntlb] [剧情] 开始: selftest_cutscene（5 步）` → `[剧情] 对话 2 行` → Z 翻页 → `[剧情] 结束` + `[剧情] 自检剧情结束`（截图 `tmp/cut-live-start.png`、`tmp/cut-live-round2.png`）；把剧情触发改绑到 `room_dw_city_hacker`、mode=area、半径 9999 后重部署，进存档即自动 `[ntlb] [触发] 剧情 selftest_cutscene（房间 room_dw_city_hacker）` → `[ntlb] [剧情] 开始`（截图 `tmp/cut-area-trigger.png`）⇒ 交互触发与区域自动触发两条路径都跑通。
- GUI 自动化注意事项（写 e2e 时踩过）：
  - 抢前台只能用 `SwitchToThisWindow(hwnd, true)`（`AttachThreadInput+SetForegroundWindow` 在本机常被 `GameInputServiceWindow` 挡住）。
  - 每个脚本开头先杀残留 `ntl-mapedit` 进程。
  - WinForms ListBox 在窗口非前台时不响应注入点击 → 先抢前台或 `SetFocus()` + `SendKeys`。
  - 不要用 `SendKeys '^a'` 清输入框（主窗口把它当「全选实例」，重建属性面板会销毁输入框）→ 用 `ValuePattern.SetValue` + `{TAB}` 触发 Leave 提交。
  - 会弹模态对话框的按钮用 UIA `InvokePattern` 可能阻塞 → 改用鼠标点按钮 bounding rect 中心。
  - DataGridView 单元格没有 UIA ValuePattern → 鼠标点单元格（EditOnEnter 单击即编辑）后 SendKeys；中文输入会触发 IME 吞字符 → 先 `CTRL+SPACE` 切英文，e2e 只用 ASCII。
  - 对话框「保存」按钮首次点击可能只提交正在编辑的单元格 → 点 2–3 次并复检窗口是否关闭。
  - 「生成战斗 mod…」现在会先弹**第二个窗体**（选择部署叶子）→ 按标题 `选择部署叶子` 找到它，ComboBox 用 UIA `SetFocus()` + `{DOWN}` 改值，再点「生成」。
  - DataGridView 的 ComboBox 单元格（部署叶子列）不暴露 DataItem → 用相邻两列（章节/路径）DataItem 矩形中点定位列 x，行中心取 `Row N` 的 Custom rect。
  - `CutsceneEditDialog` 网格列宽固定（92/74/56/58/58/68/130/52/72/130/60/300/90/90）→ 取单元格必须用 UIA Name（如 `帧数 Row 3`）拿矩形，自己按列宽推算会偏数百像素。

## 已知限制（分清「硬限制」与「当前取舍」）

### A. 引擎 / 数据模型硬限制（做不到，不是没做）

- **不能编辑 GML 代码**：对象事件、脚本、CreationCode 都是 `UndertaleCode` 资源；改它们需要 GML→bytecode 编译器。所以实例只能改位置/缩放/旋转/图像帧/颜色等内置字段，不能改/加自定义实例变量，也没有 entity 属性编辑页。
- **autotile**：DR 的 data.win 不含 GameMaker IDE 的 autotile 元数据，无法还原「自动拼接」语义。
- **Celeste 式 map 级 bin / 房间摆放**：DR 房间各自独立加载，Loenn 的 `tools/room.lua`（地图画布 + filler）语义不适用。
- **资源只读**：精灵/对象/声音/背景/字体/路径只能引用 data.win 里已有的，不能新建；能新建的只有房间、图层、实例、瓦片。
- **资源浏览只读**：`F4` 资源浏览器能看全部 12 类资源（含字体/音效/脚本代码/着色器/序列/时间线/纹理页/路径），但不支持编辑这些资源本身（改字体贴图、换音效文件、改 GML 代码都不行，原因见上一条「不能编辑 GML 代码」）。
- **图层类型有限**：渲染与编辑覆盖实例层 / 图块层（Assets）/ 背景层 / 原生 GMS2 Tiles 层（含翻转旋转、单格拾取/删除/批量画笔）；路径层只画占位框（UTMT 不保存 PathData 点集，无法还原真实走向），效果层（Effect）不渲染也不可编辑。
- 瓦片编辑基于 DR 现有兼容层瓦片列表（`UndertaleRoom.Tile`），不生成 GameMaker 2.3 的新版 tilemap 图层数据。
- **战斗弹幕受原版模板限制**：type 是目标章节 `obj_dbulletcontroller` 的模板编号，形状/运动由原版分支写死，不能自定义轨迹；章节 2 的 26/27 引用 Rouxls 实例、在别章会崩，跨章节安全的是 0（环形）/1（上下边缘）。原版敌人对象必须在该章产物里存在，跨章请用生成器自带的 `ntlb_*` 敌人。
- **原版战斗流程不可替换**：FIGHT/ACT/ITEM/SPARE 菜单与结算动画仍是原版；mod 只替换敌人槽位、属性、每回合弹幕、回合上限与结束 effects。

### B. 当前实现的取舍（能补，但要改代码 / 接线）

- **生成 mod 绑一个章节叶子（leaf）**：`mods/<id>/<author>/<leaf>/`，默认 `chapter1`；「战斗 → 生成战斗 mod…」会先弹窗选 `root/chapter1..chapter5`（写回 sidecar 的 `mod.leaf`；CLI 用 `battle-demo <data.win> <modsDir> <leaf>`）。弹幕 type 必须按该 leaf 章节填。
- **生成 mod 是快照**：编辑只写 sidecar `<data.win>.ntlbattle.json`；必须再点「战斗 → 生成战斗 mod…」，并让 Neutraled 重新部署才进游戏。
- **战斗敌人最多 3 个**（原版 3 个怪物槽）；**ACT 只能设名字（最多 6 个）**，行为仍是原版默认，不能给单个 ACT 配自定义效果；**初始紧张度**在战斗首帧写入 `global.tension`（按 `scr_gamestart` 的 250 上限 clamp）。
- **effects 的 `message`** 同时写 `dr-api.log` 与屏幕文本（约 150 帧，`ntlb_show_text`）；`healPercent` 只作用于 3 名队员；`gotoRoom` 是 `room_goto`（无过渡特效）；`startBattle` 在当前房间直接再开一场；**队伍全灭会判失败并执行 defeat effects**（随后仍走原版 game over）。
- **overworld 弹幕是自建子弹**：只有 rain/ring/aim/wall 4 种简易模式，直线移动 + `point_distance<14` 判命中，没有墙体碰撞/寻路，也不接管原版 overworld 敌人。
- **对话**：`Speaker`（说话人）以「名字：」前缀拼进正文第一行（原版对白框没有名字栏，不会画在单独的名字区）；文本里 `%` 会被当翻页符删掉；只有文本行 + 结束 effects，没有立绘/表情/选项。
- **剧情共 10 种步骤**（wait/move/face/action/dialogue/flag/message/sound/shake/waitinput）：仍没有相机/音乐控制（`sound` 只播单个音效）、表情、选项、条件分支、并行分支；`move` 是两点线性插值、会穿墙；`action` 改了精灵不会自动恢复；`message` 日志 + 屏幕文本；`shake` 依赖原版 `obj_shake`；剧情期间只设 `global.interact=1` 拦交互，**不锁玩家输入**。
- **触发判定是「房间 + flag + 半径 + 按键」+ 原版交互矩形近似**：`interact` 模式现在会按玩家 `global.facing` 用原版 `obj_mainchara` 的四向交互矩形判定（`ntlb_in_interact_zone`），但**仍不校验玩家真的和绑定对象交互**；附近有原版可交互物时可能同时触发。`area` 模式在 `global.interact != 0`（菜单/战斗/其它对话/剧情）时整个跳过。
- **战斗触发**：房间名匹配 + flag + 距离（radius=0 表示进房间即触发）；`auto` 条件满足就自动开战，`interact` 还需按 Z/Enter 且在交互矩形内。
- **「只触发一次」有两层**：`once` 默认只在进程内有效（`global.ntlb_once` ds_map，重启复位）；勾选**跨重启只触发一次**（`persistOnce`）会写进 Neutraled 的 `mods-shared.json`，重启游戏后仍然记得（依赖共享数据机制，清理共享数据会复位）。战斗/对话/剧情的一次性键共用 `ntlb.<id>` 前缀。
- **mods 清单编辑器写的是源侧清单**（类似 Everest.yaml 的元信息/校验文档）：Neutraled builder 部署时**不读它**，排序/启停读各 mod 自己的 `mod.json`（`LoadAfter`/`LoadBefore`）；产物里的 `Neutraled/mods.json` 是 builder 生成的运行时清单，不要手改。
- **Mod 设置项只有 bool/choice/int 3 类**、6 种 feature 绑定（none/battlesEnabled/overworldEnabled/dialoguesEnabled/cutscenesEnabled/barrageDamagePercent）；同一 feature 多项绑定只取第一个（编辑器给警告）；值持久化 key 含 mod 前缀，改 mod id 会丢已保存值。
- **地图编辑是直接改 data.win**：没有工程文件，撤销栈只在内存（关掉就没了）；保存整份重写 data.win（本次打开第一次保存会留 `.bak`）；一次只开一个 data.win / 一个房间；撤销快照是整房间，超大房间编辑成本高。
- **对齐/分布的包围盒**按「当前图像帧 × 缩放」估算；**有旋转时按绕中心的旋转外接矩形近似**（原版旋转绕 sprite origin，origin 不一定是中心，所以是近似值）。
- 属性面板只覆盖编辑器认识的字段（实例：位置/缩放/旋转/帧/颜色；图块：位置/尺寸/源/缩放/颜色；背景：可见/平铺/拉伸/颜色/精灵）；对象的其它属性（父对象、mask、物理、事件等）只读。

### C. 运行 / 部署环境限制

- **战斗/对话/剧情运行时只在章节进程有效**（root 章节选择器里没有 `obj_mainchara`/`global.flag` 等）；手动开战/开演前必须已有玩家实例（读档进房间之后），且 `global.interact == 0`（菜单关掉）。
- 触发检测跑在 `ntl_hook("on_frame")` 上，依赖 Neutraled 的 API 与 `global.flag`；单独跑原版 data.win 时这些机制都不存在。
- **Mod 设置项面板依赖 Neutraled 的设置菜单补丁**（部署自检 (g) 项验证 7 个标记）；单独跑原版 data.win 时设置面板不存在（控制台 `setting` 仍可用）。
- 设置项持久化用 `ntl_mod_data_*`（mods-shared.json），依赖 Neutraled 的共享数据机制。
- 编辑器是 Windows WinForms / .NET 9 + UTMT DLL；必须打开真实 data.win，不能新建空白工程。

## 代码结构（src/ntl-mapedit）

| 文件 | 职责 |
|---|---|
| `EditorForm.cs` | 主窗口：工具栏/菜单/左右面板/属性面板/快捷键/文件对话框/对齐分布/战斗·对话·剧情·设置接线 |
| `CanvasPanel.cs` | 画布：缩放平移、工具输入状态机（DragMode）、实例/瓦片绘制、框选、拖拽、预览 |
| `EditorDocument.cs` | 文档模型：选区（实例/图块）、撤销栈、剪贴板、对齐分布、房间/图层/瓦片/实例编辑入口、保存 |
| `RoomEdit.cs` | 与 UndertaleModLib 打交道的纯函数：房间/图层结构操作、瓦片画笔（矩形/直线/圆/椭圆/油漆桶/调色板格）、克隆 |
| `RoomRender.cs` | 离屏渲染（图层排序、瓦片、实例、背景平铺/拉伸/色调） |
| `GameData.cs` | 打开/保存 data.win、帧位图缓存、`SetupRooms()` 补父子链 |
| `TileSourceDialog.cs` | 帧内矩形选区对话框（瓦片子区域） |
| `TilePaletteDialog.cs` | 非模态瓦片调色板（按单元格切图集） |
| `AssetPickerDialog.cs` | 精灵/背景资源选择器 |
| `BattleData.cs` | 侧车配置模型（battles + dialogues + cutscenes + settings 的 schema/Normalize/Validate）+ sidecar 路径/按实例查找 |
| `BattleEditDialog.cs` | 战斗编辑对话框（基本/敌人/回合弹幕/overworld/触发结局 5 页） |
| `DialogueEditDialog.cs` | 纯对话编辑对话框（多行文本/触发半径 flag/结束 effects） |
| `CutsceneEditDialog.cs` | 简单剧情编辑对话框（触发方式/步骤表格/结束 effects） |
| `SettingsEditDialog.cs` | Mod 设置项编辑对话框（bool/choice/int + feature 绑定） |
| `BattleGenerator.cs` | 生成可部署 Neutraled mod（mod.json/gml/objects/files） |
| `BattleRuntimeTemplates.cs` | 生成 mod 的 GML 运行时函数模板（战斗启动/敌人契约/弹幕/overworld/对话/剧情/设置/控制台命令） |
| `BattleSelfTest.cs` | `battle-test` 自检（示例工程 + 生成断言） |
| `ModsManifestDialog.cs` | mods.json 整包清单编辑器 + 静态扫描/校验/保存逻辑 |
| `ResourceCatalog.cs` | 只读资源目录：12 类资源的统一枚举/字段描述（GUI 与 CLI 自检共用同一实现） |
| `ResourceBrowserDialog.cs` | 只读资源浏览器窗口（虚拟列表 + 详情面板，`F4`） |
| `PromptDialog.cs`/`RoomGeometry.cs`/`Theme.cs`/`Program.cs` | 输入对话框/几何/深色主题/CLI 入口 |

## 重要数据模型坑（改代码前先看）

1. **直接 `UndertaleIO.Read` 出来的对象没有编辑器侧父子链**：`LayerBackgroundData.Sprite` 的 setter 会调用 `ParentLayer.ParentRoom.UpdateBGColorLayer()`（本机 0.8.3.0 DLL 非 null-safe），不补链改精灵必 NRE。`GameData` 构造后统一 `room.SetupRoom(false, false)` 补链；新建/复制图层时手动 `dst.ParentRoom = ownerRoom`。
2. Tile/GameObject 的 `ObjectDefinition`/`SpriteDefinition`/`BackgroundDefinition` **setter 按 `spriteMode` 强转**：克隆/改写必须**先设 `spriteMode`** 再分别写两个字段；`Tpag` 是计算属性不可赋值。
3. 瓦片坐标是**世界像素**，画笔以「格」为操作单位；`TileWorldSize` 才是格尺寸（不是 8/16 假设）。
4. 撤销快照按**图层对象引用**记录（深度编辑会重排图层列表，按 index 会写错层）；撤销会**重建实例/瓦片对象**，断言要按 `InstanceID` 重新取引用。
5. 图块属性面板（`SelectedTile`）只在「用瓦片工具画/替换」或「Alt+点击拾取」后出现；多选瓦片走瓦片选择工具自己的剪贴板路径。