# DRMapEditor / ntl-mapedit

DELTARUNE（GameMaker 2023.6 / data.win）的房间编辑器：GUI + 命令行。用于查看与修改房间、图层、实例、瓦片、背景层属性，并把改动写回 data.win；同时支持**直接打开原版 data.win 编辑对话/战斗/剧情**（保存即生成 Neutraled mod，不改原版文件）、左侧房间列表按剧情流程排序，以及编辑整包 mods.json 清单。参考实现是 Celeste 的 Loenn，按 DELTARUNE 的数据模型重做（DR 没有 Celeste 的 map 级 bin，也没有 autotile 元数据）。

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

- 左侧：房间列表（默认**按剧情线性流程排序**：主线编号「01/02/…」、支线紧跟其前置主线并缩进并标「支线」、测试/旧版房间收进底部「特殊 / 测试 / 旧版」分组；视图 → 「房间按剧情流程排序」可切回 data.win 原始顺序；筛选框中输入关键词时平铺为原始顺序）、图层列表（点眼睛图标切换可见性）。
- 中间：画布（滚轮缩放、`F` 适应窗口、拖拽平移、网格/辅助线显示开关、`V/H` 等工具）。
- 右侧：属性面板（可滚动），按当前选择切换：
  - **房间**：名称、宽度/高度（16–10000，提交后左侧列表/标题/状态栏同步）、背景色、绘制背景色；「原版房间（只读）」段显示有无房间创建代码 + 「查看房间创建代码…」。
  - **目标图层**：名称/类型只读、深度（改完自动按深度重排图层）、偏移 X/Y、图层可见。
  - **背景层**：精灵只读 + 「选择背景精灵…」、背景可见/水平平铺/垂直平铺/拉伸铺满、背景色。
  - **图块**（用瓦片工具画/替换后，或 Alt+点击拾取）：X/Y、宽/高、源 X/Y、缩放 X/Y、颜色、Tile ID。
  - **单选实例**：顶部依次「原版交互（只读）」段（交互事件 / 对白行数 / 文本来源 + 「查看原版对话…」「原版事件…」「创建代码…」三按钮）、「Mod 战斗（可编辑）」段（未绑定 →「为此实体新建战斗…」；已绑定 →「编辑战斗…」「解除绑定」）、「Mod 对话（可编辑）」段（「为此实体新建纯对话…」/「编辑对话…」「解除绑定」）、「Mod 剧情（可编辑）」段（「为此实体新建剧情…」/「编辑剧情…」「解除绑定」），然后是 X/Y、缩放 X/Y、旋转、图像帧、颜色（可编辑）。
  - **多选实例**：批量删除、「对齐 / 分布」8 个按钮（左/中X/右/顶/中Y/底/分布X/分布Y）。
  - **底部**：瓦片笔刷列表 + 「选择图块来源…（精灵帧内选区域）」+「瓦片调色板…（点格子选笔刷）」+ 对象列表（选择后进入放置工具）。
- **资源浏览器（只读，`F4`）**：视图 → 资源浏览器…，左侧 12 类资源（房间 / 对象 / 精灵 / 背景瓦片集 / 字体 / 音效 / 脚本代码 / 着色器 / 序列 / 时间线 / 纹理页 / 路径），右侧列表 + 底部完整字段。之前这些资源（尤其字体、音效、数万段代码、着色器、路径）完全没有入口；现在可以查名字、数量、尺寸、EmSize/字形数、文件/音量、代码字节/指令数、着色器各语言源码长度等，方便复制 id 或判断资源是否存在。只读：不写 data.win。`resource-browser-test` 用同一套 `ResourceCatalog` 做无 GUI 自检。

### 原版交互 / 对话显示（只读）

编辑器会把原版 GML 也读出来，帮你判断「这个实体交互后会发生什么」，但**不改原版代码**（改 GML 需要编译器，见限制 A）。

- 选中实例 → 右侧「原版交互（只读）」段：交互事件（`Other_10` 用户事件，继承来的会标「继承自 <父对象>」+ 父对象链）、对白行数（共 N 行 = **可阅读 X 行 + 运行期文本/控制码 Y 行**，当前房间/当前坐标条件下可达 M 行，其中继承 K 行）、文本统计（msc 旧式文本 N 行 · 含参数替换 M 行 · 未知条件 K 行）、文本来源（完整 lang 路径 + 条数；无 lang 时给出回退搜索过的目录）、最多 3 条对白预览（带 `[msc N]`/`[参数]` 前缀；**运行期文本/控制码行不进预览**）。超过 4000 行会明确标「已截断」。
  - ⚠「可达」是**坐标相关**的：同一对象不同位置可达行数完全不同（例：ch1 `obj_readable_room1` 在 room_krisroom (196,99) 只有 11 行可达，因为 x>220 的床/msc 402 分支和其它房间分支都不可达）。不要把它当固定值。
  - 对象列表右键查看器**没有实例坐标**时，`x`/`y` 条件显示为 `?`（未知），摘要写「坐标未知：确定可达 N 条、待定 ? M 条」。
- 三按钮：`查看原版对话…` / `原版事件…`（事件查看器：左侧事件列表 + 对白行列表，右侧反编译代码；可「扫描全部事件（较慢）」「复制事件摘要 / 对白摘要 / 代码」）/ `创建代码…`（实例 CreationCode → PreCreateCode → 房间创建代码，有哪个看哪个）。
- 房间属性（没选实例时）有「原版房间（只读）」段：「原版属性」行（`speed= · persistent= · flags= · views= · 旧版背景= · 创建顺序= · 路径层= · 效果层=`）+ 有没有房间创建代码 + 「查看房间创建代码…」。
- 对象列表点选对象时，状态栏追加 `· 原版：交互 Other_10，对白 N 行（当前房间/坐标可达 M）· 对象: 深度 D · visible · solid · persistent · 父对象`。
- 对象列表**右键 → 查看原版交互 / 事件…**：对象级事件查看器（不需要先放实例），坐标未知时条件按 Unknown 显示（宁可多显示）。
- 实现：`VanillaCode.cs` 懒反编译（反射调 `CodeImportGroup.DecompileExistingCode`）+ 行级条件判定（三态 True/False/Unknown）；`room == N` 用 `data.Rooms` 索引求值，也支持把 `room_xxx` 标识符换成索引；`global.*` 按 Unknown；`x`/`y` 在**有实例坐标**时按数值求值，无坐标（NaN）时 Unknown。
- 条件帧按反编译器的真实排版解析：条件行下一行的 `{ … }` 才是块体（**反编译输出从不写单语句 if**，实测 7558 个 .gml 中花括号换行 58485 次、无花括号 0 次）；`else`/`else if` 与外层条件合成三态（`else` 可取反成 False/True）；`with` 体内条件按 Unknown。
- 两代消息链都解析：直接消息 `global.msg[n] = scr_84_get_lang_string(key)` / `stringsetloc` / `msgsetloc` / `msgnextloc` / `msgsetsubloc` / `msgnextsubloc`；旧式 `global.msc = N; scr_text(N)` 会展开 `scr_text` 里对应 `case` 的文本，并把 **`global.msc = N` 赋值点的条件**和 `scr_text()` **调用点的条件**合并（例 `room == 2 && x > 220 && global.tempflag[10] == 1 && global.plot < 10`），msc 多候选（if 分支里再赋值 msc）会全部展开。
- 文本清理：`\M*`/`\E*`/`\c*`/`~N`/`\C2` 等控制码剥离（未知转义先保留反斜杠再整体剥除，不再出现 `C2`/`C3`/`F0` 这种残字），换行显示 ` / `，参数模板 `%`→`{0}…` 并标「含参数替换」。清洗后正文为空的纯控制码行**保留但单列**为「运行期文本/控制码」，不计入可阅读正文。
- **文本来源覆盖两代写法**：① 直接 `global.msg[n] = scr_84_get_lang_string("key")` / 字符串字面量；② 章节 2 的 `msgsetloc / msgset / msgnextloc / msgnext / msgsetsubloc / msgnextsubloc` 与 `stringsetloc`；③ 旧式 `global.msc = N; scr_text()` —— 直接从 `scr_text` 脚本的 `case N:` 里展开，条件与调用点条件叠加（`msc 行` 计数可核对）；④ `scr_84_get_subst_string` 等带参数文本标 `[参数]`（`%s/%d` 显示成 `{0}`/`{1}` 占位）。
- **lang 回退顺序**：data.win 同目录及最多 4 层祖先的 `lang/lang_{zh,en,ja}.json` → 按章节推断 `游戏根/chapterN_windows/lang/*` → `游戏根/backup/chapterN_windows/lang/*`；都没有时只显示 key 并在「文本来源」里说明。汉化包把 `lang_en.json` 换成中文，显示的就是汉化文本。
- 文本清理：去掉 `\M0`/`\m0`、`\E4`/`\e4`、`\c*`、`^N`、`~` 等控制码与 `%%`；换行显示成 ` ⏎ `；`global.msc = 0` 的重置不再产生假对白行。
- 事件名用 UTMT 的 `EventSubtype`（不是内层列表位置），`Other_10` 标「· 交互」、其它 `Other_*` 标「· 非交互事件」；对象事件按父链继承（深度 ≤ 12、防环），子对象定义的同名事件覆盖父事件。
- 自检：`vanilla-scan-test <data.win> [out.json]` —— ch1 副本断言 obj_readable_room1 有 Other_10、对白行 ≥400（大修后实测 **428 = 可阅读 405 + 运行期 23**，多出的 26 行是 msc 多候选展开，合法）、实例坐标下可达 ≥5、msc 展开行带调用点条件（实测 80 条）、`scr_text` 文本非占位、未知条件保留 `?`、非交互 `Other_*` 标注、章节推断、继承事件（47/353 对象）；ch2 断言 ≥700（实测 **722 = 689 + 33**）、含参数行；另断言 else 三态与 False 分支存在、`可阅读 + 运行期 = 总行数` 守恒、控制码清洗后无 `C2`/`F0` 残字。
- 单对象排查：`vanilla-scan <data.win> <object> [room] [x] [y] [outJson]` —— 打印该对象的 lang 来源、事件/行数/截断/继承、父对象、交互事件、msc/含参数/未知条件计数与前 30 条可达对白（`truth=` / `cond=` 便于核对原版分支）；给 `outJson` 时导出逐行 JSON（`msg/msc/key/text/condition/truth/requiresArgs/runtimeText` + 顶层 `displayableLines/runtimeTextLines`），便于与基线 TSV 做 key 级对账。**不给 x/y = 坐标未知**（带 x/y 的条件显示 `?`，不会误判 False）。

### 画布工具

| 工具 | 按键 | 行为 |
|---|---|---|
| 选择 | `V` | 点选/框选实例；拖动移动；方向键微移（Shift = 8px）；**右键画布 = 实体上下文菜单**（战斗/对话/剧情/文本覆盖/复制粘贴删除） |
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
- 实体脚本：`Ctrl+B` 战斗、`Ctrl+Shift+B` 纯对话、`Ctrl+Shift+K` 简单剧情、`Ctrl+Shift+E` 编辑本房间原版对话文本（前三个都要先选中一个实例；`Ctrl+Shift+E` 按当前房间扫描）。
- 右键：选择工具下右键画布 → 上下文菜单（编辑此实体的战斗/对话/剧情…、编辑本房间原版对话（文本覆盖）…、复制/粘贴/删除）；瓦片类工具的右键仍是擦除/删块。

### 启动方式与默认章节

- **双击 exe（无参数）= 直接开 GUI**，并自动打开「默认启动章节」的 data.win；命令行帮助是 `help` / `-h` / `--help`。
- 文件 → **默认启动章节（双击 exe 时打开）**：自动（最近打开，其次 chapter1）/ Chapter 1..5；存到 `%LOCALAPPDATA%\DRMapEditor\settings.json`（`defaultChapter`）。
- 解析顺序：显式打开（命令行/文件菜单/最近文件）→ 默认章节 → 最近打开 → chapter1。默认章节优先打开**游戏文件**（可保存）；该章游戏文件不存在时退回 `backup/<chapter>_windows/data.win`（只读，状态栏提示，Ctrl+S 被拦截）。
- CLI：`default-chapter [auto|chapter1..chapter5]`（无参打印当前解析目标）、`chapters`（章节表：游戏文件/原版基线是否存在、哪章是默认）、`gui chapterN`（按章节打开，不用记路径）、`settings-test <workDir>`（25 项启动解析自检）。

## 直接编辑原版 data（推荐用法）

**打开原版基线也能编辑内容**：文件 → 打开游戏章节 → 原版基线（只读），或直接 `gui <backup/chapterN_windows/data.win>`。data.win 本身仍然不被改写（原版文件与 Steam 校验安全），但**对话/战斗/剧情/设置项/文本改写**都可以直接编辑并保存：

- 编辑配置写到 **`游戏根/Neutraled/editor/baseline_<leaf>.ntlbattle.json`**（原版基线）或 **`<data.win>.ntlbattle.json`**（游戏文件），不会写进 `backup/`。
- **保存（Ctrl+S）即自动生成 Mod**：保存配置后立即导出到 `游戏根/Neutraled/mods/battle.demo/<author>/<leaf>/`（可在「战斗 → 保存时自动生成 Mod」关掉，或「立即重新生成 Mod…」手动触发）。生成的 mod 默认 `enabled: true`，重新部署/启动后生效。
- **原版对话文本改写（Ctrl+Shift+E）**：列出当前房间里所有原版对话行（对象 / 事件 / 条件 / lang key / 原文），只改「改写文本」列即可。不改 GML，而是按 lang key 用 Neutraled 的 `ntl_set_lang_string` 覆盖（生成 mod 的 `ntlb_apply_text_overrides`，游戏就绪后生效）。清空某行 = 恢复原文。限制：只能改**有 lang key** 的行（运行期拼出来的文本没有 key，改不了）；文本里不能含 `%`（原版 `obj_writer` 分页符）。
- **非简单剧情用代码引用**：剧情步骤新增 `event`（在指定原版对象的最近实例上执行它的某个事件，如 `obj_readable` 的 `Other_10`）与 `script`（直接执行原版脚本，如 `gml_Script_scr_sideb_active`）。剧情编辑器工具条「插入原版代码…」会列出当前房间所有实例对象的原版事件（含继承与事件号），点选即插入一步，不需要自己记脚本名。
- **右键直达**：选择工具下右键画布上的实体，菜单里直接有「编辑此实体的战斗/对话/剧情…」与「编辑本房间原版对话（文本覆盖）…」，不用先去菜单栏找。
- 状态栏会显示「原版基线 · 只读（data.win 不改；对话/战斗/剧情保存到 Neutraled/editor 并生成 mod）」，提示的只读仅指 data.win 本体。

## 战斗编辑（绑定地图实体）

战斗不是单独工具，而是地图实体属性：在地图上选中一个实例 → 右侧属性面板顶部「战斗」→「为此实体新建战斗…」（等价快捷键 `Ctrl+B`）。触发房间/坐标直接取自该实体（保存时写入 sidecar），不用手填。

- 战斗编辑对话框分页：
  - **基本**：id、名称、类型（`encounter` = 进原版战斗界面 / `overworld` = 地图上直接躲弹幕）、启用、开场文字、回合上限（0 = 不限，达到后判胜利）、循环最后一回合、初始紧张度、战斗前延迟帧数。
  - **敌人**（encounter）：最多 3 个敌人；对象（data.win 里的敌人对象或生成的 `ntlb_*` 自定义敌人）、名称、HP/AT/DF/EXP/Gold、宽恕值/上限、状态、评论、ACT（逗号分隔）。
  - **回合 / 弹幕**：回合列表（增删/上下移），每回合有文字、持续帧数、弹幕组（type / 数量 / ratio / 伤害(0 = AT×5) / timermax / joker / side / graze / time / 随机 type 列表 / ratio 浮动 / 延迟）。随机 type 非空时每回合从列表里随机挑一个模板。
  - **Overworld 弹幕**：模式（danmaku / encounter）、持续帧数、接触伤害、敌人半径、敌人精灵，以及弹幕 pattern 列表（kind `rain|ring|aim|wall` / interval / count / speed / spread / damage / life / angle / 随机角度/数量/速度）。
  - **触发 / 结局**：绑定房间/实例（只读）、需要 flag、触发半径（0 = 进房间即触发）、只触发一次；胜利与失败的 setFlags / clearFlags / healPercent / gotoRoom / startBattle / message。
- 「触发 / 结局」页还有：**触发方式**（`auto` = 条件满足自动开战；`interact` = 还要按 Z/Enter，并用原版 `obj_mainchara` 四向交互矩形判定）、**跨重启只触发一次**（`persistOnce`，写 Neutraled 的 mods-shared.json，重启游戏也不重复）；**初始紧张度**运行时在战斗首帧写入 `global.tension`（上限 250）；**开局文字**会作为屏幕文本提示显示；**队伍全灭判失败**并执行 defeat effects（同时交给原版 game over）；结束 effects 的 `message` 现在也会以屏幕文本显示（约 150 帧）。
- 保存主文档时，战斗/对话/剧情/设置/文本改写配置写到 sidecar（游戏文件 = `<data.win>.ntlbattle.json`；原版基线 = `Neutraled/editor/baseline_<leaf>.ntlbattle.json`），不污染 data.win。**保存会自动生成 Mod**（见上一节）。
- 菜单 **战斗 → 立即重新生成 Mod…**：先弹窗选部署叶子（root/chapter1..5），再把全部配置导出成可部署的 Neutraled mod（默认写到 `游戏根/Neutraled/mods/battle.demo/<author>/<leaf>/`），包含 `mod.json`、`gml/`（战斗/弹幕/overworld/对话/剧情/设置/控制台命令的运行时函数 + 内嵌数据 `ntlb_data.gml` + `main.gml`）、`objects/`（敌人/子弹/overworld 控制器事件文件）、`files/Neutraled/ntl_battle/battles.json`、README。
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
- 步骤（`steps`，按顺序播放，共 12 种）：`wait` 等待帧数 / `move` 移动到 X,Y（按帧数线性插值）/ `face` 转向（down/right/up/left）/ `action` 换成指定精灵帧与动画速度 / `dialogue` 显示多行对话（Z 翻页）/ `flag` 设置或清除 flag / `message` 显示一行提示文字（日志 + 屏幕文本）/ **`sound` 播放音效**（音效资源名 + 增益 0..4，`frames` 当前未用）/ **`shake` 屏幕震动**（`frames` = 持续帧数，`X` = 强度，默认 20 帧 / 4 像素）/ **`waitinput` 等待玩家按键**（`frames` 之后接受 Z/Enter）/ **`event` 执行原版对象事件**（`evObject` = 对象名，`script` = 事件脚本名；在离玩家最近的该对象实例上 `script_execute`；工具条「插入原版代码…」可直接选）/ **`script` 执行原版脚本**（不绑对象，直接 `script_execute`）。后两种的 `frames` = 执行后等待帧数（0 = 立即继续），可用来跑原版演出代码而不必在编辑器里重写。
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
| `gui [data.win|chapterN]` | 打开编辑器；给章节名时自动找该章游戏文件（找不到退回原版基线只读） |
| `default-chapter [auto|chapter1..chapter5]` | 设置/查看「双击 exe 默认打开哪章」（settings.json 的 defaultChapter；无参打印当前解析目标与章节表） |
| `chapters` | 章节表：chapter1..5 的游戏文件/原版基线是否存在、默认章节 |
| `vanilla-scan-test <data.win> [out.json]` | 原版交互/对白扫描自检（Other_10、对白行/可达行、msc 文本与调用点条件、else 三态、lang 回退、条件求值、继承） |
| `vanilla-scan <data.win> <object> [room] [x] [y] [outJson]` | 单对象原版对白排查：lang 来源、事件/行数/截断/继承、msc/参数/未知计数 + 前 30 条可达对白；给 outJson 导出逐行 JSON（对账用） |
| `settings-test <workDir>` | 默认章节/启动解析自检（25 项：归一化/坏文件自愈/优先级/GuessChapter/IsBaselinePath） |
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
| `battle-test [data.win] [outDir]` | 战斗/对话/剧情/设置项/文本改写自检（**83 项**）：示例工程（encounter 3 回合 + 随机弹幕 / overworld 3 种 pattern / 1 段纯对话 / 1 段 9 步剧情含 sound·shake·waitinput·event / 4 个设置项 / 1 条文本改写）、sidecar 往返（含 triggerMode/persistOnce/mode/sound gain）、Validate（新步骤类型与音效参数校验）、choice 缺选项报错、生成 mod 的 gml/objects/data 数量（对象事件 13 个含文本对象）、`ntlb_*` 新函数（once / in_interact_zone / show_text / text_create·step·draw）、main.gml 调用文本覆盖、设置面板动作文件数、内嵌 JSON 对话/设置/剧情/文本改写、mod.json 入口、JSON 可反解析 |
| `battle-demo [data.win] [modsDir] [leaf]` | 生成演示用战斗+对话+剧情+设置 mod（默认 leaf=chapter1，真机验证用 chapter2），可直接 deploy |
| `manifest-test [modsDir] [outJson]` | mods 清单逻辑自检（8 项）：扫描真实 mods、id 唯一、校验、写盘/重读、.bak、重复与缺 id 检出 |
| `resource-browser-test <data.win>` | 资源浏览器只读自检：12 类资源逐类计数 + 抽样名称/概要/详情非空 + 源文件 SHA256 不变（ch1 537 项 PASS、ch2 645、ch5 714） |
| `render-stats <data.win> <room> [scale] [out.json]` | 渲染覆盖统计（逐层绘制数/unsupported/黑像素占比）；ch5 room_town_north 黑像素 1.06%、ch4 room_dw_castle_town 48.96%（残黑是 TILES 层真实美术，覆盖 70.00%、unsupported 0） |

## 测试与证据

- `document-test`：ch1..ch5 每章 **194 PASS / 0 FAIL**（A2 修复后由 lead 独立复跑；日志 `tmp/lead-verify-doctest`）。
- `resource-browser-test`：ch1 **537** / ch2 **645** / ch5 **714** 项全 PASS，测试前后源 data.win SHA256 与大小不变。
- `battle-test`：**84 PASS / 0 FAIL**（含剧情 `event`/`script` 步骤、文本改写往返、对话绑定房间/触发器；最近一次跑在 `tmp/round3/work`）。
- `mapedit-test`：PASS（原版基线可直接编辑：sidecar 落到 `Neutraled/editor/baseline_<leaf>.ntlbattle.json`、data.win SHA 不变；房间流程排序 147/147、支线紧随前置主线；日志 `tmp/round3/work`）。
- `manifest-test`：8 项 PASS（真实 mods 目录扫到 38 个 mod、校验 0 错误、重读 38/38、重复 id 检出 2 条；日志 `tmp/manifest-test.log`）。
- `vanilla-scan-test`：PASS（ch1 副本 `tmp/round2/vst-final4-ch1.json`；ch2 live `tmp/round2/vst-final4-ch2.json`）：ch1 obj_readable_room1 对白 **428 行 = 可阅读 405 + 运行期 23**（msc 80、实例坐标 196,99 下可达 11、未知 4）、ch1 obj_npc_room 510 行（msc 98）、ch2 obj_readable_room1 **722 行 = 689 + 33**（msc 88）、ch2 obj_npc_room **824 行**（msc 164、含参数 3）；控制码残字 0；`room == 2` 判定 True、`global.filechoice` Unknown、`scr_text` 文本含调用点条件、else 行 truth=False、非交互 `Other_*` 标注、章节推断 1/2、继承事件 47/353 对象（ch2 有继承交互事件 `obj_npc_butler ← obj_npc_room`）。
  行数比旧基线多（402→428、505→510、819→824）的原因已逐条核实：msc 多候选展开与 msgnextsubloc/msgsetsubloc 带参数行，均为真实对白；独立审计在 `tmp/round2/mer-display-audit.md`。
- `settings-test`：25/25 PASS（默认章节归一化/缺省 auto/保存回读/坏文件加载 auto+备份 .bad-* /解析优先级/GuessChapter/IsBaselinePath）。
- Round3 GUI e2e（`tmp/round3/scripts/r3-e2e.ps1`，证据 `tmp/round3/evidence/`，结果 `tmp/round3/results/results-r3-e2e.txt`）：**21/21 PASS** —— 沙箱基线打开 + 只读徽标、房间流程排序（首项 `room_krisroom` / 分组标题在第 133 项 / 关掉开关回到 data 顺序 147 项）、原版对话文本改写（打开→改写→保存→sidecar key 断言）、保存即生成 mod、焦点在筛选框时 Ctrl+S 仍保存、剧情步骤插入原版代码（ID 预填）、画布右键菜单、`data.win` SHA256 前后不变（`82C2BB61B8D78CD2`）。
- Round2 UIA（`tmp/round2/`，脚本 `tmp/round2/scripts/`，证据 `tmp/round2/evidence/`）：**31/31 PASS** —— scenario1 inspector 10/10（原版交互段 + 三按钮同排 + 对白预览）、scenario2 对话框 12/12（三个按钮各开一次；曾因 SplitContainer 初始化顺序崩溃，已修）、scenario3 默认章节菜单 5/5、scenario4 无参数启动 4/4；继承版复测见 `tmp/round2/results/REPORT-round2.md`。解析器大修（花括号帧/msc 条件合并/else 三态/NaN 坐标）后的最终 dll 复测见 `tmp/round2/results/r2-display-verify.txt` 与 REPORT 追加节。
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
- **生成 mod 是快照**：编辑只写 sidecar（游戏文件 `<data.win>.ntlbattle.json` / 原版基线 `Neutraled/editor/baseline_<leaf>.ntlbattle.json`）；保存时会按「保存时自动生成 Mod」自动导出（默认开），导出的仍是快照，必须让 Neutraled 重新部署才进游戏。
- **战斗敌人最多 3 个**（原版 3 个怪物槽）；**ACT 只能设名字（最多 6 个）**，行为仍是原版默认，不能给单个 ACT 配自定义效果；**初始紧张度**在战斗首帧写入 `global.tension`（按 `scr_gamestart` 的 250 上限 clamp）。
- **effects 的 `message`** 同时写 `dr-api.log` 与屏幕文本（约 150 帧，`ntlb_show_text`）；`healPercent` 只作用于 3 名队员；`gotoRoom` 是 `room_goto`（无过渡特效）；`startBattle` 在当前房间直接再开一场；**队伍全灭会判失败并执行 defeat effects**（随后仍走原版 game over）。
- **overworld 弹幕是自建子弹**：只有 rain/ring/aim/wall 4 种简易模式，直线移动 + `point_distance<14` 判命中，没有墙体碰撞/寻路，也不接管原版 overworld 敌人。
- **对话**：`Speaker`（说话人）以「名字：」前缀拼进正文第一行（原版对白框没有名字栏，不会画在单独的名字区）；文本里 `%` 会被当翻页符删掉；只有文本行 + 结束 effects，没有立绘/表情/选项。
- **剧情共 12 种步骤**（wait/move/face/action/dialogue/flag/message/sound/shake/waitinput/event/script）：`event`/`script` 可直接执行原版对象事件或脚本（工具条「插入原版代码…」点选插入），但原版事件的参数、返回值与副作用不在编辑器可见范围内；仍没有相机/音乐控制（`sound` 只播单个音效）、表情、选项、条件分支、并行分支；`move` 是两点线性插值、会穿墙；`action` 改了精灵不会自动恢复；`message` 日志 + 屏幕文本；`shake` 依赖原版 `obj_shake`；剧情期间只设 `global.interact=1` 拦交互，**不锁玩家输入**。
- **触发判定是「房间 + flag + 半径 + 按键」+ 原版交互矩形近似**：`interact` 模式现在会按玩家 `global.facing` 用原版 `obj_mainchara` 的四向交互矩形判定（`ntlb_in_interact_zone`），但**仍不校验玩家真的和绑定对象交互**；附近有原版可交互物时可能同时触发。`area` 模式在 `global.interact != 0`（菜单/战斗/其它对话/剧情）时整个跳过。
- **战斗触发**：房间名匹配 + flag + 距离（radius=0 表示进房间即触发）；`auto` 条件满足就自动开战，`interact` 还需按 Z/Enter 且在交互矩形内。
- **「只触发一次」有两层**：`once` 默认只在进程内有效（`global.ntlb_once` ds_map，重启复位）；勾选**跨重启只触发一次**（`persistOnce`）会写进 Neutraled 的 `mods-shared.json`，重启游戏后仍然记得（依赖共享数据机制，清理共享数据会复位）。战斗/对话/剧情的一次性键共用 `ntlb.<id>` 前缀。
- **mods 清单编辑器写的是源侧清单**（类似 Everest.yaml 的元信息/校验文档）：Neutraled builder 部署时**不读它**，排序/启停读各 mod 自己的 `mod.json`（`LoadAfter`/`LoadBefore`）；产物里的 `Neutraled/mods.json` 是 builder 生成的运行时清单，不要手改。
- **Mod 设置项只有 bool/choice/int 3 类**、6 种 feature 绑定（none/battlesEnabled/overworldEnabled/dialoguesEnabled/cutscenesEnabled/barrageDamagePercent）；同一 feature 多项绑定只取第一个（编辑器给警告）；值持久化 key 含 mod 前缀，改 mod id 会丢已保存值。
- **地图编辑是直接改 data.win**：没有工程文件，撤销栈只在内存（关掉就没了）；保存整份重写 data.win（本次打开第一次保存会留 `.bak`）；一次只开一个 data.win / 一个房间；撤销快照是整房间，超大房间编辑成本高。
- **对齐/分布的包围盒**按「当前图像帧 × 缩放」估算；**有旋转时按绕中心的旋转外接矩形近似**（原版旋转绕 sprite origin，origin 不一定是中心，所以是近似值）。
- 属性面板只覆盖编辑器认识的字段（实例：位置/缩放/旋转/帧/颜色；图块：位置/尺寸/源/缩放/颜色；背景：可见/平铺/拉伸/颜色/精灵）；对象的其它属性（父对象、mask、物理、事件等）只读。
- **原版交互/对白显示是只读反编译推断**：事件继承只沿对象 ParentId 父链（不模拟代码级 event_inherited）；条件求值只覆盖常见子集（数字/字符串/room/x/y/比较/逻辑/四则/三目），其它一律 ?；with 体内条件按 ?；global.msc 只有 scr_text 里能找到对应 case 时才展开（找不到保留「(msc N)」占位）；对象级查看器没有实例坐标时 x/y 条件为 ?。
- Path/Effect 层只统计数量、不渲染也不编辑（Path 点集 UTMT 不保存）；InstanceCreationOrderIDs 只显示条数，没有编辑入口；Viewport 预览未实现；对象列表第二行只显示精灵/深度/不可见/solid/persistent，不含父对象名（状态栏里有）；房间 speed/views 等原版字段在画布渲染时未参与。
- vanilla-scan 的 JSON 字段随版本可能增减，跨版本对账时先看自检输出与 README。

### C. 运行 / 部署环境限制

- **战斗/对话/剧情运行时只在章节进程有效**（root 章节选择器里没有 `obj_mainchara`/`global.flag` 等）；手动开战/开演前必须已有玩家实例（读档进房间之后），且 `global.interact == 0`（菜单关掉）。
- 触发检测跑在 `ntl_hook("on_frame")` 上，依赖 Neutraled 的 API 与 `global.flag`；单独跑原版 data.win 时这些机制都不存在。
- **Mod 设置项面板依赖 Neutraled 的设置菜单补丁**（部署自检 (g) 项验证 7 个标记）；单独跑原版 data.win 时设置面板不存在（控制台 `setting` 仍可用）。
- 设置项持久化用 `ntl_mod_data_*`（mods-shared.json），依赖 Neutraled 的共享数据机制。
- 编辑器是 Windows WinForms / .NET 9 + UTMT DLL；必须打开真实 data.win，不能新建空白工程。

## 代码结构（src/ntl-mapedit）

| 文件 | 职责 |
|---|---|
| `EditorForm.cs` | 主窗口：工具栏/菜单/左右面板/属性面板/快捷键/文件对话框/对齐分布/战斗·对话·剧情·设置接线、原版交互段与默认启动章节菜单 |
| `VanillaCode.cs` | 原版 GML 只读扫描：懒反编译 + 事件/对白/条件三态求值 + lang 文本解析 |
| `VanillaCodeDialog.cs` | 原版事件/对话查看器（事件+对白列表 + 反编译代码 + 复制/全量扫描） |
| `StartupSettings.cs` | 默认启动章节/启动目标解析（settings.json、最近文件、章节 live/baseline 路径、GuessChapter/IsBaselinePath） |
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